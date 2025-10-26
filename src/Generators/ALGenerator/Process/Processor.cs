using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;

using ALGenerator.Parsing;
using ALGenerator.Process;

using GeneratorBase;
using GeneratorBase.Overloading;
using GeneratorBase.Utility;
using GeneratorBase.Utility.Extensions;

namespace ALGenerator.Process
{
    internal static class Processor
    {
        // These types are only used to pass data from ProcessSpec to GetOutputApiFromRequireTags.
        private record ProcessedGLInformation(
            Dictionary<string, OverloadedFunction> AllFunctions,
            Dictionary<OutputApi, Dictionary<string, EnumGroupMember>> AllEnumsPerAPI,
            List<EnumGroupInfo> AllEnumGroups);

        internal record OverloadedFunction(
            Function NativeFunction,
            Dictionary<OutputApi, CommandDocumentation> Documentation,
            Overload[] Overloads,
            bool ChangeNativeName);

        internal sealed record EnumGroupInfo(
            string OriginalName,
            string GroupName,
            bool IsFlags)
        {
            // To deduplicate these correctly we need special logic for the IsFlags bool
            // so we don't consider it in the equality check and hashcode to allow for that.
            //
            // Example:
            // PathFontStyle uses GL_NONE which is not marked as bitmask
            // but other entries such as GL_BOLD_BIT_NV is marked as bitmask.
            //
            // When this case happens we want to consider the entire groupName as a bitmask.
            //
            // In the current spec this case only happens for PathFontStyle.
            // - 2021-07-04
            public bool Equals(EnumGroupInfo? other) =>
                other?.GroupName == GroupName;

            public override int GetHashCode() =>
                HashCode.Combine(GroupName);
        };

