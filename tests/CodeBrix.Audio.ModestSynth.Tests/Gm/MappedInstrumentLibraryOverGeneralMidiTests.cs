using System;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// The workflow <see cref="MappedInstrumentLibrary" /> exists for, played out over the General MIDI
/// library: start from "ModestSynthGm", listen, then swap voices one at a time for instruments of
/// your own, listening in between.
/// </summary>
/// <remarks>
/// <para>
/// The instruments swapped in here are other General MIDI voicings, because those are what this
/// test project has to hand - a Decent Sampler pack from Pianobook or an SFZ file works exactly the
/// same way, and is covered over in CodeBrix.Audio.Tests where those fixtures live. What is being
/// proved here is the ARITHMETIC of the swap: the voice that was replaced sounds like the
/// replacement, every other voice is untouched, and putting it back restores what was there.
/// </para>
/// <para>
/// EVERY TEST HERE RESOLVES BY NAME and none of them reads or asserts on the registry's default.
/// </para>
/// </remarks>
[Collection(GeneralMidiLibraryCollection.Name)]
public class MappedInstrumentLibraryOverGeneralMidiTests
{
    private const int ChoirAahs = (int)GeneralMidiProgram.ChoirAahs;
    private const int ChurchOrgan = (int)GeneralMidiProgram.ChurchOrgan;
    private const int Celesta = (int)GeneralMidiProgram.Celesta;
    private const int Xylophone = (int)GeneralMidiProgram.Xylophone;

    [Fact]
    public void the_base_library_plays_every_voice_until_one_is_swapped()
    {
        //Arrange
        MappedInstrumentLibrary library = Mapped();

        //Act
        var throughTheMapping = Play(library.CreateSynthesizer(ChoirAahs, GmProbe.SampleRate), 60);
        var straightFromTheBase = Play(
            GeneralMidiInstrumentLibrary.Instance.CreateSynthesizer(ChoirAahs, GmProbe.SampleRate), 60);

        //Assert
        GmProbe.Rms(throughTheMapping).Should().BeGreaterThan(0.0005);
        GmProbe.LargestDifference(throughTheMapping.Left, straightFromTheBase.Left).Should().Be(0.0);
    }

    [Fact]
    public void swapping_one_voice_changes_only_that_program()
    {
        //Arrange
        MappedInstrumentLibrary library = Mapped();
        var celestaBefore = Play(library.CreateSynthesizer(Celesta, GmProbe.SampleRate), 72);

        //Act
        library.SetInstrumentFromLibrary(
            GeneralMidiProgram.ChoirAahs, GeneralMidiInstrumentLibrary.LibraryName, Xylophone);

        //Assert
        var choirNow = Play(library.CreateSynthesizer(ChoirAahs, GmProbe.SampleRate), 72);
        var xylophone = Play(
            GeneralMidiInstrumentLibrary.Instance.CreateSynthesizer(Xylophone, GmProbe.SampleRate), 72);
        var celestaNow = Play(library.CreateSynthesizer(Celesta, GmProbe.SampleRate), 72);

        GmProbe.LargestDifference(choirNow.Left, xylophone.Left).Should().Be(0.0);
        GmProbe.LargestDifference(celestaNow.Left, celestaBefore.Left).Should().Be(0.0);
    }

    [Fact]
    public void a_second_voice_swapped_later_changes_only_its_own_program()
    {
        //Arrange
        MappedInstrumentLibrary library = Mapped();
        library.SetInstrumentFromLibrary(
            GeneralMidiProgram.ChoirAahs, GeneralMidiInstrumentLibrary.LibraryName, Xylophone);
        var choirAfterTheFirstSwap = Play(library.CreateSynthesizer(ChoirAahs, GmProbe.SampleRate), 72);

        //Act
        library.SetInstrument(
            GeneralMidiProgram.Celesta,
            sampleRate => GeneralMidiSynthesizer.CreateForProgram(ChurchOrgan, sampleRate));

        //Assert
        // The second swap is the same one-line change as the first, and the first is not disturbed
        // by it - which is what "one at a time, listening in between" has to mean in the API.
        var choirNow = Play(library.CreateSynthesizer(ChoirAahs, GmProbe.SampleRate), 72);
        var celestaNow = Play(library.CreateSynthesizer(Celesta, GmProbe.SampleRate), 72);
        var organ = Play(
            GeneralMidiInstrumentLibrary.Instance.CreateSynthesizer(ChurchOrgan, GmProbe.SampleRate), 72);

        GmProbe.LargestDifference(choirNow.Left, choirAfterTheFirstSwap.Left).Should().Be(0.0);
        GmProbe.LargestDifference(celestaNow.Left, organ.Left).Should().Be(0.0);
        library.SubstitutedPrograms.Should().Equal(Celesta, ChoirAahs);
    }

