using System;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.ModestSynth.Internal.Gm;

namespace CodeBrix.Audio.ModestSynth;

/// <summary>
/// Every <see cref="GeneralMidiAdjustment" /> a synthesizer or a library is carrying: one per
/// General MIDI program, one per percussion note, and one that covers the whole kit.
/// </summary>
/// <remarks>
/// <para>
/// This is the consumer's control over how the bank sounds (plan D49). The bank's own rows are
/// internal and free to be retuned in a later, data-only release; the handful of knobs per program
/// here are the stable surface over them.
/// </para>
/// <para>
/// Nothing is allocated for a program until it is asked for, so a synthesizer that nobody adjusts
/// carries nothing but an empty array, and a note-on for an unadjusted program does no work at all.
/// </para>
/// <para>
/// FOR THE KIT, the whole-kit adjustment and the per-note one are BOTH applied: the multipliers
/// multiply, the offsets add, and a per-note <see cref="GeneralMidiAdjustment.ReverbSend" /> wins
/// over the kit-wide one. That is what lets a consumer move the entire kit slightly right and still
/// dry out just the snare.
/// </para>
/// <para>
/// Not thread-safe, like everything else on the render path: set the knobs before playing, or from
/// the thread that drives the synthesizer.
/// </para>
/// </remarks>
public sealed class GeneralMidiAdjustments
{
    private const int PercussionCount =
        GeneralMidi.HighestPercussionNote - GeneralMidi.LowestPercussionNote + 1;

    private readonly GeneralMidiAdjustment[] programs = new GeneralMidiAdjustment[GeneralMidi.ProgramCount];
    private readonly GeneralMidiAdjustment[] percussionNotes = new GeneralMidiAdjustment[PercussionCount];

    private GeneralMidiAdjustment kit;

    /// <summary>Creates an empty set, in which nothing is adjusted.</summary>
    public GeneralMidiAdjustments()
    {
    }

    /// <summary>
    /// The adjustment that covers the WHOLE percussion kit, applied on top of every note of it.
    /// </summary>
    /// <remarks>
    /// Because <see cref="GeneralMidiAdjustment.Pan" /> is an offset rather than a position, moving
    /// the kit here slides its whole layout across the stereo field instead of flattening it.
    /// </remarks>
    public GeneralMidiAdjustment Percussion => kit ?? (kit = new GeneralMidiAdjustment());

    /// <summary>Whether anything at all has been adjusted.</summary>
    public bool HasAny
    {
        get
        {
            if (kit != null && !kit.IsDefault) { return true; }

            for (int i = 0; i < programs.Length; i++)
            {
                if (programs[i] != null && !programs[i].IsDefault) { return true; }
            }

            for (int i = 0; i < percussionNotes.Length; i++)
            {
                if (percussionNotes[i] != null && !percussionNotes[i].IsDefault) { return true; }
            }

            return false;
        }
    }

    /// <summary>The adjustment for one General MIDI program, created the first time it is asked for.</summary>
    /// <param name="program">The program number, 0 to 127.</param>
    /// <returns>That program's adjustment, which starts out changing nothing.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="program" /> is outside 0 to 127.</exception>
    public GeneralMidiAdjustment Program(int program)
    {
        if (program < 0 || program >= GeneralMidi.ProgramCount)
        {
            throw new ArgumentOutOfRangeException(nameof(program), program,
                "A General MIDI program is 0 to 127.");
        }

        return programs[program] ?? (programs[program] = new GeneralMidiAdjustment());
    }

    /// <summary>The adjustment for one General MIDI program, named.</summary>
    /// <param name="program">The program.</param>
    /// <returns>That program's adjustment, which starts out changing nothing.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="program" /> is not one of the 128 programs.</exception>
    public GeneralMidiAdjustment Program(GeneralMidiProgram program) => Program((int)program);

    /// <summary>The adjustment for one note of the percussion kit, created the first time it is asked for.</summary>
    /// <param name="noteNumber">The percussion note number, 35 to 81.</param>
    /// <returns>That note's adjustment, which starts out changing nothing.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="noteNumber" /> is outside 35 to 81.</exception>
    public GeneralMidiAdjustment PercussionNote(int noteNumber)
    {
        if (!GmBank.IsPercussionNote(noteNumber))
        {
            throw new ArgumentOutOfRangeException(nameof(noteNumber), noteNumber,
                "A General MIDI percussion note is 35 to 81.");
        }

        int index = noteNumber - GeneralMidi.LowestPercussionNote;
        return percussionNotes[index] ?? (percussionNotes[index] = new GeneralMidiAdjustment());
    }

