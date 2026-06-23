using System;
using CodeBrix.Audio.Dsp;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests for <see cref="EnvelopeFollower"/>.
/// </summary>
public class EnvelopeFollowerTests
{
    [Fact]
    public void Envelope_rises_toward_the_amplitude_of_a_sustained_tone()
    {
        //Arrange
        var follower = new EnvelopeFollower(attackMilliseconds: 5f, releaseMilliseconds: 50f, sampleRate: 44100);
        float last = 0f;

        //Act
        for (int n = 0; n < 44100; n++)
        {
            float input = (float)Math.Sin(2.0 * Math.PI * 440 * n / 44100);
            last = follower.ProcessSample(input);
        }

        //Assert
        last.Should().BeGreaterThan(0.5f);
        last.Should().BeLessThan(1.01f);
    }

    [Fact]
    public void Envelope_decays_after_the_signal_stops()
    {
        //Arrange
        var follower = new EnvelopeFollower(attackMilliseconds: 5f, releaseMilliseconds: 20f, sampleRate: 44100);
        for (int n = 0; n < 22050; n++)
        {
            follower.ProcessSample(1.0f);
        }
        float peak = follower.Envelope;

        //Act
        float decayed = 0f;
        for (int n = 0; n < 22050; n++)
        {
            decayed = follower.ProcessSample(0f);
        }

        //Assert
        decayed.Should().BeLessThan(peak);
    }
}
