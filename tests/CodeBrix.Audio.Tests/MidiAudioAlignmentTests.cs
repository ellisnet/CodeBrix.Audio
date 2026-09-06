using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests for <see cref="MidiAudioAlignment"/>: the estimator recovers a planted offset from
/// synthetic material, refuses to be fooled by a decoy that repeats inside the search window,
/// and reports low confidence when there is nothing to find.
/// </summary>
public class MidiAudioAlignmentTests
{
    private const int SampleRate = 48000;

    // Long enough to hold every hit Rhythm(48) produces, with a second to spare.
    private const double RhythmSeconds = 20.0;

    [Theory]
    [InlineData(0.0)]
    [InlineData(0.040)]
    [InlineData(-0.120)]
    [InlineData(0.250)]
    public void Estimate_recovers_a_planted_offset_to_within_two_milliseconds(double plantedOffset)
    {
        //Arrange
        var noteOnTimes = Rhythm(48);
        var audio = ClickTrack(noteOnTimes.Select(t => t + plantedOffset), RhythmSeconds);

        //Act
        var result = MidiAudioAlignment.Estimate(noteOnTimes, audio, SampleRate);

        //Assert
        result.OffsetSeconds.Should().BeApproximately(plantedOffset, 0.002);
        result.IsReliable.Should().BeTrue();
    }

    [Fact]
    public void Estimate_offset_is_positive_when_the_audio_lags_the_midi()
    {
        //Arrange
        var noteOnTimes = Rhythm(48);
        var audio = ClickTrack(noteOnTimes.Select(t => t + 0.080), RhythmSeconds);

        //Act
        var result = MidiAudioAlignment.Estimate(noteOnTimes, audio, SampleRate);

        //Assert
        result.OffsetSeconds.Should().BeGreaterThan(0.0);
    }

    [Fact]
    public void Offset_property_agrees_with_OffsetSeconds()
    {
        //Arrange
        var noteOnTimes = Rhythm(48);
        var audio = ClickTrack(noteOnTimes.Select(t => t + 0.040), RhythmSeconds);

        //Act
        var result = MidiAudioAlignment.Estimate(noteOnTimes, audio, SampleRate);

        //Assert
        result.Offset.TotalSeconds.Should().BeApproximately(result.OffsetSeconds, 1e-6);
    }

    [Fact]
    public void Estimate_prefers_the_true_offset_over_an_alias_a_beat_multiple_away()
    {
        // The transcribed part is one hit every four steps of a 150 ms grid, but the audio also
        // carries a quieter hi-hat on EVERY step. That decoy is what makes a lag of one, two
        // whole grid steps - 150 and 300 ms, both inside the default search window - look
        // plausible: the impulse train lands on real audio onsets there too. The true offset has
        // to win on the size of what it lands on, not merely on landing on something.

        //Arrange
        const double planted = 0.040;
        const double grid = 0.150;
        var noteOnTimes = new List<double>();
        var clicks = new List<(double time, float amplitude)>();
        for (int n = 0; n < 96; n++)
        {
            double time = 0.5 + (n * grid);
            bool isPartOfTheTranscribedPattern = n % 4 == 0;
            if (isPartOfTheTranscribedPattern) { noteOnTimes.Add(time); }
            clicks.Add((time + planted, isPartOfTheTranscribedPattern ? 1.0f : 0.3f));
        }

        //Act
        var result = MidiAudioAlignment.Estimate(noteOnTimes, ClickTrack(clicks, 16.0), SampleRate);

        //Assert
        result.OffsetSeconds.Should().BeApproximately(planted, 0.002);
        result.IsReliable.Should().BeTrue();
    }

    [Fact]
    public void Estimate_reports_one_candidate_peak_when_nothing_rivals_the_winner()
    {
        //Arrange
        var noteOnTimes = Rhythm(48);
        var audio = ClickTrack(noteOnTimes.Select(t => t + 0.040), RhythmSeconds);

        //Act
        var result = MidiAudioAlignment.Estimate(noteOnTimes, audio, SampleRate);

        //Assert
        result.CandidatePeakCount.Should().Be(1);
        result.IsReliable.Should().BeTrue();
    }

