using System;
using System.CodeDom.Compiler;
using System.Collections.Frozen;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;

using ALGenerator.Parsing;
using ALGenerator.Process;

using GeneratorBase;
using GeneratorBase.Overloading;
using GeneratorBase.Utility;
using GeneratorBase.Utility.Extensions;

namespace ALGenerator
{
    internal static class Writer
    {
        private const string BaseNamespace = "OpenTK";
        private const string AudioNamespace = BaseNamespace + ".Audio";

        private const string APIExtensionSuffix = "Extensions";
        private static IReadOnlyList<string> Usings => [
            "System",
            "System.Buffers",
            "System.Diagnostics.CodeAnalysis",
            "System.Runtime.CompilerServices",
            "System.Runtime.InteropServices",
            "OpenTK.Core.Native",
            "OpenTK.Mathematics",
            "OpenTK.Audio",
            ];
        internal record FileStrings(string FileNamePrefix, string ClassName, string Namespace, string LoaderClass, string LoaderBindingsContext, string LoadFunction)
        {
            /// <summary>Alias for <see cref="ClassName"/>.</summary>
            public string ApiName => ClassName;
        }

        private static void GetNativeFunctionSignature(Function function, bool postfixName, bool swapTypesForUnderlyingType,
            out string name, out StringBuilder paramNames, out StringBuilder delegateTypes, out StringBuilder signature, out bool castReturnType, out string returnType)
        {
            name = function.Name;
            if (postfixName) name += "_";

            paramNames = new StringBuilder();
            delegateTypes = new StringBuilder();
            signature = new StringBuilder();
            for (int i = 0; i < function.Parameters.Count; i++)
            {
                var param = function.Parameters[i];
                string type = swapTypesForUnderlyingType ? SwapUnderlyingTypeForPrimitive(param.StrongType!) : param.StrongType!.ToCSString();

                string primitiveType = SwapUnderlyingTypeForPrimitive(param.StrongType!);

                if (type != primitiveType)
                {
                    paramNames.Append($"({primitiveType})");
                }

                // HACK: FIXME: You can't cast a bool to byte, sigh..
                if (!swapTypesForUnderlyingType && param.StrongType is CSBool8)
                {
                    paramNames.Append($"({param.Name} ? 1 : 0)");
                }
                else
                {
                    paramNames.Append(param.Name);
                }

                delegateTypes.Append(type);
                signature.Append($"{type} {param.Name}");

                // If we are adding more types, append a ", "
                if (i + 1 < function.Parameters.Count)
                {
                    paramNames.Append(", ");
                    signature.Append(", ");
                }

                delegateTypes.Append(", ");
            }

            returnType = swapTypesForUnderlyingType ? SwapUnderlyingTypeForPrimitive(function.StrongReturnType!) : function.StrongReturnType!.ToCSString();
            string primitiveReturnType = SwapUnderlyingTypeForPrimitive(function.StrongReturnType!);
            if (returnType != primitiveReturnType)
            {
                castReturnType = true;
            }
            else
            {

                castReturnType = false;
            }

            delegateTypes.Append(returnType);

            static string SwapUnderlyingTypeForPrimitive(BaseCSType type)
            {
                // Peel off all pointers
                StringBuilder pointers = new StringBuilder();
                while (type is CSPointer cspointer)
                {
                    type = cspointer.BaseType;
                    pointers.Append('*');
                }

                string underlyingType = type switch
                {
                    CSStructPrimitive csstruct => csstruct.UnderlyingType?.ToCSString() ?? throw new Exception("A struct didnt contain an underlying type."),
                    CSEnum csenum => csenum.PrimitiveType.ToCSString(),
                    CSBool8 => "byte",
                    _ => type.ToCSString()
                };

                return underlyingType + pointers;
            }
        }

        public static void Write(OutputData data)
        {
            // This is quite fragile, no idea if there is an easy way that is "better".
            string outputProjectPath = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? throw new NullReferenceException(),
                "..", "..", "..", "..", "..", AudioNamespace);

            foreach (Namespace @namespace in data.Namespaces)
            {
                WriteNamespace(outputProjectPath, @namespace);
            }
        }

        public static void WriteNamespace(string outputProjectPath, Namespace @namespace)
        {
            // FIXME: Fix function pointers so we can merge this.
            FileStrings strings = @namespace.Name switch
            {
                OutputApi.AL => new FileStrings("AL", "AL", "OpenAL", "ALLoader", "ALLoader", "DefaultALGetProcAddress"),
                OutputApi.ALC => new FileStrings("ALC", "ALC", "OpenAL.ALC", "ALCLoader", "ALLoader", "DefaultALCGetProcAddress"),
                _ => throw new ArgumentException($"This is not a valid output API ({@namespace.Name})"),
            };

            string directoryPath = Path.Combine(outputProjectPath, Path.Combine(strings.Namespace.Split('.')));
            if (!Directory.Exists(directoryPath)) Directory.CreateDirectory(directoryPath);
            var sortedNativeFunctions = SortNativeFunctions(@namespace);

            WriteContainers(directoryPath, strings, @namespace);
            WriteFunctionPointers(outputProjectPath, strings, sortedNativeFunctions);
            WriteNativeFunctions(directoryPath, strings, @namespace.VendorFunctions, @namespace.Documentation);
            WriteOverloads(directoryPath, strings, @namespace.VendorFunctions, @namespace.Documentation);
            WriteFunctionNameList(outputProjectPath, strings, sortedNativeFunctions);
            const string LoadFunctionName = "loadFunction";
            switch (@namespace.Name)
            {
                case OutputApi.AL:
                    WriteFunctionPointerInitializers(outputProjectPath, strings, @namespace, sortedNativeFunctions, "ByDeviceOrContext",
                        ("delegate* unmanaged[Cdecl]<IntPtr, byte*, void*>", LoadFunctionName), [("IntPtr", "handle")]);
                    WriteFunctionPointerInitializers(outputProjectPath, strings, @namespace, sortedNativeFunctions, "",
                        ("delegate* unmanaged[Cdecl]<byte*, void*>", LoadFunctionName), []);
                    break;
                case OutputApi.ALC:
                    WriteFunctionPointerInitializers(outputProjectPath, strings, @namespace, sortedNativeFunctions, "",
                        ("delegate* unmanaged[Cdecl]<IntPtr, byte*, void*>", LoadFunctionName), [("IntPtr", "device")]);
                    break;
                default:
                    break;
            }

            WriteEnums(directoryPath, strings, @namespace.EnumGroups);
        }

