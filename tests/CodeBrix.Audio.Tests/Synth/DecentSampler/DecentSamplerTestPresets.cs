using System;
using System.IO;
using System.IO.Compression;
using System.Text;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler;

/// <summary>
/// Builds tiny Decent Sampler libraries on disk for the engine tests: a fresh temp directory,
/// synthetic samples written in code, and a <c>.dspreset</c> from a test-authored string. Nothing
/// third-party, matching how the SFZ suite builds its instruments.
/// </summary>
internal sealed class DecentSamplerTestPresets : IDisposable
{
    private DecentSamplerTestPresets(string directory)
    {
        Directory = directory;
    }

    /// <summary>The temp directory holding this library's files. Deleted on dispose.</summary>
    public string Directory { get; }

    /// <summary>Creates a fresh temp directory to build a library in.</summary>
    public static DecentSamplerTestPresets Create()
    {
        var directory = Path.Combine(Path.GetTempPath(), "codebrix-ds-" + Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(directory);
        return new DecentSamplerTestPresets(directory);
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

    /// <summary>Writes a 32-bit float WAV holding one constant value.</summary>
    /// <param name="relativePath">Where to write it, relative to the library folder.</param>
    /// <param name="value">The sample value.</param>
    /// <param name="frames">How many frames to write.</param>
    /// <param name="sampleRate">The sample rate.</param>
    /// <param name="channels">1 for mono, 2 for stereo.</param>
    /// <returns>The full path of the file.</returns>
    public string WriteWav(
        string relativePath, float value = 0.5f, int frames = 64, int sampleRate = 44100, int channels = 1)
    {
        var path = PathFor(relativePath);
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);

        using (var writer = new WaveFileWriter(path, format))
        {
            var samples = new float[frames * channels];
            Array.Fill(samples, value);
            writer.WriteSamples(samples, 0, samples.Length);
        }

        return path;
    }

    /// <summary>Writes a 16-bit AIFF holding one constant value.</summary>
    /// <param name="relativePath">Where to write it, relative to the library folder.</param>
    /// <param name="value">The sample value.</param>
    /// <param name="frames">How many frames to write.</param>
    /// <param name="sampleRate">The sample rate.</param>
    /// <returns>The full path of the file.</returns>
    public string WriteAiff(string relativePath, float value = 0.5f, int frames = 64, int sampleRate = 44100)
    {
        var path = PathFor(relativePath);
        var format = new WaveFormat(sampleRate, 16, 1);

        using (var writer = new AiffFileWriter(path, format))
        {
            var samples = new float[frames];
            Array.Fill(samples, value);
            writer.WriteSamples(samples, 0, samples.Length);
        }

        return path;
    }

    /// <summary>
    /// Copies one of the repository's synthetic FLAC fixtures into the library.
    /// </summary>
    /// <remarks>
    /// The suite has no FLAC encoder, so a FLAC zone is fed by the fixture that
    /// tools/make_test_fixtures already generates. It is synthesized locally, not third-party audio.
    /// </remarks>
    /// <param name="relativePath">Where to write it, relative to the library folder.</param>
    /// <returns>The full path of the file.</returns>
    public string WriteFlac(string relativePath)
    {
        var path = PathFor(relativePath);
        File.Copy(TestAssets.Path("flac-tone-mono-16bit-22050.flac"), path, overwrite: true);
        return path;
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

    /// <summary>Writes a <c>DSLibraryInfo.xml</c> sidecar at the top of the library folder.</summary>
    /// <param name="xml">The sidecar text.</param>
    /// <returns>The full path of the file.</returns>
    public string WriteLibraryInfo(string xml)
    {
        var path = PathFor("DSLibraryInfo.xml");
        File.WriteAllText(path, xml, Encoding.UTF8);
        return path;
    }

    /// <summary>Writes a preset and loads it as an instrument.</summary>
    /// <param name="xml">The preset text.</param>
    /// <param name="options">How to load it, or null for the defaults.</param>
    /// <param name="relativePath">Where to write the preset, relative to the library folder.</param>
    /// <returns>The loaded instrument. The caller disposes it.</returns>
    public DecentSamplerInstrument Load(
        string xml, DecentSamplerLoadOptions options = null, string relativePath = "instrument.dspreset") =>
        DecentSamplerInstrument.Load(WritePreset(xml, relativePath), options);

    /// <summary>
    /// Packs part of the library folder into a <c>.dslibrary</c> archive, under one top-level folder.
    /// </summary>
    /// <param name="archiveName">The archive file name, written beside the library folder.</param>
    /// <param name="topFolder">The folder name inside the archive.</param>
    /// <param name="relativePaths">The files to pack, relative to the library folder.</param>
    /// <returns>The full path of the archive.</returns>
    public string BuildLibraryArchive(string archiveName, string topFolder, params string[] relativePaths)
    {
        var path = Path.Combine(Directory, archiveName);

        using (var stream = new FileStream(path, FileMode.Create, FileAccess.Write))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var relativePath in relativePaths)
            {
                var entryName = topFolder.Length == 0 ? relativePath : topFolder + "/" + relativePath;
                var entry = archive.CreateEntry(entryName, CompressionLevel.Optimal);

                using (var source = File.OpenRead(PathFor(relativePath)))
                using (var target = entry.Open())
                {
                    source.CopyTo(target);
                }
            }
        }

        return path;
    }

    /// <summary>A minimal preset around the given body, with one group and one sample.</summary>
    /// <param name="samplePath">The sample path the zone should use.</param>
    /// <returns>The preset text.</returns>
    public static string MinimalPreset(string samplePath = "Samples/tone.wav") =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<DecentSampler minVersion=\"1.0.0\" pluginVersion=\"1\">\n" +
        "  <groups>\n" +
        "    <group>\n" +
        $"      <sample path=\"{samplePath}\" rootNote=\"60\" loNote=\"0\" hiNote=\"127\" />\n" +
        "    </group>\n" +
        "  </groups>\n" +
        "</DecentSampler>\n";

    /// <summary>Deletes the temp directory and everything in it.</summary>
    public void Dispose()
    {
        try
        {
            System.IO.Directory.Delete(Directory, recursive: true);
        }
        catch (Exception)
        {
            // A test that leaves a file open should not fail on cleanup.
        }
    }
}
