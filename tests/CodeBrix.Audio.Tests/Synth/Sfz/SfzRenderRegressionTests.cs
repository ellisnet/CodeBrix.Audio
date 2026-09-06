using System;
using CodeBrix.Audio.Synth.Sfz;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.Sfz;

/// <summary>
/// A pinned render of the SFZ engine, so that work on the Decent Sampler engine - which shares
/// <see cref="SfzOscillator"/> and <see cref="SfzSampleData"/> with it - cannot change what an SFZ
/// instrument sounds like without a test saying so.
/// </summary>
/// <remarks>
/// <para>
/// The two sampled engines share their sample decoding and their playback oscillator on purpose: one
/// piece of arithmetic, one set of loop semantics, one place to fix a bug. The cost is that a change
/// made for one engine can move the other, and the SFZ engine's output is the one nobody is allowed to
/// move. These are the numbers that were rendered before the streaming work began; a failure here means
/// the shared DSP changed, not that the expectation is stale.
/// </para>
/// <para>
/// The values are sampled ACROSS the whole render rather than taken from its first block, because a
/// loop that drifts by one frame after ten seconds shows up nowhere near the start. They were pinned
/// with a tolerance rather than a checksum so that the same numbers hold on Windows, Linux and macOS -
/// see <see cref="PinnedRender"/> for why a checksum cannot.
/// </para>
/// </remarks>
public class SfzRenderRegressionTests
{
    private const int Rate = 44100;

    /// <summary>Every 128th frame of the one-shot render, from frame zero.</summary>
    private static readonly double[] OneShotReference =
    [
        0.000000000, -0.083809063, 0.108017027, -0.055408310,
        -0.036604218, 0.102585524, -0.095612787, 0.020644683,
        0.069004960, -0.109581485, 0.072228767, 0.016489690,
        -0.093481444, 0.103993550, -0.040550284, -0.051730458,
        0.107222900, -0.086463422, 0.004215173, 0.081030704,
        -0.108651318, 0.059004176, 0.032603990, -0.101025715,
        0.097602651, -0.024769133, -0.065679044, 0.109419346,
        -0.075345702, -0.012310296, 0.091211781, -0.105247699,
        0.044436350, 0.047976062, -0.106270127, 0.088989832,
        -0.008424110, -0.078132443, 0.109124847, -0.062512733,
        -0.028555522, 0.099316426, -0.099448107, 0.028856931,
        0.062255949, -0.109095298, 0.078351147, 0.008112688,
        -0.088807158, 0.106346115, -0.048256665, -0.044150677,
        0.105160117, -0.091384575, 0.012620581, 0.075118586,
        -0.109436907, 0.065928802, 0.024464801, -0.097460173,
        0.101146415, -0.032902032, -0.058740743, 0.108609833,
    ];

    /// <summary>Every 512th frame of the looped render, from frame zero - thirty turns of the loop.</summary>
    private static readonly double[] LoopedReference =
    [
        0.000000000, -0.036604218, 0.069004960, -0.093481444,
        0.108567469, -0.107350588, 0.089533508, -0.063280031,
        0.022176147, 0.014944282, -0.057150215, 0.085102811,
        -0.105631225, 0.109329306, -0.097100519, 0.074548125,
        -0.036162317, -0.000468467, 0.044293560, -0.075232223,
        0.100843132, -0.109391324, 0.102965228, -0.084509298,
        0.049514517, -0.014015562, -0.030660382, 0.064042710,
        -0.094287135, 0.107535586, -0.107024841, 0.092988916,
        -0.061998662, 0.028253881, 0.016489690, -0.051730458,
        0.086078160, -0.103794612, 0.109208167, -0.099838316,
        0.073395900, -0.041996870, -0.002029913, 0.045717377,
        -0.076360129, 0.101444565, -0.109476946, 0.102419674,
        -0.083506413, 0.048116412, -0.012465451, -0.032156434,
        0.065303408, -0.095073678, 0.107826442, -0.106677361,
        0.092152946, -0.060704704, 0.026742281, 0.018031750,
        -0.053101838, 0.087036036, -0.104285613, 0.109064870,
    ];

    [Fact]
    public void a_plain_one_shot_renders_the_values_it_always_did()
    {
        //Arrange
        using var instruments = SfzTestInstruments.Create();
        instruments.WriteSineWav("tone.wav", 220f, 20000);

        var instrument = instruments.Load("""
            <region> sample=tone.wav pitch_keycenter=60 lokey=0 hikey=127 ampeg_release=0.001
            """);

        //Act
        var render = Render(instrument, 60, 8192);

        //Assert
        PinnedRender.ShouldStillRender(render, 128, OneShotReference);
        PinnedRender.Sum(render).Should().BeApproximately(1.190069357631728, PinnedRender.SumTolerance);
    }

    [Fact]
    public void an_embedded_smpl_loop_still_drives_the_default_loop_mode()
    {
        //Arrange - the smpl chunk is read by the code the streaming source now shares, so this is the
        //fence around that refactor: a file with a loop still loops, and one without still does not.
        using var instruments = SfzTestInstruments.Create();
        instruments.WriteWavWithSmplLoop("looped.wav", 4000, 1000, 1999);
        instruments.WriteSineWav("plain.wav", 220f, 4000);

        var looped = instruments.Load(
            "<region> sample=looped.wav pitch_keycenter=60 lokey=0 hikey=127",
            "looped.sfz");

        var plain = instruments.Load(
            "<region> sample=plain.wav pitch_keycenter=60 lokey=0 hikey=127",
            "plain.sfz");

        //Act - well past the end of both files.
        var loopedTail = Render(looped, 60, 16384);
        var plainTail = Render(plain, 60, 16384);

        //Assert
        Peak(loopedTail, 12000, 4000).Should().BeGreaterThan(0.01);
        Peak(plainTail, 12000, 4000).Should().Be(0.0);
    }

    [Fact]
    public void a_looped_region_lands_on_the_same_samples_after_many_turns()
    {
        //Arrange - a 1,000 frame loop rendered for 32,768 frames wraps thirty times.
        using var instruments = SfzTestInstruments.Create();
        instruments.WriteSineWav("tone.wav", 220f, 8000);

        var instrument = instruments.Load("""
            <region> sample=tone.wav pitch_keycenter=60 lokey=0 hikey=127
                     loop_mode=loop_continuous loop_start=1000 loop_end=1999
            """);

        //Act
        var first = Render(instrument, 60, 32768);
        var second = Render(instrument, 60, 32768);

        //Assert
        // Two renders on one machine ARE required to agree bit for bit; only the pinned values need a
        // tolerance, because only they came from a different machine.
        first.Should().Equal(second);
        PinnedRender.ShouldStillRender(first, 512, LoopedReference);
        PinnedRender.Sum(first).Should().BeApproximately(3.0018201072816737, PinnedRender.SumTolerance);
    }

    private static float[] Render(SfzInstrument instrument, int note, int frames)
    {
        var synthesizer = new SfzSynthesizer(instrument, Rate);
        synthesizer.NoteOn(0, note, 100);

        var left = new float[frames];
        var right = new float[frames];
        synthesizer.Render(left, right);

        return left;
    }

    private static double Peak(float[] samples, int offset, int length)
    {
        var peak = 0.0;

        for (var i = offset; i < offset + length && i < samples.Length; i++)
        {
            peak = Math.Max(peak, Math.Abs(samples[i]));
        }

        return peak;
    }
}
