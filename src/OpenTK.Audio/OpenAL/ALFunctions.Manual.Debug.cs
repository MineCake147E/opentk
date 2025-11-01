using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading.Tasks;

using OpenTK.Core.Native;

namespace OpenTK.Audio.OpenAL
{
    public static unsafe partial class ALFunctions
    {
        /// <inheritdoc cref="GetObjectLabelEXT(ALExtensions.EXT, ObjectType, uint, int, int*, byte*)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int GetObjectLabelLengthEXT(this ALExtensions.EXT ext, ObjectType identifier, uint name)
        {
            int length = -1;
            ext.GetObjectLabelEXT(identifier, name, 0, &length, null);
            return length;
        }

        /// <inheritdoc cref="GetObjectLabelEXT(ALExtensions.EXT, ObjectType, uint, int, int*, byte*)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int GetObjectLabelEXT(this ALExtensions.EXT ext, ObjectType identifier, uint name, int bufSize, byte* label)
        {
            int length = -1;
            ext.GetObjectLabelEXT(identifier, name, bufSize, &length, label);
            return length;
        }

        /// <inheritdoc cref="GetObjectLabelEXT(ALExtensions.EXT, ObjectType, uint, int, int*, byte*)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int GetObjectLabelEXT(this ALExtensions.EXT ext, ObjectType identifier, uint name, Span<byte> label)
        {
            int length = -1;
            fixed (byte* label_ptr = label)
            {
                ext.GetObjectLabelEXT(identifier, name, label.Length, &length, label_ptr);
            }
            return length;
        }

        /// <inheritdoc cref="GetObjectLabelEXT(ALExtensions.EXT, ObjectType, uint, int, int*, byte*)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe string? GetObjectLabelEXT(this ALExtensions.EXT ext, ObjectType identifier, uint name, [ConstantExpected] NativeCharacterEncoding resultEncoding = NativeCharacterEncoding.Utf8)
        {
            var length = ext.GetObjectLabelLengthEXT(identifier, name);
            switch (length)
            {
                case 0:
                    return "";
                case int.MaxValue:
                    return ThrowObjectLabelTooLong<string>(identifier, name);
                case < 0:
                    // Likely an error has been thrown inside AL if value is negative.
                    return null;
                default:
                    break;
            }
            int sizeToAdd = 1;
            int bytesWritten = -1;
            string? result = null;
            length++;
            var array = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                while (length > 0)
                {
                    var buffer = array.AsSpan();
                    if (buffer.Length < length)
                    {
                        return ThrowObjectLabelTooLong<string>(identifier, name);
                    }
                    length = buffer.Length;
                    fixed (byte* ptr = buffer)
                    {
                        bytesWritten = -1;
                        ext.GetObjectLabelEXT(identifier, name, buffer.Length, &bytesWritten, ptr);
                    }
                    if (bytesWritten < 0 || bytesWritten != length - 1)
                    {
                        break;
                    }
                    sizeToAdd = int.Min(Array.MaxLength - length, sizeToAdd);
                    // Buffer might have been insufficient.
                    if (sizeToAdd < 1)
                    {
                        return ThrowObjectLabelTooLong<string>(identifier, name);
                    }
                    length += sizeToAdd;
                    sizeToAdd *= 2;
                    ArrayPool<byte>.Shared.Return(array, true);
                }
                if (length < 1)
                {
                    return ThrowObjectLabelTooLong<string>(identifier, name);
                }
                result = bytesWritten switch
                {
                    < 0 => null,
                    0 => "",
                    _ => NativeString.ByteToString(array.AsSpan(0, bytesWritten)),
                };
            }
            finally
            {
                if (array is not null)
                {
                    ArrayPool<byte>.Shared.Return(array, true);
                }
            }
            return result;
        }

