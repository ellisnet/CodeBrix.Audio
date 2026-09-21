using System;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.ModestSynth.Internal.Gm;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// What makes 052 Choir Aahs and 053 Voice Oohs sound like people singing rather than like an organ
/// with a vowel painted on it: resonances that STAY WHERE THEY ARE while the note moves, a pitch
/// that never quite settles, air at the onset, and a section of singers who are not copies of one
/// another.
/// </summary>
/// <remarks>
/// <para>
/// NOTHING HERE IS PINNED AGAINST A COMMITTED NUMBER THAT CAME OFF ANOTHER MACHINE. Every
/// comparison is either two renders made here in this run, or a measurement against a frequency
/// written in the bank - see MAINTAINER-README.txt, "PINNED RENDERS AND THE PLATFORM MATHS
/// LIBRARY".
/// </para>
/// <para>
/// The readings of the choir the bank does not carry are reached through
/// <c>GeneralMidiSynthesizer</c>'s internal <c>ChoirVoicing</c>, which belongs to the INSTANCE. The
/// only process-wide state these tests reach is the bank's build-once voicing cache, which nothing
/// ever mutates; the class shares the library collection anyway, so a run cannot interleave it with
/// the tests that do hold shared state.
/// </para>
/// </remarks>
[Collection(GeneralMidiLibraryCollection.Name)]
public class GeneralMidiChoirVoiceTests
{
    private const int ChoirAahs = (int)GeneralMidiProgram.ChoirAahs;
    private const int VoiceOohs = (int)GeneralMidiProgram.VoiceOohs;

    // Middle C, and two octaves around it, is the range a choir part lives in.
    private const int LowKey = 48;
    private const int HighKey = 72;

    // How long a note is held before its spectrum is taken, and how much of it is analysed.
    private const double Hold = 2.2;

    private static readonly double[] SopranoFormants = [820.0, 1260.0, 2900.0];
    private static readonly double[] BassFormants = [620.0, 1060.0, 2450.0];

    [Theory]
    [InlineData(48)]
    [InlineData(52)]
    [InlineData(56)]
    [InlineData(60)]
    [InlineData(64)]
    [InlineData(68)]
    [InlineData(72)]
    public void the_vowel_formants_stay_where_they_are_as_the_pitch_moves(int key)
    {
        //Arrange
        // The vowel's three resonances for this key, interpolated between the bass's and the
        // soprano's exactly as the bank interpolates them - written out here rather than read from
        // the bank, so the test says what it expects rather than agreeing with itself.
        double register =
            (key - GmChoirSpec.LowRegisterKey) /
            (GmChoirSpec.HighRegisterKey - GmChoirSpec.LowRegisterKey);

        double fundamental = 440.0 * Math.Pow(2.0, (key - 69) / 12.0);

        //Act
        // ONE singer on the fully open "ah", with no unison, no filter and no room around it, so
        // what is measured is the vowel rather than whichever vowel a row happens to ask for.
        double[] magnitudes = OneSingerSpectrum(key);

        //Assert
        for (int formant = 0; formant < SopranoFormants.Length; formant++)
        {
            double expected = BassFormants[formant] +
                (register * (SopranoFormants[formant] - BassFormants[formant]));

            // A HARMONIC SPECTRUM CANNOT SHOW A PEAK BETWEEN ITS OWN HARMONICS. The loudest thing
            // near a formant is the harmonic nearest to it, so the tolerance is half the gap
            // between harmonics - or ninety hertz for a low note, where that gap is small and the
            // analysis bin is what limits the answer.
            double tolerance = Math.Max(90.0, fundamental * 0.6);

            double measured = PeakFrequency(magnitudes, expected - tolerance, expected + tolerance);

            Math.Abs(measured - expected).Should().BeLessThan(
                tolerance,
                "formant {0} of the open vowel at key {1} should sound near {2:F0} Hz and it peaked at {3:F0}",
                formant + 1, key, expected, measured);
        }
    }

    [Fact]
    public void the_choir_does_not_slide_its_spectrum_up_with_the_note_the_way_an_organ_does()
    {
        //Arrange
        // An organ pipe's spectrum is a set of HARMONIC levels, so playing an octave higher moves
        // every peak up an octave with it. A voice's does not move at all.
        double lowest = double.MaxValue;
        double highest = 0.0;

        //Act
        for (int key = LowKey; key <= HighKey; key += 4)
        {
            double peak = PeakFrequency(SustainedSpectrum(ChoirAahs, key), 380.0, 1400.0);

            if (peak < lowest) { lowest = peak; }
            if (peak > highest) { highest = peak; }
        }

        //Assert
        // The note itself moves by a factor of four over these two octaves. The strongest thing in
        // the first formant's region must move by very much less than that; an organ would move it
        // by exactly four.
        (highest / lowest).Should().BeLessThan(
            2.0,
            "the first formant sat between {0:F0} Hz and {1:F0} Hz while the note moved by a factor of four",
            lowest, highest);
    }

