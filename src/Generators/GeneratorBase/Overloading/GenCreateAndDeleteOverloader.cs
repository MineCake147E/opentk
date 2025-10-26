using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

using GeneratorBase.Utility;
using GeneratorBase.Utility.Extensions;

namespace GeneratorBase.Overloading
{
    public class GenCreateAndDeleteOverloader : IOverloader
    {
        internal static readonly string[] Prefixes = new string[] { "Gen", "Create", "Delete" };

        public static readonly Dictionary<string, string> PluralNameToSingularNameOpenGL = new Dictionary<string, string>()
        {
            { "Queries", "Query" },
            { "TransformFeedbacks", "TransformFeedback" },
            { "VertexArrays", "VertexArray" },
            { "Textures", "Texture" },
            { "Samplers", "Sampler" },
            { "Renderbuffers", "Renderbuffer" },
            { "ProgramPipelines", "ProgramPipeline" },
            { "Framebuffers", "Framebuffer" },
            { "Buffers", "Buffer" },
        };

        public static readonly Dictionary<string, string> PluralParameterNameToSingularNameOpenGL = new Dictionary<string, string>()
        {
            { "ids", "id" },
            { "arrays", "array" },
            { "textures", "texture" },
            { "samplers", "sampler" },
            { "renderbuffers", "renderbuffer" },
            { "pipelines", "pipeline" },
            { "framebuffers", "framebuffer" },
            { "buffers", "buffer" },
        };

        public static readonly Dictionary<string, string> PluralNameToSingularNameOpenAL = new Dictionary<string, string>()
        {
            { "Sources", "Source" },
            { "Buffers", "Buffer" },
            { "Effects", "Effect" },
            { "Filters", "Filter" },
            { "AuxiliaryEffectSlots", "AuxiliaryEffectSlot" },
            { "SourcesDirect", "SourceDirect" },
            { "BuffersDirect", "BufferDirect" },
            { "EffectsDirect", "EffectDirect" },
            { "FiltersDirect", "FilterDirect" },
            { "AuxiliaryEffectSlotsDirect", "AuxiliaryEffectSlotDirect" },
        };

        public static readonly Dictionary<string, string> PluralParameterNameToSingularNameOpenAL = new Dictionary<string, string>()
        {
            { "sources", "source" },
            { "buffers", "buffer" },
            { "effects", "effect" },
            { "filters", "filter" },
            { "effectslots", "effectslot" },
        };

        internal Dictionary<string, string> PluralNameToSingular { get; }

        internal Dictionary<string, string> PluralParameterNameToSingular { get; }

        public GenCreateAndDeleteOverloader(Dictionary<string, string> pluralNameToSingular, Dictionary<string, string> pluralParameterNameToSingular)
        {
            PluralNameToSingular = pluralNameToSingular;
            PluralParameterNameToSingular = pluralParameterNameToSingular;
        }

