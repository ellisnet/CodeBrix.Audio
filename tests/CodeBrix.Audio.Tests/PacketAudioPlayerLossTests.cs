using System;
using System.Collections.Generic;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Interfaces;
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
/// Tests for PACKET LOSS through <see cref="PacketAudioPlayer"/>: a source that reports a gap of a
/// known length, and what the player asks the decoder to do about it.
/// </summary>
/// <remarks>
/// None of this needs an audio device - the player's own adapter is driven directly - so the whole
/// file runs everywhere.
/// </remarks>
[Collection("SharedAudioOutput")]
public sealed class PacketAudioPlayerLossTests : IDisposable
{
    private const int Channels = 2;
    private const int SampleRate = 48000;
    private const int FramesPerPacket = 100;
    private const int ReadFrames = 37;

    /// <summary>Resets the process-wide shared output before each test for isolation.</summary>
    public PacketAudioPlayerLossTests() => SharedAudioOutput.Shutdown();

    /// <summary>Resets the process-wide shared output after each test for isolation.</summary>
    public void Dispose() => SharedAudioOutput.Shutdown();

    [Fact]
    public void A_gap_of_a_known_duration_becomes_exactly_that_much_audio()
    {
        //Arrange
        const int gapFrames = 250;
        var decoder = new ConcealingPacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = Feed(
            Packet(0),
            AudioPacket.Loss(TimeSpan.FromSeconds(gapFrames / (double)SampleRate)),
            Packet(1));
        using var adapter = NewAdapter(decoder, source);

        //Act
        var delivered = DrainAll(adapter);

        //Assert
        // Two packets of audio plus the gap: the timeline keeps its length, which is the whole point.
        delivered.Count.Should().Be(((2 * FramesPerPacket) + gapFrames) * Channels);
    }

    [Fact]
    public void A_gap_stated_in_frames_needs_no_rounding()
    {
        //Arrange
        const int gapFrames = 137;
        var decoder = new ConcealingPacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = Feed(Packet(0), AudioPacket.Loss(gapFrames), Packet(1));
        using var adapter = NewAdapter(decoder, source);

        //Act
        var delivered = DrainAll(adapter);

        //Assert
        delivered.Count.Should().Be(((2 * FramesPerPacket) + gapFrames) * Channels);
    }

    [Fact]
    public void The_concealed_audio_lands_where_the_gap_was()
    {
        //Arrange
        const int gapFrames = 60;
        var decoder = new ConcealingPacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = Feed(Packet(0), AudioPacket.Loss(gapFrames), Packet(1));
        using var adapter = NewAdapter(decoder, source);

        //Act
        var delivered = DrainAll(adapter);

        //Assert
        // Frame 0..99 decoded, 100..159 concealed, 160..259 decoded again - and the decoded audio
        // after the gap carries on counting from where it left off, so nothing slid.
        delivered[0].Should().Be(0f);
        delivered[(FramesPerPacket - 1) * Channels].Should().Be(FramesPerPacket - 1);
        for (var frame = FramesPerPacket; frame < FramesPerPacket + gapFrames; frame++)
        {
            delivered[frame * Channels].Should().Be(ConcealingPacketSoundDecoder.ConcealmentValue);
        }
        delivered[(FramesPerPacket + gapFrames) * Channels].Should().Be(FramesPerPacket);
    }

    [Fact]
    public void A_long_gap_is_covered_in_helpings()
    {
        //Arrange
        const int gapFrames = 350;   // three and a half of the decoder's largest helping
        var decoder = new ConcealingPacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = Feed(Packet(0), AudioPacket.Loss(gapFrames), Packet(1));
        using var adapter = NewAdapter(decoder, source);

        //Act
        var delivered = DrainAll(adapter);

        //Assert
        // Each call is told how much is STILL missing, and the last one asks for the remainder only.
        delivered.Count.Should().Be(((2 * FramesPerPacket) + gapFrames) * Channels);
        decoder.ConcealCallCount.Should().Be(4);
        decoder.ConcealRequests.Should().Equal(350, 250, 150, 50);
    }

