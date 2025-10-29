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
            var success = loader.ALC.MakeContextCurrent(context);
            if (!success)
            {
                throw new ArgumentException("The specified laoder didn't recognize the specified context!", nameof(context));
            }
            _pointers = new(loadFunction);
        }
    }
}