    [Fact]
    public void clearing_a_swap_restores_the_base_voicing()
    {
        //Arrange
        MappedInstrumentLibrary library = Mapped();
        var choirFromTheBase = Play(library.CreateSynthesizer(ChoirAahs, GmProbe.SampleRate), 72);
        library.SetInstrumentFromLibrary(
            GeneralMidiProgram.ChoirAahs, GeneralMidiInstrumentLibrary.LibraryName, Xylophone);

        //Act
        library.ClearInstrument(GeneralMidiProgram.ChoirAahs);

        //Assert
        var choirAgain = Play(library.CreateSynthesizer(ChoirAahs, GmProbe.SampleRate), 72);

        library.HasSubstitute(GeneralMidiProgram.ChoirAahs).Should().BeFalse();
        GmProbe.LargestDifference(choirAgain.Left, choirFromTheBase.Left).Should().Be(0.0);
    }

    [Fact]
    public void a_voice_swapped_after_the_library_is_registered_is_honoured()
    {
        //Arrange
        string name = $"MappedOverGm-{Guid.NewGuid():N}";
        MappedInstrumentLibrary library = Mapped(name);
        library.Register();

        //Act
        library.SetInstrumentFromLibrary(
            GeneralMidiProgram.ChoirAahs, GeneralMidiInstrumentLibrary.LibraryName, Xylophone);

        //Assert
        IInstrumentLibrary resolved = InstrumentLibraryRegistry.Resolve(name);
        var choirNow = Play(resolved.CreateSynthesizer(ChoirAahs, GmProbe.SampleRate), 72);
        var xylophone = Play(
            GeneralMidiInstrumentLibrary.Instance.CreateSynthesizer(Xylophone, GmProbe.SampleRate), 72);

        resolved.Should().BeSameAs(library);
        GmProbe.LargestDifference(choirNow.Left, xylophone.Left).Should().Be(0.0);
    }

    [Fact]
    public void the_shapes_and_the_coverage_come_from_the_base_library()
    {
        //Arrange
        MappedInstrumentLibrary library = Mapped();

        //Act
        InstrumentCoverage coverage = library.Coverage;

        //Assert
        library.SupportsPerPart.Should().BeTrue();
        library.SupportsMultiTimbral.Should().BeTrue();
        library.BaseLibraryName.Should().Be(GeneralMidiInstrumentLibrary.LibraryName);
        coverage.Programs.Count.Should().Be(GeneralMidi.ProgramCount);
        coverage.PercussionNotes.Count.Should().Be(47);
    }

    [Fact]
    public void a_swapped_voice_with_a_narrow_key_range_says_so_in_the_coverage()
    {
        //Arrange
        MappedInstrumentLibrary library = Mapped();

        //Act
        library.SetInstrument(
            GeneralMidiProgram.ChoirAahs,
            sampleRate => GeneralMidiSynthesizer.CreateForProgram(Xylophone, sampleRate),
            new InstrumentKeyRange(48, 72));

        //Assert
        // A General MIDI library covers the whole keyboard; a Pianobook-style instrument sampled
        // over two octaves does not, and the coverage has to say which one is playing the part.
        GeneralMidiInstrumentLibrary.Instance.Coverage.KeyRangeOf(ChoirAahs)
            .Should().Be(InstrumentKeyRange.Full);
        library.Coverage.KeyRangeOf(ChoirAahs).Should().Be(new InstrumentKeyRange(48, 72));
        library.Coverage.CoversNote(ChoirAahs, 36).Should().BeFalse();
        library.Coverage.CoversNote(Celesta, 36).Should().BeTrue();
    }

