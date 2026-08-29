using System;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Tests.Utils;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests for the TRAILING TRIM of <see cref="PacketAudioPlayer"/>: the hold-back that keeps the
/// encoder padding at the end of a track from ever reaching the mixer.
/// </summary>
/// <remarks>
/// The trim lives in the player's own adapter, which needs no audio device, so everything here drives
/// that adapter directly and counts frames. The one sounding test is opt-in via
/// CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1, like every other test in this package that opens a device.
/// </remarks>
[Collection("SharedAudioOutput")]
public sealed class PacketAudioPlayerTrimTests : IDisposable
{
    private const int Channels = 2;
    private const int SampleRate = 48000;
    private const int FramesPerPacket = 100;

    // Deliberately not a whole number of packets, so the hold-back ring is exercised across the
    // boundaries rather than lining up neatly with them.
    private const int ReadFrames = 37;

    private static readonly bool PlaybackEnabled =
        Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS") == "1";

    private const string PlaybackSkipReason =
        "Set CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 to run tests that open the audio device.";

    /// <summary>Resets the process-wide shared output before each test for isolation.</summary>
    public PacketAudioPlayerTrimTests() => SharedAudioOutput.Shutdown();

    /// <summary>Resets the process-wide shared output after each test for isolation.</summary>
    public void Dispose() => SharedAudioOutput.Shutdown();

    // ----- the hold-back (no device) -----

    [Fact]
    public void The_hold_back_releases_every_frame_but_the_trim()
    {
        //Arrange
        const int packets = 10;
        const int trimFrames = 250;
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        using var adapter = NewAdapter(decoder, EndedSource(packets));
        adapter.SetTrailingTrimFrames(trimFrames);

        //Act
        var delivered = DrainAll(adapter);

        //Assert
        delivered.Count.Should().Be(((packets * FramesPerPacket) - trimFrames) * Channels);
    }

    [Fact]
    public void The_frames_released_are_the_first_ones_and_the_last_are_the_ones_dropped()
    {
        //Arrange
        const int packets = 6;
        const int trimFrames = 137;
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        using var adapter = NewAdapter(decoder, EndedSource(packets));
        adapter.SetTrailingTrimFrames(trimFrames);

        //Act
        var delivered = DrainAll(adapter);

        //Assert
        // The fake decoder numbers its frames, so the value of a sample says which frame it was.
        // Everything from frame 0 up to the trim must be there, in order, and nothing beyond it.
        var expectedFrames = (packets * FramesPerPacket) - trimFrames;
        delivered.Count.Should().Be(expectedFrames * Channels);
        for (var frame = 0; frame < expectedFrames; frame++)
        {
            for (var channel = 0; channel < Channels; channel++)
            {
                delivered[(frame * Channels) + channel].Should().Be(frame);
            }
        }
    }

    [Fact]
    public void A_zero_trim_delivers_exactly_what_no_trim_at_all_delivers()
    {
        //Arrange
        const int packets = 5;
        var untrimmedDecoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        using var untrimmed = NewAdapter(untrimmedDecoder, EndedSource(packets));

        var zeroTrimDecoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        using var zeroTrim = NewAdapter(zeroTrimDecoder, EndedSource(packets));
        zeroTrim.SetTrailingTrimFrames(0);

        //Act
        var without = DrainAll(untrimmed);
        var withZero = DrainAll(zeroTrim);

        //Assert
        withZero.Count.Should().Be(without.Count);
        withZero.Count.Should().Be(packets * FramesPerPacket * Channels);
        for (var i = 0; i < withZero.Count; i++)
        {
            withZero[i].Should().Be(without[i]);
        }
    }

