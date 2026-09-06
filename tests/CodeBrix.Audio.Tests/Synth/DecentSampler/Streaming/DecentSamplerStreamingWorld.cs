using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Streaming;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Streaming;

/// <summary>
/// Builds the same little library twice - once decoded into memory, once streamed from disk - and
/// renders the same notes through both, which is the shape of every acceptance test in this folder.
/// </summary>
/// <remarks>
/// The preset text carries a <c>{MODE}</c> placeholder where <c>playbackMode</c> goes, so one string
/// describes both worlds and nothing but the playback mode can differ between them.
/// </remarks>
internal sealed class DecentSamplerStreamingWorld : IDisposable
{
    private readonly DecentSamplerEngineFixtures _fixtures;
    private readonly List<DecentSamplerInstrument> _instruments = [];

    private DecentSamplerStreamingWorld(DecentSamplerEngineFixtures fixtures)
    {
        _fixtures = fixtures;
    }

    /// <summary>The fixtures the samples are written into.</summary>
    public DecentSamplerEngineFixtures Fixtures => _fixtures;

    /// <summary>Creates a world with a fresh temp library folder.</summary>
    /// <returns>The world.</returns>
    public static DecentSamplerStreamingWorld Create() =>
        new DecentSamplerStreamingWorld(DecentSamplerEngineFixtures.Create());

    /// <summary>Loads the preset with <c>{MODE}</c> replaced.</summary>
    /// <param name="preset">The preset text.</param>
    /// <param name="mode">The <c>playbackMode</c> value to write.</param>
    /// <param name="preloadFrames">The streaming preload head.</param>
    /// <returns>The instrument. Disposed with the world.</returns>
    public DecentSamplerInstrument Load(string preset, string mode, int preloadFrames = 256)
    {
        var options = new DecentSamplerLoadOptions
        {
            StreamingPreloadFrames = preloadFrames,
        };

        var path = _fixtures.WritePreset(
            preset.Replace("{MODE}", mode, StringComparison.Ordinal),
            mode + ".dspreset");

        var instrument = DecentSamplerInstrument.Load(path, options);
        _instruments.Add(instrument);
        return instrument;
    }

    /// <summary>Renders one held note from the start of the instrument.</summary>
    /// <param name="instrument">The instrument to play.</param>
    /// <param name="note">The MIDI note.</param>
    /// <param name="blocks">How many 64-frame blocks to render.</param>
    /// <returns>The left and right channels.</returns>
    public static (float[] Left, float[] Right) Play(
        DecentSamplerInstrument instrument, int note, int blocks)
    {
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument, settings =>
        {
            settings.StreamingMode = DecentSamplerStreamingMode.Offline;
            settings.StreamingRingFrames = 1024;
        });

        synthesizer.NoteOn(0, note, 100);
        return DecentSamplerRenderProbe.RenderBlocks(synthesizer, blocks);
    }

    /// <summary>The first index at which two renders differ, or -1 when they are identical.</summary>
    /// <param name="left">One render.</param>
    /// <param name="right">The other.</param>
    /// <returns>The index, or -1.</returns>
    public static int FirstDifference(float[] left, float[] right)
    {
        var length = Math.Min(left.Length, right.Length);

        for (var i = 0; i < length; i++)
        {
            if (left[i] != right[i])
            {
                return i;
            }
        }

        return left.Length == right.Length ? -1 : length;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        foreach (var instrument in _instruments)
        {
            instrument.Dispose();
        }

        _fixtures.Dispose();
    }
}
