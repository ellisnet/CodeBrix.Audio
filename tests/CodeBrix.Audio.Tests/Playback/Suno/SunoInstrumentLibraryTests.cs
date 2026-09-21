using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Playback.Suno;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Tests.Instruments;
using CodeBrix.Audio.Tests.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Playback.Suno;

/// <summary>
/// Playing a stems export through a NAMED instrument library, with an instrument of its own for a
/// named stem, and the order the two settle in against everything that was there before them.
/// </summary>
/// <remarks>
/// <para>
/// The registry is PROCESS-WIDE, so every test here registers under a name nobody else uses and
/// asks for that name; none of them reads the default, which belongs to whichever test registered
/// first. The collection is the non-parallel one the rest of the stems tests share.
/// </para>
/// <para>
/// No audio device is opened: an offline render is what builds the synthesizers, and that is how
/// these tests find out which library was asked.
/// </para>
/// </remarks>
[Collection("SunoStems")]
public class SunoInstrumentLibraryTests
{
    private const int RenderRate = 22050;

    private static readonly TimeSpan NoTail = TimeSpan.Zero;

    // ---------------------------------------------------------------------------------------
    // The library, by name
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void every_midi_stem_is_played_by_the_named_librarys_instrument_for_its_own_program()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        var library = Registered("byname");

        //Act
        using var player = song.CreatePlayer(new SunoPlayerOptions { InstrumentLibraryName = library.Name });
        player.Render(RenderRate, NoTail);

        //Assert - the melodic stems asked for their own programs, once each
        var melodic = song.Stems
            .Where(stem => stem.Midi != null && !stem.IsPercussion)
            .Select(stem => stem.GmProgram)
            .OrderBy(program => program);

