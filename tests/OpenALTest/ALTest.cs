using System;
using System.Buffers;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Xml.Linq;

using OpenTK.Audio;
using OpenTK.Audio.OpenAL;
using OpenTK.Audio.OpenAL.ALC;
using OpenTK.Mathematics;

namespace OpenALTest
{
    internal static class ALTest
    {
        public static unsafe int LoadEffect(AL AL, ReverbProperties preset)
        {
            AL.GetError();
            int effect = AL.EXT.GenEffect();
            AL.EXT.Effecti(effect, EffectPNameI.EffectType, (int)EffectType.EffectEaxreverb);
            var error = AL.GetError();
            if (error == OpenTK.Audio.OpenAL.ErrorCode.NoError)
            {
                Console.WriteLine("Using EAX reverb.");

                AL.EXT.EffectEaxReverb(effect, preset);
            }
            else
            {
                Console.WriteLine("Using standard reverb.");
                AL.EXT.Effecti(effect, EffectPNameI.EffectType, (int)EffectType.EffectReverb);

                AL.EXT.EffectReverb(effect, preset);
            }

            error = AL.GetError();
            if (error != OpenTK.Audio.OpenAL.ErrorCode.NoError)
            {
                Console.WriteLine($"Could not create effect, error: {error}");
            }

            return effect;
        }

