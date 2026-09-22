using System;
using System.Threading.Tasks;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Tests.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Level matching: the rate it measures at, that an offline render honours the option at all, and
/// what it reports about the headroom of the mix it has just matched.
/// </summary>
/// <remarks>
/// Nothing here opens an audio device. Every measurement and every render is offline, which is the
/// whole point of several of these tests.
/// </remarks>
public class MultiTrackPlayerLevelMatchTests
{
    // A partial at 14 kHz exists at 44100 Hz (Nyquist 22050) and does not exist at 22050 Hz
    // (Nyquist 11025). It carries almost all of this instrument's energy, so the two rates measure
    // its loudness 25 times apart - which is the defect, in one number.
    private static readonly (double Frequency, float Amplitude)[] BrightPartials =
        [(14000.0, 0.5f), (200.0, 0.02f)];

    // Everything this one plays is well below either rate's Nyquist frequency, so it measures the
    // same at both - the control, and the shape a sampled bank has.
    private static readonly (double Frequency, float Amplitude)[] DarkPartials = [(300.0, 0.5f)];

    // The recording every rendition below is matched against: a constant, so its RMS is its value
    // at any rate. It is the bright instrument's own RMS at 44100 Hz, so a correct measurement
    // there writes a gain of 1.
    private const float RecordingLevel = 0.35384f;

    private const int Frames = 44100;

    // ---------------------------------------------------------------------------------------
    // The defect: measuring below an instrument's energy
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void MeasureRelativeTrackLevels_measures_a_bright_instrument_at_the_rate_it_will_be_heard_at()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = BrightPlayer(song);

        //Act - no rate given, and the player is not prepared, so it measures at the rate an offline
        // render uses. That rate can carry the instrument's energy; 22050 Hz cannot.
        player.MeasureRelativeTrackLevels();

