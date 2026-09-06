using System;
using CodeBrix.Audio.ModestSynth.Effects;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Effects;

/// <summary>
/// Tests for <see cref="GateEffect" />.
/// </summary>
/// <remarks>
/// A block of direct current makes the gate's own gain envelope directly measurable: the output IS
/// the envelope, so the duty cycle, the edge shape and the click-free claim can all be read off it
/// rather than inferred from a level.
/// </remarks>
public class GateEffectTests
{
    private const int Windows = 2000;

    [Fact]
    public void Defaults_are_the_documented_ones()
    {
        //Arrange
        GateEffect effect = new GateEffect();

        //Assert
        effect.Amount.Should().Be(0.5);
        effect.Mix.Should().Be(1.0);
        effect.WindowSeconds.Should().Be(GateEffect.DefaultWindowSeconds);
    }

    [Fact]
    public void Amount_of_one_half_gates_about_half_the_windows()
    {
        //Act
        float[] envelope = Envelope(0.5, GateEffect.DefaultSeed);

        //Assert
        int open = 0;
        for (int i = 0; i < envelope.Length; i++)
        {
            if (envelope[i] > 0.5f) { open++; }
        }

        ((double)open / envelope.Length).Should().BeApproximately(0.5, 0.05);
    }

    [Fact]
    public void Amount_of_zero_never_gates()
    {
        //Arrange
        GateEffect effect = new GateEffect { Amount = 0.0 };
        float[] source = EffectSignals.Noise(4096, 53u, 0.5);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        left.Should().Equal(source);
    }

    [Fact]
    public void Amount_of_one_gates_everything()
    {
        //Act
        float[] envelope = Envelope(1.0, GateEffect.DefaultSeed);

        //Assert
        for (int i = 500; i < envelope.Length; i++)
        {
            Math.Abs(envelope[i]).Should().BeLessThan(1.0e-6f);
        }
    }

    [Fact]
    public void The_gain_never_steps_hard_enough_to_click()
    {
        //Arrange
        float[] envelope = Envelope(0.5, GateEffect.DefaultSeed);

        //Act
        double biggest = 0.0;
        for (int i = 1; i < envelope.Length; i++)
        {
            biggest = Math.Max(biggest, Math.Abs(envelope[i] - envelope[i - 1]));
        }

        //Assert
        // A raised-cosine edge over two milliseconds cannot move faster than pi/2 divided by its
        // length, which at 48 kHz is one part in sixty of the full swing.
        double edgeSamples = GateEffect.EdgeSeconds * EffectSignals.SampleRate;
        biggest.Should().BeLessThan((Math.PI / 2.0 / edgeSamples) * 1.05);
    }

    [Fact]
    public void The_same_seed_renders_the_same_dropouts()
    {
        //Act
        float[] first = Envelope(0.5, 4242u);
        float[] second = Envelope(0.5, 4242u);

        //Assert
        first.Should().Equal(second);
    }

    [Fact]
    public void A_different_seed_renders_different_dropouts()
    {
        //Act
        float[] first = Envelope(0.5, 4242u);
        float[] second = Envelope(0.5, 9999u);

        //Assert
        first.Should().NotEqual(second);
    }

    [Fact]
    public void Reset_restarts_the_sequence()
    {
        //Arrange
        GateEffect effect = new GateEffect { Amount = 0.5, Seed = 77u };
        effect.Prepare(EffectSignals.SampleRate);
        float[] first = DirectCurrent(48000);
        float[] firstRight = DirectCurrent(48000);
        EffectSignals.Render(effect, first, firstRight);

        //Act
        effect.Reset();
        float[] second = DirectCurrent(48000);
        float[] secondRight = DirectCurrent(48000);
        EffectSignals.Render(effect, second, secondRight);

        //Assert
        second.Should().Equal(first);
    }

    [Fact]
    public void Amount_of_one_half_costs_about_three_decibels_on_noise()
    {
        //Arrange
        // The reference player measured 2.4 dB; a half-open gate gives 3.01 dB exactly.
        GateEffect effect = new GateEffect { Amount = 0.5, Mix = 1.0 };
        float[] source = EffectSignals.Noise(EffectSignals.SampleRate, 12345u, 0.25);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        double change = EffectSignals.RmsDb(left, 0, left.Length)
            - EffectSignals.RmsDb(source, 0, source.Length);
        change.Should().BeInRange(-3.6, -1.8);
    }

    [Fact]
    public void Mix_of_zero_leaves_the_block_alone()
    {
        //Arrange
        GateEffect effect = new GateEffect { Amount = 1.0, Mix = 0.0 };
        float[] source = EffectSignals.Noise(4096, 59u, 0.5);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        left.Should().Equal(source);
    }

    private static float[] Envelope(double amount, uint seed)
    {
        GateEffect effect = new GateEffect { Amount = amount, Mix = 1.0, Seed = seed };
        effect.Prepare(EffectSignals.SampleRate);

        int frames = Windows * (int)Math.Round(GateEffect.DefaultWindowSeconds * EffectSignals.SampleRate);
        float[] left = DirectCurrent(frames);
        float[] right = DirectCurrent(frames);

        EffectSignals.Render(effect, left, right);

        return left;
    }

    private static float[] DirectCurrent(int frames)
    {
        float[] samples = new float[frames];
        for (int i = 0; i < frames; i++) { samples[i] = 1.0f; }
        return samples;
    }
}
