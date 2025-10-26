using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace GeneratorBase.Overloading
{
    public class FunctionPtrToDelegateOverloader : IOverloader
    {
        public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
        {
            Parameter[] parameters = new Parameter[overload.InputParameters.Length];
            List<Parameter> original = new List<Parameter>();
            List<Parameter> changed = new List<Parameter>();
            NameTable nameTable = overload.NameTable.New();
            for (int i = 0; i < overload.InputParameters.Length; i++)
            {
                Parameter parameter = overload.InputParameters[i];
                parameters[i] = parameter;

                if (parameter.StrongType is CSOpaqueFunctionPointer fpt)
                {
                    // Rename the parameter
                    nameTable.Rename(parameter, $"{parameter.Name}_ptr");

                    original.Add(parameters[i]);

                    parameters[i] = parameters[i] with { StrongType = new CSDelegateType(fpt.TypeName) };

                    changed.Add(parameters[i]);
                }
            }

            if (changed.Count > 0)
            {
                var layer = new FunctionPtrToDelegateLayer(changed, original);
                newOverloads = new List<Overload>()
                    {
                        overload with { NestedOverload = overload, MarshalLayerToNested = layer, InputParameters = parameters, NameTable = nameTable }
                    };
                return true;
            }
            else
            {
                newOverloads = default;
                return false;
            }
        }

        private class FunctionPtrToDelegateLayer : IOverloadLayer
        {
            internal List<Parameter> DelegateParameters { get; }

            internal List<Parameter> PointerParameters { get; }

            internal FunctionPtrToDelegateLayer(List<Parameter> delegateParameters, List<Parameter> pointerParameters)
            {
                DelegateParameters = delegateParameters;
                PointerParameters = pointerParameters;
            }

            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                for (int i = 0; i < DelegateParameters.Count; i++)
                {
                    string type = PointerParameters[i].StrongType!.ToCSString();
                    writer.WriteLine($"{type} {nameTable[PointerParameters[i]]} = Marshal.GetFunctionPointerForDelegate({nameTable[DelegateParameters[i]]});");
                }
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                return returnName;
            }
        }
    }
}
