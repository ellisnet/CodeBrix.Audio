using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Instruments.Internal;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.Sfz;

namespace CodeBrix.Audio.Instruments;

/// <summary>
/// A library that starts as another library and lets a developer replace its voices one at a time
/// with instruments of their own - a Decent Sampler pack, an SFZ file, one program of a SoundFont,
/// anything that is an <see cref="IMidiSynthesizer"/>.
/// </summary>
/// <remarks>
/// <para>
/// THE WORKFLOW THIS IS FOR. Start from a General MIDI library, hear the piece, and then replace
/// the voices you want to replace, one at a time, listening in between. Everything you have not
/// replaced keeps coming from the library you started with, so each change is one line and nothing
/// else moves:
/// </para>
/// <code>
/// GeneralMidiInstrumentLibrary.Register();                          // CodeBrix.Audio.ModestSynth
///
/// var voices = new MappedInstrumentLibrary("MyVoices", "General MIDI, with a few of my own", "ModestSynthGm");
/// voices.Register();
///
/// // Listen. Then: "I want THIS voice replaced with the pack I downloaded from Pianobook."
/// voices.SetInstrument(GeneralMidiProgram.ChoirAahs, "/home/me/Pianobook/Whisper Choir/Whisper Choir.dspreset");
///
/// // Listen again. Then: "and THIS one replaced with another."
/// voices.SetInstrument(GeneralMidiProgram.AcousticBass, "/home/me/sfz/upright-bass.sfz");
///
/// // Not what you hoped for? Put it back and the base library plays it again.
/// voices.ClearInstrument(GeneralMidiProgram.AcousticBass);
/// </code>
/// <para>
/// A BASE LIBRARY IS OPTIONAL, and may be given either as an instance or as the NAME of a library
/// in <see cref="InstrumentLibraryRegistry"/> - the name a developer already writes everywhere
/// else. A name is resolved on first use, so the base may be registered before or after this
/// library is built, and an unregistered name gives the registry's own error, listing what IS
/// registered. With no base at all, this library covers exactly the voices it has been given and
/// nothing else.
/// </para>
/// <para>
/// INSTRUMENTS MAY BE SET, REPLACED AND CLEARED AT ANY TIME - before or after
/// <see cref="Register"/>. A change takes effect for the synthesizers built AFTERWARDS: one that
/// is already playing keeps the instrument it was built with, which is what makes swapping a voice
/// while a piece is sounding a safe thing to do.
/// </para>
/// <para>
/// FILES ARE LOADED ONCE AND SHARED. Setting an instrument from a path parses that file
/// immediately - so a mistake in the path is an error where you wrote it, not silence later - and
/// every synthesizer built from it afterwards renders from that one loaded instrument, exactly as
/// <see cref="SoundFontInstrumentLibrary"/> shares one <see cref="SoundFont"/>. A loaded instrument
/// is held for the life of this library; a developer who wants to control that themselves passes a
/// factory over an instrument they own.
/// </para>
/// <para>
/// BOTH SHAPES ARE OFFERED. <see cref="CreateSynthesizer"/> hands back the substitute for a program
/// that has one and the base library's own per-part synthesizer for a program that does not; either
/// way it is pinned to its instrument, ignores program change and bank select, and sounds on
/// whichever channel the music uses. <see cref="CreateMultiTimbralSynthesizer"/> hands back ONE
/// synthesizer that honours the program changes the music carries, playing each channel with
/// whichever instrument that channel's current program calls for.
/// </para>
/// <para>
/// THIS SUBSTITUTES BY PROGRAM - "everything that plays Choir Aahs". Replacing ONE PART when two
/// parts share a program is a different question, answered per channel by
/// <see cref="RoutingSynthesizer.SetChannel(int, IMidiSynthesizer)"/> today and by the
/// music-generation package's renditions later. The two compose: build a router, and voice each
/// channel from whichever library - this one included - has the sound that part wants.
/// </para>
/// <para>
/// Every member is safe to call from several threads at once, as the registry is. The SYNTHESIZERS
/// this library hands back are not: <see cref="IMidiSynthesizer"/> is single-threaded by contract.
/// </para>
/// </remarks>
public sealed class MappedInstrumentLibrary : IInstrumentLibrary
{
    private static readonly string[] DecentSamplerExtensions = [".dspreset", ".dslibrary", ".dsbundle"];

    private readonly object gate = new object();

    private readonly Dictionary<int, InstrumentSubstitution> substitutions =
        new Dictionary<int, InstrumentSubstitution>();

    private readonly string name;
    private readonly string description;
    private readonly string requestedBaseLibraryName;

    private IInstrumentLibrary baseLibrary;
    private InstrumentSubstitution percussionSubstitution;
    private DecentSamplerInstrumentCache decentSamplerInstruments;
    private SfzInstrumentCache sfzInstruments;
    private Dictionary<string, SoundFontInstrumentLibrary> soundFonts;

    /// <summary>Creates a library with no base: it covers only the voices it is given.</summary>
    /// <param name="name">How a consumer asks for this library; unique in the registry.</param>
    /// <param name="description">What this library sounds like, in a sentence.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> is null.</exception>
    public MappedInstrumentLibrary(string name, string description)
        : this(name, description, null, null)
    {
    }

