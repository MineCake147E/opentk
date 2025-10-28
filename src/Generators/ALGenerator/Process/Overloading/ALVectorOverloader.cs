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

        [GeneratedRegex("(\\w+)fv(\\w*)$", RegexOptions.Compiled)]
        private static partial Regex VectorNameMatchRegex();

        public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
        {
            var match = VectorNameMatch.Match(overload.NativeFunction.Name);
            if (match is null || !match.Success)
            {
                newOverloads = null;
                return false;
            }
            var nameTable = overload.NameTable.New();
            List<Parameter> newVectorParams = [.. overload.InputParameters];
            List<Parameter> newGetVectorParams = [.. overload.InputParameters];
            List<Overload> vectorOverloads = [];
            int j = 0;
            for (int i = 0; i < overload.InputParameters.Length; i++, j++)
            {
                var parameter = overload.InputParameters[i];

                if (parameter.StrongType is CSPointer pointer && parameter.StrongLength is null && pointer.BaseType is CSPrimitive { TypeName: "float" })
                {
                    nameTable.Rename(parameter, $"{parameter.Name}_ptr");
                    if (overload.NativeFunction.Name.Contains("Get"))
                    {
                        newGetVectorParams.RemoveAt(i);
                        var systemType4 = new CSStruct($"System.Numerics.Vector4", true);
                        var systemVectorLayer4 = new ALVector4ResultLayer(parameter, "returnValue");
                        vectorOverloads.Add(overload with
                        {
                            NestedOverload = overload,
                            MarshalLayerToNested = systemVectorLayer4,
                            InputParameters = [.. newGetVectorParams],
                            NameTable = nameTable,
                            OverloadName = VectorNameMatch.Replace(overload.NativeFunction.Name, "${1}4fv${2}"),
                            ReturnType = systemType4
                        });
                        for (int d = 2; d < 4; d++)
                        {
                            var systemType = new CSStruct($"System.Numerics.Vector{d}", true);
                            var systemVectorLayer = new ALVectorResultLayer(parameter, systemType4.ToCSString(), systemType.ToCSString(), $"System.Numerics.Vector.AsVector{d}(", $")", "result4");
                            vectorOverloads.Add(overload with
                            {
                                NestedOverload = overload,
                                MarshalLayerToNested = systemVectorLayer,
                                InputParameters = [.. newGetVectorParams],
                                NameTable = nameTable,
                                OverloadName = VectorNameMatch.Replace(overload.NativeFunction.Name, $"${{1}}{d}fv${{2}}"),
                                ReturnType = systemType
                            });
                        }
                    }
                    else
                    {
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
            }

            newOverloads = vectorOverloads.Count > 0 ? vectorOverloads : null;
            newOverloads?.Add(overload);
            return vectorOverloads.Count > 0;
        }

        private sealed record class ALVectorLayer(Parameter PointerParameter, Parameter VectorParameter) : IOverloadLayer
        {
            public int OverloadResolutionPriority { get; init; } = 0;

            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                writer.WriteLine($"var {nameTable[PointerParameter]} = (float*)&{nameTable[VectorParameter]};");
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                return returnName;
            }
        }

        private sealed record class ALVector4ResultLayer(Parameter PointerParameter, string VectorName) : IOverloadLayer
        {
            public int OverloadResolutionPriority { get; init; } = 0;

            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                writer.WriteLine($"{VectorName} = default;");
                writer.WriteLine($"var {nameTable[PointerParameter]} = (float*)&{VectorName};");
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                return VectorName;
            }
        }

        private sealed record class ALVectorResultLayer(Parameter PointerParameter, string VectorType, string ReturnVectorType, string ConversionPrologue, string ConversionEpilogue, string VectorName) : IOverloadLayer
        {
            public int OverloadResolutionPriority { get; init; } = 0;

            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                writer.WriteLine($"{VectorType} {VectorName} = default;");
                writer.WriteLine($"var {nameTable[PointerParameter]} = (float*)&{VectorName};");
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                writer.WriteLine($"returnValue = {ConversionPrologue}{VectorName}{ConversionEpilogue};");
                return "returnValue";
            }
        }
    }
}
