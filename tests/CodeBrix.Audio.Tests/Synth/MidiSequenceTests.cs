using System;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

public class MidiSequenceTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(1.1)]
    [InlineData(1.11)]
    [InlineData(1.111)]
    [InlineData(1.1111)]
    [InlineData(1.11111)]
    [InlineData(3.1415)]
    public void timecents_to_seconds_test(double value)
    {
        var actual = MidiSequence.GetTimeSpanFromSeconds(value);
        var expected = TimeSpan.FromSeconds(value);
        actual.Ticks.Should().Be(expected.Ticks);
    }
}
