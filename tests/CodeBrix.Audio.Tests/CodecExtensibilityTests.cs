using System;
using System.IO;
using CodeBrix.Audio.Codecs;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Engine.Structs;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests the seams an add-on package uses to bring its own codec to CodeBrix.Audio, and the errors
/// a user gets for a format nothing installed can decode.
/// </summary>
/// <remarks>
/// The motivating case is Ogg Opus: it shares the .ogg container with Vorbis (and often takes the
/// .opus extension), so the engine offers it to every Ogg-capable codec. A codec that cannot handle
/// it must decline cleanly, and the resulting failure must name Opus rather than blaming "ogg".
/// </remarks>
public class CodecExtensibilityTests
{
    /// <summary>An Ogg Opus stream, built from a real Opus identification header.</summary>
    private static MemoryStream BuildOpusStream()
    {
        // One Ogg page carrying an OpusHead identification packet. Enough for codec detection,
        // which is all these tests exercise.
        var packet = new byte[19];
        "OpusHead"u8.CopyTo(packet);
        packet[8] = 1;                                    // version
        packet[9] = 2;                                    // channels
        BitConverter.GetBytes((short)312).CopyTo(packet, 10);  // pre-skip
        BitConverter.GetBytes(48000).CopyTo(packet, 12);       // input sample rate

        var page = new byte[27 + 1 + packet.Length];
        "OggS"u8.CopyTo(page);
        page[4] = 0;                                      // version
        page[5] = 0x02;                                   // first page of the logical bitstream
        page[26] = 1;                                     // one segment
        page[27] = (byte)packet.Length;                   // its length
        packet.CopyTo(page, 28);

        return new MemoryStream(page);
    }

    // ---------------------------------------------------------------------------------------
    // Codec identification
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void An_ogg_stream_is_identified_by_the_codec_inside_it()
    {
        //Arrange / Act / Assert
        using var vorbis = TestAssets.Open(TestAssets.VorbisToneStereo);
        OggCodecSniffer.Identify(vorbis).Should().Be(OggCodec.Vorbis);

        using var opus = BuildOpusStream();
        OggCodecSniffer.Identify(opus).Should().Be(OggCodec.Opus);

        using var flac = TestAssets.Open(TestAssets.FlacToneStereo);
        OggCodecSniffer.Identify(flac).Should().Be(OggCodec.NotOgg); // native FLAC, not Ogg FLAC
    }

    [Fact]
    public void Identifying_a_stream_leaves_its_position_alone()
    {
        //Arrange
        using var stream = TestAssets.Open(TestAssets.VorbisToneStereo);
        stream.Position = 3;

        //Act
        OggCodecSniffer.Identify(stream);

        //Assert
        stream.Position.Should().Be(3);
    }

    // ---------------------------------------------------------------------------------------
    // Declining cleanly, so another codec can take the stream
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void The_vorbis_codec_declines_an_opus_stream_instead_of_failing_on_it()
    {
        //Arrange
        // This is what lets a separately packaged Opus codec work: the engine tries factories in
        // turn, and a factory that accepted-then-threw would leave the stream half-read.
        var factory = new VorbisCodecFactory();
        using var stream = BuildOpusStream();
        var format = new AudioFormat { Format = SampleFormat.F32, Channels = 2, SampleRate = 48000 };

        //Act
        var decoder = factory.CreateDecoder(stream, "ogg", format);
        var probed = factory.TryCreateDecoder(stream, out _);

        //Assert
        decoder.Should().BeNull();
        probed.Should().BeNull();
        stream.Position.Should().Be(0);
    }

    // ---------------------------------------------------------------------------------------
    // Errors that name the actual codec
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Opening_an_opus_stream_as_vorbis_says_it_is_opus()
    {
        //Arrange
        using var stream = BuildOpusStream();

        //Act
        var open = () => new OggVorbisFileReader(stream);

        //Assert
        // It used to surface the decoder's internal "Found OPUS bitstream." ArgumentException.
        open.Should().Throw<NotSupportedException>()
            .WithMessage("*Opus*");
    }

