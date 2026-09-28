using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Audio.MidiConnect.Internal; //was previously: Commons.Music.Midi (device-access design)

internal delegate void MidiReceive(ReadOnlySpan<byte> bytes, TimeSpan timestamp);

internal interface IMidiBackend
{
    IReadOnlyList<MidiPortInfo> Enumerate();
    Task<IMidiInputConnection> OpenInputAsync(MidiPortInfo port, CancellationToken cancellationToken);
    Task<IMidiOutputConnection> OpenOutputAsync(MidiPortInfo port, CancellationToken cancellationToken);
}

internal interface IMidiInputConnection : IDisposable
{
    void Start(MidiReceive receive, Action<Exception> error);
}

internal interface IMidiOutputConnection : IDisposable
{
    void Send(ReadOnlySpan<byte> message);
}