        /// <inheritdoc cref="GetObjectLabelEXT(ALExtensions.EXT, ObjectType, uint, int, int*, byte*)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int GetObjectLabelEXT<TBufferWriter>(this ALExtensions.EXT ext, ObjectType identifier, uint name, TBufferWriter bufferWriter)
            where TBufferWriter : class, IBufferWriter<byte>
        {
            var length = ext.GetObjectLabelLengthEXT(identifier, name);
            switch (length)
            {
                case int.MaxValue:
                    return ThrowObjectLabelTooLong<int>(identifier, name);
                case <= 0:
                    // Likely an error has been thrown inside AL if value is negative.
                    return length;
                default:
                    break;
            }
            int sizeToAdd = 1;
            int bytesWritten = 0;
            length++;
            while (length > 0)
            {
                var buffer = bufferWriter.GetSpan(length);
                if (buffer.Length < length)
                {
                    return ThrowObjectLabelTooLong<int>(identifier, name);
                }
                length = buffer.Length;
                fixed (byte* ptr = buffer)
                {
                    ext.GetObjectLabelEXT(identifier, name, buffer.Length, &bytesWritten, ptr);
                }
                if (bytesWritten < 0 || bytesWritten != length - 1)
                {
                    break;
                }
                sizeToAdd = int.Min(int.MaxValue - length, sizeToAdd);
                // Buffer might be insufficient.
                if (sizeToAdd < 1)
                {
                    return ThrowObjectLabelTooLong<int>(identifier, name);
                }
                length += sizeToAdd;
                sizeToAdd *= 2;
            }
            if (length < 1)
            {
                return ThrowObjectLabelTooLong<int>(identifier, name);
            }
            bufferWriter.Advance(int.Max(0, bytesWritten));
            return bytesWritten;
        }

        /// <inheritdoc cref="GetObjectLabelEXT(ALExtensions.EXT, ObjectType, uint, int, int*, byte*)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int GetObjectLabelEXT<TBufferWriter>(this ALExtensions.EXT ext, ObjectType identifier, uint name, ref TBufferWriter bufferWriter)
            where TBufferWriter : struct, IBufferWriter<byte>
        {
            var length = ext.GetObjectLabelLengthEXT(identifier, name);
            switch (length)
            {
                case 0:
                    return 0;
                case int.MaxValue:
                    return ThrowObjectLabelTooLong<int>(identifier, name);
                case < 0:
                    throw new OverflowException($"The reported length of the label of {identifier} with name <0x{name:x8}> is negative!");
                default:
                    break;
            }
            int sizeToAdd = 1;
            int bytesWritten = 0;
            while (length > 0)
            {
                var buffer = bufferWriter.GetSpan(length);
                if (buffer.Length < length)
                {
                    return ThrowObjectLabelTooLong<int>(identifier, name);
                }
                length = buffer.Length;
                fixed (byte* ptr = buffer)
                {
                    ext.GetObjectLabelEXT(identifier, name, buffer.Length, &bytesWritten, ptr);
                }
                if (bytesWritten < 0 || bytesWritten != length - 1)
                {
                    break;
                }
                sizeToAdd = int.Min(int.MaxValue - length, sizeToAdd);
                // Buffer might be insufficient.
                if (sizeToAdd < 1)
                {
                    return ThrowObjectLabelTooLong<int>(identifier, name);
                }
                length += sizeToAdd;
                sizeToAdd *= 2;
            }
            if (length < 1)
            {
                return ThrowObjectLabelTooLong<int>(identifier, name);
            }
            bufferWriter.Advance(int.Max(0, bytesWritten));
            return bytesWritten;
        }

        /// <inheritdoc cref="GetObjectLabelDirectEXT(ALExtensions.Direct{ALExtensions.EXT}, ALCContext, ObjectType, uint, int, int*, byte*)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int GetObjectLabelLengthDirectEXT(this ALExtensions.Direct<ALExtensions.EXT> direct, ALCContext context, ObjectType identifier, uint name)
        {
            int length = -1;
            direct.GetObjectLabelDirectEXT(context, identifier, name, 0, &length, null);
            return length;
        }

