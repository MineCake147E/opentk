using System;
using System.Runtime.InteropServices;

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
        private readonly ALCDevice _parentDevice;
        private readonly ALCPointers _pointers;

        internal delegate* unmanaged[Cdecl]<byte*, void*> AlGetProcAddressFnptr => _alGetProcAddressFnptr;

        /// <summary>
        /// The <see cref="OpenAL.ALC.ALC"/> loaded by this <see cref="ALCLoader"/>.
        /// </summary>
        public ALC ALC => new(in _pointers);

        /// <summary>
        /// The <see cref="ALCDevice"/> associated with this <see cref="ALCLoader"/>.
        /// This may be different from <see cref="ALCDevice"/> returned by <see cref="Parent"/>.
        /// </summary>
        public ALCDevice Device => _device;

        /// <summary>
        /// The <see cref="ALCLoader"/> that loaded this <see cref="ALCLoader"/>, and <see cref="ALCDevice"/> used while loading.
        /// </summary>
        public (ALCLoader? Loader, ALCDevice Device) Parent => (_parent, _parentDevice);

        private ALCLoader(IntPtr alHandle)
        {
            _alHandle = alHandle;
            _parentDevice = default;
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
            var alGetProcAddressFnptr = loader.AlGetProcAddressFnptr;
            var isDirect = false;
            var oldAlc = loader.ALC;
            if (preferDirect)
            {
                var alcGetProcAddress2Fnptr = loader._pointers._alcGetProcAddress2_fnptr;
                if (alcGetProcAddress2Fnptr is null)
                {
                    alcGetProcAddress2Fnptr = (delegate* unmanaged[Cdecl]<nint, byte*, void*>)oldAlc.GetProcAddress(newDevice, ALCPointers.AllFunctionNames.Slice(ALCPointers.alcGetProcAddress2_offset));
                }
                if (alcGetProcAddress2Fnptr is not null)
                {
                    alcGetProcAddressFnptr = alcGetProcAddress2Fnptr;
                }
            }
            _pointers = new(alcGetProcAddressFnptr, newDevice);
            var newAlc = ALC;
            _ = newAlc.GetInteger(newDevice, OpenAL.ALC.GetPNameIV.AttributesSize);
            var deviceInvalid = newAlc.GetError(newDevice) == OpenAL.ALC.ErrorCode.InvalidDevice;
            _parentDevice = newDevice;
            deviceReconnectionRequired = deviceInvalid;
            if (deviceInvalid)
            {
                newDevice = default;
            }
            _device = newDevice;
            isDirect = newAlc.Pointers._alcGetProcAddress2_fnptr is not null;
            delegate* unmanaged[Cdecl]<byte*, void*> newAlGetProcAddressFnptr = default;
            if (isDirect)
            {
                newAlGetProcAddressFnptr = (delegate* unmanaged[Cdecl]<byte*, void*>)newAlc.Direct.GetProcAddress2(newDevice, ALPointers.AllFunctionNames.Slice(ALPointers.alGetProcAddress_offset));
            }
            if (newAlGetProcAddressFnptr is not null)
            {
                alGetProcAddressFnptr = newAlGetProcAddressFnptr;
            }
            _alGetProcAddressFnptr = alGetProcAddressFnptr;
        }

        /// <summary>
        /// Creates a new <see cref="ALCLoader"/> with the specified <paramref name="targetDevice"/>.
        /// </summary>
        /// <param name="targetDevice">The target <see cref="ALCDevice"/> to load ALC function pointers for.</param>
        /// <param name="deviceReconnectionRequired">When this method returns, contains the value which indicates whether the <paramref name="targetDevice"/> is not valid for the newly loaded <see cref="ALC"/>.</param>
        /// <param name="preferDirect">
        /// Whether <see cref="ALCLoader"/> should rather load function pointers with <see cref="ALCFunctions.GetProcAddress2(ALCExtensions.Direct, ALCDevice, byte*)"/> instead of <see cref="ALCFunctions.GetProcAddress(ALC, ALCDevice, byte*)"/>.<br/>
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
