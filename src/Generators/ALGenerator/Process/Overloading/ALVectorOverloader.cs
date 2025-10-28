using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Security.AccessControl;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;

using GeneratorBase;
using GeneratorBase.Overloading;
using GeneratorBase.Utility;

namespace ALGenerator.Process.Overloading
{
    internal partial class ALVectorOverloader : IOverloader
    {
        private static readonly Regex VectorNameMatch = VectorNameMatchRegex();

        [GeneratedRegex("(?<!Get)(\\w+)fv(\\w*)$", RegexOptions.Compiled)]
        private static partial Regex VectorNameMatchRegex();

        public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
        {
            if (!VectorNameMatch.IsMatch(overload.NativeFunction.EntryPoint))
            {
                newOverloads = null;
                return false;
            }
            var nameTable = overload.NameTable.New();
            List<Parameter> newVectorParams = [.. overload.InputParameters];
            List<Overload> vectorOverloads = [];
            int j = 0;
            for (int i = 0; i < overload.InputParameters.Length; i++, j++)
            {
                var parameter = overload.InputParameters[i];

                if (parameter.StrongType is CSPointer pointer && parameter.StrongLength is null && pointer.BaseType is CSPrimitive { TypeName: "float" })
                {
                    nameTable.Rename(parameter, $"{parameter.Name}_ptr");
                    for (int d = 2; d < 5; d++)
                    {
                        var mathType = new CSStruct($"Vector{d}", true);
                        var vectorParameter = newVectorParams[i] with { StrongType = mathType };
                        newVectorParams[i] = vectorParameter;
                        var vectorLayer = new ALVectorLayer(parameter, vectorParameter);
                        vectorOverloads.Add(overload with
                        {
                            NestedOverload = overload,
                            MarshalLayerToNested = vectorLayer,
                            InputParameters = [.. newVectorParams],
                            NameTable = nameTable,
                        });
                        var systemType = new CSStruct($"System.Numerics.Vector{d}", true);
                        var systemVectorParameter = newVectorParams[i] with { StrongType = systemType };
                        newVectorParams[i] = systemVectorParameter;
                        var systemVectorLayer = new ALVectorLayer(parameter, systemVectorParameter) { OverloadResolutionPriority = 1 };
                        vectorOverloads.Add(overload with
                        {
                            NestedOverload = overload,
                            MarshalLayerToNested = systemVectorLayer,
                            InputParameters = [.. newVectorParams],
                            NameTable = nameTable,
                        });
                    }
                }
            }

            newOverloads = vectorOverloads.Count > 0 ? vectorOverloads : null;
            newOverloads?.Add(overload);
            return vectorOverloads.Count > 0;
        }

        private sealed record class ALVectorLayer(Parameter PointerParameter, Parameter VectorParameter) : IOverloadLayer
        {
            public int OverloadResolutionPriority { get; init; } = 0;

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                return returnName;
            }

            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                writer.WriteLine($"var {nameTable[PointerParameter]} = (float*)&{nameTable[VectorParameter]};");
            }
        }
    }
}