    [Fact]
    public void the_percussion_kit_can_be_swapped_for_an_instrument_of_your_own()
    {
        //Arrange
        MappedInstrumentLibrary library = Mapped();

        //Act
        library.SetPercussion(
            sampleRate => GeneralMidiSynthesizer.CreateForProgram(Xylophone, sampleRate), [60, 62]);

        //Assert
        var kit = Play(library.CreatePercussionSynthesizer(GmProbe.SampleRate), 60);
        var xylophone = Play(
            GeneralMidiInstrumentLibrary.Instance.CreateSynthesizer(Xylophone, GmProbe.SampleRate), 60);

        library.HasPercussionSubstitute.Should().BeTrue();
        library.Coverage.PercussionNotes.Should().Equal(60, 62);
        GmProbe.LargestDifference(kit.Left, xylophone.Left).Should().Be(0.0);
    }

    [Fact]
    public void the_multi_timbral_shape_plays_a_swapped_voice_when_the_music_selects_it()
    {
        //Arrange
        MappedInstrumentLibrary library = Mapped();
        library.SetInstrumentFromLibrary(
            GeneralMidiProgram.ChoirAahs, GeneralMidiInstrumentLibrary.LibraryName, Xylophone);
        IMidiSynthesizer synthesizer = library.CreateMultiTimbralSynthesizer(GmProbe.SampleRate);

        //Act
        synthesizer.ProcessMidiMessage(0, 0xC0, ChoirAahs, 0);
        var swapped = Play(synthesizer, 72);

        //Assert
        var xylophone = Play(
            GeneralMidiInstrumentLibrary.Instance.CreateSynthesizer(Xylophone, GmProbe.SampleRate), 72);
        var choirFromTheBase = Play(
            GeneralMidiInstrumentLibrary.Instance.CreateSynthesizer(ChoirAahs, GmProbe.SampleRate), 72);

        GmProbe.LargestDifference(swapped.Left, xylophone.Left).Should().Be(0.0);
        GmProbe.LargestDifference(swapped.Left, choirFromTheBase.Left).Should().BeGreaterThan(0.001);
    }

    [Fact]
    public void the_multi_timbral_shape_plays_the_base_voicing_for_every_other_program()
    {
        //Arrange
        MappedInstrumentLibrary library = Mapped();
        library.SetInstrumentFromLibrary(
            GeneralMidiProgram.ChoirAahs, GeneralMidiInstrumentLibrary.LibraryName, Xylophone);
        IMidiSynthesizer synthesizer = library.CreateMultiTimbralSynthesizer(GmProbe.SampleRate);

        //Act
        synthesizer.ProcessMidiMessage(0, 0xC0, Celesta, 0);
        var throughTheMapping = Play(synthesizer, 72);

        //Assert
        GeneralMidiSynthesizer plain = GeneralMidiSynthesizer.CreateForProgram(Celesta, GmProbe.SampleRate);
        var straightFromTheBase = Play(plain, 72);

        GmProbe.LargestDifference(throughTheMapping.Left, straightFromTheBase.Left).Should().Be(0.0);
    }

