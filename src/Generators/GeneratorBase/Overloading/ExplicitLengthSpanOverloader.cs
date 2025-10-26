using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using GeneratorBase.Utility;
using GeneratorBase.Utility.Extensions;

namespace GeneratorBase.Overloading
{
    public class ExplicitLengthSpanOverloader : IOverloader
    {
        public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
        {
            // FIXME: We want to be able to handle more than just one Span and Array overload
            // functions like "glShaderSource" can take more than one array.
            //

            List<Parameter> newSpanParams = [.. overload.InputParameters];
            string[] genericTypes = overload.GenericTypes;
            Overload spanOverload = overload;

            // FIXME: We would ideally combine all span and array overloads into a single overload layer
            // to reduce fixed() nesting in the generated code.
            // - Noggin_bops 2024-03-16
            int j = 0;
            for (int i = 0; i < overload.InputParameters.Length; i++, j++)
            {
                var param = overload.InputParameters[i];

                if (param.StrongType is CSPointer pointer && !string.IsNullOrEmpty(param.Length))
                {
                    if (pointer.BaseType is CSPointer)
                    {
                        // FIXME: Maybe we can generate an IntPtr[] overload when this happens?
                        // - Noggin_bops 2025-08-08
                        Logger.Warning($"Pointer leaked from earlier overloaders: \"{overload.NativeFunction.EntryPoint}\" ({param})");
                        continue;
                    }
                    else
                    {
                        // If the parameter has length 1 there is no point in having an array overload.
                        // We leave it to be ref overloaded instead.
                        // - Noggin_bops 2024-03-16
                        if (param.StrongLength is ConstantExpression constant && constant.Value == 1)
                        {
                            continue;
                        }

                        BaseCSType baseType;
                        if (pointer.BaseType is CSVoid)
                        {
                            genericTypes = genericTypes.MakeCopyAndGrow(1);
                            genericTypes[^1] = $"T{genericTypes.Length}";
                            baseType = new CSGenericType(genericTypes[^1]);
                        }
                        else
                        {
                            baseType = pointer.BaseType;
                        }

                        bool isBaseTypeConstant = false;
                        if (pointer.BaseType is IConstantCSType constantType)
                        {
                            isBaseTypeConstant = constantType.Constant;
                        }

                        if (!string.IsNullOrEmpty(param.Length))
                        {
                            var lengthParamIndex = newSpanParams.FindIndex(a => a.OriginalName == param.Length);
                            if (lengthParamIndex >= 0 && overload.InputParameters.Count(a => a.Length == param.Length) == 1)
                            {
                                var spanNameTable = overload.NameTable.New();
                                spanNameTable.Rename(param, $"{param.Name}_ptr");
                                newSpanParams[j] = newSpanParams[j] with { StrongType = new CSSpan(baseType, isBaseTypeConstant) };
                                var lengthParam = newSpanParams[lengthParamIndex];
                                var spanLayer = new ExplicitLengthSpanLayer(param, newSpanParams[j], lengthParam);
                                newSpanParams.RemoveAt(lengthParamIndex);
                                j--;
                                spanOverload = spanOverload with
                                {
                                    NestedOverload = spanOverload,
                                    MarshalLayerToNested = spanLayer,
                                    InputParameters = [.. newSpanParams],
                                    NameTable = spanNameTable,
                                    GenericTypes = genericTypes
                                };
                            }
                        }
                    }
                }
            }

            if (spanOverload == overload)
            {
                newOverloads = default;
                return false;
            }
            else
            {
                newOverloads =
                [
                    spanOverload,
                    overload,
                ];
                return true;
            }
        }

        internal record ExplicitLengthSpanLayer(
            Parameter PointerParameter,
            Parameter SpanParameter, Parameter LengthParameter) : IOverloadLayer
        {
            private CsScope _csScope;

            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                writer.WriteLine($"fixed ({PointerParameter.StrongType!.ToCSString()} {nameTable[PointerParameter]} = {nameTable[SpanParameter]})");
                _csScope = writer.CsScope();
                writer.Write($"var {nameTable[LengthParameter]} = ");
                if (LengthParameter.StrongType != CSPrimitive.Int(false))
                {
                    writer.Write($"({LengthParameter.StrongType!.ToCSString()})");
                }
                writer.WriteLine($"{nameTable[SpanParameter]}.Length;");
                if (SpanParameter.StrongType is CSSpan span && span.BaseType is CSGenericType genericType)
                {
                    writer.WriteLine($"{nameTable[LengthParameter]} = checked({nameTable[LengthParameter]} * Unsafe.SizeOf<{genericType.GenericTypeName}>());");
                }
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                _csScope.Dispose();
                return returnName;
            }
        }
    }
}
