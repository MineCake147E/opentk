using System;

using OpenTK.Audio.OpenAL;
using OpenTK.Audio.OpenAL.ALC;

namespace OpenTK.Audio
{
    /// <summary>
    /// A container of all OpenAL AL function pointers.
    /// </summary>
    public sealed unsafe class ALLoader
    {
        private readonly ALCContext _context;
        private readonly ALCLoader _parent;
        private readonly ALPointers _pointers;

        /// <summary>
        /// The <see cref="OpenAL.AL"/> loaded by this <see cref="ALLoader"/>.
        /// </summary>
        public AL AL => new(in _pointers);

        /// <summary>
        /// The <see cref="ALCContext"/> associated with this <see cref="ALLoader"/>.
        /// </summary>
        public unsafe ALCContext Context => _context;

        /// <summary>
        /// The <see cref="ALCLoader"/> that created the <see cref="Context"/>.
        /// </summary>
        public ALCLoader Parent => _parent;

        internal ALLoader(ALCLoader loader, ALCContext context)
        {
            _context = context;
            _parent = loader;
            var loadFunction = loader.AlGetProcAddressFnptr;
            var alc = loader.ALC;
            var device = alc.GetContextsDevice(context);
            _ = alc.GetInteger(device, OpenAL.ALC.GetPNameIV.AttributesSize);
            var deviceOrContextInvalid = alc.GetError(device) != OpenAL.ALC.ErrorCode.NoError;
            if (deviceOrContextInvalid)
            {
                throw new ArgumentException("The specified loader didn't recognize the specified context!", nameof(context));
            }
            var alcGetProcAddress2_fnptr = alc._pointers._alcGetProcAddress2_fnptr;
            if (alcGetProcAddress2_fnptr is null && alc.IsExtensionPresent(device, "ALC_EXT_direct_context\0"u8))
            {
                alcGetProcAddress2_fnptr = (delegate* unmanaged[Cdecl]<IntPtr, byte*, void*>)alc.GetProcAddress(device, ALCPointers.AllFunctionNames.Slice(ALCPointers.alcGetProcAddress2_offset));
            }
            if (alcGetProcAddress2_fnptr is not null)
            {
                var alGetProcAddressDirect_fnptr = (delegate* unmanaged[Cdecl]<IntPtr, byte*, void*>)alc.Direct.GetProcAddress2(device, ALPointers.AllFunctionNames.Slice(ALPointers.alGetProcAddressDirect_offset));
                if (alGetProcAddressDirect_fnptr is not null)
                {
                    _pointers = new(alGetProcAddressDirect_fnptr, context);
                    if (_pointers._alGetProcAddress_fnptr is not null)
                    {
                        return;
                    }
                }
            }
            bool success;
            var previousContext = alc.GetCurrentContext();
            bool isContextThreadLocal = alc._pointers._alcSetThreadContext_fnptr is not null;
            var previousThreadContext = isContextThreadLocal ? alc.EXT.GetThreadContext() : previousContext;
            success = isContextThreadLocal ? alc.EXT.SetThreadContext(context) : alc.MakeContextCurrent(context);
            if (!success)
            {
                throw new ArgumentException("The specified loader didn't recognize the specified context!", nameof(context));
            }
            _pointers = new(loadFunction);
            _ = isContextThreadLocal ? alc.EXT.SetThreadContext(previousThreadContext) : alc.MakeContextCurrent(previousContext);
        }
    }
}
