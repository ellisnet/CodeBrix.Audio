using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Playback.Internal;

/// <summary>
/// Every track of a song rendered in lockstep into one interleaved stereo buffer.
/// </summary>
/// <remarks>
/// <para>
/// This is where "sample-accurate" comes from: one position, advanced once per block, with every
/// track asked for exactly the same frames. There is no per-track transport to drift, and no mixer
/// scheduling to be at the mercy of. It is also why the same class serves playback and offline
/// rendering - the device path wraps it in a data provider, the offline path pulls it dry, and both
/// hear the same mix.
/// </para>
/// <para>
/// Not thread-safe. The owner serializes it: <c>MultiTrackDataProvider</c> holds a lock around
/// every entry point, exactly as <c>MidiSynthDataProvider</c> does for the synthesizer it owns.
/// </para>
/// </remarks>
internal sealed class MultiTrackMix : IDisposable
{
    // How many frames are mixed per inner pass. Bounds the scratch buffers and keeps a very large
    // ReadBytes from asking a synthesizer for an unreasonable slice in one go.
    private const int MaximumChunkFrames = 2048;

    private readonly TrackVoice[] voices;
    private readonly MidiTempoMap tempoMap;
    private readonly long tempoReferenceOffsetFrames;
    private readonly TempoSource tempoSource;

    private long position;
    private bool disposed;

    /// <summary>Builds a mix over a snapshot of the player's tracks.</summary>
    /// <param name="tracks">The tracks to render. The list is not kept; the track objects are.</param>
    /// <param name="sampleRate">The rate every source renders at.</param>
    /// <param name="tempoSource">The tempo source to publish musical time into, or null for none.</param>
    /// <param name="forcedSource">
    /// When given, every track renders only that source - what a per-source loudness measurement
    /// needs. When null, every track renders every source it holds.
    /// </param>
    internal MultiTrackMix(
        IReadOnlyList<PlayerTrack> tracks,
        int sampleRate,
        TempoSource tempoSource,
        TrackSource? forcedSource = null)
    {
        SampleRate = sampleRate;
        this.tempoSource = tempoSource;

        var built = new List<TrackVoice>(tracks.Count);
        try
        {
            foreach (var track in tracks)
            {
                if (forcedSource == TrackSource.Audio && !track.HasAudioSource)
                {
                    continue;
                }

                if (forcedSource == TrackSource.Midi && !track.HasMidiSource)
                {
                    continue;
                }

                built.Add(new TrackVoice(track, sampleRate, forcedSource));
            }
        }
        catch (Exception)
        {
            foreach (var voice in built)
            {
                voice.Dispose();
            }
            throw;
        }

        voices = built.ToArray();

        var end = 0L;
        foreach (var voice in voices)
        {
            if (voice.EndFrame > end)
            {
                end = voice.EndFrame;
            }
        }

        LengthFrames = end;

        // Musical time comes from the first track that has a MIDI performance: within one song
        // every stem carries the same tempo map, and a song with no MIDI at all is simply not
        // something a tempo can be read from - it reports the MIDI default.
        foreach (var track in tracks)
        {
            if (track.HasMidiSource)
            {
                tempoMap = track.MidiSequence.TempoMap;
                tempoReferenceOffsetFrames = (long)Math.Round(track.Offset.TotalSeconds * sampleRate);
                break;
            }
        }

        tempoMap ??= new MidiTempoMap([]);

        Seek(0);
    }

    /// <summary>The rate every source renders at.</summary>
    internal int SampleRate { get; }

    /// <summary>The song's length in frames: the last frame any track reaches, offsets included.</summary>
    internal long LengthFrames { get; }

    /// <summary>The current position in frames from the start of the song.</summary>
    internal long Position => position;

    /// <summary>The tempo map musical time is read from. Never null.</summary>
    internal MidiTempoMap TempoMap => tempoMap;

    /// <summary>The number of tracks actually being rendered.</summary>
    internal int VoiceCount => voices.Length;

    /// <summary>Moves the whole song to a position, taking every track with it.</summary>
    /// <param name="frame">The frame to move to; negative values are clamped to zero.</param>
    internal void Seek(long frame)
    {
        if (frame < 0)
        {
            frame = 0;
        }

        position = frame;
        foreach (var voice in voices)
        {
            voice.Seek(frame);
        }

        PublishTempo(tempoSource != null && tempoSource.IsPlaying);
    }

    /// <summary>Renders the next frames of the song.</summary>
    /// <param name="buffer">Interleaved stereo destination. Filled completely; silence past the end.</param>
    internal void Render(Span<float> buffer)
    {
        buffer.Clear();

        if (disposed)
        {
            return;
        }

        var anySolo = false;
        foreach (var voice in voices)
        {
            if (voice.Track.Solo)
            {
                anySolo = true;
                break;
            }
        }

        var frames = buffer.Length / 2;
        var written = 0;

        while (written < frames)
        {
            var take = Math.Min(MaximumChunkFrames, frames - written);
            var chunk = buffer.Slice(written * 2, take * 2);

            foreach (var voice in voices)
            {
                voice.Render(chunk, anySolo);
            }

            written += take;
            position += take;
        }

        PublishTempo(true);
    }

    /// <summary>Publishes the current musical time into the tempo source, if there is one.</summary>
    /// <param name="isPlaying">What to report for <see cref="TempoSource.IsPlaying"/>.</param>
    internal void PublishTempo(bool isPlaying)
    {
        if (tempoSource == null)
        {
            return;
        }

        var frame = position - tempoReferenceOffsetFrames;
        var time = frame > 0
            ? TimeSpan.FromSeconds((double)frame / SampleRate)
            : TimeSpan.Zero;

        tempoSource.Update(tempoMap.BeatsPerMinuteAt(time), tempoMap.BeatPositionAt(time), isPlaying);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        if (disposed)
        {
            return;
        }

        disposed = true;
        foreach (var voice in voices)
        {
            voice.Dispose();
        }
    }
}