    [Fact]
    public void the_first_formant_holds_more_of_the_sound_than_the_fundamental_does_in_the_bass()
    {
        //Arrange
        // The organ complaint in one measurement: the OLD voicing put nearly all of a low note's
        // energy on the fundamental and left the vowel region empty.
        const int key = 45;         // A2, a bass's comfortable middle

        //Act
        double[] magnitudes = SustainedSpectrum(ChoirAahs, key);

        double fundamental = PeakMagnitude(magnitudes, 95.0, 125.0);
        double vowel = PeakMagnitude(magnitudes, 500.0, 900.0);

        //Assert
        vowel.Should().BeGreaterThan(
            fundamental,
            "a bass singing \"ah\" is heard through the vowel rather than through the pipe tone under it");
    }

    [Fact]
    public void one_singer_never_strays_further_from_the_note_than_the_voicing_allows()
    {
        //Arrange
        GmChoirSpec spec = GmChoirSpec.For(GmTone.ChoirVowel, 1.0, 1.0);
        float[] buffer = new float[GmChoirOscillator.ControlSamples];

        //Act & Assert
        for (uint seed = 1u; seed <= 12u; seed++)
        {
            GmChoirOscillator singer = new GmChoirOscillator(GmProbe.SampleRate, spec, seed * 7919u);
            singer.SetFrequency(261.626);
            singer.Reset(0.0);

            double widest = 0.0;

            // Twelve seconds, which is long past the vibrato's onset and several turns of the
            // slowest component of the wander.
            int chunks = 12 * GmProbe.SampleRate / GmChoirOscillator.ControlSamples;

            for (int chunk = 0; chunk < chunks; chunk++)
            {
                singer.Render(buffer);

                double detune = Math.Abs(singer.DetuneCents);
                if (detune > widest) { widest = detune; }
            }

            widest.Should().BeLessThanOrEqualTo(
                spec.WidestDetuneCents,
                "singer {0} wandered {1:F1} cents from the note", seed, widest);

            // And it really does wander: a singer that never leaves the note is the organ again.
            widest.Should().BeGreaterThan(spec.DriftCents * 0.5);
        }
    }

    [Fact]
    public void no_two_singers_wander_the_same_way()
    {
        //Arrange
        GmChoirSpec spec = GmChoirSpec.For(GmTone.ChoirVowel, 1.0, 1.0);

        //Act
        double[] first = DetuneTrace(spec, 0x51DE0001u);
        double[] second = DetuneTrace(spec, 0x51DE0002u);

        double widest = 0.0;

        for (int i = 0; i < first.Length; i++)
        {
            double apart = Math.Abs(first[i] - second[i]);
            if (apart > widest) { widest = apart; }
        }

        //Assert
        widest.Should().BeGreaterThan(
            spec.DriftCents,
            "two singers drawn from different seeds must not hold the note the same way");
    }

    [Fact]
    public void two_voices_on_the_same_note_are_not_one_voice_twice()
    {
        //Arrange
        GeneralMidiSynthesizer pair = GmProbe.BuildForProgram(
            ChoirAahs, settings => settings.EnableReverbAndChorus = false);

        GeneralMidiSynthesizer single = GmProbe.BuildForProgram(
            ChoirAahs, settings => settings.EnableReverbAndChorus = false);

        //Act
        pair.NoteOn(1, 60, 100);
        pair.NoteOn(2, 60, 100);
        var together = GmProbe.Render(pair, 1.5);

        single.NoteOn(1, 60, 100);
        var alone = GmProbe.Render(single, 1.5);

        //Assert
        // Two identical copies would sum to exactly twice one of them. People do not.
        int offset = GmProbe.SampleRate;
        int window = GmProbe.SampleRate / 2;

        double doubled = 0.0;
        double difference = 0.0;

        for (int i = offset; i < offset + window; i++)
        {
            double expected = 2.0 * alone.Left[i];
            double actual = together.Left[i];

            doubled += expected * expected;
            difference += (actual - expected) * (actual - expected);
        }

        Math.Sqrt(difference / doubled).Should().BeGreaterThan(
            0.25, "two singers on one note must not be one singer at twice the level");
    }

