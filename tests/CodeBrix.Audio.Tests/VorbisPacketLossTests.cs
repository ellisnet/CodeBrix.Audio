using System;
using System.Collections.Generic;
using CodeBrix.Audio.Codecs;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Tests.Utils;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// What the managed Vorbis packet decoder does about PACKET LOSS: Vorbis has no concealment of its
/// own, so a gap becomes silence of exactly the length that was lost, and the audio after it keeps
/// the position it had.
/// </summary>
/// <remarks>
/// Every test here runs against a real Ogg fixture taken apart into packets, and none of them needs
/// an audio device.
/// </remarks>
public class VorbisPacketLossTests
{
    // The three setup headers come first in every Ogg Vorbis stream; audio starts at packet 3.
    private const int FirstAudioPacket = 3;

    [Fact]
    public void Vorbis_offers_no_concealment_of_its_own()
    {
        //Arrange
        using var decoder = NewDecoder(TestAssets.VorbisToneStereo);

        //Act
        var supported = decoder.SupportsLossConcealment;

        //Assert
        // It says so plainly, so an application can tell silence from synthesised audio.
        supported.Should().BeFalse();
    }

    [Fact]
    public void An_empty_packet_yields_nothing_because_its_length_is_unknown()
    {
        //Arrange
        using var decoder = NewDecoder(TestAssets.VorbisToneStereo);
        var output = new float[decoder.MaxSamplesPerPacket];

        //Act
        var produced = decoder.DecodePacket(ReadOnlySpan<byte>.Empty, output);

        //Assert
        // The lengthless convention says a packet was lost without saying how long it was, and a
        // decoder with no concealment has nothing useful to do with that.
        produced.Should().Be(0);
    }

    [Fact]
    public void Concealing_a_gap_gives_back_silence_of_exactly_the_length_asked_for()
    {
        //Arrange
        const int gapFrames = 500;
        using var decoder = NewDecoder(TestAssets.VorbisToneStereo);
        var output = new float[decoder.MaxSamplesPerPacket];

        //Act
        var produced = decoder.ConcealLoss(gapFrames, output);

        //Assert
        var expected = Math.Min(gapFrames, decoder.MaxSamplesPerPacket / decoder.Channels);
        produced.Should().Be(expected * decoder.Channels);
        for (var i = 0; i < produced; i++)
        {
            output[i].Should().Be(0f);
        }
    }

    [Fact]
    public void A_gap_longer_than_one_helping_is_covered_over_several_calls()
    {
        //Arrange
        using var decoder = NewDecoder(TestAssets.VorbisToneStereo);
        var output = new float[decoder.MaxSamplesPerPacket];
        var perCall = decoder.MaxSamplesPerPacket / decoder.Channels;
        var gapFrames = (perCall * 3) + 17;
        var covered = 0;
        var calls = 0;

        //Act
        while (covered < gapFrames)
        {
            var produced = decoder.ConcealLoss(gapFrames - covered, output);
            produced.Should().BeGreaterThan(0);
            covered += produced / decoder.Channels;
            calls++;
        }

        //Assert
        // Not one frame more and not one frame less, however many calls it took.
        covered.Should().Be(gapFrames);
        calls.Should().Be(4);
    }

    [Fact]
    public void Concealing_nothing_produces_nothing()
    {
        //Arrange
        using var decoder = NewDecoder(TestAssets.VorbisToneStereo);
        var output = new float[decoder.MaxSamplesPerPacket];

        //Act
        var produced = decoder.ConcealLoss(0, output);

        //Assert
        produced.Should().Be(0);
    }

