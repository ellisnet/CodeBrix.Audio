using System;
using CodeBrix.Audio.Synth.Internal;

namespace CodeBrix.Audio.Synth;

// The playback-core seam a MidiSequencer presents to MidiSynthDataProvider (see
// Internal/IMidiPlaybackCore.cs). Every member forwards to one this class already has, or answers
// from the sequence it is playing - the public surface is untouched, and the implementation is
// explicit so nothing here shows up on it either.
//
// It lives beside the ported file rather than in it, the way MidiSequenceTempoMap.cs does, so the
// port stays as close to upstream as it can.
//
// This file is NOT part of the MeltySynth port; it is CodeBrix code added alongside it.
public sealed partial class MidiSequencer : IMidiPlaybackCore
{
    /// <summary>Renders the next block of audio, as <see cref="Render"/> does.</summary>
    /// <param name="left">The buffer the left channel is written to.</param>
    /// <param name="right">The buffer the right channel is written to.</param>
    void IMidiPlaybackCore.Render(Span<float> left, Span<float> right) => Render(left, right);

    /// <summary>Stops playing, as <see cref="Stop"/> does.</summary>
    void IMidiPlaybackCore.Stop() => Stop();

    /// <summary>Seeks, as <see cref="Seek"/> does.</summary>
    /// <param name="position">The position to seek to, from the start of the sequence.</param>
    void IMidiPlaybackCore.Seek(TimeSpan position) => Seek(position);

    /// <summary>The current playback position.</summary>
    TimeSpan IMidiPlaybackCore.Position => Position;

    /// <summary>
    /// How long the loaded sequence runs for, or <see cref="TimeSpan.Zero"/> when none is loaded.
    /// </summary>
    TimeSpan IMidiPlaybackCore.Length => midiSequence == null ? TimeSpan.Zero : midiSequence.Length;

    /// <summary>Whether the sequence has been played to its end - <see cref="EndOfSequence"/>.</summary>
    bool IMidiPlaybackCore.IsEnded => EndOfSequence;

    /// <summary>The playback speed multiplier.</summary>
    float IMidiPlaybackCore.Speed
    {
        get => Speed;
        set => Speed = value;
    }

    /// <summary>The hook that replaces delivery of each message to the synthesizer.</summary>
    MessageHook IMidiPlaybackCore.OnSendMessage
    {
        get => OnSendMessage;
        set => OnSendMessage = value;
    }

    /// <summary>The synthesizer being driven.</summary>
    IMidiSynthesizer IMidiPlaybackCore.Synthesizer => Synthesizer;

    /// <summary>
    /// The tempo in force at the current position, read from the sequence's own tempo map. A
    /// sequence that carries no tempo event of its own reports the MIDI default.
    /// </summary>
    double IMidiPlaybackCore.CurrentBeatsPerMinute
    {
        get
        {
            var map = midiSequence?.TempoMap;
            return map == null ? MidiTempoMap.DefaultBeatsPerMinute : map.BeatsPerMinuteAt(Position);
        }
    }

    /// <summary>How far playback has travelled, in quarter-note beats from the start.</summary>
    double IMidiPlaybackCore.CurrentBeatPosition
    {
        get
        {
            var map = midiSequence?.TempoMap;
            return map == null ? 0.0 : map.BeatPositionAt(Position);
        }
    }

    /// <summary>
    /// Four. A <see cref="MidiSequence"/> never parses a time signature, so there is nothing else
    /// it could honestly say.
    /// </summary>
    int IMidiPlaybackCore.BeatsPerBar => TempoSource.DefaultBeatsPerBar;
}
