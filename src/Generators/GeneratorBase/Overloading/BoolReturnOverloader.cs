using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace GeneratorBase.Overloading
{
    public class BoolReturnOverloader : IOverloader
    {
        public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
        {
            if (overload.ReturnType is CSBool32 boolType)
            {
                var newReturnName = $"{overload.NameTable.ReturnName}_bool";
                var layer = new BoolReturnLayer(boolType, newReturnName, overload.NameTable.ReturnName!);
                var returnType = new CSBool8(boolType.Constant);
                var nameTable = overload.NameTable.New();
                nameTable.ReturnName = newReturnName;
                newOverloads = new List<Overload>()
                {
                    overload with
                    {
                        NestedOverload = overload,
                        MarshalLayerToNested = layer,
                        ReturnType = returnType,
                        NameTable = nameTable,
                    }
                };
                return true;
            }
            else
            {
                newOverloads = default;
                return false;
            }
        }

        private record BoolReturnLayer(BaseCSType Type, string NewReturnName, string NestedReturnName) : IOverloadLayer
        {
            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                // This is weird, here we need to create the variable that the internal layers
                // are going to consider as the final return variable. So we need access to the
                // return name of the nested layer.
                writer.WriteLine($"{Type.ToCSString()} {NestedReturnName};");
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                writer.WriteLine($"{NewReturnName} = {returnName} != 0;");
                return NewReturnName;
            }
        }
    }
}