    [Fact]
    public void Loading_an_opus_clip_says_it_is_opus_rather_than_blaming_ogg()
    {
        //Arrange
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".opus");
        File.WriteAllBytes(path, BuildOpusStream().ToArray());

        try
        {
            //Act
            var load = () => SoundEffectClip.Load(path);

            //Assert
            // The engine's own message is "No registered and working codec factory found for
            // decoding format 'ogg'", which is baffling for a file the user knows is .opus.
            load.Should().Throw<NotSupportedException>()
                .WithMessage("*Opus*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    // ---------------------------------------------------------------------------------------
    // The registration seams themselves
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void The_built_in_readers_are_registered_by_extension()
    {
        //Assert
        AudioFileReaderRegistry.SupportedExtensions.Should().Contain(".wav", ".mp3", ".ogg", ".flac");
        AudioFileReaderRegistry.Supports("music.ogg").Should().BeTrue();
        AudioFileReaderRegistry.Supports(".flac").Should().BeTrue();
        AudioFileReaderRegistry.Supports("voice.opus").Should().BeFalse();
    }

    [Fact]
    public void An_add_on_package_can_register_a_reader_for_a_new_extension()
    {
        //Arrange
        // Exactly what a separately packaged codec would do at start-up. A WAV reader stands in
        // for the real decoder; what is being tested is the wiring, not the codec.
        AudioFileReaderRegistry.Register(".codebrixtest", stream => new WaveFileReader(stream));

        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".codebrixtest");
        TestAudio.WriteSineWaveFile(Path.ChangeExtension(path, ".wav"));
        File.Move(Path.ChangeExtension(path, ".wav"), path);

        try
        {
            //Act
            using var reader = new AudioFileReader(path);
            var samples = new float[512];
            var read = ((ISampleProvider)reader).Read(samples);

            //Assert
            AudioFileReaderRegistry.Supports(path).Should().BeTrue();
            read.Should().BeGreaterThan(0);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void An_unregistered_extension_reports_what_is_registered()
    {
        //Arrange
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".notanaudioformat");
        File.WriteAllBytes(path, new byte[] { 1, 2, 3, 4 });

        try
        {
            //Act
            var open = () => new AudioFileReader(path);

            //Assert
            open.Should().Throw<InvalidOperationException>().WithMessage("*.wav*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void An_add_on_package_can_register_a_codec_with_the_shared_output()
    {
        //Arrange
        // The other half of the seam: registering a reader covers opening files by name, while
        // this covers playback, which goes through the audio engine.
        var factory = new StubCodecFactory();

        try
        {
            //Act
            SharedAudioOutput.RegisterCodecFactory(factory);
            SharedAudioOutput.RegisterCodecFactory(factory); // idempotent

            //Assert
            SharedAudioOutput.RegisteredCodecFactories.Should().Contain(factory);
            SharedAudioOutput.RegisteredCodecFactories.Count.Should().Be(1);
        }
        finally
        {
            SharedAudioOutput.Shutdown();
        }
    }

    [Fact]
    public void Registering_a_null_codec_factory_throws()
    {
        //Act
        var register = () => SharedAudioOutput.RegisterCodecFactory(null);

        //Assert
        register.Should().Throw<ArgumentNullException>();
    }

    /// <summary>A do-nothing codec factory, standing in for one an add-on package would supply.</summary>
    private sealed class StubCodecFactory : ICodecFactory
    {
        public string FactoryId => "CodeBrix.Audio.Tests.Stub";

        public System.Collections.Generic.IReadOnlyCollection<string> SupportedFormatIds { get; } =
            new[] { "codebrixtest" };

        public int Priority => -100;

        public ISoundDecoder CreateDecoder(Stream stream, string formatId, AudioFormat format) => null;

        public ISoundDecoder TryCreateDecoder(Stream stream, out AudioFormat detectedFormat, AudioFormat? hintFormat = null)
        {
            detectedFormat = hintFormat ?? default;
            return null;
        }

        public ISoundEncoder CreateEncoder(Stream stream, string formatId, AudioFormat format) => null;
    }
}