        internal static OutputData ProcessSpec(Specification spec, Documentation docs)
        {
            // The first thing we do is process all of the vendorFunctions defined into a dictionary of Functions.
            Dictionary<string, OverloadedFunction> allFunctions = new Dictionary<string, OverloadedFunction>(spec.Functions.Count);
            foreach (Function nativeFunction in spec.Functions)
            {
                var dependency = nativeFunction.OriginalEntryPoint ?? "";
                Dictionary<OutputApi, CommandDocumentation> functionDocumentation = MakeDocumentationForNativeFunction(nativeFunction, docs, dependency);
                OverloadedFunction overloadedFunction = GenerateOverloads(nativeFunction, functionDocumentation);

                allFunctions.Add(nativeFunction.EntryPoint, overloadedFunction);
            }

            Dictionary<OutputApi, Dictionary<string, EnumGroupMember>> allEnumsPerAPI = new Dictionary<OutputApi, Dictionary<string, EnumGroupMember>>();
            Dictionary<OutputApi, HashSet<EnumGroupInfo>> allEnumGroups = new Dictionary<OutputApi, HashSet<EnumGroupInfo>>();
            foreach (OutputApi outputApi in Enum.GetValues<OutputApi>())
            {
                if (outputApi == OutputApi.Invalid) continue;
                allEnumsPerAPI.Add(outputApi, new Dictionary<string, EnumGroupMember>());
                allEnumGroups.Add(outputApi, new HashSet<EnumGroupInfo>());
            }

            foreach (EnumEntry @enum in spec.Enums)
            {
                bool isFlag = @enum.IsFlags;

                foreach ((string originalName, string translatedName, APIFile @namespace) in @enum.Groups)
                {
                    switch (@namespace)
                    {
                        case APIFile.AL:
                            AddToGroup(allEnumGroups, OutputApi.AL, originalName, translatedName, isFlag);
                            break;
                        case APIFile.ALC:
                            AddToGroup(allEnumGroups, OutputApi.ALC, originalName, translatedName, isFlag);
                            break;
                        default:
                            throw new Exception();
                    }

                    static void AddToGroup(Dictionary<OutputApi, HashSet<EnumGroupInfo>> allEnumGroups, OutputApi api, string originalName, string translatedName, bool isFlag)
                    {
                        // If the first groupNameToEnumGroup tag wasn't flagged as a bitmask, but later ones in the same groupName are.
                        // Then we want the groupName to be considered a bitmask.
                        if (allEnumGroups[api].TryGetValue(new EnumGroupInfo(originalName, translatedName, isFlag), out EnumGroupInfo? actual))
                        {
                            // In the current spec this case never happens, but it could.
                            // - 2021-07-04
                            if (isFlag == true && actual.IsFlags == false)
                            {
                                allEnumGroups[api].Remove(actual);
                                allEnumGroups[api].Add(actual with { IsFlags = true });
                            }
                        }
                        else
                        {
                            allEnumGroups[api].Add(new EnumGroupInfo(originalName, translatedName, isFlag));
                        }
                    }
                }

                EnumGroupMember data = new EnumGroupMember(@enum.Name, @enum.MangledName, @enum.Value, @enum.Groups, isFlag);

                if (@enum.Apis == OutputApiFlags.None)
                {
                    throw new Exception();
                }

                if (@enum.Apis.HasFlag(OutputApiFlags.AL))
                {
                    allEnumsPerAPI.AddToNestedDictIfNotPresent(OutputApi.AL, @enum.Name, data);
                }

                if (@enum.Apis.HasFlag(OutputApiFlags.ALC))
                {
                    allEnumsPerAPI.AddToNestedDictIfNotPresent(OutputApi.ALC, @enum.Name, data);
                }
            }

            // Resolve cross referenced enums between APIs
            foreach (var (api, _, enums) in spec.APIs)
            {
                OutputApi outAPI = api switch
                {
                    InputAPI.AL => OutputApi.AL,
                    InputAPI.ALC => OutputApi.ALC,

                    _ => throw new Exception(),
                };

                // FIXME: Do we need this here?
                APIFile file = api switch
                {
                    InputAPI.AL => APIFile.AL,
                    InputAPI.ALC => APIFile.ALC,

                    _ => throw new Exception(),
                };

                bool removeFunctions = outAPI switch
                {
                    OutputApi.AL => true,
                    OutputApi.ALC => true,
                    _ => false,
                };

                Dictionary<string, EnumGroupMember>? enumsDict = allEnumsPerAPI[outAPI];

                foreach (EnumReference enumRef in enums)
                {
                    if (enumRef.IsCrossReferenced)
                        continue;

                    if (removeFunctions)
                    {
                        // FIXME: Should we check the profile of the extension??
                        if (enumRef.VersionInfo.RemovedBy.Count > 0)
                        {
                            // FIXME: Add the enum if an extension uses it??
                            continue;
                        }
                    }

                    // FIXME! This is a big hack!
                    // We don't want to process this "enum" as it is a string.
                    if (enumRef.EnumName == "GLX_EXTENSION_NAME") continue;

                    if (enumsDict.TryGetValue(enumRef.EnumName, out EnumGroupMember? @enum))
                    {
                        foreach (var groupRef in @enum.Groups)
                        {
                            APIFile @namespace = groupRef.Namespace;
                            if (@namespace != file)
                            {
                                switch (@namespace)
                                {
                                    case APIFile.AL:
                                        AddEnumToAPI(OutputApi.AL, @enum);
                                        break;
                                    case APIFile.ALC:
                                        AddEnumToAPI(OutputApi.ALC, @enum);
                                        break;
                                    default:
                                        throw new Exception();
                                }

                                void AddEnumToAPI(OutputApi outputApi, EnumGroupMember @enum)
                                {
                                    // FIXME: There is an issue where a cross referenced enum gets readded here.
                                    // We want to avoid this.

                                    if (allEnumsPerAPI[outputApi].ContainsKey(@enum.Name) == false)
                                    {
                                        allEnumsPerAPI.AddToNestedDict(outputApi, @enum.Name, @enum);
                                    }

                                    foreach (var api in spec.APIs)
                                    {
                                        if (MatchesAPI(api.Name, outputApi))
                                        {
                                            api.Enums.Add(new EnumReference(@enum.Name, new VersionInfo(null, []), true));
                                            Logger.Info($"Added enum entry '{@enum.MangledName}' to {outputApi}.");
                                        }
                                    }

                                    AddToGroup(allEnumGroups, outputApi, groupRef, @enum.IsFlag);

                                    static bool MatchesAPI(InputAPI api, OutputApi output)
                                    {
                                        switch (api)
                                        {
                                            case InputAPI.AL: return output == OutputApi.AL;
                                            case InputAPI.ALC: return output == OutputApi.ALC;
                                            default: throw new Exception();
                                        }
                                    }

                                    // FIXME: Duplicate implementation, see above.
                                    static void AddToGroup(Dictionary<OutputApi, HashSet<EnumGroupInfo>> allEnumGroups, OutputApi api, GroupRef @ref, bool isFlag)
                                    {
                                        // If the first groupNameToEnumGroup tag wasn't flagged as a bitmask, but later ones in the same groupName are.
                                        // Then we want the groupName to be considered a bitmask.
                                        if (allEnumGroups[api].TryGetValue(new EnumGroupInfo(@ref.OriginalName, @ref.TranslatedName, isFlag), out EnumGroupInfo? actual))
                                        {
                                            // In the current spec this case never happens, but it could.
                                            // - 2021-07-04
                                            if (isFlag == true && actual.IsFlags == false)
                                            {
                                                allEnumGroups[api].Remove(actual);
                                                allEnumGroups[api].Add(actual with { IsFlags = true });
                                            }
                                        }
                                        else
                                        {
                                            allEnumGroups[api].Add(new EnumGroupInfo(@ref.OriginalName, @ref.TranslatedName, isFlag));
                                        }
                                    }
                                }
                            }
                        }
                    }
                    else
                    {
                        throw new Exception($"Could not find any enum called '{enumRef.EnumName}'.");
                    }
                }
            }

            List<Namespace> outputNamespaces = new List<Namespace>();

            foreach (var (api, functions, enums) in spec.APIs)
            {
                // FIXME: Probably make these the same enum!
                OutputApi outAPI = api switch
                {
                    InputAPI.AL => OutputApi.AL,
                    InputAPI.ALC => OutputApi.ALC,

                    _ => throw new Exception(),
                };

                // FIXME: Do we need this here?
                APIFile file = api switch
                {
                    InputAPI.AL => APIFile.AL,
                    InputAPI.ALC => APIFile.ALC,

                    _ => throw new Exception(),
                };

                outputNamespaces.Add(CreateOutputAPI(outAPI, file));

                Namespace CreateOutputAPI(OutputApi outAPI, APIFile alFile)
                {
                    // Function processing

                    bool removeFunctions = outAPI switch
                    {
                        OutputApi.AL => true,
                        OutputApi.ALC => true,
                        _ => false,
                    };

                    HashSet<GroupRef> groupsReferencedByFunctions = new HashSet<GroupRef>();
                    Dictionary<string, HashSet<OverloadedFunction>> functionsByVendor = new Dictionary<string, HashSet<OverloadedFunction>>();
                    foreach (var functionRef in functions)
                    {
                        if (allFunctions.TryGetValue(functionRef.EntryPoint, out OverloadedFunction? overloadedFunction))
                        {
                            bool referenced = false;

                            if (functionRef.VersionInfo.Version != null)
                            {
                                if (removeFunctions && (functionRef.VersionInfo.RemovedBy.Count > 0))
                                {
                                    // Do not add this function
                                }
                                else
                                {
                                    functionsByVendor.AddToNestedHashSet("", overloadedFunction);

                                    referenced = true;
                                }
                            }

                            foreach (var extension in functionRef.VersionInfo.Extensions)
                            {
                                functionsByVendor.AddToNestedHashSet(extension.Vendor, overloadedFunction);

                                referenced = true;
                            }

                            if (referenced)
                            {
                                groupsReferencedByFunctions.UnionWith(overloadedFunction.NativeFunction.ReferencedEnumGroups);
                            }
                        }
                        else
                        {
                            // FIXME!
                            /*if (GeneratorSettings.Settings.IgnoreFunctions.Contains(functionRef.EntryPoint))
                            {
                                // We are ignoring this function.
                            }
                            else
                            {
                                throw new Exception($"Could not find function '{functionRef.EntryPoint}'!");
                            }*/
                        }
                    }

                    // Go through all vendorFunctions and build up a Dictionary from enumName groups to function using them
                    Dictionary<GroupRef, List<(string Vendor, Function Function)>> enumGroupToNativeFunctionsUsingThatEnumGroup = [];
                    Dictionary<string, VendorFunctions> vendors = [];
                    foreach (var (vendor, vendorFunctions) in functionsByVendor)
                    {
                        foreach (var function in vendorFunctions)
                        {
                            if (!vendors.TryGetValue(vendor, out VendorFunctions? group))
                            {
                                group = new VendorFunctions(vendor, new List<Process.OverloadedFunction>(), new HashSet<Function>());
                                vendors.Add(vendor, group);
                            }

                            var nativeFunction = function.NativeFunction;
                            if (!nativeFunction.IsNative) continue;
                            group.Functions.Add(new Process.OverloadedFunction(nativeFunction, function.Overloads));

                            if (function.ChangeNativeName)
                            {
                                group.NativeFunctionsWithPostfix.Add(nativeFunction);
                            }

                            foreach (var enumGroup in nativeFunction.ReferencedEnumGroups)
                            {
                                if (enumGroupToNativeFunctionsUsingThatEnumGroup.TryGetValue(enumGroup, out var listOfFunctions) == false)
                                {
                                    listOfFunctions = new List<(string Vendor, Function Function)>();
                                    enumGroupToNativeFunctionsUsingThatEnumGroup.Add(enumGroup, listOfFunctions);
                                }

                                if (listOfFunctions.Contains((vendor, NativeFunction: nativeFunction)) == false)
                                {
                                    listOfFunctions.Add((vendor, NativeFunction: nativeFunction));
                                }
                            }
                        }
                    }
                    Dictionary<string, string> vendorsByEntryPoint = vendors.SelectMany(a => a.Value.Functions.Select(b => b.NativeFunction.EntryPoint).Select(c => (c, a.Key))).DistinctBy(a => a.c).ToDictionary(a => a.c, a => a.Key);

                    List<VendorFunctions> sortedVendorFunctions = [.. vendors.Values];
                    foreach (VendorFunctions functions in sortedVendorFunctions)
                    {
                        functions.Functions.Sort();
                    }
                    sortedVendorFunctions.Sort((e1, e2) => e1.Vendor.CompareTo(e2.Vendor));
                    var contextParameterDocumentation = new ParameterDocumentation("context", "The ALC context to access.");
                    Dictionary<Function, FunctionDocumentation> documentation = new Dictionary<Function, FunctionDocumentation>();
                    foreach (var (vendor, vendorFunctions) in functionsByVendor.OrderBy(a => a.Key == "Direct" ? 1 : 0))
                    {
                        foreach (var function in vendorFunctions.OrderBy(a => a.NativeFunction.IsNative ? 1 : 0))
                        {
                            FunctionReference func = functions.Find(f => f.EntryPoint == function.NativeFunction.EntryPoint) ?? throw new Exception($"Could not find function {function.NativeFunction.EntryPoint}!");

                            List<string> addedIn = new List<string>();
                            if (func.VersionInfo.Version != null)
                            {
                                addedIn.Add($"v{func.VersionInfo.Version.Major}.{func.VersionInfo.Version.Minor}");
                            }

                            foreach (var extension in func.VersionInfo.Extensions)
                            {
                                addedIn.Add(extension.Name);
                            }

                            List<string> removedIn = new List<string>();
                            if (func.VersionInfo.RemovedBy.Count > 0)
                            {
                                // FIXME: We only handle one RemovedBy entry for now.
                                Debug.Assert(func.VersionInfo.RemovedBy.Count == 1);

                                // In OpenGL only feature versions can remove so we can use the version straight.
                                // - Noggin_bops 2025-08-11
                                Version removedInV = func.VersionInfo.RemovedBy[0].Version!;

                                removedIn.Add($"v{removedInV.Major}.{removedInV.Minor}");
                            }

                            List<string> extensionURLs = [];
                            foreach (var extension in func.VersionInfo.Extensions)
                            {
                                string ext = NameMangler.MaybeRemoveStart(extension.Name, "GL_");

                                // FIXME: This does not work super well here as there are
                                // ALC extensions like ALC_EXT_EFX that have many functions
                                // added to the AL api, which means we will choose the wrong
                                // api string in the url here.
                                // - Noggin_bops 2025-08-10
                                string apiString = api switch
                                {
                                    InputAPI.AL => "AL%20Extensions",
                                    InputAPI.ALC => "ALC%20Extensions",
                                    _ => throw new Exception()
                                };

                                string url = $"https://raw.githubusercontent.com/Raulshc/OpenAL-EXT-Repository/refs/heads/master/{apiString}/{ext}.txt";
                                extensionURLs.Add(url);
                            }

                            if (function.Documentation.TryGetValue(outAPI, out CommandDocumentation? commandDocumentation))
                            {
                                // FIXME: Added and removed information.
                                documentation[function.NativeFunction] = new FunctionDocumentation(
                                    commandDocumentation.Name,
                                    commandDocumentation.Purpose,
                                    commandDocumentation.Parameters,
                                    [commandDocumentation.RefPagesLink, .. extensionURLs],
                                    addedIn,
                                    removedIn,
                                    commandDocumentation.Dependency
                                    );
                            }
                            else
                            {
                                switch (vendor)
                                {
                                    case "Direct" when allFunctions.TryGetValue(function.NativeFunction.OriginalEntryPoint ?? "", out var dependency):
                                        // Auto-generated documentation for AL_EXT_direct_context functions
                                        var dependencyFunctionDocumentation = documentation[dependency.NativeFunction];
                                        var dependentVendor = vendorsByEntryPoint.TryGetValue(function.NativeFunction.OriginalEntryPoint ?? "", out var k) ? k : "";
                                        var dependencyAddedIn = dependencyFunctionDocumentation.AddedIn;
                                        dependencyAddedIn.Sort();
                                        var parameters = function.NativeFunction.Parameters.Any(a => a.OriginalName == "context" && a.StrongType is CSStructPrimitive primitive && primitive.StructName == "ALCContext")
                                            ? [contextParameterDocumentation, .. dependencyFunctionDocumentation.Parameters] : dependencyFunctionDocumentation.Parameters;
                                        var newDocumentation = new FunctionDocumentation(
                                            function.NativeFunction.EntryPoint,
                                            dependencyFunctionDocumentation.Purpose,
                                            parameters,
                                            [.. extensionURLs, .. dependencyFunctionDocumentation.RefPagesLinks],
                                            addedIn,
                                            removedIn,
                                            string.Join(" | ", dependencyAddedIn),
                                            dependentVendor);
                                        documentation[function.NativeFunction] = newDocumentation;
                                        foreach (var item in function.Overloads.Where(a => !a.NativeFunction.IsNative))
                                        {
                                            documentation.TryAdd(item.NativeFunction, newDocumentation);
                                        }
                                        break;
                                    default:
                                        if (string.IsNullOrEmpty(vendor))
                                        {
                                            Logger.Warning($"{function.NativeFunction.EntryPoint} doesn't have any documentation for {api}");
                                        }
                                        // Extensions don't have documentation (yet?)
                                        documentation[function.NativeFunction] = new FunctionDocumentation(
                                            function.NativeFunction.EntryPoint,
                                            "",
                                            [],
                                            // TODO: Is it possible to get the extension spec file and link to it here?
                                            extensionURLs,
                                            addedIn,
                                            removedIn);
                                        break;
                                }
                            }
                        }
                    }

                    // Enum processing

                    Dictionary<string, EnumGroupMember>? enumsDict = allEnumsPerAPI[outAPI];

                    Dictionary<string, List<EnumGroupMember>> groupNameToEnumGroup = new Dictionary<string, List<EnumGroupMember>>();

                    HashSet<EnumGroupMember> theAllEnumGroup = new HashSet<EnumGroupMember>();

                    // FIXME: Here we are trusting that the enum refs in the <require> tags tell us all of the
                    // enums to include. But this is not necessarily true as is the case with WGL as it references
                    // some enums from OpenGL without them going through the require tag...
                    // - Noggin_bops 2023-08-26
                    foreach (var enumRef in enums)
                    {
                        if (removeFunctions)
                        {
                            // FIXME: Should we check the profile of the extension??
                            if (enumRef.VersionInfo.RemovedBy.Count > 0)
                            {
                                // FIXME: Add the enum if an extension uses it??
                                continue;
                            }
                        }

                        if (enumsDict.TryGetValue(enumRef.EnumName, out EnumGroupMember? @enum))
                        {
                            foreach (var (originalName, translatedName, @namespace) in @enum.Groups)
                            {
                                if (@namespace != alFile)
                                    continue;

                                if (groupNameToEnumGroup.TryGetValue(translatedName, out List<EnumGroupMember>? groupMembers) == false)
                                {
                                    groupMembers = new List<EnumGroupMember>();
                                    groupNameToEnumGroup.Add(translatedName, groupMembers);
                                }

                                if (groupMembers.Find(g => g.MangledName == @enum.MangledName) == null)
                                {
                                    groupMembers.Add(@enum);
                                }
                            }

                            if (@enum.Value <= uint.MaxValue)
                            {
                                theAllEnumGroup.Add(@enum);
                            }
                        }
                        else
                        {
                            throw new Exception($"Could not find any enum called '{enumRef.EnumName}'.");
                        }
                    }

                    // Go through all of the groupNameToEnumGroup and put them into their groups

                    // Add keys + lists for all enumName names
                    List<EnumGroup> finalGroups = new List<EnumGroup>();
                    foreach ((string originalName, string translatedName, bool isFlags) in allEnumGroups[outAPI])
                    {
                        if (groupNameToEnumGroup.TryGetValue(translatedName, out List<EnumGroupMember>? members) == false)
                        {
                            members = [];
                            groupNameToEnumGroup.Add(translatedName, members);
                        }

                        // SpecialNumbers is not an enumName groupName that we want to output.
                        // We handle these entries differently as some of the entries don't fit in an int.
                        if (originalName == "SpecialNumbers")
                            continue;

                        // Remove all empty enumName groups, except the empty groups referenced by included vendorFunctions.
                        // In GL 4.1 to 4.5 there are vendorFunctions that use the groupName "ShaderBinaryFormat"
                        // while not including any members for that enumName groupName.
                        // This is needed to solve that case.
                        if (members.Count <= 0 && groupsReferencedByFunctions.Contains(new GroupRef(originalName, translatedName, alFile)) == false)
                            continue;

                        if (enumGroupToNativeFunctionsUsingThatEnumGroup.TryGetValue(new GroupRef(originalName, translatedName, alFile), out var functionsUsingEnumGroup) == false)
                        {
                            functionsUsingEnumGroup = null;
                        }

                        // If there is a list, sort it by name
                        if (functionsUsingEnumGroup != null)
                            functionsUsingEnumGroup.Sort((f1, f2) =>
                            {
                                // We want to prioritize "core" vendorFunctions before extensions.
                                if (f1.Vendor == "" && f2.Vendor != "") return -1;
                                if (f1.Vendor != "" && f2.Vendor == "") return 1;

                                return f1.Function.Name.CompareTo(f2.Function.Name);
                            });

                        members.Sort(EnumGroupMember.DefaultComparison);

                        finalGroups.Add(new EnumGroup(translatedName, isFlags, members, functionsUsingEnumGroup));
                    }
                    foreach (var group in groupsReferencedByFunctions)
                    {
                        // This group is not part of this file, so we can't do anything here about adding it.
                        // For now this is not a problem as all referenced groups from between the different
                        // files are always populated, so we will never have to add them to the other file.
                        // - Noggin_bops 2025-08-05
                        if (group.Namespace != file)
                        {
                            continue;
                        }

                        if (groupNameToEnumGroup.TryGetValue(group.TranslatedName, out List<EnumGroupMember>? members) == false)
                        {
                            if (enumGroupToNativeFunctionsUsingThatEnumGroup.TryGetValue(group, out var functionsUsingEnumGroup) == false)
                            {
                                functionsUsingEnumGroup = null;
                            }

                            finalGroups.Add(new EnumGroup(group.TranslatedName, false, [], functionsUsingEnumGroup));
                        }
                    }

                    // Sort enum groups be name
                    finalGroups.Sort((g1, g2) => g1.Name.CompareTo(g2.Name));

                    List<EnumGroupMember> allEnumGroup = theAllEnumGroup.ToList();
                    allEnumGroup.Sort(EnumGroupMember.DefaultComparison);

                    // Add the All enum group first.
                    finalGroups.Insert(0, new EnumGroup("All", false, allEnumGroup, null));

                    return new Namespace(outAPI, sortedVendorFunctions, finalGroups, documentation);
                }
            }

            // FIXME: This requires us to merge all input data!
            List<Pointers> pointers = new List<Pointers>();
            pointers.Add(CreatePointersList(APIFile.AL, outputNamespaces));
            pointers.Add(CreatePointersList(APIFile.ALC, outputNamespaces));

            return new OutputData(pointers, outputNamespaces);

            Pointers CreatePointersList(APIFile file, List<Namespace> namespaces)
            {
                SortedList<string, Function> allFunctions = [];
                foreach (Namespace @namespace in namespaces)
                {
                    bool addFunctions = false;
                    switch (file)
                    {
                        case APIFile.AL:
                            if (@namespace.Name == OutputApi.AL)
                            {
                                addFunctions = true;
                            }
                            break;
                        case APIFile.ALC:
                            if (@namespace.Name == OutputApi.ALC)
                            {
                                addFunctions = true;
                            }
                            break;
                        default:
                            throw new Exception();
                    }

                    if (addFunctions)
                    {
                        foreach (var functions in @namespace.VendorFunctions)
                        {
                            foreach (var function in functions.Functions.Select(a => a.NativeFunction).Where(function => function.IsNative && !allFunctions.ContainsKey(function.EntryPoint)))
                            {
                                allFunctions.Add(function.EntryPoint, function);
                            }
                        }
                    }
                }

                return new Pointers(file, allFunctions.Values.ToList());
            }
        }

