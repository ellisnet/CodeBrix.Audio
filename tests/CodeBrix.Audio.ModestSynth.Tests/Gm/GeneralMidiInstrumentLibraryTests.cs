using System;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// The instrument library: both shapes of <see cref="IInstrumentLibrary" />, the coverage it
/// reports, its registered name, and a <see cref="GeneralMidiInstrumentLibrary.Register" /> that can
/// be called any number of times.
/// </summary>
/// <remarks>
/// EVERY TEST HERE RESOLVES BY NAME and none of them reads or asserts on the registry's DEFAULT.
/// The default is whichever library registered first in the process, so a test that asserted on it
/// would be asserting on which test ran first. The tests that deliberately change the default live
/// in CodeBrix.Audio.Tests behind their own environment gate.
/// </remarks>
[Collection(GeneralMidiLibraryCollection.Name)]
public class GeneralMidiInstrumentLibraryTests
{
    [Fact]
    public void the_registered_name_is_the_one_the_plan_settled_on()
    {
        //Arrange
        //Act
        //Assert
        GeneralMidiInstrumentLibrary.LibraryName.Should().Be("ModestSynthGm");
        GeneralMidiInstrumentLibrary.Instance.Name.Should().Be("ModestSynthGm");
    }

    [Fact]
    public void registering_it_makes_it_resolvable_by_name_and_doing_so_again_changes_nothing()
    {
        //Arrange
        //Act
        GeneralMidiInstrumentLibrary.Register();
        GeneralMidiInstrumentLibrary.Register();
        GeneralMidiInstrumentLibrary.Register();

        //Assert
        GeneralMidiInstrumentLibrary.IsRegistered.Should().BeTrue();
        InstrumentLibraryRegistry.IsRegistered(GeneralMidiInstrumentLibrary.LibraryName).Should().BeTrue();

        InstrumentLibraryRegistry.Resolve("modestsynthgm")
            .Should().BeSameAs(GeneralMidiInstrumentLibrary.Instance);
    }

    [Fact]
    public void it_offers_both_shapes_and_says_so()
    {
        //Arrange
        IInstrumentLibrary library = GeneralMidiInstrumentLibrary.Instance;

        //Act
        //Assert
        library.SupportsPerPart.Should().BeTrue();
        library.SupportsMultiTimbral.Should().BeTrue();
        library.Description.Should().NotBeNullOrWhiteSpace();
    }

    [Fact]
    public void the_coverage_is_the_whole_of_general_midi()
    {
        //Arrange
        InstrumentCoverage coverage = GeneralMidiInstrumentLibrary.Instance.Coverage;

        //Act
        //Assert
        coverage.Programs.Count.Should().Be(GeneralMidi.ProgramCount);
        coverage.PercussionNotes.Count.Should().Be(47);
        coverage.CoversProgram(0).Should().BeTrue();
        coverage.CoversProgram(127).Should().BeTrue();
        coverage.CoversPercussionNote(GeneralMidi.LowestPercussionNote).Should().BeTrue();
        coverage.CoversPercussionNote(GeneralMidi.HighestPercussionNote).Should().BeTrue();
        coverage.CoversPercussionNote(GeneralMidi.HighestPercussionNote + 1).Should().BeFalse();
    }

    [Fact]
    public void a_per_part_synthesizer_plays_the_program_it_was_asked_for()
    {
        //Arrange
        IInstrumentLibrary library = GeneralMidiInstrumentLibrary.Instance;

        //Act
        IMidiSynthesizer celesta =
            library.CreateSynthesizer((int)GeneralMidiProgram.Celesta, GmProbe.SampleRate);
        IMidiSynthesizer flute =
            library.CreateSynthesizer((int)GeneralMidiProgram.Flute, GmProbe.SampleRate);

        var first = Play(celesta, 72);
        var second = Play(flute, 72);

        //Assert
        celesta.SampleRate.Should().Be(GmProbe.SampleRate);
        GmProbe.Rms(first).Should().BeGreaterThan(0.0005);
        GmProbe.LargestDifference(first.Left, second.Left).Should().BeGreaterThan(0.001);
    }

    [Fact]
    public void a_per_part_synthesizer_is_pinned_and_ignores_a_program_change()
    {
        //Arrange
        IInstrumentLibrary library = GeneralMidiInstrumentLibrary.Instance;

        IMidiSynthesizer pinned =
            library.CreateSynthesizer((int)GeneralMidiProgram.Celesta, GmProbe.SampleRate);
        IMidiSynthesizer reference =
            library.CreateSynthesizer((int)GeneralMidiProgram.Celesta, GmProbe.SampleRate);

        //Act
        pinned.ProcessMidiMessage(0, 0xC0, (int)GeneralMidiProgram.Tuba, 0);
        pinned.ProcessMidiMessage(0, 0xB0, 0, 4);

        var afterChange = Play(pinned, 72);
        var untouched = Play(reference, 72);

        //Assert
        GmProbe.LargestDifference(afterChange.Left, untouched.Left).Should().Be(0.0);
    }

    [Fact]
    public void a_per_part_synthesizer_sounds_on_any_channel_a_router_puts_it_on()
    {
        //Arrange
        IInstrumentLibrary library = GeneralMidiInstrumentLibrary.Instance;

        IMidiSynthesizer first =
            library.CreateSynthesizer((int)GeneralMidiProgram.Flute, GmProbe.SampleRate);
        IMidiSynthesizer second =
            library.CreateSynthesizer((int)GeneralMidiProgram.Flute, GmProbe.SampleRate);

        //Act
        var onWireZero = Play(first, 67, wireChannel: 0);
        var onWireFifteen = Play(second, 67, wireChannel: 15);

        //Assert
        GmProbe.LargestDifference(onWireZero.Left, onWireFifteen.Left).Should().Be(0.0);
        GmProbe.Rms(onWireZero).Should().BeGreaterThan(0.0005);
    }

