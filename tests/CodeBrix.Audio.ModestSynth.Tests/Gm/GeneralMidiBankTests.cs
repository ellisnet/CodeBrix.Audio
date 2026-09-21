using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// The bank, tested generically because the bank is DATA: every one of the 128 programs and every
/// one of the 47 percussion notes has to resolve, make a deliberate sound, and render the same way
/// twice.
/// </summary>
/// <remarks>
/// <para>
/// DETERMINISM IS FENCED THE REPOSITORY'S WAY: two renders taken on THIS machine in THIS run are
/// compared with each other. Nothing is pinned to a digest or to a committed sample value, because a
/// synthesizer reaches the platform's maths library and those values do not survive a change of
/// operating system - see MAINTAINER-README, "PINNED RENDERS AND THE PLATFORM MATHS LIBRARY".
/// </para>
/// <para>
/// THE LOUDNESS MEASURE is the root-mean-square of the mono sum over the LOUDEST HUNDRED
/// MILLISECONDS of a held middle C at velocity 100, rendered at unity master volume. It is measured
/// over a window rather than over the whole note because an average over the note would report how
/// LONG a program lasts as much as how loud it is, which would punish a woodblock and flatter a pad.
/// A General MIDI file mixes itself assuming its programs are comparably loud, so the band the bank
/// has to sit inside is asserted rather than left to taste.
/// </para>
/// </remarks>
[Collection(GeneralMidiLibraryCollection.Name)]
public class GeneralMidiBankTests
{
    // Enough of a note to hear the attack, the body and the start of the decay, and short enough
    // that 128 programs rendered three times over still runs in a couple of seconds.
    private const double Hold = 0.35;
    private const double Tail = 0.25;

    // Long enough for a pad with a second-long attack to reach its body, which is what the loudness
    // band has to compare.
    private const double LoudnessHold = 1.4;

    // The tail rendered after an audition phrase, so a decay is measured rather than cut off. It is
    // the tail the bank's loudness was measured with.
    private static readonly TimeSpan PhraseTail = TimeSpan.FromSeconds(1.5);

    /// <summary>Every General MIDI program number.</summary>
    /// <returns>One case per program, 0 to 127.</returns>
    public static IEnumerable<object[]> Programs()
    {
        for (int program = 0; program < GeneralMidi.ProgramCount; program++)
        {
            yield return new object[] { program };
        }
    }

    /// <summary>Every General MIDI percussion note number.</summary>
    /// <returns>One case per note, 35 to 81.</returns>
    public static IEnumerable<object[]> PercussionNotes()
    {
        for (int note = GeneralMidi.LowestPercussionNote; note <= GeneralMidi.HighestPercussionNote; note++)
        {
            yield return new object[] { note };
        }
    }

    [Theory]
    [MemberData(nameof(Programs))]
    public void every_program_sounds_and_renders_the_same_way_twice(int program)
    {
        //Arrange
        GeneralMidiSynthesizer first = GmProbe.BuildForProgram(program);
        GeneralMidiSynthesizer second = GmProbe.BuildForProgram(program);

        //Act
        var one = GmProbe.PlayNote(first, 1, 60, 100, Hold, Tail);
        var two = GmProbe.PlayNote(second, 1, 60, 100, Hold, Tail);

        //Assert
        GmProbe.Rms(one).Should().BeGreaterThan(
            0.0005, "program {0} ({1}) must make a sound", program, GeneralMidi.DisplayName((GeneralMidiProgram)program));

        GmProbe.LargestDifference(one.Left, two.Left).Should().Be(0.0);
        GmProbe.LargestDifference(one.Right, two.Right).Should().Be(0.0);
    }

    [Theory]
    [MemberData(nameof(Programs))]
    public void no_program_clips_at_full_velocity(int program)
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.BuildForProgram(
            program, settings => settings.MasterVolume = GeneralMidiSynthesizerSettings.DefaultMasterVolume);

        //Act
        var render = GmProbe.PlayNote(synthesizer, 1, 60, 127, Hold, Tail);

