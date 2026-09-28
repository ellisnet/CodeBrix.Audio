using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.MidiConnect.Internal;
using static CodeBrix.Audio.MidiConnect.Backends.Linux.AlsaNative;

namespace CodeBrix.Audio.MidiConnect.Backends.Linux;

// Authored from the ALSA sequencer API documentation, not alsa-sharp.
internal sealed class AlsaBackend : IMidiBackend
{
    public IReadOnlyList<MidiPortInfo> Enumerate()
    {
        IntPtr handle = Open("CodeBrix MIDI discovery");
        IntPtr clientInfo = IntPtr.Zero, portInfo = IntPtr.Zero;
        try
        {
            Check(snd_seq_client_info_malloc(out clientInfo), "allocate client info");
            Check(snd_seq_port_info_malloc(out portInfo), "allocate port info");
            snd_seq_client_info_set_client(clientInfo, -1);
            var result = new List<MidiPortInfo>();
            while (snd_seq_query_next_client(handle, clientInfo) >= 0)
            {
                int client = snd_seq_client_info_get_client(clientInfo);
                if (client == 0 || client == snd_seq_client_id(handle)) continue;
                string clientName = Marshal.PtrToStringUTF8(snd_seq_client_info_get_name(clientInfo)) ?? "";
                snd_seq_port_info_set_client(portInfo, client);
                snd_seq_port_info_set_port(portInfo, -1);
                while (snd_seq_query_next_port(handle, portInfo) >= 0)
                {
                    uint caps = snd_seq_port_info_get_capability(portInfo);
                    if ((caps & (128 | 256 | 512)) != 0) continue; // private, inactive, UMP endpoint
                    uint type = snd_seq_port_info_get_type(portInfo);
                    if ((type & (2 | (1 << 20) | (1 << 18))) == 0) continue;
                    int port = snd_seq_port_info_get_port(portInfo);
                    string name = Marshal.PtrToStringUTF8(snd_seq_port_info_get_name(portInfo)) ?? clientName;
                    if (!name.Contains(clientName, StringComparison.Ordinal)) name = clientName + ": " + name;
                    if ((caps & 33) == 33) result.Add(new MidiPortInfo($"alsa:in:{client}:{port}", name, MidiPortDirection.Input));
                    if ((caps & 66) == 66) result.Add(new MidiPortInfo($"alsa:out:{client}:{port}", name, MidiPortDirection.Output));
                }
            }
            return result;
        }
        finally
        {
            if (portInfo != IntPtr.Zero) snd_seq_port_info_free(portInfo);
            if (clientInfo != IntPtr.Zero) snd_seq_client_info_free(clientInfo);
            snd_seq_close(handle);
        }
    }

    private static Address Parse(MidiPortInfo port)
    {
        string[] parts = port.Id.Split(':');
        return new Address { Client = byte.Parse(parts[2]), Port = byte.Parse(parts[3]) };
    }
    public Task<IMidiInputConnection> OpenInputAsync(MidiPortInfo port, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IMidiInputConnection>(new Input(Parse(port)));
    }
    public Task<IMidiOutputConnection> OpenOutputAsync(MidiPortInfo port, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IMidiOutputConnection>(new Output(Parse(port)));
    }

    private sealed class Input : IMidiInputConnection
    {
        private IntPtr handle;
        private IntPtr decoder;
        private readonly Address source;
        private readonly int port;
        private int queue;
        private TimeSpan queueOrigin;
        private Thread thread;
        private int stopping;

        internal Input(Address source)
        {
            this.source = source;
            handle = Open("CodeBrix MIDI input");
            try
            {
                Check(snd_seq_set_client_pool_input(handle, 2000), "size input event pool");
                Check(snd_seq_set_input_buffer_size(handle, 64 * 1024), "size input buffer");
                port = Check(snd_seq_create_simple_port(handle, "Capture", 2 | 64 | 128, 2 | (1 << 20)), "create input port");
                Check(snd_midi_event_new(4096, out decoder), "create decoder");
                snd_midi_event_no_status(decoder, 1);
            }
            catch { Dispose(); throw; }
        }

        public void Start(MidiReceive receive, Action<Exception> error)
        {
            queue = Check(snd_seq_alloc_named_queue(handle, "CodeBrix capture clock"), "allocate timestamp queue");
            queueOrigin = MidiClock.Now;
            Check(snd_seq_control_queue(handle, queue, 30, 0, IntPtr.Zero), "start timestamp queue");
            Check(snd_seq_drain_output(handle), "start clock");
            Check(snd_seq_port_subscribe_malloc(out var subscription), "allocate subscription");
            try
            {
                var sender = source;
                var destination = new Address { Client = checked((byte)snd_seq_client_id(handle)), Port = checked((byte)port) };
                snd_seq_port_subscribe_set_sender(subscription, ref sender);
                snd_seq_port_subscribe_set_dest(subscription, ref destination);
                snd_seq_port_subscribe_set_queue(subscription, queue);
                snd_seq_port_subscribe_set_time_update(subscription, 1);
                snd_seq_port_subscribe_set_time_real(subscription, 1);
                Check(snd_seq_subscribe_port(handle, subscription), "connect input");
            }
            finally { snd_seq_port_subscribe_free(subscription); }
            thread = new Thread(() => Run(receive, error)) { IsBackground = true, Name = "CodeBrix ALSA MIDI input" };
            thread.Start();
        }