        public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
        {
            var nativeName = overload.NativeFunction.Name;
            if ((!nativeName.StartsWith("Create") && !nativeName.StartsWith("Gen") && !nativeName.StartsWith("Delete")) ||
                (!nativeName.EndsWith('s') && !nativeName.EndsWith("sDirect")))
            {
                newOverloads = default;
                return false;
            }

            // Here we assume that the last parameter is the pointer parameter.
            var pointerParameter = overload.InputParameters.LastOrDefault();

            if (pointerParameter == null || pointerParameter.StrongType is not CSPointer pointerParameterType)
            {
                newOverloads = default;
                return false;
            }

            if (pointerParameter.StrongLength == null || pointerParameter.StrongLength is not ParameterReferenceExpression handleLength)
            {
                newOverloads = default;
                return false;
            }

            string? namePrefix = null;
            string? nameWithoutPrefix = null;
            foreach (var prefix in Prefixes)
            {
                if (nativeName.StartsWith(prefix))
                {
                    namePrefix = prefix;
                    nameWithoutPrefix = nativeName[prefix.Length..];
                }
            }

            if (nameWithoutPrefix == null || namePrefix == null)
            {
                throw new Exception($"Function name '{nativeName}' doesn't start with Gen/Create/Delete and cannot be overloaded by this overloader.");
            }

            string newName;
            if (PluralNameToSingular.TryGetValue(nameWithoutPrefix, out string? newPostfix))
            {
                newName = $"{namePrefix}{newPostfix}";
            }
            else
            {
                // If the name didn't have a custom singular name, we just remove the trailing 's'
                newName = nativeName;
                Logger.Warning($"Function '{nativeName}' ({nameWithoutPrefix}) {nameWithoutPrefix[..^1]} needs a depluralized name.");
            }

            int lengthParameterIndex = -1;
            Parameter[] parameters = new Parameter[overload.InputParameters.Length - 1];
            for (var i = 0; i < overload.InputParameters.Length - 1; i++)
            {
                var parameter = overload.InputParameters[i];
                if (parameter.Name.Equals(handleLength.ParameterName))
                {
                    lengthParameterIndex = i;
                }
                else
                {
                    parameters[lengthParameterIndex != -1 ? i + 1 : i] = parameter;
                }
            }

            if (lengthParameterIndex == -1)
            {
                throw new Exception($"Couldnt find len {handleLength.ParameterName} on method {nativeName}");
            }

            string? newPointerParameterName;
            if (PluralParameterNameToSingular.TryGetValue(pointerParameter.Name, out newPointerParameterName) == false)
            {
                newPointerParameterName = pointerParameter.Name;
                Logger.Warning($"Parameter '{pointerParameter.Name}' needs a depluralized name!");
            }

            var nameTable = overload.NameTable.New();
            nameTable.Rename(pointerParameter, $"{pointerParameter.Name}_handle");
            nameTable.MarkFixed(overload.InputParameters[lengthParameterIndex]);

            IOverloadLayer layer;
            if (nativeName.StartsWith("Delete"))
            {
                parameters[^1] = pointerParameter with
                {
                    // Remove ending 's' in parameter name.
                    // This works for Queries/Query because the parameter names in these functions is "ids
                    // - 2022-06-27
                    Name = newPointerParameterName,
                    StrongType = pointerParameterType.BaseType,
                    StrongLength = null
                };
                layer = new DeleteOverloadLayer(overload.InputParameters[lengthParameterIndex], parameters[^1], pointerParameter);
            }
            else
            {
                parameters[^1] = pointerParameter with
                {
                    // Remove ending 's' in parameter name.
                    // This works for Queries/Query because the parameter names in these functions is "ids
                    // - 2022-06-27
                    Name = newPointerParameterName,
                    StrongType = new CSRef(CSRef.Type.Out, pointerParameterType.BaseType),
                    StrongLength = null
                };
                layer = new GenAndCreateOverloadLayer(overload.InputParameters[lengthParameterIndex], parameters[^1], pointerParameter);
            }

            newOverloads = new List<Overload>()
            {
                overload with
                {
                    InputParameters = parameters, NestedOverload = overload, OverloadName = newName,
                    NameTable = nameTable,
                    MarshalLayerToNested = layer
                },
                overload,
            };
            return true;
        }

        private record DeleteOverloadLayer(
            Parameter LengthParameter,
            Parameter InParameter,
            Parameter PointerParameter) : IOverloadLayer
        {
            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                writer.WriteLine($"{LengthParameter.StrongType!.ToCSString()} {nameTable[LengthParameter]} = 1;");
                writer.WriteLine($"{PointerParameter.StrongType!.ToCSString()} {nameTable[PointerParameter]} = &{nameTable[InParameter]};");
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                return returnName;
            }
        }

        private record GenAndCreateOverloadLayer(
            Parameter LengthParameter,
            Parameter OutParameter,
            Parameter PointerParameter) : IOverloadLayer
        {
            private CsScope? _csScope = null;

            public void WritePrologue(IndentedTextWriter writer, NameTable nameTable)
            {
                writer.WriteLine($"{LengthParameter.StrongType!.ToCSString()} {nameTable[LengthParameter]} = 1;");
                writer.WriteLine($"Unsafe.SkipInit(out {nameTable[OutParameter]});");
                if (nameTable.IsFixed(OutParameter))
                {
                    writer.WriteLine($"{PointerParameter.StrongType!.ToCSString()} {nameTable[PointerParameter]} = &{nameTable[OutParameter]};");
                    _csScope = null;
                }
                else
                {
                    writer.WriteLine($"fixed({PointerParameter.StrongType!.ToCSString()} {nameTable[PointerParameter]} = &{nameTable[OutParameter]})");
                    _csScope = writer.CsScope();
                }
            }

            public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName)
            {
                if (_csScope.HasValue)
                {
                    _csScope.Value.Dispose();
                }

                return returnName;
            }
        }
    }
}
