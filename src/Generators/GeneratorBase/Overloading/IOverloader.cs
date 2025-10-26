using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;

namespace GeneratorBase.Overloading
{
    public interface IOverloader
    {
        public bool TryGenerateOverloads(Overload overload, [NotNullWhen(true)] out List<Overload>? newOverloads);
    }
}
