using System;
using System.IO;
using System.Text;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// Builds tiny synthetic Decent Sampler libraries for the engine tests, and the measuring helpers the
/// tests read renders with. Nothing here touches a real sample library.
/// </summary>
/// <remarks>
/// Two sharp edges the SFZ suite learned and this one inherits: the synthesizer renders in 64-frame
/// blocks, so consecutive measurements on one instance need block-aligned frame counts or a fresh
/// synthesizer; and a PEAK measurement cannot tell a fade from a steady level, so anything asserting an
/// envelope, a crossfade or a silencing time measures RMS over a window instead.
/// </remarks>
internal sealed class DecentSamplerEngineFixtures : IDisposable
{
    /// <summary>The block size every engine test renders in, matching the engine's own default.</summary>
    public const int BlockSize = 64;

    /// <summary>The sample rate every engine test renders at.</summary>
    public const int SampleRate = 44100;

    private DecentSamplerEngineFixtures(string directory)
    {
        Directory = directory;
    }

    /// <summary>The temp directory holding the library. Deleted on dispose.</summary>
    public string Directory { get; }

    /// <summary>Creates a fresh temp directory to build a library in.</summary>
    public static DecentSamplerEngineFixtures Create()
    {
        var directory = Path.Combine(Path.GetTempPath(), "codebrix-dse-" + Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(directory);
        return new DecentSamplerEngineFixtures(directory);
    }

    /// <summary>A full path inside the library folder, creating the folders it needs.</summary>
    /// <param name="relativePath">The path relative to the library folder.</param>
    /// <returns>The full path.</returns>
    public string PathFor(string relativePath)
    {
        var full = Path.Combine(Directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        var parent = Path.GetDirectoryName(full);

        if (parent != null)
        {
            System.IO.Directory.CreateDirectory(parent);
        }

        return full;
    }

    /// <summary>Writes a 32-bit float WAV holding one constant value, so gain assertions are arithmetic.</summary>
    /// <param name="relativePath">Where to write it, relative to the library folder.</param>
    /// <param name="value">The sample value.</param>
    /// <param name="frames">How many frames to write.</param>
    /// <param name="channels">1 for mono, 2 for stereo.</param>
    /// <param name="sampleRate">The recorded sample rate.</param>
    /// <returns>The full path of the file.</returns>
    public string WriteConstantWav(
        string relativePath, float value = 0.5f, int frames = 44100, int channels = 1,
        int sampleRate = SampleRate)
    {
        var samples = new float[frames * channels];
        Array.Fill(samples, value);
        return WriteWav(relativePath, samples, channels, sampleRate);
    }

    /// <summary>
    /// Writes a 32-bit float stereo WAV with a different constant in each channel, for the pan and
    /// stereo-source tests.
    /// </summary>
    /// <param name="relativePath">Where to write it, relative to the library folder.</param>
    /// <param name="left">The left channel value.</param>
    /// <param name="right">The right channel value.</param>
    /// <param name="frames">How many frames to write.</param>
    /// <returns>The full path of the file.</returns>
    public string WriteStereoConstantWav(string relativePath, float left, float right, int frames = 44100)
    {
        var samples = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            samples[i * 2] = left;
            samples[i * 2 + 1] = right;
        }

        return WriteWav(relativePath, samples, 2, SampleRate);
    }

    /// <summary>Writes a 32-bit float mono WAV holding a sine, so pitch is measurable.</summary>
    /// <param name="relativePath">Where to write it, relative to the library folder.</param>
    /// <param name="frequency">The tone frequency in Hz.</param>
    /// <param name="amplitude">The peak amplitude.</param>
    /// <param name="frames">How many frames to write.</param>
    /// <param name="sampleRate">The recorded sample rate.</param>
    /// <returns>The full path of the file.</returns>
    public string WriteSineWav(
        string relativePath, double frequency, float amplitude = 0.5f, int frames = 44100,
        int sampleRate = SampleRate)
    {
        var samples = new float[frames];
        for (var i = 0; i < frames; i++)
        {
            samples[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * frequency * i / sampleRate));
        }

        return WriteWav(relativePath, samples, 1, sampleRate);
    }

    /// <summary>
    /// Writes a 32-bit float mono WAV made of constant-valued sections, so a test can tell which part of
    /// the file is sounding from the level alone.
    /// </summary>
    /// <param name="relativePath">Where to write it, relative to the library folder.</param>
    /// <param name="sectionFrames">How many frames each section runs for.</param>
    /// <param name="values">The value of each section, in order.</param>
    /// <returns>The full path of the file.</returns>
    public string WriteSectionedWav(string relativePath, int sectionFrames, params float[] values)
    {
        var samples = new float[sectionFrames * values.Length];
        for (var section = 0; section < values.Length; section++)
        {
            Array.Fill(samples, values[section], section * sectionFrames, sectionFrames);
        }

        return WriteWav(relativePath, samples, 1, SampleRate);
    }

