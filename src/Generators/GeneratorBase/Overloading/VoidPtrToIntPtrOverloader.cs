using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace GeneratorBase.Overloading
{
    public class VoidPtrToIntPtrOverloader : IOverloader
    {
        public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
        {
            Parameter[] parameters = overload.InputParameters.ToArray();
            NameTable nameTable = overload.NameTable.New();
            List<(Parameter VPtr, Parameter IPtr)>
                parameterNames = new List<(Parameter VPtr, Parameter IPtr)>();

            for (int i = 0; i < parameters.Length; i++)
            {
                Parameter parameter = parameters[i];
                if (parameter.StrongType is not CSPointer pointerType ||
                    pointerType.BaseType is not CSVoid)
                {
                    continue;
                }

                nameTable.Rename(parameter, parameter.Name + "_vptr");
                parameters[i] = parameter with { StrongType = CSPrimitive.IntPtr(false), StrongLength = null };
                parameterNames.Add((parameter, parameters[i]));
            }

            if (parameterNames.Count == 0)
            {
                newOverloads = null;
                return false;
            }

            IOverloadLayer layer = new VoidPtrToIntPtrOverloadLayer(parameterNames);
            newOverloads = new List<Overload>()
            {
                overload with
                {
                    NestedOverload = overload, InputParameters = parameters, MarshalLayerToNested = layer,
                    NameTable = nameTable
                },
                overload,
            };
            return true;
        }

        private record VoidPtrToIntPtrOverloadLayer(
            List<(Parameter VPtr, Parameter IPtr)> ParameterNames) : IOverloadLayer
        {
            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                foreach ((Parameter vPtr, Parameter iPtr) in ParameterNames)
                {
                    writer.WriteLine($"void* {nameTable[vPtr]} = (void*){nameTable[iPtr]};");
                }
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                return returnName;
            }
        }
    }
}
