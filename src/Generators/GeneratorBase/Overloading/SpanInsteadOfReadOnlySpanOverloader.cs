using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace GeneratorBase.Overloading
{
    public class SpanInsteadOfReadOnlySpanOverloader : IOverloader
    {
        public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
        {
            List<Parameter> newSpanParams = [.. overload.InputParameters];
            string[] genericTypes = overload.GenericTypes;
            var spanOverload = overload;
            bool overloadUpdated = false;
            for (int i = 0; i < overload.InputParameters.Length; i++)
            {
                var param = overload.InputParameters[i];

                if (param.StrongType is CSSpan span && span.Readonly && span.BaseType is CSGenericType)
                {
                    newSpanParams[i] = newSpanParams[i] with { StrongType = new CSSpan(span.BaseType, false) };
                    var spanLayer = new GenericSpanToReadOnlySpanLayer(param, newSpanParams[i]);
                    spanOverload = overloadUpdated ? spanOverload : spanOverload.AsBaseOverload();
                    var spanNameTable = spanOverload.NameTable.New();
                    spanNameTable.Rename(param, $"{param.Name}_r");
                    spanOverload = spanOverload with
                    {
                        NestedOverload = spanOverload,
                        MarshalLayerToNested = spanLayer,
                        InputParameters = [.. newSpanParams],
                        NameTable = spanNameTable,
                        GenericTypes = genericTypes
                    };
                    overloadUpdated = true;
                }
            }

            newOverloads = !overloadUpdated ? default : [spanOverload, overload];
            return overloadUpdated;
        }

        private record GenericSpanToReadOnlySpanLayer(
            Parameter ReadOnlySpanParameter,
            Parameter SpanOrArrayParameter) : IOverloadLayer
        {
            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                writer.WriteLine($"{ReadOnlySpanParameter.StrongType!.ToCSString()} {nameTable[ReadOnlySpanParameter]} = {nameTable[SpanOrArrayParameter]};");
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                return returnName;
            }

            public int OverloadResolutionPriority => -1;
        }
    }
}