    [Fact]
    public void Estimate_counts_the_rival_peaks_a_repeating_pattern_offers_and_trusts_none_of_them()
    {
        // Every hit identical, on a perfectly regular grid, for the whole file: the correlation is
        // then a comb whose teeth are all the same height, and there is no honest way to choose
        // between them. What must NOT happen is a confident answer.

        //Arrange
        const double grid = 0.250;
        var noteOnTimes = Enumerable.Range(0, 60).Select(n => 0.5 + (n * grid)).ToList();

        //Act
        var result = MidiAudioAlignment.Estimate(noteOnTimes, ClickTrack(noteOnTimes, 20.0), SampleRate);

        //Assert
        result.CandidatePeakCount.Should().BeGreaterThan(1);
        result.IsReliable.Should().BeFalse();
    }

    [Fact]
    public void Estimate_uses_the_coarse_shape_of_the_part_to_choose_between_rival_peaks()
    {
        // Two answers land on real onsets: the true one, and a decoy a whole grid step earlier
        // whose hits are LOUDER, so the decoy wins on peak height. The part plays in one-second
        // phrases with a second of rest between them, and the recording is quiet during those
        // rests - which is what tells the two apart once both are smoothed to a quarter second.

        //Arrange
        const double grid = 0.250;
        var noteOnTimes = new List<double>();
        var clicks = new List<(double time, float amplitude)>();
        for (int phrase = 0; phrase < 10; phrase++)
        {
            double start = 0.5 + (phrase * 2.0);
            for (int step = 0; step < 4; step++)
            {
                double time = start + (step * grid);
                noteOnTimes.Add(time);
                clicks.Add((time, 1.0f));

                // The decoy, one grid step early. Not for the first hit of a phrase: that one
                // would land in the rest and give the rest away.
                if (step > 0) { clicks.Add((time - grid, 1.6f)); }
            }
        }

        //Act
        var result = MidiAudioAlignment.Estimate(noteOnTimes, ClickTrack(clicks, 21.0), SampleRate);

        //Assert
        result.CandidatePeakCount.Should().BeGreaterThan(1);
        result.OffsetSeconds.Should().BeApproximately(0.0, 0.010);
    }

    [Fact]
    public void Estimate_reports_low_confidence_when_the_audio_is_noise()
    {
        //Arrange
        var noteOnTimes = Rhythm(48);
        var noise = Noise(RhythmSeconds, 20260906);

        //Act
        var result = MidiAudioAlignment.Estimate(noteOnTimes, noise, SampleRate);

        //Assert
        result.Confidence.Should().BeLessThan(MidiAudioAlignment.ReliableConfidence);
        result.IsReliable.Should().BeFalse();
    }

    [Fact]
    public void Estimate_reports_nothing_when_the_audio_is_silent()
    {
        //Arrange
        var noteOnTimes = Rhythm(48);

        //Act
        var result = MidiAudioAlignment.Estimate(noteOnTimes, new float[SampleRate * 4], SampleRate);

        //Assert
        result.OffsetSeconds.Should().Be(0.0);
        result.Confidence.Should().Be(0.0);
        result.IsReliable.Should().BeFalse();
    }

    [Fact]
    public void Estimate_reports_nothing_when_there_are_no_note_ons()
    {
        //Arrange
        var audio = ClickTrack(new[] { 0.5, 1.0, 1.5 }, 3.0);

        //Act
        var result = MidiAudioAlignment.Estimate(Array.Empty<double>(), audio, SampleRate);

        //Assert
        result.NoteOnCount.Should().Be(0);
        result.IsReliable.Should().BeFalse();
    }

    [Fact]
    public void Estimate_is_not_reliable_with_only_a_handful_of_note_ons()
    {
        //Arrange
        var noteOnTimes = Rhythm(48).Take(4).ToList();
        var audio = ClickTrack(noteOnTimes.Select(t => t + 0.040), RhythmSeconds);

        //Act
        var result = MidiAudioAlignment.Estimate(noteOnTimes, audio, SampleRate);

        //Assert
        result.NoteOnCount.Should().BeLessThan(MidiAudioAlignment.MinimumNoteOnsForReliability);
        result.IsReliable.Should().BeFalse();
    }

    [Fact]
    public void Estimate_narrows_the_search_to_the_requested_window()
    {
        // The planted offset is 250 ms and the window is 50 ms, so the true answer is out of
        // reach. The estimate stays inside the window - and note that it may still come back
        // confident, because confidence is measured against the other lags in the window and a
        // narrow window has few of them. Too small a window is as much of a trap as too large.

        //Arrange
        var noteOnTimes = Rhythm(48);
        var audio = ClickTrack(noteOnTimes.Select(t => t + 0.250), RhythmSeconds);

        //Act
        var result = MidiAudioAlignment.Estimate(noteOnTimes, audio, SampleRate, 0.05);

        //Assert
        Math.Abs(result.OffsetSeconds).Should().BeLessThanOrEqualTo(0.05);
    }