    [Fact]
    public void A_trim_longer_than_the_stream_delivers_nothing_and_still_ends()
    {
        //Arrange
        const int packets = 3;   // 300 frames in all
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var adapter = NewAdapter(decoder, EndedSource(packets));
        adapter.SetTrailingTrimFrames(500);
        using var provider = new PacketAudioPlayer.PacketDataProvider(adapter);
        var ended = 0;
        provider.EndOfStreamReached += (_, _) => ended++;
        var buffer = new float[ReadFrames * Channels];
        var total = 0;

        //Act
        while (true)
        {
            var read = provider.ReadBytes(buffer);
            if (read <= 0) break;
            total += read;
        }

        //Assert
        // Nothing is heard, but the stream still ENDS - which is what raises PlaybackEnded on the
        // player, so a caller waiting for the track to finish is not left waiting.
        total.Should().Be(0);
        ended.Should().Be(1);
    }

    [Fact]
    public void A_trim_set_after_the_audio_is_open_still_catches_the_end()
    {
        //Arrange
        const int packets = 8;
        const int trimFrames = 150;
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        using var adapter = NewAdapter(decoder, EndedSource(packets));
        var delivered = new List<float>();
        var buffer = new float[ReadFrames * Channels];

        // Play a couple of packets' worth before the trim is known - the shape of a demultiplexer
        // that only reads the container's padding when it reaches the last block.
        for (var i = 0; i < 6; i++)
        {
            var read = adapter.Decode(buffer);
            for (var s = 0; s < read; s++) delivered.Add(buffer[s]);
        }

        //Act
        adapter.SetTrailingTrimFrames(trimFrames);
        delivered.AddRange(DrainAll(adapter));

        //Assert
        var expectedFrames = (packets * FramesPerPacket) - trimFrames;
        delivered.Count.Should().Be(expectedFrames * Channels);
        delivered[0].Should().Be(0f);
        delivered[delivered.Count - 1].Should().Be(expectedFrames - 1);
    }

    [Fact]
    public void Lowering_the_trim_releases_what_is_no_longer_inside_the_window()
    {
        //Arrange
        const int packets = 6;
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        using var adapter = NewAdapter(decoder, EndedSource(packets));
        adapter.SetTrailingTrimFrames(300);
        var delivered = new List<float>();
        var buffer = new float[ReadFrames * Channels];
        for (var i = 0; i < 8; i++)
        {
            var read = adapter.Decode(buffer);
            for (var s = 0; s < read; s++) delivered.Add(buffer[s]);
        }

        //Act
        adapter.SetTrailingTrimFrames(0);
        delivered.AddRange(DrainAll(adapter));

        //Assert
        // Held audio is delayed, never dropped: dropping the trim to nothing hands all of it over.
        delivered.Count.Should().Be(packets * FramesPerPacket * Channels);
        delivered[delivered.Count - Channels].Should().Be((packets * FramesPerPacket) - 1);
    }

    [Fact]
    public void A_reposition_clears_the_hold_back_and_the_trim_still_applies_at_the_new_end()
    {
        //Arrange
        const int trimFrames = 100;
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = new FakeAudioPacketSource(Packets(6)) { EndWhenDrained = true };
        using var adapter = NewAdapter(decoder, source);
        adapter.SetTrailingTrimFrames(trimFrames);
        var buffer = new float[ReadFrames * Channels];
        adapter.Decode(buffer);
        adapter.Decode(buffer);

        //Act
        source.Reposition(Packets(3));
        decoder.NextFrameValue = 5000;   // the new position is obvious in the samples
        adapter.Reposition(TimeSpan.FromSeconds(5), 0);
        var delivered = DrainAll(adapter);

        //Assert
        // Nothing held from before the jump survives it, and the trim - a property of the track, not
        // of the moment - is still applied at the new end.
        var expectedFrames = (3 * FramesPerPacket) - trimFrames;
        delivered.Count.Should().Be(expectedFrames * Channels);
        delivered[0].Should().Be(5000f);
        delivered[delivered.Count - Channels].Should().Be(5000f + expectedFrames - 1);
    }

