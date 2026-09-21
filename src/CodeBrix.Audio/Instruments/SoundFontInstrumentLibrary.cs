using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Instruments.Internal;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Instruments;

/// <summary>
/// An <see cref="IInstrumentLibrary"/> over a SoundFont file the consumer supplies - the "bring
/// your own <c>.sf2</c>" library, and the one a sample package wraps.
/// </summary>
/// <remarks>
/// <para>
/// This is CODE, not instruments: CodeBrix.Audio ships no recorded samples and this class plays a
/// file that is not in the package. Register an instance to make a SoundFont the application's
/// instrument library:
/// </para>
/// <code>
/// var library = new SoundFontInstrumentLibrary("MyBank", "A General MIDI bank", "bank.sf2");
/// InstrumentLibraryRegistry.Register(library);
/// </code>
/// <para>
/// ONE SOUNDFONT, SHARED. The file is parsed once, by this object, and EVERY synthesizer it hands
/// back renders from that one <see cref="Synth.SoundFont"/>. Twelve parts of an arrangement
/// therefore cost twelve voice engines and ONE copy of the sample data, which is the difference
/// between a 140 MB bank being usable and being impossible.
/// </para>
/// <para>
/// BOTH SHAPES ARE OFFERED. The per-part synthesizers are pinned to the program they were created
/// for, so a program change in the music cannot re-voice a part the caller voiced deliberately;
/// the multi-timbral synthesizer is an ordinary <see cref="SoundFontSynthesizer"/> and honours
/// program changes inline. One sharp edge comes from the SoundFont contract itself: MIDI channel
/// 10 is hard-wired to the drum bank, so a melodic part routed onto that channel sounds as
/// percussion whatever program it was created with.
/// </para>
/// <para>
/// COVERAGE IS READ FROM THE FILE. The programs reported are the patch numbers of the presets in
/// bank 0, each with the note range its regions actually answer to; the percussion notes reported
/// are the notes the bank 128 presets answer to. A SoundFont with no drum bank therefore reports
/// no percussion notes, which is the honest answer rather than silence at play time.
/// </para>
/// </remarks>
public sealed class SoundFontInstrumentLibrary : IInstrumentLibrary
{
    private const int MelodicBank = 0;
    private const int DrumBank = 128;

    private readonly string name;
    private readonly string description;
    private readonly SoundFont soundFont;
    private readonly InstrumentCoverage coverage;

    /// <summary>Creates a library over a SoundFont file on disk.</summary>
    /// <param name="name">How a consumer asks for this library; unique in the registry.</param>
    /// <param name="description">What this library sounds like, in a sentence.</param>
    /// <param name="soundFontPath">Path of the <c>.sf2</c> file to play.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> or <paramref name="soundFontPath"/> is null or blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> is null.</exception>
    public SoundFontInstrumentLibrary(string name, string description, string soundFontPath)
        : this(name, description, LoadFromPath(soundFontPath))
    {
    }

    /// <summary>Creates a library over a SoundFont read from a stream.</summary>
    /// <param name="name">How a consumer asks for this library; unique in the registry.</param>
    /// <param name="description">What this library sounds like, in a sentence.</param>
    /// <param name="soundFontStream">
    /// A stream positioned at the start of a <c>.sf2</c> file. It is read in full by this
    /// constructor and not held afterwards, so the caller may close it on return.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or blank.</exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="description"/> or <paramref name="soundFontStream"/> is null.
    /// </exception>
    public SoundFontInstrumentLibrary(string name, string description, Stream soundFontStream)
        : this(name, description, LoadFromStream(soundFontStream))
    {
    }

    /// <summary>Creates a library over a SoundFont that is already loaded.</summary>
    /// <param name="name">How a consumer asks for this library; unique in the registry.</param>
    /// <param name="description">What this library sounds like, in a sentence.</param>
    /// <param name="soundFont">The SoundFont to play. It is shared, never copied.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or blank.</exception>
    /// <exception cref="ArgumentNullException">
    /// <paramref name="description"/> or <paramref name="soundFont"/> is null.
    /// </exception>
    public SoundFontInstrumentLibrary(string name, string description, SoundFont soundFont)
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

        if (soundFont == null)
        {
            throw new ArgumentNullException(nameof(soundFont));
        }

        this.name = name;
        this.description = description;
        this.soundFont = soundFont;

