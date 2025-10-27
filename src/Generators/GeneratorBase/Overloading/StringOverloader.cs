using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using GeneratorBase.Utility;
using GeneratorBase.Utility.Extensions;

namespace GeneratorBase.Overloading
{
    public class StringOverloader : IOverloader
    {
        public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
        {
            List<Parameter> newParams = [.. overload.InputParameters];
            List<Parameter> newSpanParams = [.. overload.InputParameters];
            Overload newOverload = overload;
            var spanOverload = overload;
            int j = newParams.Count - 1;
            for (int i = newParams.Count - 1; i >= 0; i--, j--)
            {
                var param = newParams[i];

                // There are a few functions that are supposed to take string arguments but are defined as
                // GLubyte* or unsigned byte*. The ones marked with kind="String" we overload so that they get the correct signature.
                // - Noggin_bops 2024-09-23
                if (param.Kinds.Contains("String") && param.StrongType is CSPointer spt && spt.BaseType is CSPrimitive sbt && sbt.TypeName == "byte")
                {
                    var pointerParam = newParams[i];
                    var nameTable = newOverload.NameTable.New();
                    nameTable.Rename(pointerParam, $"{pointerParam.Name}_ptr");

                    StringType stringType = StringType.Char8;

                    // FIXME: Can we know if the string is nullable or not?
                    newParams[i] = newParams[i] with { StrongType = new CSString(Nullable: false), StrongLength = null };
                    var stringParams = newParams.ToArray();
                    var stringLayer = new StringLayer(pointerParam, newParams[i], stringType);

                    newOverload = newOverload with
                    {
                        NestedOverload = newOverload,
                        MarshalLayerToNested = stringLayer,
                        InputParameters = stringParams,
                        NameTable = nameTable
                    };
                }
                else if (param.StrongType is CSPointer pt && pt.BaseType is ICSCharType bt)
                {
                    var pointerParam = newParams[i];
                    var nameTable = newOverload.NameTable.New();
                    nameTable.Rename(pointerParam, $"{pointerParam.Name}_ptr");

                    if (bt.Constant)
                    {
                        StringType stringType = bt switch
                        {
                            CSChar8 => StringType.Char8,
                            CSChar16 => StringType.Char16,
                            _ => throw new Exception("Unknown string type!"),
                        };

                        // FIXME: Can we know if the string is nullable or not?
                        newParams[i] = newParams[i] with { StrongType = new CSString(Nullable: false), StrongLength = null };
                        var stringParams = newParams.ToArray();
                        var stringLayer = new StringLayer(pointerParam, newParams[i], stringType);

                        newOverload = newOverload with
                        {
                            NestedOverload = newOverload,
                            MarshalLayerToNested = stringLayer,
                            InputParameters = stringParams,
                            NameTable = nameTable
                        };

                        if (stringType == StringType.Char8)
                        {
                            var spanNameTable = spanOverload.NameTable.New();
                            spanNameTable.Rename(pointerParam, $"{pointerParam.Name}_ptr");
                            var name = pointerParam.Name;
                            var lengthParamIndex = string.IsNullOrEmpty(pointerParam.Length) ? -1 : newSpanParams.FindIndex(a => a.OriginalName == pointerParam.Length);
                            if (lengthParamIndex >= 0 && overload.InputParameters.Count(a => a.Length == pointerParam.Length) == 1)
                            {
                                Logger.Warning($"Pointer with length leaked from earlier overloaders: \"{overload.NativeFunction.EntryPoint}\" ({param})");
                                continue;
                            }
                            else
                            {
                                newSpanParams[j] = newSpanParams[j] with { Name = $"nullTerminatedUtf8{char.ToUpperInvariant(name[0])}{name.Substring(1)}", StrongType = new CSSpan(CSPrimitive.Byte(false), true), StrongLength = null };
                                var spanLayer = new Utf8StringLayer(pointerParam, newSpanParams[j]);
                                spanOverload = spanOverload with
                                {
                                    NestedOverload = spanOverload,
                                    MarshalLayerToNested = spanLayer,
                                    InputParameters = [.. newSpanParams],
                                    NameTable = spanNameTable
                                };
                            }
                        }
                    }
                    else
                    {
                        int stringParamIndex = i;
                        Parameter? lenParam = null;
                        if (param.StrongLength != null)
                        {
                            string? paramName = Expression.InvertExpressionAndGetReferencedName(param.StrongLength, out var expr);
                            if (paramName == null)
                            {
                                Logger.Info($"{overload.NativeFunction.EntryPoint} has a COMPSIZE string length for parameter '{param.Name}'!");
                                continue;
                            }

                            int index = newParams.FindIndex(p => p.Name == paramName);
                            lenParam = newParams[index];
                        }

                        if (lenParam == null)
                        {
                            Logger.Info($"{overload.NativeFunction.EntryPoint} is missing a len attribute for parameter '{param.Name}'");
                            continue;
                        }

                        // FIXME: Can we know if the string is nullable or not?
                        var stringParam = newParams[stringParamIndex] with
                        {
                            StrongType = new CSRef(CSRef.Type.Out, new CSString(Nullable: false)),
                            StrongLength = null
                        };
                        newParams[stringParamIndex] = stringParam;

                        // As wgl doesn't output any 16-bit strings we don't handle this case for now
                        // - 2023-03-23 NogginBops
                        if (bt is CSChar16)
                        {
                            throw new Exception("We don't support out 16-bit strings atm.");
                        }

                        var stringParams = newParams.ToArray();
                        var stringLayer = new OutStringLayer(pointerParam, lenParam, stringParam);

                        newOverload = newOverload with
                        {
                            NestedOverload = newOverload,
                            MarshalLayerToNested = stringLayer,
                            InputParameters = stringParams,
                            NameTable = nameTable
                        };
                    }
                }
            }
            newOverloads = default;
            if (newOverload != overload)
            {
                newOverloads =
                [
                    newOverload,
                ];
            }
            if (spanOverload != overload)
            {
                newOverloads ??= [];
                newOverloads.Add(spanOverload);
            }
            return (newOverloads?.Count ?? 0) > 0;
        }

