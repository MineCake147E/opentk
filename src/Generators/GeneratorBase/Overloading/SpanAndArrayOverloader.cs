using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using GeneratorBase.Utility;
using GeneratorBase.Utility.Extensions;

namespace GeneratorBase.Overloading
{
    public class SpanAndArrayOverloader : IOverloader
    {
        public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
        {
            // FIXME: We want to be able to handle more than just one Span and Array overload
            // functions like "glShaderSource" can take more than one array.
            //
            List<Parameter> newArrayParams = [.. overload.InputParameters];
            List<Parameter> newSpanParams = [.. overload.InputParameters];
            string[] genericTypes = overload.GenericTypes;
            Overload arrayOverload = overload;
            Overload spanOverload = overload;

            // FIXME: We would ideally combine all span and array overloads into a single overload layer
            // to reduce fixed() nesting in the generated code.
            // - Noggin_bops 2024-03-16
            int j = 0;
            for (int i = 0; i < overload.InputParameters.Length; i++, j++)
            {
                Parameter param = overload.InputParameters[i];

                if (param.StrongType is CSPointer pointer)
                {
                    if (pointer.BaseType is CSChar8 && string.IsNullOrEmpty(param.Length))
                    {
                        Logger.Warning($"Char pointer leaked from earlier overloaders: \"{overload.NativeFunction.EntryPoint}\" ({param})");
                        continue;
                    }
                    else if (pointer.BaseType is CSPointer)
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

                        var arrayNameTable = overload.NameTable.New();
                        arrayNameTable.Rename(param, $"{param.Name}_ptr");
                        newArrayParams[i] = newArrayParams[i] with { StrongType = new CSArray(baseType) };

                        var arrayLayer = new SpanOrArrayLayer(param, newArrayParams[i]);

                        arrayOverload = arrayOverload with
                        {
                            NestedOverload = arrayOverload,
                            MarshalLayerToNested = arrayLayer,
                            InputParameters = [.. newArrayParams],
                            NameTable = arrayNameTable,
                            GenericTypes = genericTypes
                        };

                        if (!string.IsNullOrEmpty(param.Length))
                        {
                            var lengthParamIndex = newSpanParams.FindIndex(a => a.OriginalName == param.Length);
                            if (lengthParamIndex >= 0 && overload.InputParameters.Count(a => a.Length == param.Length) == 1)
                            {
                                Logger.Warning($"Pointer with length leaked from earlier overloaders: \"{overload.NativeFunction.EntryPoint}\" ({param})");
                                continue;
                            }
                        }

                        var spanNameTable = overload.NameTable.New();
                        spanNameTable.Rename(param, $"{param.Name}_ptr");
                        newSpanParams[j] = newSpanParams[j] with { StrongType = new CSSpan(baseType, isBaseTypeConstant) };
                        var spanLayer = new SpanOrArrayLayer(param, newSpanParams[j]);

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

            if (arrayOverload == spanOverload)
            {
                newOverloads = default;
                return false;
            }
            else
            {
                newOverloads =
                [
                    overload,
                ];
                if (arrayOverload != overload)
                {
                    newOverloads.Add(arrayOverload);
                }

                if (spanOverload != overload)
                {
                    newOverloads.Add(spanOverload);
                }

                return true;
            }
        }

        private record SpanOrArrayLayer(
            Parameter PointerParameter,
            Parameter SpanOrArrayParameter) : IOverloadLayer
        {
            private CsScope _csScope;

            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                writer.WriteLine($"fixed ({PointerParameter.StrongType!.ToCSString()} {nameTable[PointerParameter]} = {nameTable[SpanOrArrayParameter]})");
                _csScope = writer.CsScope();
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                _csScope.Dispose();
                return returnName;
            }
        }
    }
}
