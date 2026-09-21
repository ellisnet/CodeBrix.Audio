using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Streaming;

namespace CodeBrix.Audio.Playback.Internal;

/// <summary>
/// Puts every Decent Sampler synthesizer of an offline render into
/// <see cref="DecentSamplerStreamingMode.Offline"/> for the render, and puts each one back into the
/// mode it was in afterwards - however the render ended.
/// </summary>
/// <remarks>
/// An offline render runs as fast as the machine allows. A Decent Sampler synthesizer streaming its
/// samples in the real-time mode would outrun its background reader and write starved blocks as
/// silence, so a render that is not an audio callback has to say so. <c>SoundFontRenderer</c> does
/// exactly this for every render it makes; the multi-track path does it here, for every track at
/// once.
/// </remarks>
internal sealed class OfflineStreamingScope : IDisposable
{
    private readonly DecentSamplerSynthesizer[] synthesizers;
    private readonly DecentSamplerStreamingMode[] previousModes;

    private bool restored;

    private OfflineStreamingScope(
        DecentSamplerSynthesizer[] synthesizers, DecentSamplerStreamingMode[] previousModes)
    {
        this.synthesizers = synthesizers;
        this.previousModes = previousModes;
    }

    /// <summary>
    /// Switches whichever of these synthesizers are Decent Sampler ones to the offline mode.
    /// </summary>
    /// <param name="candidates">The synthesizers a render is about to drive; nulls are skipped.</param>
    /// <returns>A scope that restores every mode it changed when it is disposed. Never null.</returns>
    internal static OfflineStreamingScope Enter(IReadOnlyList<IMidiSynthesizer> candidates)
    {
        if (candidates == null || candidates.Count == 0)
        {
            return new OfflineStreamingScope([], []);
        }

        var found = new List<DecentSamplerSynthesizer>();
        foreach (var candidate in candidates)
        {
            if (candidate is DecentSamplerSynthesizer decentSampler)
            {
                found.Add(decentSampler);
            }
        }

        if (found.Count == 0)
        {
            return new OfflineStreamingScope([], []);
        }

        var modes = new DecentSamplerStreamingMode[found.Count];
        for (var i = 0; i < found.Count; i++)
        {
            modes[i] = found[i].StreamingMode;
            found[i].StreamingMode = DecentSamplerStreamingMode.Offline;
        }

        return new OfflineStreamingScope(found.ToArray(), modes);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (restored)
        {
            return;
        }

        restored = true;

        for (var i = 0; i < synthesizers.Length; i++)
        {
            // Best effort per synthesizer: one that has been disposed under us must not stop the
            // rest from being put back.
            try { synthesizers[i].StreamingMode = previousModes[i]; }
            catch (Exception) { /* the render is over; nothing here is worth failing for */ }
        }
    }
}
