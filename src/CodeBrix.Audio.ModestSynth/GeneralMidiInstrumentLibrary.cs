using System;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.ModestSynth;

/// <summary>
/// The complete General MIDI Level 1 sound set, synthesized: all 128 programs and all 47 percussion
/// notes, offered to <see cref="InstrumentLibraryRegistry" /> under the name
/// <see cref="LibraryName" />.
/// </summary>
/// <remarks>
/// <para>
/// REGISTER IT YOURSELF: <c>GeneralMidiInstrumentLibrary.Register();</c>. CodeBrix.Audio ships no
/// instruments and registers nothing, so with an empty registry nothing plays and nothing renders.
/// The call is idempotent and safe from any thread, and there is deliberately no module initializer
/// doing it for you - a module initializer only runs once something in the assembly is touched,
/// which trimming and lazy assembly loading make unreliable.
/// </para>
/// <para>
/// IT OFFERS BOTH SHAPES. <see cref="CreateSynthesizer" /> hands back a synthesizer PINNED to one
/// program - the per-part road, where the caller has decided what each part sounds like and a
/// program change in the music must not undo that - and
/// <see cref="CreateMultiTimbralSynthesizer" /> hands back one synthesizer that honours program
/// change inline, which is the "play this .mid with no configuration" road.
/// <see cref="CreatePercussionSynthesizer" /> is the kit, pinned, sounding on ANY channel a router
/// puts it on rather than only on channel 10.
/// </para>
/// <para>
/// IT SOUNDS LIKE A GOOD SYNTHESIZER, NOT LIKE RECORDED INSTRUMENTS, and it is meant to. Pads,
/// bells, celesta, vibraphone, electric pianos, organs, plucked strings, choir textures, string
/// ensembles and synth leads and basses are where it is strong; a solo bowed string and a concert
/// piano are where synthesis is weakest and this is an honest best rather than an imitation. Nothing
/// here is a recording: every sound is oscillators, envelopes, filters and tables, which is why the
/// whole bank costs tens of kilobytes.
/// </para>
/// <para>
/// PER-PROGRAM ADJUSTMENTS. <see cref="Adjustments" /> is a TEMPLATE copied into every synthesizer
/// this library creates afterwards, so "make the celesta darker everywhere" is set once; each
/// synthesizer also has adjustments of its own for changes that should not be shared.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// GeneralMidiInstrumentLibrary.Register();
///
/// var library = InstrumentLibraryRegistry.Resolve(GeneralMidiInstrumentLibrary.LibraryName);
/// var flute = library.CreateSynthesizer((int)GeneralMidiProgram.Flute, 44100);
/// var kit = library.CreatePercussionSynthesizer(44100);
/// var whole = library.CreateMultiTimbralSynthesizer(44100);
/// </code>
/// </example>
public sealed class GeneralMidiInstrumentLibrary : IInstrumentLibrary
{
    /// <summary>The name this library registers under, matched case-insensitively.</summary>
    public const string LibraryName = "ModestSynthGm";

    private static readonly object Gate = new object();

    private static readonly GeneralMidiInstrumentLibrary Shared = new GeneralMidiInstrumentLibrary();

    private static bool registered;

    private GeneralMidiInstrumentLibrary()
    {
        Adjustments = new GeneralMidiAdjustments();
    }

    /// <summary>
    /// The one shared library instance. It is a single instance on purpose: the registry treats the
    /// same instance registered twice as a no-op but two different instances under one name as an
    /// error, so this is what makes <see cref="Register" /> idempotent for free.
    /// </summary>
    public static GeneralMidiInstrumentLibrary Instance => Shared;

    /// <summary>Whether <see cref="Register" /> has run.</summary>
    public static bool IsRegistered
    {
        get { lock (Gate) { return registered; } }
    }

    /// <inheritdoc />
    public string Name => LibraryName;

    /// <inheritdoc />
    public string Description =>
        "The complete General MIDI Level 1 sound set synthesized by CodeBrix.Audio.ModestSynth - " +
        "128 programs and the 47-note percussion kit, built from oscillators, envelopes, filters " +
        "and tables rather than from recorded samples.";

    /// <inheritdoc />
    /// <remarks>
    /// All 128 programs over the whole keyboard, and percussion notes 35 to 81. Nothing in the bank
    /// is a placeholder and nothing falls back to another program: every one of the 175 voicings is
    /// deliberate.
    /// </remarks>
    public InstrumentCoverage Coverage => InstrumentCoverage.General;

    /// <inheritdoc />
    public bool SupportsPerPart => true;

    /// <inheritdoc />
    public bool SupportsMultiTimbral => true;

