using System;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Tests.Utils;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests for <see cref="PacketAudioPlayer"/>. The decode pump - underruns, the end of the stream,
/// the clock and the repositioning contract - is exercised through the player's own adapter, which
/// needs no audio device; only the one audible test opens one, and it is opt-in via
/// CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1.
/// </summary>
[Collection("SharedAudioOutput")]
public sealed class PacketAudioPlayerTests : IDisposable
{
    private const int Channels = 2;
    private const int SampleRate = 48000;
    private const int FramesPerPacket = 100;

    private static readonly bool PlaybackEnabled =
        Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS") == "1";

    private const string PlaybackSkipReason =
        "Set CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 to run tests that open the audio device.";

    /// <summary>Resets the process-wide shared output before each test for isolation.</summary>
    public PacketAudioPlayerTests() => SharedAudioOutput.Shutdown();

    /// <summary>Resets the process-wide shared output after each test for isolation.</summary>
    public void Dispose() => SharedAudioOutput.Shutdown();

    // ----- pre-open surface (no device) -----

    [Fact]
    public void A_new_player_is_closed_and_stopped()
    {
        //Arrange / Act
        using var player = new PacketAudioPlayer();

        //Assert
        player.IsOpen.Should().BeFalse();
        player.PlaybackState.Should().Be(PlaybackState.Stopped);
        player.Position.Should().Be(TimeSpan.Zero);
        player.Volume.Should().Be(1.0f);
        player.SampleRate.Should().Be(0);
        player.Channels.Should().Be(0);
    }

