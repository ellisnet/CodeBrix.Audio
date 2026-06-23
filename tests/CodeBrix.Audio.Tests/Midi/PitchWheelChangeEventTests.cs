using System;
using System.IO;
using CodeBrix.Audio.Midi;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.Midi;
public class PitchWheelChangeEventTests
{
    [Fact]
    public void ConstructorSetsProperties()
    {
        var pitchEvent = new PitchWheelChangeEvent(123, 2, 0x2000);

        Assert.Equal(123, pitchEvent.AbsoluteTime);
        Assert.Equal(2, pitchEvent.Channel);
        Assert.Equal(MidiCommandCode.PitchWheelChange, pitchEvent.CommandCode);
        Assert.Equal(0x2000, pitchEvent.Pitch);
    }

    [Fact]
    public void BinaryReaderConstructorSetsPitchFromLsbAndMsb()
    {
        using (var ms = new MemoryStream(new byte[] { 0x7D, 0x40 }))
        using (var br = new BinaryReader(ms))
        {
            var pitchEvent = new PitchWheelChangeEvent(br);

            Assert.Equal(0x207D, pitchEvent.Pitch);
        }
    }

    [Fact]
    public void BinaryReaderConstructorRejectsInvalidFirstDataByte()
    {
        using (var ms = new MemoryStream(new byte[] { 0x80, 0x00 }))
        using (var br = new BinaryReader(ms))
        {
            Assert.Throws<FormatException>(() => new PitchWheelChangeEvent(br));
        }
    }

    [Fact]
    public void BinaryReaderConstructorRejectsInvalidSecondDataByte()
    {
        using (var ms = new MemoryStream(new byte[] { 0x00, 0x80 }))
        using (var br = new BinaryReader(ms))
        {
            Assert.Throws<FormatException>(() => new PitchWheelChangeEvent(br));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0x4000)]
    public void ConstructorRejectsPitchOutOfRange(int value)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new PitchWheelChangeEvent(0, 1, value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(0x4000)]
    public void PitchPropertyRejectsOutOfRangeValues(int value)
    {
        var pitchEvent = new PitchWheelChangeEvent(0, 1, 0x2000);

        Assert.Throws<ArgumentOutOfRangeException>(() => pitchEvent.Pitch = value);
    }

    [Fact]
    public void GetAsShortMessageReturnsCorrectValue()
    {
        var pitchEvent = new PitchWheelChangeEvent(0, 2, 0x3FFF);

        Assert.Equal(0x007F7FE1, pitchEvent.GetAsShortMessage());
    }

    [Fact]
    public void ExportWritesDeltaStatusAndPitchBytes()
    {
        var pitchEvent = new PitchWheelChangeEvent(0, 2, 0x207D);
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            long absoluteTime = 0;
            pitchEvent.Export(ref absoluteTime, writer);

            var bytes = ms.ToArray();
            Assert.Equal(4, bytes.Length);
            Assert.Equal(0x00, bytes[0]);
            Assert.Equal(0xE1, bytes[1]);
            Assert.Equal(0x7D, bytes[2]);
            Assert.Equal(0x40, bytes[3]);
        }
    }

    [Fact]
    public void ToStringIncludesAbsoluteAndCenteredPitch()
    {
        var pitchEvent = new PitchWheelChangeEvent(0, 1, 0x2001);

        var text = pitchEvent.ToString();

        Assert.Contains("Pitch 8193", text);
        Assert.Contains("(1)", text);
    }
}
