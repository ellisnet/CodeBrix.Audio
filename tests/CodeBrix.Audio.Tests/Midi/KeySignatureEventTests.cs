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
public class KeySignatureEventTests
{
    [Fact]
    public void ConstructorSetsProperties()
    {
        var keySignatureEvent = new KeySignatureEvent(-3, 1, 123);

        Assert.Equal(123, keySignatureEvent.AbsoluteTime);
        Assert.Equal(MidiCommandCode.MetaEvent, keySignatureEvent.CommandCode);
        Assert.Equal(MetaEventType.KeySignature, keySignatureEvent.MetaEventType);
        Assert.Equal(-3, keySignatureEvent.SharpsFlats);
        Assert.Equal(1, keySignatureEvent.MajorMinor);
    }

    [Theory]
    [InlineData(-8)]
    [InlineData(8)]
    public void ConstructorRejectsSharpsFlatsOutOfRange(int sharpsFlats)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new KeySignatureEvent(sharpsFlats, 0, 0));
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(2)]
    public void ConstructorRejectsMajorMinorOutOfRange(int majorMinor)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => new KeySignatureEvent(0, majorMinor, 0));
    }

    [Fact]
    public void BinaryReaderConstructorRejectsInvalidLength()
    {
        using (var ms = new MemoryStream(new byte[] { 0x00, 0x00 }))
        using (var br = new BinaryReader(ms))
        {
            Assert.Throws<FormatException>(() => new KeySignatureEvent(br, 1));
        }
    }

    [Fact]
    public void BinaryReaderConstructorRejectsSharpsFlatsOutOfRange()
    {
        using (var ms = new MemoryStream(new byte[] { 0x08, 0x00 }))
        using (var br = new BinaryReader(ms))
        {
            Assert.Throws<FormatException>(() => new KeySignatureEvent(br, 2));
        }
    }

    [Fact]
    public void BinaryReaderConstructorRejectsMajorMinorOutOfRange()
    {
        using (var ms = new MemoryStream(new byte[] { 0x00, 0x02 }))
        using (var br = new BinaryReader(ms))
        {
            Assert.Throws<FormatException>(() => new KeySignatureEvent(br, 2));
        }
    }

    [Fact]
    public void ReadNextEventParsesKeySignatureMetaEvent()
    {
        var bytes = new byte[] { 0x00, 0xFF, 0x59, 0x02, 0x00, 0x00 };
        using (var ms = new MemoryStream(bytes))
        using (var br = new BinaryReader(ms))
        {
            var midiEvent = MidiEvent.ReadNextEvent(br, null);

            Assert.IsType<KeySignatureEvent>(midiEvent);
            var keySignatureEvent = (KeySignatureEvent)midiEvent;
            Assert.Equal(0, keySignatureEvent.DeltaTime);
            Assert.Equal(1, keySignatureEvent.Channel);
            Assert.Equal(MidiCommandCode.MetaEvent, keySignatureEvent.CommandCode);
            Assert.Equal(MetaEventType.KeySignature, keySignatureEvent.MetaEventType);
            Assert.Equal(0, keySignatureEvent.SharpsFlats);
            Assert.Equal(0, keySignatureEvent.MajorMinor);
        }
    }

    [Fact]
    public void SharpsFlatsInterpretsSignedValueFromStream()
    {
        var bytes = new byte[] { 0x00, 0xFF, 0x59, 0x02, 0xF9, 0x01 };
        using (var ms = new MemoryStream(bytes))
        using (var br = new BinaryReader(ms))
        {
            var keySignatureEvent = (KeySignatureEvent)MidiEvent.ReadNextEvent(br, null);

            Assert.Equal(-7, keySignatureEvent.SharpsFlats);
            Assert.Equal(1, keySignatureEvent.MajorMinor);
        }
    }

    [Theory]
    [InlineData(-2, 0, "Bb major")]
    [InlineData(4, 1, "C# minor")]
    [InlineData(0, 0, "C major")]
    public void KeyNameReturnsExpectedMusicalKey(int sharpsFlats, int majorMinor, string expected)
    {
        var keySignatureEvent = new KeySignatureEvent(sharpsFlats, majorMinor, 0);

        Assert.Equal(expected, keySignatureEvent.KeyName);
    }

    [Fact]
    public void ExportWritesDeltaMetaTypeLengthAndData()
    {
        var keySignatureEvent = new KeySignatureEvent(-1, 1, 10);

        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            long absoluteTime = 0;
            keySignatureEvent.Export(ref absoluteTime, writer);

            Assert.Equal(10, absoluteTime);
            Assert.Equal(new byte[] { 0x0A, 0xFF, 0x59, 0x02, 0xFF, 0x01 }, ms.ToArray());
        }
    }

    [Fact]
    public void CloneCopiesAllProperties()
    {
        var keySignatureEvent = new KeySignatureEvent(4, 0, 55);

        var clone = (KeySignatureEvent)keySignatureEvent.Clone();

        Assert.NotSame(keySignatureEvent, clone);
        Assert.Equal(keySignatureEvent.AbsoluteTime, clone.AbsoluteTime);
        Assert.Equal(keySignatureEvent.CommandCode, clone.CommandCode);
        Assert.Equal(keySignatureEvent.MetaEventType, clone.MetaEventType);
        Assert.Equal(keySignatureEvent.SharpsFlats, clone.SharpsFlats);
        Assert.Equal(keySignatureEvent.MajorMinor, clone.MajorMinor);
        Assert.Equal(keySignatureEvent.KeyName, clone.KeyName);
    }

    [Fact]
    public void ToStringIncludesMusicalKeyName()
    {
        var keySignatureEvent = new KeySignatureEvent(-2, 0, 0);

        var text = keySignatureEvent.ToString();

        Assert.Contains("KeySignature", text);
        Assert.Contains("Bb major", text);
    }
}
