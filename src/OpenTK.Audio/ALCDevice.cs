//
// ALDevice.cs
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
    /// Opaque handle to an OpenAL device.
    /// </summary>
    public readonly struct ALCDevice : IEquatable<ALCDevice>, IEqualityOperators<ALCDevice, ALCDevice, bool>
    {
        /// <summary>
        /// The default value of <see cref="ALCDevice"/>.
        /// </summary>
        public static ALCDevice Null => default;

        /// <summary>
        /// The underlying value of the <see cref="ALCDevice"/>.
        /// </summary>
        public IntPtr Handle { get; }

        /// <summary>
        /// Gets a value indicating whether the current <see cref="ALCDevice"/> object has a valid value.
        /// </summary>
        public bool HasValue => Handle != default;

        /// <summary>
        /// Initializes a new instance of the <see cref="ALCDevice"/> struct.
        /// </summary>
        /// <param name="handle">The <see cref="IntPtr"/> to initialize with.</param>
        public ALCDevice(IntPtr handle)
        {
            Handle = handle;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            return obj is ALCDevice device && Equals(device);
        }

        /// <inheritdoc/>
        public bool Equals([AllowNull] ALCDevice other)
        {
            return Handle.Equals(other.Handle);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            return HashCode.Combine(Handle);
        }

        /// <inheritdoc/>
        public static bool operator ==(ALCDevice left, ALCDevice right)
        {
            return left.Equals(right);
        }

        /// <inheritdoc/>
        public static bool operator !=(ALCDevice left, ALCDevice right)
        {
            return !(left == right);
        }

        /// <summary>
        /// Defines an implicit conversion of an <see cref="ALCDevice"/> to an <see cref="IntPtr"/>.
        /// </summary>
        /// <param name="device">The <see cref="ALCDevice"/> to convert.</param>
        public static implicit operator IntPtr(ALCDevice device) => device.Handle;

        /// <summary>
        /// Defines an explicit conversion of an <see cref="IntPtr"/> to an <see cref="ALCDevice"/>.
        /// </summary>
        /// <param name="ptr">The <see cref="IntPtr"/> to convert.</param>
        public static explicit operator ALCDevice(IntPtr ptr) => new ALCDevice(ptr);
    }
}