    [Fact]
    public void the_onset_carries_more_breath_than_the_sustain()
    {
        //Arrange
        // The same voicing twice, once with the air taken out of it. Everything else - the source,
        // the resonances, the seeds - is identical, and the oscillator is linear in the air, so the
        // DIFFERENCE between the two renders is the breath and nothing else.
        GmVoiceSpec breathing = Singer(breath: true);
        GmVoiceSpec dry = Singer(breath: false);

        //Act
        var withAir = GmVoiceProbe.Render(breathing, 60, 100, 1.4, 0.2);
        var withoutAir = GmVoiceProbe.Render(dry, 60, 100, 1.4, 0.2);

        int window = GmVoiceProbe.SampleRate / 10;

        double atTheOnset = BreathLevel(withAir.Left, withoutAir.Left, 0, window);
        double inTheSustain = BreathLevel(
            withAir.Left, withoutAir.Left, GmVoiceProbe.SampleRate, window);

        //Assert
        inTheSustain.Should().BeGreaterThan(0.0, "a held note keeps a little air in it");

        atTheOnset.Should().BeGreaterThan(
            inTheSustain * 2.0,
            "the note should be taken with audible breath ({0:F5}) and hold with much less ({1:F5})",
            atTheOnset, inTheSustain);
    }

    [Theory]
    [InlineData(ChoirAahs)]
    [InlineData(VoiceOohs)]
    public void the_same_voice_renders_the_same_samples_twice_in_one_run(int program)
    {
        //Arrange
        GeneralMidiSynthesizer first = GmProbe.BuildForProgram(program);
        GeneralMidiSynthesizer second = GmProbe.BuildForProgram(program);

        //Act
        var one = GmProbe.PlayNote(first, 1, 57, 96, 1.2, 0.4);
        var two = GmProbe.PlayNote(second, 1, 57, 96, 1.2, 0.4);

        //Assert
        GmProbe.LargestDifference(one.Left, two.Left).Should().Be(0.0);
        GmProbe.LargestDifference(one.Right, two.Right).Should().Be(0.0);
    }

    // The reading arrives as its number rather than as itself, because GmChoirVoicing is internal
    // and a public test method cannot take it.
    [Theory]
    [InlineData(ChoirAahs, (int)GmChoirVoicing.Massed)]
    [InlineData(ChoirAahs, (int)GmChoirVoicing.Section)]
    [InlineData(VoiceOohs, (int)GmChoirVoicing.Massed)]
    [InlineData(VoiceOohs, (int)GmChoirVoicing.Section)]
    public void every_reading_of_the_choir_stands_at_the_same_level(int program, int reading)
    {
        //Arrange
        GmChoirVoicing voicing = (GmChoirVoicing)reading;

        GeneralMidiSynthesizer bank = GmProbe.BuildForProgram(program);
        GeneralMidiSynthesizer other = GmProbe.BuildForProgram(program);

        //Act
        other.ChoirVoicing = voicing;

        var asShipped = GmProbe.PlayNote(bank, 1, 60, 100, 1.4, 0.6);
        var alternative = GmProbe.PlayNote(other, 1, 60, 100, 1.4, 0.6);

        //Assert
        GmProbe.LargestDifference(asShipped.Left, alternative.Left).Should().BeGreaterThan(
            0.01, "{0} must be a different reading, not the same one under another name", voicing);

        double shipped = GmProbe.LoudestWindow(asShipped);
        double heard = GmProbe.LoudestWindow(alternative);

        Math.Abs(20.0 * Math.Log10(heard / shipped)).Should().BeLessThan(
            1.5,
            "the readings must be a matter of taste rather than of loudness: program {0} in {1} " +
            "came out at {2:F5} against the bank's {3:F5}", program, voicing, heard, shipped);
    }

    [Fact]
    public void the_close_reading_sings_a_more_open_vowel_than_the_one_the_bank_carries()
    {
        //Arrange
        //Act
        double close = PeakFrequency(VowelSpectrum(GmChoirVoicing.Section, 60), 380.0, 1000.0);
        double bank = PeakFrequency(VowelSpectrum(GmChoirRows.BankVoicing, 60), 380.0, 1000.0);

        //Assert
        // A more open vowel is a HIGHER first formant, and that is the difference between the two
        // readings that a measurement can see plainly. A spectral centroid cannot: the bank's
        // reading carries far more breath, and breath is broadband, so the two effects cancel in
        // the centroid and it reads the wrong way round by a few hertz.
        close.Should().BeGreaterThan(
            bank * 1.2,
            "the close reading's vowel ({0:F0} Hz) should sit well above the bank's rounder one " +
            "({1:F0} Hz)", close, bank);
    }

    [Fact]
    public void a_reading_belongs_to_one_synthesizer_and_not_to_the_process()
    {
        //Arrange
        GeneralMidiSynthesizer close = GmProbe.BuildForProgram(ChoirAahs);
        GeneralMidiSynthesizer untouched = GmProbe.BuildForProgram(ChoirAahs);
        GeneralMidiSynthesizer reference = GmProbe.BuildForProgram(ChoirAahs);

        //Act
        close.ChoirVoicing = GmChoirVoicing.Section;

        var alternative = GmProbe.PlayNote(close, 1, 60, 100, 0.8, 0.3);
        var neighbour = GmProbe.PlayNote(untouched, 1, 60, 100, 0.8, 0.3);
        var expected = GmProbe.PlayNote(reference, 1, 60, 100, 0.8, 0.3);

        //Assert
        GmProbe.LargestDifference(neighbour.Left, expected.Left).Should().Be(0.0);
        GmProbe.LargestDifference(alternative.Left, expected.Left).Should().BeGreaterThan(0.01);
    }