    [Fact]
    public void A_decoder_that_conceals_a_little_at_a_time_is_asked_again()
    {
        //Arrange
        const int gapFrames = 120;
        var decoder = new ConcealingPacketSoundDecoder(Channels, SampleRate, FramesPerPacket)
        {
            FramesPerConcealCall = 25
        };
        var source = Feed(Packet(0), AudioPacket.Loss(gapFrames), Packet(1));
        using var adapter = NewAdapter(decoder, source);

        //Act
        var delivered = DrainAll(adapter);

        //Assert
        delivered.Count.Should().Be(((2 * FramesPerPacket) + gapFrames) * Channels);
        decoder.ConcealCallCount.Should().Be(5);   // 25 + 25 + 25 + 25 + 20
    }

    [Fact]
    public void A_decoder_with_nothing_to_offer_gets_silence_of_the_right_length()
    {
        //Arrange
        const int gapFrames = 90;
        var decoder = new NoConcealmentDecoder();
        var source = Feed(Packet(0), AudioPacket.Loss(gapFrames), Packet(1));
        using var adapter = NewAdapter(decoder, source);

        //Act
        var delivered = DrainAll(adapter);

        //Assert
        // The player fills what the decoder would not, so the length of the timeline is the same
        // either way; only what fills the gap differs.
        delivered.Count.Should().Be(((2 * FramesPerPacket) + gapFrames) * Channels);
        for (var frame = FramesPerPacket; frame < FramesPerPacket + gapFrames; frame++)
        {
            delivered[frame * Channels].Should().Be(0f);
        }
        delivered[(FramesPerPacket + gapFrames) * Channels].Should().Be(FramesPerPacket);
    }

    [Fact]
    public void The_default_concealment_asks_the_decoder_with_an_empty_packet()
    {
        //Arrange
        const int gapFrames = 250;
        // This fake implements none of the loss members, so the interface's own default is what runs.
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = Feed(Packet(0), AudioPacket.Loss(gapFrames), Packet(1));
        using var adapter = NewAdapter(decoder, source);

        //Act
        var delivered = DrainAll(adapter);

        //Assert
        // The default forwards to DecodePacket with an empty packet - the long-standing way of
        // saying "one packet was lost" - so the gap cost three extra decode calls, and what came
        // back was still cut to the length of the gap.
        ((IPacketSoundDecoder)decoder).SupportsLossConcealment.Should().BeFalse();
        decoder.DecodeCount.Should().Be(5);   // two real packets, three helpings of concealment
        delivered.Count.Should().Be(((2 * FramesPerPacket) + gapFrames) * Channels);
    }

    [Fact]
    public void An_empty_packet_is_still_one_packet_lost()
    {
        //Arrange
        var decoder = new FakePacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = Feed(Packet(0), new AudioPacket(ReadOnlyMemory<byte>.Empty), Packet(1));
        using var adapter = NewAdapter(decoder, source);

        //Act
        var delivered = DrainAll(adapter);

        //Assert
        // The empty packet reaches the decoder rather than being swallowed by the player: that is
        // what lets a decoder with concealment of its own answer it at all.
        decoder.DecodeCount.Should().Be(3);
        delivered.Count.Should().Be(3 * FramesPerPacket * Channels);
    }

    [Fact]
    public void A_gap_is_media_time_and_advances_the_clock()
    {
        //Arrange
        const int gapFrames = 200;
        var decoder = new ConcealingPacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = Feed(Packet(0), AudioPacket.Loss(gapFrames), Packet(1));
        using var adapter = NewAdapter(decoder, source);

        //Act
        DrainAll(adapter);

        //Assert
        var frames = (2 * FramesPerPacket) + gapFrames;
        adapter.Position.Should().Be(TimeSpan.FromSeconds(frames / (double)SampleRate));
    }

