using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using OpenTK.Audio.OpenAL;
using OpenTK.Audio.OpenAL.ALC;

namespace OpenTK.Audio
{
    /// <summary>
    /// A container of all OpenAL ALC function pointers.
    /// </summary>
    public sealed unsafe class ALCLoader
    {
        private static readonly OpenALLibraryNameContainer NameContainer = new OpenALLibraryNameContainer();

        /// <summary>
        /// The shared instance of <see cref="ALCLoader"/>.
        /// </summary>
        public static ALCLoader Default { get; }

        static ALCLoader()
        {
            var alHandle = NativeLibrary.Load(NameContainer.GetLibraryName());
            Default = new(alHandle);
        }

        private readonly IntPtr _alHandle;
        private readonly ALCDevice _device;
        private readonly delegate* unmanaged[Cdecl]<byte*, void*> _alGetProcAddressFnptr;
        private readonly ALCLoader? _parent;
        private readonly ALCPointers _pointers;

        internal delegate* unmanaged[Cdecl]<byte*, void*> AlGetProcAddressFnptr => _alGetProcAddressFnptr;

        /// <summary>
        /// The <see cref="OpenAL.ALC.ALC"/> loaded by this <see cref="ALCLoader"/>.
        /// </summary>
        public ALC ALC => new(in _pointers);

        /// <summary>
        /// The <see cref="ALCDevice"/> used while loading this <see cref="ALCLoader"/>.
        /// This <see cref="ALCDevice"/> may not be a valid device for <see cref="ALC"/> of this instance.
        /// </summary>
        public unsafe ALCDevice Device => _device;

        /// <summary>
        /// The <see cref="ALCLoader"/> that loaded this <see cref="ALCLoader"/>.
        /// </summary>
        public ALCLoader? Parent => _parent;

        private ALCLoader(IntPtr alHandle)
        {
            _alHandle = alHandle;
            _device = default;
            NativeLibrary.TryGetExport(alHandle, "alcGetProcAddress", out var alcGetProcAddressFnptr);
            NativeLibrary.TryGetExport(alHandle, "alGetProcAddress", out var alGetProcAddressFnptr);
            _alGetProcAddressFnptr = (delegate* unmanaged[Cdecl]<byte*, void*>)alGetProcAddressFnptr;
            _pointers = new((delegate* unmanaged[Cdecl]<IntPtr, byte*, void*>)alcGetProcAddressFnptr, (nint)ALCDevice.Null);
            _parent = null;
        }

        private ALCLoader(ALCLoader loader, ALCDevice newDevice, out bool deviceReconnectionRequired, bool preferDirect = false)
        {
            _alHandle = loader._alHandle;
            var alcGetProcAddressFnptr = loader._pointers._alcGetProcAddress_fnptr;
            if (preferDirect)
            {
                var alcGetProcAddress2Fnptr = loader._pointers._alcGetProcAddress2_fnptr;
                if (alcGetProcAddress2Fnptr is null)
                {
                    alcGetProcAddress2Fnptr = (delegate* unmanaged[Cdecl]<nint, byte*, void*>)loader.ALC.GetProcAddress(newDevice, ALCPointers.AllFunctionNames.Slice(ALCPointers.alcGetProcAddress2_offset));
                }
                if (alcGetProcAddress2Fnptr is not null)
                {
                    alcGetProcAddressFnptr = alcGetProcAddress2Fnptr;
                }
            }
            _pointers = new(alcGetProcAddressFnptr, newDevice);
            _ = ALC.GetInteger(newDevice, OpenAL.ALC.GetPNameIV.AttributesSize);
            var deviceInvalid = ALC.GetError(newDevice) == OpenAL.ALC.ErrorCode.InvalidDevice;
            _device = newDevice;
            deviceReconnectionRequired = deviceInvalid;
            _alGetProcAddressFnptr = loader._alGetProcAddressFnptr;
            if (!deviceInvalid)
            {
                var alc = loader.ALC;
                var alGetProcAddress_fnptr = (delegate* unmanaged[Cdecl]<byte*, void*>)alc.GetProcAddress(newDevice, ALPointers.AllFunctionNames.Slice(ALPointers.alGetProcAddress_offset));
                if (alGetProcAddress_fnptr is not null)
                {
                    _alGetProcAddressFnptr = alGetProcAddress_fnptr;
                }
            }
        }

        /// <summary>
        /// Creates a new <see cref="ALCLoader"/> with the specified <paramref name="targetDevice"/>.
        /// </summary>
        /// <param name="targetDevice">The target <see cref="ALCDevice"/> to load ALC function pointers for.</param>
        /// <param name="deviceReconnectionRequired">When this method returns, contains the value which indicates whether the <paramref name="targetDevice"/> is not valid for the newly laoded <see cref="ALC"/>.</param>
        /// <param name="preferDirect">
        /// Whether <see cref="ALCLoader"/> should rather laod function pointers with <see cref="ALCFunctions.GetProcAddress2(ALCExtensions.Direct, ALCDevice, byte*)"/> instead of <see cref="ALCFunctions.GetProcAddress(ALC, ALCDevice, byte*)"/>.<br/>
        /// Setting this makes chance of <paramref name="deviceReconnectionRequired"/> being <see langword="true"/> higher, at least in Windows.
        /// </param>
        /// <returns>The newly created <see cref="ALCLoader"/>.</returns>
        public ALCLoader WithDevice(ALCDevice targetDevice, out bool deviceReconnectionRequired, bool preferDirect = true)
            => new(this, targetDevice, out deviceReconnectionRequired, preferDirect);

        /// <summary>
        /// Creates a new <see cref="ALLoader"/> with the specified <paramref name="targetContext"/>.
        /// </summary>
        /// <param name="targetContext">The target <see cref="ALCContext"/> to load AL function pointers for.</param>
        /// <returns>The newly created <see cref="ALLoader"/>.</returns>
        public ALLoader LoadALWithContext(ALCContext targetContext)
            => new(this, targetContext);
    }
}
