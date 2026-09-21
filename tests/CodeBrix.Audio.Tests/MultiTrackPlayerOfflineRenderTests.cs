using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Streaming;
using CodeBrix.Audio.Tests.Synth;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// What an OFFLINE render of a multi-track song has to get right beyond the samples: the streaming
/// mode a Decent Sampler synthesizer is driven in, and writing the result out through the registered
/// writers rather than as 32-bit float WAV alone.
/// </summary>
/// <remarks>
/// No audio device is opened here. That is the point: an offline render runs as fast as the machine
/// allows and is not an audio callback, which is exactly what the streaming mode is about.
/// </remarks>
public class MultiTrackPlayerOfflineRenderTests
{
    private const int RenderRate = 44100;

    private static readonly TimeSpan NoTail = TimeSpan.Zero;

    // The whole file streams, whatever its size, so the streaming path is exercised without a large
    // fixture.
    private const string StreamingPreset = """
        <DecentSampler>
          <groups>
            <group ampVelTrack="0" release="0.05" playbackMode="disk_streaming">
              <sample path="Samples/tone.wav" rootNote="69" loNote="0" hiNote="127" />
            </group>
          </groups>
        </DecentSampler>
        """;

    // ---------------------------------------------------------------------------------------
    // The streaming mode an offline render must set
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void Render_puts_a_decent_sampler_synthesizer_into_offline_streaming_and_back()
    {
        //Arrange
        using var fixtures = Fixtures();
        using var instrument = fixtures.LoadPreset(StreamingPreset);
        using var song = MultiTrackTestSong.Create();

        DecentSamplerSynthesizer built = null;
        var seenDuringRender = new List<DecentSamplerStreamingMode>();

        using var player = new MultiTrackPlayer();
        player.Add(new MidiTrack(
            MultiTrackTestSong.BuildSingleNoteSequence(69, 500, 2000),
            rate =>
            {
                built = new DecentSamplerSynthesizer(instrument, new DecentSamplerSynthesizerSettings(rate));
                return built;
            },
            "Sampled"));

        player.Add(new MidiTrack(
            MultiTrackTestSong.BuildSingleNoteSequence(60, 500, 2000),
            rate => new ObservingSynthesizer(rate, () => seenDuringRender.Add(built.StreamingMode)),
            "Observer"));

        //Act
        player.Render(RenderRate, NoTail);

        //Assert
        built.Should().NotBeNull();
        seenDuringRender.Should().NotBeEmpty();
        seenDuringRender.Should().OnlyContain(mode => mode == DecentSamplerStreamingMode.Offline);
        built.StreamingMode.Should().Be(DecentSamplerStreamingMode.RealTime);
    }

    [Fact]
    public void Render_puts_the_streaming_mode_back_even_when_the_render_throws()
    {
        //Arrange
        using var fixtures = Fixtures();
        using var instrument = fixtures.LoadPreset(StreamingPreset);

        DecentSamplerSynthesizer built = null;

        using var player = new MultiTrackPlayer();
        player.Add(new MidiTrack(
            MultiTrackTestSong.BuildSingleNoteSequence(69, 500, 2000),
            rate =>
            {
                built = new DecentSamplerSynthesizer(instrument, new DecentSamplerSynthesizerSettings(rate));
                return built;
            },
            "Sampled"));

        player.Add(new MidiTrack(
            MultiTrackTestSong.BuildSingleNoteSequence(60, 500, 2000),
            rate => new ObservingSynthesizer(rate, () => throw new InvalidOperationException("rendering failed")),
            "Breaks"));

        //Act
        var rendering = () => player.Render(RenderRate, NoTail);

        //Assert
        rendering.Should().Throw<InvalidOperationException>().WithMessage("rendering failed");
        built.StreamingMode.Should().Be(DecentSamplerStreamingMode.RealTime);
    }

    [Fact]
    public void Render_leaves_a_synthesizer_already_in_the_offline_mode_in_it()
    {
        //Arrange
        using var fixtures = Fixtures();
        using var instrument = fixtures.LoadPreset(StreamingPreset);

        DecentSamplerSynthesizer built = null;

        using var player = new MultiTrackPlayer();
        player.Add(new MidiTrack(
            MultiTrackTestSong.BuildSingleNoteSequence(69, 500, 2000),
            rate =>
            {
                built = new DecentSamplerSynthesizer(
                    instrument,
                    new DecentSamplerSynthesizerSettings(rate)
                    {
                        StreamingMode = DecentSamplerStreamingMode.Offline,
                    });

                return built;
            },
            "Sampled"));

        //Act
        player.Render(RenderRate, NoTail);

        //Assert
        built.StreamingMode.Should().Be(DecentSamplerStreamingMode.Offline);
    }

