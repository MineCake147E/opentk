//
// ALContext.cs
//
// Copyright (C) 2020 OpenTK
//
// This software may be modified and distributed under the terms
// of the MIT license. See the LICENSE file for details.
//

using System;
using System.Diagnostics.CodeAnalysis;
using System.Numerics;

namespace OpenTK.Audio.OpenAL
{
    /// <summary>
    /// Represents an OpenAL Context.
    /// </summary>
    public readonly struct ALCContext : IEquatable<ALCContext>, IEqualityOperators<ALCContext, ALCContext, bool>
    {
        /// <summary>
        /// The default value of <see cref="ALCContext"/>.
        /// </summary>
        public static ALCContext Null => default;

        /// <summary>
        /// The underlying value of the <see cref="ALCContext"/>.
        /// </summary>
        public IntPtr Handle { get; }

        /// <summary>
        /// Gets a value indicating whether the current <see cref="ALCContext"/> object has a valid value.
        /// </summary>
        public bool HasValue => Handle != default;

        /// <summary>
        /// Initializes a new instance of the <see cref="ALCContext"/> struct.
        /// </summary>
        /// <param name="handle">The <see cref="IntPtr"/> to initialize with.</param>
        public ALCContext(IntPtr handle)
        {
            Handle = handle;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is ALCContext handle && Equals(handle);
        }

        /// <inheritdoc/>
        public bool Equals([AllowNull] ALCContext other)
        {
            return Handle.Equals(other.Handle);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Handle);
        }

        /// <inheritdoc/>
        public static bool operator ==(ALCContext left, ALCContext right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(ALCContext left, ALCContext right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Defines an implicit conversion of an <see cref="ALCContext"/> to an <see cref="IntPtr"/>.
        /// </summary>
        /// <param name="context">The <see cref="ALCContext"/> to convert.</param>
        public static implicit operator IntPtr(ALCContext context) => context.Handle;

        /// <summary>
        /// Defines an explicit conversion of an <see cref="IntPtr"/> to an <see cref="ALCContext"/>.
        /// </summary>
        /// <param name="ptr">The <see cref="IntPtr"/> to convert.</param>
        public static explicit operator ALCContext(IntPtr ptr) => new ALCContext(ptr);
    }
}
