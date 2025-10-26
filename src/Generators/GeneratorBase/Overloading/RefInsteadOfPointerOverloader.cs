using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

using GeneratorBase.Utility;
using GeneratorBase.Utility.Extensions;

namespace GeneratorBase.Overloading
{
    public class RefInsteadOfPointerOverloader : IOverloader
    {
        public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
        {
            Parameter[] parameters = new Parameter[overload.InputParameters.Length];
            List<Parameter> original = new List<Parameter>();
            List<Parameter> changed = new List<Parameter>();
            NameTable nameTable = overload.NameTable.New();
            string[] genericTypes = overload.GenericTypes;
            for (int i = 0; i < overload.InputParameters.Length; i++)
            {
                Parameter parameter = overload.InputParameters[i];
                parameters[i] = parameter;

                if (parameter.StrongType is CSPointer pt)
                {
                    bool constant = pt.Constant;
                    BaseCSType baseType;
                    switch (pt.BaseType)
                    {
                        case CSVoid btVoid:
                            genericTypes = genericTypes.MakeCopyAndGrow(1);
                            genericTypes[^1] = $"T{genericTypes.Length}";
                            baseType = new CSGenericType(genericTypes[^1]);
                            constant |= btVoid.Constant;
                            break;
                        case CSPrimitive bt:
                            baseType = pt.BaseType;
                            constant |= bt.Constant;
                            break;
                        case CSEnum bt:
                            baseType = pt.BaseType;
                            constant |= bt.Constant;
                            break;
                        case CSStructPrimitive bt:
                            baseType = pt.BaseType;
                            constant = bt.Constant;
                            break;
                        case CSStruct bt:
                            baseType = pt.BaseType;
                            constant |= bt.Constant;
                            break;
                        case CSBool8 bt:
                            baseType = pt.BaseType;
                            constant |= bt.Constant;
                            break;
                        case CSBool32 bt:
                            baseType = pt.BaseType;
                            constant |= bt.Constant;
                            break;

                        case CSPointer:
                        case CSChar8:
                        case CSChar16:
                            continue;

                        default:
                            throw new InvalidOperationException($"{pt} is not supported by the ref overloader.");
                    }

                    bool outParamSuitable;
                    if (parameter.StrongLength != null)
                    {
                        if (parameter.StrongLength is ConstantExpression c && c.Value == 1)
                        {
                            // The length is 1, in/out overload is suitable.
                            outParamSuitable = true;
                        }
                        else if (parameter.StrongLength is CompSizeExpression && overload.NativeFunction.EntryPoint.StartsWith("glGet"))
                        {
                            // We assume that all glGet* functions with CompSize arguments are fine to mark as out
                            outParamSuitable = true;
                        }
                        else
                        {
                            // Non-zero length, not suitable for out overload.
                            outParamSuitable = false;
                        }
                    }
                    else
                    {
                        // If there is no length parameter we have very little information
                        // about if this parameter is suitable as an out parameter.
                        // Therefore we take a safe bet that it's not suitable.
                        // - Noggin_bops 2024-03-16
                        outParamSuitable = false;
                    }

                    CSRef.Type refType;
                    // This is a list of functions that either have "in" parameters not marked with const in gl.xml
                    // or functions that use the same pointer parameter for both input and output.
                    // - Noggin_bops 2024-03-16
                    // FIXME: Check GLX for non-const in parameters and ref parameters!
                    switch (overload.NativeFunction.EntryPoint)
                    {
                        // FIXME: glImportMemoryWin32HandleEXT should take a HANDLE object, i.e. IntPtr....
                        case "glImportMemoryWin32HandleEXT" when parameter.Name == "handle": refType = CSRef.Type.RefReadonly; break;
                        case "glSelectPerfMonitorCountersAMD" when parameter.Name == "counterList": refType = CSRef.Type.RefReadonly; break;
                        case "glSharpenTexFuncSGIS" when parameter.Name == "points": refType = CSRef.Type.RefReadonly; break;
                        case "glVertexArrayRangeAPPLE" when parameter.Name == "pointer": refType = CSRef.Type.RefReadonly; break;
                        // FIXME: Should we have glCullParameter*vEXT here? They have len="4" and never get triggered...
                        case "glCullParameterdvEXT" when parameter.Name == "params": refType = CSRef.Type.RefReadonly; break;
                        case "glCullParameterfvEXT" when parameter.Name == "params": refType = CSRef.Type.RefReadonly; break;
                        case "glDeletePerfMonitorsAMD" when parameter.Name == "monitors": refType = CSRef.Type.RefReadonly; break;
                        case "glFlushVertexArrayRangeAPPLE" when parameter.Name == "pointer": refType = CSRef.Type.RefReadonly; break;

                        case "wglDXLockObjectsNV" when parameter.Name == "hObjects": refType = CSRef.Type.RefReadonly; break;
                        case "wglDXOpenDeviceNV" when parameter.Name == "dxDevice": refType = CSRef.Type.RefReadonly; break;
                        case "wglDXRegisterObjectNV" when parameter.Name == "dxObject": refType = CSRef.Type.RefReadonly; break;
                        case "wglDXSetResourceShareHandleNV" when parameter.Name == "dxObject": refType = CSRef.Type.RefReadonly; break;
                        case "wglDXUnlockObjectsNV" when parameter.Name == "hObjects": refType = CSRef.Type.RefReadonly; break;
                        case "wglGetPixelFormatAttribfvEXT" when parameter.Name == "piAttributes": refType = CSRef.Type.RefReadonly; break;
                        case "wglGetPixelFormatAttribivEXT" when parameter.Name == "piAttributes": refType = CSRef.Type.RefReadonly; break;

                        // We do the special handling above so that we can assume that any parameter that is not marked
                        // "const" here is an out parameter.
                        // Any potential ref parameters should be handled above.
                        // - Noggin_bops 2024-03-16
                        default: refType = constant ? CSRef.Type.RefReadonly : outParamSuitable ? CSRef.Type.Out : CSRef.Type.Ref; break;
                    }

                    // Rename the parameter
                    nameTable.Rename(parameter, $"{parameter.Name}_ptr");

                    original.Add(parameters[i]);

                    parameters[i] = parameters[i] with { StrongType = new CSRef(refType, baseType) };

                    changed.Add(parameters[i]);
                }
            }

            if (changed.Count > 0)
            {
                var layer = new RefInsteadOfPointerLayer(changed, original);
                newOverloads =
                [
                    overload with {
                        NestedOverload = overload,
                        MarshalLayerToNested = layer,
                        InputParameters = parameters,
                        NameTable = nameTable,
                        GenericTypes = genericTypes
                    }
                ];
                return true;
            }
            else
            {
                newOverloads = default;
                return false;
            }
        }

        private record RefInsteadOfPointerLayer(
            List<Parameter> RefParameters,
            List<Parameter> PointerParameters) : IOverloadLayer
        {
            private CsScope _csScope;

            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                // First we take references to all already "fixed" variables.
                for (int i = 0; i < RefParameters.Count; i++)
                {
                    if (nameTable.IsFixed(RefParameters[i]))
                    {
                        string type = PointerParameters[i].StrongType!.ToCSString();
                        writer.WriteLine($"{type} {nameTable[PointerParameters[i]]} = &{nameTable[RefParameters[i]]};");
                    }
                }

                // Second we fix all of the not already fixed parameters.
                for (int i = 0; i < RefParameters.Count; i++)
                {
                    if (nameTable.IsFixed(RefParameters[i]) == false)
                    {
                        string type = PointerParameters[i].StrongType!.ToCSString();
                        writer.WriteLine($"fixed ({type} {nameTable[PointerParameters[i]]} = &{nameTable[RefParameters[i]]})");
                    }
                }

                _csScope = writer.CsScope();
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                _csScope.Dispose();
                return returnName;
            }
        }
    }
}