    // ---------------------------------------------------------------------------------------
    // Writing the render out through the registered writers
    // ---------------------------------------------------------------------------------------

    [Fact]
    public void RenderToFile_writes_the_format_the_extension_names()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = ConstantPlayer(song, 0.5f);
        var path = Path.Combine(song.Directory, "mix.wav");

        //Act
        player.RenderToFile(path, RenderRate, NoTail);

        //Assert
        using var reader = new WaveFileReader(path);
        reader.WaveFormat.SampleRate.Should().Be(RenderRate);
        reader.WaveFormat.Channels.Should().Be(2);
        reader.WaveFormat.BitsPerSample.Should().Be(32);
    }

    [Fact]
    public void RenderToFile_writes_a_sixteen_bit_bounce_in_one_argument()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = ConstantPlayer(song, 0.5f);
        var path = Path.Combine(song.Directory, "mix-16.wav");

        //Act
        player.RenderToFile(path, new WaveFormat(RenderRate, 16, 2), NoTail);

        //Assert
        using var reader = new WaveFileReader(path);
        reader.WaveFormat.BitsPerSample.Should().Be(16);
        reader.WaveFormat.SampleRate.Should().Be(RenderRate);
        reader.SampleCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void RenderToFile_refuses_an_extension_nothing_is_registered_for_without_writing_a_file()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = ConstantPlayer(song, 0.5f);
        var path = Path.Combine(song.Directory, "mix.nosuchformat");

        //Act
        var writing = () => player.RenderToFile(path, RenderRate, NoTail);

        //Assert
        writing.Should().Throw<NotSupportedException>();
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void RenderToFile_refuses_a_format_that_is_not_stereo()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = ConstantPlayer(song, 0.5f);
        var path = Path.Combine(song.Directory, "mono.wav");

        //Act
        var writing = () => player.RenderToFile(path, new WaveFormat(RenderRate, 16, 1), NoTail);

        //Assert
        writing.Should().Throw<ArgumentException>().WithMessage("*2 channels*");
    }

    [Fact]
    public void RenderToStream_writes_the_same_bytes_and_leaves_the_stream_open()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = ConstantPlayer(song, 0.5f);
        var path = Path.Combine(song.Directory, "compare.wav");
        player.RenderToFile(path, new WaveFormat(RenderRate, 16, 2), NoTail);

        //Act
        using var output = new MemoryStream();
        player.RenderToStream(output, ".wav", new WaveFormat(RenderRate, 16, 2), NoTail);

        //Assert
        output.CanWrite.Should().BeTrue();
        output.ToArray().Should().Equal(File.ReadAllBytes(path));
    }

    [Fact]
    public void RenderToWav_still_writes_what_it_always_wrote()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var player = ConstantPlayer(song, 0.5f);
        var path = Path.Combine(song.Directory, "legacy.wav");

        //Act
        player.RenderToWav(path, RenderRate, NoTail);

        //Assert
        using var reader = new WaveFileReader(path);
        reader.WaveFormat.BitsPerSample.Should().Be(32);
        reader.WaveFormat.Encoding.Should().Be(WaveFormatEncoding.IeeeFloat);
    }

    // ---------------------------------------------------------------------------------------

    private static DecentSamplerEngineFixtures Fixtures()
    {
        var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteSineWav("Samples/tone.wav", frequency: 440.0, frames: 44100);
        return fixtures;
    }

    private static MultiTrackPlayer ConstantPlayer(MultiTrackTestSong song, float level)
    {
        var player = new MultiTrackPlayer();
        try
        {
            player.Add(new AudioTrack(
                song.WriteConstantWav("part.wav", level, level, 4410), "Part"));
        }
        catch
        {
            player.Dispose();
            throw;
        }

        return player;
    }

    /// <summary>
    /// A synthesizer that runs a callback on every render call - so a test can see what the rest of
    /// the mix looks like WHILE the render is happening, rather than only afterwards.
    /// </summary>
    private sealed class ObservingSynthesizer : IMidiSynthesizer
    {
        private readonly Action onRender;

        internal ObservingSynthesizer(int sampleRate, Action onRender)
        {
            SampleRate = sampleRate;
            this.onRender = onRender;
            MasterVolume = 1.0f;
        }

        public int SampleRate { get; }

        public int BlockSize => 64;

        public int ActiveVoiceCount => 0;

        public float MasterVolume { get; set; }

        public void ProcessMidiMessage(int channel, int command, int data1, int data2)
        {
        }

        public void NoteOffAll(bool immediate)
        {
        }

        public void Reset()
        {
        }

        public void Render(Span<float> left, Span<float> right)
        {
            onRender();
            left.Clear();
            right.Clear();
        }
    }
}