        /// <inheritdoc cref="GetObjectLabelDirectEXT(ALExtensions.Direct{ALExtensions.EXT}, ALCContext, ObjectType, uint, int, int*, byte*)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int GetObjectLabelDirectEXT(this ALExtensions.Direct<ALExtensions.EXT> direct, ALCContext context, ObjectType identifier, uint name, int bufSize, byte* label)
        {
            int length = -1;
            direct.GetObjectLabelDirectEXT(context, identifier, name, bufSize, &length, label);
            return length;
        }

        /// <inheritdoc cref="GetObjectLabelDirectEXT(ALExtensions.Direct{ALExtensions.EXT}, ALCContext, ObjectType, uint, int, int*, byte*)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int GetObjectLabelDirectEXT(this ALExtensions.Direct<ALExtensions.EXT> direct, ALCContext context, ObjectType identifier, uint name, Span<byte> label)
        {
            int length = -1;
            fixed (byte* label_ptr = label)
            {
                direct.GetObjectLabelDirectEXT(context, identifier, name, label.Length, &length, label_ptr);
            }
            return length;
        }

        /// <inheritdoc cref="GetObjectLabelDirectEXT(ALExtensions.Direct{ALExtensions.EXT}, ALCContext, ObjectType, uint, int, int*, byte*)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe string? GetObjectLabelDirectEXT(this ALExtensions.Direct<ALExtensions.EXT> direct, ALCContext context, ObjectType identifier, uint name, [ConstantExpected] NativeCharacterEncoding resultEncoding = NativeCharacterEncoding.Utf8)
        {
            var length = direct.GetObjectLabelLengthDirectEXT(context, identifier, name);
            switch (length)
            {
                case 0:
                    return "";
                case int.MaxValue:
                    return ThrowObjectLabelTooLong<string>(identifier, name);
                case < 0:
                    // Likely an error has been thrown inside AL if value is negative.
                    return null;
                default:
                    break;
            }
            int sizeToAdd = 1;
            int bytesWritten = -1;
            string? result = null;
            length++;
            var array = ArrayPool<byte>.Shared.Rent(length);
            try
            {
                while (length > 0)
                {
                    var buffer = array.AsSpan();
                    if (buffer.Length < length)
                    {
                        return ThrowObjectLabelTooLong<string>(identifier, name);
                    }
                    length = buffer.Length;
                    fixed (byte* ptr = buffer)
                    {
                        bytesWritten = -1;
                        direct.GetObjectLabelDirectEXT(context, identifier, name, buffer.Length, &bytesWritten, ptr);
                    }
                    if (bytesWritten < 0 || bytesWritten != length - 1)
                    {
                        break;
                    }
                    sizeToAdd = int.Min(Array.MaxLength - length, sizeToAdd);
                    // Buffer might have been insufficient.
                    if (sizeToAdd < 1)
                    {
                        return ThrowObjectLabelTooLong<string>(identifier, name);
                    }
                    length += sizeToAdd;
                    sizeToAdd *= 2;
                    ArrayPool<byte>.Shared.Return(array, true);
                }
                if (length < 1)
                {
                    return ThrowObjectLabelTooLong<string>(identifier, name);
                }
                result = bytesWritten switch
                {
                    < 0 => null,
                    0 => "",
                    _ => NativeString.ByteToString(array.AsSpan(0, bytesWritten)),
                };
            }
            finally
            {
                if (array is not null)
                {
                    ArrayPool<byte>.Shared.Return(array, true);
                }
            }
            return result;
        }

