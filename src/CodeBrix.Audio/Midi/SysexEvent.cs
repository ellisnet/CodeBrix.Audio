using System;
using System.IO;
using System.Text;
using System.Collections.Generic;

namespace CodeBrix.Audio.Midi; //was previously: NAudio.Midi;

/// <summary>
/// Represents a MIDI sysex message
/// </summary>
public class SysexEvent : MidiEvent 
{
    private byte[] data;
    private bool appendTerminator = true;

    private SysexEvent(long absoluteTime, MidiCommandCode command) : base(absoluteTime, 1, command)
    {
        data = Array.Empty<byte>();
    }

    /// <summary>
    /// Creates a new sysex event
    /// </summary>
    public SysexEvent()
        : base(0, 1, MidiCommandCode.Sysex)
    {
        data = Array.Empty<byte>();
    }

    /// <summary>
    /// Creates a new sysex event with the specified payload.
    /// Payload data should not include the 0xF0 status byte or the 0xF7 terminator byte.
    /// </summary>
    /// <param name="absoluteTime">Absolute time of this event</param>
    /// <param name="data">Sysex payload bytes (excluding 0xF0/0xF7)</param>
    public SysexEvent(long absoluteTime, byte[] data)
        : base(absoluteTime, 1, MidiCommandCode.Sysex)
    {
        if (data == null)
        {
            throw new ArgumentNullException("data");
        }

        this.data = (byte[])data.Clone();
    }

    /// <summary>Creates a length-delimited Standard MIDI File F0 or F7 event.
    /// The data is exactly the file payload, including a terminating F7 only when present.
    /// F7 events may contain escaped MIDI messages or continue a preceding SysEx.</summary>
    public static SysexEvent FromFileData(long absoluteTime, MidiCommandCode command, ReadOnlySpan<byte> fileData)
    {
        if (command != MidiCommandCode.Sysex && command != MidiCommandCode.Eox)
            throw new ArgumentOutOfRangeException(nameof(command));
        var result = new SysexEvent(absoluteTime, command);
        result.appendTerminator = fileData.Length > 0 && fileData[^1] == 0xF7;
        result.data = (result.appendTerminator ? fileData[..^1] : fileData).ToArray();
        return result;
    }

    /// <summary>Returns an owned copy of the Standard MIDI File payload, including any F7 terminator.
    /// Does not include the event's F0/F7 status or its variable-length size field.</summary>
    public byte[] GetFileData()
    {
        var result = new byte[data.Length + (appendTerminator ? 1 : 0)];
        data.CopyTo(result, 0);
        if (appendTerminator) result[^1] = 0xF7;
        return result;
    }

    internal static SysexEvent ReadFileEvent(BinaryReader reader, MidiCommandCode command)
    {
        int length = ReadVarInt(reader);
        if (reader.BaseStream.CanSeek && length > reader.BaseStream.Length - reader.BaseStream.Position)
            throw new EndOfStreamException("The MIDI file's SysEx payload is truncated.");
        byte[] payload = reader.ReadBytes(length);
        if (payload.Length != length) throw new EndOfStreamException("The MIDI file's SysEx payload is truncated.");
        return FromFileData(0, command, payload);
    }

    /// <summary>
    /// Reads an F7-terminated raw device message after its F0 byte. This is not the Standard MIDI File format.
    /// </summary>
    /// <param name="br">Stream of MIDI data</param>
    /// <returns>a new sysex message</returns>
    public static SysexEvent ReadSysexEvent(BinaryReader br) 
    {
        SysexEvent se = new SysexEvent();

        var sysexData = new List<byte>();
        bool loop = true;
        while(loop) 
        {
            byte b = br.ReadByte();
            if(b == 0xF7) 
            {
                loop = false;
            }
            else 
            {
                sysexData.Add(b);
            }
        }
        
        se.data = sysexData.ToArray();

        return se;
    }

    /// <summary>
    /// Creates a deep clone of this MIDI event.
    /// </summary>
    public override MidiEvent Clone()
    {
        var clone = (SysexEvent)MemberwiseClone();
        clone.data = (byte[])data?.Clone();
        return clone;
    }

    /// <summary>
    /// Describes this sysex message
    /// </summary>
    /// <returns>A string describing the sysex message</returns>
    public override string ToString() 
    {
        var sysexData = data ?? Array.Empty<byte>();
        var sb = new StringBuilder();
        foreach (byte b in sysexData)
        {
            sb.AppendFormat("{0:X2} ", b);
        }
        return $"{this.AbsoluteTime} Sysex: {sysexData.Length} bytes\r\n{sb}";
    }
    
    /// <summary>
    /// Calls base class export first, then exports the data 
    /// specific to this event
    /// <seealso cref="MidiEvent.Export">MidiEvent.Export</seealso>
    /// </summary>
    public override void Export(ref long absoluteTime, BinaryWriter writer)
    {
        base.Export(ref absoluteTime, writer);
        var fileData = GetFileData();
        WriteVarInt(writer, fileData.Length);
        writer.Write(fileData);
    }
}
