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
public class ControlChangeEventTests
{
    [Fact]
    public void ConstructorSetsProperties()
    {
        var controlChangeEvent = new ControlChangeEvent(123, 2, MidiController.Expression, 100);

        Assert.Equal(123, controlChangeEvent.AbsoluteTime);
        Assert.Equal(2, controlChangeEvent.Channel);
        Assert.Equal(MidiCommandCode.ControlChange, controlChangeEvent.CommandCode);
        Assert.Equal(MidiController.Expression, controlChangeEvent.Controller);
        Assert.Equal(100, controlChangeEvent.ControllerValue);
    }

    [Fact]
    public void BinaryReaderConstructorSetsControllerAndValue()
    {
        using (var ms = new MemoryStream(new byte[] { (byte)MidiController.MainVolume, 127 }))
        using (var reader = new BinaryReader(ms))
        {
            var controlChangeEvent = new ControlChangeEvent(reader);

            Assert.Equal(MidiController.MainVolume, controlChangeEvent.Controller);
            Assert.Equal(127, controlChangeEvent.ControllerValue);
        }
    }

    [Fact]
    public void BinaryReaderConstructorRejectsControllerWithMsbSet()
    {
        using (var ms = new MemoryStream(new byte[] { 0x80, 0x00 }))
        using (var reader = new BinaryReader(ms))
        {
            Assert.Throws<InvalidDataException>(() => new ControlChangeEvent(reader));
        }
    }

    [Fact]
    public void BinaryReaderConstructorRejectsControllerValueWithMsbSet()
    {
        using (var ms = new MemoryStream(new byte[] { 0x07, 0x80 }))
        using (var reader = new BinaryReader(ms))
        {
            Assert.Throws<InvalidDataException>(() => new ControlChangeEvent(reader));
        }
    }

    [Theory]
    [InlineData(128)]
    [InlineData(255)]
    public void ControllerRejectsValuesOutOfRange(int value)
    {
        var controlChangeEvent = new ControlChangeEvent(0, 1, MidiController.Modulation, 64);

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            controlChangeEvent.Controller = unchecked((MidiController)value));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(128)]
    public void ControllerValueRejectsValuesOutOfRange(int value)
    {
        var controlChangeEvent = new ControlChangeEvent(0, 1, MidiController.Modulation, 64);

        Assert.Throws<ArgumentOutOfRangeException>(() => controlChangeEvent.ControllerValue = value);
    }

    [Fact]
    public void GetAsShortMessageReturnsCorrectValue()
    {
        var controlChangeEvent = new ControlChangeEvent(0, 2, MidiController.Expression, 127);

        Assert.Equal(0x007F0BB1, controlChangeEvent.GetAsShortMessage());
    }

    [Fact]
    public void ExportWritesDeltaStatusControllerAndValue()
    {
        var controlChangeEvent = new ControlChangeEvent(0, 2, MidiController.Expression, 127);
        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            long absoluteTime = 0;
            controlChangeEvent.Export(ref absoluteTime, writer);

            var bytes = ms.ToArray();
            Assert.Equal(4, bytes.Length);
            Assert.Equal(0x00, bytes[0]);
            Assert.Equal(0xB1, bytes[1]);
            Assert.Equal((byte)MidiController.Expression, bytes[2]);
            Assert.Equal(0x7F, bytes[3]);
        }
    }
}