        //Assert
        GmProbe.Peak(render).Should().BeLessThanOrEqualTo(
            1.0, "program {0} ({1}) must not clip a single note at full velocity",
            program, GeneralMidi.DisplayName((GeneralMidiProgram)program));
    }

    [Theory]
    [MemberData(nameof(PercussionNotes))]
    public void every_percussion_note_sounds_and_renders_the_same_way_twice(int note)
    {
        //Arrange
        GeneralMidiSynthesizer first = GmProbe.BuildForPercussion();
        GeneralMidiSynthesizer second = GmProbe.BuildForPercussion();

        //Act
        var one = GmProbe.PlayNote(first, 1, note, 100, Hold, Tail);
        var two = GmProbe.PlayNote(second, 1, note, 100, Hold, Tail);

        //Assert
        GmProbe.Rms(one).Should().BeGreaterThan(
            0.0005, "percussion note {0} ({1}) must make a sound",
            note, GeneralMidi.DisplayName((GeneralMidiPercussion)note));

        GmProbe.LargestDifference(one.Left, two.Left).Should().Be(0.0);
        GmProbe.LargestDifference(one.Right, two.Right).Should().Be(0.0);
    }

    [Theory]
    [MemberData(nameof(PercussionNotes))]
    public void no_percussion_note_clips_at_full_velocity(int note)
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.BuildForPercussion(
            settings => settings.MasterVolume = GeneralMidiSynthesizerSettings.DefaultMasterVolume);

        //Act
        var render = GmProbe.PlayNote(synthesizer, 1, note, 127, Hold, Tail);

        //Assert
        GmProbe.Peak(render).Should().BeLessThanOrEqualTo(
            1.0, "percussion note {0} ({1}) must not clip on its own at full velocity",
            note, GeneralMidi.DisplayName((GeneralMidiPercussion)note));
    }

    [Fact]
    public void the_programs_sit_inside_one_loudness_band()
    {
        //Arrange
        double[] levels = new double[GeneralMidi.ProgramCount];

        //Act
        for (int program = 0; program < levels.Length; program++)
        {
            GeneralMidiSynthesizer synthesizer = GmProbe.BuildForProgram(program);
            levels[program] = GmProbe.LoudestWindow(GmProbe.PlayNote(synthesizer, 1, 60, 100, LoudnessHold, Tail));
        }

        //Assert
        int quietestProgram = 0;
        int loudestProgram = 0;

        for (int program = 0; program < levels.Length; program++)
        {
            if (levels[program] < levels[quietestProgram]) { quietestProgram = program; }
            if (levels[program] > levels[loudestProgram]) { loudestProgram = program; }
        }

        double spreadDecibels = 20.0 * Math.Log10(levels[loudestProgram] / levels[quietestProgram]);

        // A General MIDI file mixes itself assuming its programs are comparably loud, so the band is
        // asserted rather than left to taste. The measure is the LOUDEST HUNDRED MILLISECONDS of a
        // held middle C at velocity 100, which is fair to a pad with a second-long attack and to a
        // woodblock alike - a plain average over the note would simply report how long each one
        // lasts.
        spreadDecibels.Should().BeLessThan(
            6.0,
            "the loudest program ({0}, {1:F4}) must stay close to the quietest ({2}, {3:F4})",
            GeneralMidi.DisplayName((GeneralMidiProgram)loudestProgram), levels[loudestProgram],
            GeneralMidi.DisplayName((GeneralMidiProgram)quietestProgram), levels[quietestProgram]);
    }

    [Fact]
    public void the_kit_sits_inside_one_loudness_band()
    {
        //Arrange
        int count = GeneralMidi.HighestPercussionNote - GeneralMidi.LowestPercussionNote + 1;
        double[] levels = new double[count];

        //Act
        for (int index = 0; index < count; index++)
        {
            int note = GeneralMidi.LowestPercussionNote + index;
            GeneralMidiSynthesizer synthesizer = GmProbe.BuildForPercussion();
            levels[index] = GmProbe.Peak(GmProbe.PlayNote(synthesizer, 1, note, 100, Hold, Tail));
        }

        //Assert
        int quietest = 0;
        int loudest = 0;

        for (int index = 0; index < count; index++)
        {
            if (levels[index] < levels[quietest]) { quietest = index; }
            if (levels[index] > levels[loudest]) { loudest = index; }
        }

        // The kit is measured by PEAK rather than by RMS: a kick and a shaker have wildly different
        // durations, and a listener judges a drum by how hard it hits rather than by its energy over
        // time.
        (20.0 * Math.Log10(levels[loudest] / levels[quietest])).Should().BeLessThan(
            12.0,
            "the loudest kit piece ({0}, {1:F4}) must stay close to the quietest ({2}, {3:F4})",
            GeneralMidi.DisplayName((GeneralMidiPercussion)(GeneralMidi.LowestPercussionNote + loudest)),
            levels[loudest],
            GeneralMidi.DisplayName((GeneralMidiPercussion)(GeneralMidi.LowestPercussionNote + quietest)),
            levels[quietest]);
    }

    /// <summary>
    /// The programs whose rows carry a level trim have to stay with their families on the phrase
    /// they are actually heard on.
    /// </summary>
    /// <param name="program">The program to measure against its own family.</param>
    /// <remarks>
    /// A held middle C says all 128 programs are level, and on the phrases the auditions play these
    /// sustained and summed their way clear of the family around them - the one imbalance a listener
    /// noticed. Their rows were trimmed for it, and this is the fence: the eight programs of the
    /// family are measured HERE, in THIS run, on the same machine, and the trimmed one is compared
    /// with the median of its seven neighbours. Nothing is pinned to a figure recorded on another
    /// machine. The two VOICE programs are here because their whole voicing was rebuilt, which moved
    /// where they sit on a sustained chord even though it left them level on a held note.
    /// </remarks>
    [Theory]
    [InlineData((int)GeneralMidiProgram.ElectricPiano1)]
    [InlineData((int)GeneralMidiProgram.ElectricGuitarClean)]
    [InlineData((int)GeneralMidiProgram.BagPipe)]
    [InlineData((int)GeneralMidiProgram.ChoirAahs)]
    [InlineData((int)GeneralMidiProgram.VoiceOohs)]
    public void a_trimmed_program_stands_with_its_family_on_the_phrase_it_is_heard_on(int program)
    {
        //Arrange
        int first = program / GeneralMidi.ProgramsPerFamily * GeneralMidi.ProgramsPerFamily;
        double[] levels = new double[GeneralMidi.ProgramsPerFamily];

        //Act
        for (int index = 0; index < levels.Length; index++)
        {
            levels[index] = PhraseLevel(first + index);
        }

        //Assert
        List<double> neighbours = new List<double>();

        for (int index = 0; index < levels.Length; index++)
        {
            if (first + index != program) { neighbours.Add(levels[index]); }
        }

        neighbours.Sort();

        double family = neighbours[neighbours.Count / 2];
        double decibels = 20.0 * Math.Log10(levels[program - first] / family);

        Math.Abs(decibels).Should().BeLessThan(
            1.5,
            "{0} ({1:F4}) must stand with the {2} family ({3:F4}) on the phrase it is heard on, " +
            "and it is {4:F1} dB away",
            GeneralMidi.DisplayName((GeneralMidiProgram)program),
            levels[program - first],
            GeneralMidi.DisplayName(GeneralMidi.FamilyOf((GeneralMidiProgram)program)),
            family,
            decibels);
    }

    // The loudest hundred milliseconds of a program playing ITS OWN audition phrase, at the default
    // master volume - which is how a listener meets it, and what the trims were measured on.
    private static double PhraseLevel(int program)
    {
        GmAuditionPlan plan = GmAudition.SingleProgram(program);

        float[] interleaved = SoundFontRenderer.Render(
            new GeneralMidiSynthesizer(GmProbe.SampleRate), plan.Sequence, PhraseTail);

        int frames = interleaved.Length / 2;
        float[] left = new float[frames];
        float[] right = new float[frames];

        for (int frame = 0; frame < frames; frame++)
        {
            left[frame] = interleaved[frame * 2];
            right[frame] = interleaved[(frame * 2) + 1];
        }

        return GmProbe.LoudestWindow((left, right));
    }

    [Fact]
    public void every_program_is_a_different_sound_from_its_neighbour()
    {
        //Arrange
        int identical = 0;

        //Act
        for (int program = 1; program < GeneralMidi.ProgramCount; program++)
        {
            GeneralMidiSynthesizer previous = GmProbe.BuildForProgram(program - 1);
            GeneralMidiSynthesizer current = GmProbe.BuildForProgram(program);

            var before = GmProbe.PlayNote(previous, 1, 60, 100, 0.2, 0.05);
            var after = GmProbe.PlayNote(current, 1, 60, 100, 0.2, 0.05);

            if (GmProbe.LargestDifference(before.Left, after.Left) == 0.0) { identical++; }
        }

        //Assert
        // No program may be a byte-for-byte copy of the one before it: that is what "no
        // placeholders and nothing falls back to piano" means as a test.
        identical.Should().Be(0);
    }

    [Fact]
    public void a_high_note_and_a_low_note_are_both_in_tune_on_a_pitched_program()
    {
        //Arrange
        GeneralMidiSynthesizer low = GmProbe.BuildForProgram((int)GeneralMidiProgram.Flute);
        GeneralMidiSynthesizer high = GmProbe.BuildForProgram((int)GeneralMidiProgram.Flute);

        //Act
        low.NoteOn(1, 57, 100);
        var (lowLeft, _) = GmProbe.Render(low, 0.5);

        high.NoteOn(1, 81, 100);
        var (highLeft, _) = GmProbe.Render(high, 0.5);

        //Assert
        // A4 is 220 Hz two octaves under concert A, and A5 is 880.
        Integration.RenderProbe.Frequency(lowLeft, 2048).Should().BeApproximately(220.0, 6.0);
        Integration.RenderProbe.Frequency(highLeft, 2048).Should().BeApproximately(880.0, 14.0);
    }

    /// <summary>The programs a listener would say have a definite pitch.</summary>
    /// <returns>One case per program.</returns>
    public static IEnumerable<object[]> PitchedPrograms()
    {
        // Everything whose job is to play a tune, which is every family except the synth effects,
        // the sound effects, and the three percussive programs that are drums rather than notes.
        for (int program = 0; program < GeneralMidi.ProgramCount; program++)
        {
            GeneralMidiProgramFamily family = GeneralMidi.FamilyOf((GeneralMidiProgram)program);

            if (family == GeneralMidiProgramFamily.SynthEffects) { continue; }
            if (family == GeneralMidiProgramFamily.SoundEffects) { continue; }

            // Taiko, the melodic tom and the synth drum are drums with a pitch envelope on them;
            // the reverse cymbal is noise. None of them holds a steady fundamental.
            if (program >= 116) { continue; }

            yield return new object[] { program };
        }
    }

    [Theory]
    [MemberData(nameof(PitchedPrograms))]
    public void a_pitched_program_really_sounds_at_the_note_it_was_played(int program)
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.BuildForProgram(program);

        //Act
        synthesizer.NoteOn(1, 60, 100);
        var (left, _) = GmProbe.Render(synthesizer, 0.6);

        //Assert
        // FREQUENCY MODULATION CAN NULL A FUNDAMENTAL OUTRIGHT. Past a modulation index of about 2.4
        // radians the carrier disappears from its own spectrum and the note sounds a fifth or an
        // octave away from the key that was pressed - which is a fault a "does it make a sound" test
        // cannot see. So: there must be real energy AT middle C, not merely somewhere.
        double[] magnitudes = Spectrum.WindowedMagnitudes(left, Spectrum.BlockLength);

        int fundamental = (int)Math.Round(261.626 * Spectrum.BlockLength / Spectrum.SampleRate);
        double atTheNote = 0.0;

        for (int bin = fundamental - 2; bin <= fundamental + 2; bin++)
        {
            if (magnitudes[bin] > atTheNote) { atTheNote = magnitudes[bin]; }
        }

        double loudest = magnitudes[Spectrum.PeakBin(magnitudes)];

        // A sixth of the loudest partial. Plenty of real instruments - a brass note, a reed, a bell -
        // put more energy into an upper partial than into the fundamental, so the bar is "there is
        // definitely something there" rather than "the fundamental is loudest".
        atTheNote.Should().BeGreaterThan(
            loudest * 0.15,
            "program {0} ({1}) must put real energy on the note it was played",
            program, GeneralMidi.DisplayName((GeneralMidiProgram)program));
    }

    [Fact]
    public void the_coverage_reports_every_program_and_every_percussion_note()
    {
        //Arrange
        var coverage = GeneralMidiInstrumentLibrary.Instance.Coverage;

        //Act
        //Assert
        coverage.Programs.Count.Should().Be(GeneralMidi.ProgramCount);
        coverage.PercussionNotes.Count.Should().Be(
            GeneralMidi.HighestPercussionNote - GeneralMidi.LowestPercussionNote + 1);

        for (int program = 0; program < GeneralMidi.ProgramCount; program++)
        {
            coverage.CoversProgram(program).Should().BeTrue();
            coverage.KeyRangeOf(program).KeyCount.Should().Be(128);
        }

        for (int note = GeneralMidi.LowestPercussionNote; note <= GeneralMidi.HighestPercussionNote; note++)
        {
            coverage.CoversPercussionNote(note).Should().BeTrue();
        }
    }
}
