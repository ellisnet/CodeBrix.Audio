using System;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.Instruments.Internal;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Tests.Synth;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests.Instruments;

/// <summary>
/// Covers <see cref="SoundFontInstrumentLibrary"/> over the synthetic <c>codebrix-test.sf2</c>
/// fixture, which holds two melodic presets - patch 0 (looping tones) and patch 1 (one-shot
/// tones), both in bank 0 - and no drum bank at all.
/// </summary>
/// <remarks>
/// That shape is useful rather than limiting: it gives a library whose coverage is genuinely
/// PARTIAL, which is what the coverage report exists for, and two presets that sound different,
/// which is how the per-part pinning is proved.
/// </remarks>
public class SoundFontInstrumentLibraryTests
{
    private const int Rate = 44100;

    private static SoundFontInstrumentLibrary BuildLibrary(string name = "TestSoundFontLibrary") =>
        new SoundFontInstrumentLibrary(
            name,
            "The synthetic test SoundFont.",
            SynthTestAssets.SoundFontPath(SynthTestAssets.TestSoundFontName));

    private static MidiSequence BuildNoteSequence(int channel)
    {
        var collection = new MidiEventCollection(1, 120);
        collection.AddEvent(new NoteOnEvent(0, channel, 60, 100, 240), 1);
        collection.AddEvent(new NoteEvent(240, channel, MidiCommandCode.NoteOff, 60, 0), 1);
        collection.PrepareForExport();
        return MidiSequence.FromEvents(collection);
    }

    private static SoundFont SoundFontBehind(IMidiSynthesizer synthesizer)
    {
        var pinned = synthesizer as ProgramPinnedSynthesizer;
        var soundFontSynthesizer = (pinned == null ? synthesizer : pinned.Inner) as SoundFontSynthesizer;

        soundFontSynthesizer.Should().NotBeNull("the library builds SoundFont synthesizers");
        return soundFontSynthesizer.SoundFont;
    }

    // ----- identity -----

    [Fact]
    public void the_library_reports_the_name_and_description_it_was_given()
    {
        //Arrange & Act
        var library = new SoundFontInstrumentLibrary("Named", "A description.", SoundFontPath());

        //Assert
        library.Name.Should().Be("Named");
        library.Description.Should().Be("A description.");
    }