    [Fact]
    public void the_percussion_synthesizer_sounds_the_kit_on_any_channel()
    {
        //Arrange
        IInstrumentLibrary library = GeneralMidiInstrumentLibrary.Instance;

        IMidiSynthesizer onNine = library.CreatePercussionSynthesizer(GmProbe.SampleRate);
        IMidiSynthesizer onZero = library.CreatePercussionSynthesizer(GmProbe.SampleRate);

        //Act
        var wireNine = Play(onNine, (int)GeneralMidiPercussion.AcousticSnare, wireChannel: 9);
        var wireZero = Play(onZero, (int)GeneralMidiPercussion.AcousticSnare, wireChannel: 0);

        //Assert
        GmProbe.LargestDifference(wireNine.Left, wireZero.Left).Should().Be(0.0);
        GmProbe.Rms(wireZero).Should().BeGreaterThan(0.0005);
    }

    [Fact]
    public void the_multi_timbral_synthesizer_honours_program_change_and_keeps_the_kit_on_channel_ten()
    {
        //Arrange
        IInstrumentLibrary library = GeneralMidiInstrumentLibrary.Instance;
        IMidiSynthesizer synthesizer = library.CreateMultiTimbralSynthesizer(GmProbe.SampleRate);

        //Act
        synthesizer.ProcessMidiMessage(0, 0xC0, (int)GeneralMidiProgram.Xylophone, 0);

        //Assert
        GeneralMidiSynthesizer typed = synthesizer.Should().BeOfType<GeneralMidiSynthesizer>().Subject;
        typed.IsPinned.Should().BeFalse();
        typed.GetProgram(1).Should().Be((int)GeneralMidiProgram.Xylophone);
        typed.IsPercussionChannel(GeneralMidi.PercussionChannel).Should().BeTrue();
    }

    [Fact]
    public void every_program_and_every_kit_piece_can_be_created_from_the_library()
    {
        //Arrange
        IInstrumentLibrary library = GeneralMidiInstrumentLibrary.Instance;
        int created = 0;

        //Act
        for (int program = 0; program < GeneralMidi.ProgramCount; program++)
        {
            IMidiSynthesizer synthesizer = library.CreateSynthesizer(program, 22050);
            if (synthesizer != null) { created++; }
        }

        //Assert
        created.Should().Be(GeneralMidi.ProgramCount);
        library.CreatePercussionSynthesizer(22050).Should().NotBeNull();
    }

    [Fact]
    public void the_arguments_are_checked()
    {
        //Arrange
        IInstrumentLibrary library = GeneralMidiInstrumentLibrary.Instance;

        //Act
        Action lowProgram = () => library.CreateSynthesizer(-1, GmProbe.SampleRate);
        Action highProgram = () => library.CreateSynthesizer(128, GmProbe.SampleRate);
        Action badRate = () => library.CreateSynthesizer(0, 0);
        Action badPercussionRate = () => library.CreatePercussionSynthesizer(-1);
        Action badMultiRate = () => library.CreateMultiTimbralSynthesizer(0);

        //Assert
        lowProgram.Should().Throw<ArgumentOutOfRangeException>();
        highProgram.Should().Throw<ArgumentOutOfRangeException>();
        badRate.Should().Throw<ArgumentOutOfRangeException>();
        badPercussionRate.Should().Throw<ArgumentOutOfRangeException>();
        badMultiRate.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void a_whole_arrangement_plays_through_the_router_and_renders_offline()
    {
        //Arrange
        IInstrumentLibrary library = GeneralMidiInstrumentLibrary.Instance;

        RoutingSynthesizer router = new RoutingSynthesizer(GmProbe.SampleRate);
        router.MasterVolume = 1f;
        router.SetChannel(1, library.CreateSynthesizer((int)GeneralMidiProgram.Celesta, GmProbe.SampleRate), 0.9F);
        router.SetChannel(2, library.CreateSynthesizer((int)GeneralMidiProgram.ChoirAahs, GmProbe.SampleRate), 0.7F);
        router.SetChannel(GeneralMidi.PercussionChannel, library.CreatePercussionSynthesizer(GmProbe.SampleRate));

        MidiEventCollection events = new MidiEventCollection(1, 480);
        events.AddEvent(new NoteEvent(0, 1, MidiCommandCode.NoteOn, 72, 100), 1);
        events.AddEvent(new NoteEvent(480, 1, MidiCommandCode.NoteOff, 72, 0), 1);
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
            router, MidiSequence.FromEvents(events), TimeSpan.FromSeconds(1.0));

        //Assert
        router.UnroutedMessageCount.Should().Be(0L);
        GmProbe.Peak(rendered).Should().BeGreaterThan(0.02);
        GmProbe.Peak(rendered).Should().BeLessThanOrEqualTo(1.0);
    }

    private static (float[] Left, float[] Right) Play(
        IMidiSynthesizer synthesizer, int key, int wireChannel = 0)
    {
        synthesizer.MasterVolume = 1f;
        synthesizer.ProcessMidiMessage(wireChannel, 0x90, key, 100);

        int frames = synthesizer.SampleRate / 2;
        float[] left = new float[frames];
        float[] right = new float[frames];

        synthesizer.Render(left, right);

        return (left, right);
    }
}
