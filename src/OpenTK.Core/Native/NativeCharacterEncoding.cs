using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace OpenTK.Core.Native
{
    /// <summary>
    /// Represents the set of <see cref="Marshal"/> string functions for marshalling string arguments and results.
    /// </summary>
    public enum NativeCharacterEncoding : byte
    {
        /// <summary>
        /// Uses <see cref="Marshal.PtrToStringUTF8(nint)"/> for decoding, and <see cref="Marshal.StringToCoTaskMemUTF8(string?)"/> for encoding.
        /// This is the default behaviour.
        /// </summary>
        Utf8,

        /// <summary>
        /// Uses <see cref="Marshal.PtrToStringAnsi(nint)"/> for decoding, and <see cref="Marshal.StringToCoTaskMemAnsi(string?)"/> for encoding.
        /// </summary>
        Ansi,

        /// <summary>
        /// Uses <see cref="Marshal.PtrToStringUni(nint)"/> for decoding, and <see cref="Marshal.StringToCoTaskMemUni(string?)"/> for encoding.
        /// </summary>
        Uni,

        /// <summary>
        /// Uses <see cref="Marshal.PtrToStringAuto(nint)"/> for decoding, and <see cref="Marshal.StringToCoTaskMemAuto(string?)"/> for encoding.
        /// </summary>
        Auto
    }
}
