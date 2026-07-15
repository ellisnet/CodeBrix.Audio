using System;
using System.Threading;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests for <see cref="WaveOutEvent"/> and <see cref="SharedAudioOutput"/>. Tests that actually
/// open the audio device are opt-in via the CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 environment variable;
/// the rest exercise the format/state logic with no hardware. Each test starts from a clean shared
/// output (see the constructor / <see cref="Dispose"/>).
/// </summary>
[Collection("SharedAudioOutput")]
public sealed class WaveOutEventTests : IDisposable
{
    private static readonly bool PlaybackEnabled =
        Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS") == "1";

    /// <summary>Resets the process-wide shared output before each test for isolation.</summary>
    public WaveOutEventTests() => SharedAudioOutput.Shutdown();

    /// <summary>Resets the process-wide shared output after each test for isolation.</summary>
    public void Dispose() => SharedAudioOutput.Shutdown();

    // ----- format / state logic (no device) -----

    [Fact]
    public void New_player_is_stopped_with_full_volume()
    {
        using var player = new WaveOutEvent();

        player.PlaybackState.Should().Be(PlaybackState.Stopped);
        player.Volume.Should().Be(1.0f);
    }

    [Fact]
    public void Init_null_provider_throws()
    {
        using var player = new WaveOutEvent();

        Action act = () => player.Init(null);

        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Play_before_init_throws()
    {
        using var player = new WaveOutEvent();

        Action act = () => player.Play();

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Init_upmixes_mono_source_to_stereo_ieee_float_at_source_rate()
    {
        //Arrange
        using var player = new WaveOutEvent();

        //Act
        player.Init(new SilenceProvider(new WaveFormat(44100, 16, 1)));

        //Assert
        player.OutputWaveFormat.Encoding.Should().Be(WaveFormatEncoding.IeeeFloat);
        player.OutputWaveFormat.Channels.Should().Be(2);
        player.OutputWaveFormat.SampleRate.Should().Be(44100);
    }

    [Fact]
    public void Init_keeps_stereo_source_as_stereo_at_source_rate()
    {
        using var player = new WaveOutEvent();

        player.Init(new SilenceProvider(new WaveFormat(32000, 16, 2)));

        player.OutputWaveFormat.Channels.Should().Be(2);
        player.OutputWaveFormat.SampleRate.Should().Be(32000);
    }

    [Fact]
    public void Init_with_unsupported_channel_count_throws()
    {
        using var player = new WaveOutEvent();

        Action act = () => player.Init(new SilenceProvider(new WaveFormat(48000, 16, 6)));

        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Configured_output_rejects_a_source_at_a_different_rate()
    {
        //Arrange
        SharedAudioOutput.Configure(sampleRate: 48000);
        using var player = new WaveOutEvent();

        //Act
        Action act = () => player.Init(new SilenceProvider(new WaveFormat(44100, 16, 2)));

        //Assert — no resampler, so the mismatch is rejected rather than played at the wrong pitch.
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Configure_reports_pending_format_before_start()
    {
        SharedAudioOutput.Configure(sampleRate: 48000, channels: 2);

        SharedAudioOutput.IsRunning.Should().BeFalse();
        SharedAudioOutput.SampleRate.Should().Be(48000);
        SharedAudioOutput.Channels.Should().Be(2);
    }

    [Fact]
    public void Volume_round_trips_before_play()
    {
        using var player = new WaveOutEvent { Volume = 0.25f };

        player.Volume.Should().Be(0.25f);
    }

    [Fact]
    public void Configure_with_invalid_arguments_throws()
    {
        Action badRate = () => SharedAudioOutput.Configure(sampleRate: 0);
        Action badChannels = () => SharedAudioOutput.Configure(sampleRate: 48000, channels: 3);

        badRate.Should().Throw<ArgumentOutOfRangeException>();
        badChannels.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ----- device playback (opt-in) -----

    [Fact]
    public void Playing_a_source_to_its_end_raises_PlaybackStopped()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange — a fifth of a second of stereo silence at 48 kHz.
        var format = new WaveFormat(48000, 16, 2);
        long bytes = FrameAlignedBytes(format, seconds: 0.2);
        using var player = new WaveOutEvent();
        player.Init(new SilenceProvider(format, bytes));

        StoppedEventArgs stoppedArgs = null;
        using var stopped = new ManualResetEventSlim(false);
        player.PlaybackStopped += (_, e) => { stoppedArgs = e; stopped.Set(); };

        //Act
        player.Play();
        player.PlaybackState.Should().Be(PlaybackState.Playing);
        bool fired = stopped.Wait(TimeSpan.FromSeconds(4), TestContext.Current.CancellationToken);

        //Assert
        fired.Should().BeTrue();
        stoppedArgs.Exception.Should().BeNull();
        player.PlaybackState.Should().Be(PlaybackState.Stopped);
    }

    [Fact]
    public void The_output_adopts_the_first_sources_sample_rate()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        var format = new WaveFormat(32000, 16, 2);
        using var player = new WaveOutEvent();
        player.Init(new SilenceProvider(format, FrameAlignedBytes(format, seconds: 0.1)));

        player.Play();

        SharedAudioOutput.IsRunning.Should().BeTrue();
        SharedAudioOutput.SampleRate.Should().Be(32000);
    }

    [Fact]
    public void Overlapping_players_share_one_running_output()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        var format = new WaveFormat(48000, 16, 2);
        long bytes = FrameAlignedBytes(format, seconds: 0.2);

        using var first = new WaveOutEvent();
        using var second = new WaveOutEvent();
        first.Init(new SilenceProvider(format, bytes));
        second.Init(new SilenceProvider(format, bytes));

        first.Play();
        second.Play();

        first.PlaybackState.Should().Be(PlaybackState.Playing);
        second.PlaybackState.Should().Be(PlaybackState.Playing);
        SharedAudioOutput.IsRunning.Should().BeTrue();
    }

    private const string PlaybackSkipReason =
        "Set CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 to run tests that open the audio device.";

    private static long FrameAlignedBytes(WaveFormat format, double seconds)
    {
        long frames = (long)(format.SampleRate * seconds);
        return frames * format.BlockAlign;
    }

    /// <summary>An <see cref="IWaveProvider"/> that yields PCM16 silence, optionally for a finite length.</summary>
    private sealed class SilenceProvider : IWaveProvider
    {
        private long _remaining;

        public SilenceProvider(WaveFormat format, long totalBytes = -1)
        {
            WaveFormat = format;
            _remaining = totalBytes;
        }

        public WaveFormat WaveFormat { get; }

        public int Read(Span<byte> buffer)
        {
            if (_remaining < 0)
            {
                buffer.Clear();
                return buffer.Length;
            }
            if (_remaining == 0)
            {
                return 0;
            }
            int count = (int)Math.Min(_remaining, buffer.Length);
            buffer.Slice(0, count).Clear();
            _remaining -= count;
            return count;
        }
    }
}
