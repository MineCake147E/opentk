using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Text.RegularExpressions;

namespace GeneratorBase.Overloading
{
    public partial class TrimNameOverloader : IOverloader
    {

        [GeneratedRegex(@"(u?[sb](64)?v?|v|i_v|fi)$", RegexOptions.Compiled)]
        private static partial Regex GeneratedEndings();

        [GeneratedRegex("(sh|ib|[tdrey]s|[eE]n[vd]|bled|Attrib|Address|Access|Boolean|Bitmaps|Coord|Depth|Feedbacks|Finish|Flag|Groups|IDs|Indexed|Instanced|Pixels|Queries|Status|Tess|Through|Uniforms|Varyings|Weight|Width|[1-4][fdhi]v)$", RegexOptions.Compiled)]
        private static partial Regex GeneratedEndingsNotToTrimOpenGL();

        [GeneratedRegex("(sh|ib|[tdrey]s|[eE]n[vd]|bled|Attrib|Address|Access|Boolean|Bitmaps|Coord|Depth|Feedbacks|Finish|Flag|Groups|IDs|Indexed|Instanced|Pixels|Queries|Status|Tess|Through|Uniforms|Varyings|Weight|Width|[1-4][fdhi]v|fv|iv)$", RegexOptions.Compiled)]
        private static partial Regex GeneratedEndingsNotToTrimOpenAL();

        [GeneratedRegex("^0", RegexOptions.Compiled)]
        private static partial Regex GeneratedEndingsAddV();

        private static readonly Regex Endings = GeneratedEndings();

        public static readonly Regex EndingsNotToTrimOpenGL = GeneratedEndingsNotToTrimOpenGL();

        public static readonly Regex EndingsNotToTrimOpenAL = GeneratedEndingsNotToTrimOpenAL();

        private static readonly Regex EndingsAddV = GeneratedEndingsAddV();

        private readonly Regex EndingsNotToTrim;

        public TrimNameOverloader(Regex endingsNotToTrim)
        {
            EndingsNotToTrim = endingsNotToTrim;
        }

        public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads)
        {
            // See: https://github.com/opentk/opentk/blob/082c8d228d0def042b11424ac002776432f44f47/src/Generator.Bind/FuncProcessor.cs#L417

            string name = overload.OverloadName;
            string trimmedName = name;
            // FIXME: Remove vendor name before we trim endings
            //
            // We actually need to remove vendor names in context of the other
            // functions that will be in the same scope as this function
            // There are a number of ARB functions that have two defintions
            // for a function, one with the ARB postfix and one without.
            // This causes problems when we remove the postfix as the two
            // functions now conflict.
            // - Noggin_bops 2023-01-26
            Match m = EndingsNotToTrim.Match(name);
            if (m.Index + m.Length != name.Length)
            {
                m = Endings.Match(name);

                if (m.Length > 0 && m.Index + m.Length == name.Length)
                {
                    if (!name.EndsWith("xedv"))
                    {
                        trimmedName = name.Substring(0, m.Index);
                    }
                    else
                    {
                        trimmedName = name.Substring(0, m.Index);
                    }
                }
            }

            if (trimmedName != name)
            {
                newOverloads = new List<Overload>() { overload with
                {
                    OverloadName = trimmedName,
                    NestedOverload = overload,
                    MarshalLayerToNested = null
                }};
                return true;
            }
            else
            {
                newOverloads = default;
                return false;
            }
        }
    }
}