        coverage = BuildCoverage(soundFont);
    }

    /// <inheritdoc />
    public string Name => name;

    /// <inheritdoc />
    public string Description => description;

    /// <inheritdoc />
    public InstrumentCoverage Coverage => coverage;

    /// <inheritdoc />
    public bool SupportsPerPart => true;

    /// <inheritdoc />
    public bool SupportsMultiTimbral => true;

    /// <summary>
    /// The one loaded SoundFont every synthesizer this library creates renders from.
    /// </summary>
    public SoundFont SoundFont => soundFont;

    /// <inheritdoc />
    public IMidiSynthesizer CreateSynthesizer(int program, int sampleRate)
    {
        if (program < 0 || program >= GeneralMidi.ProgramCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(program), program, "A General MIDI program number is 0 to 127.");
        }

        CheckSampleRate(sampleRate);

        return new ProgramPinnedSynthesizer(
            new SoundFontSynthesizer(soundFont, sampleRate), MelodicBank, program);
    }

    /// <inheritdoc />
    public IMidiSynthesizer CreatePercussionSynthesizer(int sampleRate)
    {
        CheckSampleRate(sampleRate);

        // Bank 128 is the drum bank, and pinning it on every channel is what lets the kit sound
        // wherever a router puts it rather than only on MIDI channel 10.
        return new ProgramPinnedSynthesizer(
            new SoundFontSynthesizer(soundFont, sampleRate), DrumBank, 0);
    }

    /// <inheritdoc />
    public IMidiSynthesizer CreateMultiTimbralSynthesizer(int sampleRate)
    {
        CheckSampleRate(sampleRate);

        return new SoundFontSynthesizer(soundFont, sampleRate);
    }

    /// <summary>Registers this library with <see cref="InstrumentLibraryRegistry"/>.</summary>
    /// <remarks>
    /// A convenience for the one-line form. Calling it more than once on the same instance is a
    /// no-op, as registration always is.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// A different library is already registered under this library's name.
    /// </exception>
    public void Register() => InstrumentLibraryRegistry.Register(this);

    private static void CheckSampleRate(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate), sampleRate, "The sample rate must be positive.");
        }
    }

    private static SoundFont LoadFromPath(string soundFontPath)
    {
        if (string.IsNullOrWhiteSpace(soundFontPath))
        {
            throw new ArgumentException("A SoundFont path is required.", nameof(soundFontPath));
        }

        return new SoundFont(soundFontPath);
    }

    private static SoundFont LoadFromStream(Stream soundFontStream)
    {
        if (soundFontStream == null)
        {
            throw new ArgumentNullException(nameof(soundFontStream));
        }

        return new SoundFont(soundFontStream);
    }

    // The coverage is worked out the same way the voice engine picks a region to play: a note
    // sounds when a preset region contains it AND one of that region's instrument regions does
    // too. Reading only the preset regions would over-report, because a preset region left at the
    // default 0-127 usually narrows to a handful of samples underneath.
    private static InstrumentCoverage BuildCoverage(SoundFont soundFont)
    {
        var programs = new Dictionary<int, InstrumentKeyRange>();
        var percussionNotes = new HashSet<int>();

        foreach (var preset in soundFont.Presets)
        {
            var isDrumBank = preset.BankNumber >= DrumBank;

            if (!isDrumBank && preset.BankNumber != MelodicBank)
            {
                // A variation bank: reachable only by an explicit bank select, which is not part
                // of the General MIDI surface this interface exposes.
                continue;
            }

            if (!isDrumBank && (preset.PatchNumber < 0 || preset.PatchNumber >= GeneralMidi.ProgramCount))
            {
                continue;
            }

            var range = PlayableRange(preset);

            if (range.IsEmpty)
            {
                continue;
            }

            if (isDrumBank)
            {
                for (var note = range.LowestKey; note <= range.HighestKey; note++)
                {
                    percussionNotes.Add(note);
                }
            }
            else
            {
                programs[preset.PatchNumber] = programs.TryGetValue(preset.PatchNumber, out var existing)
                    ? existing.UnionWith(range)
                    : range;
            }
        }

        return new InstrumentCoverage(programs, percussionNotes);
    }

    private static InstrumentKeyRange PlayableRange(Preset preset)
    {
        var range = InstrumentKeyRange.Empty;

        foreach (var presetRegion in preset.Regions)
        {
            foreach (var instrumentRegion in presetRegion.SoundFontInstrument.Regions)
            {
                var lowest = Math.Max(presetRegion.KeyRangeStart, instrumentRegion.KeyRangeStart);
                var highest = Math.Min(presetRegion.KeyRangeEnd, instrumentRegion.KeyRangeEnd);

                lowest = Math.Max(lowest, 0);
                highest = Math.Min(highest, 127);

                if (highest < lowest)
                {
                    continue;
                }

                range = range.UnionWith(new InstrumentKeyRange(lowest, highest));
            }
        }

        return range;
    }
}
