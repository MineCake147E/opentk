using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;

namespace OpenTK.Audio.OpenAL
{
    /// <summary>
    /// Functions from <see cref="AL"/>.
    /// </summary>
    public static unsafe partial class ALFunctions
    {
        /// <summary>
        /// Load all <see cref="EffectType.EffectEaxreverb"/> properties while minimizing the number of GC Transitions.
        /// </summary>
        /// <param name="ext">The container of native function pointers.</param>
        /// <param name="effect">The effect ID.</param>
        /// <param name="properties">A set of predefined reverb properties.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void EffectEaxReverb(this ALExtensions.EXT ext, int effect, ReverbProperties properties)
        {
            delegate* unmanaged[Cdecl]<int, ReverbProperties*, ALPointers*, void> ptr = &EffectEaxReverbInternal;
            var pointers = ext.AL._pointers;
            // Perform GC Transition once for all calls.
            ptr(effect, &properties, pointers);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static unsafe void EffectEaxReverbInternal(int effect, ReverbProperties* properties, ALPointers* pointers)
        {
            var alEffectf = (delegate* unmanaged[Cdecl, SuppressGCTransition]<int, int, float, void>)pointers->_alEffectf_fnptr;
            var alEffectfv = (delegate* unmanaged[Cdecl, SuppressGCTransition]<int, int, float*, void>)pointers->_alEffectfv_fnptr;
            var alEffecti = (delegate* unmanaged[Cdecl, SuppressGCTransition]<int, int, int, void>)pointers->_alEffecti_fnptr;
            alEffectf(effect, (int)EffectPNameF.EaxreverbDensity, properties->Density);
            alEffectf(effect, (int)EffectPNameF.EaxreverbDiffusion, properties->Diffusion);
            alEffectf(effect, (int)EffectPNameF.EaxreverbGain, properties->Gain);
            alEffectf(effect, (int)EffectPNameF.EaxreverbGainhf, properties->GainHF);
            alEffectf(effect, (int)EffectPNameF.EaxreverbGainlf, properties->GainLF);
            alEffectf(effect, (int)EffectPNameF.EaxreverbDecayTime, properties->DecayTime);
            alEffectf(effect, (int)EffectPNameF.EaxreverbDecayHfratio, properties->DecayHFRatio);
            alEffectf(effect, (int)EffectPNameF.EaxreverbDecayLfratio, properties->DecayLFRatio);
            alEffectf(effect, (int)EffectPNameF.EaxreverbReflectionsGain, properties->ReflectionsGain);
            alEffectf(effect, (int)EffectPNameF.EaxreverbReflectionsDelay, properties->ReflectionsDelay);
            alEffectfv(effect, (int)EffectPNameFV.EaxreverbReflectionsPan, (float*)&properties->ReflectionsPan);
            alEffectf(effect, (int)EffectPNameF.EaxreverbLateReverbGain, properties->LateReverbGain);
            alEffectf(effect, (int)EffectPNameF.EaxreverbLateReverbDelay, properties->LateReverbDelay);
            alEffectfv(effect, (int)EffectPNameFV.EaxreverbLateReverbPan, (float*)&properties->LateReverbPan);
            alEffectf(effect, (int)EffectPNameF.EaxreverbEchoTime, properties->EchoTime);
            alEffectf(effect, (int)EffectPNameF.EaxreverbEchoDepth, properties->EchoDepth);
            alEffectf(effect, (int)EffectPNameF.EaxreverbModulationTime, properties->ModulationTime);
            alEffectf(effect, (int)EffectPNameF.EaxreverbModulationDepth, properties->ModulationDepth);
            alEffectf(effect, (int)EffectPNameF.EaxreverbAirAbsorptionGainhf, properties->AirAbsorptionGainHF);
            alEffectf(effect, (int)EffectPNameF.EaxreverbHfreference, properties->HFReference);
            alEffectf(effect, (int)EffectPNameF.EaxreverbLfreference, properties->LFReference);
            alEffectf(effect, (int)EffectPNameF.EaxreverbRoomRolloffFactor, properties->RoomRolloffFactor);
            alEffecti(effect, (int)EffectPNameI.EaxreverbDecayHflimit, properties->DecayHFLimit);
        }

        /// <summary>
        /// Load all <see cref="EffectType.EffectReverb"/> properties while minimizing the number of GC Transitions.
        /// </summary>
        /// <param name="ext">The container of native function pointers.</param>
        /// <param name="effect">The effect ID.</param>
        /// <param name="properties">A set of predefined reverb properties.</param>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static unsafe void EffectReverb(this ALExtensions.EXT ext, int effect, ReverbProperties properties)
        {
            delegate* unmanaged[Cdecl]<int, ReverbProperties*, ALPointers*, void> ptr = &EffectReverbInternal;
            var pointers = ext.AL._pointers;
            // Perform GC Transition once for all calls.
            ptr(effect, &properties, pointers);
        }

        [UnmanagedCallersOnly(CallConvs = [typeof(CallConvCdecl)])]
        [MethodImpl(MethodImplOptions.AggressiveOptimization)]
        private static unsafe void EffectReverbInternal(int effect, ReverbProperties* properties, ALPointers* pointers)
        {
            var alEffectf = (delegate* unmanaged[Cdecl, SuppressGCTransition]<int, int, float, void>)pointers->_alEffectf_fnptr;
            var alEffecti = (delegate* unmanaged[Cdecl, SuppressGCTransition]<int, int, int, void>)pointers->_alEffecti_fnptr;
            alEffectf(effect, (int)EffectPNameF.ReverbDensity, properties->Density);
            alEffectf(effect, (int)EffectPNameF.ReverbDiffusion, properties->Diffusion);
            alEffectf(effect, (int)EffectPNameF.ReverbGain, properties->Gain);
            alEffectf(effect, (int)EffectPNameF.ReverbGainhf, properties->GainHF);
            alEffectf(effect, (int)EffectPNameF.ReverbDecayTime, properties->DecayTime);
            alEffectf(effect, (int)EffectPNameF.ReverbDecayHfratio, properties->DecayHFRatio);
            alEffectf(effect, (int)EffectPNameF.ReverbReflectionsGain, properties->ReflectionsGain);
            alEffectf(effect, (int)EffectPNameF.ReverbReflectionsDelay, properties->ReflectionsDelay);
            alEffectf(effect, (int)EffectPNameF.ReverbLateReverbGain, properties->LateReverbGain);
            alEffectf(effect, (int)EffectPNameF.ReverbLateReverbDelay, properties->LateReverbDelay);
            alEffectf(effect, (int)EffectPNameF.ReverbAirAbsorptionGainhf, properties->AirAbsorptionGainHF);
            alEffectf(effect, (int)EffectPNameF.ReverbRoomRolloffFactor, properties->RoomRolloffFactor);
            alEffecti(effect, (int)EffectPNameI.ReverbDecayHflimit, properties->DecayHFLimit);
        }
    }
}
