using System;

namespace CodeBrix.Audio.MidiConnect;

/// <summary>Direction from the application's point of view.</summary>
public enum MidiPortDirection
{
    /// <summary>Receives messages from a controller or another application.</summary>
    Input,
    /// <summary>Sends messages to an instrument or another application.</summary>
    Output
}

/// <summary>An enumerated MIDI 1.0 port. Identifiers are backend-specific and may change after reconnecting.</summary>
public sealed record MidiPortInfo
{
    internal MidiPortInfo(string id, string name, MidiPortDirection direction, string manufacturer = "")
    {
        Id = id;
        Name = name;
        Direction = direction;
        Manufacturer = manufacturer;
    }

    /// <summary>Opaque identifier to pass back to the same device manager.</summary>
    public string Id { get; }
    /// <summary>Human-readable port name; names need not be unique.</summary>
    public string Name { get; }
    /// <summary>Direction relative to the application.</summary>
    public MidiPortDirection Direction { get; }
    /// <summary>Manufacturer when supplied by the backend; otherwise empty.</summary>
    public string Manufacturer { get; }
}

/// <summary>A failure to communicate with the operating system or a MIDI device.</summary>
public sealed class MidiDeviceException : Exception
{
    /// <summary>Creates a MIDI device exception.</summary>
    public MidiDeviceException(string message) : base(message) { }
    /// <summary>Creates a MIDI device exception with its underlying cause.</summary>
    public MidiDeviceException(string message, Exception innerException) : base(message, innerException) { }
}

/// <summary>Reports an asynchronous device, parser, queue, or subscriber failure.</summary>
public sealed class MidiErrorEventArgs : EventArgs
{
    internal MidiErrorEventArgs(Exception exception) => Exception = exception;
    /// <summary>The failure. Native callback exceptions never escape into the operating system.</summary>
    public Exception Exception { get; }
}

/// <summary>A snapshot change detected by refresh or periodic monitoring.</summary>
public sealed class MidiPortsChangedEventArgs : EventArgs
{
    internal MidiPortsChangedEventArgs(MidiPortInfo[] added, MidiPortInfo[] removed)
    {
        Added = Array.AsReadOnly(added);
        Removed = Array.AsReadOnly(removed);
    }
    /// <summary>New or changed ports.</summary>
    public System.Collections.Generic.IReadOnlyList<MidiPortInfo> Added { get; }
    /// <summary>Removed or replaced ports. Existing connections to these ports are invalidated.</summary>
    public System.Collections.Generic.IReadOnlyList<MidiPortInfo> Removed { get; }
}
