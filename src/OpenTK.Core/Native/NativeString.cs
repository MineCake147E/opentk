using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace OpenTK.Core.Native
{
    /// <summary>
    /// Provides a collection of methods for interacting with unmanaged string.
    /// </summary>
    public static class NativeString
    {
        /// <summary>
        /// Allocates a managed <see cref="string"/> and copies all characters up  to the first null character from an unmanaged string into it.
        /// </summary>
        /// <param name="pointer">The address of the first character of the unmanaged string.</param>
        /// <param name="encoding">The encoding of the unmanaged string.</param>
        /// <returns>A managed string that holds a copy of the unmanaged <see cref="string"/> if the value of the <paramref name="pointer"/> parameter is not <see langword="null"/>; otherwise, this method returns <see langword="null"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe string? PtrToString(byte* pointer, [ConstantExpected] NativeCharacterEncoding encoding = NativeCharacterEncoding.Utf8)
            => encoding switch
            {
                NativeCharacterEncoding.Utf8 => Marshal.PtrToStringUTF8((nint)pointer),
                NativeCharacterEncoding.Ansi => Marshal.PtrToStringAnsi((nint)pointer),
                NativeCharacterEncoding.Uni => Marshal.PtrToStringUni((nint)pointer),
                NativeCharacterEncoding.Auto => Marshal.PtrToStringAuto((nint)pointer),
                _ => Marshal.PtrToStringUTF8((nint)pointer)
            };

        /// <summary>
        /// Decodes a range of bytes from a byte array into a string.
        /// </summary>
        /// <param name="bytes">A read-only byte span to decode to a Unicode string.</param>
        /// <param name="encoding">The encoding of the string.</param>
        /// <returns>A string that contains the decoded bytes from the provided read-only span.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe string? ByteToString(ReadOnlySpan<byte> bytes, [ConstantExpected] NativeCharacterEncoding encoding = NativeCharacterEncoding.Utf8)
            => encoding switch
            {
                NativeCharacterEncoding.Utf8 => Encoding.UTF8.GetString(bytes),
                NativeCharacterEncoding.Uni => Encoding.Unicode.GetString(bytes),
                NativeCharacterEncoding.Auto => DecodeAuto(bytes),
                NativeCharacterEncoding.Ansi => DecodeAnsi(bytes),
                _ => Encoding.UTF8.GetString(bytes)
            };

        private static unsafe string? DecodeAuto(ReadOnlySpan<byte> bytes)
        {
            fixed (byte* ptr = bytes)
            {
                return Marshal.PtrToStringAuto((nint)ptr);
            }
        }

        private static unsafe string? DecodeAnsi(ReadOnlySpan<byte> bytes)
        {
            fixed (byte* ptr = bytes)
            {
                return new string((sbyte*)ptr, 0, bytes.Length);
            }
        }

        /// <summary>
        /// Ensures that <paramref name="nullTerminatedUtf8String"/> is null-terminated.
        /// </summary>
        /// <param name="nullTerminatedUtf8String">The <see cref="ReadOnlySpan{T}"/> to test.</param>
        /// <param name="rentArray">The <see cref="byte"/> array rent from <see cref="ArrayPool{T}.Shared"/> in case of <paramref name="nullTerminatedUtf8String"/> not being null-terminated.</param>
        /// <returns>The <paramref name="nullTerminatedUtf8String"/> if it is null-terminated, or <paramref name="rentArray"/>.</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe ReadOnlySpan<byte> EnsureNullTerminated(ReadOnlySpan<byte> nullTerminatedUtf8String, out byte[]? rentArray)
        {
            rentArray = null;
            if (nullTerminatedUtf8String.Length < 1 || nullTerminatedUtf8String[^1] != 0)
            {
                nullTerminatedUtf8String = RentNewArray(nullTerminatedUtf8String, out rentArray);
            }
            return nullTerminatedUtf8String;

            [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.AggressiveOptimization)]
            static unsafe ReadOnlySpan<byte> RentNewArray(ReadOnlySpan<byte> utf8String, out byte[]? rentArray)
            {
                var pool = ArrayPool<byte>.Shared;
                var array = pool.Rent(utf8String.Length + 1);
                var span = array.AsSpan();
                span.Clear();
                utf8String.CopyTo(span);
                rentArray = array;
                return span;
            }
        }
    }
}
