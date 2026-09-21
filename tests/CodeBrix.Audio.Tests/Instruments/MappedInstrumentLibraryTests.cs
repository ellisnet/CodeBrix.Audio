using System;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.Instruments.Internal;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.Sfz;
using CodeBrix.Audio.Tests.Synth;
using CodeBrix.Audio.Tests.Synth.DecentSampler;
using CodeBrix.Audio.Tests.Synth.Sfz;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests.Instruments;

/// <summary>
/// Covers <see cref="MappedInstrumentLibrary"/> - the library a developer starts from another
/// library and then changes one voice at a time.
/// </summary>
/// <remarks>
/// <para>
/// The base is usually the synthetic <c>codebrix-test.sf2</c>, which holds two melodic presets
/// that genuinely sound different and no drum bank at all: a base whose coverage is PARTIAL, so
/// the coverage arithmetic has something to be wrong about. The substitutes are SFZ and Decent
/// Sampler instruments built in the test, and - where the question is which synthesizer heard
/// what rather than what it sounded like - <see cref="RecordingSynthesizer"/> instances.
/// </para>
/// <para>
/// Every test that registers gives its library a name of its own and resolves BY NAME; none of
/// them reads the registry's default.
/// </para>
/// </remarks>
public class MappedInstrumentLibraryTests
{
    private const int Rate = 44100;

    // ----- identity and construction -----

