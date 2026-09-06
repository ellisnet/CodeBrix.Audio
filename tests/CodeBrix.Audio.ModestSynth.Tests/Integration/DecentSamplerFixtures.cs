using System;
using System.IO;
using System.Text;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.ModestSynth.Tests.Integration;

/// <summary>
/// Builds tiny synthetic Decent Sampler libraries for the integration tests. Nothing here touches a
/// real sample library: every preset and every .wav is written by the test that reads it.
/// </summary>
public sealed class DecentSamplerFixtures : IDisposable
{
    /// <summary>The sample rate the integration tests render at, matching the spectrum helper's.</summary>
    public const int SampleRate = Spectrum.SampleRate;

    private DecentSamplerFixtures(string directory)
    {
        Directory = directory;
    }

    /// <summary>The temp directory holding the library. Deleted on dispose.</summary>
    public string Directory { get; }

    /// <summary>Creates a fresh temp directory to build a library in.</summary>
    /// <returns>The fixture.</returns>
    public static DecentSamplerFixtures Create()
    {
        string directory = Path.Combine(Path.GetTempPath(), "codebrix-ms-" + Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(directory);
        return new DecentSamplerFixtures(directory);
    }

    /// <summary>A full path inside the library folder, creating the folders it needs.</summary>
    /// <param name="relativePath">The path relative to the library folder.</param>
    /// <returns>The full path.</returns>
    public string PathFor(string relativePath)
    {
        string full = Path.Combine(Directory, relativePath.Replace('/', Path.DirectorySeparatorChar));
        string parent = Path.GetDirectoryName(full);

        if (parent != null) { System.IO.Directory.CreateDirectory(parent); }

        return full;
    }

    /// <summary>Writes a preset file.</summary>
    /// <param name="xml">The preset body.</param>
    /// <param name="relativePath">Where to write it.</param>
    /// <returns>The full path.</returns>
    public string WritePreset(string xml, string relativePath = "instrument.dspreset")
    {
        string path = PathFor(relativePath);
        File.WriteAllText(path, xml, Encoding.UTF8);
        return path;
    }

    /// <summary>Writes a preset and loads it.</summary>
    /// <param name="xml">The preset body.</param>
    /// <param name="relativePath">Where to write it.</param>
    /// <returns>The loaded instrument. The caller disposes it.</returns>
    public DecentSamplerInstrument LoadPreset(string xml, string relativePath = "instrument.dspreset") =>
        DecentSamplerInstrument.Load(WritePreset(xml, relativePath));

    /// <summary>Writes a 32-bit float mono WAV holding a full-scale sine.</summary>
    /// <param name="relativePath">Where to write it.</param>
    /// <param name="frequency">The tone frequency in Hz.</param>
    /// <param name="amplitude">The peak amplitude.</param>
    /// <param name="frames">How many frames to write.</param>
    /// <returns>The full path.</returns>
    public string WriteSineWav(
        string relativePath, double frequency, float amplitude = 1f, int frames = SampleRate)
    {
        float[] samples = new float[frames];

        for (int i = 0; i < frames; i++)
        {
            samples[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * frequency * i / SampleRate));
        }

        return WriteWav(relativePath, samples, 1, SampleRate);
    }

    /// <summary>
    /// Writes a multi-frame wavetable as a plain .wav: additive frames from a pure sine to a
    /// sixteen-partial sawtooth, so that moving the position is measurable in the spectrum.
    /// </summary>
    /// <param name="relativePath">Where to write it.</param>
    /// <param name="frameSize">The samples per frame.</param>
    /// <returns>The full path.</returns>
    public string WriteWavetableWav(string relativePath, int frameSize = 1024)
    {
        int[] partialCounts = [1, 2, 4, 16];
        float[] samples = new float[partialCounts.Length * frameSize];

        for (int frame = 0; frame < partialCounts.Length; frame++)
        {
            int offset = frame * frameSize;

            for (int i = 0; i < frameSize; i++)
            {
                double phase = 2.0 * Math.PI * i / frameSize;
                double value = 0.0;

                for (int partial = 1; partial <= partialCounts[frame]; partial++)
                {
                    value += Math.Sin(phase * partial) / partial;
                }

                samples[offset + i] = (float)(value * 0.6);
            }
        }

        return WriteWav(relativePath, samples, 1, SampleRate);
    }

    /// <summary>Deletes the library folder.</summary>
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
        string path = PathFor(relativePath);
        WaveFormat format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);

        using (WaveFileWriter writer = new WaveFileWriter(path, format))
        {
            writer.WriteSamples(samples, 0, samples.Length);
        }

        return path;
    }
}
