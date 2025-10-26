using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;

namespace GeneratorBase.Overloading
{
    public class StringArrayOverloader : IOverloader
    {
        public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
        {
            int stringArrayParameterIndex = -1;
            for (int i = 0; i < overload.InputParameters.Length; i++)
            {
                var param = overload.InputParameters[i];

                if (param.StrongType is CSPointer pointer && pointer.BaseType is CSPointer pointer2 && pointer2.BaseType is ICSCharType bt)
                {
                    Debug.Assert(stringArrayParameterIndex == -1, "We only expect one string array argument per function.");
                    Debug.Assert(bt is CSChar8);
                    stringArrayParameterIndex = i;
                }
            }

            if (stringArrayParameterIndex != -1)
            {
                List<Parameter> newParams = new List<Parameter>(overload.InputParameters);

                var nameTable = overload.NameTable.New();

                var arrayParam = newParams[stringArrayParameterIndex];
                nameTable.Rename(arrayParam, $"{arrayParam.Name}_ptr");
                newParams[stringArrayParameterIndex] = arrayParam with { StrongType = new CSArray(new CSString(false)) };

                StringArrayLayer.StringType stringType = ((arrayParam.StrongType as CSPointer)!.BaseType as CSPointer)!.BaseType switch
                {
                    CSChar8 => StringArrayLayer.StringType.Char8,
                    CSChar16 => StringArrayLayer.StringType.Char16,
                    _ => throw new Exception("Unknown string type!"),
                };

                // FIXME: Can we know if the string is nullable or not?
                newParams[stringArrayParameterIndex] = newParams[stringArrayParameterIndex] with { StrongType = new CSArray(new CSString(Nullable: false)) };
                var stringParams = newParams.ToArray();
                var stringArrayLayer = new StringArrayLayer(arrayParam, newParams[stringArrayParameterIndex], stringType);

                newOverloads = [overload with
                {
                    NestedOverload = overload,
                    MarshalLayerToNested = stringArrayLayer,
                    InputParameters = newParams.ToArray(),
                    NameTable = nameTable
                }];
                return true;
            }
            else
            {
                newOverloads = null;
                return false;
            }
        }

        private record StringArrayLayer(Parameter PointerParameter, Parameter StringArrayParameter, StringArrayLayer.StringType Type) : IOverloadLayer
        {
            internal enum StringType
            {
                Char8,
                Char16,
            }

            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                switch (Type)
                {
                    case StringType.Char8:
                        writer.WriteLine($"byte** {nameTable[PointerParameter]} = (byte**)MarshalTk.StringArrayToCoTaskMemUTF8({nameTable[StringArrayParameter]});");
                        break;
                    case StringType.Char16:
                        writer.WriteLine($"char** {nameTable[PointerParameter]} = (char**)MarshalTk.StringArrayToCoTaskMemUni({nameTable[StringArrayParameter]});");
                        break;
                    default:
                        throw new Exception($"Unknown string type '{Type}'.");
                }
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                writer.WriteLine($"MarshalTk.FreeStringArrayCoTaskMem((IntPtr){nameTable[PointerParameter]}, {nameTable[StringArrayParameter]}.Length);");
                return returnName;
            }
        }
    }
}
