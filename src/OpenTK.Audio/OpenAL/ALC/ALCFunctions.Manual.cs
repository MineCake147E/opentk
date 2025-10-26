using System;
using System.Buffers;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace OpenTK.Audio.OpenAL.ALC
{
    /// <summary>
    /// Functions from <see cref="ALC"/>.
    /// </summary>
    public static unsafe partial class ALCFunctions
    {
        /// <inheritdoc cref="CreateContext(ALC, ALCDevice, int*)"/>
        public static ALCContext CreateContext(this ALC alc, ALCDevice device, ALCContextAttributes attributes)
        {
            var p = ArrayPool<int>.Shared;
            var a = attributes.CreateAttributeArray(p);
            var res = alc.CreateContext(device, a.AsSpan());
            p.Return(a);
            return res;
        }

        /// <inheritdoc cref="GetIntegerv(ALC, ALCDevice, GetPNameIV, int, int*)"/>
        public static unsafe int GetInteger(this ALC alc, ALCDevice device, GetPNameIV name)
        {
            int value;
            alc.GetIntegerv(device, name, 1, &value);
            return value;
        }

        /// <summary>
        /// Gets and constructs a <see cref="ALCContextAttributes"/> object by calling <see cref="GetInteger(ALC, ALCDevice, GetPNameIV)"/> on current device.
        /// </summary>
        /// <param name="alc">The container of native function pointers.</param>
        /// <param name="device">The device to get the context attributes from.</param>
        /// <returns>The parsed context attributes.</returns>
        public static ALCContextAttributes GetContextAttributes(this ALC alc, ALCDevice device)
        {
            int size = 0;
            alc.GetInteger(device, GetPNameIV.AttributesSize, 1, ref size);
            int[] attributes = ArrayPool<int>.Shared.Rent(size);
            alc.GetInteger(device, GetPNameIV.AllAttributes, size, attributes);
            var result = ALCContextAttributes.FromArray(attributes);
            ArrayPool<int>.Shared.Return(attributes, true);
            return result;
        }

        /// Overloaders: [RefInsteadOfPointerLayer]
        /// <inheritdoc cref="GetInteger64vSOFT(ALCExtensions.SOFT, ALCDevice, GetPNameI64V, int, long*)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe long GetInteger64SOFT(this ALCExtensions.SOFT soft, ALCDevice device, GetPNameI64V pname)
        {
            long value;
            soft.GetInteger64vSOFT(device, pname, 1, &value);
            return value;
        }

        /// <summary>
        /// Calls <see cref="GetString_(ALC, ALCDevice, StringName)"/> and parses a OpenAL format string list into a list of strings.
        /// </summary>
        /// <remarks>
        /// This is most useful when called with <see cref="StringName.AllDevicesSpecifier"/> to get a list of all devices.
        /// </remarks>
        /// <param name="alc">The container of native function pointers.</param>
        /// <param name="device">The device to get the string list from.</param>
        /// <param name="name">The string list to receive.</param>
        /// <returns>The string list.</returns>
        public static unsafe List<string> GetStringList(this ALC alc, ALCDevice device, StringName name)
        {
            byte* result = alc.GetString_(device, name);
            return ALUtils.ALStringListToList(result);
        }

        /// <inheritdoc cref="GetStringiSOFT_(ALCExtensions.SOFT, ALCDevice, IndexedStringName, int)"/>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe IEnumerable<string> GetAllIndexedStringSOFT(this ALCExtensions.SOFT soft, ALCDevice device, IndexedStringName paramName, int count)
        {
            for (var i = 0; i < count; i++)
            {
                yield return soft.GetStringiSOFT(device, paramName, i);
            }
        }
    }
}
