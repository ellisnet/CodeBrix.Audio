using System;
using System.IO;
using System.Reflection;
using CodeBrix.Audio.Midi;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.Midi;
public class SysexEventTests
{
    [Fact]
    public void Constructor_SetsAbsoluteTimeAndPayload()
    {
        var payload = new byte[] { 0x01, 0x02 };
        var sysex = new SysexEvent(123, payload);

        Assert.Equal(123, sysex.AbsoluteTime);
        Assert.Equal(MidiCommandCode.Sysex, sysex.CommandCode);
        Assert.Equal(1, sysex.Channel);
        Assert.Equal(payload, GetData(sysex));
    }

    [Fact]
    public void Constructor_ClonesPayload()
    {
        var payload = new byte[] { 0x01, 0x02 };
        var sysex = new SysexEvent(0, payload);

        payload[0] = 0x7F;

        Assert.Equal(new byte[] { 0x01, 0x02 }, GetData(sysex));
    }

    [Fact]
    public void Constructor_RejectsNullPayload()
    {
        Assert.Throws<ArgumentNullException>(() => new SysexEvent(0, null));
    }

    [Fact]
    public void ReadSysexEvent_ReadsDataUntilF7Terminator()
    {
        using (var ms = new MemoryStream(new byte[] { 0x01, 0x02, 0x03, 0xF7 }))
        using (var br = new BinaryReader(ms))
        {
            var sysex = SysexEvent.ReadSysexEvent(br);

            var data = GetData(sysex);
            Assert.Equal(new byte[] { 0x01, 0x02, 0x03 }, data);
            Assert.Equal(4, ms.Position);
        }
    }

    [Fact]
    public void ReadNextEvent_ParsesSysexEventAndAssignsBaseFields()
    {
        using (var ms = new MemoryStream(new byte[] { 0x05, 0xF0, 0x10, 0x20, 0xF7 }))
        using (var br = new BinaryReader(ms))
        {
            var midiEvent = MidiEvent.ReadNextEvent(br, null);

            Assert.IsType<SysexEvent>(midiEvent);
            Assert.Equal(5, midiEvent.DeltaTime);
            Assert.Equal(1, midiEvent.Channel);
            Assert.Equal(MidiCommandCode.Sysex, midiEvent.CommandCode);
            Assert.Equal(new byte[] { 0x10, 0x20 }, GetData((SysexEvent)midiEvent));
        }
    }

    [Fact]
    public void Export_WritesDeltaStatusDataAndTerminator()
    {
        var sysex = ReadViaMidiEvent(new byte[] { 0x01, 0x02 });
        sysex.AbsoluteTime = 10;

        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            long absoluteTime = 0;
            sysex.Export(ref absoluteTime, writer);

            Assert.Equal(10, absoluteTime);
            Assert.Equal(new byte[] { 0x0A, 0xF0, 0x01, 0x02, 0xF7 }, ms.ToArray());
        }
    }

    [Fact]
    public void Clone_CopiesBaseProperties()
    {
        var sysex = ReadViaMidiEvent(new byte[] { 0x7D, 0x7E });
        sysex.AbsoluteTime = 42;
        sysex.Channel = 4;

        var clone = (SysexEvent)sysex.Clone();

        Assert.NotSame(sysex, clone);
        Assert.Equal(sysex.AbsoluteTime, clone.AbsoluteTime);
        Assert.Equal(sysex.Channel, clone.Channel);
        Assert.Equal(sysex.CommandCode, clone.CommandCode);
        Assert.Equal(sysex.DeltaTime, clone.DeltaTime);
    }

    [Fact]
    public void Clone_DeepCopiesDataArray()
    {
        var sysex = ReadViaMidiEvent(new byte[] { 0x01, 0x02, 0x03 });
        var clone = (SysexEvent)sysex.Clone();

        var originalData = GetData(sysex);
        var cloneData = GetData(clone);

        Assert.Equal(originalData, cloneData);
        Assert.NotSame(originalData, cloneData);
    }

    [Fact]
    public void Export_IgnoresChannelForSysexStatusByte()
    {
        var sysex = ReadViaMidiEvent(new byte[] { 0x55 });
        sysex.Channel = 6;

        using (var ms = new MemoryStream())
        using (var writer = new BinaryWriter(ms))
        {
            long absoluteTime = 0;
            sysex.Export(ref absoluteTime, writer);

            var bytes = ms.ToArray();
            Assert.Equal(0xF0, bytes[1]);
        }
    }

    [Fact]
    public void ToString_HandlesEmptySysexEvent()
    {
        var sysex = new SysexEvent();

        Assert.Null(Record.Exception(() => sysex.ToString()));
    }

    private static SysexEvent ReadViaMidiEvent(byte[] data)
    {
        var bytes = new byte[2 + data.Length + 1];
        bytes[0] = 0x00;
        bytes[1] = 0xF0;
        Array.Copy(data, 0, bytes, 2, data.Length);
        bytes[bytes.Length - 1] = 0xF7;

        using (var ms = new MemoryStream(bytes))
        using (var br = new BinaryReader(ms))
        {
            return (SysexEvent)MidiEvent.ReadNextEvent(br, null);
        }
    }

    private static byte[] GetData(SysexEvent sysex)
    {
        var field = typeof(SysexEvent).GetField("data", BindingFlags.Instance | BindingFlags.NonPublic);
        return (byte[])field.GetValue(sysex);
    }
}
