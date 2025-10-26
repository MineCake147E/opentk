using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace GeneratorBase.Overloading
{
    public class StringReturnOverloader : IOverloader
    {
        public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
        {
            // See: https://github.com/KhronosGroup/OpenGL-Registry/issues/363
            // These are the only two functions that return strings 2020-12-29
            if (overload.NativeFunction.EntryPoint == "glGetString" ||
                overload.NativeFunction.EntryPoint == "glGetStringi")
            {
                var newReturnName = $"{overload.NameTable.ReturnName}_str";
                var encodingParameter = new Parameter()
                {
                    Name = $"resultEncoding",
                    OriginalName = "",
                    Type = "NativeCharacterEncoding",
                    Length = default,
                    Kinds = [],
                    StrongType = new CSEnum("NativeCharacterEncoding", CSPrimitive.Byte(true), true),
                    Optional = true,
                    DefaultValue = "NativeCharacterEncoding.Utf8",
                    Attributes = ["ConstantExpected"]
                };
                var layer = new StringReturnLayer(new CSPointer(CSPrimitive.Byte(true), true), newReturnName, overload.NameTable.ReturnName!, encodingParameter);
                var returnType = new CSString(Nullable: true);
                var nameTable = overload.NameTable.New();
                nameTable.ReturnName = newReturnName;
                newOverloads =
                [
                    overload with
                    {
                        InputParameters = [..overload.InputParameters, encodingParameter],
                        NestedOverload = overload,
                        MarshalLayerToNested = layer,
                        ReturnType = returnType,
                        NameTable = nameTable,
                    }
                ];
                return true;
            }
            else if (overload.ReturnType is CSPointer pt && pt.BaseType is ICSCharType bt)
            {
                // FIXME: Handle CSChar8 and CSChar16 differently!
                var newReturnName = $"{overload.NameTable.ReturnName}_str";
                var returnType = new CSString(Nullable: true);
                var spanReturnType = new CSSpan(CSPrimitive.Byte(false), true);
                var nameTable = overload.NameTable.New();
                nameTable.ReturnName = newReturnName;
                var encodingParameter = new Parameter()
                {
                    Name = $"resultEncoding",
                    OriginalName = "",
                    Type = "NativeCharacterEncoding",
                    Length = default,
                    Kinds = [],
                    StrongType = new CSEnum("NativeCharacterEncoding", CSPrimitive.Byte(true), true),
                    Optional = true,
                    DefaultValue = "NativeCharacterEncoding.Utf8",
                    Attributes = ["ConstantExpected"]
                };
                var stringLayer = new StringReturnLayer(pt, newReturnName, overload.NameTable.ReturnName!, encodingParameter);
                var spanLayer = new ReadOnlySpanReturnLayer(pt, newReturnName, overload.NameTable.ReturnName!);
                newOverloads =
                [
                    overload with
                    {
                        InputParameters = [..overload.InputParameters, encodingParameter],
                        NestedOverload = overload,
                        MarshalLayerToNested = stringLayer,
                        ReturnType = returnType,
                        NameTable = nameTable,
                    },
                    overload with
                    {
                        OverloadName = $"{overload.OverloadName}AsSpan",
                        NestedOverload = overload,
                        MarshalLayerToNested = spanLayer,
                        ReturnType = spanReturnType,
                        NameTable = nameTable,
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

        private record StringReturnLayer(CSPointer PointerType, string NewReturnName, string NestedReturnName, Parameter EncodingParameter) : IOverloadLayer
        {
            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                // This is weird, here we need to create the variable that the internal layers
                // are going to consider as the final return variable. So we need access to the
                // return name of the nested layer.
                writer.WriteLine($"{PointerType.ToCSString()} {NestedReturnName};");
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                writer.WriteLine($"{NewReturnName} = NativeString.PtrToString({returnName}, {nameTable[EncodingParameter]});");
                return NewReturnName;
            }
        }

        private record ReadOnlySpanReturnLayer(CSPointer PointerType, string NewReturnName, string NestedReturnName) : IOverloadLayer
        {
            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                // This is weird, here we need to create the variable that the internal layers
                // are going to consider as the final return variable. So we need access to the
                // return name of the nested layer.
                writer.WriteLine($"{PointerType.ToCSString()} {NestedReturnName};");
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                writer.WriteLine($"{NewReturnName} = MemoryMarshal.CreateReadOnlySpanFromNullTerminated({returnName});");
                return NewReturnName;
            }
        }
    }
}