    [Fact]
    public void a_program_change_between_the_base_and_a_swapped_voice_leaves_no_stuck_notes()
    {
        //Arrange
        MappedInstrumentLibrary library = Mapped();
        library.SetInstrumentFromLibrary(
            GeneralMidiProgram.ChoirAahs, GeneralMidiInstrumentLibrary.LibraryName, Xylophone);

        IMidiSynthesizer moved = library.CreateMultiTimbralSynthesizer(GmProbe.SampleRate);
        IMidiSynthesizer left = library.CreateMultiTimbralSynthesizer(GmProbe.SampleRate);

        // A church organ holds for as long as the key is down, so a voice still sounding a second
        // later is a voice nothing released.
        moved.ProcessMidiMessage(0, 0xC0, ChurchOrgan, 0);
        moved.ProcessMidiMessage(0, 0x90, 60, 100);
        left.ProcessMidiMessage(0, 0xC0, ChurchOrgan, 0);
        left.ProcessMidiMessage(0, 0x90, 60, 100);

        //Act
        moved.ProcessMidiMessage(0, 0xC0, ChoirAahs, 0);

        Render(moved, 2.0);
        Render(left, 2.0);

        //Assert
        moved.ActiveVoiceCount.Should().Be(0);
        left.ActiveVoiceCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void the_controllers_the_music_sent_reach_the_swapped_voice()
    {
        //Arrange
        MappedInstrumentLibrary library = Mapped();
        library.SetInstrumentFromLibrary(
            GeneralMidiProgram.ChoirAahs, GeneralMidiInstrumentLibrary.LibraryName, Xylophone);

        IMidiSynthesizer quiet = library.CreateMultiTimbralSynthesizer(GmProbe.SampleRate);
        IMidiSynthesizer loud = library.CreateMultiTimbralSynthesizer(GmProbe.SampleRate);

        //Act
        // The volume is set while the BASE still has the channel, and the program change then hands
        // the channel to the swapped voice - which has to be told.
        quiet.ProcessMidiMessage(0, 0xB0, 7, 20);
        quiet.ProcessMidiMessage(0, 0xC0, ChoirAahs, 0);
        loud.ProcessMidiMessage(0, 0xB0, 7, 127);
        loud.ProcessMidiMessage(0, 0xC0, ChoirAahs, 0);

        //Assert
        double quietLevel = GmProbe.Rms(Play(quiet, 72));
        double loudLevel = GmProbe.Rms(Play(loud, 72));

        loudLevel.Should().BeGreaterThan(quietLevel * 2.0);
    }

    [Fact]
    public void a_whole_arrangement_renders_offline_with_one_voice_of_your_own()
    {
        //Arrange
        MappedInstrumentLibrary library = Mapped();
        library.SetInstrumentFromLibrary(
            GeneralMidiProgram.ChoirAahs, GeneralMidiInstrumentLibrary.LibraryName, Xylophone);

        MidiEventCollection events = new MidiEventCollection(1, 480);
        events.AddEvent(new PatchChangeEvent(0, 1, ChoirAahs), 1);
        events.AddEvent(new NoteEvent(0, 1, MidiCommandCode.NoteOn, 72, 100), 1);
        events.AddEvent(new NoteEvent(480, 1, MidiCommandCode.NoteOff, 72, 0), 1);
        events.AddEvent(new PatchChangeEvent(0, 2, Celesta), 2);
        events.AddEvent(new NoteEvent(0, 2, MidiCommandCode.NoteOn, 60, 90), 2);
        events.AddEvent(new NoteEvent(960, 2, MidiCommandCode.NoteOff, 60, 0), 2);
        events.AddEvent(
            new NoteEvent(0, GeneralMidi.PercussionChannel, MidiCommandCode.NoteOn, 36, 110),
            GeneralMidi.PercussionChannel);
        events.AddEvent(
            new NoteEvent(240, GeneralMidi.PercussionChannel, MidiCommandCode.NoteOff, 36, 0),
            GeneralMidi.PercussionChannel);
        events.PrepareForExport();

        //Act
        float[] rendered = SoundFontRenderer.Render(
            library.CreateMultiTimbralSynthesizer(GmProbe.SampleRate),
            MidiSequence.FromEvents(events),
            TimeSpan.FromSeconds(1.0));

        //Assert
        GmProbe.Peak(rendered).Should().BeGreaterThan(0.02);
        GmProbe.Peak(rendered).Should().BeLessThanOrEqualTo(1.0);
    }

    private static MappedInstrumentLibrary Mapped(string name = "MappedOverGm")
    {
        GeneralMidiInstrumentLibrary.Register();

        return new MappedInstrumentLibrary(
            name,
            "ModestSynthGm, with a few voices of my own.",
            GeneralMidiInstrumentLibrary.LibraryName);
    }

    private static (float[] Left, float[] Right) Play(
        IMidiSynthesizer synthesizer, int key, int wireChannel = 0)
    {
        synthesizer.ProcessMidiMessage(wireChannel, 0x90, key, 100);

        int frames = synthesizer.SampleRate / 2;
        float[] left = new float[frames];
        float[] right = new float[frames];

        synthesizer.Render(left, right);

        return (left, right);
    }

    private static void Render(IMidiSynthesizer synthesizer, double seconds)
    {
        int frames = (int)(seconds * synthesizer.SampleRate);
        float[] left = new float[frames];
        float[] right = new float[frames];

        synthesizer.Render(left, right);
    }
}