    [Fact]
    public void the_library_reports_the_name_and_description_it_was_given()
    {
        //Arrange & Act
        var library = new MappedInstrumentLibrary("Named", "A description.");

        //Assert
        library.Name.Should().Be("Named");
        library.Description.Should().Be("A description.");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void the_library_refuses_to_be_built_without_a_name(string name)
    {
        //Act
        var act = () => new MappedInstrumentLibrary(name, "A description.");

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void the_library_refuses_a_null_description()
    {
        //Act
        var act = () => new MappedInstrumentLibrary("Named", null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void the_library_refuses_a_null_base_library_instance()
    {
        //Act
        var act = () => new MappedInstrumentLibrary("Named", "A description.", (IInstrumentLibrary)null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void the_library_refuses_a_blank_base_library_name(string baseLibraryName)
    {
        //Act
        var act = () => new MappedInstrumentLibrary("Named", "A description.", baseLibraryName);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void the_base_library_is_reported_by_name_whichever_way_it_was_given()
    {
        //Arrange
        var instance = SoundFontLibrary("BaseByInstance");

        //Act
        var byInstance = new MappedInstrumentLibrary("A", "A description.", instance);
        var byName = new MappedInstrumentLibrary("B", "A description.", "SomeLibraryName");
        var withNone = new MappedInstrumentLibrary("C", "A description.");

        //Assert
        byInstance.BaseLibraryName.Should().Be("BaseByInstance");
        byInstance.HasBaseLibrary.Should().BeTrue();
        byName.BaseLibraryName.Should().Be("SomeLibraryName");
        byName.HasBaseLibrary.Should().BeTrue();
        withNone.BaseLibraryName.Should().BeNull();
        withNone.HasBaseLibrary.Should().BeFalse();
    }

    // ----- the workflow: start from a library, then swap voices one at a time -----

    [Fact]
    public void the_base_library_plays_every_program_until_an_instrument_is_set()
    {
        //Arrange
        var baseLibrary = SoundFontLibrary();
        var library = new MappedInstrumentLibrary("Untouched", "Nothing set yet.", baseLibrary);

        //Act
        var throughTheMapping = RenderPart(library.CreateSynthesizer(1, Rate));
        var straightFromTheBase = RenderPart(baseLibrary.CreateSynthesizer(1, Rate));

        //Assert
        throughTheMapping.Max(Math.Abs).Should().BeGreaterThan(0.0001f);
        throughTheMapping.Should().Equal(straightFromTheBase);
    }

    [Fact]
    public void setting_one_instrument_changes_only_that_program()
    {
        //Arrange
        var baseLibrary = SoundFontLibrary();
        var library = new MappedInstrumentLibrary("OneVoice", "One voice of my own.", baseLibrary);
        var programZeroBefore = RenderPart(library.CreateSynthesizer(0, Rate));
        var programOneBefore = RenderPart(library.CreateSynthesizer(1, Rate));

        //Act
        library.SetInstrumentFromLibrary(0, baseLibrary, 1);

        //Assert
        var programZeroAfter = RenderPart(library.CreateSynthesizer(0, Rate));
        var programOneAfter = RenderPart(library.CreateSynthesizer(1, Rate));

        programZeroAfter.Should().NotEqual(programZeroBefore);
        programZeroAfter.Should().Equal(programOneBefore);
        programOneAfter.Should().Equal(programOneBefore);
    }

    [Fact]
    public void a_second_instrument_set_later_changes_only_its_own_program()
    {
        //Arrange
        using var sfz = SfzTestInstruments.Create();
        sfz.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var sfzPath = WriteSfz(sfz, "<region> sample=dc.wav loop_mode=loop_continuous");

        var baseLibrary = SoundFontLibrary();
        var library = new MappedInstrumentLibrary("TwoVoices", "Two voices of my own.", baseLibrary);
        library.SetInstrumentFromLibrary(0, baseLibrary, 1);
        var programZeroAfterFirstSwap = RenderPart(library.CreateSynthesizer(0, Rate));

        //Act
        library.SetInstrument(1, sfzPath);

        //Assert
        RenderPart(library.CreateSynthesizer(0, Rate)).Should().Equal(programZeroAfterFirstSwap);
        RenderPart(library.CreateSynthesizer(1, Rate)).Should().NotEqual(programZeroAfterFirstSwap);
        library.SubstitutedPrograms.Should().Equal(0, 1);
    }

    [Fact]
    public void clearing_an_instrument_puts_the_base_library_back()
    {
        //Arrange
        var baseLibrary = SoundFontLibrary();
        var library = new MappedInstrumentLibrary("PutBack", "Changed my mind.", baseLibrary);
        var fromTheBase = RenderPart(library.CreateSynthesizer(0, Rate));
        library.SetInstrumentFromLibrary(0, baseLibrary, 1);

        //Act
        var cleared = library.ClearInstrument(0);

        //Assert
        cleared.Should().BeTrue();
        library.HasSubstitute(0).Should().BeFalse();
        library.ClearInstrument(0).Should().BeFalse();
        RenderPart(library.CreateSynthesizer(0, Rate)).Should().Equal(fromTheBase);
    }

    [Fact]
    public void an_instrument_set_after_the_library_is_registered_is_honoured()
    {
        //Arrange
        var name = $"Mapped-{Guid.NewGuid():N}";
        var baseLibrary = SoundFontLibrary();
        var library = new MappedInstrumentLibrary(name, "Registered first, voiced second.", baseLibrary);
        library.Register();

        //Act
        library.SetInstrumentFromLibrary(0, baseLibrary, 1);

        //Assert
        // Registration is not a freeze: this is the whole workflow, where a developer registers
        // once at start-up and then changes a voice at a time while listening.
        var resolved = InstrumentLibraryRegistry.Resolve(name);
        resolved.Should().BeSameAs(library);
        RenderPart(resolved.CreateSynthesizer(0, Rate))
            .Should().Equal(RenderPart(baseLibrary.CreateSynthesizer(1, Rate)));
    }

    [Fact]
    public void Register_is_a_no_op_the_second_time()
    {
        //Arrange
        var name = $"Mapped-{Guid.NewGuid():N}";
        var library = new MappedInstrumentLibrary(name, "Registered twice.");

        //Act
        library.Register();
        library.Register();

        //Assert
        InstrumentLibraryRegistry.Resolve(name).Should().BeSameAs(library);
    }

    [Fact]
    public void a_program_may_be_re_voiced_again_and_again()
    {
        //Arrange
        var baseLibrary = SoundFontLibrary();
        var library = new MappedInstrumentLibrary("Again", "Listening between changes.", baseLibrary);
        var soundsLikeProgramZero = RenderPart(baseLibrary.CreateSynthesizer(0, Rate));
        var soundsLikeProgramOne = RenderPart(baseLibrary.CreateSynthesizer(1, Rate));

        //Act
        library.SetInstrumentFromLibrary(5, baseLibrary, 0);
        var first = RenderPart(library.CreateSynthesizer(5, Rate));
        library.SetInstrumentFromLibrary(5, baseLibrary, 1);
        var second = RenderPart(library.CreateSynthesizer(5, Rate));

        //Assert
        first.Should().Equal(soundsLikeProgramZero);
        second.Should().Equal(soundsLikeProgramOne);
    }

    // ----- the per-part shape -----

    [Fact]
    public void a_substitute_ignores_a_program_change_and_a_bank_select()
    {
        //Arrange
        var recorder = new RecordingSynthesizer(sampleRate: Rate);
        var library = new MappedInstrumentLibrary("Pinned", "Pinned to its instrument.");
        library.SetInstrument(0, sampleRate => recorder);
        var synthesizer = library.CreateSynthesizer(0, Rate);

        //Act
        synthesizer.ProcessMidiMessage(0, 0xC0, 42, 0);
        synthesizer.ProcessMidiMessage(0, 0xB0, 0, 1);
        synthesizer.ProcessMidiMessage(0, 0xB0, 32, 1);
        synthesizer.ProcessMidiMessage(0, 0x90, 60, 100);

        //Assert
        // A part the developer voiced deliberately must not be re-voiced by the music.
        recorder.Messages.Should().Equal(new RecordedMessage(0, 0x90, 60, 100));
    }

    [Fact]
    public void a_substitute_sounds_on_whichever_channel_the_music_uses()
    {
        //Arrange
        var recorder = new RecordingSynthesizer(sampleRate: Rate);
        var library = new MappedInstrumentLibrary("AnyChannel", "Sounds anywhere.");
        library.SetInstrument(0, sampleRate => recorder);
        var synthesizer = library.CreateSynthesizer(0, Rate);

        //Act
        synthesizer.ProcessMidiMessage(4, 0x90, 60, 100);

        //Assert
        // The router forwards whatever channel the music used, unrenumbered.
        recorder.Messages.Single().Channel.Should().Be(4);
    }

    [Fact]
    public void every_synthesizer_asked_for_is_built_by_the_factory_again()
    {
        //Arrange
        var built = 0;
        var library = new MappedInstrumentLibrary("Counted", "Counting what it builds.");
        library.SetInstrument(
            0,
            sampleRate =>
            {
                built++;
                return new RecordingSynthesizer(sampleRate: sampleRate);
            });

        //Act
        var first = library.CreateSynthesizer(0, Rate);
        var second = library.CreateSynthesizer(0, Rate);

        //Assert
        // Two parts are two synthesizers: one instance in two slots of a router is refused, and
        // two parts sharing one voice pool would steal each other's notes.
        built.Should().Be(2);
        first.Should().NotBeSameAs(second);
    }

    [Fact]
    public void a_substitute_is_built_at_the_sample_rate_it_was_asked_for()
    {
        //Arrange
        var library = new MappedInstrumentLibrary("Rates", "At the rate it is asked for.");
        library.SetInstrument(0, sampleRate => new RecordingSynthesizer(sampleRate: sampleRate));

        //Act
        var synthesizer = library.CreateSynthesizer(0, 22050);

        //Assert
        synthesizer.SampleRate.Should().Be(22050);
    }

    [Fact]
    public void a_factory_that_returns_null_is_refused()
    {
        //Arrange
        var library = new MappedInstrumentLibrary("Null", "A factory that builds nothing.");
        library.SetInstrument(0, sampleRate => null);

        //Act
        var act = () => library.CreateSynthesizer(0, Rate);

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void a_factory_that_returns_another_sample_rate_is_refused()
    {
        //Arrange
        var library = new MappedInstrumentLibrary("WrongRate", "A factory at the wrong rate.");
        library.SetInstrument(0, sampleRate => new RecordingSynthesizer(sampleRate: 22050));

        //Act
        var act = () => library.CreateSynthesizer(0, Rate);

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    // ----- where a substitute comes from -----

    [Fact]
    public void an_instrument_can_be_set_from_an_sfz_file()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var path = WriteSfz(fixture, "<region> sample=dc.wav loop_mode=loop_continuous");
        var library = new MappedInstrumentLibrary("Sfz", "An SFZ of my own.");

        //Act
        library.SetInstrument(0, path);

        //Assert
        RenderPart(library.CreateSynthesizer(0, Rate)).Max(Math.Abs).Should().BeGreaterThan(0.0001f);
    }

    [Fact]
    public void an_instrument_can_be_set_from_a_decent_sampler_preset()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        fixture.WriteWav("Samples/tone.wav", 0.5f, Rate);
        var path = fixture.WritePreset(DecentSamplerTestPresets.MinimalPreset(), "instrument.dspreset");
        var library = new MappedInstrumentLibrary("Pianobook", "A pack I downloaded.");

        //Act
        library.SetInstrument(GeneralMidiProgram.ChoirAahs, path);

        //Assert
        RenderPart(library.CreateSynthesizer((int)GeneralMidiProgram.ChoirAahs, Rate))
            .Max(Math.Abs).Should().BeGreaterThan(0.0001f);
    }

    [Fact]
    public void an_instrument_can_be_set_from_one_program_of_a_soundfont()
    {
        //Arrange
        var library = new MappedInstrumentLibrary("FromSf2", "One patch of a bank.");
        var baseLibrary = SoundFontLibrary();

        //Act
        library.SetInstrumentFromSoundFont(0, SoundFontPath(), 1);

        //Assert
        RenderPart(library.CreateSynthesizer(0, Rate))
            .Should().Equal(RenderPart(baseLibrary.CreateSynthesizer(1, Rate)));
    }

    [Fact]
    public void an_instrument_can_be_set_from_a_registered_library_by_name()
    {
        //Arrange
        var sourceName = $"Source-{Guid.NewGuid():N}";
        var source = SoundFontLibrary(sourceName);
        var library = new MappedInstrumentLibrary("FromNamed", "A program of a named library.");

        //Act
        // Named BEFORE it is registered: the name is resolved when the instrument is first built.
        library.SetInstrumentFromLibrary(0, sourceName, 1);
        source.Register();

        //Assert
        RenderPart(library.CreateSynthesizer(0, Rate))
            .Should().Equal(RenderPart(source.CreateSynthesizer(1, Rate)));
    }

    [Fact]
    public void an_unknown_instrument_file_says_what_it_understands()
    {
        //Arrange
        var library = new MappedInstrumentLibrary("Unknown", "An instrument it cannot load.");

        //Act
        var act = () => library.SetInstrument(0, "/somewhere/instrument.wav");

        //Assert
        act.Should().Throw<NotSupportedException>()
            .WithMessage("*.dspreset*")
            .WithMessage("*.sfz*")
            .WithMessage("*.sf2*");
    }

    [Fact]
    public void a_missing_instrument_file_is_an_error_where_it_was_written()
    {
        //Arrange
        var library = new MappedInstrumentLibrary("Missing", "A path that is wrong.");

        //Act
        var act = () => library.SetInstrument(0, Path.Combine(Path.GetTempPath(), "no-such.sfz"));

        //Assert
        // Loading HERE rather than at play time is deliberate: a typo is an error on the line the
        // developer wrote, not silence in the middle of a piece.
        act.Should().Throw<Exception>();
    }

    // ----- one loaded file, shared -----

    [Fact]
    public void one_loaded_instrument_stands_behind_every_synthesizer_built_from_a_file()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var path = WriteSfz(fixture, "<region> sample=dc.wav loop_mode=loop_continuous");
        var library = new MappedInstrumentLibrary("Shared", "One file behind two voices.");

        //Act
        library.SetInstrument(0, path);
        library.SetInstrument(1, path);

        //Assert
        // Two parts must never mean two copies of the samples in memory.
        var first = SfzInstrumentBehind(library.CreateSynthesizer(0, Rate));
        var second = SfzInstrumentBehind(library.CreateSynthesizer(1, Rate));
        var again = SfzInstrumentBehind(library.CreateSynthesizer(0, Rate));

        first.Should().BeSameAs(second);
        again.Should().BeSameAs(first);
    }

    [Fact]
    public void one_loaded_soundfont_stands_behind_every_program_taken_from_it()
    {
        //Arrange
        var library = new MappedInstrumentLibrary("SharedSf2", "One bank behind two voices.");

        //Act
        library.SetInstrumentFromSoundFont(0, SoundFontPath(), 0);
        library.SetInstrumentFromSoundFont(1, SoundFontPath(), 1);

        //Assert
        var first = SoundFontBehind(library.CreateSynthesizer(0, Rate));
        var second = SoundFontBehind(library.CreateSynthesizer(1, Rate));

        first.Should().BeSameAs(second);
    }

    // ----- coverage -----

    [Fact]
    public void Coverage_is_the_base_library_coverage_until_something_is_set()
    {
        //Arrange
        var baseLibrary = SoundFontLibrary();

        //Act
        var library = new MappedInstrumentLibrary("SameCoverage", "Nothing set yet.", baseLibrary);

        //Assert
        library.Coverage.Programs.Should().Equal(baseLibrary.Coverage.Programs);
        library.Coverage.PercussionNotes.Should().BeEmpty();
    }

    [Fact]
    public void Coverage_adds_a_program_the_base_library_does_not_have()
    {
        //Arrange
        var baseLibrary = SoundFontLibrary();
        var library = new MappedInstrumentLibrary("Added", "A voice the base never had.", baseLibrary);

        //Act
        library.SetInstrument(52, sampleRate => new RecordingSynthesizer(sampleRate: sampleRate));

        //Assert
        baseLibrary.Coverage.CoversProgram(52).Should().BeFalse();
        library.Coverage.CoversProgram(52).Should().BeTrue();
        library.Coverage.Programs.Should().Equal(0, 1, 52);
    }

    [Fact]
    public void Coverage_reports_the_key_range_that_was_given()
    {
        //Arrange
        var baseLibrary = SoundFontLibrary();
        var library = new MappedInstrumentLibrary("Narrow", "A narrowly sampled instrument.", baseLibrary);

        //Act
        library.SetInstrument(
            0,
            sampleRate => new RecordingSynthesizer(sampleRate: sampleRate),
            new InstrumentKeyRange(48, 72));

        //Assert
        // The audition that motivated this: 52 of 284 flute notes were silent because they fell
        // outside a sampled instrument's range. Saying so here is how a rendition avoids it.
        baseLibrary.Coverage.KeyRangeOf(0).Should().Be(InstrumentKeyRange.Full);
        library.Coverage.KeyRangeOf(0).Should().Be(new InstrumentKeyRange(48, 72));
        library.Coverage.CoversNote(0, 60).Should().BeTrue();
        library.Coverage.CoversNote(0, 36).Should().BeFalse();
        library.Coverage.CoversNote(1, 36).Should().BeTrue();
    }

    [Fact]
    public void Coverage_reads_the_key_range_from_an_sfz_instrument()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var path = WriteSfz(
            fixture, "<region> sample=dc.wav lokey=48 hikey=72 loop_mode=loop_continuous");
        var library = new MappedInstrumentLibrary("ReadRange", "Ask the instrument.");

        //Act
        library.SetInstrument(0, path);

        //Assert
        library.Coverage.KeyRangeOf(0).Should().Be(new InstrumentKeyRange(48, 72));
    }

    [Fact]
    public void Coverage_reports_the_whole_keyboard_for_an_instrument_built_in_code()
    {
        //Arrange
        var library = new MappedInstrumentLibrary("AllKeys", "A synthesizer of my own.");

        //Act
        library.SetInstrument(0, sampleRate => new RecordingSynthesizer(sampleRate: sampleRate));

        //Assert
        library.Coverage.KeyRangeOf(0).Should().Be(InstrumentKeyRange.Full);
    }

    [Fact]
    public void Coverage_with_no_base_library_reports_only_what_was_set()
    {
        //Arrange
        var library = new MappedInstrumentLibrary("OnMyOwn", "Only what I set.");

        //Act
        library.SetInstrument(52, sampleRate => new RecordingSynthesizer(sampleRate: sampleRate));

        //Assert
        library.Coverage.Programs.Should().Equal(52);
        library.Coverage.PercussionNotes.Should().BeEmpty();
    }

    [Fact]
    public void Coverage_follows_the_instruments_as_they_are_set_and_cleared()
    {
        //Arrange
        var baseLibrary = SoundFontLibrary();
        var library = new MappedInstrumentLibrary("Following", "Coverage keeps up.", baseLibrary);

        //Act
        library.SetInstrument(52, sampleRate => new RecordingSynthesizer(sampleRate: sampleRate));
        var withIt = library.Coverage.CoversProgram(52);
        library.ClearInstrument(52);

        //Assert
        withIt.Should().BeTrue();
        library.Coverage.CoversProgram(52).Should().BeFalse();
    }

    // ----- the percussion kit -----

    [Fact]
    public void the_percussion_kit_can_be_replaced()
    {
        //Arrange
        var recorder = new RecordingSynthesizer(sampleRate: Rate);
        var baseLibrary = SoundFontLibrary();
        var library = new MappedInstrumentLibrary("MyKit", "A kit of my own.", baseLibrary);

        //Act
        library.SetPercussion(sampleRate => recorder);
        var kit = library.CreatePercussionSynthesizer(Rate);
        kit.ProcessMidiMessage(9, 0x90, 38, 110);

        //Assert
        library.HasPercussionSubstitute.Should().BeTrue();
        recorder.Messages.Should().Equal(new RecordedMessage(9, 0x90, 38, 110));
    }

    [Fact]
    public void the_kit_that_was_set_decides_the_percussion_coverage()
    {
        //Arrange
        var baseLibrary = SoundFontLibrary();
        var library = new MappedInstrumentLibrary("KitCoverage", "A kit of my own.", baseLibrary);

        //Act
        library.SetPercussion(sampleRate => new RecordingSynthesizer(sampleRate: sampleRate), [38, 42]);

        //Assert
        // The fixture SoundFont has no drum bank at all, so every percussion note here came from
        // the kit that was set.
        baseLibrary.Coverage.PercussionNotes.Should().BeEmpty();
        library.Coverage.PercussionNotes.Should().Equal(38, 42);
    }

    [Fact]
    public void a_kit_built_in_code_reports_the_General_MIDI_key_map()
    {
        //Arrange
        var library = new MappedInstrumentLibrary("GmKit", "A kit with nothing said about it.");

        //Act
        library.SetPercussion(sampleRate => new RecordingSynthesizer(sampleRate: sampleRate));

        //Assert
        library.Coverage.PercussionNotes.Should().HaveCount(
            GeneralMidi.HighestPercussionNote - GeneralMidi.LowestPercussionNote + 1);
        library.Coverage.CoversPercussionNote(GeneralMidi.LowestPercussionNote).Should().BeTrue();
    }

    [Fact]
    public void the_kit_can_be_read_from_an_sfz_file()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var path = WriteSfz(
            fixture,
            "<region> sample=dc.wav lokey=36 hikey=36\n<region> sample=dc.wav lokey=38 hikey=38");
        var library = new MappedInstrumentLibrary("SfzKit", "A kit from an SFZ.");

        //Act
        library.SetPercussion(path);

        //Assert
        library.Coverage.PercussionNotes.Should().Equal(36, 38);
    }

    [Fact]
    public void clearing_the_kit_puts_the_base_library_kit_back()
    {
        //Arrange
        var baseLibrary = new RecordingInstrumentLibrary($"Base-{Guid.NewGuid():N}");
        var library = new MappedInstrumentLibrary("KitBack", "Changed my mind about the kit.", baseLibrary);
        library.SetPercussion(sampleRate => new RecordingSynthesizer(sampleRate: sampleRate));

        //Act
        var cleared = library.ClearPercussion();
        var kit = library.CreatePercussionSynthesizer(Rate);

        //Assert
        cleared.Should().BeTrue();
        library.ClearPercussion().Should().BeFalse();
        kit.Should().BeSameAs(baseLibrary.Percussion);
    }

    // ----- what a library with no base can and cannot do -----

    [Fact]
    public void with_no_base_library_an_unset_program_says_what_to_do()
    {
        //Arrange
        var library = new MappedInstrumentLibrary("Sparse", "Only what I set.");
        library.SetInstrument(0, sampleRate => new RecordingSynthesizer(sampleRate: sampleRate));

        //Act
        var act = () => library.CreateSynthesizer(1, Rate);

        //Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*no base library*")
            .WithMessage("*SetInstrument*");
    }

    [Fact]
    public void with_no_base_library_an_unset_kit_says_what_to_do()
    {
        //Arrange
        var library = new MappedInstrumentLibrary("NoKit", "Only what I set.");

        //Act
        var act = () => library.CreatePercussionSynthesizer(Rate);

        //Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*SetPercussion*");
    }

    [Fact]
    public void a_named_base_library_that_is_not_registered_gives_the_registry_error()
    {
        //Arrange
        var missing = $"NotRegistered-{Guid.NewGuid():N}";
        var library = new MappedInstrumentLibrary("Lazy", "A base I have not registered.", missing);

        //Act
        var act = () => library.CreateSynthesizer(0, Rate);

        //Assert
        // The registry's own message, listing what IS registered - not a second message saying the
        // same thing differently.
        act.Should().Throw<InvalidOperationException>().WithMessage($"*{missing}*");
    }

    [Fact]
    public void a_named_base_library_is_resolved_when_it_is_first_used()
    {
        //Arrange
        var baseName = $"LateBase-{Guid.NewGuid():N}";
        var baseLibrary = SoundFontLibrary(baseName);
        var library = new MappedInstrumentLibrary("Late", "Registered after the mapping was built.", baseName);

        //Act
        baseLibrary.Register();

        //Assert
        library.Coverage.Programs.Should().Equal(0, 1);
        RenderPart(library.CreateSynthesizer(1, Rate))
            .Should().Equal(RenderPart(baseLibrary.CreateSynthesizer(1, Rate)));
    }

    // ----- the shapes a library offers -----

    [Fact]
    public void the_shapes_follow_the_base_library()
    {
        //Arrange
        var perPartOnly = new FakeInstrumentLibrary(
            $"PerPartOnly-{Guid.NewGuid():N}", supportsMultiTimbral: false);

        //Act
        var library = new MappedInstrumentLibrary("Shapes", "As the base does.", perPartOnly);

        //Assert
        library.SupportsPerPart.Should().BeTrue();
        library.SupportsMultiTimbral.Should().BeFalse();
    }

    [Fact]
    public void a_library_with_no_base_offers_both_shapes()
    {
        //Arrange & Act
        var library = new MappedInstrumentLibrary("Both", "Only what I set.");

        //Assert
        library.SupportsPerPart.Should().BeTrue();
        library.SupportsMultiTimbral.Should().BeTrue();
    }

    [Fact]
    public void a_base_library_with_no_multi_timbral_shape_is_refused_and_says_what_to_do()
    {
        //Arrange
        var perPartOnly = new FakeInstrumentLibrary(
            $"PerPartOnly-{Guid.NewGuid():N}", supportsMultiTimbral: false);
        var library = new MappedInstrumentLibrary("NoMulti", "As the base does.", perPartOnly);

        //Act
        var act = () => library.CreateMultiTimbralSynthesizer(Rate);

        //Assert
        act.Should().Throw<NotSupportedException>().WithMessage("*RoutingSynthesizer*");
    }

    // ----- the multi-timbral shape -----

    [Fact]
    public void the_multi_timbral_synthesizer_plays_a_program_that_was_set_with_its_own_instrument()
    {
        //Arrange
        var baseLibrary = new RecordingInstrumentLibrary($"Base-{Guid.NewGuid():N}");
        var substitute = new RecordingSynthesizer(sampleRate: Rate);
        var library = new MappedInstrumentLibrary("Inline", "Program changes honoured.", baseLibrary);
        library.SetInstrument(52, sampleRate => substitute);
        var synthesizer = library.CreateMultiTimbralSynthesizer(Rate);

        //Act
        synthesizer.ProcessMidiMessage(0, 0xC0, 52, 0);
        synthesizer.ProcessMidiMessage(0, 0x90, 60, 100);
        synthesizer.ProcessMidiMessage(1, 0x90, 64, 100);

        //Assert
        substitute.Messages.Should().Contain(new RecordedMessage(0, 0x90, 60, 100));
        substitute.Messages.Should().NotContain(new RecordedMessage(1, 0x90, 64, 100));
        baseLibrary.MultiTimbral.Messages.Should().Contain(new RecordedMessage(1, 0x90, 64, 100));
        baseLibrary.MultiTimbral.Messages.Should().NotContain(new RecordedMessage(0, 0x90, 60, 100));
    }

    [Fact]
    public void a_channel_moving_onto_an_instrument_of_your_own_releases_what_the_base_was_holding()
    {
        //Arrange
        var baseLibrary = new RecordingInstrumentLibrary($"Base-{Guid.NewGuid():N}");
        var library = new MappedInstrumentLibrary("NoStuck", "No stuck notes.", baseLibrary);
        library.SetInstrument(52, sampleRate => new RecordingSynthesizer(sampleRate: sampleRate));
        var synthesizer = library.CreateMultiTimbralSynthesizer(Rate);
        synthesizer.ProcessMidiMessage(0, 0xB0, 64, 127);
        synthesizer.ProcessMidiMessage(0, 0x90, 60, 100);

        //Act
        synthesizer.ProcessMidiMessage(0, 0xC0, 52, 0);

        //Assert
        // The pedal is lifted FIRST: a synthesizer that honours it would hold these notes for ever,
        // because nothing is ever going to lift it on a channel this instrument no longer plays.
        var messages = baseLibrary.MultiTimbral.Messages;
        var pedalUp = messages.ToList().FindIndex(message => message.Equals(new RecordedMessage(0, 0xB0, 64, 0)));
        var allNotesOff = messages.ToList().FindIndex(message => message.Equals(new RecordedMessage(0, 0xB0, 123, 0)));

        pedalUp.Should().BeGreaterThanOrEqualTo(0);
        allNotesOff.Should().BeGreaterThan(pedalUp);
    }

    [Fact]
    public void a_channel_moving_back_to_the_base_releases_what_your_instrument_was_holding()
    {
        //Arrange
        var baseLibrary = new RecordingInstrumentLibrary($"Base-{Guid.NewGuid():N}");
        var substitute = new RecordingSynthesizer(sampleRate: Rate);
        var library = new MappedInstrumentLibrary("BackAgain", "And back again.", baseLibrary);
        library.SetInstrument(52, sampleRate => substitute);
        var synthesizer = library.CreateMultiTimbralSynthesizer(Rate);
        synthesizer.ProcessMidiMessage(0, 0xC0, 52, 0);
        synthesizer.ProcessMidiMessage(0, 0x90, 60, 100);

        //Act
        synthesizer.ProcessMidiMessage(0, 0xC0, 0, 0);
        synthesizer.ProcessMidiMessage(0, 0x90, 64, 100);

        //Assert
        substitute.Messages.Should().Contain(new RecordedMessage(0, 0xB0, 123, 0));
        substitute.Messages.Should().NotContain(new RecordedMessage(0, 0x90, 64, 100));
        baseLibrary.MultiTimbral.Messages.Should().Contain(new RecordedMessage(0, 0x90, 64, 100));
    }

    [Fact]
    public void the_controllers_the_music_sent_reach_whichever_instrument_takes_the_channel_over()
    {
        //Arrange
        var baseLibrary = new RecordingInstrumentLibrary($"Base-{Guid.NewGuid():N}");
        var substitute = new RecordingSynthesizer(sampleRate: Rate);
        var library = new MappedInstrumentLibrary("Controllers", "Controllers follow the part.", baseLibrary);
        library.SetInstrument(52, sampleRate => substitute);
        var synthesizer = library.CreateMultiTimbralSynthesizer(Rate);

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 7, 90);     // channel volume
        synthesizer.ProcessMidiMessage(0, 0xB0, 10, 20);    // pan
        synthesizer.ProcessMidiMessage(0, 0xB0, 11, 64);    // expression
        synthesizer.ProcessMidiMessage(0, 0xB0, 64, 127);   // sustain
        synthesizer.ProcessMidiMessage(0, 0xE0, 0, 96);     // pitch bend
        synthesizer.ProcessMidiMessage(0, 0xC0, 52, 0);

        //Assert
        // A part that was mixed and bent keeps its mix and its bend when its instrument changes.
        substitute.Messages.Should().Contain(new RecordedMessage(0, 0xB0, 7, 90));
        substitute.Messages.Should().Contain(new RecordedMessage(0, 0xB0, 10, 20));
        substitute.Messages.Should().Contain(new RecordedMessage(0, 0xB0, 11, 64));
        substitute.Messages.Should().Contain(new RecordedMessage(0, 0xB0, 64, 127));
        substitute.Messages.Should().Contain(new RecordedMessage(0, 0xE0, 0, 96));
    }

    [Fact]
    public void a_parameter_is_selected_before_the_value_that_sets_it_is_replayed()
    {
        //Arrange
        var baseLibrary = new RecordingInstrumentLibrary($"Base-{Guid.NewGuid():N}");
        var substitute = new RecordingSynthesizer(sampleRate: Rate);
        var library = new MappedInstrumentLibrary("Rpn", "The bend range survives.", baseLibrary);
        library.SetInstrument(52, sampleRate => substitute);
        var synthesizer = library.CreateMultiTimbralSynthesizer(Rate);

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 101, 0);    // RPN 0: the pitch bend range
        synthesizer.ProcessMidiMessage(0, 0xB0, 100, 0);
        synthesizer.ProcessMidiMessage(0, 0xB0, 6, 12);     // an octave
        synthesizer.ProcessMidiMessage(0, 0xC0, 52, 0);

        //Assert
        // Replayed in numeric order, the data entry would land on whatever parameter happened to be
        // selected - which is not the one the music chose.
        var replayed = substitute.Messages.ToList();
        var selected = replayed.FindIndex(message => message.Equals(new RecordedMessage(0, 0xB0, 100, 0)));
        var set = replayed.FindIndex(message => message.Equals(new RecordedMessage(0, 0xB0, 6, 12)));

        selected.Should().BeGreaterThanOrEqualTo(0);
        set.Should().BeGreaterThan(selected);
    }

    [Fact]
    public void one_instrument_serves_every_channel_playing_that_program()
    {
        //Arrange
        var built = 0;
        var baseLibrary = new RecordingInstrumentLibrary($"Base-{Guid.NewGuid():N}");
        var library = new MappedInstrumentLibrary("Shared", "One instrument, two parts.", baseLibrary);
        library.SetInstrument(
            52,
            sampleRate =>
            {
                built++;
                return new RecordingSynthesizer(sampleRate: sampleRate);
            });
        var synthesizer = library.CreateMultiTimbralSynthesizer(Rate);

        //Act
        synthesizer.ProcessMidiMessage(0, 0xC0, 52, 0);
        synthesizer.ProcessMidiMessage(1, 0xC0, 52, 0);

        //Assert
        // Two channels on one program are two channels of ONE instrument, exactly as they would be
        // inside the base library's own synthesizer - one engine, one pool, one copy of the audio.
        built.Should().Be(1);
    }

    [Fact]
    public void the_percussion_channel_is_played_by_the_kit_that_was_set()
    {
        //Arrange
        var baseLibrary = new RecordingInstrumentLibrary($"Base-{Guid.NewGuid():N}");
        var kit = new RecordingSynthesizer(sampleRate: Rate);
        var library = new MappedInstrumentLibrary("Drums", "A kit of my own.", baseLibrary);
        library.SetPercussion(sampleRate => kit);
        var synthesizer = library.CreateMultiTimbralSynthesizer(Rate);

        //Act
        synthesizer.ProcessMidiMessage(9, 0x90, 38, 110);
        synthesizer.ProcessMidiMessage(0, 0x90, 60, 100);

        //Assert
        kit.Messages.Should().Equal(new RecordedMessage(9, 0x90, 38, 110));
        baseLibrary.MultiTimbral.Messages.Should().Contain(new RecordedMessage(0, 0x90, 60, 100));
    }

    [Fact]
    public void instruments_set_after_a_synthesizer_was_built_do_not_change_it()
    {
        //Arrange
        var baseLibrary = new RecordingInstrumentLibrary($"Base-{Guid.NewGuid():N}");
        var library = new MappedInstrumentLibrary("Snapshot", "A snapshot at build time.", baseLibrary);
        var synthesizer = library.CreateMultiTimbralSynthesizer(Rate);
        var later = new RecordingSynthesizer(sampleRate: Rate);

        //Act
        library.SetInstrument(52, sampleRate => later);
        synthesizer.ProcessMidiMessage(0, 0xC0, 52, 0);
        synthesizer.ProcessMidiMessage(0, 0x90, 60, 100);

        //Assert
        // A synthesizer that is already playing keeps what it was built with; the NEXT one gets the
        // new voice. That is what makes swapping a voice mid-piece a safe thing to do.
        later.Messages.Should().BeEmpty();
        baseLibrary.MultiTimbral.Messages.Should().Contain(new RecordedMessage(0, 0x90, 60, 100));
    }

    [Fact]
    public void the_multi_timbral_synthesizer_renders_the_base_and_your_instruments_together()
    {
        //Arrange
        var baseLibrary = new RecordingInstrumentLibrary($"Base-{Guid.NewGuid():N}", level: 0.25F);
        var library = new MappedInstrumentLibrary("Mixed", "Mixed together.", baseLibrary);
        library.SetInstrument(52, sampleRate => new RecordingSynthesizer(0.5F, sampleRate));
        var synthesizer = library.CreateMultiTimbralSynthesizer(Rate);
        synthesizer.ProcessMidiMessage(0, 0x90, 60, 100);
        synthesizer.ProcessMidiMessage(1, 0xC0, 52, 0);

        //Act
        var left = new float[64];
        var right = new float[64];
        synthesizer.Render(left, right);

        //Assert
        left.Should().AllSatisfy(sample => sample.Should().BeApproximately(0.75F, 1e-6F));
        right.Should().AllSatisfy(sample => sample.Should().BeApproximately(0.75F, 1e-6F));
    }

    [Fact]
    public void Reset_puts_every_channel_back_on_the_base_library()
    {
        //Arrange
        var baseLibrary = new RecordingInstrumentLibrary($"Base-{Guid.NewGuid():N}");
        var substitute = new RecordingSynthesizer(sampleRate: Rate);
        var library = new MappedInstrumentLibrary("Restart", "Play it again.", baseLibrary);
        library.SetInstrument(52, sampleRate => substitute);
        var synthesizer = library.CreateMultiTimbralSynthesizer(Rate);
        synthesizer.ProcessMidiMessage(0, 0xC0, 52, 0);

        //Act
        synthesizer.Reset();
        synthesizer.ProcessMidiMessage(0, 0x90, 60, 100);

        //Assert
        substitute.ResetCount.Should().Be(1);
        substitute.Messages.Should().NotContain(new RecordedMessage(0, 0x90, 60, 100));
        baseLibrary.MultiTimbral.Messages.Should().Contain(new RecordedMessage(0, 0x90, 60, 100));
    }

    [Fact]
    public void NoteOffAll_reaches_every_instrument_that_has_been_built()
    {
        //Arrange
        var baseLibrary = new RecordingInstrumentLibrary($"Base-{Guid.NewGuid():N}");
        var substitute = new RecordingSynthesizer(sampleRate: Rate);
        var library = new MappedInstrumentLibrary("Silence", "Everything stops.", baseLibrary);
        library.SetInstrument(52, sampleRate => substitute);
        var synthesizer = library.CreateMultiTimbralSynthesizer(Rate);
        synthesizer.ProcessMidiMessage(0, 0xC0, 52, 0);
        synthesizer.ProcessMidiMessage(1, 0x90, 60, 100);

        //Act
        synthesizer.NoteOffAll(immediate: false);

        //Assert
        substitute.NoteOffAllCount.Should().Be(1);
        baseLibrary.MultiTimbral.NoteOffAllCount.Should().Be(1);
    }

    [Fact]
    public void the_multi_timbral_synthesizer_renders_a_real_sequence_offline()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("dc.wav", 1f, Rate, Rate);
        var path = WriteSfz(fixture, "<region> sample=dc.wav loop_mode=loop_continuous");

        var library = new MappedInstrumentLibrary("Offline", "Rendered to a file.", SoundFontLibrary());
        library.SetInstrument(0, path);

        //Act
        var samples = SoundFontRenderer.Render(
            library.CreateMultiTimbralSynthesizer(Rate), BuildNoteSequence(1));

        //Assert
        samples.Max(Math.Abs).Should().BeGreaterThan(0.0001f);
    }

    [Fact]
    public void the_per_part_shape_plays_an_arrangement_through_a_router()
    {
        //Arrange
        var baseLibrary = SoundFontLibrary();
        var library = new MappedInstrumentLibrary("Arranged", "Two parts.", baseLibrary);
        library.SetInstrumentFromSoundFont(0, SoundFontPath(), 1);

        var router = new RoutingSynthesizer(Rate);
        router.SetChannel(1, library.CreateSynthesizer(0, Rate));
        router.SetChannel(2, library.CreateSynthesizer(1, Rate));

        //Act
        var samples = SoundFontRenderer.Render(router, BuildNoteSequence(1));

        //Assert
        // The two compose: this library chooses the SOUND of a program, the router chooses which
        // part plays which sound.
        samples.Max(Math.Abs).Should().BeGreaterThan(0.0001f);
    }

    // ----- argument checks -----

    [Theory]
    [InlineData(-1)]
    [InlineData(128)]
    public void a_program_outside_the_General_MIDI_range_is_refused(int program)
    {
        //Arrange
        var library = new MappedInstrumentLibrary("Range", "A program that does not exist.");

        //Act
        var set = () => library.SetInstrument(
            program, sampleRate => new RecordingSynthesizer(sampleRate: sampleRate));
        var create = () => library.CreateSynthesizer(program, Rate);

        //Assert
        set.Should().Throw<ArgumentOutOfRangeException>();
        create.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void the_creators_reject_a_sample_rate_that_is_not_positive()
    {
        //Arrange
        var library = new MappedInstrumentLibrary("Rates", "A rate that cannot be.", SoundFontLibrary());

        //Act
        var perPart = () => library.CreateSynthesizer(0, 0);
        var percussion = () => library.CreatePercussionSynthesizer(-1);
        var multiTimbral = () => library.CreateMultiTimbralSynthesizer(0);

        //Assert
        perPart.Should().Throw<ArgumentOutOfRangeException>();
        percussion.Should().Throw<ArgumentOutOfRangeException>();
        multiTimbral.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void the_setters_reject_what_they_cannot_use()
    {
        //Arrange
        var library = new MappedInstrumentLibrary("Guards", "Nulls and blanks.");

        //Act
        var nullFactory = () => library.SetInstrument(0, (Func<int, IMidiSynthesizer>)null);
        var blankPath = () => library.SetInstrument(0, "   ");
        var nullLibrary = () => library.SetInstrumentFromLibrary(0, (IInstrumentLibrary)null, 0);
        var blankLibraryName = () => library.SetInstrumentFromLibrary(0, "  ", 0);
        var nullKitFactory = () => library.SetPercussion((Func<int, IMidiSynthesizer>)null);
        var blankKitPath = () => library.SetPercussion("");

        //Assert
        nullFactory.Should().Throw<ArgumentNullException>();
        blankPath.Should().Throw<ArgumentException>();
        nullLibrary.Should().Throw<ArgumentNullException>();
        blankLibraryName.Should().Throw<ArgumentException>();
        nullKitFactory.Should().Throw<ArgumentNullException>();
        blankKitPath.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void a_percussion_note_outside_the_MIDI_range_is_refused()
    {
        //Arrange
        var library = new MappedInstrumentLibrary("Notes", "A note that does not exist.");

        //Act
        var act = () => library.SetPercussion(
            sampleRate => new RecordingSynthesizer(sampleRate: sampleRate), [200]);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void the_General_MIDI_enum_says_the_same_thing_as_the_program_number()
    {
        //Arrange
        var library = new MappedInstrumentLibrary("Enum", "Written the readable way.");

        //Act
        library.SetInstrument(
            GeneralMidiProgram.ChoirAahs, sampleRate => new RecordingSynthesizer(sampleRate: sampleRate));

        //Assert
        library.HasSubstitute(GeneralMidiProgram.ChoirAahs).Should().BeTrue();
        library.HasSubstitute((int)GeneralMidiProgram.ChoirAahs).Should().BeTrue();
        library.ClearInstrument(GeneralMidiProgram.ChoirAahs).Should().BeTrue();
        library.HasSubstitute(GeneralMidiProgram.ChoirAahs).Should().BeFalse();
    }

    // ----- helpers -----

    private static string SoundFontPath() =>
        SynthTestAssets.SoundFontPath(SynthTestAssets.TestSoundFontName);

    private static SoundFontInstrumentLibrary SoundFontLibrary(string name = "MappedTestBase") =>
        new SoundFontInstrumentLibrary(name, "The synthetic test SoundFont.", SoundFontPath());

    private static string WriteSfz(SfzTestInstruments fixture, string text, string fileName = "instrument.sfz")
    {
        fixture.Load(text, fileName);
        return Path.Combine(fixture.Directory, fileName);
    }

    private static SfzInstrument SfzInstrumentBehind(IMidiSynthesizer synthesizer)
    {
        var pinned = synthesizer as PinnedInstrumentSynthesizer;
        var sfz = (pinned == null ? synthesizer : pinned.Inner) as SfzSynthesizer;

        sfz.Should().NotBeNull("the library builds SFZ synthesizers from an .sfz file");
        return sfz.Instrument;
    }

    private static SoundFont SoundFontBehind(IMidiSynthesizer synthesizer)
    {
        var pinned = synthesizer as ProgramPinnedSynthesizer;
        var soundFontSynthesizer = (pinned == null ? synthesizer : pinned.Inner) as SoundFontSynthesizer;

        soundFontSynthesizer.Should().NotBeNull("the library builds SoundFont synthesizers from an .sf2 file");
        return soundFontSynthesizer.SoundFont;
    }

    private static MidiSequence BuildNoteSequence(int channel)
    {
        var collection = new MidiEventCollection(1, 120);
        collection.AddEvent(new NoteOnEvent(0, channel, 60, 100, 240), 1);
        collection.AddEvent(new NoteEvent(240, channel, MidiCommandCode.NoteOff, 60, 0), 1);
        collection.PrepareForExport();
        return MidiSequence.FromEvents(collection);
    }

    // Plays one note on one channel and returns the left channel, which is all a comparison of two
    // voicings needs.
    private static float[] RenderPart(IMidiSynthesizer synthesizer, int channel = 1)
    {
        synthesizer.Reset();
        synthesizer.ProcessMidiMessage(channel - 1, 0x90, 60, 100);

        var left = new float[4096];
        var right = new float[4096];
        synthesizer.Render(left, right);

        return left;
    }
}
