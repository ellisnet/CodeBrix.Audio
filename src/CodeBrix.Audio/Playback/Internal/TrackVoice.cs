using System;

namespace CodeBrix.Audio.Playback.Internal;

/// <summary>
/// One track as the mixer sees it: up to two source renderers, the track's timing offset, and the
/// crossfade that moves between the two sources without stopping either.
/// </summary>
/// <remarks>
/// <para>
/// When a track holds both a recording and a MIDI performance, BOTH are rendered on every block,
/// even though only one is heard. That is what makes switching seamless: the silent one is already
/// at the right position with the right controller state, so the switch is a 20 ms gain ramp rather
/// than a restart. It costs what the silent source costs to render, which for a synthesizer is not
/// nothing - a track that will never switch should hold only the source it needs.
/// </para>
/// <para>
/// A track's <see cref="PlayerTrack.Offset"/> is read here, when the voice is built and whenever
/// the transport seeks. Positive delays the track: its own timeline runs behind the song's, and it
/// emits silence until the song reaches it.
/// </para>
/// </remarks>
internal sealed class TrackVoice : IDisposable
{
    // How long a source switch takes. Long enough that neither source clicks, short enough that
    // the change reads as immediate.
    private const double CrossfadeSeconds = 0.020;

    private readonly PlayerTrack track;
    private readonly int sampleRate;
    private readonly SourceRenderer audio;
    private readonly SourceRenderer midi;
    private readonly DelayedSourceRenderer midiDelay;
    private readonly float crossfadeStep;

    private float[] audioBuffer = [];
    private float[] midiBuffer = [];

    private long offsetFrames;
    private long trackFrame;
    private float blend;

    /// <summary>Builds the renderers a track needs at the given rate.</summary>
    /// <param name="track">The track to render.</param>
    /// <param name="sampleRate">The mix's sample rate.</param>
    /// <param name="forcedSource">
    /// When given, only that source is built and heard - what a per-source loudness measurement
    /// needs. When null, every source the track holds is built.
    /// </param>
    internal TrackVoice(PlayerTrack track, int sampleRate, TrackSource? forcedSource)
    {
        this.track = track;
        this.sampleRate = sampleRate;

        var wantAudio = track.HasAudioSource && forcedSource != TrackSource.Midi;
        var wantMidi = track.HasMidiSource && forcedSource != TrackSource.Audio;

        try
        {
            if (wantAudio)
            {
                audio = new AudioSourceRenderer(track.AudioSource, sampleRate);
            }

            if (wantMidi)
            {
                // Wrapped even when the shift is zero, because it is re-read on every seek and a
                // consumer may set it after the voice was built.
                midiDelay = new DelayedSourceRenderer(new MidiSourceRenderer(track, sampleRate));
                midi = midiDelay;
            }
        }
        catch (Exception)
        {
            audio?.Dispose();
            midi?.Dispose();
            throw;
        }

        ForcedSource = forcedSource;
        crossfadeStep = (float)(1.0 / Math.Max(1.0, CrossfadeSeconds * sampleRate));
        blend = TargetBlend();

        CaptureOffset();
    }

    /// <summary>The single source this voice was pinned to, or null when it renders every source.</summary>
    internal TrackSource? ForcedSource { get; }

    /// <summary>The track this voice renders.</summary>
    internal PlayerTrack Track => track;

    /// <summary>
    /// The frame, on the song's timeline, at which this track's last sample falls - its own length
    /// plus its offset, floored at zero.
    /// </summary>
    internal long EndFrame
    {
        get
        {
            var length = 0L;
            if (audio != null && audio.LengthFrames > length)
            {
                length = audio.LengthFrames;
            }
            if (midi != null && midi.LengthFrames > length)
            {
                length = midi.LengthFrames;
            }

            var end = offsetFrames + length;
            return end > 0 ? end : 0;
        }
    }

    /// <summary>
    /// Re-reads the track's offset and moves every source to the song position given.
    /// </summary>
    /// <param name="songFrame">The song position to move to.</param>
    internal void Seek(long songFrame)
    {
        CaptureOffset();

        trackFrame = songFrame - offsetFrames;
        var sourceFrame = trackFrame > 0 ? trackFrame : 0;

        audio?.Seek(sourceFrame);
        midi?.Seek(sourceFrame);

        // A switch that was mid-crossfade when the transport moved has no reason to continue.
        blend = TargetBlend();
    }