    /// <summary>
    /// Per-program adjustments COPIED into every synthesizer this library creates from now on.
    /// </summary>
    /// <remarks>
    /// They are a template, not a live link: changing one here does not reach a synthesizer that has
    /// already been created. Use the synthesizer's own <see cref="GeneralMidiSynthesizer.Adjustments" />
    /// for that.
    /// </remarks>
    public GeneralMidiAdjustments Adjustments { get; }

    /// <summary>
    /// Registers this library with <see cref="InstrumentLibraryRegistry" /> under
    /// <see cref="LibraryName" />.
    /// </summary>
    /// <remarks>
    /// Idempotent and safe to call from any thread; calling it more than once does nothing. The
    /// FIRST library registered in a process becomes the registry's default, so an application that
    /// registers more than one should ask for the one it wants by name.
    /// </remarks>
    public static void Register()
    {
        lock (Gate)
        {
            if (registered) { return; }

            InstrumentLibraryRegistry.Register(Shared);
            registered = true;
        }
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="program" /> is outside 0 to 127, or <paramref name="sampleRate" /> is not a
    /// rate this package can synthesize at.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The synthesizer is PINNED: it plays that one program on every one of the sixteen channels and
    /// ignores program change and bank select, because the caller - not the music - decided what this
    /// part sounds like.
    /// </para>
    /// <para>
    /// It comes back PREPARED (<see cref="GeneralMidiSynthesizer.Prepare(int)" />): the program's
    /// oscillators are already built and its code has already run once, HERE, on the thread that
    /// asked for it - so the first note on the audio thread builds nothing and compiles nothing.
    /// That is what makes creation, not the first note, the moment that costs; create parts ahead
    /// of when they have to sound, off the audio thread. The sound is exactly what an unprepared
    /// synthesizer would have made.
    /// </para>
    /// </remarks>
    public IMidiSynthesizer CreateSynthesizer(int program, int sampleRate)
    {
        if (program < 0 || program >= GeneralMidi.ProgramCount)
        {
            throw new ArgumentOutOfRangeException(nameof(program), program,
                "A General MIDI program is 0 to 127.");
        }

        GeneralMidiSynthesizer synthesizer =
            GeneralMidiSynthesizer.CreateForProgram(program, Settings(sampleRate));

        synthesizer.Adjustments.CopyFrom(Adjustments);

        // AFTER the adjustments, because an adjustment's Ensemble chooses which voicing the first
        // note plays - and that is the one worth preparing.
        synthesizer.Prepare(program);
        return synthesizer;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="sampleRate" /> is not a rate this package can synthesize at.
    /// </exception>
    /// <remarks>
    /// <para>
    /// The kit sounds on ANY channel this synthesizer is given, not only on channel 10, because a
    /// router forwards whatever channel the music used and a rendition may put the drums elsewhere.
    /// </para>
    /// <para>
    /// It comes back PREPARED (<see cref="GeneralMidiSynthesizer.PreparePercussion()" />): every kit
    /// piece's oscillators are built and every piece has been struck once on a throwaway, here on the
    /// calling thread, so the first hit on the audio thread builds nothing and compiles nothing.
    /// </para>
    /// </remarks>
    public IMidiSynthesizer CreatePercussionSynthesizer(int sampleRate)
    {
        GeneralMidiSynthesizer synthesizer =
            GeneralMidiSynthesizer.CreateForPercussion(Settings(sampleRate));

        synthesizer.Adjustments.CopyFrom(Adjustments);
        synthesizer.PreparePercussion();
        return synthesizer;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="sampleRate" /> is not a rate this package can synthesize at.
    /// </exception>
    /// <remarks>
    /// <para>
    /// This one honours program change on every channel and plays the kit on
    /// <see cref="GeneralMidi.PercussionChannel" />, which is what a General MIDI file expects.
    /// </para>
    /// <para>
    /// It is NOT prepared, because nothing yet says which programs the music will choose: a program
    /// is built at its first note, on whatever thread plays it. A host that knows - it has read the
    /// file's program changes, or is about to send one - calls
    /// <see cref="GeneralMidiSynthesizer.Prepare(int)" /> and
    /// <see cref="GeneralMidiSynthesizer.PreparePercussion()" /> itself, off the audio thread.
    /// </para>
    /// </remarks>
    public IMidiSynthesizer CreateMultiTimbralSynthesizer(int sampleRate)
    {
        GeneralMidiSynthesizer synthesizer = new GeneralMidiSynthesizer(Settings(sampleRate));

        synthesizer.Adjustments.CopyFrom(Adjustments);
        return synthesizer;
    }

    private static GeneralMidiSynthesizerSettings Settings(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate,
                "The sample rate must be greater than zero.");
        }

        return new GeneralMidiSynthesizerSettings(sampleRate);
    }
}
