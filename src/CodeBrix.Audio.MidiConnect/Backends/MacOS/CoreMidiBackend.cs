using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.MidiConnect.Internal;
using static CodeBrix.Audio.MidiConnect.Backends.MacOS.CoreMidiNative;

namespace CodeBrix.Audio.MidiConnect.Backends.MacOS; //was previously: Commons.Music.Midi.CoreMidiApi (backend design)

internal sealed class CoreMidiBackend : IMidiBackend
{
    public IReadOnlyList<MidiPortInfo> Enumerate()
    {
        var result = new List<MidiPortInfo>();
        foreach (var direction in new[] { MidiPortDirection.Input, MidiPortDirection.Output })
        {
            nuint count = direction == MidiPortDirection.Input ? MIDIGetNumberOfSources() : MIDIGetNumberOfDestinations();
            for (nuint i = 0; i < count; i++)
            {
                uint endpoint = direction == MidiPortDirection.Input ? MIDIGetSource(i) : MIDIGetDestination(i);
                if (endpoint == 0) continue;
                string name = GetString(endpoint, "displayName");
                if (name.Length == 0) name = GetString(endpoint, "name");
                result.Add(new MidiPortInfo($"coremidi:{direction}:{UniqueId(endpoint)}", name, direction, GetString(endpoint, "manufacturer")));
            }
        }
        return result;
    }

    private static uint Resolve(MidiPortInfo port)
    {
        int unique = int.Parse(port.Id.Split(':')[2]);
        nuint count = port.Direction == MidiPortDirection.Input ? MIDIGetNumberOfSources() : MIDIGetNumberOfDestinations();
        for (nuint i = 0; i < count; i++)
        {
            uint endpoint = port.Direction == MidiPortDirection.Input ? MIDIGetSource(i) : MIDIGetDestination(i);
            if (endpoint != 0 && UniqueId(endpoint) == unique) return endpoint;
        }
        throw new MidiDeviceException("The CoreMIDI endpoint disconnected while opening it.");
    }
    public Task<IMidiInputConnection> OpenInputAsync(MidiPortInfo port, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IMidiInputConnection>(new Input(Resolve(port)));
    }
    public Task<IMidiOutputConnection> OpenOutputAsync(MidiPortInfo port, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IMidiOutputConnection>(new Output(Resolve(port)));
    }

    private sealed class Input : IMidiInputConnection
    {
        private readonly uint endpoint;
        private readonly ReadCallback callback;
        private uint client, port;
        private MidiReceive receive;
        private Action<Exception> error;
        private int stopping;
        private ulong hostOrigin;
        private TimeSpan origin;
        private Timebase timebase;
        private GCHandle callbackRoot;

        internal Input(uint endpoint)
        {
            this.endpoint = endpoint;
            callback = OnRead;
            IntPtr name = Text("CodeBrix MIDI input");
            callbackRoot = GCHandle.Alloc(this);
            try
            {
                Check(MIDIClientCreate(name, IntPtr.Zero, IntPtr.Zero, out client), "create input client");
                Check(MIDIInputPortCreate(client, name, callback, IntPtr.Zero, out port), "create input port");
                Check(mach_timebase_info(out timebase), "read host clock scale");
            }
            catch { Dispose(); throw; }
            finally { CFRelease(name); }
        }
        public void Start(MidiReceive receive, Action<Exception> error)
        {
            this.receive = receive;
            this.error = error;
            hostOrigin = mach_absolute_time();
            origin = MidiClock.Now;
            Check(MIDIPortConnectSource(port, endpoint, IntPtr.Zero), "connect input");
        }
        private unsafe void OnRead(IntPtr packets, IntPtr context, IntPtr sourceContext)
        {
            if (Volatile.Read(ref stopping) != 0 || receive == null) return;
            try
            {
                int count = Marshal.ReadInt32(packets);
                IntPtr packet = packets + PacketLayout.FirstOffset;
                for (int i = 0; i < count && Volatile.Read(ref stopping) == 0; i++)
                {
                    ulong hostTime = unchecked((ulong)Marshal.ReadInt64(packet));
                    int length = (ushort)Marshal.ReadInt16(packet, 8);
                    TimeSpan timestamp = hostTime == 0 ? MidiClock.Now : origin + TimeSpan.FromTicks(
                        (long)(((decimal)hostTime - hostOrigin) * timebase.Numerator / timebase.Denominator / 100));
                    receive(new ReadOnlySpan<byte>((void*)(packet + 10), length), timestamp < TimeSpan.Zero ? TimeSpan.Zero : timestamp);
                    long next = packet.ToInt64() + 10 + length;
                    packet = new IntPtr(PacketLayout.AlignFour ? (next + 3) & ~3L : next);
                }
            }
            catch (Exception exception) { error?.Invoke(exception); }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref stopping, 1) != 0) return;
            if (port != 0)
            {
                MIDIPortDisconnectSource(port, endpoint);
                MIDIPortDispose(port);
                port = 0;
            }
            if (client != 0) { Check(MIDIClientDispose(client), "close input client"); client = 0; }
            GC.KeepAlive(callback);
            if (callbackRoot.IsAllocated) callbackRoot.Free();
        }
    }

    private sealed class Output : IMidiOutputConnection
    {
        private readonly uint endpoint;
        private uint client, port;
        internal Output(uint endpoint)
        {
            this.endpoint = endpoint;
            IntPtr name = Text("CodeBrix MIDI output");
            try
            {
                Check(MIDIClientCreate(name, IntPtr.Zero, IntPtr.Zero, out client), "create output client");
                Check(MIDIOutputPortCreate(client, name, out port), "create output port");
            }
            catch { Dispose(); throw; }
            finally { CFRelease(name); }
        }
        public unsafe void Send(ReadOnlySpan<byte> message)
        {
            int capacity = Math.Min(message.Length, 65535) + 64;
            IntPtr list = Marshal.AllocHGlobal(capacity);
            try
            {
                fixed (byte* data = message)
                    for (int offset = 0; offset < message.Length;)
                    {
                        int length = Math.Min(65535, message.Length - offset);
                        IntPtr packet = MIDIPacketListAdd(list, (nuint)capacity, MIDIPacketListInit(list), 0, (nuint)length, data + offset);
                        if (packet == IntPtr.Zero) throw new MidiDeviceException("CoreMIDI rejected the output packet size.");
                        Check(MIDISend(port, endpoint, list), "send message");
                        offset += length;
                    }
            }
            finally { Marshal.FreeHGlobal(list); }
        }
        public void Dispose()
        {
            if (port != 0) { MIDIPortDispose(port); port = 0; }
            if (client != 0) { MIDIClientDispose(client); client = 0; }
        }
    }
}
