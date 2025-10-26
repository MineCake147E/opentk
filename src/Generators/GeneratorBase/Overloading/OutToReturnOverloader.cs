using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using System.Security.AccessControl;
using System.Threading;
using System.Xml;

using GeneratorBase;

namespace GeneratorBase.Overloading
{

    public class OutToReturnOverloader : IOverloader
    {
        public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
        {
            var oldParameters = overload.InputParameters;
            if (overload.ReturnType is not CSVoid || oldParameters.Length == 0)
            {
                newOverloads = null;
                return false;
            }

            if (oldParameters[^1].StrongType is CSRef pRef && pRef.RefType == CSRef.Type.Out)
            {
                Parameter[] newParameters = new Parameter[oldParameters.Length - 1];
                Array.Copy(oldParameters, newParameters, newParameters.Length);
                Parameter? outParameter = oldParameters[^1];
                CSRef? outType = pRef;

                NameTable nameTable = overload.NameTable.New();
                nameTable.ReturnName = outParameter.Name;

                // This is now a local variable.
                nameTable.MarkFixed(outParameter);

                newOverloads = new List<Overload>()
                {
                    overload with
                    {
                        NestedOverload = overload,
                        InputParameters = newParameters,
                        ReturnType = outType!.ReferencedType,
                        MarshalLayerToNested = new OutToReturnOverloadLayer(outParameter, outType),
                        NameTable = nameTable,
                    },
                    overload,
                };
                return true;
            }
            else
            {
                newOverloads = null;
                return false;
            }
        }

        private record OutToReturnOverloadLayer(Parameter OutParameter, CSRef OutType) : IOverloadLayer
        {
            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                //writer.WriteLine($"{OutType.ReferencedType.ToCSString()} {nameTable[OutParameter]};");
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                return OutParameter.Name;
            }
        }
    }
}
