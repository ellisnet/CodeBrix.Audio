using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Playback.Internal;
using CodeBrix.Audio.Playback.Suno;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Playback.Suno;

/// <summary>
/// Runs both MIDI readers over real stems exports. Opt-in: point CODEBRIX_AUDIO_SUNO_CORPUS at a
/// folder of "&lt;Title&gt; Stems.zip" downloads and these run; leave it unset and they are skipped.
/// Nothing from the corpus is committed, and nothing is written outside the zips being read - with
/// one exception, the end-to-end test at the bottom, which extracts a song's audio to build a player
/// and deletes what it extracted afterwards.
/// </summary>
[Collection("SunoStems")]
public class SunoCorpusTests
{
    private const string CorpusVariable = "CODEBRIX_AUDIO_SUNO_CORPUS";

    private const string SoundFontVariable = "CODEBRIX_AUDIO_GM_SOUNDFONT";

    private const string SkipReason =
        "Set CODEBRIX_AUDIO_SUNO_CORPUS to a folder of '<Title> Stems.zip' exports to run the corpus tests.";

    private const string SoundFontSkipReason =
        "Set CODEBRIX_AUDIO_GM_SOUNDFONT to a General MIDI SoundFont as well to run the end-to-end test.";

    private static string CorpusFolder => Environment.GetEnvironmentVariable(CorpusVariable);

    private static string SoundFontPath => Environment.GetEnvironmentVariable(SoundFontVariable);

    private static bool CorpusAvailable =>
        !string.IsNullOrWhiteSpace(CorpusFolder) && Directory.Exists(CorpusFolder);

    private static bool SoundFontAvailable =>
        !string.IsNullOrWhiteSpace(SoundFontPath) && File.Exists(SoundFontPath);

    [Fact]
    public void every_exported_midi_loads_through_both_readers_and_matches_its_own_title()
    {
        //Arrange
        Assert.SkipUnless(CorpusAvailable, SkipReason);
        var zips = StemsZips();
        var songs = 0;
        var files = 0;

        //Act
        foreach (var zip in zips)
        {
            songs++;
            var title = TitleOf(zip);
            using var archive = ZipFile.OpenRead(zip);
            foreach (var entry in archive.Entries.Where(IsMidi))
            {
                files++;
                var bytes = ReadEntry(entry);
                var entryTitle = Path.GetFileNameWithoutExtension(entry.Name);

                using var forFile = new MemoryStream(bytes);
                var midiFile = new MidiFile(forFile);

                using var forSequence = new MemoryStream(bytes);
                var sequence = new MidiSequence(forSequence);

                //Assert
                midiFile.Tracks.Should().BeGreaterThan(0);
                sequence.Length.Should().BeGreaterThan(TimeSpan.Zero);

                var trackNames = midiFile.Events
                    .SelectMany(track => track.OfType<TextEvent>())
                    .Where(text => text.MetaEventType == MetaEventType.SequenceTrackName)
                    .ToList();
                trackNames.Should().NotBeEmpty($"{entry.FullName} should carry a track name");
                foreach (var trackName in trackNames)
                {
                    SunoTitleCodec.Matches(trackName.Data, entryTitle).Should()
                        .BeTrue($"the track name in {entry.FullName} should encode '{entryTitle}'");
                    SunoTitleCodec.StartsWith(trackName.Data, title).Should()
                        .BeTrue($"the track name in {entry.FullName} should start with '{title}'");
                }

                var sequenceNames = sequence.TextMetas
                    .Where(meta => meta.Kind == MetaEventType.SequenceTrackName)
                    .ToList();
                sequenceNames.Should().NotBeEmpty();
                foreach (var meta in sequenceNames)
                {
                    SunoTitleCodec.Matches(meta.ToArray(), entryTitle).Should().BeTrue();
                }
            }
        }

        //Assert
        songs.Should().BeGreaterThan(0, $"{CorpusFolder} should hold at least one '* Stems.zip'");
        files.Should().BeGreaterThan(0, "the exports should hold at least one .mid entry");
    }

