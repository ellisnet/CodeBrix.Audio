namespace CodeBrix.Audio.ModestSynth;

/// <summary>
/// The seven knobs a consumer may turn on ONE General MIDI program, or on one note of the percussion
/// kit: level, brightness, attack, release, vibrato depth, reverb send and pan.
/// </summary>
/// <remarks>
/// <para>
/// WHY THESE SEVEN AND NOT THE WHOLE VOICING. The bank's rows - which oscillator, which envelope,
/// which filter, how the layers are tuned - are deliberately NOT public. A later release retunes
/// them after a listening session, and that release has to be a data change with no API change; if
/// the row format were public, a retune that wanted one more parameter would break every consumer.
/// These seven are applied ON TOP of a row and mean the same thing whatever the row underneath grows
/// into, so "make the celesta darker" and "give the choir a longer release" each stay one line
/// forever.
/// </para>
/// <para>
/// Everything here is relative except <see cref="ReverbSend" />: the multipliers start at 1 and the
/// offsets at 0, so a fresh adjustment changes nothing. Values are clamped rather than rejected.
/// </para>
/// <para>
/// An adjustment is read when a NOTE STARTS. Changing one while a note is sounding affects the next
/// note, not the one already playing, and it survives a program change because it belongs to the
/// program rather than to the channel.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var synthesizer = new GeneralMidiSynthesizer(44100);
///
/// // Make the celesta darker and quieter.
/// var celesta = synthesizer.Adjustments.Program(GeneralMidiProgram.Celesta);
/// celesta.Brightness = -0.8;          // most of an octave down on the filter
/// celesta.Level = 0.8;
///
/// // Give the choir a longer release and a deeper vibrato.
/// var choir = synthesizer.Adjustments.Program(GeneralMidiProgram.ChoirAahs);
/// choir.Release = 2.0;
/// choir.VibratoDepth = 1.5;
///
/// // Move the whole drum kit slightly right, and dry out the snare.
/// synthesizer.Adjustments.Percussion.Pan = 0.15;
/// synthesizer.Adjustments.PercussionNote(GeneralMidiPercussion.AcousticSnare).ReverbSend = 0.05;
/// </code>
/// </example>
public sealed class GeneralMidiAdjustment
{
    private double level = 1.0;
    private double brightness;
    private double attack = 1.0;
    private double release = 1.0;
    private double vibratoDepth = 1.0;
    private double pan;
    private double? reverbSend;

    /// <summary>
    /// A gain MULTIPLIER on the program's own level, 0 to 4. Default 1, which changes nothing.
    /// </summary>
    /// <remarks>
    /// The bank keeps its 128 programs at a comparable loudness on purpose, because a General MIDI
    /// file mixes itself assuming they are. Reach for this when one program is wrong for one piece,
    /// not as a general mixing desk.
    /// </remarks>
    public double Level
    {
        get => level;
        set => level = Clamp(value, 0.0, 4.0, level);
    }

    /// <summary>
    /// How far the program's filter moves, in OCTAVES: negative is darker, positive is brighter.
    /// -4 to 4, default 0.
    /// </summary>
    /// <remarks>
    /// A program whose voicing has no filter at all - the organs are the obvious ones - is not
    /// changed by this, because there is nothing to move.
    /// </remarks>
    public double Brightness
    {
        get => brightness;
        set => brightness = Clamp(value, -4.0, 4.0, brightness);
    }

    /// <summary>
    /// A MULTIPLIER on every attack time in the voicing, 0.05 to 20. Default 1. Below 1 is a faster
    /// start, above 1 a slower one.
    /// </summary>
    public double Attack
    {
        get => attack;
        set => attack = Clamp(value, 0.05, 20.0, attack);
    }

    /// <summary>
    /// A MULTIPLIER on every release time in the voicing, 0.05 to 20. Default 1.
    /// </summary>
    public double Release
    {
        get => release;
        set => release = Clamp(value, 0.05, 20.0, release);
    }

    /// <summary>
    /// A MULTIPLIER on the voicing's own vibrato depth, 0 to 4. Default 1; 0 takes the vibrato off
    /// entirely.
    /// </summary>
    /// <remarks>
    /// It scales what the PROGRAM asks for. The modulation wheel (CC&#160;1) is added on top of the
    /// result and is not scaled, so a player can always bring vibrato in by hand.
    /// </remarks>
    public double VibratoDepth
    {
        get => vibratoDepth;
        set => vibratoDepth = Clamp(value, 0.0, 4.0, vibratoDepth);
    }

    /// <summary>
    /// The reverb send to use INSTEAD of the program's own, 0 (dry) to 1 (fully wet).
    /// <see langword="null" /> - the default - leaves the program's own send alone.
    /// </summary>
    /// <remarks>
    /// This is the one knob that replaces rather than scales, because "make the snare dry" has an
    /// obvious answer and "multiply the snare's send by 0.2" does not. It is overridden in turn by a
    /// CC&#160;91 the music sends on the channel.
    /// </remarks>
    public double? ReverbSend
    {
        get => reverbSend;
        set => reverbSend = value.HasValue ? Clamp(value.Value, 0.0, 1.0, 0.0) : (double?)null;
    }

    /// <summary>
    /// How far to move the program in the stereo field, ADDED to wherever its voicing already puts
    /// it. -1 (hard left) to 1 (hard right), default 0.
    /// </summary>
    /// <remarks>
    /// It is an offset rather than a position so that the percussion kit keeps its layout: setting
    /// it on the whole kit slides every piece across together instead of collapsing the toms, the
    /// hi-hat and the cymbals into one place.
    /// </remarks>
    public double Pan
    {
        get => pan;
        set => pan = Clamp(value, -1.0, 1.0, pan);
    }

    /// <summary>Whether every knob is still where it started, so this adjustment does nothing.</summary>
    public bool IsDefault =>
        level == 1.0 && brightness == 0.0 && attack == 1.0 && release == 1.0 &&
        vibratoDepth == 1.0 && pan == 0.0 && !reverbSend.HasValue;

    /// <summary>Puts every knob back where it started.</summary>
    public void Reset()
    {
        level = 1.0;
        brightness = 0.0;
        attack = 1.0;
        release = 1.0;
        vibratoDepth = 1.0;
        pan = 0.0;
        reverbSend = null;
    }

    /// <summary>Copies another adjustment's values over this one's.</summary>
    /// <param name="other">What to copy. Null resets instead.</param>
    public void CopyFrom(GeneralMidiAdjustment other)
    {
        if (other == null)
        {
            Reset();
            return;
        }

        level = other.level;
        brightness = other.brightness;
        attack = other.attack;
        release = other.release;
        vibratoDepth = other.vibratoDepth;
        pan = other.pan;
        reverbSend = other.reverbSend;
    }

    /// <summary>Makes an independent copy.</summary>
    /// <returns>A copy carrying the same values.</returns>
    public GeneralMidiAdjustment Clone()
    {
        GeneralMidiAdjustment copy = new GeneralMidiAdjustment();
        copy.CopyFrom(this);
        return copy;
    }

    private static double Clamp(double value, double minimum, double maximum, double current)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) { return current; }

        return value < minimum ? minimum : value > maximum ? maximum : value;
    }
}