        library.Programs.OrderBy(program => program).Should().Equal(melodic);
    }

    [Fact]
    public void a_percussion_stem_is_played_by_the_librarys_percussion_synthesizer()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        var library = Registered("percussion");

        //Act
        using var player = song.CreatePlayer(new SunoPlayerOptions { InstrumentLibraryName = library.Name });
        player.Render(RenderRate, NoTail);

        //Assert - the Drums stem is on channel 10 with a kit number for a program, so it must NOT
        // have gone to CreateSynthesizer as program 118
        song["Drums"].IsPercussion.Should().BeTrue();
        library.Percussion.Should().NotBeNull();
        library.Programs.Should().NotContain(song["Drums"].GmProgram);
    }

    [Fact]
    public void a_library_may_be_given_as_an_instance_without_being_registered()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        var library = new RecordingInstrumentLibrary(UniqueName("instance"));

        //Act
        using var player = song.CreatePlayer(new SunoPlayerOptions { InstrumentLibrary = library });
        player.Render(RenderRate, NoTail);

        //Assert
        library.Parts.Should().NotBeEmpty();
        InstrumentLibraryRegistry.IsRegistered(library.Name).Should().BeFalse();
    }

    [Fact]
    public void an_unknown_library_name_is_the_registrys_own_error_when_the_player_is_built()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        Registered("listed");

        //Act
        var building = () => song.CreatePlayer(new SunoPlayerOptions
        {
            InstrumentLibraryName = UniqueName("never-registered"),
        });

        //Assert
        building.Should().Throw<InvalidOperationException>()
            .WithMessage("*is registered*Registered libraries:*");
    }

    [Fact]
    public void a_library_that_does_not_offer_the_per_part_shape_is_refused_by_name()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        var library = new RecordingInstrumentLibrary(
            UniqueName("multitimbral-only"), supportsPerPart: false);
        InstrumentLibraryRegistry.Register(library);

        //Act
        var building = () => song.CreatePlayer(new SunoPlayerOptions
        {
            InstrumentLibraryName = library.Name,
        });

        //Assert
        building.Should().Throw<NotSupportedException>()
            .WithMessage($"*{library.Name}*per-part*");
    }

    [Fact]
    public void a_part_the_library_does_not_cover_is_reported_and_never_thrown()
    {
        //Arrange - a library covering one program only, and no percussion at all
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        var library = new RecordingInstrumentLibrary(
            UniqueName("narrow"),
            coverage: new InstrumentCoverage([song["Bass"].GmProgram], []));
        InstrumentLibraryRegistry.Register(library);

        //Act
        using var player = song.CreatePlayer(new SunoPlayerOptions
        {
            InstrumentLibraryName = library.Name,
        });

        //Assert
        player.Problems.Should().Contain(problem => problem.Contains("'Vocals'") && problem.Contains("silent"));
        player.Problems.Should().Contain(problem => problem.Contains("'Drums'") && problem.Contains("percussion"));
        player.Problems.Should().NotContain(problem => problem.Contains("'Bass'"));
    }

    [Fact]
    public void a_part_whose_notes_fall_outside_the_librarys_range_is_reported()
    {
        //Arrange - the Bass stem's notes are 36 to 38; this library plays that program from 60 up
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        var bass = song["Bass"];
        var coverage = new InstrumentCoverage(
            new[] { new KeyValuePair<int, InstrumentKeyRange>(bass.GmProgram, new InstrumentKeyRange(60, 90)) },
            []);

        var library = new RecordingInstrumentLibrary(UniqueName("high"), coverage: coverage);
        InstrumentLibraryRegistry.Register(library);

        //Act
        using var player = song.CreatePlayer(new SunoPlayerOptions
        {
            InstrumentLibraryName = library.Name,
        });

        //Assert
        bass.LowestNote.Should().BeLessThan(60);
        player.Problems.Should().Contain(problem =>
            problem.Contains("'Bass'") && problem.Contains("outside"));
    }

    // ---------------------------------------------------------------------------------------
    // The per-stem override
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void a_per_stem_instrument_wins_over_the_library_for_that_stem_alone()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        var library = Registered("beaten");
        var asked = new List<int>();

        var options = new SunoPlayerOptions { InstrumentLibraryName = library.Name };
        options.StemInstruments["Bass"] = rate =>
        {
            lock (asked) { asked.Add(rate); }
            return new RecordingSynthesizer(sampleRate: rate);
        };

        //Act
        using var player = song.CreatePlayer(options);
        player.Render(RenderRate, NoTail);

        //Assert
        asked.Should().Equal(RenderRate);
        library.Programs.Should().NotContain(song["Bass"].GmProgram);
        library.Programs.Should().Contain(song["Vocals"].GmProgram);
    }

    [Fact]
    public void two_stems_sharing_a_program_get_different_instruments()
    {
        //Arrange - the fixture's Vocals and Synth are given the same program, which is exactly the
        // case a per-PROGRAM substitution cannot express
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        song["Synth"].GmProgram = song["Vocals"].GmProgram;

        var vocalSynthesizers = new List<RecordingSynthesizer>();
        var synthSynthesizers = new List<RecordingSynthesizer>();

        var options = new SunoPlayerOptions { InstrumentLibrary = new RecordingInstrumentLibrary(UniqueName("base")) };
        options.StemInstruments.Set("Vocals", rate => Remember(vocalSynthesizers, rate));
        options.StemInstruments.Set("Synth", rate => Remember(synthSynthesizers, rate));

        //Act
        using var player = song.CreatePlayer(options);
        player.Render(RenderRate, NoTail);

        //Assert
        vocalSynthesizers.Should().ContainSingle();
        synthSynthesizers.Should().ContainSingle();
        vocalSynthesizers[0].Should().NotBeSameAs(synthSynthesizers[0]);
    }

    [Fact]
    public void a_per_stem_instrument_for_a_stem_the_song_lacks_is_reported()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);

        var options = new SunoPlayerOptions { InstrumentLibrary = new RecordingInstrumentLibrary(UniqueName("lacks")) };
        options.StemInstruments.Set("Theremin", rate => new RecordingSynthesizer(sampleRate: rate));

        //Act
        using var player = song.CreatePlayer(options);

        //Assert
        player.Problems.Should().Contain(problem =>
            problem.Contains("'Theremin'") && problem.Contains("no stem"));
    }

    [Fact]
    public void a_per_stem_instrument_may_be_loaded_from_a_soundfont_file()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);

        var options = new SunoPlayerOptions();
        options.StemInstruments.SetFromFile(
            "Bass", SynthTestAssets.SoundFontPath(SynthTestAssets.TestSoundFontName), soundFontProgram: 0);

        //Act
        using var player = song.CreatePlayer(options);
        var built = options.StemInstruments["Bass"](RenderRate);

        //Assert
        built.Should().BeAssignableTo<IMidiSynthesizer>();
        built.SampleRate.Should().Be(RenderRate);
        Track(player, "Bass").HasMidiSource.Should().BeTrue();
    }

    [Fact]
    public void a_per_stem_instrument_refuses_a_file_it_cannot_load_on_the_line_that_named_it()
    {
        //Arrange
        var instruments = new SunoStemInstruments();

        //Act
        var setting = () => instruments.SetFromFile("Bass", "/nowhere/part.wav");

        //Assert
        setting.Should().Throw<NotSupportedException>().WithMessage("*.dspreset*.sfz*.sf2*");
    }

    // ---------------------------------------------------------------------------------------
    // Precedence, pair by pair
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void the_library_wins_over_an_instrument_factory_and_says_so()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        var library = Registered("over-factory");
        var factoryCalls = 0;

        //Act
        using var player = song.CreatePlayer(new SunoPlayerOptions
        {
            InstrumentLibraryName = library.Name,
            InstrumentFactory = (stem, rate) =>
            {
                factoryCalls++;
                return new RecordingSynthesizer(sampleRate: rate);
            },
        });

        player.Render(RenderRate, NoTail);

        //Assert
        factoryCalls.Should().Be(0);
        library.Parts.Should().NotBeEmpty();
        player.Problems.Should().Contain(problem => problem.Contains("InstrumentFactory"));
    }

    [Fact]
    public void the_library_wins_over_a_general_midi_soundfont_path()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        var library = Registered("over-soundfont");

        //Act
        using var player = song.CreatePlayer(new SunoPlayerOptions
        {
            InstrumentLibraryName = library.Name,
            GeneralMidiSoundFontPath = SynthTestAssets.SoundFontPath(SynthTestAssets.TestSoundFontName),
        });

        player.Render(RenderRate, NoTail);

        //Assert
        library.Parts.Should().NotBeEmpty();
    }

    [Fact]
    public void an_instrument_factory_still_wins_over_a_general_midi_soundfont_path()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        var factoryCalls = 0;

        //Act
        using var player = song.CreatePlayer(new SunoPlayerOptions
        {
            InstrumentFactory = (stem, rate) =>
            {
                factoryCalls++;
                return new RecordingSynthesizer(sampleRate: rate);
            },
            GeneralMidiSoundFontPath = SynthTestAssets.SoundFontPath(SynthTestAssets.TestSoundFontName),
        });

        player.Render(RenderRate, NoTail);

        //Assert
        factoryCalls.Should().BeGreaterThan(0);
        player.Problems.Should().BeEmpty();
    }

    [Fact]
    public void an_instance_wins_over_a_name()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        var named = Registered("named");
        var instance = new RecordingInstrumentLibrary(UniqueName("handed-in"));

        //Act
        using var player = song.CreatePlayer(new SunoPlayerOptions
        {
            InstrumentLibraryName = named.Name,
            InstrumentLibrary = instance,
        });

        player.Render(RenderRate, NoTail);

        //Assert
        instance.Parts.Should().NotBeEmpty();
        named.Parts.Should().BeEmpty();
    }

    [Fact]
    public void with_none_of_the_new_members_set_nothing_is_reported_and_nothing_changes()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);

        //Act
        using var player = song.CreatePlayer(new SunoPlayerOptions());

        //Assert - every track on its recording, no problems, exactly as before
        player.Problems.Should().BeEmpty();
        player.Tracks.Should().OnlyContain(track => track.ActiveSource == TrackSource.Audio);
    }

    // ---------------------------------------------------------------------------------------
    // Which stems play from MIDI
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void a_stem_selection_puts_every_usable_transcription_but_the_named_ones_on_midi()
    {
        //Arrange - the fixture's Synth stem is two notes beside a full-length recording, which is
        // the shape the floors exist for
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        var selection = SunoStemSelection.EverythingBut("Vocals");
        selection.MinimumNoteCount = 5;

        //Act
        using var player = song.CreatePlayer(new SunoPlayerOptions
        {
            InstrumentLibrary = new RecordingInstrumentLibrary(UniqueName("selection")),
            MidiStems = selection,
        });

        //Assert
        Track(player, "Vocals").ActiveSource.Should().Be(TrackSource.Audio);
        Track(player, "Synth").ActiveSource.Should().Be(TrackSource.Audio);
        Track(player, "Drums").ActiveSource.Should().Be(TrackSource.Midi);
        Track(player, "Bass").ActiveSource.Should().Be(TrackSource.Midi);
    }

    [Fact]
    public void a_stem_selection_at_its_default_floors_refuses_every_sparse_transcription()
    {
        //Arrange - no stem of the fixture reaches the default twelve-note floor
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);

        //Act
        using var player = song.CreatePlayer(new SunoPlayerOptions
        {
            InstrumentLibrary = new RecordingInstrumentLibrary(UniqueName("defaults")),
            MidiStems = SunoStemSelection.Everything(),
        });

        //Assert
        song.Stems.Should().OnlyContain(stem => stem.NoteCount < SunoStemSelection.DefaultMinimumNoteCount);
        player.Tracks.Should().OnlyContain(track => track.ActiveSource == TrackSource.Audio);
    }

    [Fact]
    public void a_stem_selection_floor_is_the_consumers_to_state()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        var selection = SunoStemSelection.Everything();
        selection.MinimumNoteCount = 1;
        selection.MinimumMidiCoverage = 0.0;

        //Act
        using var player = song.CreatePlayer(new SunoPlayerOptions
        {
            InstrumentLibrary = new RecordingInstrumentLibrary(UniqueName("floors")),
            MidiStems = selection,
        });

        //Assert - the two-note stem now qualifies
        Track(player, "Synth").ActiveSource.Should().Be(TrackSource.Midi);
    }

    [Fact]
    public void a_stem_selection_naming_only_some_stems_leaves_the_rest_on_their_recordings()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        var selection = SunoStemSelection.Only("Bass");
        selection.MinimumNoteCount = 5;

        //Act
        using var player = song.CreatePlayer(new SunoPlayerOptions
        {
            InstrumentLibrary = new RecordingInstrumentLibrary(UniqueName("only")),
            MidiStems = selection,
        });

        //Assert
        Track(player, "Bass").ActiveSource.Should().Be(TrackSource.Midi);
        Track(player, "Drums").ActiveSource.Should().Be(TrackSource.Audio);
    }

    [Fact]
    public void a_stem_selection_naming_a_stem_the_song_lacks_is_reported()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);

        //Act
        using var player = song.CreatePlayer(new SunoPlayerOptions
        {
            InstrumentLibrary = new RecordingInstrumentLibrary(UniqueName("absent")),
            MidiStems = SunoStemSelection.EverythingBut("Sitar"),
        });

        //Assert
        player.Problems.Should().Contain(problem =>
            problem.Contains("'Sitar'") && problem.Contains("no stem"));
    }

    [Fact]
    public void options_are_snapshotted_so_changing_them_afterwards_does_not_reach_the_player()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var song = Load(temporary);
        var chosen = SunoStemSelection.Only("Bass");
        chosen.MinimumNoteCount = 5;

        var options = new SunoPlayerOptions
        {
            InstrumentLibrary = new RecordingInstrumentLibrary(UniqueName("snapshot")),
            MidiStems = chosen,
        };

        using var player = song.CreatePlayer(options);

        //Act
        options.MidiStems = SunoStemSelection.Everything();
        options.StemInstruments.Set("Drums", rate => new RecordingSynthesizer(sampleRate: rate));

        //Assert
        Track(player, "Drums").ActiveSource.Should().Be(TrackSource.Audio);
        player.Problems.Should().BeEmpty();
    }

    // ---------------------------------------------------------------------------------------

    private static string UniqueName(string hint) => $"SunoTest-{hint}-{Guid.NewGuid():N}";

    private static RecordingInstrumentLibrary Registered(string hint)
    {
        var library = new RecordingInstrumentLibrary(UniqueName(hint));
        InstrumentLibraryRegistry.Register(library);
        return library;
    }

    private static RecordingSynthesizer Remember(List<RecordingSynthesizer> into, int sampleRate)
    {
        var synthesizer = new RecordingSynthesizer(sampleRate: sampleRate);
        lock (into) { into.Add(synthesizer); }
        return synthesizer;
    }

    private static SunoSong Load(TemporaryFolder temporary) =>
        SunoStemsLoader.Load(FakeSongStems.WriteFolder(temporary.Path), new SunoLoadOptions());

    private static PlayerTrack Track(MultiTrackPlayer player, string name) =>
        player.Tracks.Single(track => track.Name == name);
}