    [Fact]
    public void the_only_departure_the_exports_contain_is_the_key_signature()
    {
        //Arrange
        Assert.SkipUnless(CorpusAvailable, SkipReason);
        var unexpected = new List<string>();
        var keySignatures = 0;

        //Act
        foreach (var zip in StemsZips())
        {
            using var archive = ZipFile.OpenRead(zip);
            foreach (var entry in archive.Entries.Where(IsMidi))
            {
                using var stream = new MemoryStream(ReadEntry(entry));
                var midiFile = new MidiFile(stream);
                foreach (var problem in midiFile.Problems)
                {
                    if (problem.StartsWith("Key signature event carries", StringComparison.Ordinal))
                    {
                        keySignatures++;
                    }
                    else
                    {
                        unexpected.Add($"{entry.FullName}: {problem}");
                    }
                }
            }
        }

        //Assert
        unexpected.Should().BeEmpty();
        keySignatures.Should().BeGreaterThan(0, "every export seen so far writes an out-of-range key signature");
    }

    [Fact]
    public void every_exported_midi_is_rejected_by_the_strict_reader()
    {
        //Arrange
        Assert.SkipUnless(CorpusAvailable, SkipReason);
        var rejected = 0;
        var accepted = new List<string>();

        //Act
        foreach (var zip in StemsZips())
        {
            using var archive = ZipFile.OpenRead(zip);
            foreach (var entry in archive.Entries.Where(IsMidi))
            {
                var bytes = ReadEntry(entry);
                try
                {
                    using var stream = new MemoryStream(bytes);
                    _ = new MidiFile(stream, MidiReadMode.Strict);
                    accepted.Add(entry.FullName);
                }
                catch (FormatException)
                {
                    rejected++;
                }
            }
        }

        //Assert - the point of the tolerant default: strict reading fails on every one of these
        accepted.Should().BeEmpty();
        rejected.Should().BeGreaterThan(0);
    }

    [Fact]
    public void every_export_loads_from_its_zip_with_nothing_to_report()
    {
        //Arrange
        Assert.SkipUnless(CorpusAvailable, SkipReason);
        var songs = 0;

        //Act
        foreach (var zip in StemsZips())
        {
            var song = SunoStemsLoader.Load(zip, LoadOptions());
            songs++;

            //Assert
            song.Problems.Should().BeEmpty($"'{Path.GetFileName(zip)}' should load cleanly");
            song.Title.Should().Be(TitleOf(zip));
            song.Source.Should().Be(SunoSourceKind.ZipArchive);
            song.Stems.Should().NotBeEmpty();
            song.Duration.Should().BeGreaterThan(TimeSpan.Zero);
            song.TempoMap.Should().NotBeEmpty("every export carries a tempo event per beat");
            song.AudioStems.Should().HaveCount(song.Stems.Count);
        }

        //Assert
        songs.Should().BeGreaterThan(0, $"{CorpusFolder} should hold at least one '* Stems.zip'");
    }

    [Fact]
    public void every_stem_of_every_export_is_a_known_name_carrying_a_full_length_wav()
    {
        //Arrange
        Assert.SkipUnless(CorpusAvailable, SkipReason);
        var stems = 0;

        //Act
        foreach (var zip in StemsZips())
        {
            var song = SunoStemsLoader.Load(zip, LoadOptions());
            foreach (var stem in song.Stems)
            {
                stems++;

                //Assert
                stem.IsKnownName.Should().BeTrue($"'{stem.Name}' should be in the stem vocabulary");
                stem.HasWav.Should().BeTrue("the corpus was downloaded with the WAV option ticked");
                stem.AudioSampleRate.Should().Be(48000);
                stem.AudioChannels.Should().Be(2);
                (song.Duration - stem.Duration).Duration().Should().BeLessThan(TimeSpan.FromMilliseconds(1));

                if (stem.HasMidi)
                {
                    stem.Midi.Should().NotBeNull();
                    stem.GmProgram.Should().BeGreaterThan(-1);
                    stem.Channel.Should().BeGreaterThan(0);
                    if (stem.IsPercussion)
                    {
                        stem.Channel.Should().Be(10);
                    }
                }
            }
        }

        //Assert
        stems.Should().BeGreaterThan(0);
    }

