using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using GeneratorBase.Utility;
using GeneratorBase.Utility.Extensions;

namespace GeneratorBase.Overloading
{
    public class ColorTypeOverloader : IOverloader
    {
        public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
        {
            NameTable nameTable = overload.NameTable.New();

            Parameter[] parameters = overload.InputParameters.ToArray();
            List<Parameter> colorParameters = new List<Parameter>();
            List<Parameter> pointerParameters = new List<Parameter>();
            bool isOverloaded = false;
            for (int i = 0; i < parameters.Length; i++)
            {
                Parameter parameter = parameters[i];

                if (parameter.StrongType is CSPointer pointer && parameter.Kinds.Contains("Color"))
                {
                    // We only support float colors!
                    if (pointer.BaseType is not CSPrimitive primitive || primitive.TypeName != "float")
                    {
                        continue;
                    }

                    if (parameter.StrongLength == null)
                    {
                        continue;
                    }

                    if (parameter.StrongLength is ConstantExpression constant)
                    {
                        int colorSize = constant.Value;
                        if (colorSize > 4 || colorSize < 3)
                        {
                            throw new Exception($"The kind=Color parameter {parameter.Name} in {overload.NativeFunction.EntryPoint} was marked with a size that was not 3 or 4. length: {colorSize}");
                        }

                        string colorSpace = colorSize == 4 ? "Rgba" : "Rgb";

                        nameTable.Rename(parameter, $"{parameter.Name}_ptr");

                        // FIXME: ref vs ref readonly depending on Constant memeber
                        Parameter colorParameter = parameter with { StrongType = new CSRef(CSRef.Type.RefReadonly, new CSStruct($"Color{colorSize}<{colorSpace}>", pointer.Constant)), StrongLength = null };

                        pointerParameters.Add(parameter);
                        colorParameters.Add(colorParameter);
                        parameters[i] = colorParameter;

                        isOverloaded = true;
                    }
                    else
                    {
                        throw new Exception();
                    }
                }
            }

            if (isOverloaded)
            {
                // FIXME: We want to remove the v postfix, but vendor names are still in the overload names...
                // We probably want to remove the extension name from the overload name.
                string overloadName = overload.OverloadName;
                if (overloadName.EndsWith('v'))
                {
                    overloadName = NameMangler.RemoveEnd(overload.OverloadName, "v");
                }

                newOverloads = new List<Overload>()
                {
                    overload with
                    {
                        OverloadName = overloadName,
                        InputParameters = parameters,
                        MarshalLayerToNested = new ColorLayer(colorParameters, pointerParameters),
                        NameTable = nameTable,
                        NestedOverload = overload,
                    }
                };
                return true;
            }

            newOverloads = null;
            return false;
        }

        internal record ColorLayer(List<Parameter> ColorParamters, List<Parameter> PointerParameters) : IOverloadLayer
        {
            private CsScope _csScope;

            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                for (int i = 0; i < ColorParamters.Count; i++)
                {
                    Parameter colorParamter = ColorParamters[i];
                    BaseCSType colorType = ((CSRef)colorParamter.StrongType!).ReferencedType;

                    writer.WriteLine($"fixed ({colorType.ToCSString()}* tmp_{nameTable[colorParamter]} = &{nameTable[colorParamter]})");
                }
                _csScope = writer.CsScope();

                for (int i = 0; i < ColorParamters.Count; i++)
                {
                    Parameter colorParamter = ColorParamters[i];
                    Parameter pointerParameter = PointerParameters[i];

                    writer.WriteLine($"{pointerParameter.StrongType!.ToCSString()} {nameTable[pointerParameter]} = ({pointerParameter.StrongType!.ToCSString()})tmp_{nameTable[colorParamter]};");
                }
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                _csScope.Dispose();
                return returnName;
            }
        }
    }
}