    [Fact]
    public void the_bank_carries_the_reading_the_rows_say_it_does()
    {
        //Arrange
        GeneralMidiSynthesizer plain = GmProbe.BuildForProgram(ChoirAahs);
        GeneralMidiSynthesizer named = GmProbe.BuildForProgram(ChoirAahs);

        //Act
        named.ChoirVoicing = GmChoirRows.BankVoicing;

        var untouched = GmProbe.PlayNote(plain, 1, 60, 100, 0.8, 0.3);
        var asked = GmProbe.PlayNote(named, 1, 60, 100, 0.8, 0.3);

        //Assert
        GmProbe.LargestDifference(untouched.Left, asked.Left).Should().Be(0.0);
    }

    [Theory]
    [InlineData(ChoirAahs)]
    [InlineData(VoiceOohs)]
    public void asking_for_a_full_ensemble_doubles_the_singers_on_the_note(int program)
    {
        //Arrange
        GeneralMidiSynthesizer standard = GmProbe.BuildForProgram(program);
        GeneralMidiSynthesizer full = GmProbe.BuildForProgram(program);

        //Act
        full.Adjustments.Program(program).Ensemble = GeneralMidiEnsemble.Full;

        int standardSingers = GmBank.Program(program, GmChoirRows.BankVoicing).OscillatorCount;
        int fullSingers = GmBank.Program(program, GmChoirRows.LargerSection).OscillatorCount;

        var plainly = GmProbe.PlayNote(standard, 1, 60, 100, 1.2, 0.5);
        var fully = GmProbe.PlayNote(full, 1, 60, 100, 1.2, 0.5);

        //Assert
        standardSingers.Should().Be(3, "the bank's reading is a section of three");
        fullSingers.Should().Be(6, "the larger section is twice that");

        GmProbe.LargestDifference(plainly.Left, fully.Left).Should().BeGreaterThan(
            0.01, "asking for the larger section must actually change the sound");
    }

    [Theory]
    [InlineData(ChoirAahs)]
    [InlineData(VoiceOohs)]
    public void a_full_ensemble_stands_at_the_same_level_as_the_standard_one(int program)
    {
        //Arrange
        GeneralMidiSynthesizer standard = GmProbe.BuildForProgram(program);
        GeneralMidiSynthesizer full = GmProbe.BuildForProgram(program);

        //Act
        full.Adjustments.Program(program).Ensemble = GeneralMidiEnsemble.Full;

        double plainly = GmProbe.LoudestWindow(GmProbe.PlayNote(standard, 1, 60, 100, 1.2, 0.8));
        double fully = GmProbe.LoudestWindow(GmProbe.PlayNote(full, 1, 60, 100, 1.2, 0.8));

        //Assert
        Math.Abs(20.0 * Math.Log10(fully / plainly)).Should().BeLessThan(
            1.5,
            "a fuller section must not be a louder one: {0:F5} against {1:F5}", fully, plainly);
    }

    [Theory]
    [InlineData((int)GeneralMidiProgram.Celesta)]
    [InlineData((int)GeneralMidiProgram.StringEnsemble1)]
    [InlineData((int)GeneralMidiProgram.AcousticGrandPiano)]
    [InlineData((int)GeneralMidiProgram.Pad4Choir)]
    public void a_full_ensemble_changes_nothing_on_a_program_that_has_no_larger_section(int program)
    {
        //Arrange
        GeneralMidiSynthesizer standard = GmProbe.BuildForProgram(program);
        GeneralMidiSynthesizer full = GmProbe.BuildForProgram(program);

        //Act
        full.Adjustments.Program(program).Ensemble = GeneralMidiEnsemble.Full;

        var plainly = GmProbe.PlayNote(standard, 1, 60, 100, 1.0, 0.5);
        var fully = GmProbe.PlayNote(full, 1, 60, 100, 1.0, 0.5);

        //Assert
        // Sample for sample, in one run on one machine - not a digest and not a committed sample.
        GmProbe.LargestDifference(plainly.Left, fully.Left).Should().Be(
            0.0, "program {0} has only one section, so asking for a fuller one must do nothing",
            program);

        GmProbe.LargestDifference(plainly.Right, fully.Right).Should().Be(0.0);
    }