    [Fact]
    public void an_export_extracted_to_a_folder_loads_exactly_as_its_zip_does()
    {
        //Arrange
        Assert.SkipUnless(CorpusAvailable, SkipReason);
        var compared = 0;

        //Act
        foreach (var folder in Directory.EnumerateDirectories(CorpusFolder, "* Stems", SearchOption.TopDirectoryOnly))
        {
            var zip = folder + ".zip";
            if (!File.Exists(zip))
            {
                continue;
            }

            compared++;
            var fromFolder = SunoStemsLoader.Load(folder, LoadOptions());
            var fromZip = SunoStemsLoader.Load(zip, LoadOptions());

            //Assert
            fromFolder.Title.Should().Be(fromZip.Title);
            fromFolder.Duration.Should().Be(fromZip.Duration);
            fromFolder.Stems.Select(stem => stem.Name).Should().Equal(fromZip.Stems.Select(stem => stem.Name).ToArray());
            fromFolder.TempoMap.Count.Should().Be(fromZip.TempoMap.Count);
        }

        //Assert
        compared.Should().BeGreaterThan(0, "the corpus should hold at least one extracted stems folder");
    }

    [Fact]
    public void every_export_builds_a_player_whose_mix_renders_a_bar_without_complaint()
    {
        //Arrange - the second variable is what makes this run: a General MIDI SoundFont to play
        //          the MIDI stems through. It is never shipped and never named in the package.
        Assert.SkipUnless(CorpusAvailable, SkipReason);
        Assert.SkipUnless(SoundFontAvailable, SoundFontSkipReason);
        const int renderRate = 44100;
        var played = 0;

        //Act
        foreach (var zip in StemsZips())
        {
            var song = SunoStemsLoader.Load(zip, LoadOptions());

            //Assert - the export itself first
            song.Problems.Should().BeEmpty();

            try
            {
                using var player = song.CreatePlayer(new SunoPlayerOptions
                {
                    GeneralMidiSoundFontPath = SoundFontPath,
                });

                player.Tracks.Should().HaveCount(song.Stems.Count);
                foreach (var track in player.Tracks)
                {
                    if (track.HasMidiSource)
                    {
                        track.ActiveSource = TrackSource.Midi;
                    }
                }

                // One bar at the song's own tempo, rendered through the internal mix rather than
                // through Render, which would render the whole four minutes.
                var barSeconds = 4.0 * 60.0 / Math.Max(1.0, song.InitialBeatsPerMinute);
                var frames = (int)(barSeconds * renderRate);
                using var mix = new MultiTrackMix(player.Tracks, renderRate, null);
                var buffer = new float[frames * 2];
                mix.Render(buffer);

                buffer.Should().Contain(sample => sample != 0.0f,
                    $"'{song.Title}' should not render a bar of silence");
                buffer.Should().OnlyContain(sample => !float.IsNaN(sample) && !float.IsInfinity(sample));
                played++;
            }
            finally
            {
                song.ClearCache();
            }
        }

        //Assert
        played.Should().BeGreaterThan(0);
    }

    private static SunoLoadOptions LoadOptions() =>
        new SunoLoadOptions { MeasureAlignment = false, FindFullMix = true };

    private static IEnumerable<string> StemsZips() =>
        Directory.EnumerateFiles(CorpusFolder, "* Stems.zip", SearchOption.TopDirectoryOnly).OrderBy(p => p);

    private static string TitleOf(string zipPath)
    {
        var name = Path.GetFileNameWithoutExtension(zipPath);
        return name.EndsWith(" Stems", StringComparison.Ordinal) ? name[..^" Stems".Length] : name;
    }

    private static bool IsMidi(ZipArchiveEntry entry) =>
        entry.Name.EndsWith(".mid", StringComparison.OrdinalIgnoreCase);

    private static byte[] ReadEntry(ZipArchiveEntry entry)
    {
        using var source = entry.Open();
        using var buffer = new MemoryStream();
        source.CopyTo(buffer);
        return buffer.ToArray();
    }
}
