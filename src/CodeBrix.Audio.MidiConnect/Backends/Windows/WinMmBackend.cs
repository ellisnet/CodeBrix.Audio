using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.MidiConnect.Internal;
using static CodeBrix.Audio.MidiConnect.Backends.Windows.WinMmNative;
using NativeBuffer = CodeBrix.Audio.MidiConnect.Backends.Windows.WinMmNative.Buffer;

namespace CodeBrix.Audio.MidiConnect.Backends.Windows; //was previously: NAudio.Midi

// Adapted device enumeration/WinMM buffer lifecycle from NAudio MidiIn/MidiOut.
// Buffers are owned until unprepared; native callbacks never invoke user handlers.
internal sealed class WinMmBackend : IMidiBackend
{
    public IReadOnlyList<MidiPortInfo> Enumerate()
    {
        if (IntPtr.Size != 8) throw new PlatformNotSupportedException("MidiConnect supports 64-bit Windows processes.");
        var result = new List<MidiPortInfo>();
        for (uint i = 0; i < midiInGetNumDevs(); i++)
        {
            Check(midiInGetDevCapsW(i, out var caps, (uint)Marshal.SizeOf<InputCaps>()), "enumerate input");
            result.Add(new MidiPortInfo($"winmm:in:{i}", caps.Name, MidiPortDirection.Input));
        }
        for (uint i = 0; i < midiOutGetNumDevs(); i++)
        {
            Check(midiOutGetDevCapsW(i, out var caps, (uint)Marshal.SizeOf<OutputCaps>()), "enumerate output");
            result.Add(new MidiPortInfo($"winmm:out:{i}", caps.Name, MidiPortDirection.Output));
        }
        return result;
    }
    public Task<IMidiInputConnection> OpenInputAsync(MidiPortInfo port, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IMidiInputConnection>(new Input(uint.Parse(port.Id.Split(':')[2])));
    }
    public Task<IMidiOutputConnection> OpenOutputAsync(MidiPortInfo port, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        return Task.FromResult<IMidiOutputConnection>(new Output(uint.Parse(port.Id.Split(':')[2])));
    }

    private sealed class Input : IMidiInputConnection
    {
        private IntPtr handle;
        private readonly Callback callback;
        private readonly List<NativeBuffer> buffers = new();
        private readonly ConcurrentQueue<IntPtr> returned = new();
        private readonly AutoResetEvent wake = new(false);
        private readonly object callbackGate = new();
        private MidiReceive receive;
        private Action<Exception> error;
        private Thread recycler;
        private TimeSpan origin;
        private uint lastMilliseconds;
        private long millisecondsWrap;
        private int stopping;
        private GCHandle callbackRoot;

        internal Input(uint id)
        {
            callback = OnCallback;
            callbackRoot = GCHandle.Alloc(this);
            try { Check(midiInOpen(out handle, id, callback, UIntPtr.Zero, 0x30000), "open input"); }
            catch { callbackRoot.Free(); wake.Dispose(); throw; }
        }
        public void Start(MidiReceive receive, Action<Exception> error)
        {
            this.receive = receive;
            this.error = error;
            for (int i = 0; i < 8; i++)
            {
                var buffer = new NativeBuffer(4096);
                buffers.Add(buffer);
                Check(midiInPrepareHeader(handle, buffer.Pointer, HeaderSize), "prepare input buffer");
                Check(midiInAddBuffer(handle, buffer.Pointer, HeaderSize), "queue input buffer");
            }
            recycler = new Thread(() =>
            {
                try
                {
                    while (Volatile.Read(ref stopping) == 0)
                    {
                        wake.WaitOne(100);
                        while (Volatile.Read(ref stopping) == 0 && returned.TryDequeue(out var pointer))
                            Check(midiInAddBuffer(handle, pointer, HeaderSize), "requeue input buffer");
                    }
                }
                catch (Exception exception) { if (Volatile.Read(ref stopping) == 0) error(exception); }
            }) { IsBackground = true, Name = "CodeBrix WinMM buffer recycler" };
            recycler.Start();
            origin = MidiClock.Now;
            Check(midiInStart(handle), "start input");
        }
        private unsafe void OnCallback(IntPtr _, uint message, UIntPtr instance, UIntPtr parameter1, UIntPtr parameter2)
        {
            if (Volatile.Read(ref stopping) != 0 || receive == null) return;
            try
            {
                lock (callbackGate)
                {
                    if (Volatile.Read(ref stopping) != 0) return;
                    uint milliseconds = (uint)parameter2.ToUInt64();
                    if (milliseconds < lastMilliseconds && lastMilliseconds - milliseconds > 0x80000000)
                        millisecondsWrap += 1L << 32;
                    lastMilliseconds = milliseconds;
                    TimeSpan timestamp = origin + TimeSpan.FromMilliseconds(millisecondsWrap + milliseconds);
                    if (message is 0x3C3 or 0x3CC)
                    {
                        uint packed = (uint)parameter1.ToUInt64();
                        Span<byte> bytes = stackalloc byte[3] { (byte)packed, (byte)(packed >> 8), (byte)(packed >> 16) };
                        int length = MidiPacket.MessageLength(bytes[0]);
                        receive(bytes[..(length == 0 ? 1 : length)], timestamp);
                    }
                    else if (message is 0x3C4 or 0x3C6)
                    {
                        var pointer = (IntPtr)parameter1;
                        var header = Marshal.PtrToStructure<Header>(pointer);
                        if (header.Recorded > header.Length) throw new MidiDeviceException("WinMM returned an invalid input length.");
                        if (message == 0x3C4 && header.Recorded != 0)
                            receive(new ReadOnlySpan<byte>((void*)header.Data, checked((int)header.Recorded)), timestamp);
                        if (message == 0x3C6) error(new MidiDeviceException("WinMM reported a malformed SysEx message."));
                        returned.Enqueue(pointer);
                        wake.Set();
                    }
                    else if (message == 0x3C5) error(new MidiDeviceException("WinMM reported a malformed MIDI message."));
                }
            }
            catch (Exception exception) { error?.Invoke(exception); }
        }
        public void Dispose()
        {
            if (Interlocked.Exchange(ref stopping, 1) != 0) return;
            wake.Set();
            recycler?.Join();
            if (handle != IntPtr.Zero)
            {
                midiInStop(handle);
                Check(midiInReset(handle), "reset input");
                lock (callbackGate)
                {
                    foreach (var buffer in buffers)
                    {
                        if ((buffer.Value.Flags & 2) != 0) Check(midiInUnprepareHeader(handle, buffer.Pointer, HeaderSize), "unprepare input");
                        buffer.Dispose();
                    }
                }
                // Do not hold the callback lock across close: a driver may wait for a callback.
                Check(midiInClose(handle), "close input");
                handle = IntPtr.Zero;
            }
            GC.KeepAlive(callback);
            if (callbackRoot.IsAllocated) callbackRoot.Free();
            wake.Dispose();
        }
    }

