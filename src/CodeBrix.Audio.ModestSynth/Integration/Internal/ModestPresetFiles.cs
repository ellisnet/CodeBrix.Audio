using System;
using System.IO;
using CodeBrix.Audio.ModestSynth.Wavetable;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Containers;

namespace CodeBrix.Audio.ModestSynth.Integration.Internal;

// Finds a file a preset names - a wavetable - the way the Decent Sampler engine finds a sample: through
// the instrument's own container, so a path works whether the preset sits in a folder or inside a
// .dslibrary archive, and whether or not its capitalisation matches the disk.
//
// DecentSamplerInstrument.Container is the whole of it. WavetableFileCache remembers the decoded table
// under the container's own cache key, so exactly one resolve and one decode happen per instrument per
// distinct wavetable path and every voice after the first finds both already done.
internal static class ModestPresetFiles
{
    // Loads the wavetable a zone names. Never throws: a reason comes back in `problem` and the caller
    // falls back to a sine, which is what the reference player does for a wavetable with no table.
    internal static bool TryLoadWavetable(
        DecentSamplerInstrument instrument,
        string relativePath,
        int frameSize,
        out WavetableFile file,
        out string problem)
    {
        file = null;
        problem = null;

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            problem = "No file reference: <oscillator> @wavetableFile is not set; " +
                      "the wavetable oscillator falls back to a sine.";
            return false;
        }

        DecentSamplerContainer container = instrument?.Container;

        if (container == null)
        {
            // No instrument means no preset folder to resolve against; the path stands on its own.
            return WavetableFileCache.TryGetOrLoad(relativePath, frameSize, out file, out problem);
        }

        try
        {
            if (!container.TryResolve(relativePath, out string key))
            {
                problem = "Missing file reference: <oscillator> @wavetableFile=\"" + relativePath +
                          "\" (in " + container.Description +
                          "); the wavetable oscillator falls back to a sine.";
                return false;
            }

            return WavetableFileCache.TryGetOrLoad(
                container.CacheKeyFor(key), frameSize, () => container.OpenFile(key), out file, out problem);
        }
        catch (Exception exception) when (exception is IOException || exception is InvalidDataException ||
                                          exception is ObjectDisposedException ||
                                          exception is UnauthorizedAccessException ||
                                          exception is ArgumentException ||
                                          exception is NotSupportedException)
        {
            problem = "Missing file reference: <oscillator> @wavetableFile=\"" + relativePath +
                      "\" (" + exception.Message + "); the wavetable oscillator falls back to a sine.";
            return false;
        }
    }
}
