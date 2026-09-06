using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Tests.Utils;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Runs <see cref="MidiAudioAlignment"/> over a folder of real machine-generated stem exports,
/// which is the only way to know whether the estimator works on the material it exists for.
/// Opt-in: point CODEBRIX_AUDIO_SUNO_CORPUS at a folder of "&lt;Title&gt; Stems.zip" files. Without
/// it the tests skip. Nothing from the corpus is ever copied into the repository; entries are
/// extracted to a temporary folder and deleted again.
/// </summary>
/// <remarks>
/// What the corpus showed, and why the assertions are shaped the way they are: drum and
/// percussion stems align confidently and agree with hand measurement to within a few
/// milliseconds. Pitched stems - bass, guitar, pads - often do not align at all, because the
/// transcription's idea of where a note starts is its own. The estimator says so, through
/// <see cref="MidiAudioAlignmentResult.Confidence"/>, and that is the useful behaviour: an
/// unreliable estimate is worth more than a confident wrong one. So the corpus assertion is
/// that MOST rhythmic stems are reliable, not that every stem is.
/// </remarks>
public class MidiAudioAlignmentCorpusTests
{
    private const string CorpusVariable = "CODEBRIX_AUDIO_SUNO_CORPUS";

    private const string SkipReason =
        "Set " + CorpusVariable + " to a folder of stem-export zips to run the corpus alignment tests.";

    private static string CorpusFolder => Environment.GetEnvironmentVariable(CorpusVariable);

    private static bool CorpusAvailable =>
        !string.IsNullOrWhiteSpace(CorpusFolder) && Directory.Exists(CorpusFolder);