        //Assert
        player["Part"].MidiSourceGain.Should().BeApproximately(1.0f, 0.1f);
        player.IsPrepared.Should().BeFalse();
    }

    [Fact]
    public void MeasureRelativeTrackLevels_at_a_rate_below_the_instruments_energy_overstates_the_gain()
    {
        //Arrange - the reproduction, stated as the defect it is: ask for the old fixed measuring
        // rate and the same instrument measures as almost silent.
        using var song = MultiTrackTestSong.Create();
        using var player = BrightPlayer(song);

        //Act
        player.MeasureRelativeTrackLevels(MultiTrackPlayer.LevelMeasurementSampleRate);

        //Assert
        player["Part"].MidiSourceGain.Should().BeGreaterThan(20.0f);
    }

    [Fact]
    public void MeasureRelativeTrackLevels_measures_an_instrument_within_the_band_the_same_at_either_rate()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var low = DarkPlayer(song);
        using var high = DarkPlayer(song);

        //Act
        low.MeasureRelativeTrackLevels(22050);
        high.MeasureRelativeTrackLevels(48000);

        //Assert - within a quarter of a decibel of each other, which is what "rate-independent"
        // really means for an instrument whose energy is inside both bands.
        var ratio = high["Part"].MidiSourceGain / low["Part"].MidiSourceGain;
        ratio.Should().BeApproximately(1.0f, 0.03f);
    }

    [Fact]
    public void MeasureRelativeTrackLevels_never_measures_below_the_documented_floor()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var floored = DarkPlayer(song);
        using var atTheFloor = DarkPlayer(song);

        //Act
        floored.MeasureRelativeTrackLevels(8000);
        atTheFloor.MeasureRelativeTrackLevels(MultiTrackPlayer.LevelMeasurementSampleRate);

        //Assert
        floored.LastLevelMatch.SampleRate.Should().Be(MultiTrackPlayer.LevelMeasurementSampleRate);
        floored["Part"].MidiSourceGain.Should().BeApproximately(atTheFloor["Part"].MidiSourceGain, 0.001f);
    }

    [Fact]
    public async Task MeasureRelativeTrackLevelsAsync_takes_a_rate_of_its_own()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = BrightPlayer(song);

        //Act
        await player.MeasureRelativeTrackLevelsAsync(48000);

        //Assert
        player["Part"].MidiSourceGain.Should().BeApproximately(1.0f, 0.1f);
    }

    // ---------------------------------------------------------------------------------------
    // The defect: an offline render that silently did not measure
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Render_measures_when_the_option_is_on_and_nothing_prepared_it()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = BrightPlayer(song);
        player.AutoSetRelativeTrackLevels = true;

        //Act - no Prepare, no explicit measurement, no audio device
        player.Render(44100, TimeSpan.Zero);

        //Assert
        player["Part"].MidiSourceGain.Should().NotBe(1.0f);
        player.LastLevelMatch.Should().NotBeNull();
        player.LastLevelMatch.SampleRate.Should().Be(44100);
        player.IsPrepared.Should().BeFalse();
    }

    [Fact]
    public void Render_leaves_every_gain_alone_when_the_option_is_off()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = BrightPlayer(song);

        //Act
        player.Render(44100, TimeSpan.Zero);

        //Assert
        player["Part"].MidiSourceGain.Should().Be(1.0f);
        player.LastLevelMatch.Should().BeNull();
    }

    [Fact]
    public void Render_auto_level_matching_skips_a_track_with_MeasureLevel_off()
    {
        //Arrange - the first part stays on its recording and keeps its hand-set MIDI gain
        using var song = MultiTrackTestSong.Create();
        using var player = MatchedPlayer(song, DarkPartials, 0.6f, parts: 2);
        player["Part 1"].MeasureLevel = false;
        player["Part 1"].MidiSourceGain = 0.75f;
        player.AutoSetRelativeTrackLevels = true;

        //Act
        player.Render(44100, TimeSpan.Zero);

        //Assert - the second part is still measured
        player["Part 1"].MidiSourceGain.Should().Be(0.75f);
        player["Part 2"].MidiSourceGain.Should().BeGreaterThan(1.0f);
        player.LastLevelMatch.MatchedTrackCount.Should().Be(1);
    }

    [Fact]
    public void Render_does_not_measure_a_second_time_over_a_measurement_already_made()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = BrightPlayer(song);
        player.AutoSetRelativeTrackLevels = true;
        player.MeasureRelativeTrackLevels(48000);
        var measured = player["Part"].MidiSourceGain;

        //Act
        player.Render(22050, TimeSpan.Zero);

        //Assert - the render's own low rate did not overwrite what the caller measured
        player["Part"].MidiSourceGain.Should().Be(measured);
        player.LastLevelMatch.SampleRate.Should().Be(48000);
    }

    // ---------------------------------------------------------------------------------------
    // What the measurement reports about headroom
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void MeasureRelativeTrackLevels_reports_the_peak_the_matched_mix_reaches()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = BrightPlayer(song);
        player["Part"].ActiveSource = TrackSource.Midi;

        //Act
        player.MeasureRelativeTrackLevels(44100);
        var rendered = Peak(player.Render(44100, TimeSpan.Zero));

        //Assert - the reported peak is the peak the render really produces
        var match = player.LastLevelMatch;
        match.MatchedTrackCount.Should().Be(1);
        match.MixPeak.Should().BeApproximately(rendered, 0.02f);
        match.SuggestedVolume.Should().BeApproximately(1.0f / match.MixPeak, 0.001f);
    }

    [Fact]
    public void MeasureRelativeTrackLevels_reports_a_mix_that_would_clip_without_changing_it()
    {
        //Arrange - three copies of the same matched part add up well past full scale
        using var song = MultiTrackTestSong.Create();
        using var player = LoudPlayer(song);

        //Act
        player.MeasureRelativeTrackLevels(44100);

        //Assert
        player.LastLevelMatch.WouldClip.Should().BeTrue();
        player.LastLevelMatch.SuggestedVolume.Should().BeLessThan(1.0f);
        player.Volume.Should().Be(1.0f);
        Peak(player.Render(44100, TimeSpan.Zero)).Should().BeGreaterThan(1.0f);
    }

    [Fact]
    public void FitVolumeAfterLevelMatching_sets_the_volume_that_lands_the_mix_at_full_scale()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = LoudPlayer(song);
        player.FitVolumeAfterLevelMatching = true;

        //Act
        player.MeasureRelativeTrackLevels(44100);

        //Assert
        player.Volume.Should().Be(player.LastLevelMatch.SuggestedVolume);
        Peak(player.Render(44100, TimeSpan.Zero)).Should().BeApproximately(1.0f, 0.03f);
    }

    [Fact]
    public void MeasureRelativeTrackLevels_reports_nothing_when_no_track_holds_both_sources()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = new MultiTrackPlayer();
        player.Add(new AudioTrack(song.WriteConstantWav("solo.wav", 0.5f, 0.5f, Frames), "Solo"));

        //Act
        player.MeasureRelativeTrackLevels(44100);

        //Assert
        player.LastLevelMatch.Should().BeNull();
        player["Solo"].MidiSourceGain.Should().Be(1.0f);
    }

    // ---------------------------------------------------------------------------------------

    private static MultiTrackPlayer BrightPlayer(MultiTrackTestSong song) =>
        MatchedPlayer(song, BrightPartials, RecordingLevel, parts: 1);

    private static MultiTrackPlayer DarkPlayer(MultiTrackTestSong song) =>
        MatchedPlayer(song, DarkPartials, RecordingLevel, parts: 1);

    private static MultiTrackPlayer LoudPlayer(MultiTrackTestSong song) =>
        MatchedPlayer(song, DarkPartials, 0.6f, parts: 3);

    // One track per part, each holding a constant recording and a rendition of the same length.
    private static MultiTrackPlayer MatchedPlayer(
        MultiTrackTestSong song,
        (double Frequency, float Amplitude)[] partials,
        float recordingLevel,
        int parts)
    {
        var player = new MultiTrackPlayer();

        try
        {
            for (var i = 0; i < parts; i++)
            {
                var name = parts == 1 ? "Part" : $"Part {i + 1}";
                var path = song.WriteConstantWav(
                    $"{name}-{Guid.NewGuid():N}.wav", recordingLevel, recordingLevel, Frames);

                var track = new AudioTrack(path, name);
                track.SetMidiSource(
                    MultiTrackTestSong.BuildSingleNoteSequence(60, 500, 2000),
                    rate => new BandLimitedTestSynthesizer(rate, partials));

                player.Add(track);
            }
        }
        catch
        {
            player.Dispose();
            throw;
        }

        return player;
    }

    private static float Peak(float[] samples)
    {
        var peak = 0.0f;
        foreach (var sample in samples)
        {
            var magnitude = Math.Abs(sample);
            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }

        return peak;
    }
}