    [Fact]
    public void a_full_ensemble_reaches_the_next_note_and_not_the_one_already_sounding()
    {
        //Arrange
        GeneralMidiSynthesizer changing = GmProbe.BuildForProgram(ChoirAahs);
        GeneralMidiSynthesizer untouched = GmProbe.BuildForProgram(ChoirAahs);

        //Act
        // A note is started on both, then the knob is turned on one of them WHILE IT SOUNDS.
        changing.NoteOn(1, 60, 100);
        untouched.NoteOn(1, 60, 100);

        GmProbe.Render(changing, 0.5);
        GmProbe.Render(untouched, 0.5);

        changing.Adjustments.Program(ChoirAahs).Ensemble = GeneralMidiEnsemble.Full;

        var restOfIt = GmProbe.Render(changing, 0.8);
        var restOfTheOther = GmProbe.Render(untouched, 0.8);

        // And then a NEW note on each.
        changing.NoteOff(1, 60);
        untouched.NoteOff(1, 60);
        GmProbe.Render(changing, 0.6);
        GmProbe.Render(untouched, 0.6);

        var nextNote = GmProbe.PlayNote(changing, 1, 64, 100, 0.8, 0.2);
        var theirNextNote = GmProbe.PlayNote(untouched, 1, 64, 100, 0.8, 0.2);

        //Assert
        GmProbe.LargestDifference(restOfIt.Left, restOfTheOther.Left).Should().Be(
            0.0, "a note already sounding must not change under the knob");

        GmProbe.LargestDifference(nextNote.Left, theirNextNote.Left).Should().BeGreaterThan(
            0.01, "but the next note must be the fuller one");
    }

    [Fact]
    public void a_full_ensemble_renders_the_same_samples_twice_in_one_run()
    {
        //Arrange
        GeneralMidiSynthesizer first = GmProbe.BuildForProgram(ChoirAahs);
        GeneralMidiSynthesizer second = GmProbe.BuildForProgram(ChoirAahs);

        //Act
        first.Adjustments.Program(ChoirAahs).Ensemble = GeneralMidiEnsemble.Full;
        second.Adjustments.Program(ChoirAahs).Ensemble = GeneralMidiEnsemble.Full;

        var one = GmProbe.PlayNote(first, 1, 57, 96, 1.2, 0.4);
        var two = GmProbe.PlayNote(second, 1, 57, 96, 1.2, 0.4);

        //Assert
        GmProbe.LargestDifference(one.Left, two.Left).Should().Be(0.0);
        GmProbe.LargestDifference(one.Right, two.Right).Should().Be(0.0);
    }

    [Fact]
    public void the_ensemble_knob_takes_part_in_the_adjustment_bookkeeping()
    {
        //Arrange
        GeneralMidiAdjustment adjustment = new GeneralMidiAdjustment();

        //Act
        //Assert
        adjustment.Ensemble.Should().Be(GeneralMidiEnsemble.Standard);
        adjustment.IsDefault.Should().BeTrue();

        adjustment.Ensemble = GeneralMidiEnsemble.Full;
        adjustment.IsDefault.Should().BeFalse();

        GeneralMidiAdjustment clone = adjustment.Clone();
        clone.Ensemble.Should().Be(GeneralMidiEnsemble.Full);

        GeneralMidiAdjustment copy = new GeneralMidiAdjustment();
        copy.CopyFrom(adjustment);
        copy.Ensemble.Should().Be(GeneralMidiEnsemble.Full);
        copy.IsDefault.Should().BeFalse();

        copy.Reset();
        copy.Ensemble.Should().Be(GeneralMidiEnsemble.Standard);
        copy.IsDefault.Should().BeTrue();

        // Anything that is not one of the two defined values is the standard section.
        adjustment.Ensemble = (GeneralMidiEnsemble)57;
        adjustment.Ensemble.Should().Be(GeneralMidiEnsemble.Standard);
    }

