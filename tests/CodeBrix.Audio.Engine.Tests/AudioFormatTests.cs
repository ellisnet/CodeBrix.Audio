using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Structs;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.Engine.Tests;

/// <summary>
/// Pure-managed tests for <see cref="AudioFormat"/> that touch no native code.
/// </summary>
public class AudioFormatTests
{
    [Fact]
    public void Cd_preset_is_signed16_stereo_44100()
    {
        //Arrange
        var cd = AudioFormat.Cd;

        //Assert
        cd.Format.Should().Be(SampleFormat.S16);
        cd.Channels.Should().Be(2);
        cd.SampleRate.Should().Be(44100);
    }

    [Fact]
    public void InverseSampleRate_is_reciprocal_of_sample_rate()
    {
        //Arrange
        var format = new AudioFormat { Format = SampleFormat.F32, Channels = 2, SampleRate = 48000 };

        //Assert
        format.InverseSampleRate.Should().BeApproximately(1f / 48000f, 1e-9f);
    }

    [Fact]
    public void Formats_with_the_same_fields_are_value_equal()
    {
        //Arrange
        var a = new AudioFormat { Format = SampleFormat.F32, Channels = 2, SampleRate = 48000 };
        var b = new AudioFormat { Format = SampleFormat.F32, Channels = 2, SampleRate = 48000 };

        //Assert
        a.Should().Be(b);
    }
}