        /// <inheritdoc cref="GetObjectLabelDirectEXT(ALExtensions.Direct{ALExtensions.EXT}, ALCContext, ObjectType, uint, int, int*, byte*)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int GetObjectLabelDirectEXT<TBufferWriter>(this ALExtensions.Direct<ALExtensions.EXT> direct, ALCContext context, ObjectType identifier, uint name, TBufferWriter bufferWriter)
            where TBufferWriter : class, IBufferWriter<byte>
        {
            var length = direct.GetObjectLabelLengthDirectEXT(context, identifier, name);
            switch (length)
            {
                case int.MaxValue:
                    return ThrowObjectLabelTooLong<int>(identifier, name);
                case <= 0:
                    // Likely an error has been thrown inside AL if value is negative.
                    return length;
                default:
                    break;
            }
            int sizeToAdd = 1;
            int bytesWritten = 0;
            length++;
            while (length > 0)
            {
                var buffer = bufferWriter.GetSpan(length);
                if (buffer.Length < length)
                {
                    return ThrowObjectLabelTooLong<int>(identifier, name);
                }
                length = buffer.Length;
                fixed (byte* ptr = buffer)
                {
                    direct.GetObjectLabelDirectEXT(context, identifier, name, buffer.Length, &bytesWritten, ptr);
                }
                if (bytesWritten < 0 || bytesWritten != length - 1)
                {
                    break;
                }
                sizeToAdd = int.Min(int.MaxValue - length, sizeToAdd);
                // Buffer might be insufficient.
                if (sizeToAdd < 1)
                {
                    return ThrowObjectLabelTooLong<int>(identifier, name);
                }
                length += sizeToAdd;
                sizeToAdd *= 2;
            }
            if (length < 1)
            {
                return ThrowObjectLabelTooLong<int>(identifier, name);
            }
            bufferWriter.Advance(int.Max(0, bytesWritten));
            return bytesWritten;
        }

        /// <inheritdoc cref="GetObjectLabelDirectEXT(ALExtensions.Direct{ALExtensions.EXT}, ALCContext, ObjectType, uint, int, int*, byte*)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe int GetObjectLabelDirectEXT<TBufferWriter>(this ALExtensions.Direct<ALExtensions.EXT> direct, ALCContext context, ObjectType identifier, uint name, ref TBufferWriter bufferWriter)
            where TBufferWriter : struct, IBufferWriter<byte>
        {
            var length = direct.GetObjectLabelLengthDirectEXT(context, identifier, name);
            switch (length)
            {
                case 0:
                    return 0;
                case int.MaxValue:
                    return ThrowObjectLabelTooLong<int>(identifier, name);
                case < 0:
                    throw new OverflowException($"The reported length of the label of {identifier} with name <0x{name:x8}> is negative!");
                default:
                    break;
            }
            int sizeToAdd = 1;
            int bytesWritten = 0;
            while (length > 0)
            {
                var buffer = bufferWriter.GetSpan(length);
                if (buffer.Length < length)
                {
                    return ThrowObjectLabelTooLong<int>(identifier, name);
                }
                length = buffer.Length;
                fixed (byte* ptr = buffer)
                {
                    direct.GetObjectLabelDirectEXT(context, identifier, name, buffer.Length, &bytesWritten, ptr);
                }
                if (bytesWritten < 0 || bytesWritten != length - 1)
                {
                    break;
                }
                sizeToAdd = int.Min(int.MaxValue - length, sizeToAdd);
                // Buffer might be insufficient.
                if (sizeToAdd < 1)
                {
                    return ThrowObjectLabelTooLong<int>(identifier, name);
                }
                length += sizeToAdd;
                sizeToAdd *= 2;
            }
            if (length < 1)
            {
                return ThrowObjectLabelTooLong<int>(identifier, name);
            }
            bufferWriter.Advance(int.Max(0, bytesWritten));
            return bytesWritten;
        }

        [DoesNotReturn, MethodImpl(MethodImplOptions.NoInlining)]
        private static T ThrowObjectLabelTooLong<T>(ObjectType identifier, uint name)
        {
            throw new NotSupportedException($"The label of {identifier} with name <0x{name:x8}> is too long!");
        }
    }
}