    [Fact]
    public void a_whole_set_of_adjustments_carries_the_ensemble_through_copy_and_clone()
    {
        //Arrange
        GeneralMidiAdjustments set = new GeneralMidiAdjustments();

        //Act
        set.Program(GeneralMidiProgram.ChoirAahs).Ensemble = GeneralMidiEnsemble.Full;

        GeneralMidiAdjustments cloned = set.Clone();

        GeneralMidiAdjustments copied = new GeneralMidiAdjustments();
        copied.CopyFrom(set);

        //Assert
        set.HasAny.Should().BeTrue();
        cloned.Program(GeneralMidiProgram.ChoirAahs).Ensemble.Should().Be(GeneralMidiEnsemble.Full);
        copied.Program(GeneralMidiProgram.ChoirAahs).Ensemble.Should().Be(GeneralMidiEnsemble.Full);

        set.Reset();
        set.Program(GeneralMidiProgram.ChoirAahs).Ensemble
            .Should().Be(GeneralMidiEnsemble.Standard);

        // The clone was independent, so resetting the original left it alone.
        cloned.Program(GeneralMidiProgram.ChoirAahs).Ensemble.Should().Be(GeneralMidiEnsemble.Full);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void the_library_hands_the_ensemble_to_the_synthesizers_it_makes(bool perPart)
    {
        //Arrange
        // THE LIBRARY'S ADJUSTMENTS ARE A PROCESS-WIDE TEMPLATE, so this test sets one and puts it
        // back. The class is in the non-parallel collection for exactly this.
        GeneralMidiInstrumentLibrary library = GeneralMidiInstrumentLibrary.Instance;

        IMidiSynthesizer standard = Make(library, perPart);

        try
        {
            //Act
            library.Adjustments.Program(GeneralMidiProgram.ChoirAahs).Ensemble =
                GeneralMidiEnsemble.Full;

            IMidiSynthesizer full = Make(library, perPart);

            var plainly = Sing(standard);
            var fully = Sing(full);

            //Assert
            GmProbe.LargestDifference(plainly.Left, fully.Left).Should().BeGreaterThan(
                0.01,
                "a synthesizer the library made after the knob was turned must sing the fuller " +
                "section, in the {0} shape too", perPart ? "per-part" : "multi-timbral");
        }
        finally
        {
            library.Adjustments.Program(GeneralMidiProgram.ChoirAahs).Reset();
        }
    }

    [Fact]
    public void the_ensemble_only_reaches_the_synthesizers_the_library_makes_afterwards()
    {
        //Arrange
        GeneralMidiInstrumentLibrary library = GeneralMidiInstrumentLibrary.Instance;
        IMidiSynthesizer before = Make(library, perPart: true);

        try
        {
            //Act
            library.Adjustments.Program(GeneralMidiProgram.ChoirAahs).Ensemble =
                GeneralMidiEnsemble.Full;

            IMidiSynthesizer untouched = GeneralMidiSynthesizer.CreateForProgram(
                ChoirAahs, GmProbe.Settings());

            var made = Sing(before);
            var plain = Sing(untouched);

            //Assert
            // The library's set is COPIED into what it creates, not linked to it - so the one made
            // before the knob was turned is unchanged, and one built by hand never saw the knob.
            GmProbe.LargestDifference(made.Left, plain.Left).Should().Be(0.0);
        }
        finally
        {
            library.Adjustments.Program(GeneralMidiProgram.ChoirAahs).Reset();
        }
    }

    private static IMidiSynthesizer Make(GeneralMidiInstrumentLibrary library, bool perPart)
    {
        IMidiSynthesizer synthesizer = perPart
            ? library.CreateSynthesizer(ChoirAahs, GmProbe.SampleRate)
            : library.CreateMultiTimbralSynthesizer(GmProbe.SampleRate);

        synthesizer.MasterVolume = 1f;

        if (!perPart) { synthesizer.ProcessMidiMessage(0, 0xC0, ChoirAahs, 0); }

        return synthesizer;
    }

    // Driven through IMidiSynthesizer, on the WIRE, the way a sequencer drives one.
    private static (float[] Left, float[] Right) Sing(IMidiSynthesizer synthesizer)
    {
        int frames = (int)(1.2 * GmProbe.SampleRate / synthesizer.BlockSize) * synthesizer.BlockSize;

        float[] left = new float[frames];
        float[] right = new float[frames];

        synthesizer.ProcessMidiMessage(0, 0x90, 60, 100);
        synthesizer.Render(left, right);

        return (left, right);
    }

    [Fact]
    public void level_still_turns_the_choir_down()
    {
        //Arrange
        GeneralMidiSynthesizer quiet = GmProbe.BuildForProgram(ChoirAahs);
        GeneralMidiSynthesizer plain = GmProbe.BuildForProgram(ChoirAahs);

        //Act
        quiet.Adjustments.Program(ChoirAahs).Level = 0.5;

        double quietLevel = GmProbe.Rms(GmProbe.PlayNote(quiet, 1, 60, 100, 1.0, 0.4));
        double plainLevel = GmProbe.Rms(GmProbe.PlayNote(plain, 1, 60, 100, 1.0, 0.4));

        //Assert
        (quietLevel / plainLevel).Should().BeApproximately(0.5, 0.03);
    }

    [Fact]
    public void brightness_still_darkens_the_choir()
    {
        //Arrange
        GeneralMidiSynthesizer dark = GmProbe.BuildForProgram(ChoirAahs);
        GeneralMidiSynthesizer plain = GmProbe.BuildForProgram(ChoirAahs);

        //Act
        dark.Adjustments.Program(ChoirAahs).Brightness = -2.5;

        var darker = GmProbe.PlayNote(dark, 1, 60, 100, 1.0, 0.4);
        var normal = GmProbe.PlayNote(plain, 1, 60, 100, 1.0, 0.4);

        //Assert
        // The centroid of a vowel barely moves, because almost all of its energy is in the first
        // formant however dark the rest is made. What the knob really moves is how much sits ABOVE
        // the vowel, so that is what is measured.
        double dimmed = HighOverLow(darker.Left);
        double ordinary = HighOverLow(normal.Left);

        dimmed.Should().BeLessThan(
            ordinary / 3.0,
            "two and a half octaves down the filter should take most of what is above the vowel " +
            "away: {0:F5} against {1:F5}", dimmed, ordinary);

        GmProbe.Centroid(darker.Left).Should().BeLessThan(GmProbe.Centroid(normal.Left));
    }

    [Fact]
    public void vibrato_depth_still_reaches_the_choir()
    {
        //Arrange
        GeneralMidiSynthesizer deep = GmProbe.BuildForProgram(
            ChoirAahs, settings => settings.EnableReverbAndChorus = false);

        GeneralMidiSynthesizer plain = GmProbe.BuildForProgram(
            ChoirAahs, settings => settings.EnableReverbAndChorus = false);

        //Act
        deep.Adjustments.Program(ChoirAahs).VibratoDepth = 4.0;
        plain.Adjustments.Program(ChoirAahs).VibratoDepth = 0.0;

        var wide = GmProbe.PlayNote(deep, 1, 69, 100, 2.4, 0.1);
        var straight = GmProbe.PlayNote(plain, 1, 69, 100, 2.4, 0.1);
        var ordinary = GmProbe.PlayNote(
            GmProbe.BuildForProgram(ChoirAahs, settings => settings.EnableReverbAndChorus = false),
            1, 69, 100, 2.4, 0.1);

        //Assert
        // WHAT THE KNOB SCALES is the voicing's own low-frequency oscillator, and every singer
        // already carries a wander and a vibrato of its own that the knob does not touch - so a
        // measurement of "how much the note moves" is mostly measuring the section, not the knob.
        // What says the knob works is that turning it further moves the render FURTHER from the
        // straight one, and monotonically.
        int offset = GmProbe.SampleRate;
        int window = GmProbe.SampleRate;

        double atRest = DifferenceLevel(straight.Left, ordinary.Left, offset, window);
        double deeply = DifferenceLevel(straight.Left, wide.Left, offset, window);

        atRest.Should().BeGreaterThan(0.0, "the knob must reach the voice at all");

        deeply.Should().BeGreaterThan(
            atRest * 1.5,
            "four times the vibrato must move the note further than the voicing's own does: " +
            "{0:F5} against {1:F5}", deeply, atRest);
    }

    // The root-mean-square of the difference between two renders over a window.
    private static double DifferenceLevel(float[] first, float[] second, int offset, int count)
    {
        double sum = 0.0;

        for (int i = offset; i < offset + count; i++)
        {
            double apart = (double)first[i] - second[i];
            sum += apart * apart;
        }

        return Math.Sqrt(sum / count);
    }

    // How much of a render's power sits above the vowel - what a brightness control really moves,
    // where a spectral centroid dominated by the first formant hardly moves at all.
    private static double HighOverLow(float[] samples)
    {
        double[] magnitudes = Spectrum.WindowedMagnitudes(samples, GmProbe.SampleRate / 2);

        double low = 0.0;
        double high = 0.0;

        for (int bin = Bin(60.0); bin < magnitudes.Length; bin++)
        {
            double power = magnitudes[bin] * magnitudes[bin];

            if (bin < Bin(1500.0)) { low += power; } else { high += power; }
        }

        return low <= 0.0 ? 0.0 : high / low;
    }

    [Fact]
    public void pan_still_moves_the_choir_across_the_field()
    {
        //Arrange
        GeneralMidiSynthesizer left = GmProbe.BuildForProgram(ChoirAahs);

        //Act
        left.Adjustments.Program(ChoirAahs).Pan = -0.9;

        var placed = GmProbe.PlayNote(left, 1, 60, 100, 1.0, 0.3);

        //Assert
        GmProbe.Rms(placed.Left).Should().BeGreaterThan(GmProbe.Rms(placed.Right) * 1.5);
    }

    [Fact]
    public void the_reverb_send_still_reaches_the_choir()
    {
        //Arrange
        GeneralMidiSynthesizer wet = GmProbe.BuildForProgram(ChoirAahs);
        GeneralMidiSynthesizer dry = GmProbe.BuildForProgram(ChoirAahs);

        //Act
        dry.Adjustments.Program(ChoirAahs).ReverbSend = 0.0;

        var wetly = GmProbe.PlayNote(wet, 1, 60, 100, 0.6, 2.0);
        var dryly = GmProbe.PlayNote(dry, 1, 60, 100, 0.6, 2.0);

        //Assert
        int tail = (int)(1.8 * GmProbe.SampleRate);
        int window = GmProbe.SampleRate / 4;

        GmProbe.Rms(wetly.Left, tail, window)
            .Should().BeGreaterThan(GmProbe.Rms(dryly.Left, tail, window) * 2.0);
    }

    // How far the note swings over the second half of a held note, in cents - the spread between the
    // highest and the lowest frequency four analysis blocks report.
    private static double Swing(float[] samples)
    {
        double lowest = double.MaxValue;
        double highest = 0.0;

        for (int i = 0; i < 6; i++)
        {
            int offset = GmProbe.SampleRate + (i * Spectrum.BlockLength);
            double frequency = Integration.RenderProbe.Frequency(samples, offset);

            if (frequency < lowest) { lowest = frequency; }
            if (frequency > highest) { highest = frequency; }
        }

        return 1200.0 * Math.Log2(highest / lowest);
    }

    private static double[] DetuneTrace(GmChoirSpec spec, uint seed)
    {
        GmChoirOscillator singer = new GmChoirOscillator(GmProbe.SampleRate, spec, seed);
        singer.SetFrequency(261.626);
        singer.Reset(0.0);

        float[] buffer = new float[GmChoirOscillator.ControlSamples];
        int chunks = 10 * GmProbe.SampleRate / GmChoirOscillator.ControlSamples;

        double[] trace = new double[chunks];

        for (int chunk = 0; chunk < chunks; chunk++)
        {
            singer.Render(buffer);
            trace[chunk] = singer.DetuneCents;
        }

        return trace;
    }

    // One singer on its own, with or without the air.
    private static GmVoiceSpec Singer(bool breath)
    {
        GmVoiceSpec spec = GmVoiceProbe.Spec(GmTone.ChoirVowel, 1.0);
        GmChoirSpec choir = GmChoirSpec.For(GmTone.ChoirVowel, 1.0, 1.0);

        if (!breath)
        {
            choir.BreathAtOnset = 0.0;
            choir.BreathInSustain = 0.0;
        }

        spec.Layers[0].Choir = choir;

        return spec;
    }

    private static double BreathLevel(float[] withAir, float[] withoutAir, int offset, int count)
    {
        double sum = 0.0;

        for (int i = offset; i < offset + count; i++)
        {
            double air = (double)withAir[i] - withoutAir[i];
            sum += air * air;
        }

        return Math.Sqrt(sum / count);
    }

    // ONE singer on the fully open "ah", straight through the voice with no synthesizer, no unison,
    // no filter and no room - so a formant measurement measures the vowel and nothing else.
    private static double[] OneSingerSpectrum(int key)
    {
        GmVoiceSpec spec = GmVoiceProbe.Spec(GmTone.ChoirVowel, 1.0);

        var render = GmVoiceProbe.Render(spec, key, 100, 1.6, 0.1);

        return Spectrum.WindowedMagnitudes(render.Left, GmVoiceProbe.SampleRate);
    }

    // Choir Aahs in a named reading, sustained, with no room around it.
    private static double[] VowelSpectrum(GmChoirVoicing voicing, int key)
    {
        GeneralMidiSynthesizer synthesizer = GmProbe.BuildForProgram(
            ChoirAahs, settings => settings.EnableReverbAndChorus = false);

        synthesizer.ChoirVoicing = voicing;

        var render = GmProbe.PlayNote(synthesizer, 1, key, 100, Hold, 0.1);

        return Spectrum.WindowedMagnitudes(render.Left, GmProbe.SampleRate);
    }

    private static double[] SustainedSpectrum(int program, int key)
    {
        GeneralMidiSynthesizer synthesizer = GmProbe.BuildForProgram(
            program, settings => settings.EnableReverbAndChorus = false);

        var render = GmProbe.PlayNote(synthesizer, 1, key, 100, Hold, 0.1);

        // A second in, which is well past the attack and past the onset breath.
        return Spectrum.WindowedMagnitudes(render.Left, GmProbe.SampleRate);
    }

    private static double PeakFrequency(double[] magnitudes, double low, double high)
    {
        int first = Bin(low);
        int last = Bin(high);
        if (last >= magnitudes.Length) { last = magnitudes.Length - 1; }

        int best = first;

        for (int bin = first; bin <= last; bin++)
        {
            if (magnitudes[bin] > magnitudes[best]) { best = bin; }
        }

        return best * (double)Spectrum.SampleRate / Spectrum.BlockLength;
    }

    private static double PeakMagnitude(double[] magnitudes, double low, double high)
    {
        int first = Bin(low);
        int last = Bin(high);
        if (last >= magnitudes.Length) { last = magnitudes.Length - 1; }

        double best = 0.0;

        for (int bin = first; bin <= last; bin++)
        {
            if (magnitudes[bin] > best) { best = magnitudes[bin]; }
        }

        return best;
    }

    private static int Bin(double frequencyHz) =>
        (int)(frequencyHz * Spectrum.BlockLength / Spectrum.SampleRate);
}