        internal static Dictionary<OutputApi, CommandDocumentation> MakeDocumentationForNativeFunction(Function function, Documentation documentation, string dependency = "")
        {
            Dictionary<OutputApi, CommandDocumentation> commandDocs = new Dictionary<OutputApi, CommandDocumentation>();

            foreach (var (version, versionDocumentation) in documentation.VersionDocumentation)
            {
                if (versionDocumentation.Commands.TryGetValue(function.EntryPoint, out CommandDocumentation? commandDoc))
                {
                    if (function.Parameters.Count != commandDoc.Parameters.Length)
                    {
                        Logger.Warning($"Function {function.EntryPoint} has differnet number of parameters than the parsed documentation. (gl.xml:{function.Parameters.Count}, documentation:{commandDoc.Parameters.Length})");
                    }

                    for (int i = 0; i < Math.Min(function.Parameters.Count, commandDoc.Parameters.Length); i++)
                    {
                        if (function.Parameters[i].OriginalName != commandDoc.Parameters[i].Name)
                        {
                            Logger.Warning($"[{version}][{function.EntryPoint}] Function parameter '{function.Parameters[i].OriginalName}' doesn't have the same name in the documentation. ('{commandDoc.Parameters[i].Name}')");
                        }
                    }

                    commandDocs.Add(version, string.IsNullOrEmpty(dependency) ? commandDoc : commandDoc with { Dependency = dependency });
                }
            }

            return commandDocs;
        }