    /// <summary>Creates a library that starts as another library.</summary>
    /// <param name="name">How a consumer asks for this library; unique in the registry.</param>
    /// <param name="description">What this library sounds like, in a sentence.</param>
    /// <param name="baseLibrary">
    /// The library that plays every program no instrument has been set for.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or blank.</exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="description"/> or <paramref name="baseLibrary"/> is null.
    /// </exception>
    public MappedInstrumentLibrary(string name, string description, IInstrumentLibrary baseLibrary)
        : this(name, description, baseLibrary, null)
    {
        if (baseLibrary == null)
        {
            throw new ArgumentNullException(nameof(baseLibrary));
        }
    }

    /// <summary>Creates a library that starts as a registered library, named.</summary>
    /// <param name="name">How a consumer asks for this library; unique in the registry.</param>
    /// <param name="description">What this library sounds like, in a sentence.</param>
    /// <param name="baseLibraryName">
    /// The registry name of the library that plays every program no instrument has been set for -
    /// "ModestSynthGm", "FluidR3Gm". It is resolved on FIRST USE, so it may be registered after
    /// this library is built; a name that is still not registered then gives the registry's own
    /// error.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="name"/> or <paramref name="baseLibraryName"/> is null or blank.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> is null.</exception>
    public MappedInstrumentLibrary(string name, string description, string baseLibraryName)
        : this(name, description, null, baseLibraryName)
    {
        if (string.IsNullOrWhiteSpace(baseLibraryName))
        {
            throw new ArgumentException(
                "A base instrument library name is required. Use the constructor without one to " +
                "build a library that covers only the instruments you set yourself.",
                nameof(baseLibraryName));
        }
    }

