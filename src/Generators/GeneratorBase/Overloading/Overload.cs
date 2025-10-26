using System;

namespace GeneratorBase.Overloading
{
    public record Overload(
        Overload? NestedOverload,
        IOverloadLayer? MarshalLayerToNested,
        Parameter[] InputParameters,
        Function NativeFunction,
        BaseCSType ReturnType,
        NameTable NameTable,
        string[] GenericTypes,
        string OverloadName)
    {
        public int OverloadResolutionPriority => (MarshalLayerToNested?.OverloadResolutionPriority ?? 0) + (NestedOverload?.OverloadResolutionPriority ?? 0);

        // Used to define overloads that call another overloads
        public Function AsFunction() => new()
        {
            Name = OverloadName,
            EntryPoint = OverloadName,
            Parameters = [.. InputParameters],
            ReturnType = ReturnType.ToCSString(),
            StrongReturnType = ReturnType,
            CommandType = NativeFunction.CommandType,
            OriginalEntryPoint = NativeFunction.OriginalEntryPoint,
            ReferencedEnumGroups = NativeFunction.ReferencedEnumGroups,
            VersionInfo = NativeFunction.VersionInfo,
            GenericTypes = GenericTypes,
            IsNative = false
        };

        // Used to define overloads that call another overloads
        public Overload AsBaseOverload()
            => new(null, null, [.. InputParameters], AsFunction(), ReturnType, new NameTable(), GenericTypes, OverloadName);

        public static Overload CreateBaseOverload(Function function)
            => new Overload(null, null, [.. function.Parameters], function, function.StrongReturnType ?? throw new ArgumentException($"{nameof(function)}.{nameof(Function.StrongReturnType)} has not yet been set!", nameof(function)),
                new NameTable(), [.. function.GenericTypes ?? []], function.Name);
    }
}