        private static void WriteAutoGeneratedWarnings(IndentedTextWriter writer)
        {
            // Style Analyzers needs <auto-generated /> to be inserted at the start of file for auto-generated files.
            writer.WriteLine($"// <auto-generated />");
            writer.WriteLine($"// This file is auto generated, do not edit.");
            writer.WriteLine($"#nullable enable");
        }

        private static void WriteUsings(IndentedTextWriter writer, params IReadOnlyList<string> additionalUsings)
        {
            var allUsings = Usings.Concat(additionalUsings).OrderBy(a => a == "System" ? 0 : 1).ThenBy(a => a.StartsWith("System.") ? 0 : 1).ThenBy(a => a);
            foreach (var item in allUsings)
            {
                writer.WriteLine($"using {item};");
            }
        }

        // FIXME: Maybe we should nest this 
        private static void WriteFunctionPointers(string directoryPath, FileStrings strings, SortedNativeFunctions sortedNativeFunctions)
        {
            using StreamWriter stream = File.CreateText(Path.Combine(directoryPath, $"{strings.FileNamePrefix}.Pointers.cs"));
            using IndentedTextWriter writer = new IndentedTextWriter(stream);
            WriteAutoGeneratedWarnings(writer);
            WriteUsings(writer);
            writer.WriteLine();
            writer.WriteLine($"namespace {AudioNamespace}.{strings.Namespace}");

            using (writer.CsScope())
            {
                var groupsFunctions = sortedNativeFunctions.Groups.SelectMany(a => a.SelectMany(b => b.functions));
                var directGroupsFunctions = sortedNativeFunctions.DirectGroups.SelectMany(a => a.SelectMany(b => b.functions));
                var allFunctions = groupsFunctions.Concat(directGroupsFunctions).Distinct().ToList();
                writer.WriteLine($"/// <summary>An immutable collection of all function pointers to all OpenAL <c>{strings.ApiName.ToLowerInvariant()}</c> entry points.</summary>");
                // FIXME: Better class name?
                writer.WriteLine($"public readonly unsafe partial struct {strings.ClassName}Pointers");
                using (writer.CsScope())
                {
                    foreach (var function in allFunctions)
                    {
                        WriteFunctionPointer(writer, function, strings, true);
                    }
                }

                writer.WriteLine($"/// <summary>A mutable collection of all function pointers to all OpenAL <c>{strings.ApiName.ToLowerInvariant()}</c> entry points.</summary>");
                writer.WriteLine($"public unsafe partial struct Mutable{strings.ClassName}Pointers");
                using (writer.CsScope())
                {
                    foreach (var function in allFunctions)
                    {
                        WriteFunctionPointer(writer, function, strings, false);
                    }
                }
            }
        }

        private static void WriteFunctionPointer(IndentedTextWriter writer, Function function, FileStrings strings, bool isReadOnly)
        {
            // Write delegate field initialized to the lazy loader.
            // Write public function definition that calls delegate.
            // Write lazy loader function.
            GetNativeFunctionSignature(function, false, true, out _, out StringBuilder paramNames, out StringBuilder delegateTypes, out StringBuilder signature, out _, out string returnType);

            string entryPoint = function.EntryPoint;

            writer.WriteLine($"/// <summary><b>[entry point: <c>{entryPoint}</c>]</b></summary>");
            writer.Write("public");
            if (isReadOnly)
                writer.Write(" readonly");
            writer.WriteLine($" delegate* unmanaged[Cdecl]<{delegateTypes}> _{entryPoint}_fnptr;");
            writer.WriteLine();
        }

        private static readonly EqualityComparer<List<string>> StringListComparer = EqualityComparer<List<string>>.Create(
            (a, b) =>
            {
                if (a is null) return b is null;
                if (b is null) return false;
                if (a.Count != b.Count) return false;
                for (int i = 0; i < a.Count; i++)
                {
                    if (a[i] != b[i]) return false;
                }
                return true;
            }, a =>
            {
                var h = new HashCode();
                foreach (var item in a)
                {
                    h.Add(item);
                }
                return h.ToHashCode();
            });

