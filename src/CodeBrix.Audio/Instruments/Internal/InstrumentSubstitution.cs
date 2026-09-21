using System;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Instruments.Internal;

/// <summary>
/// One voice a <see cref="MappedInstrumentLibrary"/> plays with an instrument of the developer's
/// own: how to build it, and which notes it answers to.
/// </summary>
/// <remarks>
/// It is immutable, so a synthesizer built from it keeps what it was built with even after the
/// library is changed again - which is what lets voices be swapped one at a time while something
/// is already playing. The note coverage may be worked out LAZILY, because a substitution that
/// names a library the developer has not registered yet cannot be asked what it covers until that
/// library exists.
/// </remarks>
internal sealed class InstrumentSubstitution
{
    private readonly InstrumentKeyRange declaredKeyRange;
    private readonly Func<InstrumentKeyRange> keyRangeSource;
    private readonly int[] declaredPercussionNotes;
    private readonly Func<int[]> percussionNotesSource;

    private InstrumentSubstitution(
        Func<int, IMidiSynthesizer> synthesizerFactory,
        InstrumentKeyRange declaredKeyRange,
        Func<InstrumentKeyRange> keyRangeSource,
        int[] declaredPercussionNotes,
        Func<int[]> percussionNotesSource)
    {
        SynthesizerFactory = synthesizerFactory;
        this.declaredKeyRange = declaredKeyRange;
        this.keyRangeSource = keyRangeSource;
        this.declaredPercussionNotes = declaredPercussionNotes;
        this.percussionNotesSource = percussionNotesSource;
    }

    /// <summary>Builds the substitute instrument at a sample rate.</summary>
    internal Func<int, IMidiSynthesizer> SynthesizerFactory { get; }

    /// <summary>Records a melodic instrument standing in for one General MIDI program.</summary>
    /// <param name="synthesizerFactory">Builds the instrument at a sample rate.</param>
    /// <param name="declaredKeyRange">
    /// The notes the caller says it answers to, or <see cref="InstrumentKeyRange.Empty"/> to work
    /// it out.
    /// </param>
    /// <param name="keyRangeSource">
    /// Where to read the range from when the caller did not declare one, or null for the whole
    /// keyboard.
    /// </param>
    /// <returns>The substitution.</returns>
    internal static InstrumentSubstitution ForProgram(
        Func<int, IMidiSynthesizer> synthesizerFactory,
        InstrumentKeyRange declaredKeyRange,
        Func<InstrumentKeyRange> keyRangeSource) =>
        new InstrumentSubstitution(synthesizerFactory, declaredKeyRange, keyRangeSource, null, null);

    /// <summary>Records a kit standing in for the whole percussion channel.</summary>
    /// <param name="synthesizerFactory">Builds the kit at a sample rate.</param>
    /// <param name="declaredPercussionNotes">
    /// The notes the caller says the kit holds, or null to work it out.
    /// </param>
    /// <param name="percussionNotesSource">
    /// Where to read the notes from when the caller did not declare them, or null for the General
    /// MIDI percussion key map.
    /// </param>
    /// <returns>The substitution.</returns>
    internal static InstrumentSubstitution ForPercussion(
        Func<int, IMidiSynthesizer> synthesizerFactory,
        int[] declaredPercussionNotes,
        Func<int[]> percussionNotesSource) =>
        new InstrumentSubstitution(
            synthesizerFactory, InstrumentKeyRange.Empty, null,
            declaredPercussionNotes, percussionNotesSource);

    /// <summary>The notes this instrument answers to.</summary>
    /// <returns>
    /// The range the caller declared; failing that the range the instrument itself reports; failing
    /// that the whole keyboard, because an instrument that cannot say is assumed to play anything.
    /// </returns>
    internal InstrumentKeyRange ResolveKeyRange()
    {
        if (!declaredKeyRange.IsEmpty)
        {
            return declaredKeyRange;
        }

        return keyRangeSource == null ? InstrumentKeyRange.Full : keyRangeSource();
    }

    /// <summary>The percussion notes this kit holds.</summary>
    /// <returns>
    /// The notes the caller declared; failing that the notes the instrument itself reports; failing
    /// that the General MIDI percussion key map, 35 to 81, because nothing else can be known about
    /// a kit built by a factory.
    /// </returns>
    internal int[] ResolvePercussionNotes()
    {
        if (declaredPercussionNotes != null)
        {
            return declaredPercussionNotes;
        }

        return percussionNotesSource == null ? GeneralMidiPercussionNotes() : percussionNotesSource();
    }

    /// <summary>
    /// Builds the instrument and holds it to itself, checking that it came back at the rate that
    /// was asked for.
    /// </summary>
    /// <param name="sampleRate">The sample rate to build at, in Hz.</param>
    /// <param name="voice">What this substitution stands for, for the error message.</param>
    /// <returns>The pinned substitute synthesizer.</returns>
    /// <exception cref="InvalidOperationException">
    /// The factory returned null, or returned a synthesizer at another sample rate.
    /// </exception>
    internal IMidiSynthesizer CreateSynthesizer(int sampleRate, string voice)
    {
        var synthesizer = SynthesizerFactory(sampleRate);

        if (synthesizer == null)
        {
            throw new InvalidOperationException(
                $"The instrument set for {voice} returned null instead of a synthesizer. A " +
                "substitute instrument's factory must build one every time it is called.");
        }

        if (synthesizer.SampleRate != sampleRate)
        {
            throw new InvalidOperationException(
                $"The instrument set for {voice} was asked for {sampleRate} Hz and built a " +
                $"synthesizer that renders at {synthesizer.SampleRate} Hz. A substitute " +
                "instrument's factory must honour the sample rate it is given.");
        }

        return PinnedInstrumentSynthesizer.Pin(synthesizer);
    }

    private static int[] GeneralMidiPercussionNotes()
    {
        var count = GeneralMidi.HighestPercussionNote - GeneralMidi.LowestPercussionNote + 1;
        var notes = new int[count];

        for (var index = 0; index < count; index++)
        {
            notes[index] = GeneralMidi.LowestPercussionNote + index;
        }

        return notes;
    }
}
