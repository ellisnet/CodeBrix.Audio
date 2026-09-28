using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.MidiConnect.Internal;
using Xunit;

namespace CodeBrix.Audio.MidiConnect.Tests;

public class MidiStreamParserTests
{
    [Fact]
    public void Every_fragment_boundary_preserves_messages_running_status_and_realtime()
    {
        //Arrange
        byte[] wire = { 0x91, 60, 0xF8, 100, 61, 0, 0xD1, 12, 13, 0xF2, 1, 2, 0xF0, 0x7D, 1, 0xFA, 2, 0xF7, 0xF6 };
        string[] expected = { "F8", "913C64", "913D00", "D10C", "D10D", "F20102", "FA", "F07D0102F7", "F6" };
        for (int split = 0; split <= wire.Length; split++)
        {
            var output = new List<MidiPacket>();
            var errors = new List<Exception>();
            var parser = new MidiStreamParser(100, (packet, _) => output.Add(packet), errors.Add);
            //Act
            parser.Feed(wire.AsSpan(0, split), TimeSpan.Zero);
            parser.Feed(wire.AsSpan(split), TimeSpan.FromSeconds(1));
            //Assert
            Assert.Equal(expected, output.Select(p => p.ToString()));
            Assert.Empty(errors);
            Assert.False(parser.HasIncompleteMessage);
            Assert.True(output[2].IsNoteOff);
        }
    }

    [Fact]
    public void Timestamp_comes_from_first_byte_even_when_dispatch_is_later()
    {
        //Arrange
        var timestamps = new List<TimeSpan>();
        var parser = new MidiStreamParser(100, (_, time) => timestamps.Add(time), _ => Assert.Fail("Unexpected parser error"));
        //Act
        parser.Feed(new byte[] { 0x90, 60 }, TimeSpan.FromSeconds(2));
        parser.Feed(new byte[] { 100, 61, 100 }, TimeSpan.FromSeconds(3));
        //Assert
        Assert.Equal(new[] { TimeSpan.FromSeconds(2), TimeSpan.FromSeconds(3) }, timestamps);
    }

    [Fact]
    public void Oversized_sysex_is_discarded_once_and_next_message_recovers()
    {
        //Arrange
        var messages = new List<MidiPacket>();
        var errors = new List<Exception>();
        var parser = new MidiStreamParser(4, (packet, _) => messages.Add(packet), errors.Add);
        //Act
        parser.Feed(new byte[] { 0xF0, 1, 2, 3, 4, 5, 0xF8, 0xF7, 0x90, 60, 1 }, TimeSpan.Zero);
        //Assert
        Assert.Single(errors);
        Assert.Equal(new[] { "F8", "903C01" }, messages.Select(m => m.ToString()));
    }

    [Fact]
    public void System_common_cancels_running_status_and_realtime_does_not()
    {
        //Arrange
        var messages = new List<MidiPacket>();
        var errors = new List<Exception>();
        var parser = new MidiStreamParser(32, (packet, _) => messages.Add(packet), errors.Add);
        //Act
        parser.Feed(new byte[] { 0xC0, 1, 0xF8, 2, 0xF6, 3, 0xC0, 4 }, TimeSpan.Zero);
        //Assert
        Assert.Single(errors);
        Assert.Equal(new[] { "C001", "F8", "C002", "F6", "C004" }, messages.Select(m => m.ToString()));
    }

    [Theory]
    [InlineData(new byte[] { })]
    [InlineData(new byte[] { 0x90, 60 })]
    [InlineData(new byte[] { 0x90, 60, 128 })]
    [InlineData(new byte[] { 0xF0, 1 })]
    [InlineData(new byte[] { 0xF0, 0xF8, 0xF7 })]
    [InlineData(new byte[] { 0xF7 })]
    [InlineData(new byte[] { 0xF9 })]
    public void Invalid_complete_packets_are_rejected(byte[] bytes) => Assert.Throws<ArgumentException>(() => new MidiPacket(bytes));

    [Fact]
    public void Packet_owns_storage_and_channels_match_core()
    {
        //Arrange
        byte[] bytes = { 0x9F, 60, 100 };
        var packet = new MidiPacket(bytes);
        //Act
        bytes[1] = 1;
        //Assert
        Assert.Equal(60, packet.Data1);
        Assert.Equal(16, packet.Channel);
        Assert.Equal(16, packet.ToMidiEvent(0).Channel);
        Assert.Equal("EF7F7F", MidiPacket.ChannelMessage(0xE0, 16, 127, 127).ToString());
    }
}