    private sealed class Output : IMidiOutputConnection
    {
        private IntPtr handle;
        private readonly AutoResetEvent completed = new(false);
        private readonly List<NativeBuffer> retained = new();
        private GCHandle resourceRoot;
        internal Output(uint id)
        {
            resourceRoot = GCHandle.Alloc(this);
            try { Check(midiOutOpen(out handle, id, completed.SafeWaitHandle.DangerousGetHandle(), UIntPtr.Zero, 0x50000), "open output"); }
            catch { resourceRoot.Free(); completed.Dispose(); throw; }
        }
        public unsafe void Send(ReadOnlySpan<byte> message)
        {
            if (message[0] != 0xF0)
            {
                uint packed = message[0];
                if (message.Length > 1) packed |= (uint)message[1] << 8;
                if (message.Length > 2) packed |= (uint)message[2] << 16;
                Check(midiOutShortMsg(handle, packed), "send short message");
                return;
            }
            var buffer = new NativeBuffer(message.Length);
            retained.Add(buffer);
            message.CopyTo(new Span<byte>((void*)buffer.Value.Data, message.Length));
            try
            {
                Check(midiOutPrepareHeader(handle, buffer.Pointer, HeaderSize), "prepare SysEx");
                Check(midiOutLongMsg(handle, buffer.Pointer, HeaderSize), "send SysEx");
                var deadline = Stopwatch.StartNew();
                double timeoutSeconds = 30 + message.Length / 2000.0;
                while ((buffer.Value.Flags & 1) == 0)
                {
                    if (deadline.Elapsed.TotalSeconds > timeoutSeconds)
                    {
                        Check(midiOutReset(handle), "reset timed-out SysEx");
                        throw new MidiDeviceException("Windows MIDI SysEx transmission timed out.");
                    }
                    completed.WaitOne(20);
                }
            }
            finally
            {
                // On an exceptional driver failure retain memory until Dispose successfully resets
                // and unprepares it. Never free memory that may still be owned by the driver.
                if ((buffer.Value.Flags & 2) == 0 || midiOutUnprepareHeader(handle, buffer.Pointer, HeaderSize) == 0)
                {
                    retained.Remove(buffer);
                    buffer.Dispose();
                }
            }
        }
        public void Dispose()
        {
            if (handle == IntPtr.Zero) return;
            Check(midiOutReset(handle), "reset output");
            foreach (var buffer in retained)
            {
                Check(midiOutUnprepareHeader(handle, buffer.Pointer, HeaderSize), "unprepare output");
                buffer.Dispose();
            }
            retained.Clear();
            Check(midiOutClose(handle), "close output");
            handle = IntPtr.Zero;
            completed.Dispose();
            if (resourceRoot.IsAllocated) resourceRoot.Free();
        }
    }
}
