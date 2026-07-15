using System;
using System.IO;
using System.Threading;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests for <see cref="AudioFilePlayer"/>. Tests that actually open the audio device / decode a file
/// are opt-in via CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1; the rest exercise the pre-load surface with no
/// hardware. Shares the non-parallel "SharedAudioOutput" collection with the WaveOutEvent tests.
/// </summary>
[Collection("SharedAudioOutput")]
public sealed class AudioFilePlayerTests : IDisposable
{
    private static readonly bool PlaybackEnabled =
        Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS") == "1";

    /// <summary>Resets the process-wide shared output before each test for isolation.</summary>
    public AudioFilePlayerTests() => SharedAudioOutput.Shutdown();

    /// <summary>Resets the process-wide shared output after each test for isolation.</summary>
    public void Dispose() => SharedAudioOutput.Shutdown();

    // ----- pre-load surface (no device) -----

    [Fact]
    public void New_player_is_unloaded_and_stopped()
    {
        using var player = new AudioFilePlayer();

        player.IsLoaded.Should().BeFalse();
        player.PlaybackState.Should().Be(PlaybackState.Stopped);
        player.Position.Should().Be(TimeSpan.Zero);
        player.Duration.Should().Be(TimeSpan.Zero);
        player.Volume.Should().Be(1.0f);
    }

    [Fact]
    public void Play_before_load_throws()
    {
        using var player = new AudioFilePlayer();

        Action act = () => player.Play();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Seek_before_load_throws()
    {
        using var player = new AudioFilePlayer();

        Action act = () => player.Seek(TimeSpan.FromSeconds(1));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Load_null_path_throws()
    {
        using var player = new AudioFilePlayer();

        Action act = () => player.Load((string)null);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Load_null_stream_throws()
    {
        using var player = new AudioFilePlayer();

        Action act = () => player.Load((Stream)null);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Volume_and_looping_round_trip_before_load()
    {
        using var player = new AudioFilePlayer { Volume = 0.4f, IsLooping = true };

        player.Volume.Should().Be(0.4f);
        player.IsLooping.Should().BeTrue();
    }

    [Fact]
    public void Pause_and_stop_before_load_are_noops()
    {
        using var player = new AudioFilePlayer();

        player.Pause();
        player.Stop();

        player.PlaybackState.Should().Be(PlaybackState.Stopped);
    }

    [Fact]
    public void Dispose_before_load_is_safe()
    {
        var player = new AudioFilePlayer();

        Action act = () => player.Dispose();

        act.Should().NotThrow();
    }

    // ----- file playback (opt-in) -----

    [Fact]
    public void Load_reports_duration_and_stopped_state()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        string path = WriteTempWav(sampleRate: 44100, channels: 2, seconds: 0.4);
        try
        {
            using var player = new AudioFilePlayer();
            player.Load(path);

            player.IsLoaded.Should().BeTrue();
            player.PlaybackState.Should().Be(PlaybackState.Stopped);
            player.Duration.TotalSeconds.Should().BeApproximately(0.4, 0.1);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Play_sets_playing_state()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        string path = WriteTempWav(sampleRate: 48000, channels: 2, seconds: 1.0);
        try
        {
            using var player = new AudioFilePlayer();
            player.Load(path);
            player.Volume = 0.5f;
            player.Play();

            player.PlaybackState.Should().Be(PlaybackState.Playing);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Playing_to_the_end_raises_PlaybackEnded()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        string path = WriteTempWav(sampleRate: 48000, channels: 2, seconds: 0.3);
        try
        {
            using var player = new AudioFilePlayer();
            using var ended = new ManualResetEventSlim(false);
            player.PlaybackEnded += (_, _) => ended.Set();

            player.Load(path);
            player.Play();
            bool fired = ended.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);

            fired.Should().BeTrue();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Seeking_near_the_end_shortens_playback()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        // A 2s file seeked to 1.8s should end in well under the full duration.
        string path = WriteTempWav(sampleRate: 48000, channels: 2, seconds: 2.0);
        try
        {
            using var player = new AudioFilePlayer();
            using var ended = new ManualResetEventSlim(false);
            player.PlaybackEnded += (_, _) => ended.Set();

            player.Load(path);
            player.Seek(TimeSpan.FromSeconds(1.8));
            player.Play();
            bool fired = ended.Wait(TimeSpan.FromSeconds(1.2), TestContext.Current.CancellationToken);

            fired.Should().BeTrue();   // would have taken ~2s without the seek
        }
        finally
        {
            File.Delete(path);
        }
    }

    private const string PlaybackSkipReason =
        "Set CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 to run tests that open the audio device.";

    // Writes a short silent PCM16 WAV to a temp file that miniaudio can decode.
    private static string WriteTempWav(int sampleRate, int channels, double seconds)
    {
        string path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
        var format = new WaveFormat(sampleRate, 16, channels);
        using (var writer = new WaveFileWriter(path, format))
        {
            int frames = (int)(sampleRate * seconds);
            var samples = new float[frames * channels];   // silence
            writer.WriteSamples(samples, 0, samples.Length);
        }
        return path;
    }
}
