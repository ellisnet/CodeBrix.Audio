using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Covers <see cref="AudioFileWriterRegistry"/> and the two writers it ships with - the write-side
/// mirror of <see cref="AudioFileReaderRegistry"/>.
/// </summary>
/// <remarks>
/// Every round trip here writes through <see cref="IAudioFileWriter"/> and reads back with this
/// library's own readers, because a file that only this library can read is not a file format.
/// </remarks>
public class AudioFileWriterRegistryTests
{
    private const int Rate = 22050;

    // A short ramp with both signs and a value at each end, so a bit depth that truncates or a
    // byte order that swaps shows up immediately.
    private static float[] BuildRamp(int frames)
    {
        var samples = new float[frames * 2];

        for (var frame = 0; frame < frames; frame++)
        {
            var position = frame / (float)(frames - 1);
            samples[frame * 2] = -0.9F + 1.8F * position;
            samples[frame * 2 + 1] = 0.9F - 1.8F * position;
        }

        return samples;
    }

    // ----- what is registered out of the box -----

    [Fact]
    public void wav_and_aiff_are_registered_out_of_the_box()
    {
        //Act
        var extensions = AudioFileWriterRegistry.SupportedExtensions.ToArray();

        //Assert
        extensions.Should().Contain(".wav");
        extensions.Should().Contain(".aif");
        extensions.Should().Contain(".aiff");
    }

    [Theory]
    [InlineData(".wav")]
    [InlineData("wav")]
    [InlineData(".WAV")]
    [InlineData("tune.wav")]
    [InlineData("/tmp/some folder/tune.WaV")]
    public void Resolve_takes_a_file_name_or_an_extension_in_any_case(string request)
    {
        //Act
        var factory = AudioFileWriterRegistry.Resolve(request);

        //Assert
        factory.Extensions.Should().Contain(".wav");
    }

    [Fact]
    public void Supports_answers_without_throwing()
    {
        //Act & Assert
        AudioFileWriterRegistry.Supports("tune.wav").Should().BeTrue();
        AudioFileWriterRegistry.Supports(".AIFF").Should().BeTrue();
        AudioFileWriterRegistry.Supports(".xyz").Should().BeFalse();
        AudioFileWriterRegistry.Supports(null).Should().BeFalse();
    }

    [Fact]
    public void Resolve_rejects_an_unknown_extension_by_name_and_lists_what_is_registered()
    {
        //Act
        var act = () => AudioFileWriterRegistry.Resolve("song.xyz");

        //Assert
        var thrown = act.Should().Throw<NotSupportedException>().Which;
        thrown.Message.Should().Contain("'.xyz'");
        thrown.Message.Should().Contain(".wav");
        thrown.Message.Should().Contain("AudioFileWriterRegistry.Register");
    }