    /// <summary>Adds this track's contribution to the mix buffer.</summary>
    /// <param name="mix">Interleaved stereo accumulator; already holds the tracks mixed so far.</param>
    /// <param name="anySolo">Whether any track in the player is soloed.</param>
    internal void Render(Span<float> mix, bool anySolo)
    {
        var frames = mix.Length / 2;
        if (frames == 0)
        {
            return;
        }

        // A track that starts late emits silence until the song reaches it, and its sources stay
        // parked at frame zero meanwhile.
        var lead = 0;
        if (trackFrame < 0)
        {
            lead = (int)Math.Min(frames, -trackFrame);
            trackFrame += lead;
            if (lead == frames)
            {
                return;
            }
        }

        var toRender = frames - lead;
        var samples = toRender * 2;

        var audioSpan = default(Span<float>);
        var midiSpan = default(Span<float>);

        if (audio != null)
        {
            EnsureBuffer(ref audioBuffer, samples);
            audioSpan = audioBuffer.AsSpan(0, samples);
            audio.Render(audioSpan);
        }

        if (midi != null)
        {
            EnsureBuffer(ref midiBuffer, samples);
            midiSpan = midiBuffer.AsSpan(0, samples);
            midi.Render(midiSpan);
        }

        trackFrame += toRender;

        var audible = !track.Mute && (!anySolo || track.Solo);
        if (!audible)
        {
            // Still rendered - the sources have to stay in time - just not mixed in. Let the
            // crossfade settle so unmuting does not produce a ramp from wherever it stopped.
            blend = TargetBlend();
            return;
        }

        var gain = track.Gain;
        var midiGain = gain * track.MidiSourceGain;
        var pan = track.Pan;
        var leftGain = pan <= 0.0f ? 1.0f : 1.0f - pan;
        var rightGain = pan >= 0.0f ? 1.0f : 1.0f + pan;

        var target = TargetBlend();
        var current = blend;
        var offset = lead * 2;

        for (var i = 0; i < toRender; i++)
        {
            if (current < target)
            {
                current = Math.Min(target, current + crossfadeStep);
            }
            else if (current > target)
            {
                current = Math.Max(target, current - crossfadeStep);
            }

            var audioWeight = 1.0f - current;
            var midiWeight = current;

            var left = 0.0f;
            var right = 0.0f;

            if (audio != null && audioWeight > 0.0f)
            {
                left += audioSpan[i * 2] * gain * audioWeight;
                right += audioSpan[i * 2 + 1] * gain * audioWeight;
            }

            if (midi != null && midiWeight > 0.0f)
            {
                left += midiSpan[i * 2] * midiGain * midiWeight;
                right += midiSpan[i * 2 + 1] * midiGain * midiWeight;
            }

            mix[offset + i * 2] += left * leftGain;
            mix[offset + i * 2 + 1] += right * rightGain;
        }

        blend = current;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        audio?.Dispose();
        midi?.Dispose();
        audioBuffer = [];
        midiBuffer = [];
    }

    // 0 means the recording is heard, 1 the MIDI performance. A track with only one source is
    // pinned to it whatever ActiveSource says.
    private float TargetBlend()
    {
        if (audio == null)
        {
            return midi == null ? 0.0f : 1.0f;
        }

        if (midi == null)
        {
            return 0.0f;
        }

        return track.ActiveSource == TrackSource.Midi ? 1.0f : 0.0f;
    }

    private void CaptureOffset()
    {
        var seconds = track.Offset.TotalSeconds;
        offsetFrames = (long)Math.Round(seconds * sampleRate);

        if (midiDelay != null)
        {
            midiDelay.DelayFrames = (long)Math.Round(track.MidiSourceOffset.TotalSeconds * sampleRate);
        }
    }

    private static void EnsureBuffer(ref float[] buffer, int samples)
    {
        if (buffer.Length < samples)
        {
            buffer = new float[samples];
        }
    }
}