        private unsafe void Run(MidiReceive receive, Action<Exception> error)
        {
            try
            {
                int count = Check(snd_seq_poll_descriptors_count(handle, 1), "poll descriptor count");
                if (count == 0) throw new MidiDeviceException("ALSA returned no input poll descriptors.");
                var descriptors = new PollFd[count];
                Check(snd_seq_poll_descriptors(handle, descriptors, (uint)count, 1), "poll descriptors");
                byte[] data = new byte[512];
                while (Volatile.Read(ref stopping) == 0)
                {
                    int ready = poll(descriptors, (nuint)count, 100);
                    if (ready < 0)
                    {
                        if (Marshal.GetLastPInvokeError() == 4) continue; // EINTR
                        throw new MidiDeviceException("ALSA input poll failed.");
                    }
                    if (ready == 0) continue;
                    foreach (var fd in descriptors)
                        if ((fd.ReturnedEvents & (8 | 16 | 32)) != 0) throw new MidiDeviceException("ALSA input disconnected.");
                    while (Volatile.Read(ref stopping) == 0)
                    {
                        int result = snd_seq_event_input(handle, out var pointer);
                        if (result == -11) break; // EAGAIN
                        Check(result, "receive event");
                        var ev = Marshal.PtrToStructure<Event>(pointer);
                        TimeSpan timestamp = (ev.Flags & 1) != 0
                            ? queueOrigin + TimeSpan.FromTicks((long)ev.Seconds * TimeSpan.TicksPerSecond + ev.Nanoseconds / 100)
                            : MidiClock.Now;
                        if (ev.Type == 130) // SysEx may arrive fragmented; the shared parser assembles it.
                        {
                            if (ev.ExternalLength > int.MaxValue || (ev.ExternalLength != 0 && ev.ExternalData == IntPtr.Zero))
                                throw new MidiDeviceException("ALSA returned invalid SysEx storage.");
                            receive(new ReadOnlySpan<byte>((void*)ev.ExternalData, (int)ev.ExternalLength), timestamp);
                        }
                        else
                        {
                            fixed (byte* buffer = data)
                            {
                                nint length = snd_midi_event_decode(decoder, buffer, data.Length, pointer);
                                if (length > 0) receive(data.AsSpan(0, checked((int)length)), timestamp);
                                else if (length < 0 && length != -2) Check(checked((int)length), "decode MIDI event");
                            }
                        }
                    }
                }
            }
            catch (Exception exception) { if (Volatile.Read(ref stopping) == 0) error(exception); }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref stopping, 1) != 0) return;
            thread?.Join(); // Nonblocking ALSA reads and a bounded poll allow prompt shutdown.
            if (decoder != IntPtr.Zero) { snd_midi_event_free(decoder); decoder = IntPtr.Zero; }
            if (handle != IntPtr.Zero) { snd_seq_close(handle); handle = IntPtr.Zero; } // Releases ports, subscriptions and queues.
        }
    }

    private sealed class Output : IMidiOutputConnection
    {
        private IntPtr handle;
        private IntPtr encoder;
        private readonly int port;
        private readonly Address destination;

        internal Output(Address destination)
        {
            this.destination = destination;
            handle = Open("CodeBrix MIDI output");
            try
            {
                port = Check(snd_seq_create_simple_port(handle, "Send", 1 | 32 | 128, 2 | (1 << 20)), "create output port");
                Check(snd_seq_connect_to(handle, port, destination.Client, destination.Port), "connect output");
                Check(snd_midi_event_new(4096, out encoder), "create encoder");
            }
            catch { Dispose(); throw; }
        }

        public unsafe void Send(ReadOnlySpan<byte> message)
        {
            snd_midi_event_reset_encode(encoder);
            fixed (byte* data = message)
            {
                int offset = 0;
                while (offset < message.Length)
                {
                    Event ev = default;
                    nint consumed = snd_midi_event_encode(encoder, data + offset, message.Length - offset, ref ev);
                    if (consumed <= 0) throw new MidiDeviceException($"ALSA could not encode the MIDI message ({consumed}).");
                    offset += checked((int)consumed);
                    if (ev.Type == 255) continue;
                    ev.Source.Port = checked((byte)port);
                    ev.Destination = destination;
                    ev.Queue = 253; // SND_SEQ_QUEUE_DIRECT
                    Check(snd_seq_event_output_direct(handle, ref ev), "send MIDI event");
                }
            }
        }

        public void Dispose()
        {
            if (encoder != IntPtr.Zero) { snd_midi_event_free(encoder); encoder = IntPtr.Zero; }
            if (handle != IntPtr.Zero) { snd_seq_close(handle); handle = IntPtr.Zero; }
        }
    }
}
