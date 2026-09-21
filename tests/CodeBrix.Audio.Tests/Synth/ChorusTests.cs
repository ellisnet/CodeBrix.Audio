using System;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

/// <summary>
/// The chorus, which became public API alongside the reverb so that any synthesizer in the family
/// can send to one shared implementation.
/// </summary>
/// <remarks>
/// Light tests of the surface and of the behaviour a caller depends on - that it is a SEND effect
/// writing the wet signal alone, that it delays what it is given, that the two sides differ, and
/// that muting clears it. How it sounds is not pinned; the algorithm is unchanged.
/// </remarks>
public class ChorusTests
{
    private const int SampleRate = 44100;
    private const int BlockSize = 256;

    // The figures the SoundFont engine runs it at.
    private const double Delay = 0.002;
    private const double Depth = 0.0019;
    private const double Frequency = 0.4;

    [Fact]
    public void a_silent_input_produces_a_silent_output()
    {
        //Arrange
        Chorus chorus = new Chorus(SampleRate, Delay, Depth, Frequency);
        var (inputLeft, inputRight, left, right) = Buffers();

        //Act
        chorus.Process(inputLeft, inputRight, left, right);

        //Assert
        Peak(left).Should().Be(0f);
        Peak(right).Should().Be(0f);
    }

    [Fact]
    public void what_goes_in_comes_out_a_little_later()
    {
        //Arrange
        Chorus chorus = new Chorus(SampleRate, Delay, Depth, Frequency);
        var (inputLeft, inputRight, left, right) = Buffers();

        for (int i = 0; i < BlockSize; i++)
        {
            inputLeft[i] = 1f;
            inputRight[i] = 1f;
        }

        //Act
        chorus.Process(inputLeft, inputRight, left, right);

        //Assert
        // The delay is a couple of milliseconds, so the first samples are still the empty buffer and
        // the signal has arrived well before the block is out.
        left[0].Should().Be(0f);
        left[BlockSize - 1].Should().BeApproximately(1f, 1e-3f);
    }

    [Fact]
    public void the_two_sides_are_not_the_same_signal()
    {
        //Arrange
        Chorus chorus = new Chorus(SampleRate, Delay, Depth, Frequency);
        var (inputLeft, inputRight, left, right) = Buffers();

        for (int i = 0; i < BlockSize; i++)
        {
            double phase = 2.0 * Math.PI * 220.0 * i / SampleRate;
            inputLeft[i] = (float)Math.Sin(phase);
            inputRight[i] = inputLeft[i];
        }

        //Act
        double worst = 0.0;

        for (int block = 0; block < 32; block++)
        {
            chorus.Process(inputLeft, inputRight, left, right);

            for (int i = 0; i < BlockSize; i++)
            {
                double difference = Math.Abs((double)left[i] - right[i]);
                if (difference > worst) { worst = difference; }
            }
        }

        //Assert
        // The two sides read the same modulation table a quarter of a cycle apart, which is where
        // the width comes from.
        worst.Should().BeGreaterThan(0.01);
    }

    [Fact]
    public void muting_clears_what_was_in_the_delay_buffers()
    {
        //Arrange
        Chorus chorus = new Chorus(SampleRate, Delay, Depth, Frequency);
        var (inputLeft, inputRight, left, right) = Buffers();

        for (int i = 0; i < BlockSize; i++)
        {
            inputLeft[i] = 1f;
            inputRight[i] = 1f;
        }

        chorus.Process(inputLeft, inputRight, left, right);

        Array.Clear(inputLeft, 0, inputLeft.Length);
        Array.Clear(inputRight, 0, inputRight.Length);

        //Act
        chorus.Mute();
        chorus.Process(inputLeft, inputRight, left, right);

        //Assert
        Peak(left).Should().Be(0f);
        Peak(right).Should().Be(0f);
    }

    [Fact]
    public void the_modulation_moves_the_delay_rather_than_holding_it_still()
    {
        //Arrange
        Chorus chorus = new Chorus(SampleRate, Delay, Depth, Frequency);
        var (inputLeft, inputRight, left, right) = Buffers();

        for (int i = 0; i < BlockSize; i++)
        {
            double phase = 2.0 * Math.PI * 1000.0 * i / SampleRate;
            inputLeft[i] = (float)Math.Sin(phase);
            inputRight[i] = inputLeft[i];
        }

        //Act
        // A steady tone through a MOVING delay comes out at a slightly different phase each block,
        // which a fixed delay could not do.
        chorus.Process(inputLeft, inputRight, left, right);
        float[] early = (float[])left.Clone();

        for (int block = 0; block < 40; block++)
        {
            chorus.Process(inputLeft, inputRight, left, right);
        }

        //Assert
        double worst = 0.0;

        for (int i = 0; i < BlockSize; i++)
        {
            double difference = Math.Abs((double)early[i] - left[i]);
            if (difference > worst) { worst = difference; }
        }

        worst.Should().BeGreaterThan(0.005);
    }

    private static (float[] InputLeft, float[] InputRight, float[] Left, float[] Right) Buffers() =>
        (new float[BlockSize], new float[BlockSize], new float[BlockSize], new float[BlockSize]);

    private static float Peak(float[] samples)
    {
        float peak = 0f;

        for (int i = 0; i < samples.Length; i++)
        {
            float value = Math.Abs(samples[i]);
            if (value > peak) { peak = value; }
        }

        return peak;
    }
}
