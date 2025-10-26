using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Text.RegularExpressions;

using GeneratorBase.Utility;
using GeneratorBase.Utility.Extensions;

namespace GeneratorBase.Overloading
{
    public sealed class MathTypeOverloader : IOverloader
    {
        // Regex to match names of vector methods.
        private static readonly Regex VectorNameMatch = new Regex("(?<!6)([1-4])([fdhi])v$", RegexOptions.Compiled);

        private static readonly HashSet<string> _mathKinds = new HashSet<string>()
        {
            "Vector1",
            "Vector2",
            "Vector3",
            "Vector4",

            "Matrix2x2",
            "Matrix2x3",
            "Matrix2x4",
            "Matrix3x2",
            "Matrix3x3",
            "Matrix3x4",
            "Matrix4x2",
            "Matrix4x3",
            "Matrix4x4",
        };

        private static readonly Dictionary<string, int> _kindSize = new Dictionary<string, int>()
        {
            { "Vector1", 1 },
            { "Vector2", 2 },
            { "Vector3", 3 },
            { "Vector4", 4 },
            { "Matrix2x2", 4 },
            { "Matrix2x3", 6 },
            { "Matrix2x4", 8 },
            { "Matrix3x2", 6 },
            { "Matrix3x3", 9 },
            { "Matrix3x4", 12 },
            { "Matrix4x2", 8 },
            { "Matrix4x3", 12 },
            { "Matrix4x4", 16 },
        };

        private static readonly Dictionary<string, string> _toSystemNumerics = new Dictionary<string, string>()
        {
            { "Vector2", "System.Numerics.Vector2" },
            { "Vector3", "System.Numerics.Vector3" },
            { "Vector4", "System.Numerics.Vector4" },
            { "Matrix3x2", "System.Numerics.Matrix3x2" },
            { "Matrix4", "System.Numerics.Matrix4x4" },
        };