        public static readonly IOverloader[] Overloaders = [
            new TrimNameOverloader(TrimNameOverloader.EndingsNotToTrimOpenAL),

            new StringReturnOverloader(),
            new BoolReturnOverloader(),

            new ColorTypeOverloader(),
            new MathTypeOverloader(),
            new FunctionPtrToDelegateOverloader(),
            new PointerToOffsetOverloader(),
            new VoidPtrToIntPtrOverloader(),
            new GenCreateAndDeleteOverloader(
                GenCreateAndDeleteOverloader.PluralNameToSingularNameOpenAL,
                GenCreateAndDeleteOverloader.PluralParameterNameToSingularNameOpenAL),
            new ExplicitLengthSpanOverloader(),
            new StringOverloader(),
            new StringArrayOverloader(),
            new SpanAndArrayOverloader(),
            new RefInsteadOfPointerOverloader(),
            new OutToReturnOverloader(),
            new SpanInsteadOfReadOnlySpanOverloader(),
        ];

        // Maybe we can do the return type overloading in a post processing step?
        internal static OverloadedFunction GenerateOverloads(Function nativeFunction, Dictionary<OutputApi, CommandDocumentation> functionDocumentation)
        {
            // Make a "base" overload
            List<Overload> overloads = [Overload.CreateBaseOverload(nativeFunction)];

            bool hasOverloads = false;
            foreach (IOverloader overloader in Overloaders)
            {
                List<Overload> newOverloads = new List<Overload>();
                foreach (Overload overload in overloads)
                {
                    if (overloader.TryGenerateOverloads(overload, out List<Overload>? overloaderOverloads))
                    {
                        hasOverloads = true;

                        newOverloads.AddRange(overloaderOverloads);
                    }
                    else
                    {
                        newOverloads.Add(overload);
                    }
                }
                // Replace the old overloads with the new overloads
                overloads = newOverloads;
            }
            Overload[] overloadArray = hasOverloads ? overloads.ToArray() : [];

            bool changeNativeName = false;
            foreach (Overload overload in overloadArray)
            {
                if (AreSignaturesDifferent(nativeFunction, overload) == false)
                {
                    changeNativeName = true;
                }
            }

            return new OverloadedFunction(nativeFunction, functionDocumentation, overloadArray, changeNativeName);

            static bool AreSignaturesDifferent(Function nativeFunction, Overload overload)
            {
                if (nativeFunction.Parameters.Count(a => !a.Optional) != overload.InputParameters.Count(a => !a.Optional))
                {
                    return true;
                }

                if (overload.OverloadName != nativeFunction.Name)
                {
                    return true;
                }

                for (int i = 0; i < nativeFunction.Parameters.Count; i++)
                {
                    if (nativeFunction.Parameters[i].StrongType!.Equals(overload.InputParameters[i].StrongType!) == false)
                    {
                        return true;
                    }
                }

                return false;
            }
        }
    }
}