    [Fact]
    public void Play_before_open_throws()
    {
        //Arrange
        using var player = new PacketAudioPlayer();

        //Act
        var play = () => player.Play();

        //Assert
        play.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Seek_before_open_throws()
    {
        //Arrange
        using var player = new PacketAudioPlayer();

        //Act
        var seek = () => player.Seek(TimeSpan.FromSeconds(1));

        //Assert
        seek.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Seek_refuses_a_negative_pre_roll()
    {
        //Arrange
        using var player = new PacketAudioPlayer();

        //Act
        var seek = () => player.Seek(TimeSpan.Zero, TimeSpan.FromSeconds(-1));

        //Assert
        seek.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Open_without_a_codec_id_throws()
    {
        //Arrange
        using var player = new PacketAudioPlayer();
        var source = new FakeAudioPacketSource(new List<byte[]>());

        //Act
        var open = () => player.Open(string.Empty, ReadOnlyMemory<byte>.Empty, source);

        //Assert
        open.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Open_without_a_source_throws()
    {
        //Arrange
        using var player = new PacketAudioPlayer();
        var decoder = new FakePacketSoundDecoder();

        //Act
        var open = () => player.Open(decoder, null);

        //Assert
        open.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Open_without_a_decoder_throws()
    {
        //Arrange
        using var player = new PacketAudioPlayer();
        var source = new FakeAudioPacketSource(new List<byte[]>());

        //Act
        var open = () => player.Open(null, source);

        //Assert
        open.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Volume_round_trips_before_open()
    {
        //Arrange / Act
        using var player = new PacketAudioPlayer { Volume = 0.25f };

        //Assert
        player.Volume.Should().Be(0.25f);
    }

    [Fact]
    public void Pause_and_stop_before_open_are_noops()
    {
        //Arrange
        using var player = new PacketAudioPlayer();

        //Act
        player.Pause();
        player.Stop();

        //Assert
        player.PlaybackState.Should().Be(PlaybackState.Stopped);
    }

    [Fact]
    public void Dispose_before_open_is_safe()
    {
        //Arrange
        var player = new PacketAudioPlayer();

        //Act
        var dispose = () => player.Dispose();

        //Assert
        dispose.Should().NotThrow();
    }

    // ----- the decode pump (no device) -----

    [Fact]
    public void An_underrun_plays_silence_and_keeps_the_stream_alive()
    {
        //Arrange
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = new FakeAudioPacketSource(Packets(1));   // one packet, then dry, but not ended
        using var adapter = NewAdapter(decoder, source);
        var buffer = new float[FramesPerPacket * Channels * 3];

        //Act
        var read = adapter.Decode(buffer);

        //Assert
        // The whole buffer comes back - the packet's audio, then silence for what has not arrived.
        // Anything less would be read as the end of the stream by the engine's player.
        read.Should().Be(buffer.Length);
        buffer[0].Should().Be(0f);                                  // frame 0 of the packet
        buffer[(FramesPerPacket - 1) * Channels].Should().Be(FramesPerPacket - 1);
        buffer[FramesPerPacket * Channels].Should().Be(0f);         // silence from here on
        buffer[buffer.Length - 1].Should().Be(0f);
    }

    [Fact]
    public void An_underrun_recovers_when_packets_arrive()
    {
        //Arrange
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = new FakeAudioPacketSource(Packets(1));
        using var adapter = NewAdapter(decoder, source);
        var buffer = new float[FramesPerPacket * Channels * 2];
        adapter.Decode(buffer);   // drains the packet and underruns

        //Act
        source.Add(Packets(1));
        var read = adapter.Decode(buffer);

        //Assert
        read.Should().Be(buffer.Length);
        buffer[0].Should().Be(FramesPerPacket);   // decoding picked up where it left off
    }

    [Fact]
    public void The_end_of_the_stream_stops_the_samples()
    {
        //Arrange
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = new FakeAudioPacketSource(Packets(2)) { EndWhenDrained = true };
        using var adapter = NewAdapter(decoder, source);
        var buffer = new float[FramesPerPacket * Channels];
        var total = 0;

        //Act
        while (true)
        {
            var read = adapter.Decode(buffer);
            if (read <= 0) break;
            total += read;
        }

        //Assert
        total.Should().Be(2 * FramesPerPacket * Channels);
    }

    [Fact]
    public void The_provider_reports_the_end_of_the_stream_once_the_packets_are_spent()
    {
        //Arrange
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = new FakeAudioPacketSource(Packets(1)) { EndWhenDrained = true };
        var adapter = NewAdapter(decoder, source);
        using var provider = new PacketAudioPlayer.PacketDataProvider(adapter);
        var ended = 0;
        provider.EndOfStreamReached += (_, _) => ended++;
        var buffer = new float[FramesPerPacket * Channels];

        //Act
        var first = provider.ReadBytes(buffer);
        var second = provider.ReadBytes(buffer);

        //Assert
        first.Should().Be(buffer.Length);
        second.Should().Be(0);
        ended.Should().Be(1);
    }

    [Fact]
    public void The_provider_reports_no_length_so_the_engine_treats_it_as_live()
    {
        //Arrange
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = new FakeAudioPacketSource(Packets(1));
        var adapter = NewAdapter(decoder, source);

        //Act
        using var provider = new PacketAudioPlayer.PacketDataProvider(adapter);

        //Assert
        // Zero length is what keeps the engine's player from ending playback on its own when a read
        // comes back empty; only the packet source knows an underrun from an ending.
        provider.Length.Should().Be(0);
        provider.CanSeek.Should().BeFalse();
        provider.SampleRate.Should().Be(SampleRate);
    }

    [Fact]
    public void The_clock_counts_decoded_audio_and_not_silence()
    {
        //Arrange
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = new FakeAudioPacketSource(Packets(2));
        using var adapter = NewAdapter(decoder, source);
        var buffer = new float[FramesPerPacket * Channels * 4];   // more than the packets can fill

        //Act
        adapter.Decode(buffer);

        //Assert
        // Two packets of real audio; the rest of the buffer was silence for an underrun, and silence
        // is not media time.
        var expected = TimeSpan.FromSeconds(2.0 * FramesPerPacket / SampleRate);
        adapter.Position.Should().Be(expected);
    }

    [Fact]
    public void The_clock_starts_again_from_the_position_a_reposition_names()
    {
        //Arrange
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = new FakeAudioPacketSource(Packets(2));
        using var adapter = NewAdapter(decoder, source);
        var buffer = new float[FramesPerPacket * Channels];
        adapter.Decode(buffer);

        //Act
        source.Reposition(Packets(1));
        adapter.Reposition(TimeSpan.FromSeconds(30), 0);
        var positionAfterSeek = adapter.Position;
        adapter.Decode(buffer);

        //Assert
        decoder.ResetCount.Should().Be(1);
        positionAfterSeek.Should().Be(TimeSpan.FromSeconds(30));
        adapter.Position.Should().Be(TimeSpan.FromSeconds(30) +
                                     TimeSpan.FromSeconds((double)FramesPerPacket / SampleRate));
    }

    [Fact]
    public void A_reposition_drops_the_audio_decoded_from_the_old_position()
    {
        //Arrange
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = new FakeAudioPacketSource(Packets(4));
        using var adapter = NewAdapter(decoder, source);

        // Decode a fraction of a packet, so the rest of it is still held.
        var small = new float[10 * Channels];
        adapter.Decode(small);

        //Act
        source.Reposition(Packets(1));
        decoder.NextFrameValue = 5000;   // the "new position" is obvious in the samples
        adapter.Reposition(TimeSpan.FromSeconds(5), 0);
        var buffer = new float[FramesPerPacket * Channels];
        adapter.Decode(buffer);

        //Assert
        // Nothing from before the jump survives it: the very first sample is from the new position.
        buffer[0].Should().Be(5000f);
    }

    [Fact]
    public void The_pre_roll_of_a_reposition_is_decoded_and_discarded()
    {
        //Arrange
        const int preRollFrames = 150;   // one and a half packets
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = new FakeAudioPacketSource(Packets(4));
        using var adapter = NewAdapter(decoder, source);

        //Act
        adapter.Reposition(TimeSpan.FromSeconds(10), preRollFrames);
        var buffer = new float[FramesPerPacket * Channels];
        adapter.Decode(buffer);

        //Assert
        // The discarded frames are media time, so the clock reads the position that was sought to
        // plus what has been heard since; the first audible frame is the one after the pre-roll.
        buffer[0].Should().Be(preRollFrames);
        var heard = (double)FramesPerPacket / SampleRate;
        adapter.Position.Should().Be(TimeSpan.FromSeconds(10) +
                                     TimeSpan.FromSeconds(((double)preRollFrames / SampleRate) + heard));
    }

    [Fact]
    public void The_codecs_own_pre_skip_is_discarded_at_the_start()
    {
        //Arrange
        const int preSkipFrames = 40;
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket)
        {
            PreSkipSamples = preSkipFrames
        };
        var source = new FakeAudioPacketSource(Packets(2));
        using var adapter = NewAdapter(decoder, source);
        var buffer = new float[FramesPerPacket * Channels];

        //Act
        adapter.Decode(buffer);

        //Assert
        buffer[0].Should().Be(preSkipFrames);
    }

    [Fact]
    public void A_packet_that_decodes_to_nothing_is_not_the_end_of_the_stream()
    {
        //Arrange
        // What a lapped-transform codec does with the first packet after a reset.
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket)
        {
            SilentFirstPacket = true
        };
        var source = new FakeAudioPacketSource(Packets(2)) { EndWhenDrained = true };
        using var adapter = NewAdapter(decoder, source);
        var buffer = new float[FramesPerPacket * Channels];

        //Act
        var read = adapter.Decode(buffer);

        //Assert
        read.Should().Be(buffer.Length);
        decoder.DecodeCount.Should().Be(2);   // it asked for another packet rather than giving up
    }

    [Fact]
    public void The_adapter_leaves_a_decoder_it_does_not_own_alone()
    {
        //Arrange
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = new FakeAudioPacketSource(Packets(1));
        var adapter = NewAdapter(decoder, source);

        //Act
        adapter.Dispose();

        //Assert
        decoder.IsDisposed.Should().BeFalse();
    }

    // ----- audible (opt-in) -----

    [Fact]
    public void A_vorbis_asset_plays_through_the_packet_path()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange
        // The one sounding test here plays a fixture tone rather than the five-tone motif the other
        // audible tests use: the motif exists as WAV, and what this has to prove is that packets
        // lifted out of a container reach the device at all.
        using var audible = new AudibleTestScope();
        var packets = OggPacketReader.ReadPackets(TestAssets.Path(TestAssets.VorbisToneStereo));
        var codecPrivate = OggPacketReader.BuildXiphCodecPrivate(packets[0], packets[1], packets[2]);
        var audio = packets.GetRange(3, packets.Count - 3);
        var source = new FakeAudioPacketSource(audio) { EndWhenDrained = true };

        using var player = new PacketAudioPlayer();
        using var ended = new ManualResetEventSlim(false);
        player.PlaybackEnded += (_, _) => ended.Set();
        player.Open("vorbis", codecPrivate, source);
        player.Volume = 0.6f;

        //Act
        player.Play();
        var fired = ended.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        //Assert
        fired.Should().BeTrue();
        player.Channels.Should().Be(2);
        player.SampleRate.Should().Be(44100);
        player.Position.Should().BeGreaterThan(TimeSpan.FromSeconds(0.2));
        source.DeliveredCount.Should().Be(audio.Count);
    }

    private static PacketAudioPlayer.PacketDecoderAdapter NewAdapter(
        FakePacketSoundDecoder decoder, FakeAudioPacketSource source) =>
        new PacketAudioPlayer.PacketDecoderAdapter(decoder, source, ownsDecoder: false,
            channels: Channels, sampleRate: SampleRate);

    // The fake decoder ignores packet contents, so one byte per packet is enough to count with.
    private static List<byte[]> Packets(int count)
    {
        var packets = new List<byte[]>(count);
        for (var i = 0; i < count; i++)
        {
            packets.Add(new byte[] { (byte)i });
        }
        return packets;
    }
}
