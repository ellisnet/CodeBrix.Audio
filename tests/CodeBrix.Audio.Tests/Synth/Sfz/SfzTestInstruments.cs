using System;
using System.IO;
using System.Text;
using CodeBrix.Audio.Synth.Sfz;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Tests.Synth.Sfz;

/// <summary>
/// Builds tiny SFZ instruments on disk for the engine tests: a fresh temp directory, synthetic WAV
/// samples written in code, and an <c>.sfz</c> file from a test-authored string. Nothing third-party,
/// matching how the rest of the suite builds its WAV fixtures.
/// </summary>
internal sealed class SfzTestInstruments : IDisposable
{
    private SfzTestInstruments(string directory)
    {
        Directory = directory;
    }

    /// <summary>The temp directory holding this instrument's files. Deleted on dispose.</summary>
    public string Directory { get; }

    /// <summary>Creates a fresh temp directory to build an instrument in.</summary>
    public static SfzTestInstruments Create()
    {
        var directory = Path.Combine(Path.GetTempPath(), "codebrix-sfz-" + Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(directory);
        return new SfzTestInstruments(directory);
    }

    /// <summary>Writes an .sfz file with the given text and loads it as an instrument.</summary>
    public SfzInstrument Load(string sfzText, string fileName = "instrument.sfz")
    {
        var path = Path.Combine(Directory, fileName);
        File.WriteAllText(path, sfzText);
        return new SfzInstrument(path);
    }

    /// <summary>
    /// Writes a mono or stereo 32-bit float WAV holding a constant value - the simplest possible
    /// signal, which turns gain assertions into arithmetic.
    /// </summary>
    public string WriteConstantWav(string name, float value, int frames, int sampleRate = 44100, int channels = 1)
    {
        var path = Path.Combine(Directory, name);
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        using (var writer = new WaveFileWriter(path, format))
        {
            var samples = new float[frames * channels];
            Array.Fill(samples, value);
            writer.WriteSamples(samples, 0, samples.Length);
        }

        return path;
    }

    /// <summary>Writes a mono 32-bit float WAV holding a sine tone.</summary>
    public string WriteSineWav(string name, float frequency, int frames, int sampleRate = 44100, float amplitude = 0.5f)
    {
        var path = Path.Combine(Directory, name);
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
        using (var writer = new WaveFileWriter(path, format))
        {
            var samples = new float[frames];
            for (var i = 0; i < frames; i++)
            {
                samples[i] = (float)(amplitude * Sine((double)frequency * i / sampleRate));
            }
            writer.WriteSamples(samples, 0, samples.Length);
        }

        return path;
    }

    /// <summary>
    /// Sine of a phase given in REVOLUTIONS, computed without the platform maths library so that every
    /// operating system writes a byte-identical fixture.
    /// </summary>
    /// <remarks>
    /// <para>
    /// <c>MathF.Sin</c> compiles to the platform's <c>sinf</c> - UCRT on Windows, glibc on Linux,
    /// Apple's libm on macOS - and none of the three is correctly rounded, so they disagree by one ulp
    /// on a few arguments in every thousand. That is harmless for a test that asserts within a
    /// tolerance and fatal for one that pins a render, so the fixture must not depend on it. Only
    /// IEEE-754 add, subtract, multiply, divide and floor appear below, and every one of those is
    /// exactly specified: same bits on every platform and architecture.
    /// </para>
    /// <para>
    /// The phase arrives in revolutions rather than radians so that reducing it is an exact
    /// <see cref="Math.Floor"/> rather than a division by pi. What is left is folded onto the first
    /// quarter turn by the sine's own symmetry and evaluated with the Taylor series through x^15,
    /// whose error over that range is about 1e-12 - four orders finer than the float the sample ends
    /// up in.
    /// </para>
    /// </remarks>
    private static double Sine(double revolutions)
    {
        var turn = revolutions - Math.Floor(revolutions);
        var sign = 1.0;

        if (turn >= 0.5)
        {
            turn -= 0.5;
            sign = -1.0;
        }

        if (turn > 0.25)
        {
            turn = 0.5 - turn;
        }

        var x = turn * (2.0 * Math.PI);
        var square = x * x;

        return sign * x * (1.0
            + square * (-1.0 / 6.0
            + square * (1.0 / 120.0
            + square * (-1.0 / 5040.0
            + square * (1.0 / 362880.0
            + square * (-1.0 / 39916800.0
            + square * (1.0 / 6227020800.0
            + square * (-1.0 / 1307674368000.0))))))));
    }

    /// <summary>
    /// Writes a stereo 32-bit float WAV whose left channel holds one constant and right another, so a
    /// test can tell the channels apart after mixing.
    /// </summary>
    public string WriteStereoWav(string name, float leftValue, float rightValue, int frames, int sampleRate = 44100)
    {
        var path = Path.Combine(Directory, name);
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        using (var writer = new WaveFileWriter(path, format))
        {
            var samples = new float[frames * 2];
            for (var i = 0; i < frames; i++)
            {
                samples[i * 2] = leftValue;
                samples[i * 2 + 1] = rightValue;
            }
            writer.WriteSamples(samples, 0, samples.Length);
        }

        return path;
    }

    /// <summary>
    /// Hand-writes a 16-bit PCM WAV with a <c>smpl</c> chunk declaring one loop, for the
    /// embedded-loop-point paths. The audio ramps linearly so positions are distinguishable.
    /// </summary>
    public string WriteWavWithSmplLoop(string name, int frames, uint loopStart, uint loopEnd, int sampleRate = 44100)
    {
        var path = Path.Combine(Directory, name);

        var dataBytes = frames * 2;
        const int fmtSize = 16;
        const int smplSize = 36 + 24;
        var riffSize = 4 + (8 + fmtSize) + (8 + smplSize) + (8 + dataBytes);

        using (var stream = File.Create(path))
        using (var writer = new BinaryWriter(stream, Encoding.ASCII, leaveOpen: false))
        {
            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(riffSize);
            writer.Write(Encoding.ASCII.GetBytes("WAVE"));

            writer.Write(Encoding.ASCII.GetBytes("fmt "));
            writer.Write(fmtSize);
            writer.Write((short)1);              // PCM
            writer.Write((short)1);              // mono
            writer.Write(sampleRate);
            writer.Write(sampleRate * 2);        // byte rate
            writer.Write((short)2);              // block align
            writer.Write((short)16);             // bits

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
                writer.Write((short)(short.MaxValue * i / (float)frames));
            }
        }

        return path;
    }

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
}