        internal enum StringType
        {
            Char8,
            Char16,
        }

        private record StringLayer(Parameter PointerParameter, Parameter StringParameter, StringType Type) : IOverloadLayer
        {

            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                switch (Type)
                {
                    case StringType.Char8:
                        writer.WriteLine($"byte* {nameTable[PointerParameter]} = (byte*)Marshal.StringToCoTaskMemUTF8({nameTable[StringParameter]});");
                        break;
                    case StringType.Char16:
                        writer.WriteLine($"char* {nameTable[PointerParameter]} = (char*)Marshal.StringToCoTaskMemAuto({nameTable[StringParameter]});");
                        break;
                    default:
                        throw new Exception($"Unknown string type '{Type}'.");
                }
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                writer.WriteLine($"Marshal.FreeCoTaskMem((IntPtr){nameTable[PointerParameter]});");
                return returnName;
            }
        }

        private record Utf8StringLayer(Parameter PointerParameter, Parameter StringParameter) : IOverloadLayer
        {
            private CsScope? _csScope;

            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                writer.WriteLine($"var {nameTable[StringParameter]}_span = NativeString.EnsureNullTerminated({nameTable[StringParameter]}, out var {nameTable[StringParameter]}_array);");
                writer.WriteLine($"fixed (byte* {nameTable[PointerParameter]} = {nameTable[StringParameter]}_span)");
                _csScope = writer.CsScope();
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                _csScope?.Dispose();
                writer.WriteLine($"if ({nameTable[StringParameter]}_array is not null) ArrayPool<byte>.Shared.Return({nameTable[StringParameter]}_array, true);");
                return returnName;
            }
        }

        private record OutStringLayer(
            Parameter PointerParameter,
            Parameter StringLengthParameter,
            Parameter StringParameter) : IOverloadLayer
        {
            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                if (StringLengthParameter.StrongType is CSPrimitive primitive)
                {
                    // If the parameter is unsigned we need to cast it for AllocCoTaskMem
                    if (primitive.TypeName == "int")
                    {
                        writer.WriteLine($"var {nameTable[PointerParameter]} = (byte*)Marshal.AllocCoTaskMem({nameTable[StringLengthParameter]});");
                    }
                    else if (primitive.TypeName == "uint")
                    {

                        writer.WriteLine($"var {nameTable[PointerParameter]} = (byte*)Marshal.AllocCoTaskMem((int){nameTable[StringLengthParameter]});");
                    }
                    else
                    {
                        throw new Exception($"Unsupported primitive type for length parameter ({primitive.ToCSString()})!");
                    }
                }
                else if (StringLengthParameter.StrongType is CSPointer pointer && pointer.BaseType is CSPrimitive basePrimitive)
                {
                    if (basePrimitive.TypeName == "int")
                    {
                        // This case is needed for ExtGetProgramBinarySourceQCOM and ExtGetProgramBinarySourceQCOM
                        // - 2022-03-22
                        writer.WriteLine($"var {nameTable[PointerParameter]} = (byte*)Marshal.AllocCoTaskMem(*{nameTable[StringLengthParameter]});");
                    }
                    else
                    {
                        throw new Exception($"Unsupported pointer type for length parameter ({pointer.ToCSString()})!");
                    }
                }
                else
                {
                    throw new Exception($"Unsupported type for length parameter ({StringLengthParameter.StrongType!.ToCSString()})");
                }
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                writer.WriteLine($"{nameTable[StringParameter]} = Marshal.PtrToStringUTF8((IntPtr){nameTable[PointerParameter]})!;");
                writer.WriteLine($"Marshal.FreeCoTaskMem((IntPtr){nameTable[PointerParameter]});");
                return returnName;
            }
        }
    }
}
