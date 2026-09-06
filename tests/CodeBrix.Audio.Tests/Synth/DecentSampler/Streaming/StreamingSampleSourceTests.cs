using System;
using System.IO;
using CodeBrix.Audio.Synth.DecentSampler.Streaming;
using CodeBrix.Audio.Synth.Sfz;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Streaming;

/// <summary>
/// The streaming source on its own: the preload head, seeking, and the promise that a frame read off
/// disk is bit-for-bit the frame the in-memory decoder would have produced.
/// </summary>
public class StreamingSampleSourceTests
{
    [Fact]
    public void the_header_is_read_without_decoding_the_audio()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        var path = fixtures.WriteSineWav("Samples/tone.wav", 220.0, frames: 40000);

        //Act
        using var source = StreamingSampleSource.Open(File.OpenRead(path), path, 256);

        //Assert
        source.Frames.Should().Be(40000);
        source.ChannelCount.Should().Be(1);
        source.SampleRate.Should().Be(44100);
        source.PreloadFrameCount.Should().Be(256);
        source.IsInMemory.Should().BeFalse();
        source.DecodedByteCount.Should().Be(256 * sizeof(float));
        source.FullyDecodedByteCount.Should().Be(40000 * sizeof(float));
    }

    [Fact]
    public void every_frame_matches_the_in_memory_decode()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        var path = fixtures.WriteSineWav("Samples/tone.wav", 220.0, frames: 12000);
        var decoded = SfzSampleData.Load(path);

        using var source = StreamingSampleSource.Open(File.OpenRead(path), path, 100);

        //Act - read in awkward pieces so the head, the seek and the forward read all take part.
        var streamed = ReadWhole(source, 777);

        //Assert
        streamed.Should().Equal(decoded.Channels[0]);
    }

    [Fact]
    public void a_backwards_seek_lands_on_the_right_frame()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        var path = fixtures.WriteSineWav("Samples/tone.wav", 220.0, frames: 12000);
        var decoded = SfzSampleData.Load(path);

        using var source = StreamingSampleSource.Open(File.OpenRead(path), path, 8);

        //Act - forward, far forward, then back behind both.
        var late = Read(source, 9000, 64);
        var later = Read(source, 11000, 64);
        var early = Read(source, 500, 64);

        //Assert
        late.Should().Equal(decoded.Channels[0].AsSpan(9000, 64).ToArray());
        later.Should().Equal(decoded.Channels[0].AsSpan(11000, 64).ToArray());
        early.Should().Equal(decoded.Channels[0].AsSpan(500, 64).ToArray());
    }

    [Fact]
    public void the_head_serves_reads_without_the_decoder()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        var path = fixtures.WriteSineWav("Samples/tone.wav", 220.0, frames: 12000);
        var decoded = SfzSampleData.Load(path);

        using var source = StreamingSampleSource.Open(File.OpenRead(path), path, 1000);

        //Act
        var planar = new float[1][];
        planar[0] = new float[400];
        var read = source.ReadFromHead(100, planar, 0, 400);

        //Assert
        read.Should().Be(400);
        planar[0].Should().Equal(decoded.Channels[0].AsSpan(100, 400).ToArray());
    }

    [Fact]
    public void the_head_gives_nothing_past_its_end() =>
        WithSource(1000, 12000, source =>
        {
            var planar = new float[1][];
            planar[0] = new float[64];
            source.ReadFromHead(5000, planar, 0, 64).Should().Be(0);
        });

    [Fact]
    public void an_embedded_loop_is_read_from_the_smpl_chunk()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        var path = fixtures.WriteSmplLoopWav("Samples/loop.wav", 1000, 1000, 1999);

        //Act
        using var source = StreamingSampleSource.Open(File.OpenRead(path), path, 128);

        //Assert
        source.HasEmbeddedLoop.Should().BeTrue();
        source.EmbeddedLoopStart.Should().Be(1000);
        source.EmbeddedLoopEnd.Should().Be(1999);
    }

    [Fact]
    public void a_stereo_source_keeps_its_channels_apart()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        var path = fixtures.WriteStereoConstantWav("Samples/stereo.wav", 0.25f, -0.5f, frames: 5000);

        using var source = StreamingSampleSource.Open(File.OpenRead(path), path, 16);

        //Act
        var planar = new float[2][];
        planar[0] = new float[64];
        planar[1] = new float[64];
        source.ReadFrames(3000, planar, 0, 64);

        //Assert
        source.ChannelCount.Should().Be(2);
        planar[0].Should().AllSatisfy(value => value.Should().Be(0.25f));
        planar[1].Should().AllSatisfy(value => value.Should().Be(-0.5f));
    }

    [Fact]
    public void a_read_past_the_end_gives_nothing() =>
        WithSource(64, 5000, source =>
        {
            var planar = new float[1][];
            planar[0] = new float[64];
            source.ReadFrames(5000, planar, 0, 64).Should().Be(0);
        });

    [Fact]
    public void the_isamplesource_read_overload_agrees_with_the_bulk_one()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        var path = fixtures.WriteSineWav("Samples/tone.wav", 220.0, frames: 5000);
        var decoded = SfzSampleData.Load(path);

        using var source = StreamingSampleSource.Open(File.OpenRead(path), path, 64);

        //Act
        var destination = new float[128];
        var read = source.Read(0, 2000, destination);

        //Assert
        read.Should().Be(128);
        destination.Should().Equal(decoded.Channels[0].AsSpan(2000, 128).ToArray());
    }

    private static void WithSource(int preloadFrames, int frames, Action<StreamingSampleSource> body)
    {
        using var fixtures = DecentSamplerEngineFixtures.Create();
        var path = fixtures.WriteSineWav("Samples/tone.wav", 220.0, frames: frames);

        using var source = StreamingSampleSource.Open(File.OpenRead(path), path, preloadFrames);
        body(source);
    }

    private static float[] Read(StreamingSampleSource source, long startFrame, int count)
    {
        var planar = new float[source.ChannelCount][];
        for (var c = 0; c < source.ChannelCount; c++)
        {
            planar[c] = new float[count];
        }

        source.ReadFrames(startFrame, planar, 0, count);
        return planar[0];
    }

    private static float[] ReadWhole(StreamingSampleSource source, int chunk)
    {
        var result = new float[source.Frames];
        var planar = new float[source.ChannelCount][];
        for (var c = 0; c < source.ChannelCount; c++)
        {
            planar[c] = new float[chunk];
        }

        var position = 0L;

        while (position < source.Frames)
        {
            var want = (int)Math.Min(chunk, source.Frames - position);
            var read = source.ReadFrames(position, planar, 0, want);

            if (read <= 0)
            {
                break;
            }

            Array.Copy(planar[0], 0, result, position, read);
            position += read;
        }

        return result;
    }
}
