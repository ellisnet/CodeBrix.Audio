using System;
using System.Globalization;
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
/// The values are a checksum over a whole render rather than a handful of samples, because a loop that
/// drifts by one frame after ten seconds shows up in the sum and not in the first block.
/// </para>
/// </remarks>
public class SfzRenderRegressionTests
{
    private const int Rate = 44100;

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
        Checksum(render).Should().Be("139517359957290");
        Sum(render).Should().BeApproximately(1.190061321362208, 1e-9);
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
        first.Should().Equal(second);
        Checksum(first).Should().Be("75345306110086");
        Sum(first).Should().BeApproximately(3.0018009736813838, 1e-9);
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

    private static double Sum(float[] samples)
    {
        var total = 0.0;
        foreach (var sample in samples)
        {
            total += sample;
        }

        return total;
    }

    private static string Checksum(float[] samples)
    {
        var hash = 17L;

        foreach (var sample in samples)
        {
            hash = (hash * 31 + BitConverter.SingleToInt32Bits(sample)) & 0x7FFFFFFFFFFF;
        }

        return hash.ToString(CultureInfo.InvariantCulture);
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