    [Fact]
    public void Every_stem_with_both_audio_and_midi_produces_an_estimate_inside_the_window()
    {
        Assert.SkipUnless(CorpusAvailable, SkipReason);

        //Arrange
        var measurements = MeasureCorpus();

        //Assert
        measurements.Should().NotBeEmpty();
        foreach (var measurement in measurements)
        {
            Math.Abs(measurement.Result.OffsetSeconds).Should()
                .BeLessThanOrEqualTo(MidiAudioAlignment.DefaultMaxOffsetSeconds);
            measurement.Result.Confidence.Should().BeInRange(0.0, 1.0);
            measurement.Result.NoteOnCount.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void Most_rhythmic_stems_with_a_real_part_align_reliably()
    {
        Assert.SkipUnless(CorpusAvailable, SkipReason);

        //Arrange
        var rhythmic = MeasureCorpus()
            .Where(m => (m.Stem.Contains("Drum", StringComparison.OrdinalIgnoreCase)
                         || m.Stem.Contains("Percussion", StringComparison.OrdinalIgnoreCase))
                        && m.Result.NoteOnCount > 50)
            .ToList();

        //Act
        int reliable = rhythmic.Count(m => m.Result.IsReliable);

        //Assert
        rhythmic.Should().NotBeEmpty();
        reliable.Should().BeGreaterThan(rhythmic.Count / 2);
    }

    [Fact]
    public void The_estimate_is_the_same_whether_it_is_measured_from_the_wav_or_the_mp3()
    {
        Assert.SkipUnless(CorpusAvailable, SkipReason);

        //Arrange - only some exports include the MP3s; the ones that do are what pin the
        //          reader's gapless trim against real encoder output. Without the trim the two
        //          would differ by the encoder delay plus the decoder's, about 23 ms at 48 kHz.
        var both = MeasureCorpus(includeMp3: true).Where(m => m.Mp3Result != null).ToList();

        //Assert
        both.Should().NotBeEmpty();
        foreach (var measurement in both)
        {
            // A stem the estimator is confident about lands on the same millisecond either way.
            // One it is not confident about is measuring noise, and lossy coding moves noise
            // around; a wider bound is all that can honestly be asked of those.
            double tolerance = measurement.Result.IsReliable ? 0.002 : 0.010;
            measurement.Mp3Result.OffsetSeconds.Should()
                .BeApproximately(measurement.Result.OffsetSeconds, tolerance);
        }
    }

    // ---------------------------------------------------------------------------------------
    // Measurement
    // ---------------------------------------------------------------------------------------

    private sealed class Measurement
    {
        public string Song;
        public string Stem;
        public int NoteCount;
        public bool UsedFallbackReader;
        public MidiAudioAlignmentResult Result;
        public MidiAudioAlignmentResult Mp3Result;
    }

    private static List<Measurement> MeasureCorpus(bool includeMp3 = false)
    {
        var measurements = new List<Measurement>();
        var report = new StringBuilder();
        report.AppendLine("song                          stem              notes   offset ms  peak    conf  reliable");

        foreach (string zipPath in Directory.GetFiles(CorpusFolder, "*Stems.zip").OrderBy(p => p))
        {
            string song = Path.GetFileNameWithoutExtension(zipPath);
            string workFolder = Path.Combine(Path.GetTempPath(),
                "codebrix-audio-corpus-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(workFolder);
            try
            {
                using var archive = ZipFile.OpenRead(zipPath);
                foreach (var midiEntry in archive.Entries
                             .Where(e => e.FullName.EndsWith(".mid", StringComparison.OrdinalIgnoreCase))
                             .OrderBy(e => e.FullName))
                {
                    string audioName = Path.ChangeExtension(midiEntry.FullName, ".wav");
                    var audioEntry = archive.GetEntry(audioName);
                    if (audioEntry == null) { continue; }

                    string midiPath = Extract(midiEntry, workFolder);
                    string audioPath = Extract(audioEntry, workFolder);

                    var noteOnTimes = ReadNoteOnTimes(midiPath, out bool usedFallback);
                    var samples = MidiAudioAlignment.ReadMonoSamples(audioPath, out int sampleRate);

                    var measurement = new Measurement
                    {
                        Song = song,
                        Stem = StemName(midiEntry.FullName),
                        NoteCount = noteOnTimes.Count,
                        UsedFallbackReader = usedFallback,
                        Result = MidiAudioAlignment.Estimate(noteOnTimes, samples, sampleRate)
                    };

                    var mp3Entry = includeMp3
                        ? archive.GetEntry(Path.ChangeExtension(midiEntry.FullName, ".mp3"))
                        : null;
                    if (mp3Entry != null)
                    {
                        string mp3Path = Extract(mp3Entry, workFolder);
                        var mp3Samples = MidiAudioAlignment.ReadMonoSamples(mp3Path, out int mp3Rate);
                        measurement.Mp3Result = MidiAudioAlignment.Estimate(noteOnTimes, mp3Samples, mp3Rate);
                        File.Delete(mp3Path);
                    }

                    measurements.Add(measurement);
                    report.AppendLine(
                        $"{Shorten(song, 28),-28}  {measurement.Stem,-16} {measurement.NoteCount,6} " +
                        $"{measurement.Result.OffsetSeconds * 1000,9:F1}  " +
                        $"{measurement.Result.PeakCorrelation,6:F4}  {measurement.Result.Confidence,4:F2}  " +
                        $"{measurement.Result.IsReliable,-5}" +
                        (usedFallback ? "  (fallback MIDI reader)" : string.Empty) +
                        (measurement.Mp3Result != null
                            ? $"  mp3 {measurement.Mp3Result.OffsetSeconds * 1000,7:F1} ms"
                            : string.Empty));

                    File.Delete(midiPath);
                    File.Delete(audioPath);
                }
            }
            finally
            {
                Directory.Delete(workFolder, true);
            }
        }

        TestContext.Current.TestOutputHelper?.WriteLine(report.ToString());
        return measurements;
    }

    private static string Extract(ZipArchiveEntry entry, string folder)
    {
        string path = Path.Combine(folder, Path.GetFileName(entry.FullName));
        entry.ExtractToFile(path, true);
        return path;
    }

    private static string Shorten(string value, int length) =>
        value.Length <= length ? value : value.Substring(0, length);

    /// <summary>The "(Drums)" part of "Title (Drums).mid", or the file name if there is none.</summary>
    private static string StemName(string entryName)
    {
        string name = Path.GetFileNameWithoutExtension(entryName);
        int open = name.LastIndexOf('(');
        int close = name.LastIndexOf(')');
        return open >= 0 && close > open ? name.Substring(open + 1, close - open - 1) : name;
    }

    /// <summary>
    /// The note-on times of a MIDI file, through the ordinary sequence reader where it can read
    /// the file and through a minimal Standard MIDI File reader where it cannot. Real exports
    /// carry quirks - key signatures outside the specification, meta events with surprising
    /// payloads - and the point of this suite is to measure alignment, not to be stopped by one.
    /// </summary>
    private static IReadOnlyList<double> ReadNoteOnTimes(string path, out bool usedFallback)
    {
        try
        {
            usedFallback = false;
            return MidiAudioAlignment.GetNoteOnTimesSeconds(new MidiSequence(path));
        }
        catch (Exception)
        {
            usedFallback = true;
            return MinimalSmfReader.ReadNoteOnTimesSeconds(File.ReadAllBytes(path));
        }
    }
}