    [Fact]
    public void A_gap_is_not_the_end_of_the_stream()
    {
        //Arrange
        var decoder = new ConcealingPacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = Feed(AudioPacket.Loss(50), Packet(0));
        using var adapter = NewAdapter(decoder, source);

        //Act
        var delivered = DrainAll(adapter);

        //Assert
        // A stream that opens with a gap still plays what follows it.
        delivered.Count.Should().Be((50 + FramesPerPacket) * Channels);
        delivered[0].Should().Be(ConcealingPacketSoundDecoder.ConcealmentValue);
    }

    [Fact]
    public void A_gap_and_a_trailing_trim_work_together()
    {
        //Arrange
        const int gapFrames = 100;
        const int trimFrames = 60;
        var decoder = new ConcealingPacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = Feed(Packet(0), AudioPacket.Loss(gapFrames), Packet(1), Packet(2));
        using var adapter = NewAdapter(decoder, source);
        adapter.SetTrailingTrimFrames(trimFrames);

        //Act
        var delivered = DrainAll(adapter);

        //Assert
        // Concealed audio flows through the same hold-back as decoded audio; the trim still takes
        // exactly the last of the track.
        var expectedFrames = (3 * FramesPerPacket) + gapFrames - trimFrames;
        delivered.Count.Should().Be(expectedFrames * Channels);
    }

    [Fact]
    public void A_gap_the_codecs_pre_skip_lands_on_is_discarded_like_any_other_audio()
    {
        //Arrange
        const int preSkipFrames = 40;
        const int gapFrames = 30;
        var decoder = new ConcealingPacketSoundDecoder(Channels, SampleRate, FramesPerPacket)
        {
            PreSkipSamples = preSkipFrames
        };
        var source = Feed(AudioPacket.Loss(gapFrames), Packet(0));
        using var adapter = NewAdapter(decoder, source);

        //Act
        var delivered = DrainAll(adapter);

        //Assert
        // The whole gap and ten frames of the packet after it fall inside the priming discard.
        delivered.Count.Should().Be(((gapFrames + FramesPerPacket) - preSkipFrames) * Channels);
        delivered[0].Should().Be(preSkipFrames - gapFrames);
    }

    [Fact]
    public void A_reposition_forgets_a_gap_that_had_not_been_covered_yet()
    {
        //Arrange
        var decoder = new ConcealingPacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = new PushablePacketSource();
        source.Add(AudioPacket.Loss(1000));
        using var adapter = NewAdapter(decoder, source);
        var buffer = new float[ReadFrames * Channels];
        adapter.Decode(buffer);   // starts covering the gap, nowhere near finishing it

        //Act
        source.Clear();
        source.Add(Packet(0));
        source.EndWhenDrained = true;
        adapter.Reposition(TimeSpan.FromSeconds(3), 0);
        var delivered = DrainAll(adapter);

        //Assert
        // The gap belonged to the position that was left behind.
        delivered.Count.Should().Be(FramesPerPacket * Channels);
        delivered[0].Should().Be(0f);   // decoded audio from the new position, nothing concealed
        delivered.Should().NotContain(ConcealingPacketSoundDecoder.ConcealmentValue);
    }

    [Fact]
    public void A_gap_of_no_length_costs_nothing()
    {
        //Arrange
        var decoder = new ConcealingPacketSoundDecoder(Channels, SampleRate, FramesPerPacket);
        var source = Feed(Packet(0), AudioPacket.Loss(TimeSpan.Zero), Packet(1));
        using var adapter = NewAdapter(decoder, source);

        //Act
        var delivered = DrainAll(adapter);

        //Assert
        delivered.Count.Should().Be(2 * FramesPerPacket * Channels);
        decoder.ConcealCallCount.Should().Be(0);
    }