    [Fact]
    public void Resolve_rejects_a_blank_request()
    {
        //Act
        var act = () => AudioFileWriterRegistry.Resolve("   ");

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    // ----- WAV -----

    [Fact]
    public void wav_defaults_to_thirty_two_bit_float()
    {
        //Arrange
        var factory = AudioFileWriterRegistry.Resolve(".wav");

        //Act
        var format = factory.DefaultFormat(Rate, 2);

        //Assert
        // This is what SoundFontRenderer.RenderToWavFile has always written, and it is what an
        // offline render produces: nothing is quantised on the way to disk.
        format.Encoding.Should().Be(WaveFormatEncoding.IeeeFloat);
        format.BitsPerSample.Should().Be(32);
        format.SampleRate.Should().Be(Rate);
        format.Channels.Should().Be(2);
    }

    [Fact]
    public void a_float_wav_round_trips_through_the_writer_interface()
    {
        //Arrange
        var written = BuildRamp(64);
        using var stream = new MemoryStream();

        //Act
        using (var writer = AudioFileWriterRegistry.Create(".wav", stream, Rate, 2))
        {
            writer.Write(written, 0, written.Length);
            writer.Finish();
        }

        //Assert
        var readBack = ReadWavSamples(stream);
        readBack.Should().HaveCount(written.Length);

        for (var index = 0; index < written.Length; index++)
        {
            readBack[index].Should().BeApproximately(written[index], 1e-6F);
        }
    }

    [Fact]
    public void a_sixteen_bit_wav_is_written_when_the_format_asks_for_it()
    {
        //Arrange
        var written = BuildRamp(64);
        var format = new WaveFormat(Rate, 16, 2);
        using var stream = new MemoryStream();

        //Act
        using (var writer = AudioFileWriterRegistry.Create(".wav", stream, format))
        {
            writer.Write(written, 0, written.Length);
            writer.Finish();
        }

        //Assert
        stream.Position = 0;
        using var reader = new WaveFileReader(stream);
        reader.WaveFormat.Encoding.Should().Be(WaveFormatEncoding.Pcm);
        reader.WaveFormat.BitsPerSample.Should().Be(16);
        reader.SampleCount.Should().Be(64);

        var readBack = ReadFrames(reader).SelectMany(frame => frame).ToArray();

        for (var index = 0; index < written.Length; index++)
        {
            // Sixteen bits is one part in 32,768, so the tolerance is the quantiser rather than
            // anything to do with the writer.
            readBack[index].Should().BeApproximately(written[index], 1e-4F);
        }
    }

    [Fact]
    public void the_wav_writer_reports_what_it_has_written()
    {
        //Arrange
        var written = BuildRamp(16);
        using var stream = new MemoryStream();

        //Act
        using var writer = AudioFileWriterRegistry.Create(".wav", stream, Rate, 2);
        writer.Write(written, 0, written.Length);

        //Assert
        writer.SamplesWritten.Should().Be(written.Length);
        writer.WaveFormat.Channels.Should().Be(2);
    }

    [Fact]
    public void the_wav_writer_accepts_a_span()
    {
        //Arrange
        var written = BuildRamp(16);
        using var stream = new MemoryStream();

        //Act
        using (var writer = AudioFileWriterRegistry.Create(".wav", stream, Rate, 2))
        {
            writer.Write(written.AsSpan());
        }

        //Assert
        ReadWavSamples(stream).Should().HaveCount(written.Length);
    }

    // ----- AIFF -----

    [Fact]
    public void aiff_defaults_to_sixteen_bit_pcm()
    {
        //Arrange
        var factory = AudioFileWriterRegistry.Resolve(".aiff");

        //Act
        var format = factory.DefaultFormat(Rate, 2);

        //Assert
        // AIFF has no container for float samples without AIFF-C, and a reader would take the
        // bytes for PCM and produce noise.
        format.Encoding.Should().Be(WaveFormatEncoding.Pcm);
        format.BitsPerSample.Should().Be(16);
    }

    [Fact]
    public void an_aiff_round_trips_through_the_writer_interface()
    {
        //Arrange
        var written = BuildRamp(64);
        using var stream = new MemoryStream();

        //Act
        using (var writer = AudioFileWriterRegistry.Create(".aiff", stream, Rate, 2))
        {
            writer.Write(written, 0, written.Length);
            writer.Finish();
        }

        //Assert
        stream.Position = 0;
        using var reader = new AiffFileReader(stream);
        reader.WaveFormat.SampleRate.Should().Be(Rate);
        reader.WaveFormat.Channels.Should().Be(2);
        reader.WaveFormat.BitsPerSample.Should().Be(16);
        reader.SampleCount.Should().Be(64);

        var readBack = ReadAiffSamples(reader);

        for (var index = 0; index < written.Length; index++)
        {
            readBack[index].Should().BeApproximately(written[index], 1e-4F);
        }
    }

    [Fact]
    public void the_aiff_writer_refuses_float_samples()
    {
        //Arrange
        var factory = AudioFileWriterRegistry.Resolve(".aif");
        using var stream = new MemoryStream();

        //Act
        var act = () => factory.Create(stream, WaveFormat.CreateIeeeFloatWaveFormat(Rate, 2));

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*AIFF-C*");
    }

    // ----- seeking -----

    [Fact]
    public void wav_and_aiff_both_declare_that_they_need_to_seek()
    {
        //Act & Assert
        AudioFileWriterRegistry.Resolve(".wav").RequiresSeekableStream.Should().BeTrue();
        AudioFileWriterRegistry.Resolve(".aiff").RequiresSeekableStream.Should().BeTrue();
    }

    [Fact]
    public void the_wav_writer_refuses_a_stream_it_cannot_seek()
    {
        //Arrange
        using var forwardOnly = new ForwardOnlyStream();

        //Act
        var act = () => AudioFileWriterRegistry.Create(".wav", forwardOnly, Rate, 2);

        //Assert
        // The header carries a length that is only known at the end, so a forward-only stream
        // would leave a file nothing could read.
        act.Should().Throw<ArgumentException>().WithMessage("*seek*");
    }

    [Fact]
    public void the_aiff_writer_refuses_a_stream_it_cannot_seek()
    {
        //Arrange
        using var forwardOnly = new ForwardOnlyStream();

        //Act
        var act = () => AudioFileWriterRegistry.Create(".aiff", forwardOnly, Rate, 2);

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*seek*");
    }

    [Fact]
    public void a_writer_that_needs_no_seeking_works_on_a_forward_only_stream()
    {
        //Arrange
        var factory = new ForwardOnlyWriterFactory();
        using var forwardOnly = new ForwardOnlyStream();

        //Act
        using (var writer = factory.Create(forwardOnly, factory.DefaultFormat(Rate, 2)))
        {
            writer.Write(BuildRamp(8), 0, 16);
            writer.Finish();
        }

        //Assert
        // This is the shape a future Ogg or Opus writer has: written strictly forwards, so it can
        // go to a pipe or a socket.
        forwardOnly.BytesWritten.Should().BeGreaterThan(0);
    }

    [Fact]
    public void a_registered_third_format_is_reachable_by_its_extension()
    {
        //Arrange
        var factory = new ForwardOnlyWriterFactory();

        try
        {
            //Act
            AudioFileWriterRegistry.Register(factory);

            //Assert
            AudioFileWriterRegistry.Supports("tune.fake").Should().BeTrue();
            AudioFileWriterRegistry.Resolve(".FAKE").Should().BeSameAs(factory);
            AudioFileWriterRegistry.SupportedExtensions.Should().Contain(".fake");
        }
        finally
        {
            AudioFileWriterRegistry.ResetForTesting();
        }
    }

    [Fact]
    public void Register_rejects_a_factory_with_nothing_to_offer()
    {
        //Act
        var nullFactory = () => AudioFileWriterRegistry.Register(null);
        var noExtensions = () => AudioFileWriterRegistry.Register(new ForwardOnlyWriterFactory([]));
        var blankExtension = () => AudioFileWriterRegistry.Register(new ForwardOnlyWriterFactory(["  "]));

        //Assert
        nullFactory.Should().Throw<ArgumentNullException>();
        noExtensions.Should().Throw<ArgumentException>();
        blankExtension.Should().Throw<ArgumentException>();
    }

    // ----- the writer's contract -----

    [Fact]
    public void a_writer_leaves_the_stream_it_was_handed_open()
    {
        //Arrange
        using var stream = new MemoryStream();

        //Act
        using (var writer = AudioFileWriterRegistry.Create(".wav", stream, Rate, 2))
        {
            writer.Write(BuildRamp(8), 0, 16);
        }

        //Assert
        // The caller opened the stream and the caller closes it; finishing the FILE is a different
        // thing from closing the stream underneath it.
        stream.CanRead.Should().BeTrue();
        stream.Length.Should().BeGreaterThan(44);
    }

    [Fact]
    public void Finish_can_be_called_twice()
    {
        //Arrange
        using var stream = new MemoryStream();
        var writer = AudioFileWriterRegistry.Create(".wav", stream, Rate, 2);
        writer.Write(BuildRamp(8), 0, 16);

        //Act
        writer.Finish();
        var act = () => writer.Finish();

        //Assert
        act.Should().NotThrow();
        writer.Dispose();
    }

    [Fact]
    public void writing_after_Finish_is_an_error()
    {
        //Arrange
        using var stream = new MemoryStream();
        using var writer = AudioFileWriterRegistry.Create(".wav", stream, Rate, 2);
        writer.Write(BuildRamp(8), 0, 16);
        writer.Finish();

        //Act
        var act = () => writer.Write(BuildRamp(8), 0, 16);

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void a_writer_checks_the_buffer_it_is_handed()
    {
        //Arrange
        using var stream = new MemoryStream();
        using var writer = AudioFileWriterRegistry.Create(".wav", stream, Rate, 2);

        //Act
        var nullBuffer = () => writer.Write(null, 0, 1);
        var badOffset = () => writer.Write(new float[4], -1, 1);
        var badCount = () => writer.Write(new float[4], 2, 3);

        //Assert
        nullBuffer.Should().Throw<ArgumentNullException>();
        badOffset.Should().Throw<ArgumentOutOfRangeException>();
        badCount.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void a_factory_rejects_a_stream_that_cannot_be_written_to()
    {
        //Arrange
        using var readOnly = new MemoryStream(new byte[16], writable: false);

        //Act
        var act = () => AudioFileWriterRegistry.Create(".wav", readOnly, Rate, 2);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void a_factory_rejects_a_sample_rate_or_channel_count_that_is_not_positive()
    {
        //Arrange
        var factory = AudioFileWriterRegistry.Resolve(".wav");

        //Act
        var badRate = () => factory.DefaultFormat(0, 2);
        var badChannels = () => factory.DefaultFormat(Rate, 0);

        //Assert
        badRate.Should().Throw<ArgumentOutOfRangeException>();
        badChannels.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void a_wav_factory_can_be_built_with_a_different_default_depth()
    {
        //Arrange & Act
        var sixteenBit = new WavAudioFileWriterFactory(16);
        var badDepth = () => new WavAudioFileWriterFactory(8);

        //Assert
        sixteenBit.DefaultBitsPerSample.Should().Be(16);
        sixteenBit.DefaultFormat(Rate, 2).Encoding.Should().Be(WaveFormatEncoding.Pcm);
        badDepth.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void an_aiff_factory_can_be_built_with_a_different_default_depth()
    {
        //Arrange & Act
        var twentyFourBit = new AiffAudioFileWriterFactory(24);
        var badDepth = () => new AiffAudioFileWriterFactory(32);

        //Assert
        twentyFourBit.DefaultBitsPerSample.Should().Be(24);
        twentyFourBit.DefaultFormat(Rate, 2).BitsPerSample.Should().Be(24);
        badDepth.Should().Throw<ArgumentOutOfRangeException>();
    }

    private static float[] ReadWavSamples(MemoryStream stream)
    {
        stream.Position = 0;
        using var reader = new WaveFileReader(stream);
        return ReadFrames(reader).SelectMany(frame => frame).ToArray();
    }

    private static IEnumerable<float[]> ReadFrames(WaveFileReader reader)
    {
        while (true)
        {
            var frame = reader.ReadNextSampleFrame();

            if (frame == null)
            {
                yield break;
            }

            yield return frame;
        }
    }

    private static float[] ReadAiffSamples(AiffFileReader reader)
    {
        // The AIFF reader hands back little-endian PCM bytes, having swapped them on the way out.
        var raw = new byte[reader.Length];
        var read = reader.Read(raw, 0, raw.Length);
        var samples = new float[read / 2];

        for (var index = 0; index < samples.Length; index++)
        {
            samples[index] = BitConverter.ToInt16(raw, index * 2) / 32768F;
        }

        return samples;
    }
}
