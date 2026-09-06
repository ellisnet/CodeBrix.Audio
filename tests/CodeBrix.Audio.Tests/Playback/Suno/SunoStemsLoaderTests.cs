using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using CodeBrix.Audio.Playback.Suno;
using CodeBrix.Audio.Playback.Suno.Internal;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Playback.Suno;

/// <summary>
/// Loads the synthetic export that <see cref="FakeSongStems"/> writes, in both of its shapes. The
/// tests share one class deliberately: several of them install an alignment estimator into a static
/// seam, and xUnit runs the tests of a class one at a time. The collection keeps them from running
/// beside the other classes that load a song, for the same reason.
/// </summary>
[Collection("SunoStems")]
public class SunoStemsLoaderTests
{
    // ---------------------------------------------------------------------------------------
    // The model
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Load_takes_the_title_from_the_file_names_and_not_from_the_midi_meta()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);

        //Act
        var song = Load(folder);

        //Assert - the meta name is mangled beyond recovery; the file names are the authority
        song.Title.Should().Be(FakeSongStems.Title);
        SunoTitleCodec.IsLossy(song.Title).Should().BeTrue();
    }

    [Fact]
    public void Load_finds_every_stem_and_the_files_it_carries()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);

        //Act
        var song = Load(folder);

        //Assert
        song.Stems.Select(stem => stem.Name).Should()
            .Equal(["Vocals", "Drums", "Bass", "Synth", "FX"]);
        song["Vocals"].HasWav.Should().BeTrue();
        song["Vocals"].HasMp3.Should().BeTrue();
        song["Vocals"].HasMidi.Should().BeTrue();
        song["FX"].HasMidi.Should().BeFalse();
        song["FX"].Midi.Should().BeNull();
        song.AudioStems.Should().HaveCount(5);
        song.MidiStems.Should().HaveCount(4);
    }

    [Fact]
    public void Load_gives_the_same_model_from_a_zip_and_from_a_folder()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        using var cache = new TemporaryFolder();
        var options = new FakeSongOptions { IncludeUnknownStem = true, IncludeShortStem = true };
        var folder = FakeSongStems.WriteFolder(temporary.Path, options);
        var zip = FakeSongStems.WriteZip(temporary.Path, options);

        //Act
        var fromFolder = Load(folder);
        var fromZip = Load(zip, cache.Path);

        //Assert
        fromFolder.Source.Should().Be(SunoSourceKind.Folder);
        fromZip.Source.Should().Be(SunoSourceKind.ZipArchive);
        Describe(fromZip).Should().Be(Describe(fromFolder));
        fromZip.Problems.Should().Equal(fromFolder.Problems.ToArray());
    }

    [Fact]
    public void Load_takes_the_program_and_channel_from_the_midi_when_the_stem_has_one()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);

        //Act
        var song = Load(folder);

        //Assert
        song["Vocals"].GmProgram.Should().Be(54);
        song["Vocals"].Channel.Should().Be(1);
        song["Bass"].GmProgram.Should().Be(32);
        song["Synth"].GmProgram.Should().Be(80);
    }

    [Fact]
    public void Load_puts_a_drum_stem_on_channel_ten_with_its_kit_number()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);

        //Act
        var drums = Load(folder)["Drums"];

        //Assert
        drums.Channel.Should().Be(10);
        drums.GmProgram.Should().Be(118);
        drums.IsPercussion.Should().BeTrue();
    }

    [Fact]
    public void Load_applies_the_vocabulary_defaults_to_a_stem_that_has_no_midi()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);

        //Act
        var fx = Load(folder)["FX"];

        //Assert - "FX" is in the vocabulary, so it starts on the program the exporter uses for it
        fx.HasMidi.Should().BeFalse();
        fx.IsKnownName.Should().BeTrue();
        fx.GmProgram.Should().Be(96);
        fx.Channel.Should().Be(1);
    }

    [Fact]
    public void Load_leaves_a_stem_outside_the_vocabulary_on_the_fallback_program()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path, new FakeSongOptions { IncludeUnknownStem = true });

        //Act
        var kazoo = Load(folder)["Kazoo"];

        //Assert
        kazoo.IsKnownName.Should().BeFalse();
        kazoo.GmProgram.Should().Be(0);
        kazoo.Channel.Should().Be(1);
    }

    [Fact]
    public void Load_measures_the_song_from_the_stem_audio()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);

        //Act
        var song = Load(folder);

        //Assert
        song.Duration.TotalSeconds.Should().BeApproximately(FakeSongStems.SongSeconds, 0.001);
        song["Vocals"].AudioSampleRate.Should().Be(FakeSongStems.SampleRate);
        song["Vocals"].AudioChannels.Should().Be(1);
    }

    [Fact]
    public void Load_counts_the_notes_of_every_midi_stem()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);

        //Act
        var song = Load(folder);

        //Assert
        song["Vocals"].NoteCount.Should().Be(4);
        song["Drums"].NoteCount.Should().Be(8);
        song["Synth"].NoteCount.Should().Be(2);
        song["FX"].NoteCount.Should().Be(0);
    }

    [Fact]
    public void Load_reports_a_near_empty_midi_stem_as_barely_covering_the_song()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);

        //Act
        var song = Load(folder);

        //Assert - a host can see that the Synth MIDI is not worth playing instead of its audio
        song["Vocals"].MidiCoverage.Should().BeApproximately(0.5, 0.01);
        song["Synth"].MidiCoverage.Should().BeApproximately(0.125, 0.01);
        song["FX"].MidiCoverage.Should().Be(0.0);
    }

    [Fact]
    public void Load_measures_zero_length_drum_notes_with_the_minimum_hold()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);

        //Act - eight hits, each written one tick long, each held for 60 ms
        var drums = Load(folder)["Drums"];

        //Assert
        drums.MidiCoverage.Should().BeApproximately(8 * 0.06 / FakeSongStems.SongSeconds, 0.01);
    }

    [Fact]
    public void Load_reads_the_tempo_map_from_a_midi_stem()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);

        //Act
        var song = Load(folder);

        //Assert - one tempo event per beat, as "Follow tempo changes" produces
        song.TempoMap.Should().HaveCount(4);
        song.TempoMap[0].Time.Should().Be(TimeSpan.Zero);
        song.TempoMap[0].BeatsPerMinute.Should().BeApproximately(120.0, 0.001);
        song.TempoMap[1].BeatsPerMinute.Should().BeApproximately(125.0, 0.001);
        song.InitialBeatsPerMinute.Should().BeApproximately(120.0, 0.001);
    }

    [Fact]
    public void Load_finds_the_full_mix_that_sits_beside_the_stems()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);

        //Act
        var song = Load(folder);

        //Assert
        song.HasFullMix.Should().BeTrue();
        Path.GetFileName(song.FullMixPath).Should().Be(FakeSongStems.Title + ".wav");
        song.FullMixPath.Should().Be(song.FullMixWavPath);
    }

    [Fact]
    public void Load_reports_no_full_mix_when_none_was_downloaded()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path, new FakeSongOptions { IncludeFullMix = false });

        //Act
        var song = Load(folder);

        //Assert
        song.HasFullMix.Should().BeFalse();
        song.FullMixPath.Should().BeNull();
    }

    // ---------------------------------------------------------------------------------------
    // Validation
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Load_reports_nothing_about_a_clean_export()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);

        //Act
        var song = Load(folder);

        //Assert - the out-of-range key signature is not a problem for a sequence, which does not model one
        song.Problems.Should().BeEmpty();
        song.Stems.All(stem => stem.Problems.Count == 0).Should().BeTrue();
    }

    [Fact]
    public void Load_reports_a_stem_that_has_midi_and_no_audio()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path, new FakeSongOptions { IncludeMidiOnlyStem = true });

        //Act
        var song = Load(folder);

        //Assert
        song["Brass"].HasAudio.Should().BeFalse();
        Reported(song, "Brass", "no audio file").Should().BeTrue();
    }

    [Fact]
    public void Load_reports_a_midi_file_that_will_not_read_and_keeps_the_rest_of_the_song()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path, new FakeSongOptions { IncludeUnreadableMidi = true });

        //Act
        var song = Load(folder);

        //Assert
        song["Percussion"].Midi.Should().BeNull();
        song["Percussion"].HasWav.Should().BeTrue();
        song["Vocals"].Midi.Should().NotBeNull();
        Reported(song, "Percussion", "could not be read").Should().BeTrue();
    }

    [Fact]
    public void Load_reports_a_stem_whose_audio_is_a_different_length_from_the_song()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path, new FakeSongOptions { IncludeShortStem = true });

        //Act
        var song = Load(folder);

        //Assert
        song["Guitar"].Duration.TotalSeconds.Should().BeApproximately(1.5, 0.001);
        Reported(song, "Guitar", "the song is").Should().BeTrue();
    }

    [Fact]
    public void Load_reports_a_track_name_that_belongs_to_another_song()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path, new FakeSongOptions { IncludeForeignTrackName = true });

        //Act
        var song = Load(folder);

        //Assert - the MIDI is kept; the file name, not the meta event, decides what the song is
        song["Strings"].Midi.Should().NotBeNull();
        Reported(song, "Strings", "does not belong to the song").Should().BeTrue();
    }

    [Fact]
    public void Load_reports_an_unrecognised_stem_name_once_and_loads_it_anyway()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path, new FakeSongOptions { IncludeUnknownStem = true });

        //Act
        var song = Load(folder);

        //Assert
        song["Kazoo"].Should().NotBeNull();
        song.Problems.Count(problem => problem.Contains("Unrecognised stem name")).Should().Be(1);
    }

    [Fact]
    public void Load_reports_a_stem_for_which_no_wav_was_downloaded()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path, new FakeSongOptions { IncludeMp3OnlyStem = true });

        //Act
        var song = Load(folder);

        //Assert
        song["Keyboard"].HasWav.Should().BeFalse();
        song["Keyboard"].Duration.TotalSeconds.Should().BeApproximately(2.0, 0.05);
        song.Problems.Count(problem => problem.Contains("No WAV was downloaded")).Should().Be(1);
    }

    [Fact]
    public void Load_reports_a_file_that_does_not_follow_the_naming_convention()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path, new FakeSongOptions { IncludeStrayFile = true });

        //Act
        var song = Load(folder);

        //Assert
        song.Stems.Should().HaveCount(5);
        song.Problems.Any(problem => problem.Contains("read me first.wav")).Should().BeTrue();
    }

    [Fact]
    public void Load_reports_a_folder_that_holds_no_stems_at_all()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var empty = Directory.CreateDirectory(Path.Combine(temporary.Path, "Nothing Stems")).FullName;

        //Act
        var song = Load(empty);

        //Assert
        song.Stems.Should().BeEmpty();
        song.Title.Should().Be("Nothing");
        song.Problems.Any(problem => problem.Contains("No files following")).Should().BeTrue();
    }

    // ---------------------------------------------------------------------------------------
    // On-demand extraction
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Load_from_a_zip_extracts_nothing_until_a_stem_is_asked_for_its_audio()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        using var cache = new TemporaryFolder();
        var zip = FakeSongStems.WriteZip(temporary.Path);

        //Act
        var song = Load(zip, cache.Path);
        var beforeAsking = Directory.GetFiles(cache.Path).Length;
        var wav = song["Drums"].GetWavPath();

        //Assert
        song.Duration.TotalSeconds.Should().BeApproximately(FakeSongStems.SongSeconds, 0.001);
        beforeAsking.Should().Be(0);
        Directory.GetFiles(cache.Path).Should().HaveCount(1);
        File.Exists(wav).Should().BeTrue();
        new FileInfo(wav).Length.Should().Be(44 + (FakeSongStems.SampleRate * 2 * 2));
    }

    [Fact]
    public void a_zip_entry_that_has_already_been_extracted_is_not_extracted_again()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        using var cache = new TemporaryFolder();
        var zip = FakeSongStems.WriteZip(temporary.Path);
        var song = Load(zip, cache.Path);
        var first = song["Bass"].GetWavPath();
        var written = File.GetLastWriteTimeUtc(first);

        //Act
        var second = song["Bass"].GetWavPath();

        //Assert
        second.Should().Be(first);
        File.GetLastWriteTimeUtc(second).Should().Be(written);
        Directory.GetFiles(cache.Path).Should().HaveCount(1);
    }

    [Fact]
    public void a_second_load_of_the_same_zip_reuses_the_same_cache_folder()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var zip = FakeSongStems.WriteZip(temporary.Path);

        //Act - no cache folder given, so the loader keys one off the zip itself
        var first = Load(zip);
        var second = Load(zip);

        //Assert
        try
        {
            first.CacheFolder.Should().Be(second.CacheFolder);
            first.CacheFolder.StartsWith(SunoStemsLoader.DefaultCacheRoot, StringComparison.Ordinal)
                .Should().BeTrue();
            File.Exists(first["FX"].GetWavPath()).Should().BeTrue();
            File.Exists(second["FX"].GetWavPath()).Should().BeTrue();
        }
        finally
        {
            first.ClearCache();
        }
    }

    [Fact]
    public void ClearCache_removes_what_the_song_extracted()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        using var cache = new TemporaryFolder();
        var zip = FakeSongStems.WriteZip(temporary.Path);
        var song = Load(zip, cache.Path);
        _ = song["Vocals"].GetWavPath();

        //Act
        song.ClearCache();

        //Assert
        Directory.Exists(cache.Path).Should().BeFalse();
        File.Exists(song["Vocals"].GetWavPath()).Should().BeTrue();
    }

    [Fact]
    public void a_song_loaded_into_memory_writes_nothing_and_offers_no_file_path()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var zip = FakeSongStems.WriteZip(temporary.Path);
        var options = new SunoLoadOptions { ZipExtraction = SunoZipExtraction.Memory, MeasureAlignment = false };

        //Act
        var song = SunoStemsLoader.Load(zip, options);

        //Assert
        song.CacheFolder.Should().BeNull();
        song.Duration.TotalSeconds.Should().BeApproximately(FakeSongStems.SongSeconds, 0.001);
        using (var stream = song["Drums"].OpenWav())
        {
            stream.CanSeek.Should().BeTrue();
            stream.Length.Should().Be(44 + (FakeSongStems.SampleRate * 2 * 2));
        }

        Assert.Throws<InvalidOperationException>(() => song["Drums"].GetWavPath());
    }

    [Fact]
    public void OpenAudio_prefers_the_wav_and_says_so()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);
        var song = Load(folder);

        //Act
        var name = song["Vocals"].AudioFileName;

        //Assert
        name.Should().Be(FakeSongStems.FileNameFor("Vocals", ".wav"));
        song["Vocals"].GetAudioPath().Should().Be(song["Vocals"].GetWavPath());
    }

    [Fact]
    public void asking_a_stem_for_a_file_it_does_not_have_says_which_stem_and_which_file()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);
        var song = Load(folder);

        //Act
        var thrown = Assert.Throws<InvalidOperationException>(() => song["FX"].GetMp3Path());

        //Assert
        thrown.Message.Should().Be("The stem 'FX' has no MP3 file.");
    }

    // ---------------------------------------------------------------------------------------
    // Entry points
    // ---------------------------------------------------------------------------------------

    [Fact]
    public async System.Threading.Tasks.Task LoadAsync_returns_what_Load_returns()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);
        var options = new SunoLoadOptions { MeasureAlignment = false };

        //Act
        var song = await SunoStemsLoader.LoadAsync(folder, options, TestContext.Current.CancellationToken);

        //Assert
        Describe(song).Should().Be(Describe(Load(folder)));
    }

    [Fact]
    public void Load_refuses_a_path_that_is_not_there() =>
        Assert.Throws<FileNotFoundException>(() =>
            SunoStemsLoader.Load(Path.Combine(Path.GetTempPath(), "no such song Stems.zip")));

    [Fact]
    public void Load_refuses_a_blank_path() =>
        Assert.Throws<ArgumentException>(() => SunoStemsLoader.Load("  "));

    [Fact]
    public void the_options_a_song_was_loaded_with_are_a_snapshot()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);
        var options = new SunoLoadOptions { MeasureAlignment = false, GeneralMidiSoundFontPath = "/sound/fonts/gm.sf2" };

        //Act
        var song = SunoStemsLoader.Load(folder, options);
        options.GeneralMidiSoundFontPath = "/somewhere/else.sf2";

        //Assert
        song.Options.GeneralMidiSoundFontPath.Should().Be("/sound/fonts/gm.sf2");
        song.Options.Should().NotBeSameAs(options);
    }

    // ---------------------------------------------------------------------------------------
    // The alignment seam
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void no_alignment_is_measured_while_no_estimator_is_installed()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);

        //Act
        SunoSong song;
        try
        {
            SunoAlignmentSeam.Estimator = null;
            song = SunoStemsLoader.Load(folder, new SunoLoadOptions());
        }
        finally
        {
            SunoAlignmentSeam.ResetToDefault();
        }

        //Assert
        song.Stems.All(stem => !stem.AlignmentMeasured).Should().BeTrue();
        song.Stems.All(stem => stem.AlignmentOffset == TimeSpan.Zero).Should().BeTrue();
    }

    [Fact]
    public void the_alignment_estimator_is_asked_about_every_stem_that_has_both_audio_and_midi()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path, new FakeSongOptions { IncludeMidiOnlyStem = true });
        var calls = new List<RecordedCall>();

        //Act
        SunoSong song;
        try
        {
            SunoAlignmentSeam.Estimator = (noteOns, audio, rate, maximum) =>
            {
                lock (calls)
                {
                    calls.Add(new RecordedCall(noteOns.ToArray(), audio.Length, rate, maximum));
                }

                return new SunoAlignmentEstimate(FakeSongStems.PlantedBassOffset.TotalSeconds, 0.9, true);
            };

            song = SunoStemsLoader.Load(folder, new SunoLoadOptions());
        }
        finally
        {
            SunoAlignmentSeam.ResetToDefault();
        }

        //Assert - Vocals, Drums, Bass and Synth have both; FX has no MIDI and Brass has no audio
        calls.Should().HaveCount(4);
        song.Stems.Count(stem => stem.AlignmentMeasured).Should().Be(4);
        song["FX"].AlignmentMeasured.Should().BeFalse();
        song["Brass"].AlignmentMeasured.Should().BeFalse();
        song["Bass"].AlignmentOffset.Should().Be(FakeSongStems.PlantedBassOffset);
        song["Bass"].AlignmentConfidence.Should().BeApproximately(0.9, 1e-9);
        song["Bass"].AlignmentIsReliable.Should().BeTrue();
    }

    [Fact]
    public void the_alignment_estimator_is_handed_the_note_on_times_and_the_stem_audio_as_mono()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);
        RecordedCall bass = null;

        //Act
        try
        {
            SunoAlignmentSeam.Estimator = (noteOns, audio, rate, maximum) =>
            {
                if (noteOns.Count == FakeSongStems.BassNoteOnSeconds.Length &&
                    Math.Abs(noteOns[1] - FakeSongStems.BassNoteOnSeconds[1]) < 0.001)
                {
                    lock (folder)
                    {
                        bass = new RecordedCall(noteOns.ToArray(), audio.Length, rate, maximum);
                    }
                }

                return new SunoAlignmentEstimate(0.0, 0.0, false);
            };

            SunoStemsLoader.Load(folder, new SunoLoadOptions());
        }
        finally
        {
            SunoAlignmentSeam.ResetToDefault();
        }

        //Assert
        bass.Should().NotBeNull();
        bass.SampleRate.Should().Be(FakeSongStems.SampleRate);
        bass.MonoSamples.Should().Be((int)(FakeSongStems.SongSeconds * FakeSongStems.SampleRate));
        bass.MaximumOffsetSeconds.Should().BeApproximately(0.3, 1e-9);
        bass.NoteOnTimes[3].Should().BeApproximately(FakeSongStems.BassNoteOnSeconds[3], 0.001);
    }

    [Fact]
    public void turning_measurement_off_leaves_the_estimator_alone()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);
        var asked = 0;

        //Act
        SunoSong song;
        try
        {
            SunoAlignmentSeam.Estimator = (noteOns, audio, rate, maximum) =>
            {
                Interlocked.Increment(ref asked);
                return new SunoAlignmentEstimate(0.1, 1.0, true);
            };

            song = SunoStemsLoader.Load(folder, new SunoLoadOptions { MeasureAlignment = false });
        }
        finally
        {
            SunoAlignmentSeam.ResetToDefault();
        }

        //Assert
        asked.Should().Be(0);
        song.Stems.All(stem => !stem.AlignmentMeasured).Should().BeTrue();
    }

    [Fact]
    public void the_installed_estimator_recovers_the_planted_offset_through_the_loader()
    {
        //Arrange - the fixture's Bass stem has its audio 40 ms behind its own MIDI
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);

        //Act
        var song = SunoStemsLoader.Load(folder, new SunoLoadOptions());

        //Assert
        var bass = song["Bass"];
        bass.AlignmentMeasured.Should().BeTrue();
        bass.AlignmentIsReliable.Should().BeTrue();
        bass.AlignmentIsFallback.Should().BeFalse();
        bass.MeasuredAlignmentOffset.TotalMilliseconds
            .Should().BeApproximately(FakeSongStems.PlantedBassOffset.TotalMilliseconds, 10.0);
        bass.AlignmentOffset.Should().Be(bass.MeasuredAlignmentOffset);
    }

    [Fact]
    public void the_search_window_is_the_load_options_maximum_by_default()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);

        //Act
        var song = SunoStemsLoader.Load(folder, new SunoLoadOptions());

        //Assert
        song["Bass"].AlignmentWindow.Should().Be(TimeSpan.FromMilliseconds(300));
    }

    [Fact]
    public void the_tempo_aware_window_is_half_an_eighth_note_at_the_songs_slowest_tempo()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);

        //Act
        var song = SunoStemsLoader.Load(folder, new SunoLoadOptions { TempoAwareAlignmentWindow = true });

        //Assert - the fixture's slowest beat is 520,000 microseconds, so an eighth note is 260 ms
        song["Bass"].AlignmentWindow.TotalMilliseconds
            .Should().BeApproximately(FakeSongStems.TempoAwareWindowSeconds * 1000.0, 0.5);
        song["Bass"].MeasuredAlignmentOffset.TotalMilliseconds
            .Should().BeApproximately(FakeSongStems.PlantedBassOffset.TotalMilliseconds, 10.0);
    }

    [Theory]
    [InlineData(120.0, 300, 125)]      // an eighth note is 250 ms; half of it is 125
    [InlineData(40.0, 300, 300)]       // half an eighth note is 375 ms, but the maximum caps it
    [InlineData(400.0, 300, 80)]       // half an eighth note is 37 ms, and the floor lifts it
    [InlineData(400.0, 50, 50)]        // a maximum below the floor wins, because it was asked for
    public void the_tempo_aware_window_is_clamped_at_both_ends(double slowestBeatsPerMinute,
        int maximumMilliseconds, int expectedMilliseconds) =>
        SunoStemsLoader.TempoAwareWindow(slowestBeatsPerMinute, TimeSpan.FromMilliseconds(maximumMilliseconds))
            .TotalMilliseconds.Should().BeApproximately(expectedMilliseconds, 0.001);

    [Fact]
    public void a_stem_whose_own_estimate_is_not_reliable_takes_the_median_of_the_reliable_ones()
    {
        //Arrange - Vocals and Synth have too few notes to be believed; Drums and Bass do not
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);

        //Act
        var song = SunoStemsLoader.Load(folder, new SunoLoadOptions());

        //Assert
        var reliable = song.Stems.Where(stem => stem.AlignmentIsReliable)
            .Select(stem => stem.MeasuredAlignmentOffset.TotalSeconds).OrderBy(value => value).ToList();
        reliable.Should().HaveCountGreaterThan(0);
        var median = reliable.Count % 2 == 1
            ? reliable[reliable.Count / 2]
            : (reliable[(reliable.Count / 2) - 1] + reliable[reliable.Count / 2]) / 2.0;

        var vocals = song["Vocals"];
        vocals.AlignmentMeasured.Should().BeTrue();
        vocals.AlignmentIsReliable.Should().BeFalse();
        vocals.AlignmentIsFallback.Should().BeTrue();
        vocals.AlignmentOffset.TotalSeconds.Should().BeApproximately(median, 1e-6);
        vocals.MeasuredAlignmentOffset.Should().NotBe(vocals.AlignmentOffset);
    }

    [Fact]
    public void a_midi_stem_with_no_audio_of_its_own_takes_the_songs_fallback()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path, new FakeSongOptions { IncludeMidiOnlyStem = true });

        //Act
        var song = SunoStemsLoader.Load(folder, new SunoLoadOptions());

        //Assert
        var brass = song["Brass"];
        brass.HasAudio.Should().BeFalse();
        brass.AlignmentMeasured.Should().BeFalse();
        brass.AlignmentIsFallback.Should().BeTrue();
        brass.AlignmentOffset.Should().Be(song["Vocals"].AlignmentOffset);
    }

    [Fact]
    public void the_fallback_is_zero_when_nothing_was_measured_reliably()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);

        //Act
        SunoSong song;
        try
        {
            SunoAlignmentSeam.Estimator = (noteOns, audio, rate, maximum) =>
                new SunoAlignmentEstimate(0.222, 0.1, false);
            song = SunoStemsLoader.Load(folder, new SunoLoadOptions());
        }
        finally
        {
            SunoAlignmentSeam.ResetToDefault();
        }

        //Assert
        song.Stems.Where(stem => stem.Midi != null).Should()
            .OnlyContain(stem => stem.AlignmentIsFallback && stem.AlignmentOffset == TimeSpan.Zero);
        song["Bass"].MeasuredAlignmentOffset.TotalSeconds.Should().BeApproximately(0.222, 1e-9);
    }

    [Fact]
    public void an_alignment_offset_can_be_overridden_by_the_host()
    {
        //Arrange
        using var temporary = new TemporaryFolder();
        var folder = FakeSongStems.WriteFolder(temporary.Path);
        var song = Load(folder);

        //Act
        song["Drums"].AlignmentOffset = TimeSpan.FromMilliseconds(-120);

        //Assert
        song["Drums"].AlignmentOffset.TotalMilliseconds.Should().Be(-120);
    }

    // ---------------------------------------------------------------------------------------

    private sealed class RecordedCall
    {
        internal RecordedCall(double[] noteOnTimes, int monoSamples, int sampleRate, double maximumOffsetSeconds)
        {
            NoteOnTimes = noteOnTimes;
            MonoSamples = monoSamples;
            SampleRate = sampleRate;
            MaximumOffsetSeconds = maximumOffsetSeconds;
        }

        internal double[] NoteOnTimes { get; }

        internal int MonoSamples { get; }

        internal int SampleRate { get; }

        internal double MaximumOffsetSeconds { get; }
    }

    private static SunoSong Load(string path, string cacheFolder = null) =>
        SunoStemsLoader.Load(path, new SunoLoadOptions
        {
            MeasureAlignment = false,
            CacheFolder = cacheFolder,
        });

    private static bool Reported(SunoSong song, string stemName, string fragment) =>
        song.Problems.Any(problem =>
            problem.Contains($"'{stemName}'", StringComparison.Ordinal) &&
            problem.Contains(fragment, StringComparison.Ordinal));

    /// <summary>Everything about a song that must not depend on which shape it was loaded from.</summary>
    private static string Describe(SunoSong song)
    {
        var lines = new List<string>
        {
            $"title={song.Title}",
            $"duration={song.Duration.TotalSeconds:0.000}",
            $"tempoMap={song.TempoMap.Count}@{song.InitialBeatsPerMinute:0.000}",
            $"fullMix={Path.GetFileName(song.FullMixPath)}",
        };

        foreach (var stem in song.Stems)
        {
            lines.Add(string.Join('|',
                stem.Name,
                stem.WavFileName,
                stem.Mp3FileName,
                stem.MidiFileName,
                stem.GmProgram,
                stem.Channel,
                stem.IsPercussion,
                stem.NoteCount,
                stem.MidiCoverage.ToString("0.0000"),
                stem.Duration.TotalSeconds.ToString("0.000"),
                stem.AudioSampleRate,
                stem.AudioChannels));
        }

        return string.Join('\n', lines);
    }
}