    [Fact]
    public void Two_lost_packets_become_silence_and_the_audio_after_them_stays_where_it_was()
    {
        //Arrange
        const int lostAt = 4;   // an audio packet well inside the stream
        var packets = OggPacketReader.ReadPackets(TestAssets.Path(TestAssets.VorbisToneStereo));
        var codecPrivate = OggPacketReader.BuildXiphCodecPrivate(packets[0], packets[1], packets[2]);
        var audio = packets.GetRange(FirstAudioPacket, packets.Count - FirstAudioPacket);

        // What an unbroken decode of the same packets produces, and how many frames each packet of
        // it finalised - the second is what says how long the gap has to be.
        var sizes = new List<int>();
        List<float> reference;
        int channels;
        using (var referenceDecoder = NewDecoder(TestAssets.VorbisToneStereo))
        {
            channels = referenceDecoder.Channels;
            reference = DecodeEachPacket(referenceDecoder, audio, sizes);
        }

        var gapFrames = sizes[lostAt] + sizes[lostAt + 1];
        var framesBeforeGap = 0;
        for (var i = 0; i < lostAt; i++) framesBeforeGap += sizes[i];

        // The same packets with two of them missing, and a loss packet in their place.
        var withGap = new List<AudioPacket>();
        for (var i = 0; i < audio.Count; i++)
        {
            if (i == lostAt)
            {
                withGap.Add(AudioPacket.Loss(gapFrames));
                continue;
            }
            if (i == lostAt + 1) continue;
            withGap.Add(new AudioPacket(audio[i]));
        }

        var decoder = NewDecoder(TestAssets.VorbisToneStereo);
        var source = new FakeAudioPacketQueue(withGap) { EndWhenDrained = true };
        using var adapter = new PacketAudioPlayer.PacketDecoderAdapter(decoder, source,
            ownsDecoder: true, channels: decoder.Channels, sampleRate: decoder.SampleRate);

        //Act
        var delivered = DrainAll(adapter, channels);

        //Assert
        // 1. The timeline keeps its length: what was lost was replaced, not skipped.
        delivered.Count.Should().Be(reference.Count);

        // 2. Everything before the gap is the same audio it always was.
        for (var i = 0; i < framesBeforeGap * channels; i++)
        {
            delivered[i].Should().Be(reference[i]);
        }

        // 3. The gap itself is silence, exactly as long as the packets that went missing.
        for (var i = framesBeforeGap * channels; i < (framesBeforeGap + gapFrames) * channels; i++)
        {
            delivered[i].Should().Be(0f);
        }

        // 4. Vorbis needs ONE packet to re-sync: the first packet after a gap is decoded against the
        //    overlap window the packet before the gap left behind, so what it finalises is wrong;
        //    from the packet after THAT the audio is sample-exact again. Measured on this fixture:
        //    the gap starts at frame 1728, runs for 2048 frames, and the last frame that differs
        //    from an unbroken decode is 1024 frames - exactly one packet - past the end of it.
        var resyncFrames = sizes[lostAt + 2];
        var from = (framesBeforeGap + gapFrames + resyncFrames) * channels;
        for (var i = from; i < delivered.Count; i++)
        {
            delivered[i].Should().Be(reference[i]);
        }
    }

    // ----- helpers -----

    private static IPacketSoundDecoder NewDecoder(string assetName)
    {
        var packets = OggPacketReader.ReadPackets(TestAssets.Path(assetName));
        var codecPrivate = OggPacketReader.BuildXiphCodecPrivate(packets[0], packets[1], packets[2]);
        return new VorbisPacketCodecFactory().CreateDecoder("vorbis", codecPrivate, null);
    }

    // Feeds every packet through the decoder one at a time, recording what each one finalised.
    private static List<float> DecodeEachPacket(IPacketSoundDecoder decoder, List<byte[]> audio, List<int> sizes)
    {
        var buffer = new float[decoder.MaxSamplesPerPacket];
        var samples = new List<float>();

        foreach (var packet in audio)
        {
            var produced = decoder.DecodePacket(packet, buffer);
            sizes.Add(produced / decoder.Channels);
            for (var i = 0; i < produced; i++)
            {
                samples.Add(buffer[i]);
            }
        }

        return samples;
    }

    private static List<float> DrainAll(PacketAudioPlayer.PacketDecoderAdapter adapter, int channels)
    {
        var buffer = new float[512 * channels];
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
}
