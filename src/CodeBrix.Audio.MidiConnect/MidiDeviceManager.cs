using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.MidiConnect.Internal;

namespace CodeBrix.Audio.MidiConnect; //was previously: Commons.Music.Midi (device-access design)

/// <summary>Discovers ports and owns connections for the current operating system. No audio engine is required.</summary>
/// <remarks>Call Refresh explicitly or StartWatching for periodic discovery. Port identifiers are not a
/// persistent hardware identity; in particular WinMM and ALSA identifiers can change after reconnecting.</remarks>
public sealed class MidiDeviceManager : IDisposable
{
    private readonly object gate = new();
    private readonly IMidiBackend backend;
    private readonly List<MidiInput> inputs = new();
    private readonly List<MidiOutput> outputs = new();
    private MidiPortInfo[] ports = Array.Empty<MidiPortInfo>();
    private Timer watcher;
    private bool disposed;
    private int refreshing;

    /// <summary>Creates a manager. Native resources are opened only when enumerating or opening ports.</summary>
    public MidiDeviceManager() : this(CreateBackend()) { }
    internal MidiDeviceManager(IMidiBackend backend) => this.backend = backend;

    private static IMidiBackend CreateBackend()
    {
#if ANDROID
        return new Backends.Android.AndroidMidiBackend();
#else
        if (OperatingSystem.IsWindows()) return new Backends.Windows.WinMmBackend();
        if (OperatingSystem.IsMacOS()) return new Backends.MacOS.CoreMidiBackend();
        if (OperatingSystem.IsLinux() && !OperatingSystem.IsAndroid()) return new Backends.Linux.AlsaBackend();
        throw new PlatformNotSupportedException("MidiConnect supports Windows, macOS, Linux and the Android-specific target on Android API 33 or newer.");
#endif
    }

    /// <summary>Changes detected by Refresh or the watcher. Watcher notifications run on a worker thread.</summary>
    public event EventHandler<MidiPortsChangedEventArgs> PortsChanged;
    /// <summary>Watcher and notification-handler errors.</summary>
    public event EventHandler<MidiErrorEventArgs> Error;

    /// <summary>Returns a fresh snapshot and invalidates connections whose ports disappeared or changed.</summary>
    public IReadOnlyList<MidiPortInfo> Refresh()
    {
        MidiPortInfo[] next;
        MidiPortInfo[] added;
        MidiPortInfo[] removed;
        MidiInput[] lostInputs;
        MidiOutput[] lostOutputs;
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            next = backend.Enumerate().ToArray();
            added = next.Except(ports).ToArray();
            removed = ports.Except(next).ToArray();
            ports = next;
            lostInputs = inputs.Where(p => removed.Contains(p.Port)).ToArray();
            lostOutputs = outputs.Where(p => removed.Contains(p.Port)).ToArray();
            inputs.RemoveAll(p => !p.IsOpen || lostInputs.Contains(p));
            outputs.RemoveAll(p => !p.IsOpen || lostOutputs.Contains(p));
        }
        foreach (var input in lostInputs) input.Fail(new MidiDeviceException($"MIDI input disconnected: {input.Port.Name}."));
        foreach (var output in lostOutputs)
            try { output.Dispose(); } catch (Exception exception) { Report(exception); }
        if ((added.Length != 0 || removed.Length != 0) && PortsChanged is { } handlers)
            foreach (EventHandler<MidiPortsChangedEventArgs> handler in handlers.GetInvocationList())
                try { handler(this, new MidiPortsChangedEventArgs(added, removed)); } catch (Exception exception) { Report(exception); }
        return Array.AsReadOnly(next);
    }

    /// <summary>Refreshes and lists input ports.</summary>
    public IReadOnlyList<MidiPortInfo> GetInputs() => Array.AsReadOnly(Refresh().Where(p => p.Direction == MidiPortDirection.Input).ToArray());
    /// <summary>Refreshes and lists output ports.</summary>
    public IReadOnlyList<MidiPortInfo> GetOutputs() => Array.AsReadOnly(Refresh().Where(p => p.Direction == MidiPortDirection.Output).ToArray());

    /// <summary>Opens an input. Subscribe to MessageReceived, then call Start on the returned connection.</summary>
    public async Task<MidiInput> OpenInputAsync(string portId, MidiInputOptions options = null, CancellationToken cancellationToken = default)
    {
        options ??= new MidiInputOptions();
        options.Validate();
        var port = Find(portId, MidiPortDirection.Input);
        cancellationToken.ThrowIfCancellationRequested();
        var native = await backend.OpenInputAsync(port, cancellationToken).ConfigureAwait(false);
        var input = new MidiInput(port, native, options);
        lock (gate)
        {
            if (disposed || cancellationToken.IsCancellationRequested)
            {
                input.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
                throw new ObjectDisposedException(nameof(MidiDeviceManager));
            }
            inputs.Add(input);
        }
        return input;
    }

    /// <summary>Opens an output.</summary>
    public async Task<MidiOutput> OpenOutputAsync(string portId, CancellationToken cancellationToken = default)
    {
        var port = Find(portId, MidiPortDirection.Output);
        cancellationToken.ThrowIfCancellationRequested();
        var native = await backend.OpenOutputAsync(port, cancellationToken).ConfigureAwait(false);
        var output = new MidiOutput(port, native);
        lock (gate)
        {
            if (disposed || cancellationToken.IsCancellationRequested)
            {
                output.Dispose();
                cancellationToken.ThrowIfCancellationRequested();
                throw new ObjectDisposedException(nameof(MidiDeviceManager));
            }
            outputs.Add(output);
        }
        return output;
    }

    private MidiPortInfo Find(string id, MidiPortDirection direction)
    {
        ArgumentException.ThrowIfNullOrEmpty(id);
        return Refresh().FirstOrDefault(p => p.Id == id && p.Direction == direction)
            ?? throw new MidiDeviceException($"MIDI {direction} port '{id}' is no longer available. Refresh the device list.");
    }

    /// <summary>Starts periodic enumeration. Default interval is 500 ms. Replaces any previous watcher.</summary>
    public void StartWatching(TimeSpan? interval = null)
    {
        TimeSpan period = interval ?? TimeSpan.FromMilliseconds(500);
        if (period < TimeSpan.FromMilliseconds(100) || period > TimeSpan.FromDays(1)) throw new ArgumentOutOfRangeException(nameof(interval));
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            watcher?.Dispose();
            watcher = new Timer(_ =>
            {
                if (Interlocked.Exchange(ref refreshing, 1) != 0) return;
                try { Refresh(); }
                catch (ObjectDisposedException) { }
                catch (Exception exception) { Report(exception); }
                finally { Volatile.Write(ref refreshing, 0); }
            }, null, TimeSpan.Zero, period);
        }
    }

    /// <summary>Stops periodic enumeration. A callback already running may finish.</summary>
    public void StopWatching() { lock (gate) { watcher?.Dispose(); watcher = null; } }

    private void Report(Exception exception)
    {
        if (Error is not { } handlers) return;
        foreach (EventHandler<MidiErrorEventArgs> handler in handlers.GetInvocationList())
            try { handler(this, new MidiErrorEventArgs(exception)); } catch { }
    }

    /// <summary>Stops monitoring and closes all connections opened through this manager.</summary>
    public void Dispose()
    {
        IDisposable[] connections;
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            watcher?.Dispose();
            watcher = null;
            connections = inputs.Cast<IDisposable>().Concat(outputs).ToArray();
            inputs.Clear();
            outputs.Clear();
        }
        foreach (var connection in connections)
            try { connection.Dispose(); } catch (Exception exception) { Report(exception); }
    }
}
