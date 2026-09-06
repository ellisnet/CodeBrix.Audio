using System;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Streaming;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Streaming;

/// <summary>
/// What happens when the disk cannot keep up: the voice goes quiet, the timeline does not shift, no
/// exception escapes, and the instrument says so once.
/// </summary>
/// <remarks>
/// A slow reader is modelled by a reader that has not run at all, which is what "slow" looks like from
/// the audio thread: the ring holds fewer frames than the block needs. Nothing here waits on a real
/// thread, so the tests are exact rather than timing-dependent.
/// </remarks>
public class DecentSamplerStreamingUnderrunTests
{
    private const string StreamedPreset = """
        <DecentSampler>
          <groups>
            <group ampVelTrack="0" playbackMode="disk_streaming">
              <sample path="Samples/tone.wav" rootNote="60" loNote="0" hiNote="127" />
            </group>
          </groups>
        </DecentSampler>
        """;

    [Fact]
    public void a_starved_voice_renders_silence_rather_than_a_glitch_loop()
    {
        //Arrange - a buffer nobody has filled beyond the four frames its head could supply.
        using var fixtures = DecentSamplerEngineFixtures.Create();
        var path = fixtures.WriteConstantWav("Samples/tone.wav", value: 0.5f, frames: 20000);

        using var source = StreamingSampleSource.Open(File.OpenRead(path), path, 4);

        var buffer = new StreamingVoiceBuffer(1, 1024);
        buffer.TryClaim();
        buffer.Configure(source, 0, 20000, false, 0, 20000, 0);
        buffer.PrimeFromHead();
        buffer.Start();

        var oscillator = new DecentSamplerStreamingOscillator();
        oscillator.Start(buffer, 1, 0, false);

        var left = new float[64];
        var right = new float[64];

        //Act
        var continued = oscillator.Process(left, right, 1.0);

        //Assert - the first three frames came out of the head, the rest is silence, and the buffer says
        //so without anything having thrown.
        continued.Should().BeTrue();
        left[0].Should().Be(0.5f);
        left[10].Should().Be(0f);
        buffer.HadUnderrun.Should().BeTrue();
    }

    [Fact]
    public void a_starved_voice_keeps_its_place_so_the_audio_resumes_in_time()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        var path = fixtures.WriteSineWav("Samples/tone.wav", 220.0, frames: 20000);
        var decoded = CodeBrix.Audio.Synth.Sfz.SfzSampleData.Load(path);

        using var source = StreamingSampleSource.Open(File.OpenRead(path), path, 4);

        var buffer = new StreamingVoiceBuffer(1, 1024);
        buffer.TryClaim();
        buffer.Configure(source, 0, 20000, false, 0, 20000, 0);
        buffer.PrimeFromHead();
        buffer.Start();

        var oscillator = new DecentSamplerStreamingOscillator();
        oscillator.Start(buffer, 1, 0, false);

        var left = new float[64];
        var right = new float[64];

        //Act - one starved block, then the reader catches up and the second block is on time.
        oscillator.Process(left, right, 1.0);
        buffer.Service();
        oscillator.Process(left, right, 1.0);

