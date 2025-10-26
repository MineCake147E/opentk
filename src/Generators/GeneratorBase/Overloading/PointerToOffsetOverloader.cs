using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using GeneratorBase.Utility;

namespace GeneratorBase.Overloading
{
    public sealed class PointerToOffsetOverloader : IOverloader
    {
        private readonly Dictionary<string, string> _methodsAndParametersToOverload = new Dictionary<string, string>
        {
            // Draw elements permutations.
            {"glDrawElements", "indices"},
            {"glDrawElementsBaseVertex", "indices"},
            {"glDrawElementsBaseVertexEXT", "indices"},
            {"glDrawElementsBaseVertexOES", "indices"},
            {"glDrawElementsInstanced", "indices"},
            {"glDrawElementsInstancedANGLE", "indices"},
            {"glDrawElementsInstancedARB", "indices"},
            {"glDrawElementsInstancedBaseInstance", "indices"},
            {"glDrawElementsInstancedBaseInstanceEXT", "indices"},
            {"glDrawElementsInstancedBaseVertex", "indices"},
            {"glDrawElementsInstancedBaseVertexBaseInstance", "indices"},
            {"glDrawElementsInstancedBaseVertexBaseInstanceEXT", "indices"},
            {"glDrawElementsInstancedBaseVertexEXT", "indices"},
            {"glDrawElementsInstancedBaseVertexOES", "indices"},
            {"glDrawElementsInstancedEXT", "indices"},
            {"glDrawElementsInstancedNV", "indices"},
            {"glDrawRangeElements", "indices"},
            {"glDrawRangeElementsBaseVertex", "indices"},
            {"glDrawRangeElementsBaseVertexEXT", "indices"},
            {"glDrawRangeElementsBaseVertexOES", "indices"},
            {"glDrawRangeElementsEXT", "indices"},
            // FIXME: These methods contain an array of offsets, which we cannot currently handle.
            // {"glMultiDrawElements", "indices"},
            // {"glMultiDrawElementsBaseVertex", "indices"},
            // {"glMultiDrawElementsBaseVertexEXT", "indices"},
            // {"glMultiDrawElementsEXT", "indices"},
            // {"glMultiModeDrawElementsIBM", "indices"},

            // Vertex attribute pointer permutations.
            {"glVertexAttribPointer", "pointer"},
            {"glVertexAttribIPointer", "pointer"},
            {"glVertexAttribLPointer", "pointer"},
        };

        public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
        {
            if (!_methodsAndParametersToOverload.TryGetValue(overload.NativeFunction.EntryPoint, out var parameterName))
            {
                newOverloads = null;
                return false;
            }

            // Get the parameter index.
            int parameterIndex = Array.FindIndex(overload.InputParameters, p => p.Name == parameterName);
            if (parameterIndex == -1)
            {
                Logger.Warning($"{overload.NativeFunction.Name} does not have a parameter with the name {parameterName}");
                newOverloads = null;
                return false;
            }

            NameTable nameTable = overload.NameTable.New();
            Parameter pointerParameter = overload.InputParameters[parameterIndex];
            Parameter offsetParameter = pointerParameter with
            {
                StrongType = CSPrimitive.Nint(false),
                Name = "offset",
                StrongLength = null
            };
            Parameter[] newParameters = overload.InputParameters.ToArray();
            newParameters[parameterIndex] = offsetParameter;
            nameTable.Rename(pointerParameter, pointerParameter.Name);
            newOverloads = new List<Overload>()
            {
                overload with
                {
                    NestedOverload = overload,
                    MarshalLayerToNested = new PointerToOffsetLayer(pointerParameter, offsetParameter),
                    InputParameters = newParameters,
                    NameTable = nameTable
                }
            };
            return true;
        }

        internal record PointerToOffsetLayer(Parameter PointerParameter, Parameter OffsetParameter) : IOverloadLayer
        {
            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                writer.WriteLine(
                    $"{PointerParameter.StrongType!.ToCSString()} {nameTable[PointerParameter]} = ({PointerParameter.StrongType!.ToCSString()}){nameTable[OffsetParameter]};");
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                return returnName;
            }
        }
    }
}