    [Fact]
    public void Estimate_collapses_note_ons_that_land_in_the_same_millisecond()
    {
        //Arrange - a three-note chord is one onset in the audio, not three
        var beats = Rhythm(48);
        var chorded = beats.SelectMany(t => new[] { t, t, t }).ToList();
        var audio = ClickTrack(beats.Select(t => t + 0.040), RhythmSeconds);

        //Act
        var result = MidiAudioAlignment.Estimate(chorded, audio, SampleRate);

        //Assert
        result.NoteOnCount.Should().Be(beats.Count);
        result.OffsetSeconds.Should().BeApproximately(0.040, 0.002);
    }

    [Fact]
    public void Estimate_rejects_a_sample_rate_that_is_not_positive()
    {
        //Arrange
        var audio = new float[1000];

        //Act
        Action act = () => MidiAudioAlignment.Estimate(new[] { 0.1 }, audio, 0);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Estimate_rejects_a_search_window_that_is_out_of_range()
    {
        //Arrange
        var audio = new float[1000];

        //Act
        Action act = () => MidiAudioAlignment.Estimate(new[] { 0.1 }, audio, SampleRate,
            MidiAudioAlignment.MaximumMaxOffsetSeconds + 0.1);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Estimate_rejects_a_null_note_on_list()
    {
        //Arrange
        var audio = new float[1000];

        //Act
        Action act = () => MidiAudioAlignment.Estimate((IReadOnlyList<double>)null, audio, SampleRate);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void EstimateInterleaved_downmixes_stereo_and_recovers_the_same_offset()
    {
        //Arrange
        var noteOnTimes = Rhythm(48);
        var mono = ClickTrack(noteOnTimes.Select(t => t + 0.040), RhythmSeconds);
        var stereo = new float[mono.Length * 2];
        for (int f = 0; f < mono.Length; f++)
        {
            stereo[(f * 2) + 0] = mono[f];
            stereo[(f * 2) + 1] = mono[f] * 0.5f;
        }

        //Act
        var result = MidiAudioAlignment.EstimateInterleaved(noteOnTimes, stereo, 2, SampleRate);

        //Assert
        result.OffsetSeconds.Should().BeApproximately(0.040, 0.002);
        result.IsReliable.Should().BeTrue();
    }

    [Fact]
    public void Estimate_from_a_MidiSequence_recovers_the_planted_offset()
    {
        //Arrange
        var noteOnTimes = Rhythm(48);
        var sequence = BuildSequence(noteOnTimes);
        var audio = ClickTrack(noteOnTimes.Select(t => t + 0.040), RhythmSeconds);

        //Act
        var result = MidiAudioAlignment.Estimate(sequence, audio, SampleRate);

        //Assert
        result.NoteOnCount.Should().Be(noteOnTimes.Count);
        result.OffsetSeconds.Should().BeApproximately(0.040, 0.002);
        result.IsReliable.Should().BeTrue();
    }

    [Fact]
    public void EstimateInterleaved_from_a_MidiSequence_recovers_the_planted_offset()
    {
        //Arrange
        var noteOnTimes = Rhythm(48);
        var sequence = BuildSequence(noteOnTimes);
        var mono = ClickTrack(noteOnTimes.Select(t => t + 0.040), RhythmSeconds);
        var stereo = new float[mono.Length * 2];
        for (int f = 0; f < mono.Length; f++)
        {
            stereo[(f * 2) + 0] = mono[f];
            stereo[(f * 2) + 1] = mono[f];
        }

        //Act
        var result = MidiAudioAlignment.EstimateInterleaved(sequence, stereo, 2, SampleRate);

        //Assert
        result.OffsetSeconds.Should().BeApproximately(0.040, 0.002);
    }

    [Fact]
    public void GetNoteOnTimesSeconds_skips_note_offs_and_zero_velocity_note_ons()
    {
        //Arrange
        var sequence = BuildSequence(new[] { 1.0, 2.0, 3.0 });

        //Act
        var times = MidiAudioAlignment.GetNoteOnTimesSeconds(sequence);

        //Assert
        times.Should().HaveCount(3);
        times[0].Should().BeApproximately(1.0, 0.002);
        times[2].Should().BeApproximately(3.0, 0.002);
    }

    [Fact]
    public void ReadMonoSamples_decodes_a_wav_fixture_to_mono_at_its_own_rate()
    {
        //Arrange
        string path = TestAssets.Path("mp3-gapless-sweep-stereo-44100.wav");

        //Act
        var samples = MidiAudioAlignment.ReadMonoSamples(path, out int sampleRate);

        //Assert
        sampleRate.Should().Be(44100);
        samples.Length.Should().Be(22050);
    }

    [Fact]
    public void ReadMonoSamples_decodes_an_mp3_to_the_same_length_as_its_source_wav()
    {
        //Arrange
        var fromWav = MidiAudioAlignment.ReadMonoSamples(
            TestAssets.Path("mp3-gapless-sweep-stereo-44100.wav"), out int wavRate);

        //Act
        var fromMp3 = MidiAudioAlignment.ReadMonoSamples(
            TestAssets.Path("mp3-gapless-sweep-stereo-44100.mp3"), out int mp3Rate);

        //Assert
        mp3Rate.Should().Be(wavRate);
        fromMp3.Length.Should().Be(fromWav.Length);
    }

    // -----------------------------------------------------------------------------------------
    // Helpers
    // -----------------------------------------------------------------------------------------

    /// <summary>
    /// A rhythm: hits on a 125 ms grid, one to four steps apart, chosen by a fixed seed. It has
    /// to be a rhythm and not a metronome - a note every N milliseconds correlates exactly as
    /// well at a lag of N milliseconds as at the true one, so a steady grid measures nothing
    /// except that the estimator can find A peak.
    /// </summary>
    private static List<double> Rhythm(int count)
    {
        var random = new Random(20260906);
        var times = new List<double>(count);
        double time = 0.5;
        while (times.Count < count)
        {
            times.Add(time);
            time += 0.125 * (1 + random.Next(0, 4));
        }
        return times;
    }

    private static float[] ClickTrack(IEnumerable<double> times, double seconds) =>
        ClickTrack(times.Select(t => (t, 1.0f)), seconds);

    /// <summary>
    /// A short decaying burst at each time. Not an impulse: an impulse has energy in one sample
    /// and the estimator's envelope would see the same thing wherever inside a millisecond it
    /// landed, which would flatter the accuracy the tests are measuring.
    /// </summary>
    private static float[] ClickTrack(IEnumerable<(double time, float amplitude)> clicks, double seconds)
    {
        var buffer = new float[(int)(seconds * SampleRate)];
        int burstLength = SampleRate / 200; // 5 ms
        foreach (var (time, amplitude) in clicks)
        {
            int start = (int)Math.Round(time * SampleRate);
            for (int i = 0; i < burstLength; i++)
            {
                int index = start + i;
                if (index < 0 || index >= buffer.Length) { continue; }
                double decay = 1.0 - ((double)i / burstLength);
                double wave = Math.Sin(2.0 * Math.PI * 1000.0 * i / SampleRate);
                buffer[index] += (float)(amplitude * decay * decay * wave);
            }
        }
        return buffer;
    }

    private static float[] Noise(double seconds, int seed)
    {
        var random = new Random(seed);
        var buffer = new float[(int)(seconds * SampleRate)];
        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = (float)((random.NextDouble() * 2.0) - 1.0) * 0.3f;
        }
        return buffer;
    }

    /// <summary>
    /// A one-track sequence at 120 BPM with a short note at each of the given times, built
    /// through the ordinary MIDI file writer so the times come back out of the tempo map.
    /// </summary>
    private static MidiSequence BuildSequence(IEnumerable<double> noteOnTimesSeconds)
    {
        const int ticksPerQuarterNote = 960;
        const double ticksPerSecond = ticksPerQuarterNote * 2.0; // 120 BPM

        var collection = new MidiEventCollection(0, ticksPerQuarterNote);
        var track = collection.AddTrack();
        track.Add(new TempoEvent(500000, 0)); // 120 bpm
        foreach (double time in noteOnTimesSeconds)
        {
            long tick = (long)Math.Round(time * ticksPerSecond);
            track.Add(new NoteOnEvent(tick, 1, 60, 100, ticksPerQuarterNote / 8));
        }
        collection.PrepareForExport();

        using var stream = new MemoryStream();
        MidiFile.Export(stream, collection, leaveOpen: true);
        stream.Position = 0;
        return new MidiSequence(stream);
    }
}