    [Fact]
    public void the_library_offers_both_shapes()
    {
        //Arrange & Act
        var library = BuildLibrary();

        //Assert
        library.SupportsPerPart.Should().BeTrue();
        library.SupportsMultiTimbral.Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void the_library_refuses_to_be_built_without_a_name(string name)
    {
        //Act
        var act = () => new SoundFontInstrumentLibrary(name, "A description.", SoundFontPath());

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void the_library_can_be_built_from_a_stream()
    {
        //Arrange
        using var stream = File.OpenRead(SoundFontPath());

        //Act
        var library = new SoundFontInstrumentLibrary("FromStream", "Read from a stream.", stream);

        //Assert
        library.SoundFont.Should().NotBeNull();
        library.Coverage.Programs.Should().NotBeEmpty();
    }

    // ----- the shared SoundFont (D30) -----

    [Fact]
    public void every_synthesizer_renders_from_the_one_loaded_soundfont()
    {
        //Arrange
        var library = BuildLibrary();

        //Act
        var firstPart = library.CreateSynthesizer(0, Rate);
        var secondPart = library.CreateSynthesizer(1, Rate);
        var percussion = library.CreatePercussionSynthesizer(Rate);
        var multiTimbral = library.CreateMultiTimbralSynthesizer(Rate);

        //Assert
        // Several parts must never mean several copies of the file in memory: this is the whole
        // reason a 140 MB bank can sit behind the per-part shape.
        SoundFontBehind(firstPart).Should().BeSameAs(library.SoundFont);
        SoundFontBehind(secondPart).Should().BeSameAs(library.SoundFont);
        SoundFontBehind(percussion).Should().BeSameAs(library.SoundFont);
        SoundFontBehind(multiTimbral).Should().BeSameAs(library.SoundFont);
    }

    [Fact]
    public void two_libraries_over_the_same_file_do_not_share_a_soundfont()
    {
        //Arrange & Act
        var first = BuildLibrary("SharingFirst");
        var second = BuildLibrary("SharingSecond");

        //Assert
        // Sharing is WITHIN a library, deliberately: two libraries are two banks as far as a
        // consumer is concerned, even when the bytes happen to be the same.
        first.SoundFont.Should().NotBeSameAs(second.SoundFont);
    }

    // ----- the per-part shape -----

    [Fact]
    public void CreateSynthesizer_builds_a_synthesizer_at_the_requested_sample_rate()
    {
        //Arrange
        var library = BuildLibrary();

        //Act
        var synthesizer = library.CreateSynthesizer(0, 22050);

        //Assert
        synthesizer.SampleRate.Should().Be(22050);
    }

    [Fact]
    public void CreateSynthesizer_plays_its_program_on_every_channel()
    {
        //Arrange
        var library = BuildLibrary();

        //Act
        var onChannelOne = RenderPart(library.CreateSynthesizer(1, Rate), channel: 1);
        var onChannelFive = RenderPart(library.CreateSynthesizer(1, Rate), channel: 5);

        //Assert
        // A part sits on whichever channel the music used, so the binding cannot be applied to
        // channel 1 alone.
        onChannelFive.Max(Math.Abs).Should().BeGreaterThan(0.0001f);
        onChannelFive.Should().Equal(onChannelOne);
    }

    [Fact]
    public void CreateSynthesizer_ignores_a_program_change_in_the_music()
    {
        //Arrange
        var library = BuildLibrary();

        //Act
        var undisturbed = RenderPart(library.CreateSynthesizer(1, Rate), channel: 1);
        var reprogrammed = RenderPart(
            library.CreateSynthesizer(1, Rate), channel: 1, programChangeTo: 0);

        //Assert
        // A voiced part stays voiced: the rendition decided this sound, not the file.
        reprogrammed.Should().Equal(undisturbed);
    }

    [Fact]
    public void the_two_presets_of_the_fixture_really_do_sound_different()
    {
        //Arrange
        var library = BuildLibrary();

        //Act
        var loopingTone = RenderPart(library.CreateSynthesizer(0, Rate), channel: 1);
        var oneShotTone = RenderPart(library.CreateSynthesizer(1, Rate), channel: 1);

        //Assert
        // Without this, the pinning test above would pass over a fixture where every program
        // sounds the same - which would prove nothing at all.
        oneShotTone.Should().NotEqual(loopingTone);
    }

    [Fact]
    public void CreateSynthesizer_survives_a_reset_with_its_program_intact()
    {
        //Arrange
        var library = BuildLibrary();
        var synthesizer = library.CreateSynthesizer(1, Rate);
        var beforeReset = RenderPart(synthesizer, channel: 1, resetFirst: false);

        //Act
        synthesizer.Reset();
        var afterReset = RenderPart(synthesizer, channel: 1, resetFirst: false);

        //Assert
        afterReset.Should().Equal(beforeReset);
    }

    [Fact]
    public void CreatePercussionSynthesizer_builds_a_synthesizer_at_the_requested_sample_rate()
    {
        //Arrange
        var library = BuildLibrary();

        //Act
        var synthesizer = library.CreatePercussionSynthesizer(Rate);

        //Assert
        synthesizer.SampleRate.Should().Be(Rate);
    }

    [Fact]
    public void CreateSynthesizer_rejects_a_program_outside_the_General_MIDI_range()
    {
        //Arrange
        var library = BuildLibrary();

        //Act
        var tooLow = () => library.CreateSynthesizer(-1, Rate);
        var tooHigh = () => library.CreateSynthesizer(128, Rate);

        //Assert
        tooLow.Should().Throw<ArgumentOutOfRangeException>();
        tooHigh.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void the_creators_reject_a_sample_rate_that_is_not_positive()
    {
        //Arrange
        var library = BuildLibrary();

        //Act
        var perPart = () => library.CreateSynthesizer(0, 0);
        var percussion = () => library.CreatePercussionSynthesizer(-1);
        var multiTimbral = () => library.CreateMultiTimbralSynthesizer(0);

        //Assert
        perPart.Should().Throw<ArgumentOutOfRangeException>();
        percussion.Should().Throw<ArgumentOutOfRangeException>();
        multiTimbral.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ----- the multi-timbral shape -----

    [Fact]
    public void CreateMultiTimbralSynthesizer_honours_a_program_change_in_the_music()
    {
        //Arrange
        var library = BuildLibrary();

        //Act
        var asPatchOne = RenderPart(
            library.CreateMultiTimbralSynthesizer(Rate), channel: 1, programChangeTo: 1);
        var asPatchZero = RenderPart(
            library.CreateMultiTimbralSynthesizer(Rate), channel: 1, programChangeTo: 0);

        //Assert
        // The whole point of this shape: the file carries its own instrument assignments.
        asPatchOne.Should().NotEqual(asPatchZero);
    }

    [Fact]
    public void CreateMultiTimbralSynthesizer_renders_a_sequence_offline()
    {
        //Arrange
        var library = BuildLibrary();
        var synthesizer = library.CreateMultiTimbralSynthesizer(Rate);

        //Act
        var samples = SoundFontRenderer.Render(synthesizer, BuildNoteSequence(1));

        //Assert
        samples.Max(Math.Abs).Should().BeGreaterThan(0.0001f);
    }

    // ----- coverage -----

    [Fact]
    public void Coverage_reports_the_presets_the_file_really_holds()
    {
        //Arrange & Act
        var library = BuildLibrary();

        //Assert
        library.Coverage.Programs.Should().Equal(0, 1);
        library.Coverage.CoversProgram(0).Should().BeTrue();
        library.Coverage.CoversProgram(2).Should().BeFalse();
        library.Coverage.CoversProgram(GeneralMidi.ProgramCount - 1).Should().BeFalse();
    }

    [Fact]
    public void Coverage_reports_the_key_range_the_regions_answer_to()
    {
        //Arrange & Act
        var library = BuildLibrary();

        //Assert
        library.Coverage.KeyRangeOf(0).LowestKey.Should().Be(0);
        library.Coverage.KeyRangeOf(0).HighestKey.Should().Be(127);
        library.Coverage.CoversNote(0, 60).Should().BeTrue();
        library.Coverage.KeyRangeOf(2).IsEmpty.Should().BeTrue();
        library.Coverage.CoversNote(2, 60).Should().BeFalse();
    }

    [Fact]
    public void Coverage_reports_no_percussion_for_a_file_with_no_drum_bank()
    {
        //Arrange & Act
        var library = BuildLibrary();

        //Assert
        // Saying so is the point: a rendition can put the drums somewhere else instead of asking
        // for a kit that is not there and getting silence.
        library.Coverage.PercussionNotes.Should().BeEmpty();
        library.Coverage.CoversPercussionNote(GeneralMidi.LowestPercussionNote).Should().BeFalse();
    }

    // ----- registration -----

    [Fact]
    public void Register_puts_the_library_into_the_registry_under_its_own_name()
    {
        //Arrange
        var name = $"SoundFontLibrary-{Guid.NewGuid():N}";
        var library = BuildLibrary(name);

        //Act
        library.Register();
        library.Register();

        //Assert
        InstrumentLibraryRegistry.Resolve(name).Should().BeSameAs(library);
    }

    private static string SoundFontPath() =>
        SynthTestAssets.SoundFontPath(SynthTestAssets.TestSoundFontName);

    // Plays one note on one channel and returns the left channel, which is all a comparison of
    // two voicings needs.
    private static float[] RenderPart(
        IMidiSynthesizer synthesizer,
        int channel,
        int programChangeTo = -1,
        bool resetFirst = true)
    {
        if (resetFirst)
        {
            synthesizer.Reset();
        }

        var wireChannel = channel - 1;

        if (programChangeTo >= 0)
        {
            synthesizer.ProcessMidiMessage(wireChannel, 0xC0, programChangeTo, 0);
        }

        synthesizer.ProcessMidiMessage(wireChannel, 0x90, 60, 100);

        var left = new float[4096];
        var right = new float[4096];
        synthesizer.Render(left, right);

        return left;
    }
}
