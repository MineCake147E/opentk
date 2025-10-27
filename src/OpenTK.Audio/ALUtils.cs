using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace OpenTK.Audio
{
    /// <summary>
    /// Provides a collection of functions for interacting with OpenAL APIs.
    /// </summary>
    public static partial class ALUtils
    {
        /// <summary>
        /// Converts the null-separated list of string into a <see cref="List{T}"/> of <see cref="string"/>.
        /// </summary>
        /// <param name="alList">The source null-separated list of string.</param>
        /// <returns>The <see cref="List{T}"/> of <see cref="string"/> parsed from <paramref name="alList"/>.</returns>
        public static unsafe List<string> ALStringListToList(byte* alList)
        {
            if (alList is null)
            {
                return [];
            }

            var strings = new List<string>();

            byte* currentPos = alList;
            while (true)
            {
                var span = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(currentPos);
                if (span.IsEmpty)
                {
                    break;
                }

                strings.Add(Encoding.UTF8.GetString(span));
                currentPos += span.Length + 1;
            }

            return strings;
        }

        /// <summary>
        /// Acts as a <see cref="IEnumerable{T}"/> for unmanaged null-separated list of string.
        /// </summary>
        public readonly unsafe ref struct NullSeparatedByteArrayEnumerable
        {
            private readonly byte* _head;

            /// <summary>
            /// Initializes a new instance of the <see cref="NullSeparatedByteArrayEnumerable"/> struct.
            /// </summary>
            /// <param name="head">The starting pointer of a null-separated list of string.</param>
            public NullSeparatedByteArrayEnumerable(byte* head)
            {
                _head = head;
            }

            /// <summary>
            /// Returns an enumerator that iterates through the collection.
            /// </summary>
            /// <returns>An enumerator that can be used to iterate through the collection.</returns>
            public SpanEnumerator GetEnumerator() => new(_head);

            /// <summary>
            /// Enumerates the elements of a null-separated list of string.
            /// </summary>
            public unsafe ref struct SpanEnumerator : IEnumerator<ReadOnlySpan<byte>>
            {
                private byte* _head;
                private nuint _offset;
                private int _currentLength;
                private uint _separatorLength;

                internal SpanEnumerator(byte* head)
                {
                    _head = head;
                    _offset = ~(nuint)0;
                    _currentLength = 0;
                    _separatorLength = 1;
                }

                /// <inheritdoc/>
                public readonly ReadOnlySpan<byte> Current => new(_head + _offset, _currentLength);

                readonly object IEnumerator.Current => Encoding.UTF8.GetString(Current);

                /// <inheritdoc/>
                public bool MoveNext()
                {
                    var result = _head is not null;
                    if (result)
                    {
                        var currentOffset = _offset;
                        currentOffset += (nuint)unchecked((uint)_currentLength) + _separatorLength;
                        var span = MemoryMarshal.CreateReadOnlySpanFromNullTerminated(_head + currentOffset);
                        result = !span.IsEmpty;
                        _currentLength = span.Length;
                        _offset = currentOffset;
                    }
                    _separatorLength = Unsafe.BitCast<bool, byte>(result);
                    return result;
                }

                /// <inheritdoc/>
                public void Reset()
                {
                    _offset = ~(nuint)0;
                    _currentLength = 0;
                    _separatorLength = 1;
                }

                /// <inheritdoc/>
                public void Dispose()
                {
                    this = default;
                }
            }
        }
    }
}