        //Assert - frame 64 of the file, not frame 4.
        left[0].Should().Be(decoded.Channels[0][64]);
    }

    [Fact]
    public void the_underrun_becomes_one_problem_however_many_times_it_happens()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/tone.wav", value: 0.5f, frames: 20000);

        using var instrument = DecentSamplerInstrument.Load(
            fixtures.WritePreset(StreamedPreset),
            new DecentSamplerLoadOptions { StreamingPreloadFrames = 4 });

        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument, settings =>
        {
            settings.StreamingMode = DecentSamplerStreamingMode.Offline;
            settings.StreamingRingFrames = 1024;
        });

        synthesizer.NoteOn(0, 60, 100);
        DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);

        //Act - flag two starvations by hand, which is what a reader that never caught up would leave.
        var buffers = synthesizer.StreamingPool.Buffers();
        buffers[0].ReportUnderrun();
        synthesizer.StreamingContext.Service();
        buffers[0].ReportUnderrun();
        synthesizer.StreamingContext.Service();

        //Assert
        instrument.Problems.Count(problem =>
            problem.Contains("streaming underrun on", StringComparison.Ordinal)).Should().Be(1);
    }

    [Fact]
    public void an_exhausted_pool_silences_the_note_and_says_so_once()
    {
        //Arrange - one ring buffer, three streamed notes.
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/tone.wav", value: 0.5f, frames: 20000);

        using var instrument = DecentSamplerInstrument.Load(fixtures.WritePreset(StreamedPreset));

        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument, settings =>
        {
            settings.StreamingMode = DecentSamplerStreamingMode.Offline;
            settings.StreamingVoiceCount = 1;
        });

        //Act
        synthesizer.NoteOn(0, 60, 100);
        synthesizer.NoteOn(0, 64, 100);
        synthesizer.NoteOn(0, 67, 100);
        DecentSamplerRenderProbe.RenderBlocks(synthesizer, 8);

        //Assert
        synthesizer.StreamingPool.ExhaustedCount.Should().BeGreaterThan(0);
        instrument.Problems.Count(problem =>
            problem.Contains("streaming voice buffers ran out", StringComparison.Ordinal))
            .Should().Be(1);
    }

    [Fact]
    public void a_streaming_buffer_goes_back_to_the_pool_when_the_note_ends()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/tone.wav", value: 0.5f, frames: 4410);

        using var instrument = DecentSamplerInstrument.Load(fixtures.WritePreset(StreamedPreset));

        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument, settings =>
        {
            settings.StreamingMode = DecentSamplerStreamingMode.Offline;
            settings.StreamingVoiceCount = 4;
        });

        //Act
        synthesizer.NoteOn(0, 60, 100);
        DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);
        var whileSounding = synthesizer.StreamingPool.InUseCount;

        synthesizer.NoteOff(0, 60);
        DecentSamplerRenderProbe.RenderBlocks(synthesizer, 200);
        var afterwards = synthesizer.StreamingPool.InUseCount;

        //Assert
        whileSounding.Should().Be(1);
        afterwards.Should().Be(0);
        synthesizer.StreamingPool.ExhaustedCount.Should().Be(0);
    }

    [Fact]
    public void the_same_buffer_is_handed_out_again_so_a_stream_of_notes_allocates_nothing()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/tone.wav", value: 0.5f, frames: 4410);

        using var instrument = DecentSamplerInstrument.Load(fixtures.WritePreset(StreamedPreset));

        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument, settings =>
        {
            settings.StreamingMode = DecentSamplerStreamingMode.Offline;
            settings.StreamingVoiceCount = 2;
        });

        //Act
        for (var repeat = 0; repeat < 20; repeat++)
        {
            synthesizer.NoteOn(0, 60, 100);
            DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);
            synthesizer.NoteOff(0, 60);
            DecentSamplerRenderProbe.RenderBlocks(synthesizer, 200);
        }

        //Assert
        synthesizer.StreamingPool.Count.Should().Be(2);
        synthesizer.StreamingPool.InUseCount.Should().Be(0);
        synthesizer.StreamingPool.ExhaustedCount.Should().Be(0);
    }

    [Fact]
    public void a_binding_that_moves_a_sample_point_on_a_streamed_instrument_is_reported()
    {
        //Arrange - the guide: SAMPLE_START, SAMPLE_END, LOOP_START and LOOP_END need in-memory playback.
        const string preset = """
            <DecentSampler>
              <groups>
                <group playbackMode="disk_streaming">
                  <sample path="Samples/tone.wav" rootNote="60" loNote="0" hiNote="127" />
                </group>
              </groups>
              <ui>
                <tab name="main">
                  <labeled-knob x="0" y="0" width="90" height="100" parameterName="Start"
                                minValue="0" maxValue="10000" value="0">
                    <binding type="general" level="group" position="0" parameter="SAMPLE_START" />
                  </labeled-knob>
                </tab>
              </ui>
            </DecentSampler>
            """;

        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/tone.wav", value: 0.5f, frames: 20000);

        using var instrument = DecentSamplerInstrument.Load(fixtures.WritePreset(preset));

        //Act
        instrument.Controls[0].SetValue(5000.0);
        instrument.Controls[0].SetValue(6000.0);

        //Assert
        instrument.Problems.Count(problem =>
            problem.Contains("only valid for in-memory playback", StringComparison.Ordinal))
            .Should().Be(1);
    }

    [Fact]
    public void the_same_binding_on_an_in_memory_instrument_says_nothing()
    {
        //Arrange
        const string preset = """
            <DecentSampler>
              <groups>
                <group playbackMode="memory">
                  <sample path="Samples/tone.wav" rootNote="60" loNote="0" hiNote="127" />
                </group>
              </groups>
              <ui>
                <tab name="main">
                  <labeled-knob x="0" y="0" width="90" height="100" parameterName="Start"
                                minValue="0" maxValue="10000" value="0">
                    <binding type="general" level="group" position="0" parameter="SAMPLE_START" />
                  </labeled-knob>
                </tab>
              </ui>
            </DecentSampler>
            """;

        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/tone.wav", value: 0.5f, frames: 20000);

        using var instrument = DecentSamplerInstrument.Load(fixtures.WritePreset(preset));

        //Act
        instrument.Controls[0].SetValue(5000.0);

        //Assert
        instrument.Problems.Should().NotContain(problem =>
            problem.Contains("only valid for in-memory playback", StringComparison.Ordinal));
    }
}
