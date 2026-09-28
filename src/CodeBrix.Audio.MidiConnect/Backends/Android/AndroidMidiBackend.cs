using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Media.Midi;
using Android.OS;
using CodeBrix.Audio.MidiConnect.Internal;

namespace CodeBrix.Audio.MidiConnect.Backends.Android; //was previously: Commons.Music.Midi.AndroidExtensions (backend design)

// Android port directions describe the DEVICE: an Android output port is our input.
// Uses the public API 33 MIDI byte-stream transport, independently of any audio backend.
internal sealed class AndroidMidiBackend : IMidiBackend
{
    private static MidiManager Manager
    {
        get
        {
            if (Application.Context.PackageManager?.HasSystemFeature(PackageManager.FeatureMidi) != true)
                throw new MidiDeviceException("This Android device does not advertise android.software.midi. MidiConnect requires the operating system's MIDI feature; installing a .NET package cannot add it.");
            return Application.Context.GetSystemService(Context.MidiService) as MidiManager
                ?? throw new MidiDeviceException("The Android MIDI service is not available on this device.");
        }
    }

    public IReadOnlyList<MidiPortInfo> Enumerate()
    {
        var result = new List<MidiPortInfo>();
        foreach (var device in Manager.GetDevicesForTransport((int)MidiTransport.MidiByteStream))
            foreach (var port in device.GetPorts())
            {
                var direction = port.Type == MidiPortType.Output ? MidiPortDirection.Input : MidiPortDirection.Output;
                string name = device.Properties.GetString(MidiDeviceInfo.PropertyName) ?? "MIDI device";
                if (!string.IsNullOrWhiteSpace(port.Name)) name += ": " + port.Name;
                result.Add(new MidiPortInfo($"android:{direction}:{device.Id}:{port.PortNumber}", name, direction,
                    device.Properties.GetString(MidiDeviceInfo.PropertyManufacturer) ?? ""));
            }
        return result;
    }

    private static async Task<(MidiDevice Device, int Port)> OpenAsync(MidiPortInfo port, CancellationToken cancellationToken)
    {
        string[] parts = port.Id.Split(':');
        int id = int.Parse(parts[2]);
        var manager = Manager;
        var info = manager.GetDevicesForTransport((int)MidiTransport.MidiByteStream).FirstOrDefault(d => d.Id == id)
            ?? throw new MidiDeviceException("The Android MIDI device disconnected while opening it.");
        cancellationToken.ThrowIfCancellationRequested();
        var listener = new OpenListener();
        using var registration = cancellationToken.Register(() => listener.Completion.TrySetCanceled(cancellationToken));
        using var handler = new Handler(Looper.MainLooper);
        try { manager.OpenDevice(info, listener, handler); }
        catch { listener.Release(); throw; }
        var device = await listener.Completion.Task.ConfigureAwait(false);
        return (device, int.Parse(parts[3]));
    }

    private sealed class OpenListener : Java.Lang.Object, MidiManager.IOnDeviceOpenedListener
    {
        internal readonly TaskCompletionSource<MidiDevice> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private GCHandle root;
        internal OpenListener() => root = GCHandle.Alloc(this);
        public void OnDeviceOpened(MidiDevice device)
        {
            try
            {
                if (device == null) Completion.TrySetException(new MidiDeviceException("Android could not open the MIDI device; it may be disconnected or in use."));
                else if (!Completion.TrySetResult(device)) { device.Close(); device.Dispose(); }
            }
            finally { Release(); }
        }
        internal void Release() { if (root.IsAllocated) root.Free(); }
    }

    public async Task<IMidiInputConnection> OpenInputAsync(MidiPortInfo port, CancellationToken cancellationToken)
    {
        var opened = await OpenAsync(port, cancellationToken).ConfigureAwait(false);
        try
        {
            var nativePort = opened.Device.OpenOutputPort(opened.Port)
                ?? throw new MidiDeviceException("Android could not open the device's MIDI output port.");
            return new Input(opened.Device, nativePort);
        }
        catch { opened.Device.Close(); opened.Device.Dispose(); throw; }
    }
    public async Task<IMidiOutputConnection> OpenOutputAsync(MidiPortInfo port, CancellationToken cancellationToken)
    {
        var opened = await OpenAsync(port, cancellationToken).ConfigureAwait(false);
        try
        {
            var nativePort = opened.Device.OpenInputPort(opened.Port)
                ?? throw new MidiDeviceException("Android could not open the device's MIDI input port; it may already be in use.");
            return new Output(opened.Device, nativePort);
        }
        catch { opened.Device.Close(); opened.Device.Dispose(); throw; }
    }

    private sealed class Input : IMidiInputConnection
    {
        private readonly MidiDevice device;
        private readonly MidiOutputPort port;
        private Receiver receiver;
        private bool disposed;
        internal Input(MidiDevice device, MidiOutputPort port) { this.device = device; this.port = port; }
        public void Start(MidiReceive receive, Action<Exception> error)
        {
            receiver = new Receiver(receive, error);
            port.Connect(receiver);
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            if (receiver != null) { receiver.Stop(); port.Disconnect(receiver); }
            try { port.Close(); }
            finally
            {
                try { device.Close(); }
                finally { receiver?.Dispose(); port.Dispose(); device.Dispose(); }
            }
        }
    }

    private sealed class Receiver : MidiReceiver
    {
        private readonly MidiReceive receive;
        private readonly Action<Exception> error;
        private readonly long nativeOrigin = Java.Lang.JavaSystem.NanoTime();
        private readonly TimeSpan origin = MidiClock.Now;
        private int stopped;
        internal Receiver(MidiReceive receive, Action<Exception> error) { this.receive = receive; this.error = error; }
        internal void Stop() => Volatile.Write(ref stopped, 1);
        public override void OnSend(byte[] message, int offset, int count, long timestamp)
        {
            if (Volatile.Read(ref stopped) != 0) return;
            try
            {
                TimeSpan time = timestamp == 0 ? MidiClock.Now : origin + TimeSpan.FromTicks((timestamp - nativeOrigin) / 100);
                receive(message.AsSpan(offset, count), time < TimeSpan.Zero ? TimeSpan.Zero : time);
            }
            catch (Exception exception) { error(exception); }
        }
    }

    private sealed class Output : IMidiOutputConnection
    {
        private readonly MidiDevice device;
        private readonly MidiInputPort port;
        private bool disposed;
        internal Output(MidiDevice device, MidiInputPort port) { this.device = device; this.port = port; }
        public void Send(ReadOnlySpan<byte> message)
        {
            byte[] bytes = message.ToArray();
            // MidiReceiver.Send splits the buffer to its own MaxMessageSize.
            port.Send(bytes, 0, bytes.Length, 0);
        }
        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            try { port.Close(); }
            finally { try { device.Close(); } finally { port.Dispose(); device.Dispose(); } }
        }
    }
}
