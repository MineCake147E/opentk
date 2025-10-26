//
// ALContextAttributes.cs
//
// Copyright (C) 2020 OpenTK
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.
//

using System;
using System.Buffers;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

using OpenTK.Audio.OpenAL.ALC;

namespace OpenTK.Audio.OpenAL
{
    /// <summary>
    /// Convenience class for handling ALContext attributes.
    /// </summary>
    [DebuggerDisplay($"{{{nameof(GetDebuggerDisplay)}(),nq}}")]
    public readonly struct ALCContextAttributes : IDictionary<ALC.ContextAttribute, int>
    {
        private readonly Dictionary<ALC.ContextAttribute, int> _values;

        /// <summary>
        /// Gets or sets the output buffer frequency in Hz.
        /// This does not actually change any AL state. To apply these attributes see <see cref="ALCFunctions.CreateContext(ALC.ALC, ALCDevice, ALCContextAttributes)"/>.
        /// </summary>
        public int? Frequency
        {
            get => _values.TryGetValue(ALC.ContextAttribute.Frequency, out int value) ? value : null;
            set
            {
                if (!value.HasValue)
                {
                    _values.Remove(ALC.ContextAttribute.Frequency);
                }
                else
                {
                    _values[ALC.ContextAttribute.Frequency] = value.Value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the number of mono sources.
        /// This does not actually change any AL state. To apply these attributes see <see cref="ALCFunctions.CreateContext(ALC.ALC, ALCDevice, ALCContextAttributes)"/>.
        /// Not guaranteed to get exact number of mono sources when creating a context.
        /// </summary>
        public int? MonoSources
        {
            get => _values.TryGetValue(ALC.ContextAttribute.MonoSources, out int value) ? value : null;
            set
            {
                if (!value.HasValue)
                {
                    _values.Remove(ALC.ContextAttribute.MonoSources);
                }
                else
                {
                    _values[ALC.ContextAttribute.MonoSources] = value.Value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the number of stereo sources.
        /// This does not actually change any AL state. To apply these attributes see <see cref="ALCFunctions.CreateContext(ALC.ALC, ALCDevice, ALCContextAttributes)"/>.
        /// Not guaranteed to get exact number of mono sources when creating a context.
        /// </summary>
        public int? StereoSources
        {
            get => _values.TryGetValue(ALC.ContextAttribute.StereoSources, out int value) ? value : null;
            set
            {
                if (!value.HasValue)
                {
                    _values.Remove(ALC.ContextAttribute.StereoSources);
                }
                else
                {
                    _values[ALC.ContextAttribute.StereoSources] = value.Value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the refrash interval in Hz.
        /// This does not actually change any AL state. To apply these attributes see <see cref="ALCFunctions.CreateContext(ALC.ALC, ALCDevice, ALCContextAttributes)"/>.
        /// </summary>
        public int? Refresh
        {
            get => _values.TryGetValue(ALC.ContextAttribute.Refresh, out int value) ? value : null;
            set
            {
                if (!value.HasValue)
                {
                    _values.Remove(ALC.ContextAttribute.Refresh);
                }
                else
                {
                    _values[ALC.ContextAttribute.Refresh] = value.Value;
                }
            }
        }

        /// <summary>
        /// Gets or sets if the context is synchronous.
        /// This does not actually change any AL state. To apply these attributes see <see cref="ALCFunctions.CreateContext(ALC.ALC, ALCDevice, ALCContextAttributes)"/>.
        /// </summary>
        public bool? Sync
        {
            get => _values.TryGetValue(ALC.ContextAttribute.Sync, out int value) ? (value != 0) : null;
            set
            {
                if (!value.HasValue)
                {
                    _values.Remove(ALC.ContextAttribute.MonoSources);
                }
                else
                {
                    _values[ALC.ContextAttribute.MonoSources] = value.Value ? 1 : 0;
                }
            }
        }

        /// <inheritdoc/>
        public readonly ICollection<ALC.ContextAttribute> Keys => _values.Keys;

        /// <inheritdoc/>
        public readonly ICollection<int> Values => _values.Values;

        /// <inheritdoc/>
        public readonly int Count => _values.Count;

        /// <inheritdoc/>
        public readonly bool IsReadOnly => false;

        /// <inheritdoc/>
        public readonly int this[ALC.ContextAttribute key] { get => _values[key]; set => _values[key] = value; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ALCContextAttributes"/> struct.
        /// Leaving all attributes to the driver implementation default values.
        /// </summary>
        public ALCContextAttributes()
        {
            _values = [];
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ALCContextAttributes"/> struct.
        /// </summary>
        /// <param name="frequency">The mixing output buffer frequency in Hz.</param>
        /// <param name="monoSources">The number of mono sources available. Not guaranteed.</param>
        /// <param name="stereoSources">The number of stereo sources available. Not guaranteed.</param>
        /// <param name="refresh">The refresh interval in Hz.</param>
        /// <param name="sync">If the context is synchronous.</param>
        public ALCContextAttributes(int? frequency, int? monoSources, int? stereoSources, int? refresh, bool? sync)
        {
            _values = [];
            Frequency = frequency;
            MonoSources = monoSources;
            StereoSources = stereoSources;
            Refresh = refresh;
            Sync = sync;
        }

        /// <summary>
        /// Converts these context attributes to a <see cref="ALCFunctions.CreateContext(ALC.ALC, ALCDevice, int[])"/> compatible list.
        /// Alternativly, consider using the more convenient <see cref="ALCFunctions.CreateContext(ALC.ALC, ALCDevice, ALCContextAttributes)"/> overload.
        /// </summary>
        /// <returns>The attibute list in the form of a span.</returns>
        public readonly int[] CreateAttributeArray()
            => [.. _values.SelectMany<KeyValuePair<ALC.ContextAttribute, int>, int>(a => [(int)a.Key, a.Value]), 0];

        /// <summary>
        /// Converts these context attributes to a <see cref="ALCFunctions.CreateContext(ALC.ALC, ALCDevice, int[])"/> compatible list.
        /// Alternativly, consider using the more convenient <see cref="ALCFunctions.CreateContext(ALC.ALC, ALCDevice, ALCContextAttributes)"/> overload.
        /// </summary>
        /// <param name="arrayPool">The <see cref="ArrayPool{T}"/> to rent a new array.</param>
        /// <returns>The attibute list in the form of a pooled array.</returns>
        public readonly int[] CreateAttributeArray(ArrayPool<int> arrayPool)
        {
            ArgumentNullException.ThrowIfNull(arrayPool);
            var valuesToEncode = _values;
            var l = (valuesToEncode.Count * 2) + 1;
            var array = arrayPool.Rent(l);
            var span = array.AsSpan(0, l);
            var enumerator = valuesToEncode.GetEnumerator();
            var len2 = span.Length - 1;
            ref var head = ref MemoryMarshal.GetReference(span);
            for (var i = 0; i < len2 && enumerator.MoveNext(); i += 2)
            {
                var item = enumerator.Current;
                head = (int)item.Key;
                Unsafe.Add(ref head, 1) = item.Value;
            }
            return array;
        }

        /// <summary>
        /// Parses a AL attribute list.
        /// </summary>
        /// <param name="attributes">The AL context attribute list.</param>
        /// <returns>The parsed <see cref="ALC.ContextAttribute"/> object.</returns>
        public static ALCContextAttributes FromArray(scoped ReadOnlySpan<int> attributes)
        {
            var result = new ALCContextAttributes();

            void ParseAttribute(int @enum, int value)
            {
                result[(ALC.ContextAttribute)@enum] = value;
            }

            for (int i = 0; i < attributes.Length - 1; i += 2)
            {
                ParseAttribute(attributes[i], attributes[i + 1]);
            }

            return result;
        }

        /// <summary>
        /// Converts the attributes to a string representation.
        /// </summary>
        /// <returns>The string representation of the attributes.</returns>
        public override readonly string ToString() => GetDebuggerDisplay();

        private string GetDebuggerDisplay()
            => string.Join(", ", _values.Select(a => $"{a.Key}: {a.Value}"));

        /// <inheritdoc/>
        public readonly void Add(ALC.ContextAttribute key, int value) => _values.Add(key, value);

        /// <inheritdoc/>
        public readonly bool ContainsKey(ALC.ContextAttribute key) => _values.ContainsKey(key);

        /// <inheritdoc/>
        public readonly bool Remove(ALC.ContextAttribute key) => _values.Remove(key);

        /// <inheritdoc/>
        public readonly bool TryGetValue(ALC.ContextAttribute key, [MaybeNullWhen(false)] out int value) => _values.TryGetValue(key, out value);

        /// <inheritdoc/>
        public readonly void Add(KeyValuePair<ALC.ContextAttribute, int> item) => _values.Add(item.Key, item.Value);

        /// <inheritdoc/>
        public readonly void Clear() => _values.Clear();

        /// <inheritdoc/>
        readonly bool ICollection<KeyValuePair<ALC.ContextAttribute, int>>.Contains(KeyValuePair<ALC.ContextAttribute, int> item) => _values.TryGetValue(item.Key, out var value) && item.Value.Equals(value);

        /// <inheritdoc/>
        readonly void ICollection<KeyValuePair<ALC.ContextAttribute, int>>.CopyTo(KeyValuePair<ALC.ContextAttribute, int>[] array, int arrayIndex) => ((ICollection<KeyValuePair<ALC.ContextAttribute, int>>)_values).CopyTo(array, arrayIndex);

        /// <inheritdoc/>
        readonly bool ICollection<KeyValuePair<ALC.ContextAttribute, int>>.Remove(KeyValuePair<ALC.ContextAttribute, int> item) => ((ICollection<KeyValuePair<ALC.ContextAttribute, int>>)_values).Remove(item);

        /// <inheritdoc/>
        public readonly IEnumerator<KeyValuePair<ALC.ContextAttribute, int>> GetEnumerator() => ((IEnumerable<KeyValuePair<ALC.ContextAttribute, int>>)_values).GetEnumerator();

        /// <inheritdoc/>
        readonly IEnumerator IEnumerable.GetEnumerator() => ((IEnumerable)_values).GetEnumerator();
    }
}
