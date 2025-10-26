using System.CodeDom.Compiler;

namespace GeneratorBase.Overloading
{
    public interface IOverloadLayer
    {
        public void WritePrologue(IndentedTextWriter writer, NameTable nameTable);

        public string? WriteEpilogue(IndentedTextWriter writer, NameTable nameTable, string? returnName);

        public int OverloadResolutionPriority => 0;
    }
}
