using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

using OpenTK.Audio.OpenAL;
using OpenTK.Audio.OpenAL.ALC;

namespace OpenTK.Audio
{
    /// <summary>
    /// A container of all OpenAL function pointers.
    /// It also manages the lifetime of <see cref="ALPointers"/> and <see cref="ALCPointers"/> storages.
    /// </summary>
    public readonly unsafe partial struct ALLoader
    {
        /// <summary>
        /// The shared instance of <see cref="ALLoader"/>.
        /// </summary>
        public static ALLoader Default { get; }

        private static readonly OpenALLibraryNameContainer NameContainer = new OpenALLibraryNameContainer();

        internal static readonly unsafe delegate* unmanaged[Cdecl]<ALCDevice, byte*, IntPtr> AlcGetProcAddress;
        internal static readonly unsafe delegate* unmanaged[Cdecl]<byte*, IntPtr> AlGetProcAddress;

        private readonly IntPtr _alHandle;
        private readonly ALCDevice _device;
        internal readonly ALPointers* AlPointers;
        internal readonly ALCPointers* AlcPointers;

        private readonly ALPointers[] _alPointersPinnedArray;
        private readonly ALCPointers[] _alcPointersPinnedArray;

        /// <summary>
        /// The <see cref="ALCDevice"/> associated with this <see cref="ALLoader"/>.
        /// </summary>
        public unsafe ALCDevice Device => _device;

        /// <summary>
        /// Gets the container of <see cref="OpenAL.AL"/> APIs.
        /// </summary>
        public AL AL => new(AlPointers, _alPointersPinnedArray);

        /// <summary>
        /// Gets the container of <see cref="OpenAL.ALC.ALC"/> APIs.
        /// </summary>
        public ALC ALC => new(AlcPointers, _alcPointersPinnedArray);

        static ALLoader()
        {
            var alHandle = NativeLibrary.Load(NameContainer.GetLibraryName());
            Default = new(alHandle, true);
            AlcGetProcAddress = (delegate* unmanaged[Cdecl]<ALCDevice, byte*, IntPtr>)Default.ALC._pointers->_alcGetProcAddress_fnptr;
            AlGetProcAddress = (delegate* unmanaged[Cdecl]<byte*, IntPtr>)Default.AL._pointers->_alGetProcAddress_fnptr;
        }

        private static T[] AllocatePinned<T>() where T : struct
            => GC.AllocateArray<T>(1, true);

        private unsafe ALLoader(IntPtr alHandle, bool isLazy = false)
        {
            ArgumentNullException.ThrowIfNull((void*)alHandle);
            _alHandle = alHandle;
            _device = default;
            _alPointersPinnedArray = AllocatePinned<ALPointers>();
            _alcPointersPinnedArray = AllocatePinned<ALCPointers>();
            ref var alPointersRef = ref MemoryMarshal.GetArrayDataReference(_alPointersPinnedArray);
            ref var alcPointersRef = ref MemoryMarshal.GetArrayDataReference(_alcPointersPinnedArray);
            AlPointers = (ALPointers*)Unsafe.AsPointer(ref alPointersRef);
            AlcPointers = (ALCPointers*)Unsafe.AsPointer(ref alcPointersRef);
            NativeLibrary.TryGetExport(alHandle, "alcGetProcAddress", out var alcGetProcAddressFnptr);
            alcPointersRef._alcGetProcAddress_fnptr = (delegate* unmanaged[Cdecl]<IntPtr, byte*, void*>)alcGetProcAddressFnptr;
            NativeLibrary.TryGetExport(alHandle, "alGetProcAddress", out var alGetProcAddressFnptr);
            alPointersRef._alGetProcAddress_fnptr = (delegate* unmanaged[Cdecl]<byte*, void*>)alGetProcAddressFnptr;
            if (isLazy)
            {
                ALCPointers.InitializeLazyLoaders(ref alcPointersRef);
                ALPointers.InitializeLazyLoaders(ref alPointersRef);
            }
            else
            {
                ALCPointers.InitializePointers((delegate* unmanaged[Cdecl]<IntPtr, byte*, void*>)alcGetProcAddressFnptr, (nint)ALCDevice.Null, AlcPointers);
                ALPointers.InitializePointers((delegate* unmanaged[Cdecl]<byte*, void*>)alGetProcAddressFnptr, AlPointers);
            }
        }

        private unsafe ALLoader(ALLoader loader, ALCDevice device)
        {
            _alHandle = loader._alHandle;
            _device = device;
            _alPointersPinnedArray = AllocatePinned<ALPointers>();
            _alcPointersPinnedArray = AllocatePinned<ALCPointers>();
            ref var alPointersRef = ref MemoryMarshal.GetArrayDataReference(_alPointersPinnedArray);
            ref var alcPointersRef = ref MemoryMarshal.GetArrayDataReference(_alcPointersPinnedArray);
            AlPointers = (ALPointers*)Unsafe.AsPointer(ref alPointersRef);
            AlcPointers = (ALCPointers*)Unsafe.AsPointer(ref alcPointersRef);
            ALCPointers.InitializePointers(loader.ALC.Pointers._alcGetProcAddress_fnptr, (nint)device, AlcPointers);
            ALPointers.InitializePointers(loader.AL.Pointers._alGetProcAddress_fnptr, AlPointers);
        }

        private ALLoader(nint alHandle, ALCDevice device, ALPointers[] alPointersPinnedArray, ALCPointers[] alcPointersPinnedArray)
        {
            _alHandle = alHandle;
            _device = device;
            _alPointersPinnedArray = alPointersPinnedArray ?? throw new ArgumentNullException(nameof(alPointersPinnedArray));
            _alcPointersPinnedArray = alcPointersPinnedArray ?? throw new ArgumentNullException(nameof(alcPointersPinnedArray));
            ref var alPointersRef = ref MemoryMarshal.GetArrayDataReference(_alPointersPinnedArray);
            ref var alcPointersRef = ref MemoryMarshal.GetArrayDataReference(_alcPointersPinnedArray);
            AlPointers = (ALPointers*)Unsafe.AsPointer(ref alPointersRef);
            AlcPointers = (ALCPointers*)Unsafe.AsPointer(ref alcPointersRef);
        }

        /// <summary>
        /// Retrieves the address of a specified context extension function.
        /// </summary>
        /// <param name="procName">The name of the function to retrieve the pointer of.</param>
        /// <returns>The address of the function, or <c>0</c> if it is not found.</returns>
        public unsafe IntPtr ALCGetProcAddress(string procName) => (IntPtr)ALC.GetProcAddress(Device, procName);

        /// <summary>
        /// Retrieves the address of a specified context extension function.
        /// </summary>
        /// <param name="nullTerminatedUtf8ProcName">The name of the function to retrieve the pointer of, encoded in UTF-8 and terminated with null character.</param>
        /// <returns>The address of the function, or <c>0</c> if it is not found.</returns>
        /// <exception cref="ArgumentException">The provided <paramref name="nullTerminatedUtf8ProcName"/> is not null-terminated.</exception>
        public unsafe IntPtr ALCGetProcAddress(scoped ReadOnlySpan<byte> nullTerminatedUtf8ProcName) => (IntPtr)ALC.GetProcAddress(Device, nullTerminatedUtf8ProcName);

        internal static unsafe IntPtr DefaultALCGetProcAddress(scoped ReadOnlySpan<byte> nullTerminatedUtf8ProcName)
        {
            if (nullTerminatedUtf8ProcName[^1] != 0)
            {
                throw new ArgumentException("The provided span is not null-terminated.", nameof(nullTerminatedUtf8ProcName));
            }
            fixed (byte* procNamePtr = nullTerminatedUtf8ProcName)
            {
                return AlcGetProcAddress(ALCDevice.Null, procNamePtr);
            }
        }

        internal static unsafe IntPtr DefaultALCGetProcAddress(byte* procNamePtr) => AlcGetProcAddress(ALCDevice.Null, procNamePtr);

        /// <summary>
        /// Returns the address of an OpenAL extension function.
        /// </summary>
        /// <param name="procName">The name of the function to retrieve the pointer of.</param>
        /// <returns>The address of the function, or <c>0</c> if it is not found.</returns>
        public unsafe IntPtr ALGetProcAddress(string procName)
        {
            byte* procNamePtr = (byte*)Marshal.StringToCoTaskMemUTF8(procName);
            IntPtr ret = (IntPtr)AL.GetProcAddress(procNamePtr);
            Marshal.FreeCoTaskMem((IntPtr)procNamePtr);
            return ret;
        }

        /// <summary>
        /// Returns the address of an OpenAL extension function.
        /// </summary>
        /// <param name="nullTerminatedUtf8ProcName">The name of the function to retrieve the pointer of, encoded in UTF-8 and terminated with null character.</param>
        /// <returns>The address of the function, or <c>0</c> if it is not found.</returns>
        public unsafe IntPtr ALGetProcAddress(scoped ReadOnlySpan<byte> nullTerminatedUtf8ProcName)
        {
            if (nullTerminatedUtf8ProcName[^1] != 0)
            {
                throw new ArgumentException("The provided span is not null-terminated.", nameof(nullTerminatedUtf8ProcName));
            }
            fixed (byte* procNamePtr = nullTerminatedUtf8ProcName)
            {
                return (IntPtr)AL.GetProcAddress(procNamePtr);
            }
        }

        internal static unsafe IntPtr DefaultALGetProcAddress(scoped ReadOnlySpan<byte> nullTerminatedUtf8ProcName)
        {
            if (nullTerminatedUtf8ProcName[^1] != 0)
            {
                throw new ArgumentException("The provided span is not null-terminated.", nameof(nullTerminatedUtf8ProcName));
            }
            fixed (byte* procNamePtr = nullTerminatedUtf8ProcName)
            {
                return AlGetProcAddress(procNamePtr);
            }
        }

        internal static unsafe IntPtr DefaultALGetProcAddress(byte* procNamePtr) => AlGetProcAddress(procNamePtr);

        /// <summary>
        /// Loads the set of <see cref="OpenAL.ALC.ALC"/> and <see cref="OpenAL.AL"/> functions from specified <paramref name="path"/> by using <see cref="NativeLibrary.Load(string)"/>.
        /// </summary>
        /// <param name="path">The path of the OpenAL native library file to be loaded.</param>
        /// <returns>The newly created <see cref="ALLoader"/>.</returns>
        public static ALLoader LoadFromFile(string path) => new(NativeLibrary.Load(path), false);

        /// <summary>
        /// Loads the set of <see cref="OpenAL.ALC.ALC"/> functions with specified <paramref name="device"/>, and creates a new instance of <see cref="ALLoader"/>.
        /// </summary>
        /// <param name="device">The <see cref="ALCDevice"/> to load all pointers of <see cref="OpenAL.ALC.ALC"/> functions with.</param>
        /// <returns>The newly created <see cref="ALLoader"/>.</returns>
        public ALLoader LoadWithDevice(ALCDevice device) => new(this, device);

        /// <summary>
        /// Loads the set of <see cref="OpenAL.ALC.ALC"/> and <see cref="OpenAL.AL"/> functions with <see cref="ALCFunctions.GetProcAddress2(ALCExtensions.Direct, ALCDevice, byte*)"/> and creates a new instance of <see cref="ALLoader"/>.
        /// </summary>
        /// <returns>The newly created <see cref="ALLoader"/> with the <see cref="Device"/> reset to <see cref="ALCDevice.Null"/>.</returns>
        public ALLoader LoadDirectContextFunctions()
        {
            var newLoader = new ALLoader(_alHandle, default, _alPointersPinnedArray, _alcPointersPinnedArray);
            fixed (byte* alAllFunctionsNames = ALPointers.AllFunctionNames)
            {
                fixed (byte* alcAllFunctionsNames = ALCPointers.AllFunctionNames)
                {
                    if (ALC.IsExtensionPresent(_device, "ALC_EXT_direct_context\0"u8))
                    {
                        var alcGetProcAddress2_fnptr = ALC.GetProcAddress(_device, alcAllFunctionsNames + ALCPointers.alcGetProcAddress2_offset);
                        if (alcGetProcAddress2_fnptr != null)
                        {
                            var alPointersPinnedArray = AllocatePinned<ALPointers>();
                            var alcPointersPinnedArray = AllocatePinned<ALCPointers>();
                            var alPointers = (ALPointers*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(alPointersPinnedArray));
                            var alcPointers = (ALCPointers*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(alcPointersPinnedArray));
                            ALCPointers.InitializePointers((delegate* unmanaged[Cdecl]<IntPtr, byte*, void*>)alcGetProcAddress2_fnptr, (nint)_device, alcPointers);
                            var alcGetProcAddress_fnptr = alcPointers->_alcGetProcAddress_fnptr;
                            ALPointers.InitializePointersByDeviceOrContext(alcGetProcAddress_fnptr, 0, alPointers);
                            newLoader = new ALLoader(_alHandle, default, alPointersPinnedArray, alcPointersPinnedArray);
                        }
                    }
                }
            }
            return newLoader;
        }

        /// <summary>
        /// Loads the set of <see cref="OpenAL.AL"/> functions with <see cref="ALFunctions.GetProcAddressDirect(ALExtensions.Direct, ALCContext, byte*)"/> and creates a new instance of <see cref="ALLoader"/>.
        /// </summary>
        /// <param name="context">The <see cref="ALCContext"/> to load <see cref="OpenAL.AL"/> functions with.</param>
        /// <returns>The newly created <see cref="ALLoader"/> with updated <see cref="AL"/>.</returns>
        public ALLoader LoadWithContext(ALCContext context)
        {
            var newLoader = this;
            fixed (byte* alAllFunctionsNames = ALPointers.AllFunctionNames)
            {
                var alGetProcAddressDirect_fnptr = newLoader.ALC.Direct.GetProcAddress2(_device, ALPointers.AllFunctionNames.Slice(ALPointers.alGetProcAddressDirect_offset));
                if (alGetProcAddressDirect_fnptr != null)
                {
                    var alPointersPinnedArray = AllocatePinned<ALPointers>();
                    var alPointers = (ALPointers*)Unsafe.AsPointer(ref MemoryMarshal.GetArrayDataReference(alPointersPinnedArray));
                    ALPointers.InitializePointersByDeviceOrContext((delegate* unmanaged[Cdecl]<IntPtr, byte*, void*>)alGetProcAddressDirect_fnptr, context, alPointers);
                    newLoader = new ALLoader(_alHandle, default, alPointersPinnedArray, _alcPointersPinnedArray);
                }
            }
            return newLoader;
        }

        /// <summary>
        /// Deconstructs <see cref="ALLoader"/> by <see cref="AL"/> and <see cref="ALC"/>.
        /// </summary>
        /// <param name="al">When this method returns, represents the <see cref="AL"/> value of this <see cref="ALLoader"/> instance.</param>
        /// <param name="alc">When this method returns, represents the <see cref="ALC"/> value of this <see cref="ALLoader"/> instance.</param>
        public void Deconstruct(out AL al, out ALC alc) => (al, alc) = (AL, ALC);
    }
}