    [Fact]
    public void The_clock_does_not_count_trimmed_frames()
    {
        //Arrange
        const int packets = 4;
        const int trimFrames = 90;
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        using var adapter = NewAdapter(decoder, EndedSource(packets));
        adapter.SetTrailingTrimFrames(trimFrames);

        //Act
        DrainAll(adapter);

        //Assert
        // Position counts what reached the mixer; audio held back and thrown away never did.
        var heardFrames = (packets * FramesPerPacket) - trimFrames;
        adapter.Position.Should().Be(TimeSpan.FromSeconds(heardFrames / (double)SampleRate));
    }

    [Fact]
    public void The_codecs_pre_skip_and_the_trailing_trim_are_independent()
    {
        //Arrange
        const int packets = 5;
        const int preSkipFrames = 40;
        const int trimFrames = 60;
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket)
        {
            PreSkipSamples = preSkipFrames
        };
        using var adapter = NewAdapter(decoder, EndedSource(packets));
        adapter.SetTrailingTrimFrames(trimFrames);

        //Act
        var delivered = DrainAll(adapter);

        //Assert
        // One discard at the start, one at the end, neither aware of the other.
        var expectedFrames = (packets * FramesPerPacket) - preSkipFrames - trimFrames;
        delivered.Count.Should().Be(expectedFrames * Channels);
        delivered[0].Should().Be(preSkipFrames);
        delivered[delivered.Count - Channels].Should().Be((packets * FramesPerPacket) - trimFrames - 1);
    }

    // ----- a packet's own discard padding (no device) -----

    [Fact]
    public void The_discard_padding_on_the_last_packet_trims_the_end()
    {
        //Arrange
        const int packets = 5;
        const int paddingFrames = 50;
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = new FakeAudioPacketQueue(PacketsWithFinalPadding(packets, paddingFrames))
        {
            EndWhenDrained = true
        };
        using var adapter = NewAdapter(decoder, source);

        //Act
        var delivered = DrainAll(adapter);

        //Assert
        // No track-level trim was set at all: the container's per-block value did the whole job.
        var expectedFrames = (packets * FramesPerPacket) - paddingFrames;
        delivered.Count.Should().Be(expectedFrames * Channels);
        delivered[delivered.Count - Channels].Should().Be(expectedFrames - 1);
    }

    [Fact]
    public void The_larger_of_the_track_trim_and_the_packets_padding_wins()
    {
        //Arrange
        const int packets = 5;
        var bigTrimDecoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        using var bigTrim = NewAdapter(bigTrimDecoder,
            new FakeAudioPacketQueue(PacketsWithFinalPadding(packets, 50)) { EndWhenDrained = true });
        bigTrim.SetTrailingTrimFrames(200);

        var bigPaddingDecoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        using var bigPadding = NewAdapter(bigPaddingDecoder,
            new FakeAudioPacketQueue(PacketsWithFinalPadding(packets, 80)) { EndWhenDrained = true });
        bigPadding.SetTrailingTrimFrames(30);

        //Act
        var trimWon = DrainAll(bigTrim);
        var paddingWon = DrainAll(bigPadding);

        //Assert
        trimWon.Count.Should().Be(((packets * FramesPerPacket) - 200) * Channels);
        paddingWon.Count.Should().Be(((packets * FramesPerPacket) - 80) * Channels);
    }

    [Fact]
    public void A_padding_bigger_than_what_is_still_in_hand_trims_only_what_is_left()
    {
        //Arrange
        const int packets = 5;
        const int paddingFrames = 200;   // twice what a packet holds
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = new FakeAudioPacketQueue(PacketsWithFinalPadding(packets, paddingFrames))
        {
            EndWhenDrained = true
        };
        using var adapter = NewAdapter(decoder, source);

        //Act
        var delivered = DrainAll(adapter);

        //Assert
        // A per-packet padding is only learned when that packet arrives, and by then the hold-back
        // can only withhold what it is still holding plus what that packet decodes to - here one
        // packet, because no track-level trim was keeping anything else back. A container whose
        // padding can exceed one packet should say so with SetTrailingTrim instead, which is applied
        // from the start and therefore always exact.
        delivered.Count.Should().Be(((packets * FramesPerPacket) - FramesPerPacket) * Channels);
    }

    [Fact]
    public void A_padding_on_a_packet_that_is_not_the_last_only_delays_audio()
    {
        //Arrange
        const int packets = 5;
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var list = new List<AudioPacket>();
        for (var i = 0; i < packets; i++)
        {
            // The padding sits in the MIDDLE of the stream, where it means nothing about the end.
            var padding = i == 1 ? TimeSpan.FromSeconds(150.0 / SampleRate) : TimeSpan.Zero;
            list.Add(new AudioPacket(new byte[] { (byte)i }, null, padding));
        }
        using var adapter = NewAdapter(decoder, new FakeAudioPacketQueue(list) { EndWhenDrained = true });

        //Act
        var delivered = DrainAll(adapter);

        //Assert
        // Everything is heard: raising the window mid-stream delays audio, and the packets that
        // follow let it out again.
        delivered.Count.Should().Be(packets * FramesPerPacket * Channels);
        delivered[delivered.Count - Channels].Should().Be((packets * FramesPerPacket) - 1);
    }

    // ----- cost (no device) -----

    [Fact]
    public void The_hold_back_allocates_nothing_once_it_is_running()
    {
        //Arrange
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        using var adapter = NewAdapter(decoder, new EndlessPacketSource());
        adapter.SetTrailingTrimFrames(480);   // 10 ms at 48 kHz
        var buffer = new float[256 * Channels];

        // Warm up hard so the read path is fully JIT-tiered before measuring; a re-JIT inside the
        // measured window is attributed to this thread's allocation counter.
        for (var i = 0; i < 10_000; i++) adapter.Decode(buffer);

        //Act
        // Allow a few attempts so a stray background event in one window does not fail an
        // allocation-free path; a genuine per-call allocation shows up in every window.
        long allocated = -1;
        for (var attempt = 0; attempt < 5 && allocated != 0; attempt++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++) adapter.Decode(buffer);
            var after = GC.GetAllocatedBytesForCurrentThread();
            allocated = after - before;
        }

        //Assert
        allocated.Should().Be(0);
    }

    // ----- the player's own surface (no device) -----

    [Fact]
    public void A_new_player_trims_nothing()
    {
        //Arrange / Act
        using var player = new PacketAudioPlayer();

        //Assert
        player.TrailingTrim.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void The_trailing_trim_round_trips_before_anything_is_open()
    {
        //Arrange
        using var player = new PacketAudioPlayer();

        //Act
        player.SetTrailingTrim(TimeSpan.FromMilliseconds(80));

        //Assert
        player.TrailingTrim.Should().Be(TimeSpan.FromMilliseconds(80));
    }

    [Fact]
    public void A_negative_trailing_trim_is_refused()
    {
        //Arrange
        using var player = new PacketAudioPlayer();

        //Act
        var setTime = () => player.SetTrailingTrim(TimeSpan.FromMilliseconds(-1));
        var setFrames = () => player.SetTrailingTrimFrames(-1);

        //Assert
        setTime.Should().Throw<ArgumentOutOfRangeException>();
        setFrames.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void A_trim_in_frames_reads_back_as_a_duration_only_once_a_rate_is_known()
    {
        //Arrange
        using var player = new PacketAudioPlayer();

        //Act
        player.SetTrailingTrimFrames(4800);

        //Assert
        // Frames become time at the decoder's rate, and there is no decoder yet.
        player.TrailingTrim.Should().Be(TimeSpan.Zero);
    }

    // ----- audible (opt-in) -----

    [Fact]
    public void A_vorbis_asset_plays_through_the_packet_path_with_its_tail_trimmed()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange
        // Pin the output to the fixture's own rate, so no rate conversion sits between the frames
        // the adapter hands over and the frames the test counts.
        SharedAudioOutput.Configure(44100);

        using var audible = new AudibleTestScope();
        var packets = OggPacketReader.ReadPackets(TestAssets.Path(TestAssets.VorbisToneStereo));
        var codecPrivate = OggPacketReader.BuildXiphCodecPrivate(packets[0], packets[1], packets[2]);
        var audio = packets.GetRange(3, packets.Count - 3);

        // What an untrimmed run delivers, measured device-free through the same decoder, so the
        // trimmed run below can be held to exactly that minus the trim.
        var reference = SharedAudioOutput.CreatePacketDecoder("vorbis", codecPrivate);
        var nativeRate = reference.SampleRate;
        long untrimmedFrames;
        using (var referenceAdapter = new PacketAudioPlayer.PacketDecoderAdapter(
                   reference, new FakeAudioPacketSource(audio) { EndWhenDrained = true },
                   ownsDecoder: true, channels: reference.Channels, sampleRate: nativeRate))
        {
            untrimmedFrames = DrainAll(referenceAdapter).Count / reference.Channels;
        }

        var trimFrames = (int)Math.Round(0.1 * nativeRate);   // the last ~100 ms

        using var player = new PacketAudioPlayer();
        using var ended = new ManualResetEventSlim(false);
        player.PlaybackEnded += (_, _) => ended.Set();
        var source = new FakeAudioPacketSource(audio) { EndWhenDrained = true };
        player.SetTrailingTrimFrames(trimFrames);
        player.Open("vorbis", codecPrivate, source);
        player.Volume = 0.6f;

        //Act
        player.Play();
        var fired = ended.Wait(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

        //Assert
        fired.Should().BeTrue();
        player.SampleRate.Should().Be(nativeRate);
        var heardFrames = (long)Math.Round(player.Position.TotalSeconds * player.SampleRate);
        heardFrames.Should().Be(untrimmedFrames - trimFrames);
    }

    // ----- helpers -----

    private static PacketAudioPlayer.PacketDecoderAdapter NewAdapter(
        FakePacketSoundDecoder decoder, IAudioPacketSource source) =>
        new PacketAudioPlayer.PacketDecoderAdapter(decoder, source, ownsDecoder: false,
            channels: Channels, sampleRate: SampleRate);

    private static FakeAudioPacketSource EndedSource(int packetCount) =>
        new FakeAudioPacketSource(Packets(packetCount)) { EndWhenDrained = true };

    // Every sample the adapter will ever hand over, in order.
    private static List<float> DrainAll(PacketAudioPlayer.PacketDecoderAdapter adapter)
    {
        var buffer = new float[ReadFrames * Channels];
        var delivered = new List<float>();

        while (true)
        {
            var read = adapter.Decode(buffer);
            if (read <= 0) break;
            for (var i = 0; i < read; i++)
            {
                delivered.Add(buffer[i]);
            }
        }

        return delivered;
    }

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

    private static List<AudioPacket> PacketsWithFinalPadding(int count, int paddingFrames)
    {
        var packets = new List<AudioPacket>(count);
        for (var i = 0; i < count; i++)
        {
            var padding = i == count - 1
                ? TimeSpan.FromSeconds(paddingFrames / (double)SampleRate)
                : TimeSpan.Zero;
            packets.Add(new AudioPacket(new byte[] { (byte)i }, null, padding));
        }
        return packets;
    }

    /// <summary>A source that never runs dry, for measuring the steady state.</summary>
    private sealed class EndlessPacketSource : IAudioPacketSource
    {
        private readonly byte[] bytes = new byte[] { 1 };

        public bool EndOfStream => false;

        public bool TryReadPacket(out AudioPacket packet)
        {
            packet = new AudioPacket(bytes);
            return true;
        }
    }
}