    private MappedInstrumentLibrary(
        string name, string description, IInstrumentLibrary baseLibrary, string baseLibraryName)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "An instrument library must have a name: it is how a consumer asks for it.", nameof(name));
        }

        if (description == null)
        {
            throw new ArgumentNullException(nameof(description));
        }

        this.name = name;
        this.description = description;
        this.baseLibrary = baseLibrary;
        requestedBaseLibraryName = baseLibraryName;
    }

    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public string Description => description;

    /// <summary>
    /// The name of the library that plays everything no instrument has been set for, or null when
    /// there is none. Reading it never resolves anything and never throws.
    /// </summary>
    public string BaseLibraryName
    {
        get
        {
            lock (gate)
            {
                return baseLibrary != null ? baseLibrary.Name : requestedBaseLibraryName;
            }
        }
    }

    /// <summary>Whether this library has a base library to play what it has not been given.</summary>
    public bool HasBaseLibrary
    {
        get
        {
            lock (gate)
            {
                return baseLibrary != null || requestedBaseLibraryName != null;
            }
        }
    }

    /// <summary>
    /// The General MIDI programs an instrument of the developer's own has been set for, in
    /// ascending order.
    /// </summary>
    public IReadOnlyList<int> SubstitutedPrograms
    {
        get
        {
            lock (gate)
            {
                return substitutions.Keys.OrderBy(program => program).ToArray();
            }
        }
    }

    /// <summary>Whether an instrument of the developer's own plays the percussion kit.</summary>
    public bool HasPercussionSubstitute
    {
        get
        {
            lock (gate)
            {
                return percussionSubstitution != null;
            }
        }
    }

    /// <summary>
    /// What this library can really play: the base library's coverage with everything set here
    /// applied on top of it.
    /// </summary>
    /// <remarks>
    /// It is worked out afresh on every read, so it always reflects the instruments set so far. A
    /// program whose substitute answers to part of the keyboard reports that part, which is the
    /// whole point: a Pianobook-style instrument sampled over two octaves says so here instead of
    /// going silent on the notes it does not have.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A base library was named and no library is registered under that name.
    /// </exception>
    public InstrumentCoverage Coverage
    {
        get
        {
            var library = ResolveBaseLibrary();

            Dictionary<int, InstrumentSubstitution> melodic;
            InstrumentSubstitution kit;

            lock (gate)
            {
                melodic = new Dictionary<int, InstrumentSubstitution>(substitutions);
                kit = percussionSubstitution;
            }

            var programs = new Dictionary<int, InstrumentKeyRange>();
            var notes = new HashSet<int>();

            if (library != null)
            {
                var baseCoverage = library.Coverage;

                foreach (var program in baseCoverage.Programs)
                {
                    programs[program] = baseCoverage.KeyRangeOf(program);
                }

                foreach (var note in baseCoverage.PercussionNotes)
                {
                    notes.Add(note);
                }
            }

            foreach (var entry in melodic)
            {
                // The substitute REPLACES what the base said about this program, range and all: it
                // is the instrument that will really play, so it is the one that decides.
                programs[entry.Key] = entry.Value.ResolveKeyRange();
            }

            if (kit != null)
            {
                notes.Clear();

                foreach (var note in kit.ResolvePercussionNotes())
                {
                    notes.Add(note);
                }
            }

            return new InstrumentCoverage(programs, notes);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// True unless a base library is present and does not offer the per-part shape. A program with
    /// an instrument of its own is always available per part, whatever the base does; ask
    /// <see cref="Coverage"/> whether a particular program can be played at all.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A base library was named and no library is registered under that name.
    /// </exception>
    public bool SupportsPerPart
    {
        get
        {
            var library = ResolveBaseLibrary();
            return library == null || library.SupportsPerPart;
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// True when the base library offers the multi-timbral shape, and true when there is no base
    /// library at all - a mapping with no base still honours program changes, and a channel whose
    /// program has no instrument is simply silent.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A base library was named and no library is registered under that name.
    /// </exception>
    public bool SupportsMultiTimbral
    {
        get
        {
            var library = ResolveBaseLibrary();
            return library == null || library.SupportsMultiTimbral;
        }
    }

    /// <summary>Whether an instrument of the developer's own plays a General MIDI program.</summary>
    /// <param name="program">The General MIDI program number, 0 to 127.</param>
    /// <returns>True when an instrument has been set for that program.</returns>
    public bool HasSubstitute(int program)
    {
        lock (gate)
        {
            return substitutions.ContainsKey(program);
        }
    }

    /// <summary>Whether an instrument of the developer's own plays a General MIDI program.</summary>
    /// <param name="program">The General MIDI program.</param>
    /// <returns>True when an instrument has been set for that program.</returns>
    public bool HasSubstitute(GeneralMidiProgram program) => HasSubstitute((int)program);

    // ----- setting an instrument ---------------------------------------------------------------

    /// <summary>
    /// Plays a General MIDI program with a synthesizer of your own, built on demand.
    /// </summary>
    /// <param name="program">The General MIDI program number to replace, 0 to 127.</param>
    /// <param name="synthesizerFactory">
    /// Builds the instrument. It is handed the SAMPLE RATE and must return a synthesizer that
    /// renders at exactly that rate - a <see cref="DecentSamplerSynthesizer"/>, an
    /// <see cref="SfzSynthesizer"/>, one program of another library, anything at all. It is called
    /// once per synthesizer this library is asked for, so it must build a new one every time.
    /// </param>
    /// <param name="keyRange">
    /// The notes this instrument answers to. Leave it out for the whole keyboard, which is what a
    /// synthesizer built in code usually covers.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="program"/> is outside 0 to 127.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="synthesizerFactory"/> is null.</exception>
    public void SetInstrument(
        int program, Func<int, IMidiSynthesizer> synthesizerFactory, InstrumentKeyRange keyRange = default)
    {
        CheckProgram(program);

        if (synthesizerFactory == null)
        {
            throw new ArgumentNullException(nameof(synthesizerFactory));
        }

        Store(program, InstrumentSubstitution.ForProgram(synthesizerFactory, keyRange, null));
    }

    /// <summary>
    /// Plays a General MIDI program with a synthesizer of your own, built on demand.
    /// </summary>
    /// <param name="program">The General MIDI program to replace.</param>
    /// <param name="synthesizerFactory">
    /// Builds the instrument, at the sample rate it is handed.
    /// </param>
    /// <param name="keyRange">The notes this instrument answers to; the whole keyboard when omitted.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="program"/> is outside 0 to 127.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="synthesizerFactory"/> is null.</exception>
    public void SetInstrument(
        GeneralMidiProgram program,
        Func<int, IMidiSynthesizer> synthesizerFactory,
        InstrumentKeyRange keyRange = default) =>
        SetInstrument((int)program, synthesizerFactory, keyRange);

    /// <summary>
    /// Plays a General MIDI program with an instrument file: a Decent Sampler preset, an SFZ file,
    /// or the same-numbered program of a SoundFont.
    /// </summary>
    /// <param name="program">The General MIDI program number to replace, 0 to 127.</param>
    /// <param name="instrumentPath">
    /// A <c>.dspreset</c>, <c>.dslibrary</c> or <c>.dsbundle</c> (Decent Sampler) or a Decent
    /// Sampler library folder; a <c>.sfz</c>; or a <c>.sf2</c>, whose program of the same number is
    /// used. The file is loaded HERE, once, and shared by every synthesizer built from it.
    /// </param>
    /// <param name="keyRange">
    /// The notes this instrument answers to. Leave it out and the range is read from the
    /// instrument's own samples.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="program"/> is outside 0 to 127.</exception>
    /// <exception cref="ArgumentException"><paramref name="instrumentPath"/> is null or blank.</exception>
    /// <exception cref="NotSupportedException">The file is not a kind this library can load.</exception>
    /// <exception cref="FileNotFoundException">There is no file at that path.</exception>
    public void SetInstrument(int program, string instrumentPath, InstrumentKeyRange keyRange = default)
    {
        CheckProgram(program);

        var loaded = LoadInstrument(instrumentPath, program, forPercussion: false);

        Store(program, InstrumentSubstitution.ForProgram(loaded.Factory, keyRange, loaded.KeyRange));
    }

    /// <summary>
    /// Plays a General MIDI program with an instrument file: a Decent Sampler preset, an SFZ file,
    /// or the same-numbered program of a SoundFont.
    /// </summary>
    /// <param name="program">The General MIDI program to replace.</param>
    /// <param name="instrumentPath">The instrument file, loaded here and shared afterwards.</param>
    /// <param name="keyRange">
    /// The notes this instrument answers to; read from the instrument itself when omitted.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="program"/> is outside 0 to 127.</exception>
    /// <exception cref="ArgumentException"><paramref name="instrumentPath"/> is null or blank.</exception>
    /// <exception cref="NotSupportedException">The file is not a kind this library can load.</exception>
    /// <exception cref="FileNotFoundException">There is no file at that path.</exception>
    public void SetInstrument(
        GeneralMidiProgram program, string instrumentPath, InstrumentKeyRange keyRange = default) =>
        SetInstrument((int)program, instrumentPath, keyRange);

    /// <summary>Plays a General MIDI program with one program of a SoundFont file.</summary>
    /// <param name="program">The General MIDI program number to replace, 0 to 127.</param>
    /// <param name="soundFontPath">
    /// The <c>.sf2</c> file. It is loaded HERE, once, and every synthesizer built from it - for
    /// this program and any other - renders from that one copy.
    /// </param>
    /// <param name="soundFontProgram">The program number to take from that SoundFont, 0 to 127.</param>
    /// <param name="keyRange">
    /// The notes this instrument answers to; read from the SoundFont's own regions when omitted.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="program"/> or <paramref name="soundFontProgram"/> is outside 0 to 127.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="soundFontPath"/> is null or blank.</exception>
    public void SetInstrumentFromSoundFont(
        int program, string soundFontPath, int soundFontProgram, InstrumentKeyRange keyRange = default)
    {
        CheckProgram(program);
        CheckProgram(soundFontProgram, nameof(soundFontProgram));

        var soundFont = SoundFontFor(soundFontPath);

        Store(
            program,
            InstrumentSubstitution.ForProgram(
                sampleRate => soundFont.CreateSynthesizer(soundFontProgram, sampleRate),
                keyRange,
                () => soundFont.Coverage.KeyRangeOf(soundFontProgram)));
    }

    /// <summary>Plays a General MIDI program with one program of a SoundFont file.</summary>
    /// <param name="program">The General MIDI program to replace.</param>
    /// <param name="soundFontPath">The <c>.sf2</c> file, loaded here and shared afterwards.</param>
    /// <param name="soundFontProgram">The program number to take from that SoundFont, 0 to 127.</param>
    /// <param name="keyRange">
    /// The notes this instrument answers to; read from the SoundFont's own regions when omitted.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="program"/> or <paramref name="soundFontProgram"/> is outside 0 to 127.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="soundFontPath"/> is null or blank.</exception>
    public void SetInstrumentFromSoundFont(
        GeneralMidiProgram program,
        string soundFontPath,
        int soundFontProgram,
        InstrumentKeyRange keyRange = default) =>
        SetInstrumentFromSoundFont((int)program, soundFontPath, soundFontProgram, keyRange);

    /// <summary>Plays a General MIDI program with one program of another instrument library.</summary>
    /// <param name="program">The General MIDI program number to replace, 0 to 127.</param>
    /// <param name="library">The library to take the instrument from.</param>
    /// <param name="libraryProgram">The program number to take from it, 0 to 127.</param>
    /// <param name="keyRange">
    /// The notes this instrument answers to; read from that library's coverage when omitted.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="program"/> or <paramref name="libraryProgram"/> is outside 0 to 127.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="library"/> is null.</exception>
    public void SetInstrumentFromLibrary(
        int program, IInstrumentLibrary library, int libraryProgram, InstrumentKeyRange keyRange = default)
    {
        CheckProgram(program);
        CheckProgram(libraryProgram, nameof(libraryProgram));

        if (library == null)
        {
            throw new ArgumentNullException(nameof(library));
        }

        Store(
            program,
            InstrumentSubstitution.ForProgram(
                sampleRate => library.CreateSynthesizer(libraryProgram, sampleRate),
                keyRange,
                () => library.Coverage.KeyRangeOf(libraryProgram)));
    }

    /// <summary>Plays a General MIDI program with one program of another instrument library.</summary>
    /// <param name="program">The General MIDI program to replace.</param>
    /// <param name="library">The library to take the instrument from.</param>
    /// <param name="libraryProgram">The program number to take from it, 0 to 127.</param>
    /// <param name="keyRange">
    /// The notes this instrument answers to; read from that library's coverage when omitted.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="program"/> or <paramref name="libraryProgram"/> is outside 0 to 127.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="library"/> is null.</exception>
    public void SetInstrumentFromLibrary(
        GeneralMidiProgram program,
        IInstrumentLibrary library,
        int libraryProgram,
        InstrumentKeyRange keyRange = default) =>
        SetInstrumentFromLibrary((int)program, library, libraryProgram, keyRange);

    /// <summary>
    /// Plays a General MIDI program with one program of a REGISTERED instrument library, named.
    /// </summary>
    /// <param name="program">The General MIDI program number to replace, 0 to 127.</param>
    /// <param name="libraryName">
    /// The registry name of the library to take the instrument from. It is resolved on first use,
    /// so it may be registered afterwards; a name that is still not registered then gives the
    /// registry's own error.
    /// </param>
    /// <param name="libraryProgram">The program number to take from it, 0 to 127.</param>
    /// <param name="keyRange">
    /// The notes this instrument answers to; read from that library's coverage when omitted.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="program"/> or <paramref name="libraryProgram"/> is outside 0 to 127.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="libraryName"/> is null or blank.</exception>
    public void SetInstrumentFromLibrary(
        int program, string libraryName, int libraryProgram, InstrumentKeyRange keyRange = default)
    {
        CheckProgram(program);
        CheckProgram(libraryProgram, nameof(libraryProgram));

        if (string.IsNullOrWhiteSpace(libraryName))
        {
            throw new ArgumentException("An instrument library name is required.", nameof(libraryName));
        }

        Store(
            program,
            InstrumentSubstitution.ForProgram(
                sampleRate => InstrumentLibraryRegistry.Resolve(libraryName)
                    .CreateSynthesizer(libraryProgram, sampleRate),
                keyRange,
                () => InstrumentLibraryRegistry.Resolve(libraryName).Coverage.KeyRangeOf(libraryProgram)));
    }

    /// <summary>
    /// Plays a General MIDI program with one program of a REGISTERED instrument library, named.
    /// </summary>
    /// <param name="program">The General MIDI program to replace.</param>
    /// <param name="libraryName">The registry name of the library, resolved on first use.</param>
    /// <param name="libraryProgram">The program number to take from it, 0 to 127.</param>
    /// <param name="keyRange">
    /// The notes this instrument answers to; read from that library's coverage when omitted.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="program"/> or <paramref name="libraryProgram"/> is outside 0 to 127.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="libraryName"/> is null or blank.</exception>
    public void SetInstrumentFromLibrary(
        GeneralMidiProgram program,
        string libraryName,
        int libraryProgram,
        InstrumentKeyRange keyRange = default) =>
        SetInstrumentFromLibrary((int)program, libraryName, libraryProgram, keyRange);

    /// <summary>
    /// Puts a General MIDI program back: the base library plays it again, and with no base library
    /// it is no longer covered at all.
    /// </summary>
    /// <param name="program">The General MIDI program number, 0 to 127.</param>
    /// <returns>True when an instrument was set for that program and has now been removed.</returns>
    public bool ClearInstrument(int program)
    {
        lock (gate)
        {
            return substitutions.Remove(program);
        }
    }

    /// <summary>
    /// Puts a General MIDI program back: the base library plays it again, and with no base library
    /// it is no longer covered at all.
    /// </summary>
    /// <param name="program">The General MIDI program.</param>
    /// <returns>True when an instrument was set for that program and has now been removed.</returns>
    public bool ClearInstrument(GeneralMidiProgram program) => ClearInstrument((int)program);

    // ----- setting the percussion kit ----------------------------------------------------------

    /// <summary>Plays the whole percussion kit with a synthesizer of your own, built on demand.</summary>
    /// <param name="synthesizerFactory">
    /// Builds the kit at the sample rate it is handed. It is called once per synthesizer this
    /// library is asked for.
    /// </param>
    /// <param name="percussionNotes">
    /// The note numbers this kit really holds. Leave it out and the General MIDI percussion key
    /// map, 35 to 81, is reported, because nothing else can be known about a kit built in code.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="synthesizerFactory"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A note number is outside 0 to 127.</exception>
    public void SetPercussion(
        Func<int, IMidiSynthesizer> synthesizerFactory, IEnumerable<int> percussionNotes = null)
    {
        if (synthesizerFactory == null)
        {
            throw new ArgumentNullException(nameof(synthesizerFactory));
        }

        StorePercussion(
            InstrumentSubstitution.ForPercussion(
                synthesizerFactory, CheckedNotes(percussionNotes), null));
    }

    /// <summary>Plays the whole percussion kit from an instrument file.</summary>
    /// <param name="instrumentPath">
    /// A <c>.dspreset</c>, <c>.dslibrary</c> or <c>.dsbundle</c> (Decent Sampler) or a Decent
    /// Sampler library folder; a <c>.sfz</c>; or a <c>.sf2</c>, whose drum bank is used. The file
    /// is loaded HERE, once, and shared by every synthesizer built from it.
    /// </param>
    /// <param name="percussionNotes">
    /// The note numbers this kit really holds; read from the instrument itself when omitted.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="instrumentPath"/> is null or blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A note number is outside 0 to 127.</exception>
    /// <exception cref="NotSupportedException">The file is not a kind this library can load.</exception>
    /// <exception cref="FileNotFoundException">There is no file at that path.</exception>
    public void SetPercussion(string instrumentPath, IEnumerable<int> percussionNotes = null)
    {
        var loaded = LoadInstrument(instrumentPath, 0, forPercussion: true);

        StorePercussion(
            InstrumentSubstitution.ForPercussion(
                loaded.Factory, CheckedNotes(percussionNotes), loaded.PercussionNotes));
    }

    /// <summary>Plays the whole percussion kit from the drum bank of a SoundFont file.</summary>
    /// <param name="soundFontPath">
    /// The <c>.sf2</c> file, loaded here once and shared by every synthesizer built from it.
    /// </param>
    /// <param name="percussionNotes">
    /// The note numbers this kit really holds; read from the SoundFont's drum bank when omitted.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="soundFontPath"/> is null or blank.</exception>
    /// <exception cref="ArgumentOutOfRangeException">A note number is outside 0 to 127.</exception>
    public void SetPercussionFromSoundFont(string soundFontPath, IEnumerable<int> percussionNotes = null)
    {
        var soundFont = SoundFontFor(soundFontPath);

        StorePercussion(
            InstrumentSubstitution.ForPercussion(
                sampleRate => soundFont.CreatePercussionSynthesizer(sampleRate),
                CheckedNotes(percussionNotes),
                () => soundFont.Coverage.PercussionNotes.ToArray()));
    }

    /// <summary>Plays the whole percussion kit with another instrument library's kit.</summary>
    /// <param name="library">The library to take the kit from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="library"/> is null.</exception>
    public void SetPercussionFromLibrary(IInstrumentLibrary library)
    {
        if (library == null)
        {
            throw new ArgumentNullException(nameof(library));
        }

        StorePercussion(
            InstrumentSubstitution.ForPercussion(
                sampleRate => library.CreatePercussionSynthesizer(sampleRate),
                null,
                () => library.Coverage.PercussionNotes.ToArray()));
    }

    /// <summary>
    /// Plays the whole percussion kit with a REGISTERED instrument library's kit, named.
    /// </summary>
    /// <param name="libraryName">
    /// The registry name of the library to take the kit from, resolved on first use.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="libraryName"/> is null or blank.</exception>
    public void SetPercussionFromLibrary(string libraryName)
    {
        if (string.IsNullOrWhiteSpace(libraryName))
        {
            throw new ArgumentException("An instrument library name is required.", nameof(libraryName));
        }

        StorePercussion(
            InstrumentSubstitution.ForPercussion(
                sampleRate => InstrumentLibraryRegistry.Resolve(libraryName)
                    .CreatePercussionSynthesizer(sampleRate),
                null,
                () => InstrumentLibraryRegistry.Resolve(libraryName).Coverage.PercussionNotes.ToArray()));
    }

    /// <summary>
    /// Puts the percussion kit back: the base library plays it again, and with no base library
    /// there is no kit at all.
    /// </summary>
    /// <returns>True when a kit was set and has now been removed.</returns>
    public bool ClearPercussion()
    {
        lock (gate)
        {
            var had = percussionSubstitution != null;
            percussionSubstitution = null;
            return had;
        }
    }

    // ----- the two shapes ----------------------------------------------------------------------

    /// <inheritdoc />
    /// <remarks>
    /// The instrument set for this program when there is one, and the base library's own per-part
    /// synthesizer when there is not. Either way it is pinned: program change and bank select
    /// cannot move it, and it sounds on whichever channel the music uses. A substitute is wrapped
    /// to make that true, so the synthesizer handed back is not necessarily the type the factory
    /// built.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// There is no base library and no instrument has been set for this program; or a named base
    /// library is not registered.
    /// </exception>
    public IMidiSynthesizer CreateSynthesizer(int program, int sampleRate)
    {
        CheckProgram(program);
        CheckSampleRate(sampleRate);

        InstrumentSubstitution substitution;

        lock (gate)
        {
            substitutions.TryGetValue(program, out substitution);
        }

        if (substitution != null)
        {
            return substitution.CreateSynthesizer(sampleRate, ProgramLabel(program));
        }

        var library = ResolveBaseLibrary();

        if (library == null)
        {
            throw new InvalidOperationException(NothingToPlayMessage(ProgramLabel(program), "SetInstrument"));
        }

        return library.CreateSynthesizer(program, sampleRate);
    }

    /// <inheritdoc />
    /// <remarks>
    /// The kit set here when there is one, and the base library's own kit when there is not.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// There is no base library and no kit has been set; or a named base library is not registered.
    /// </exception>
    public IMidiSynthesizer CreatePercussionSynthesizer(int sampleRate)
    {
        CheckSampleRate(sampleRate);

        InstrumentSubstitution substitution;

        lock (gate)
        {
            substitution = percussionSubstitution;
        }

        if (substitution != null)
        {
            return substitution.CreateSynthesizer(sampleRate, "the percussion kit");
        }

        var library = ResolveBaseLibrary();

        if (library == null)
        {
            throw new InvalidOperationException(
                NothingToPlayMessage("the percussion kit", "SetPercussion"));
        }

        return library.CreatePercussionSynthesizer(sampleRate);
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// ONE synthesizer that honours the program changes the music carries. A channel whose CURRENT
    /// program has an instrument set here is played by that instrument; every other channel is
    /// played by the base library's own multi-timbral synthesizer; and the percussion channel is
    /// played by the kit set here whenever there is one, whatever program the music selects there.
    /// </para>
    /// <para>
    /// The instruments are a SNAPSHOT taken now. Setting another one afterwards changes what the
    /// next synthesizer plays and leaves this one alone. Children are built on first use - one per
    /// substituted program, shared by every channel playing it - and a channel moving between the
    /// base and a substitute releases its sounding notes cleanly and carries its controllers,
    /// pitch bend and program across with it.
    /// </para>
    /// </remarks>
    /// <exception cref="NotSupportedException">
    /// The base library does not offer the multi-timbral shape.
    /// </exception>
    /// <exception cref="InvalidOperationException">A named base library is not registered.</exception>
    public IMidiSynthesizer CreateMultiTimbralSynthesizer(int sampleRate)
    {
        CheckSampleRate(sampleRate);

        var library = ResolveBaseLibrary();

        if (library != null && !library.SupportsMultiTimbral)
        {
            throw new NotSupportedException(
                $"The instrument library '{name}' plays everything it has not been given an " +
                $"instrument for with '{library.Name}', and that library does not offer the " +
                "multi-timbral shape. Play the parts one at a time with CreateSynthesizer and a " +
                "RoutingSynthesizer instead.");
        }

        Dictionary<int, InstrumentSubstitution> melodic;
        InstrumentSubstitution kit;

        lock (gate)
        {
            melodic = new Dictionary<int, InstrumentSubstitution>(substitutions);
            kit = percussionSubstitution;
        }

        return new MappedMultiTimbralSynthesizer(
            sampleRate, RoutingSynthesizer.DefaultBlockSize, library, melodic, kit);
    }

    /// <summary>Registers this library with <see cref="InstrumentLibraryRegistry"/>.</summary>
    /// <remarks>
    /// A convenience for the one-line form, and safe at any point: instruments may be set, replaced
    /// and cleared before or after this call.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A different library is already registered under this library's name.
    /// </exception>
    public void Register() => InstrumentLibraryRegistry.Register(this);

    // ----- the plumbing ------------------------------------------------------------------------

    private void Store(int program, InstrumentSubstitution substitution)
    {
        lock (gate)
        {
            substitutions[program] = substitution;
        }
    }

    private void StorePercussion(InstrumentSubstitution substitution)
    {
        lock (gate)
        {
            percussionSubstitution = substitution;
        }
    }

    private IInstrumentLibrary ResolveBaseLibrary()
    {
        string nameToResolve;

        lock (gate)
        {
            if (baseLibrary != null)
            {
                return baseLibrary;
            }

            nameToResolve = requestedBaseLibraryName;

            if (nameToResolve == null)
            {
                return null;
            }
        }

        // Resolved OUTSIDE this library's lock: the registry has a lock of its own and there is no
        // reason to hold two. The error it throws when the name is not registered is the registry's
        // own, listing what IS registered, which is the message a developer needs here.
        var resolved = InstrumentLibraryRegistry.Resolve(nameToResolve);

        lock (gate)
        {
            baseLibrary ??= resolved;
            return baseLibrary;
        }
    }

    private LoadedInstrument LoadInstrument(string instrumentPath, int soundFontProgram, bool forPercussion)
    {
        if (string.IsNullOrWhiteSpace(instrumentPath))
        {
            throw new ArgumentException("An instrument file path is required.", nameof(instrumentPath));
        }

        var extension = Path.GetExtension(instrumentPath);

        if (IsDecentSampler(extension) || (extension.Length == 0 && Directory.Exists(instrumentPath)))
        {
            var instrument = DecentSamplerInstruments().Get(instrumentPath);

            return new LoadedInstrument(
                sampleRate => new DecentSamplerSynthesizer(instrument, sampleRate),
                () => KeyRangeOf(instrument),
                () => PercussionNotesOf(instrument));
        }

        if (string.Equals(extension, ".sfz", StringComparison.OrdinalIgnoreCase))
        {
            var instrument = SfzInstruments().Get(instrumentPath);

            return new LoadedInstrument(
                sampleRate => new SfzSynthesizer(instrument, sampleRate),
                () => KeyRangeOf(instrument),
                () => PercussionNotesOf(instrument));
        }

        if (string.Equals(extension, ".sf2", StringComparison.OrdinalIgnoreCase))
        {
            var soundFont = SoundFontFor(instrumentPath);

            return forPercussion
                ? new LoadedInstrument(
                    sampleRate => soundFont.CreatePercussionSynthesizer(sampleRate),
                    () => InstrumentKeyRange.Full,
                    () => soundFont.Coverage.PercussionNotes.ToArray())
                : new LoadedInstrument(
                    sampleRate => soundFont.CreateSynthesizer(soundFontProgram, sampleRate),
                    () => soundFont.Coverage.KeyRangeOf(soundFontProgram),
                    () => soundFont.Coverage.PercussionNotes.ToArray());
        }

        throw new NotSupportedException(
            $"'{extension}' is not an instrument file this library knows how to load. It " +
            "understands .dspreset, .dslibrary and .dsbundle (Decent Sampler), a Decent Sampler " +
            "library folder, .sfz (SFZ) and .sf2 (SoundFont). Anything else is one line of your " +
            "own: SetInstrument(program, sampleRate => new MySynthesizer(sampleRate)).");
    }

    private static bool IsDecentSampler(string extension)
    {
        foreach (var known in DecentSamplerExtensions)
        {
            if (string.Equals(extension, known, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private DecentSamplerInstrumentCache DecentSamplerInstruments()
    {
        lock (gate)
        {
            return decentSamplerInstruments ??= new DecentSamplerInstrumentCache();
        }
    }

    private SfzInstrumentCache SfzInstruments()
    {
        lock (gate)
        {
            return sfzInstruments ??= new SfzInstrumentCache();
        }
    }

    private SoundFontInstrumentLibrary SoundFontFor(string soundFontPath)
    {
        if (string.IsNullOrWhiteSpace(soundFontPath))
        {
            throw new ArgumentException("A SoundFont path is required.", nameof(soundFontPath));
        }

        var key = SafeFullPath(soundFontPath);

        lock (gate)
        {
            soundFonts ??= new Dictionary<string, SoundFontInstrumentLibrary>(StringComparer.OrdinalIgnoreCase);

            if (soundFonts.TryGetValue(key, out var existing))
            {
                return existing;
            }

            // Parsed inside the lock, as the Decent Sampler and SFZ caches do it and for the same
            // reason: a bank is large and slow to read, and racing threads would double the peak
            // memory to no purpose. The library built here is never registered - it exists only to
            // hold the one parsed SoundFont and to answer for its coverage.
            var library = new SoundFontInstrumentLibrary(
                $"{name}:{Path.GetFileName(soundFontPath)}",
                $"The SoundFont '{Path.GetFileName(soundFontPath)}', loaded by the instrument library '{name}'.",
                soundFontPath);

            soundFonts.Add(key, library);
            return library;
        }
    }

    private static string SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }

    private static InstrumentKeyRange KeyRangeOf(DecentSamplerInstrument instrument)
    {
        var range = InstrumentKeyRange.Empty;

        foreach (var zone in instrument.Zones)
        {
            range = range.UnionWith(RangeOf(zone.LoNote, zone.HiNote));
        }

        return range;
    }

    private static InstrumentKeyRange KeyRangeOf(SfzInstrument instrument)
    {
        var range = InstrumentKeyRange.Empty;

        foreach (var region in instrument.Regions)
        {
            range = range.UnionWith(RangeOf(region.LoKey, region.HiKey));
        }

        return range;
    }

    private static int[] PercussionNotesOf(DecentSamplerInstrument instrument)
    {
        var notes = new HashSet<int>();

        foreach (var zone in instrument.Zones)
        {
            AddNotes(notes, zone.LoNote, zone.HiNote);
        }

        return notes.OrderBy(note => note).ToArray();
    }

    private static int[] PercussionNotesOf(SfzInstrument instrument)
    {
        var notes = new HashSet<int>();

        foreach (var region in instrument.Regions)
        {
            AddNotes(notes, region.LoKey, region.HiKey);
        }

        return notes.OrderBy(note => note).ToArray();
    }

    private static void AddNotes(HashSet<int> notes, int lowest, int highest)
    {
        var range = RangeOf(lowest, highest);

        if (range.IsEmpty)
        {
            return;
        }

        for (var note = range.LowestKey; note <= range.HighestKey; note++)
        {
            notes.Add(note);
        }
    }

    private static InstrumentKeyRange RangeOf(int lowest, int highest)
    {
        lowest = Math.Max(0, lowest);
        highest = Math.Min(127, highest);

        return highest < lowest ? InstrumentKeyRange.Empty : new InstrumentKeyRange(lowest, highest);
    }

    private static int[] CheckedNotes(IEnumerable<int> percussionNotes)
    {
        if (percussionNotes == null)
        {
            return null;
        }

        var notes = percussionNotes.ToArray();

        foreach (var note in notes)
        {
            if (note < 0 || note > 127)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(percussionNotes), note, "A MIDI note number is 0 to 127.");
            }
        }

        return notes;
    }

    private static void CheckProgram(int program, string parameterName = "program")
    {
        if (program < 0 || program >= GeneralMidi.ProgramCount)
        {
            throw new ArgumentOutOfRangeException(
                parameterName, program, "A General MIDI program number is 0 to 127.");
        }
    }

    private static void CheckSampleRate(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate), sampleRate, "The sample rate must be positive.");
        }
    }

    private static string ProgramLabel(int program) =>
        $"General MIDI program {program} ({GeneralMidi.DisplayName((GeneralMidiProgram)program)})";

    private string NothingToPlayMessage(string voice, string setter) =>
        $"The instrument library '{name}' has no base library, and no instrument has been set for " +
        $"{voice}, so there is nothing to play it. Set one with {setter}, or build this library " +
        "over a base library that plays the voices you do not set yourself.";

    // What a file turned into: how to build a synthesizer over it, and what it says about the
    // notes it answers to. The coverage is worked out on demand rather than now, because a
    // substitution that names a library may be set before that library is registered.
    private sealed class LoadedInstrument
    {
        internal LoadedInstrument(
            Func<int, IMidiSynthesizer> factory,
            Func<InstrumentKeyRange> keyRange,
            Func<int[]> percussionNotes)
        {
            Factory = factory;
            KeyRange = keyRange;
            PercussionNotes = percussionNotes;
        }

        internal Func<int, IMidiSynthesizer> Factory { get; }

        internal Func<InstrumentKeyRange> KeyRange { get; }

        internal Func<int[]> PercussionNotes { get; }
    }
}