        private static SortedNativeFunctions SortNativeFunctions(Namespace @namespace)
        {
            var items = @namespace.VendorFunctions;
            var nonDirectItems = @namespace.Name == OutputApi.AL ? items.Where(a => a.Vendor != "Direct") : items;
            var directItems = @namespace.Name == OutputApi.AL ? items.Where(a => a.Vendor == "Direct") : [];
            var groups = nonDirectItems.Select(a => a.Functions.Select(a => a.NativeFunction).GroupBy(a => @namespace.Documentation.TryGetValue(a, out var documentation) ? documentation.AddedIn : [""], StringListComparer)
                        .Select(g => (g.Key, g.OrderBy(f => f.EntryPoint).ToList())).ToList()).ToList();
            var directGroups = directItems.Select(a => a.Functions.Select(a => a.NativeFunction).GroupBy(a => @namespace.Documentation.TryGetValue(a, out var documentation) ? documentation.Dependency : "")
                                .OrderBy(a => a.Key == "v1.0" ? "" : a.Key).Select(g => (g.Key, g.OrderBy(f => f.EntryPoint).ToList())).ToList()).ToList();
            return new(groups, directGroups);
        }

        private static void WriteFunctionNameList(string directoryPath, FileStrings strings, SortedNativeFunctions sortedNativeFunctions)
        {
            using StreamWriter stream = File.CreateText(Path.Combine(directoryPath, $"{strings.FileNamePrefix}.Pointers.Names.cs"));
            using IndentedTextWriter writer = new IndentedTextWriter(stream);
            WriteAutoGeneratedWarnings(writer);
            WriteUsings(writer);
            writer.WriteLine();
            writer.WriteLine($"namespace {AudioNamespace}.{strings.Namespace}");

            using (writer.CsScope())
            {
                writer.WriteLine($"public readonly unsafe partial struct {strings.ClassName}Pointers");
                using (writer.CsScope())
                {
                    writer.Write($"internal static ReadOnlySpan<byte> AllFunctionNames => \"");
                    List<(string entryPoint, int offset)> offsets = new();
                    int currentOffset = 0;
                    var builder = new StringBuilder();
                    var groupsFunctions = sortedNativeFunctions.Groups.SelectMany(a => a.SelectMany(b => b.functions));
                    var directGroupsFunctions = sortedNativeFunctions.DirectGroups.SelectMany(a => a.SelectMany(b => b.functions));
                    var nativeFunctions = groupsFunctions.Concat(directGroupsFunctions);
                    var entryPoints = nativeFunctions.Select(a => a.EntryPoint).Distinct().ToList();
                    foreach (var entryPoint in entryPoints)
                    {
                        builder.Append($"{entryPoint}\\0");
                        offsets.Add((entryPoint, currentOffset));
                        currentOffset += entryPoint.Length + 1;
                    }
                    writer.WriteLine($"{builder}\"u8;");
                    var constType = $"internal const {offsets[^1].offset switch
                    {
                        <= byte.MaxValue => "byte",
                        <= ushort.MaxValue => "ushort",
                        _ => "int"
                    }}";
                    foreach (var (entryPoint, offset) in offsets)
                    {
                        writer.WriteLine($"{constType} {entryPoint}_offset = {offset};");
                    }
                }
            }
        }

        private static void WriteFunctionPointerInitializers(string directoryPath, FileStrings strings, Namespace @namespace, SortedNativeFunctions sortedNativeFunctions, string overloadName, (string type, string name) loadFunction, IReadOnlyList<(string type, string name)> additionalParameters)
        {
            using StreamWriter stream = File.CreateText(Path.Combine(directoryPath, $"{strings.FileNamePrefix}.Pointers.Initialize{overloadName}.cs"));
            using IndentedTextWriter writer = new IndentedTextWriter(stream);

            WriteAutoGeneratedWarnings(writer);
            WriteUsings(writer);
            writer.WriteLine();
            writer.WriteLine($"namespace {AudioNamespace}.{strings.Namespace}");

            using (writer.CsScope())
            {
                writer.WriteLine($"public readonly unsafe partial struct {strings.ClassName}Pointers");
                using (writer.CsScope())
                {
                    var parameters = string.Join(", ", additionalParameters.Prepend((loadFunction.type, loadFunction.name)).Select(a => $"{a.type} {a.name}"));
                    writer.WriteLine($"/// <summary>");
                    writer.WriteLine($"/// Initializes a new instance of the <see cref=\"{strings.ClassName}Pointers\"/> struct.");
                    writer.WriteLine($"/// </summary>");
                    writer.WriteLine($"internal {strings.ClassName}Pointers({parameters})");
                    using (writer.CsScope())
                    {
                        writer.WriteLine($"fixed (byte* names = AllFunctionNames)");
                        using (writer.CsScope())
                        {
                            var functionName = loadFunction.name;
                            var joinedAdditionalParameters = string.Join(", ", additionalParameters.Select(a => a.name));
                            HashSet<string> exportedEndpoints = [];
                            foreach (var vendor in sortedNativeFunctions.Groups)
                            {
                                foreach (var (_, functions) in vendor)
                                {
                                    writer.WriteLine();
                                    var extensionFunctions = functions.ExceptBy(exportedEndpoints, a => a.EntryPoint).OrderBy(a => a.EntryPoint).ToList();
                                    exportedEndpoints.UnionWith(extensionFunctions.Select(a => a.EntryPoint));
                                    WriteExtensionBlock(functionName, writer, joinedAdditionalParameters, extensionFunctions);
                                }
                            }
                            if (@namespace.Name != OutputApi.AL) return;
                            writer.WriteLine();
                            foreach (var item in sortedNativeFunctions.DirectGroups)
                            {
                                var vendorDirectFunctions = item.ToList();
                                CsScope? scope = null;
                                if (vendorDirectFunctions.Count > 0)
                                {
                                    var firstDependency = vendorDirectFunctions[0].functions;
                                    var extensionFunctions = firstDependency.ToList();
                                    var functions = extensionFunctions.GetEnumerator();
                                    if (functions.MoveNext())
                                    {
                                        var function = functions.Current;
                                        GetNativeFunctionSignature(function, postfixName: false, swapTypesForUnderlyingType: true, out _, out _, out StringBuilder delegateTypes, out _, out _, out _);
                                        string entryPoint = function.EntryPoint;
                                        writer.WriteLine($"var {entryPoint}_fnptr = (delegate* unmanaged[Cdecl]<{delegateTypes}>){functionName}({joinedAdditionalParameters}{(string.IsNullOrEmpty(joinedAdditionalParameters) ? "" : ", ")}names + {entryPoint}_offset);");
                                        writer.WriteLine($"_{entryPoint}_fnptr = {entryPoint}_fnptr;");
                                        writer.WriteLine($"if ({entryPoint}_fnptr is not null)");
                                        scope = writer.CsScope();
                                        while (functions.MoveNext())
                                        {
                                            function = functions.Current;
                                            WriteFunctionPointerInitializer(writer, function, functionName, joinedAdditionalParameters);
                                        }
                                    }
                                }
                                foreach (var (_, functions) in vendorDirectFunctions.Skip(1))
                                {
                                    writer.WriteLine();
                                    WriteExtensionBlock(functionName, writer, joinedAdditionalParameters, [.. functions]);
                                }
                                scope?.Dispose();
                            }
                        }
                    }
                }
            }

            static void WriteExtensionBlock(string loadFunction, IndentedTextWriter writer, string joinedAdditionalParameters, List<Function> extensionFunctions)
            {
                var functions = extensionFunctions.GetEnumerator();
                CsScope? scope = null;
                if (extensionFunctions.Count > 3 && functions.MoveNext())
                {
                    var function = functions.Current;
                    GetNativeFunctionSignature(function, postfixName: false, swapTypesForUnderlyingType: true, out _, out _, out StringBuilder delegateTypes, out _, out _, out _);
                    string entryPoint = function.EntryPoint;
                    writer.WriteLine($"var {entryPoint}_fnptr = (delegate* unmanaged[Cdecl]<{delegateTypes}>){loadFunction}({joinedAdditionalParameters}{(string.IsNullOrEmpty(joinedAdditionalParameters) ? "" : ", ")}names + {entryPoint}_offset);");
                    writer.WriteLine($"_{entryPoint}_fnptr = {entryPoint}_fnptr;");
                    writer.WriteLine($"if ({entryPoint}_fnptr is not null)");
                    scope = writer.CsScope();
                }
                while (functions.MoveNext())
                {
                    var function = functions.Current;
                    WriteFunctionPointerInitializer(writer, function, loadFunction, joinedAdditionalParameters);
                }
                scope?.Dispose();
            }
        }

        private static void WriteFunctionPointerInitializer(IndentedTextWriter writer, Function function, string loadFunction, string additionalParameters)
        {
            GetNativeFunctionSignature(function, postfixName: false, swapTypesForUnderlyingType: true, out _, out _, out StringBuilder delegateTypes, out _, out _, out _);
            string entryPoint = function.EntryPoint;
            // Dotnet gurantees you can't get torn values when assigning functionpointers, assuming proper allignment which is default.
            writer.WriteLine($"_{entryPoint}_fnptr = (delegate* unmanaged[Cdecl]<{delegateTypes}>){loadFunction}({additionalParameters}{(string.IsNullOrEmpty(additionalParameters) ? "" : ", ")}names + {entryPoint}_offset);");
        }

        private static void WriteContainers(string directoryPath, FileStrings strings, Namespace @namespace)
        {
            var groups = @namespace.VendorFunctions;
            using StreamWriter stream = File.CreateText(Path.Combine(directoryPath, $"{strings.FileNamePrefix}.cs"));
            using IndentedTextWriter writer = new IndentedTextWriter(stream);
            WriteAutoGeneratedWarnings(writer);
            WriteUsings(writer);

            writer.WriteLine();
            writer.WriteLine($"namespace {AudioNamespace}.{strings.Namespace}");
            using (writer.CsScope())
            {
                writer.WriteLine($"/// <summary>Exposes all the {strings.ApiName} functions loaded by {strings.LoaderClass}.</summary>");
                writer.WriteLine($"public readonly unsafe ref partial struct {strings.ApiName}");
                using (writer.CsScope())
                {
                    writer.WriteLine($"internal readonly ref readonly {strings.ApiName}Pointers _pointers;");
                    writer.WriteLine($"/// <summary>The reference to the container of function pointers loaded by {strings.LoaderClass}.</summary>");
                    writer.WriteLine($"public ref readonly {strings.ApiName}Pointers Pointers => ref _pointers;");
                    writer.WriteLine();
                    writer.WriteLine($"/// <summary>");
                    writer.WriteLine($"/// Initializes a new instance of the <see cref=\"{strings.ApiName}\"/> struct.");
                    writer.WriteLine($"/// </summary>");
                    writer.WriteLine($"/// <param name=\"pointers\">The <see cref=\"{strings.ApiName}Pointers\"/> to initialize with.</param>");
                    writer.WriteLine($"public {strings.ApiName}(ref readonly {strings.ApiName}Pointers pointers)");
                    using (writer.CsScope())
                    {
                        writer.WriteLine($"_pointers = ref pointers;");
                    }
                    foreach (var vendor in groups.Select(a => a.Vendor).Where(a => !string.IsNullOrEmpty(a)))
                    {
                        writer.WriteLine($"/// <summary>{vendor} extensions.</summary>");
                        writer.WriteLine($"public {strings.ApiName}{APIExtensionSuffix}.{vendor} {vendor} => new(this);");
                    }
                }
                writer.WriteLine();
                writer.WriteLine($"/// <summary>Contains extensions' pointer containers.</summary>");
                writer.WriteLine($"public static partial class {strings.ApiName}Extensions");
                using (writer.CsScope())
                {
                    var vendors = groups.Select(a => a.Vendor);
                    writer.WriteLine($"/// <summary>Interface for {strings.ApiName}Pointers containers.</summary>");
                    writer.WriteLine($"public interface I{strings.ApiName}Container");
                    using (writer.CsScope())
                    {
                        writer.WriteLine($"/// <summary>The underlying {strings.ApiName} object.</summary>");
                        writer.WriteLine($"{strings.ApiName} {strings.ApiName} {{ get; }}");
                    }
                    writer.WriteLine();
                    if (@namespace.Name == OutputApi.AL)
                    {
                        writer.WriteLine($"/// <summary>The {strings.ApiName}Pointers container for AL_EXT_direct_context extensions.</summary>");
                        writer.WriteLine($"public readonly unsafe ref partial struct Direct<TDependentAPIVendor>(TDependentAPIVendor vendor) : I{strings.ApiName}Container where TDependentAPIVendor : struct, I{strings.ApiName}Container, allows ref struct");
                        using (writer.CsScope())
                        {
                            writer.WriteLine($"/// <inheritdoc/>");
                            writer.WriteLine($"public {strings.ApiName} {strings.ApiName} {{ get; }} = vendor.{strings.ApiName};");
                        }
                    }

                    foreach (var vendor in vendors.Where(a => !string.IsNullOrEmpty(a)))
                    {
                        writer.WriteLine();
                        writer.WriteLine($"/// <summary>{vendor} extensions.</summary>");
                        writer.WriteLine($"public readonly unsafe ref partial struct {vendor}({strings.ApiName} {strings.ApiName.ToLowerInvariant()}) : I{strings.ApiName}Container");
                        using (writer.CsScope())
                        {
                            writer.WriteLine($"/// <inheritdoc/>");
                            writer.WriteLine($"public {strings.ApiName} {strings.ApiName} {{ get; }} = {strings.ApiName.ToLowerInvariant()};");
                            if (@namespace.Name == OutputApi.AL && vendor != "Direct")
                            {
                                writer.WriteLine($"/// <summary>AL_EXT_direct_context extensions for {vendor} extensions.</summary>");
                                writer.WriteLine($"public Direct<{vendor}> Direct => new(this);");
                            }
                        }
                    }
                }
            }
        }

        private static void WriteNativeFunctions(string directoryPath, FileStrings strings, List<VendorFunctions> groups, Dictionary<Function, FunctionDocumentation> documentation)
        {
            using StreamWriter stream = File.CreateText(Path.Combine(directoryPath, $"{strings.FileNamePrefix}Functions.Native.cs"));
            using IndentedTextWriter writer = new IndentedTextWriter(stream);
            WriteAutoGeneratedWarnings(writer);
            WriteUsings(writer);

            writer.WriteLine();
            writer.WriteLine($"namespace {AudioNamespace}.{strings.Namespace}");
            using (writer.CsScope())
            {
                writer.WriteLine($"public static unsafe partial class {strings.ApiName}Functions");
                using (writer.CsScope())
                {
                    foreach (var group in groups)
                    {
                        var vendor = group.Vendor;
                        foreach (var function in group.Functions)
                        {
                            var nativeFunction = function.NativeFunction;
                            bool postfixName = group.NativeFunctionsWithPostfix.Contains(nativeFunction);
                            documentation.TryGetValue(nativeFunction, out FunctionDocumentation? functionDocumentation);
                            WriteNativeFunction(writer, nativeFunction, postfixName, functionDocumentation, strings.ApiName, vendor);
                        }
                    }
                }
            }

            writer.Flush();
        }

        private static void WriteNativeFunction(IndentedTextWriter writer, Function function, bool postfixName, FunctionDocumentation? documentation, string apiName, string vendorName)
        {
            GetNativeFunctionSignature(function, postfixName, swapTypesForUnderlyingType: false,
                out string name,
                out StringBuilder paramNames,
                out StringBuilder delegateTypes,
                out StringBuilder signature,
                out bool handleAbiDifferenceForTypesafeHandles,
                out string returnType);

            string entryPoint = function.EntryPoint;
            GenerateExtensionParameters(apiName, vendorName, documentation, out var thisParameterName, out var extendingCSType, out _, out var apiVariableName);

            if (documentation != null)
            {
                WriteDocumentation(writer, function, documentation, thisParameterName);
            }
            var strongReturnType = function.StrongReturnType;
            if (handleAbiDifferenceForTypesafeHandles && strongReturnType is not null)
            {
                // Here we just cast and return the correct return type in the public facing function.
                // This works because all of the structs that get here should have a defined cast from the primitive type to the struct type.
                // These casts need to be added manually for this to work correctly.
                // - 2021-06-22
                returnType = strongReturnType.ToCSString();
            }
            // We want to generally prefer overloads, this will allow calls like
            // ALC.OpenDevice(null) to work correctly without ambiguous overloads.
            var allParamNames = $"this {extendingCSType} {thisParameterName}";
            if (signature.Length > 0) allParamNames = string.Join(", ", allParamNames, signature);
            writer.WriteLine("[OverloadResolutionPriority(short.MinValue)]");
            writer.WriteLine($"[MethodImpl(MethodImplOptions.AggressiveInlining)]");
            writer.Write($"public static {returnType} {name}({allParamNames}) => ");
            if (handleAbiDifferenceForTypesafeHandles)
            {
                if (strongReturnType is CSBool8)
                {
                    // HACK: We can't cast byte to bool, sigh...
                    writer.Write($"0 != ");
                }
                else
                {
                    writer.Write($"({returnType}) ");
                }
            }
            writer.WriteLine($"{apiVariableName}._pointers._{entryPoint}_fnptr({paramNames});");
            writer.WriteLine();
        }

        private static void WriteOverloads(string directoryPath, FileStrings strings, List<VendorFunctions> groups, Dictionary<Function, FunctionDocumentation> documentation)
        {
            using StreamWriter stream = File.CreateText(Path.Combine(directoryPath, $"{strings.FileNamePrefix}Functions.Overloads.cs"));
            using IndentedTextWriter writer = new IndentedTextWriter(stream);
            WriteAutoGeneratedWarnings(writer);
            WriteUsings(writer);

            writer.WriteLine();
            writer.WriteLine($"namespace {AudioNamespace}.{strings.Namespace}");
            using (writer.CsScope())
            {
                writer.WriteLine($"public static unsafe partial class {strings.ApiName}Functions");
                using (writer.CsScope())
                {
                    foreach (var group in groups)
                    {
                        var vendor = group.Vendor;
                        foreach (var function in group.Functions)
                        {
                            foreach (var overload in function.Overloads)
                            {
                                bool postfixNativeCall = group.NativeFunctionsWithPostfix.Contains(overload.NativeFunction);
                                documentation.TryGetValue(overload.NativeFunction, out var functionDocumentation);
                                WriteOverloadMethod(writer, overload, postfixNativeCall, strings.ApiName, vendor, functionDocumentation);
                            }
                        }
                    }
                }
            }
        }

        private static void WriteOverloadMethod(IndentedTextWriter writer, Overload overload, bool postfixNativeCall, string apiName, string vendorName, FunctionDocumentation? documentation)
        {
            // FIXME: Functions taking function pointer parameters cannot be properly referenced
            // in a cref as of yet (see https://github.com/dotnet/roslyn/issues/48363) so this
            // will fail for these functions.
            // - Noggin_bops 2025-08-076
            string parameterTypes = string.Join(", ", overload.NativeFunction.Parameters.Select(p => p.StrongType!.ToXMLString()));

            string nativeFunctionName = overload.NativeFunction.Name;
            if (postfixNativeCall)
            {
                nativeFunctionName += "_";
            }
            if (overload.NativeFunction.GenericTypes is not null)
            {
                nativeFunctionName += $"{{{string.Join(", ", overload.NativeFunction.GenericTypes)}}}";
            }
            GenerateExtensionParameters(apiName, vendorName, documentation, out var thisParameterName, out var extendingCSType, out var extendingXMLType, out _);
            writer.WriteLine($"/// <inheritdoc cref=\"{nativeFunctionName}({extendingXMLType}, {parameterTypes})\"/>");

            string genericTypes = overload.GenericTypes.Length <= 0 ? "" : $"<{string.Join(", ", overload.GenericTypes)}>";
            writer.WriteLine($"[MethodImpl(MethodImplOptions.AggressiveInlining)]");
            var priority = overload.OverloadResolutionPriority;
            if (priority != 0) writer.WriteLine($"[OverloadResolutionPriority({priority})]");
            writer.WriteLine($"public static unsafe {overload.ReturnType.ToCSString()} {overload.OverloadName}{genericTypes}({string.Join(", ", overload.InputParameters.Select(p => p.ToDefinitionString()).Prepend($"this {extendingCSType} {thisParameterName}"))})");
            using (writer.Indent())
            {
                foreach (var type in overload.GenericTypes)
                {
                    writer.WriteLine($"where {type} : unmanaged");
                }
            }

            using (writer.CsScope())
            {
                // FIXME: Shouldn't we create the overloads return type here and let the overload layers
                // create the intermediate return values?
                /*if (overload.ReturnType is not CSVoid && overload.NativeFunction.ReturnType is not CSVoid)
                {
                    writer.WriteLine($"{overload.NativeFunction.ReturnType.ToCSString()} returnValue;");
                }*/
                if (overload.ReturnType is not CSVoid /*&& overload.NativeFunction.ReturnType is not CSVoid*/)
                {
                    writer.WriteLine($"{overload.ReturnType.ToCSString()} {overload.NameTable.ReturnName};");
                }

                string? returnName = WriteNestedOverload(writer, overload, new NameTable(), postfixNativeCall, thisParameterName);

                if (returnName != null)
                {
                    writer.WriteLine($"return {returnName};");
                }
            }
        }

        private static void GenerateExtensionParameters(string apiName, string vendorName, FunctionDocumentation? documentation, out string thisParameterName, out string extendingCSType, out string extendingXMLType, out string apiVariableName)
        {
            var isVendorEmpty = string.IsNullOrEmpty(vendorName);
            thisParameterName = (isVendorEmpty ? apiName : vendorName).ToLowerInvariant();
            extendingCSType = isVendorEmpty ? apiName : $"{apiName}{APIExtensionSuffix}.{vendorName}";
            extendingXMLType = extendingCSType;
            if (documentation is not null && vendorName == "Direct" && !string.IsNullOrEmpty(documentation?.DependentVendor))
            {
                extendingCSType = $"{apiName}{APIExtensionSuffix}.Direct<{apiName}{APIExtensionSuffix}.{documentation?.DependentVendor}>";
                extendingXMLType = $"{apiName}{APIExtensionSuffix}.Direct{{{apiName}{APIExtensionSuffix}.{documentation?.DependentVendor}}}";
            }
            apiVariableName = isVendorEmpty ? thisParameterName : $"{thisParameterName}.{apiName}";
        }

        private static string? WriteNestedOverload(IndentedTextWriter writer, Overload overload, NameTable nameTable, bool postfixNativeCall, string thisParameterName)
        {
            // Update the name table with the names for this overload.
            nameTable.Apply(overload.NameTable);

            overload.MarshalLayerToNested?.WritePrologue(writer, nameTable);

            string? returnName;
            if (overload.NestedOverload is not null)
            {
                returnName = WriteNestedOverload(writer, overload.NestedOverload, nameTable, postfixNativeCall, thisParameterName);
            }
            else
            {
                // Writes the native call.
                Function nativeFunction = overload.NativeFunction;
                string name = nativeFunction.Name;
                if (postfixNativeCall) name += "_";

                string arguments = string.Join(", ", nativeFunction.Parameters.Select(p => nameTable[p]));

                if (nativeFunction.StrongReturnType is CSVoid)
                {
                    writer.WriteLine($"{thisParameterName}.{name}({arguments});");
                    return null;
                }
                else
                {
                    writer.WriteLine($"returnValue = {thisParameterName}.{name}({arguments});");
                    return "returnValue";
                }
            }

            return overload.MarshalLayerToNested?.WriteEpilogue(writer, nameTable, returnName) ?? returnName;
        }

        private static void WriteDocumentation(IndentedTextWriter writer, Function function, FunctionDocumentation documentation, string thisParameterName)
        {
            writer.Write("/// <summary> ");
            writer.Write($"<b>[requires: {string.Join(" | ", documentation.AddedIn)}");
            if (!string.IsNullOrEmpty(documentation.Dependency))
            {
                if (documentation.AddedIn.Count > 0)
                    writer.Write($" &amp; (");
                writer.Write($"{documentation.Dependency}");
                if (documentation.AddedIn.Count > 0)
                    writer.Write($")");
            }
            writer.Write($"]</b> ");
            if (documentation.RemovedIn?.Count > 0)
                writer.Write($"<b>[removed in: {string.Join(" | ", documentation.RemovedIn)}]</b> ");
            writer.Write($"<b>[entry point: <c>{function.EntryPoint}</c>]</b><br/>");
            writer.WriteLine($" {documentation.Purpose} </summary>");

            writer.WriteLine($"/// <param name=\"{thisParameterName}\">The container of native function pointers.</param>");
            for (int i = 0; i < documentation.Parameters.Length && i < function.Parameters.Count; i++)
            {
                var parameterDoc = documentation.Parameters[i];
                var parameter = function.Parameters[i];

                // We use the parameter name here, if the documentation uses another name
                // we've already warned about this, and using the name the C# documentation
                // system expects reduces a lot of the warnings that are generated.
                // - Noggin_bops 2025-08-08
                writer.WriteLine($"/// <param name=\"{parameter.Name}\">{parameterDoc.Description}</param>");
            }

            if (documentation.RefPagesLinks.Count > 0)
            {
                writer.WriteLine($"/// <remarks>{string.Join("<br/>", documentation.RefPagesLinks.Select(url => $"<see href=\"{url}\"/>"))}</remarks>");
            }
        }

        private static void WriteEnums(string directoryPath, FileStrings strings, List<EnumGroup> enumGroups)
        {
            using StreamWriter stream = File.CreateText(Path.Combine(directoryPath, $"{strings.FileNamePrefix}.Enums.cs"));
            using IndentedTextWriter writer = new IndentedTextWriter(stream);
            WriteAutoGeneratedWarnings(writer);
            WriteUsings(writer);
            writer.WriteLine();
            writer.WriteLine($"namespace {AudioNamespace}.{strings.Namespace}");
            using (writer.CsScope())
            {
                writer.WriteLineNoTabs("#pragma warning disable CA1069 // Enums values should not be duplicated");
                writer.WriteLineNoTabs("#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member");
                // FIXME: Maybe we want to fix this?
                writer.WriteLineNoTabs("#pragma warning disable CS0419 // Ambiguous reference in cref attribute");
                WriteEnumGroups(writer, strings.ApiName, enumGroups);
                writer.WriteLineNoTabs("#pragma warning restore CA1069 // Enums values should not be duplicated");
                writer.WriteLineNoTabs("#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member");
                // FIXME: Maybe we want to fix this?
                writer.WriteLineNoTabs("#pragma warning restore CS0419 // Ambiguous reference in cref attribute");
            }
        }

        private static void WriteEnumGroups(IndentedTextWriter writer, string apiName, List<EnumGroup> enumGroups)
        {
            foreach (var group in enumGroups)
            {
                if (group.FunctionsUsingEnumGroup != null)
                {
                    var functions = group.FunctionsUsingEnumGroup.Take(3);
                    writer.Write($"///<summary>Used in {string.Join(", ", functions.Select(f => $"<see cref=\"{apiName}Functions.{f.Function.Name}\"/>"))}");
                    if (group.FunctionsUsingEnumGroup.Count > 3)
                    {
                        writer.Write($", ...");
                    }
                    writer.WriteLine($"</summary>");
                }

                if (group.IsFlags) writer.WriteLine($"[Flags]");
                writer.WriteLine($"public enum {group.Name} : uint");
                using (writer.CsScope())
                {
                    foreach (var member in group.Members)
                    {

                        writer.WriteLine($"/// <remarks>[<b>originally: {member.Name}</b>]</remarks>");

                        // HACK: Some enums have a value of -1, and because
                        // we don't know the bitwidth of the enum here we can't cast
                        // the value correctly. This hack fixes this for -1 but doesn't
                        // work for any other negative numbers...
                        // - Noggin_bops 2024-11-11
                        if (member.Value == ulong.MaxValue)
                        {
                            writer.WriteLine($"{member.MangledName} = unchecked((uint)-1),");
                        }
                        else
                        {
                            writer.WriteLine($"{member.MangledName} = {member.Value},");
                        }
                    }
                }
            }
        }

        internal static void WriteEFXPresets(List<EFXPreset> efxPresets)
        {
            // This is quite fragile, no idea if there is an easy way that is "better".
            string outputProjectPath = Path.Combine(
                Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? throw new NullReferenceException(),
                "..", "..", "..", "..", "..", AudioNamespace);

            string directoryPath = Path.Combine(outputProjectPath, "OpenAL");

            using StreamWriter stream = File.CreateText(Path.Combine(directoryPath, $"EFXPresets.cs"));
            using IndentedTextWriter writer = new IndentedTextWriter(stream);

            WriteAutoGeneratedWarnings(writer);
            WriteUsings(writer);
            writer.WriteLine();
            writer.WriteLine($"namespace {AudioNamespace}.OpenAL");
            using (writer.CsScope())
            {
                // FIXME: Better class name?
                writer.WriteLine($"/// <summary>Provides a collection of Reverb presets.</summary>");
                writer.WriteLine($"public static unsafe partial class ReverbPresets");
                using (writer.CsScope())
                {
                    foreach (var preset in efxPresets)
                    {
                        writer.WriteLine($"/// <summary>The {preset.Name} reverb preset.</summary>");
                        writer.WriteLine($"public static readonly ReverbProperties {preset.Name} = new ReverbProperties");
                        writer.WriteLine("(");
                        using (writer.Indent())
                        {
                            // Non-round-trip format might alter precision, so we use round-trip format specifier.
                            writer.WriteLine($"{preset.Density:R}f,");
                            writer.WriteLine($"{preset.Diffusion:R}f,");
                            writer.WriteLine($"{preset.Gain:R}f,");
                            writer.WriteLine($"{preset.GainHF:R}f,");
                            writer.WriteLine($"{preset.GainLF:R}f,");
                            writer.WriteLine($"{preset.DecayTime:R}f,");
                            writer.WriteLine($"{preset.DecayHFRatio:R}f,");
                            writer.WriteLine($"{preset.DecayLFRatio:R}f,");
                            writer.WriteLine($"{preset.ReflectionsGain:R}f,");
                            writer.WriteLine($"{preset.RelfectionsDelay:R}f,");
                            writer.WriteLine($"new Vector3({preset.ReflectionsPan.X:R}f, {preset.ReflectionsPan.Y:R}f, {preset.ReflectionsPan.Z:R}f),");
                            writer.WriteLine($"{preset.LateReverbGain:R}f,");
                            writer.WriteLine($"{preset.LateReverbDelay:R}f,");
                            writer.WriteLine($"new Vector3({preset.LateReverbPan.X:R}f, {preset.LateReverbPan.Y:R}f, {preset.LateReverbPan.Z:R}f),");
                            writer.WriteLine($"{preset.EchoTime:R}f,");
                            writer.WriteLine($"{preset.EchoDepth:R}f,");
                            writer.WriteLine($"{preset.ModulationTime:R}f,");
                            writer.WriteLine($"{preset.ModulationDepth:R}f,");
                            writer.WriteLine($"{preset.AirAbsorptionGainHF:R}f,");
                            writer.WriteLine($"{preset.HFReference:R}f,");
                            writer.WriteLine($"{preset.LFReference:R}f,");
                            writer.WriteLine($"{preset.RoomRolloffFactor:R}f,");
                            writer.WriteLine($"{(preset.DecayHFLimit ? 1 : 0)}");
                        }
                        writer.WriteLine(");");
                        writer.WriteLine();
                    }
                }
            }
        }
    }

    internal record struct SortedNativeFunctions(List<List<(List<string> key, List<Function> functions)>> Groups, List<List<(string key, List<Function> functions)>> DirectGroups);
}