    /// <summary>
    /// Hand-writes a 16-bit PCM mono WAV carrying a <c>smpl</c> chunk with one loop, for the
    /// embedded-marker tests. The audio is three constant sections so the sounding one is readable.
    /// </summary>
    /// <param name="relativePath">Where to write it, relative to the library folder.</param>
    /// <param name="sectionFrames">How many frames each of the three sections runs for.</param>
    /// <param name="loopStart">The first frame of the declared loop.</param>
    /// <param name="loopEnd">The last frame of the declared loop.</param>
    /// <returns>The full path of the file.</returns>
    public string WriteSmplLoopWav(string relativePath, int sectionFrames, uint loopStart, uint loopEnd)
    {
        var path = PathFor(relativePath);

        var frames = sectionFrames * 3;
        var dataBytes = frames * 2;
        const int fmtSize = 16;
        const int smplSize = 36 + 24;
        var riffSize = 4 + (8 + fmtSize) + (8 + smplSize) + (8 + dataBytes);

        short[] sectionValues = [8000, 16000, 24000];

        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(riffSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(fmtSize);
            writer.Write((short)1);
            writer.Write((short)1);
            writer.Write(SampleRate);
            writer.Write(SampleRate * 2);
            writer.Write((short)2);
            writer.Write((short)16);

            writer.Write(Encoding.ASCII.GetBytes("smpl"));
            writer.Write(smplSize);
            writer.Write(0); // manufacturer
            writer.Write(0); // product
            writer.Write(0); // sample period
            writer.Write(60); // MIDI unity note
            writer.Write(0); // pitch fraction
            writer.Write(0); // SMPTE format
            writer.Write(0); // SMPTE offset
            writer.Write(1); // loop count
            writer.Write(0); // sampler data size
            writer.Write(0); // loop cue id
            writer.Write(0); // loop type: forward
            writer.Write(loopStart);
            writer.Write(loopEnd);
            writer.Write(0); // fraction
            writer.Write(0); // play count: infinite

            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(dataBytes);

            for (var i = 0; i < frames; i++)
            {
                writer.Write(sectionValues[Math.Min(sectionValues.Length - 1, i / sectionFrames)]);
            }
        }

        return path;
    }

    /// <summary>Writes a 32-bit float mono WAV holding exactly the samples given.</summary>
    /// <param name="relativePath">Where to write it, relative to the library folder.</param>
    /// <param name="samples">The samples.</param>
    /// <returns>The full path of the file.</returns>
    public string WriteRawWav(string relativePath, float[] samples) =>
        WriteWav(relativePath, samples, 1, SampleRate);

    /// <summary>Writes a 32-bit float stereo WAV from two equal-length channels.</summary>
    /// <param name="relativePath">Where to write it, relative to the library folder.</param>
    /// <param name="left">The left channel.</param>
    /// <param name="right">The right channel.</param>
    /// <returns>The full path of the file.</returns>
    public string WriteStereoImpulseWav(string relativePath, float[] left, float[] right)
    {
        var interleaved = new float[left.Length * 2];

        for (var i = 0; i < left.Length; i++)
        {
            interleaved[i * 2] = left[i];
            interleaved[i * 2 + 1] = i < right.Length ? right[i] : 0f;
        }

        return WriteWav(relativePath, interleaved, 2, SampleRate);
    }

    /// <summary>Writes a preset file.</summary>
    /// <param name="xml">The preset text.</param>
    /// <param name="relativePath">Where to write it, relative to the library folder.</param>
    /// <returns>The full path of the file.</returns>
    public string WritePreset(string xml, string relativePath = "instrument.dspreset")
    {
        var path = PathFor(relativePath);
        File.WriteAllText(path, xml, Encoding.UTF8);
        return path;
    }

    /// <summary>Writes a preset and loads it.</summary>
    /// <param name="xml">The preset text.</param>
    /// <param name="relativePath">Where to write it, relative to the library folder.</param>
    /// <returns>The loaded instrument. The caller disposes it.</returns>
    public DecentSamplerInstrument LoadPreset(string xml, string relativePath = "instrument.dspreset") =>
        DecentSamplerInstrument.Load(WritePreset(xml, relativePath));

    /// <inheritdoc/>
    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (Exception)
        {
            // A locked temp directory is the OS's problem, not the test run's.
        }
    }

    private string WriteWav(string relativePath, float[] samples, int channels, int sampleRate)
    {
        var path = PathFor(relativePath);
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);

        using (var writer = new WaveFileWriter(path, format))
        {
            writer.WriteSamples(samples, 0, samples.Length);
        }

        return path;
    }
}