    [Fact]
    public void A_loss_packet_says_what_it_is()
    {
        //Arrange / Act
        var byDuration = AudioPacket.Loss(TimeSpan.FromMilliseconds(20));
        var byFrames = AudioPacket.Loss(960, TimeSpan.FromSeconds(4));
        var ordinary = new AudioPacket(new byte[] { 1 });

        //Assert
        byDuration.IsLoss.Should().BeTrue();
        byDuration.IsEmpty.Should().BeTrue();
        byDuration.LossDuration.Should().Be(TimeSpan.FromMilliseconds(20));
        byDuration.LossFrames.Should().Be(0);

        byFrames.IsLoss.Should().BeTrue();
        byFrames.LossFrames.Should().Be(960);
        byFrames.LossDuration.Should().Be(TimeSpan.Zero);
        byFrames.Timestamp.HasValue.Should().BeTrue();
        byFrames.Timestamp.Value.Should().Be(TimeSpan.FromSeconds(4));

        ordinary.IsLoss.Should().BeFalse();
        ordinary.LossFrames.Should().Be(0);
        ordinary.DiscardPadding.Should().Be(TimeSpan.Zero);
    }

    [Fact]
    public void A_negative_gap_is_no_gap()
    {
        //Arrange / Act
        var negativeDuration = AudioPacket.Loss(TimeSpan.FromMilliseconds(-5));
        var negativeFrames = AudioPacket.Loss(-100);
        var negativePadding = new AudioPacket(new byte[] { 1 }, null, TimeSpan.FromMilliseconds(-5));

        //Assert
        negativeDuration.LossDuration.Should().Be(TimeSpan.Zero);
        negativeFrames.LossFrames.Should().Be(0);
        negativePadding.DiscardPadding.Should().Be(TimeSpan.Zero);
    }

    // ----- helpers -----

    private static PacketAudioPlayer.PacketDecoderAdapter NewAdapter(
        IPacketSoundDecoder decoder, IAudioPacketSource source) =>
        new PacketAudioPlayer.PacketDecoderAdapter(decoder, source, ownsDecoder: false,
            channels: Channels, sampleRate: SampleRate);

    private static AudioPacket Packet(int id) => new AudioPacket(new byte[] { (byte)id });

    private static FakeAudioPacketQueue Feed(params AudioPacket[] packets) =>
        new FakeAudioPacketQueue(packets) { EndWhenDrained = true };

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

    /// <summary>A decoder that offers no concealment at all, so the player has to fill the gap.</summary>
    private sealed class NoConcealmentDecoder : IPacketSoundDecoder
    {
        private int nextFrameValue;

        public int Channels => PacketAudioPlayerLossTests.Channels;

        public int SampleRate => PacketAudioPlayerLossTests.SampleRate;

        public SampleFormat SampleFormat => SampleFormat.F32;

        public int MaxSamplesPerPacket => FramesPerPacket * Channels;

        public int PreSkipSamples => 0;

        public int DecodePacket(ReadOnlySpan<byte> packet, Span<float> output)
        {
            if (packet.IsEmpty) return 0;

            for (var frame = 0; frame < FramesPerPacket; frame++)
            {
                for (var channel = 0; channel < Channels; channel++)
                {
                    output[(frame * Channels) + channel] = nextFrameValue;
                }
                nextFrameValue++;
            }

            return FramesPerPacket * Channels;
        }

        public int ConcealLoss(int lostFrames, Span<float> output) => 0;

        public void Reset()
        {
        }

        public void Dispose()
        {
        }
    }

    /// <summary>A source a test can add to and empty as it goes, for repositioning mid-gap.</summary>
    private sealed class PushablePacketSource : IAudioPacketSource
    {
        private readonly Queue<AudioPacket> packets = new Queue<AudioPacket>();

        public bool EndWhenDrained { get; set; }

        public bool EndOfStream => EndWhenDrained && packets.Count == 0;

        public void Add(AudioPacket packet) => packets.Enqueue(packet);

        public void Clear() => packets.Clear();

        public bool TryReadPacket(out AudioPacket packet)
        {
            if (packets.Count == 0)
            {
                packet = default;
                return false;
            }

            packet = packets.Dequeue();
            return true;
        }
    }
}
