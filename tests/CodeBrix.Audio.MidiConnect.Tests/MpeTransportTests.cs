using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MidiConnect.Internal;
using Xunit;

namespace CodeBrix.Audio.MidiConnect.Tests;

public class MpeTransportTests
{
    [Fact]
    public void Mpe_configuration_and_independent_expression_survive_capture_file_and_playback()
    {
        //Arrange: lower-zone RPN 6, member pitch range RPN 0, two simultaneous
        //notes, independent Glide/Press/Slide, and distinct Lift velocities.
        //MIDI 1.0 MPE uses ordinary channel messages, with no channel collapsing.
        byte[][] wire = {
            new byte[] { 0xB0, 101, 0 }, new byte[] { 0xB0, 100, 6 }, new byte[] { 0xB0, 6, 15 },
            new byte[] { 0xB1, 101, 0 }, new byte[] { 0xB1, 100, 0 }, new byte[] { 0xB1, 6, 48 },
            new byte[] { 0x91, 60, 100 }, new byte[] { 0x92, 64, 80 },
            new byte[] { 0xE1, 127, 127 }, new byte[] { 0xE2, 0, 0 },
            new byte[] { 0xD1, 110 }, new byte[] { 0xD2, 35 },
            new byte[] { 0xB1, 74, 120 }, new byte[] { 0xB2, 74, 10 },
            new byte[] { 0x81, 60, 55 }, new byte[] { 0x82, 64, 77 }
        };
        var captured = new List<MidiTimedMessage>();
        var parser = new MidiStreamParser(100, (message, time) => captured.Add(new MidiTimedMessage(time, message)),
            _ => Assert.Fail("Unexpected framing error"));
        //Act: every byte in its own native fragment.
        for (int i = 0; i < wire.Length; i++)
            foreach (byte value in wire[i]) parser.Feed(new[] { value }, TimeSpan.FromMilliseconds(i * 20));
        var recording = new MidiRecording(captured, TimeSpan.FromSeconds(1), null);
        using var stream = new MemoryStream();
        MidiFile.Export(stream, recording.ToMidiEventCollection(releaseAtEnd: false), leaveOpen: true);
        stream.Position = 0;
        var playback = MidiPlaybackSequence.FromMidiFile(new MidiFile(stream, MidiReadMode.Strict));
        //Assert
        Assert.Equal(wire.Select(Convert.ToHexString), playback.Messages.Select(m => m.Message.ToString()));
        // 960 PPQN at 120 BPM quantizes by at most half a tick (about 0.261 ms).
        for (int i = 0; i < captured.Count; i++)
            Assert.InRange((playback.Messages[i].Time - captured[i].Time).Ticks, -2605, 2605);
        Assert.Equal(new[] { 2, 3 }, recording.Notes.Select(n => n.Channel));
        Assert.Equal(new[] { 55, 77 }, recording.Notes.Select(n => n.ReleaseVelocity));
    }
}
