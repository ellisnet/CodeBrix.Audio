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
/// Tests for <see cref="VoiceActivityDetector"/>.
/// </summary>
public class VoiceActivityDetectorTests
{
    private const int SampleRate = 44100;

    private static void FeedSilence(VoiceActivityDetector detector, int samples)
    {
        for (int n = 0; n < samples; n++)
        {
            detector.Process(0f);
        }
    }

    private static bool FeedTone(VoiceActivityDetector detector, int samples)
    {
        bool active = false;
        for (int n = 0; n < samples; n++)
        {
            active = detector.Process((float)Math.Sin(2.0 * Math.PI * 300 * n / SampleRate));
        }
        return active;
    }

    [Fact]
    public void Loud_tone_after_quiet_is_flagged_as_active()
    {
        //Arrange — establish a low noise floor with silence first
        var detector = new VoiceActivityDetector(SampleRate);
        FeedSilence(detector, SampleRate / 2);

        //Act
        bool active = FeedTone(detector, SampleRate / 2);

        //Assert
        active.Should().BeTrue();
        detector.IsVoiceActive.Should().BeTrue();
    }

    [Fact]
    public void Reset_clears_the_active_state()
    {
        //Arrange — drive the detector into the active state
        var detector = new VoiceActivityDetector(SampleRate);
        FeedSilence(detector, SampleRate / 2);
        FeedTone(detector, SampleRate / 2);
        detector.IsVoiceActive.Should().BeTrue();

        //Act
        detector.Reset();

        //Assert
        detector.IsVoiceActive.Should().BeFalse();
    }
}
