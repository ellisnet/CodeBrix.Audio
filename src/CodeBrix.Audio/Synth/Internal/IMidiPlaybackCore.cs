using System;

namespace CodeBrix.Audio.Synth.Internal;

// This file is NOT part of the MeltySynth port; it is CodeBrix code added alongside it.
//
// The seam between MidiSynthDataProvider and the thing it renders. The provider held a
// MidiSequencer directly and read eleven things off it; a MidiStream is played by a different
// sequencer that answers all eleven the same way, so the provider holds whichever one is playing
// as one of these and asks it. Nothing else in the package needs to know which it has - which is
// what keeps a stream track in the multi-track player a small change rather than a parallel path.

/// <summary>
/// What <see cref="MidiSynthDataProvider"/> needs of whatever is driving the synthesizer: a
/// finished <see cref="MidiSequence"/> through <see cref="MidiSequencer"/>, or a growing
/// <see cref="MidiStream"/> through <see cref="MidiStreamSequencer"/>.
/// </summary>
/// <remarks>
/// Every member is a member the two sequencers already have; nothing here asks either of them to
/// behave differently. Neither implementation provides thread safety of its own - the provider
/// serializes rendering against the transport, exactly as it always has.
/// </remarks>
internal interface IMidiPlaybackCore
{
    /// <summary>Renders the next block of audio into the two buffers, which must be the same length.</summary>
    /// <param name="left">The buffer the left channel is written to.</param>
    /// <param name="right">The buffer the right channel is written to.</param>
    void Render(Span<float> left, Span<float> right);

    /// <summary>Stops playing and silences the synthesizer, releasing whatever was loaded.</summary>
    void Stop();

    /// <summary>Moves playback to the given position, replaying the controller state leading up to it.</summary>
    /// <param name="position">The position to seek to, from the start.</param>
    void Seek(TimeSpan position);

    /// <summary>The current playback position.</summary>
    TimeSpan Position { get; }

    /// <summary>
    /// How long what is loaded runs for: a sequence's length, or a stream's horizon, which grows as
    /// its producer appends. <see cref="TimeSpan.Zero"/> when nothing is loaded.
    /// </summary>
    TimeSpan Length { get; }

    /// <summary>
    /// Whether everything loaded has been delivered - the end of a sequence, or a completed stream
    /// that has drained. True when nothing is loaded, as both sequencers report it.
    /// </summary>
    bool IsEnded { get; }

    /// <summary>The playback speed multiplier, which scales the clock rather than the content.</summary>
    float Speed { get; set; }

    /// <summary>
    /// The hook that REPLACES delivery of each message to the synthesizer, or
    /// <see langword="null"/> for the direct path.
    /// </summary>
    MidiSequencer.MessageHook OnSendMessage { get; set; }

    /// <summary>The synthesizer being driven.</summary>
    IMidiSynthesizer Synthesizer { get; }

    /// <summary>The tempo in force at the current position, in quarter notes per minute.</summary>
    double CurrentBeatsPerMinute { get; }

    /// <summary>How far playback has travelled, in quarter-note beats from the start.</summary>
    double CurrentBeatPosition { get; }

    /// <summary>
    /// The number of beats in a bar at the current position. A sequence never carries one, so it
    /// reports the default of four; a stream reports what its last time signature declared.
    /// </summary>
    int BeatsPerBar { get; }
}