    /// <summary>The adjustment for one note of the percussion kit, named.</summary>
    /// <param name="percussion">The percussion sound.</param>
    /// <returns>That note's adjustment, which starts out changing nothing.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="percussion" /> is outside 35 to 81.</exception>
    public GeneralMidiAdjustment PercussionNote(GeneralMidiPercussion percussion) =>
        PercussionNote((int)percussion);

    /// <summary>Puts every adjustment back where it started.</summary>
    public void Reset()
    {
        Array.Clear(programs, 0, programs.Length);
        Array.Clear(percussionNotes, 0, percussionNotes.Length);
        kit = null;
    }

    /// <summary>Copies another set's adjustments over this one's, replacing whatever was here.</summary>
    /// <param name="other">What to copy. Null resets instead.</param>
    public void CopyFrom(GeneralMidiAdjustments other)
    {
        Reset();

        if (other == null) { return; }

        for (int i = 0; i < programs.Length; i++)
        {
            if (other.programs[i] != null && !other.programs[i].IsDefault)
            {
                programs[i] = other.programs[i].Clone();
            }
        }

        for (int i = 0; i < percussionNotes.Length; i++)
        {
            if (other.percussionNotes[i] != null && !other.percussionNotes[i].IsDefault)
            {
                percussionNotes[i] = other.percussionNotes[i].Clone();
            }
        }

        if (other.kit != null && !other.kit.IsDefault) { kit = other.kit.Clone(); }
    }

    /// <summary>Makes an independent copy.</summary>
    /// <returns>A copy carrying the same adjustments.</returns>
    public GeneralMidiAdjustments Clone()
    {
        GeneralMidiAdjustments copy = new GeneralMidiAdjustments();
        copy.CopyFrom(this);
        return copy;
    }

    // Which section a program has been asked for. It is read separately from the rest because it
    // chooses the VOICING a note is built from rather than a number applied on top of one, and
    // because it must be answerable without building anything: an unadjusted program has no
    // adjustment object at all.
    internal GeneralMidiEnsemble EnsembleFor(int program)
    {
        GeneralMidiAdjustment adjustment = programs[program];

        return adjustment == null ? GeneralMidiEnsemble.Standard : adjustment.Ensemble;
    }

    // What a voice actually starts with, for a melodic program.
    internal GmVoiceAdjustment ResolveProgram(int program)
    {
        GeneralMidiAdjustment adjustment = programs[program];

        if (adjustment == null || adjustment.IsDefault) { return GmVoiceAdjustment.None; }

        return new GmVoiceAdjustment(
            adjustment.Level,
            adjustment.Brightness,
            adjustment.Attack,
            adjustment.Release,
            adjustment.VibratoDepth,
            adjustment.ReverbSend ?? double.NaN,
            adjustment.Pan);
    }

    // The same for a kit piece, with the whole-kit adjustment folded in.
    internal GmVoiceAdjustment ResolvePercussion(int noteNumber)
    {
        GeneralMidiAdjustment note = percussionNotes[noteNumber - GeneralMidi.LowestPercussionNote];
        bool hasNote = note != null && !note.IsDefault;
        bool hasKit = kit != null && !kit.IsDefault;

        if (!hasNote && !hasKit) { return GmVoiceAdjustment.None; }

        if (!hasKit) { return ResolveOne(note); }
        if (!hasNote) { return ResolveOne(kit); }

        double send = note.ReverbSend ?? kit.ReverbSend ?? double.NaN;

        return new GmVoiceAdjustment(
            note.Level * kit.Level,
            note.Brightness + kit.Brightness,
            note.Attack * kit.Attack,
            note.Release * kit.Release,
            note.VibratoDepth * kit.VibratoDepth,
            send,
            note.Pan + kit.Pan);
    }

    private static GmVoiceAdjustment ResolveOne(GeneralMidiAdjustment adjustment) =>
        new GmVoiceAdjustment(
            adjustment.Level,
            adjustment.Brightness,
            adjustment.Attack,
            adjustment.Release,
            adjustment.VibratoDepth,
            adjustment.ReverbSend ?? double.NaN,
            adjustment.Pan);
}