        public static void Main()
        {
            Console.WriteLine("Hello!");
            var alcLoader = ALCLoader.Default;
            var ALC = alcLoader.ALC;
            var devices = ALC.GetStringList(ALCDevice.Null, OpenTK.Audio.OpenAL.ALC.StringName.DeviceSpecifier);
            Console.WriteLine($"Devices: {string.Join(", ", devices)}");

            // Get the default device, then go though all devices and select the AL soft device if it exists.
            var defaultDeviceName = ALC.GetString(ALCDevice.Null, OpenTK.Audio.OpenAL.ALC.StringName.DefaultDeviceSpecifier);
            Console.WriteLine($"Default Device: {defaultDeviceName}");
            var allDevices = ALC.GetStringList(ALCDevice.Null, OpenTK.Audio.OpenAL.ALC.StringName.AllDevicesSpecifier);
            Console.WriteLine($"All Devices:\n   {string.Join("\n   ", allDevices.Select((v, i) => $"{i}: {v}"))}");
            Console.Write($"Select Device (Max: {allDevices.Count - 1}): ");
            ushort index;
            while (!ushort.TryParse(Console.ReadLine(), out index) || index >= allDevices.Count)
            {
                Console.Write($"Select Device (Max: {allDevices.Count - 1}): ");
            }

            var deviceName = allDevices[index];
            Console.WriteLine($"Selected Device: {deviceName}");

            var device = ALC.OpenDevice(deviceName);
            var newLoader = alcLoader.WithDevice(device, out var deviceReconnectionRequired);
            if (deviceReconnectionRequired)
            {
                Console.WriteLine($"Device wrapping detected! Re-opening the device!");
                var name = ALC.GetString(device, OpenTK.Audio.OpenAL.ALC.StringName.DeviceSpecifier);
                var newALC = newLoader.ALC;
                allDevices = ALC.GetStringList(ALCDevice.Null, OpenTK.Audio.OpenAL.ALC.StringName.AllDevicesSpecifier);
                deviceName = allDevices.First(a => a.Contains(name));
                device = newALC.OpenDevice(deviceName);
                alcLoader = newLoader;
                newLoader = alcLoader.WithDevice(device, out deviceReconnectionRequired);
                if (deviceReconnectionRequired)
                {
                    device = newALC.OpenDevice();
                    newLoader = alcLoader.WithDevice(device, out deviceReconnectionRequired);
                    if (deviceReconnectionRequired)
                    {
                        throw new InvalidOperationException("Cannot initialize ALC!");
                    }
                }
                alcLoader = newLoader;
                ALC = newLoader.ALC;
            }
            else
            {
                alcLoader = newLoader;
                ALC = newLoader.ALC;
            }

            var alcExtensions = ALC.GetString(device, OpenTK.Audio.OpenAL.ALC.StringName.Extensions).Split(" ").Distinct().ToHashSet();

            var contextAttributes = new ALCContextAttributes();
            if (alcExtensions.Contains("ALC_SOFT_HRTF"))
            {
                // Enable HRTF if the extension is available.
                contextAttributes[OpenTK.Audio.OpenAL.ALC.ContextAttribute.HrtfSoft] = 1;
            }

            var context = ALC.CreateContext(device, contextAttributes);
            ALC.MakeContextCurrent(context);

            var alLoader = alcLoader.LoadALWithContext(context);
            var AL = alLoader.AL;

            if (alcExtensions.Contains("ALC_SOFT_HRTF"))
            {
                int numHRTFs = ALC.GetInteger(device, OpenTK.Audio.OpenAL.ALC.GetPNameIV.NumHrtfSpecifiersSoft);
                var hrtfs = ALC.SOFT.GetAllIndexedStringSOFT(device, OpenTK.Audio.OpenAL.ALC.IndexedStringName.HrtfSpecifierSoft, numHRTFs).ToList();
                Console.WriteLine($"All Available HRTFs:\n   {string.Join("\n   ", hrtfs)}");
            }

            CheckALError(AL, "Start");

            var alcMajorVersion = ALC.GetInteger(device, OpenTK.Audio.OpenAL.ALC.GetPNameIV.MajorVersion);
            var alcMinorVersion = ALC.GetInteger(device, OpenTK.Audio.OpenAL.ALC.GetPNameIV.MinorVersion);

            var attrs = ALC.GetContextAttributes(device);
            Console.WriteLine($"Attributes: {attrs}");

            string exts = AL.GetString(OpenTK.Audio.OpenAL.StringName.Extensions);
            string rend = AL.GetString(OpenTK.Audio.OpenAL.StringName.Renderer);
            string vend = AL.GetString(OpenTK.Audio.OpenAL.StringName.Vendor);
            string vers = AL.GetString(OpenTK.Audio.OpenAL.StringName.Version);

            Console.WriteLine($"Vendor: {vend}, \nVersion: {vers}, \nRenderer: {rend}, \nExtensions: {exts}, \nALC Version: {alcMajorVersion}.{alcMinorVersion}, \nALC Extensions: {string.Join(", ", alcExtensions)}");

            Console.WriteLine("Available devices: ");
            var list = ALC.GetStringList(ALCDevice.Null, OpenTK.Audio.OpenAL.ALC.StringName.AllDevicesSpecifier);
            foreach (var item in list)
            {
                Console.WriteLine("  " + item);
            }

            var allCaptureDevices = ALC.GetStringList(ALCDevice.Null, OpenTK.Audio.OpenAL.ALC.StringName.CaptureDeviceSpecifier);
            string captureDeviceName = "";
            if ((allCaptureDevices?.Count ?? 0) > 0)
            {
                Console.WriteLine($"Available Capture Devices:\n   {string.Join("\n   ", allCaptureDevices.Select((v, i) => $"{i}: {v}"))}");
                Console.Write($"Select Capture Device (Max: {allCaptureDevices.Count - 1}): ");
                while (!ushort.TryParse(Console.ReadLine(), out index) || index >= allCaptureDevices.Count)
                {
                    Console.Write($"Select Capture Device (Max: {allCaptureDevices.Count - 1}): ");
                }
                captureDeviceName = allCaptureDevices[index];
                Console.WriteLine($"Selected Capture Device: {captureDeviceName}");
            }

            // Record a second of data
            CheckALError(AL, "Before record");
            short[] recording = new short[44100 * 4];
            if (alcExtensions.Contains("ALC_EXT_CAPTURE") && !string.IsNullOrEmpty(captureDeviceName))
            {
                var captureDevice = ALC.CaptureOpenDevice(captureDeviceName, 44100u, Format.Mono16, 1024);
                string defaultCaptureName = ALC.GetString(captureDevice, OpenTK.Audio.OpenAL.ALC.StringName.CaptureDeviceSpecifier);
                string version = AL.GetString(OpenTK.Audio.OpenAL.StringName.Version);
                string vendor = AL.GetString(OpenTK.Audio.OpenAL.StringName.Vendor);
                string renderer = AL.GetString(OpenTK.Audio.OpenAL.StringName.Renderer);
                Console.WriteLine($"Recording 4 seconds of audio from {defaultCaptureName} OpenAL version: {version} {vendor} {renderer}...");
                ALC.CaptureStart(captureDevice);

                int current = 0;
                while (current < recording.Length)
                {
                    int samplesAvailable = ALC.GetInteger(captureDevice, OpenTK.Audio.OpenAL.ALC.GetPNameIV.CaptureSamples);
                    if (samplesAvailable > 512)
                    {
                        int samplesToRead = Math.Min(samplesAvailable, recording.Length - current);
                        ALC.CaptureSamples(captureDevice, ref recording[current], samplesToRead);
                        current += samplesToRead;
                    }
                    Thread.Yield();
                }

                ALC.CaptureStop(captureDevice);
                ALC.CaptureCloseDevice(captureDevice);
            }
            CheckALError(AL, "After record");

            int auxSlot = 0;
            if (ALC.IsExtensionPresent(device, "ALC_EXT_EFX"))
            {
                Console.WriteLine("EFX extension is present!!");
                int effect = LoadEffect(AL, ReverbPresets.CastleHall);
                AL.EXT.GenAuxiliaryEffectSlot(out auxSlot);
                AL.EXT.AuxiliaryEffectSloti(auxSlot, AuxEffectSlotPNameI.EffectslotEffect, effect);
            }

            // Playback the recorded data
            CheckALError(AL, "Before data");
            AL.GenBuffer(out int alBuffer);
            // short[] sine = new short[44100 * 1];
            // FillSine(sine, 4400, 44100);
            // FillSine(recording, 440, 44100);
            AL.BufferData(alBuffer, Format.Mono16, recording.AsSpan(), 44100);
            CheckALError(AL, "After data");

            AL.Listenerf(ListenerPNameF.Gain, 0.1f);

            AL.GenSource(out int alSource);
            AL.Sourcef(alSource, SourcePNameF.Gain, 1f);
            AL.Sourcei(alSource, SourcePNameI.Buffer, alBuffer);
            if (ALC.IsExtensionPresent(device, "ALC_EXT_EFX"))
            {
                AL.Source3i(alSource, SourcePName3I.AuxiliarySendFilter, auxSlot, 0, 0);
            }
            AL.SourcePlay(alSource);

            Console.WriteLine("Before Playing: " + AL.GetString((OpenTK.Audio.OpenAL.StringName)AL.GetError()));

            bool logLatency = false;

            if (ALC.IsExtensionPresent(device, "ALC_SOFT_device_clock"))
            {
                long[] clockLatency = new long[2];
                ALC.SOFT.GetInteger64vSOFT(device, GetPNameI64V.DeviceClockSoft, 2, clockLatency);
                if (logLatency) Console.WriteLine("Clock: " + clockLatency[0] + ", Latency: " + clockLatency[1]);
                CheckALError(AL, " ");
            }

            Span<long> offsets = stackalloc long[2];
            if (AL.IsExtensionPresent("AL_SOFT_source_latency"))
            {
                Vector2d values = default;
                AL.SOFT.GetSourcedvSOFT(alSource, SourceGetPNameDV.SecOffsetLatencySoft, ref values.X);
                AL.SOFT.GetSourcei64vSOFT(alSource, SourceGetPNameI64V.SampleOffsetLatencySoft, offsets);
                if (logLatency)
                {
                    Console.WriteLine("Source latency: " + values);
                    Console.WriteLine($"Source latency 2: {offsets[0] / (float)(1L << 32)}; {offsets[1]}");
                }
                CheckALError(AL, " ");
            }

            while ((SourceState)AL.GetSourcei(alSource, SourceGetPNameI.SourceState) == SourceState.Playing)
            {
                if (AL.IsExtensionPresent("AL_SOFT_source_latency"))
                {
                    Vector2d values = default;
                    AL.SOFT.GetSourcedvSOFT(alSource, SourceGetPNameDV.SecOffsetLatencySoft, ref values.X);
                    AL.SOFT.GetSourcei64vSOFT(alSource, SourceGetPNameI64V.SampleOffsetLatencySoft, offsets);
                    if (logLatency)
                    {
                        Console.WriteLine("Source latency: " + values);
                        Console.WriteLine($"Source latency 2: {offsets[0] / (float)(1L << 32)}; {offsets[1]}");
                    }
                    CheckALError(AL, " ");
                }
                if (ALC.IsExtensionPresent(device, "ALC_SOFT_device_clock"))
                {
                    long[] clockLatency = new long[2];
                    ALC.SOFT.GetInteger64vSOFT(device, OpenTK.Audio.OpenAL.ALC.GetPNameI64V.DeviceClockSoft, 1, clockLatency);
                    if (logLatency) Console.WriteLine("Clock: " + clockLatency[0] + ", Latency: " + clockLatency[1]);
                    CheckALError(AL, " ");
                }

                Thread.Sleep(10);
            }

            AL.SourceStop(alSource);

            // Test float32 format extension
            if (AL.IsExtensionPresent("AL_EXT_float32"))
            {
                Console.WriteLine("Testing float32 format extension with a sine wave...");

                const int SampleRate = 44100;
                const int Frequency = 440;
                float[] sine = new float[SampleRate * 4];
                for (int i = 0; i < sine.Length; i++)
                {
                    sine[i] = MathF.Sin(Frequency * MathF.PI * 2 * (i / (float)SampleRate));
                }

                var buffer = AL.GenBuffer();
                AL.BufferData(buffer, Format.MonoFloat32, sine.AsSpan(), SampleRate);

                AL.Listenerf(ListenerPNameF.Gain, 0.1f);

                AL.Sourcef(alSource, SourcePNameF.Gain, 1f);
                AL.Sourcei(alSource, SourcePNameI.Buffer, buffer);

                AL.SourcePlay(alSource);

                Stopwatch watch = Stopwatch.StartNew();
                while ((SourceState)AL.GetSourcei(alSource, SourceGetPNameI.SourceState) == SourceState.Playing)
                {
                    float x = MathF.Cos((float)watch.Elapsed.TotalSeconds * MathF.PI * 1f);
                    float y = MathF.Sin((float)watch.Elapsed.TotalSeconds * MathF.PI * 1f);
                    float z = 0;

                    AL.Source3f(alSource, SourcePName3F.Position, x, y, z);
                    AL.Source3f(alSource, SourcePName3F.Velocity, y, -x, z);
                    Thread.Sleep(10);
                }

                AL.SourceStop(alSource);
                AL.Source3f(alSource, SourcePName3F.Position, 0, 0, 0);
            }

            // Test double format extension
            if (AL.IsExtensionPresent("AL_EXT_double"))
            {
                Console.WriteLine("Testing float64 format extension with a saw wave...");

                double[] saw = new double[44100 * 2];
                for (int i = 0; i < saw.Length; i++)
                {
                    var t = i / (double)saw.Length * 440;
                    saw[i] = t - Math.Floor(t);
                }

                var buffer = AL.GenBuffer();
                AL.BufferData(buffer, Format.MonoDoubleExt, saw, saw.Length * sizeof(double), 44100);

                AL.Listenerf(ListenerPNameF.Gain, 0.05f);

                AL.Sourcef(alSource, SourcePNameF.Gain, 1f);
                AL.Sourcei(alSource, SourcePNameI.Buffer, buffer);

                AL.SourcePlay(alSource);

                while ((SourceState)AL.GetSourcei(alSource, SourceGetPNameI.SourceState) == SourceState.Playing)
                {
                    Thread.Sleep(10);
                }

                AL.SourceStop(alSource);
            }

            if (AL.IsExtensionPresent("AL_EXT_direct_context"))
            {
                Console.WriteLine("Testing AL_EXT_direct_context extension with a sine wave...");
                const int SampleRate = 44100;
                const int Frequency = 440;
                short[] sine = new short[SampleRate * 4];
                for (int i = 0; i < sine.Length; i++)
                {
                    sine[i] = (short)Math.Clamp(MathF.Sin(Frequency * MathF.PI * 2 * (i / (float)SampleRate)) * -short.MinValue, short.MinValue, short.MaxValue);
                }

                alSource = AL.Direct.GenSourceDirect(context);
                var buffer = AL.Direct.GenBufferDirect(context);
                AL.Direct.BufferDataDirect(context, buffer, Format.Mono16, sine.AsSpan(), SampleRate);

                AL.Direct.ListenerfDirect(context, ListenerPNameF.Gain, 0.1f);

                AL.Direct.SourcefDirect(context, alSource, SourcePNameF.Gain, 1f);
                AL.Direct.SourceiDirect(context, alSource, SourcePNameI.Buffer, buffer);

                AL.Direct.SourcePlayDirect(context, alSource);

                Console.WriteLine($"Start Playing...");
                Stopwatch watch = Stopwatch.StartNew();
                while ((SourceState)AL.Direct.GetSourceiDirect(context, alSource, SourceGetPNameI.SourceState) == SourceState.Playing)
                {
                    float x = MathF.Cos((float)watch.Elapsed.TotalSeconds * MathF.PI * 1f);
                    float y = MathF.Sin((float)watch.Elapsed.TotalSeconds * MathF.PI * 1f);
                    float z = 0;

                    AL.Direct.Source3fDirect(context, alSource, SourcePName3F.Position, x, y, z);
                    AL.Direct.Source3fDirect(context, alSource, SourcePName3F.Velocity, y, -x, z);
                    Thread.Sleep(10);
                }

                AL.Direct.SourceStopDirect(context, alSource);
                AL.Direct.Source3fDirect(context, alSource, SourcePName3F.Position, 0, 0, 0);
            }

            Console.WriteLine("Goodbye!");

            ALC.MakeContextCurrent(ALCContext.Null);
            ALC.DestroyContext(context);
            ALC.CloseDevice(device);
        }

        public static bool CheckALError(AL AL, string str)
        {
            bool hadError = false;
            var error = AL.GetError();
            if (error != OpenTK.Audio.OpenAL.ErrorCode.NoError)
            {
                hadError = true;
                Console.WriteLine($"ALError at '{str}': {AL.GetString((OpenTK.Audio.OpenAL.StringName)error)}");
            }
            return hadError;
        }

        public static void FillSine(short[] buffer, float frequency, float sampleRate)
        {
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = (short)(MathF.Sin(i * frequency * MathF.PI * 2 / sampleRate) * short.MaxValue);
            }
        }
    }
}
