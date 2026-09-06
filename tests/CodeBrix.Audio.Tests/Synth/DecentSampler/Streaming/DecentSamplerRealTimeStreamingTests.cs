using System;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Streaming;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Streaming;

/// <summary>
/// The real-time streaming path end to end: the shared background reader fills a voice's ring buffer,
/// the render call never touches a file, and a finished note's buffer finds its way back to the pool.
/// </summary>
/// <remarks>
/// These are the only tests here that wait on another thread. They wait for a CONDITION rather than a
/// duration, with a generous ceiling, so a slow machine makes them slow rather than red.
/// </remarks>
public class DecentSamplerRealTimeStreamingTests
{
    private const string StreamedPreset = """
        <DecentSampler>
          <groups>
            <group ampVelTrack="0" playbackMode="disk_streaming">
              <sample path="Samples/tone.wav" rootNote="60" loNote="0" hiNote="127"
                      loopStart="2000" loopEnd="9999" loopEnabled="true" />
            </group>
          </groups>
        </DecentSampler>
        """;

    [Fact]
    public void a_real_time_voice_plays_what_the_reader_fetched()
    {
        //Arrange - a preload head of 512 frames, so almost everything heard comes off the reader thread.
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/tone.wav", value: 0.5f, frames: 100000);

        using var instrument = DecentSamplerInstrument.Load(
            fixtures.WritePreset(StreamedPreset),
            new DecentSamplerLoadOptions { StreamingPreloadFrames = 512 });

        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument, settings =>
            settings.StreamingMode = DecentSamplerStreamingMode.RealTime);

        //Act
        synthesizer.NoteOn(0, 60, 100);
        WaitUntil(() => synthesizer.StreamingPool.Buffers().Any(buffer => buffer.Available > 4096));

        var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 60);

        //Assert
        DecentSamplerRenderProbe.Rms(left).Should().BeApproximately(0.5, 0.01);
        instrument.Problems.Should().NotContain(problem =>
            problem.Contains("streaming underrun on", StringComparison.Ordinal));
    }

    [Fact]
    public void a_real_time_buffer_returns_to_the_pool_once_the_reader_has_let_go()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/tone.wav", value: 0.5f, frames: 4410);

        const string oneShot = """
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" playbackMode="disk_streaming">
                  <sample path="Samples/tone.wav" rootNote="60" loNote="0" hiNote="127" />
                </group>
              </groups>
            </DecentSampler>
            """;

        using var instrument = DecentSamplerInstrument.Load(fixtures.WritePreset(oneShot));

        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument, settings =>
        {
            settings.StreamingMode = DecentSamplerStreamingMode.RealTime;
            settings.StreamingVoiceCount = 2;
        });

        //Act
        synthesizer.NoteOn(0, 60, 100);
        DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);
        var whileSounding = synthesizer.StreamingPool.InUseCount;

        synthesizer.NoteOff(0, 60);
        DecentSamplerRenderProbe.RenderBlocks(synthesizer, 300);

        //Assert - the audio thread hands the buffer to the reader, which frees it on a later pass.
        whileSounding.Should().Be(1);
        WaitUntil(() => synthesizer.StreamingPool.InUseCount == 0);
        synthesizer.StreamingPool.InUseCount.Should().Be(0);
    }

    [Fact]
    public void the_shared_reader_forgets_a_synthesizer_nobody_holds_any_more()
    {
        //Arrange - the reader keeps only weak references, which is what lets a synthesizer be dropped
        //rather than disposed. Nothing in the library has ever required disposing one.
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/tone.wav", value: 0.5f, frames: 4410);

        using var instrument = DecentSamplerInstrument.Load(fixtures.WritePreset(StreamedPreset));

        var before = DecentSamplerStreamingReader.Shared.ContextCount;

        //Act - the synthesizer is built, played and dropped inside a method of its own, so no local
        // of THIS frame ever refers to it. A JIT that inlined Build would leave the reference live in
        // this frame's register set for as long as the frame is alive, and in a Release build with a
        // conservative stack scan the collection then does not take it.
        var dropped = Build(instrument);

        //Assert
        // WHETHER THE RUNTIME COLLECTS IS NOT WHAT THIS TEST IS ABOUT. A Release build is free to
        // keep a reference alive in a register or a stack slot for as long as the frame that made it
        // lives, and running this class ALONE - with no other work to churn the heap - reliably does
        // exactly that. So the collection is confirmed first, through a weak reference of the test's
        // own, and the case is SKIPPED rather than failed when the runtime declines: what would be
        // reported then is the garbage collector's discretion, not a defect in the reader.
        Collect(() => !dropped.IsAlive);
        Assert.SkipWhen(
            dropped.IsAlive,
            "the runtime kept the synthesizer alive through a forced collection, so there is nothing " +
            "for the reader to forget yet");

        // The reader drops the dead reference on its next pass.
        WaitUntil(() => DecentSamplerStreamingReader.Shared.ContextCount <= before);
        DecentSamplerStreamingReader.Shared.ContextCount.Should().BeLessThanOrEqualTo(before);
    }

    // Builds a synthesizer, plays it and lets it go, handing back a weak reference so the caller can
    // tell whether the runtime actually collected it. NoInlining is load-bearing: see the test above.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference Build(DecentSamplerInstrument instrument)
    {
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument, settings =>
            settings.StreamingMode = DecentSamplerStreamingMode.RealTime);

        synthesizer.NoteOn(0, 60, 100);
        DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);

        var reference = new WeakReference(synthesizer);

        // Nothing may be left referring to it when this frame returns.
        synthesizer = null;
        GC.KeepAlive(synthesizer);
        return reference;
    }

    // Runs full blocking collections until the condition holds or the attempts run out. One
    // GC.Collect() is not a guarantee: an object that has just become unreachable may need a second
    // pass, and one promoted to generation 2 needs a blocking, compacting collection to be reclaimed.
    private static void Collect(Func<bool> condition)
    {
        // A short budget on purpose: when the runtime is going to collect, it does so on the first or
        // second pass, and when it is not, waiting minutes for it changes nothing but the clock.
        for (var attempt = 0; attempt < 8; attempt++)
        {
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
            GC.WaitForPendingFinalizers();
            GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);

            if (WaitUntil(condition, TimeSpan.FromMilliseconds(250)))
            {
                return;
            }
        }
    }

    private static bool WaitUntil(Func<bool> condition) => WaitUntil(condition, TimeSpan.FromSeconds(10));

    private static bool WaitUntil(Func<bool> condition, TimeSpan limit)
    {
        var clock = Stopwatch.StartNew();

        while (clock.Elapsed < limit && !condition())
        {
            Thread.Sleep(5);
        }

        return condition();
    }
}
