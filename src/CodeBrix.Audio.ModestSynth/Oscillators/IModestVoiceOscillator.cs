namespace CodeBrix.Audio.ModestSynth.Oscillators;

/// <summary>
/// An oscillator that is played as a NOTE: it is started with a velocity, released when the key
/// comes up, and eventually says that it has nothing left to render.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IModestOscillator" /> is enough for a waveform that simply runs - a sine has no
/// opinion about note-on and note-off. A waveform whose sound depends on how hard the key was hit,
/// or that has envelopes of its own to run down after the key is released, needs this instead.
/// The <c>fm6op</c> oscillator is the first of them: its six operators each carry an envelope and
/// a velocity sensitivity.
/// </para>
/// <para>
/// The call order is the ordinary one plus a note-on:
/// <c>SetSampleRate</c>, <c>SetFrequency</c>, <c>Reset(startPhase)</c>, <c>NoteOn(velocity)</c>,
/// then <c>Render</c> per block, <c>NoteOff()</c> when the key comes up, and <c>Render</c> until
/// <see cref="IsFinished" /> is true.
/// </para>
/// <para>
/// Nothing here is thread-safe, and none of it allocates: an implementation is one voice's state,
/// owned by the thread rendering it.
/// </para>
/// </remarks>
public interface IModestVoiceOscillator : IModestOscillator
{
    /// <summary>Whether the key is currently held down - true between <see cref="NoteOn" /> and <see cref="NoteOff" />.</summary>
    bool IsKeyDown { get; }

    /// <summary>
    /// Whether the voice has finished: the key is up and nothing it can still render would be
    /// audible, so the voice may be recycled.
    /// </summary>
    /// <remarks>
    /// A voice whose envelopes are handed to the group's envelope - the <c>-1</c> release sentinel
    /// the format documents - never finishes on its own, because deciding when the note ends is
    /// exactly what it delegated. Whatever owns the group's envelope stops it.
    /// </remarks>
    bool IsFinished { get; }

    /// <summary>
    /// Starts the note.
    /// </summary>
    /// <param name="velocity">
    /// The MIDI velocity, 0 to 127. Values outside that range are clamped. A waveform with no
    /// velocity sensitivity ignores it.
    /// </param>
    /// <remarks>
    /// This restarts every envelope but does NOT touch the phase, so call
    /// <see cref="IModestOscillator.Reset" /> first when the start phase matters.
    /// </remarks>
    void NoteOn(int velocity);

    /// <summary>
    /// Releases the note, as when the key comes up.
    /// </summary>
    /// <remarks>
    /// Rendering continues - a release stage is still sound - until <see cref="IsFinished" /> is
    /// true. Calling this on a voice that is already released does nothing.
    /// </remarks>
    void NoteOff();
}
