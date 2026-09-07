namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

/// <summary>
/// The sound generator behind one voice: a sample player, or an oscillator supplied by an add-on
/// package through <see cref="DecentSamplerExtensions"/>.
/// </summary>
/// <remarks>
/// <para>
/// One instance belongs to one voice and is never shared. The voice runtime owns the lifecycle:
/// <see cref="Start"/> when a note begins, <see cref="SetPitch"/> once per render block (glide, pitch
/// bend and live tuning all arrive that way), <see cref="Render"/> once per block, and
/// <see cref="NoteOff"/> when the key is released. The voice stops when <see cref="IsFinished"/> turns
/// true or when its amplitude envelope finishes, whichever comes first - unless the source declares
/// <see cref="OwnsRelease"/>, in which case only <see cref="IsFinished"/> ends it.
/// </para>
/// <para>
/// Everything here runs on the audio thread. An implementation must not allocate, lock or touch a file
/// inside <see cref="Render"/> or <see cref="SetPitch"/>; do that work in the factory or in
/// <see cref="Start"/> at the latest.
/// </para>
/// </remarks>
public interface IVoiceSource
{
    /// <summary>
    /// Whether the source writes both channels. When <see langword="false"/> it fills only the left
    /// buffer and the voice uses that one signal for both sides of the stereo image.
    /// </summary>
    bool IsStereo { get; }

    /// <summary>Whether the source has run out of sound and the voice may be recycled.</summary>
    bool IsFinished { get; }

    /// <summary>
    /// Whether the source's own envelopes decide when the note ends, so the group's amplitude
    /// envelope must not release over the top of them. <see langword="false"/> unless a source says
    /// otherwise, which is what every sampled zone wants.
    /// </summary>
    /// <remarks>
    /// MEASURED (round 4, item 56): a <c>fm6op</c> voice's tail is the longest OPERATOR release and
    /// never the group envelope's - a group release of 3 s over operator releases of 0.02 s was at
    /// the silence floor half a second after the note-off. A source that returns
    /// <see langword="true"/> here keeps the level it had at note-off until <see cref="IsFinished"/>
    /// turns true; voice stealing and <c>silencedByTags</c> still fade it out through the group
    /// envelope, because those are not the key coming up.
    /// </remarks>
    bool OwnsRelease => false;

    /// <summary>
    /// Begins a note.
    /// </summary>
    /// <param name="note">The MIDI note number that started the voice, 0 to 127.</param>
    /// <param name="velocity">The note-on velocity, 1 to 127.</param>
    /// <param name="pitchHz">
    /// The frequency the note should sound at, in Hz, including tuning and any glide start offset. A
    /// sample source turns it into a playback ratio against the zone's root note; an oscillator plays
    /// it directly.
    /// </param>
    void Start(int note, int velocity, double pitchHz);

    /// <summary>Tells the source the key was released. A one-shot source may ignore it.</summary>
    void NoteOff();

    /// <summary>
    /// Sets the frequency for the block about to be rendered.
    /// </summary>
    /// <param name="pitchHz">The frequency in Hz.</param>
    void SetPitch(double pitchHz);

    /// <summary>
    /// Renders one block.
    /// </summary>
    /// <param name="left">The left channel buffer, filled from index 0.</param>
    /// <param name="right">The right channel buffer, filled only when <see cref="IsStereo"/>.</param>
    /// <param name="frames">
    /// How many frames to write. The voice runtime always passes a whole block and buffers exactly that
    /// long, so <paramref name="frames"/> equals the buffer length; a source may rely on that.
    /// </param>
    /// <returns>
    /// <see langword="false"/> when the source produced nothing at all and the voice should end;
    /// <see langword="true"/> when the buffers hold audio (silence at the tail of a sample counts).
    /// </returns>
    bool Render(float[] left, float[] right, int frames);

    /// <summary>
    /// Sets one of the source's named parameters - the developer guide's <c>OSCILLATOR_*</c> binding
    /// parameter names, matched case-insensitively.
    /// </summary>
    /// <param name="name">The parameter name, for example <c>OSCILLATOR_WAVETABLE_POSITION</c>.</param>
    /// <param name="value">The new value.</param>
    /// <returns><see langword="true"/> when the source knows the parameter.</returns>
    bool TrySetParameter(string name, double value);

    /// <summary>
    /// Reads one of the source's named parameters.
    /// </summary>
    /// <param name="name">The parameter name, matched case-insensitively.</param>
    /// <param name="value">The current value, or zero when the parameter is unknown.</param>
    /// <returns><see langword="true"/> when the source knows the parameter.</returns>
    bool TryGetParameter(string name, out double value);
}