        public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
        {
            NameTable nameTable = overload.NameTable.New();

            bool isOverloaded = false;
            bool generateSpanAndArrayOverload = false;

            Parameter[] refParametersArr = overload.InputParameters.ToArray();
            Parameter[] spanParametersArr = overload.InputParameters.ToArray();
            Parameter[] arrayParametersArr = overload.InputParameters.ToArray();

            List<Parameter> refParameters = new List<Parameter>();
            List<Parameter> spanParameters = new List<Parameter>();
            List<Parameter> arrayParameters = new List<Parameter>();

            List<Parameter> pointerParameters = new List<Parameter>();
            for (int i = 0; i < overload.InputParameters.Length; i++)
            {
                Parameter parameter = overload.InputParameters[i];

                if (parameter.StrongType is CSPointer pointer && pointer.BaseType is CSPrimitive baseType)
                {
                    // FIXME: Maybe we don't overload the uint vectors??
                    string? typePostfix = pointer.BaseType switch
                    {
                        CSPrimitive { TypeName: "int" } => "i",
                        CSPrimitive { TypeName: "uint" } => "i",
                        CSPrimitive { TypeName: "half" } => "h",
                        CSPrimitive { TypeName: "float" } => "",
                        CSPrimitive { TypeName: "double" } => "d",
                        _ => null,
                    };

                    string? mathKind = parameter.Kinds.GetMatching(_mathKinds);
                    if (mathKind != null)
                    {
                        if (parameter.StrongLength is ConstantExpression constant)
                        {
                            // Verify length with kind
                            Debug.Assert(_kindSize[mathKind] == constant.Value);
                        }
                        else if (parameter.StrongLength is BinaryOperationExpression binaryOperation)
                        {
                            if (binaryOperation.TryDecomposeIntoParameterRefAndConstant(out ConstantExpression? @const, out ParameterReferenceExpression? parameterReference))
                            {
                                Debug.Assert(_kindSize[mathKind] == @const.Value);

                                generateSpanAndArrayOverload = true;
                            }
                        }

                        nameTable.Rename(parameter, $"{parameter.Name}_ptr");

                        pointerParameters.Add(parameter);

                        string name = mathKind switch
                        {
                            // Vector1 is just the base type itself.
                            "Vector1" => baseType.TypeName,

                            "Matrix2x2" or
                            "Matrix3x3" or
                            "Matrix4x4" => $"{mathKind[0..^2]}{typePostfix}",

                            _ => $"{mathKind}{typePostfix}",
                        };

                        CSStruct mathType = new CSStruct(name, baseType.Constant);

                        Parameter refParamter = parameter with { StrongType = new CSRef(baseType.Constant ? CSRef.Type.RefReadonly : CSRef.Type.Ref, mathType), StrongLength = null };
                        Parameter spanParamter = parameter with { StrongType = new CSSpan(mathType, baseType.Constant), StrongLength = null };
                        Parameter arrayParamter = parameter with { StrongType = new CSArray(mathType), StrongLength = null };

                        refParameters.Add(refParamter);
                        spanParameters.Add(spanParamter);
                        arrayParameters.Add(arrayParamter);

                        refParametersArr[i] = refParamter;
                        spanParametersArr[i] = spanParamter;
                        arrayParametersArr[i] = arrayParamter;

                        isOverloaded = true;
                        continue;
                    }
                    else
                    {
                        // Not all functions that take vectors are marked with the vector kinds
                        // However there is a pattern for function which take vector parameters
                        // This allows function like glTexCoord2f to have the correct vector overload
                        // We don't need to do this for matrices as all functions taking matrices are
                        // correctly marked with the matrix kinds.
                        // - 2023-03-20 Noggin_Bops

                        Match vectorMatch = VectorNameMatch.Match(overload.OverloadName);
                        if (vectorMatch.Success)
                        {
                            int vectorSize = int.Parse(vectorMatch.Groups[1].Value);
                            typePostfix = vectorMatch.Groups[2].Value;
                            if (typePostfix == "f")
                            {
                                typePostfix = "";
                            }

                            string typeName = $"Vector{vectorSize}{typePostfix}";

                            if (vectorSize == 1)
                            {
                                typeName = baseType.TypeName;
                            }

                            nameTable.Rename(parameter, $"{parameter.Name}_ptr");

                            pointerParameters.Add(parameter);

                            CSStruct mathType = new CSStruct(typeName, baseType.Constant);

                            Parameter refParamter = parameter with { StrongType = new CSRef(baseType.Constant ? CSRef.Type.RefReadonly : CSRef.Type.Ref, mathType), StrongLength = null };
                            Parameter spanParamter = parameter with { StrongType = new CSSpan(mathType, baseType.Constant), StrongLength = null };
                            Parameter arrayParamter = parameter with { StrongType = new CSArray(mathType), StrongLength = null };

                            refParameters.Add(refParamter);
                            spanParameters.Add(spanParamter);
                            arrayParameters.Add(arrayParamter);

                            refParametersArr[i] = refParamter;
                            spanParametersArr[i] = spanParamter;
                            arrayParametersArr[i] = arrayParamter;

                            isOverloaded = true;
                            continue;
                        }
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

                Overload refOverload = overload with
                {
                    OverloadName = overloadName,
                    InputParameters = refParametersArr,
                    MarshalLayerToNested = new MathLayer(pointerParameters, refParameters),
                    NameTable = nameTable,
                    NestedOverload = overload,
                };

                Overload spanOverload = overload with
                {
                    OverloadName = overloadName,
                    InputParameters = spanParametersArr,
                    MarshalLayerToNested = new MathLayer(pointerParameters, spanParameters),
                    NameTable = nameTable,
                    NestedOverload = overload,
                };

                Overload arrayOverload = overload with
                {
                    OverloadName = overloadName,
                    InputParameters = arrayParametersArr,
                    MarshalLayerToNested = new MathLayer(pointerParameters, arrayParameters),
                    NameTable = nameTable,
                    NestedOverload = overload,
                };

                // FIXME: Create the system.math overloads
                // FIXME: Create the array and span overloads
                newOverloads = new List<Overload>()
                {
                    refOverload,
                };

                if (generateSpanAndArrayOverload)
                {
                    newOverloads.Add(spanOverload);
                    newOverloads.Add(arrayOverload);
                }

                Overload? numericsRef = GenSystemNumericsOverload(refOverload, pointerParameters);
                Overload? numericsSpan = GenSystemNumericsOverload(spanOverload, pointerParameters);
                Overload? numericsArray = GenSystemNumericsOverload(arrayOverload, pointerParameters);

                if (numericsRef != null)
                {
                    newOverloads.Add(numericsRef);
                }

                if (numericsSpan != null)
                {
                    newOverloads.Add(numericsSpan);
                }

                if (numericsArray != null)
                {
                    newOverloads.Add(numericsArray);
                }

                return true;
            }

            newOverloads = null;
            return false;

            static Overload? GenSystemNumericsOverload(Overload overload, List<Parameter> pointerParameters)
            {
                bool modified = false;

                Parameter[] parameters = overload.InputParameters.ToArray();
                List<Parameter> numericsParameters = new List<Parameter>();

                for (int i = 0; i < overload.InputParameters.Length; i++)
                {
                    Parameter parameter = overload.InputParameters[i];

                    if (parameter.StrongType is IBaseTypeCSType pointerType &&
                        pointerType.BaseType is CSStruct mathType &&
                        _toSystemNumerics.TryGetValue(mathType.TypeName, out string? numericsTypeName))
                    {
                        // Create new object with the same mathType as pointerType
                        Parameter numericsParam = parameter with { StrongType = pointerType.CreateWithNewType(new CSStruct(numericsTypeName, mathType.Constant)) };

                        parameters[i] = numericsParam;

                        numericsParameters.Add(numericsParam);

                        modified = true;
                    }
                }

                if (modified)
                {
                    return overload with
                    {
                        InputParameters = parameters,
                        MarshalLayerToNested = new MathLayer(pointerParameters, numericsParameters),
                    };
                }
                else
                {
                    return null;
                }
            }
        }

        internal record MathLayer(List<Parameter> PointerParams, List<Parameter> VectorParams) : IOverloadLayer
        {
            private CsScope _csScope;

            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                for (int i = 0; i < PointerParams.Count; i++)
                {
                    Parameter ptrParam = PointerParams[i];
                    Parameter vectorParam = VectorParams[i];
                    BaseCSType vectorType = ((IBaseTypeCSType)vectorParam.StrongType!).BaseType;
                    bool takeAddress = ((IBaseTypeCSType)vectorParam.StrongType!).TakeAddressInFixedStatement;

                    writer.WriteLine($"fixed ({vectorType.ToCSString()}* tmp_{nameTable[vectorParam]} = {(takeAddress ? "&" : "")}{nameTable[vectorParam]})");
                }
                _csScope = writer.CsScope();

                for (int i = 0; i < PointerParams.Count; i++)
                {
                    Parameter pointerParameter = PointerParams[i];
                    Parameter colorParamter = VectorParams[i];

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
