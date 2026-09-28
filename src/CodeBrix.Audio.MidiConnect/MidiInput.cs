using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using CodeBrix.Audio.MidiConnect.Internal;

namespace CodeBrix.Audio.MidiConnect;

/// <summary>Limits retained input data. Overflow faults the connection rather than silently losing note-offs.</summary>
public sealed class MidiInputOptions
{
    /// <summary>Maximum complete messages awaiting event dispatch. Default: 4096.</summary>
    public int QueueCapacity { get; init; } = 4096;
    /// <summary>Maximum total message bytes awaiting event dispatch. Default: four MiB.</summary>
    public int MaxQueuedBytes { get; init; } = 4 * 1024 * 1024;
    /// <summary>Maximum bytes in one incoming SysEx, including framing. Default: one MiB.</summary>
    public int MaxSystemExclusiveBytes { get; init; } = 1024 * 1024;
    internal void Validate()
    {
        if (QueueCapacity < 1 || MaxQueuedBytes < 3 || MaxSystemExclusiveBytes < 2 || MaxSystemExclusiveBytes > MaxQueuedBytes)
            throw new ArgumentOutOfRangeException(nameof(MidiInputOptions), "Queue limits must be positive and accommodate the maximum SysEx.");
    }
}

/// <summary>An open input. Subscribe to events, then call <see cref="Start"/>. Dispose to stop and close.</summary>
/// <remarks>Events run serially on a worker, never on a native callback or UI thread. A handler already
/// executing may finish after Dispose returns. Messages have owned buffers. Handler exceptions are
/// reported through Error and do not terminate capture.</remarks>
public sealed class MidiInput : IDisposable
{
    private readonly object lifecycle = new();
    private readonly object parserGate = new();
    private readonly IMidiInputConnection connection;
    private readonly MidiInputOptions options;
    private readonly Channel<MidiMessageReceivedEventArgs> queue;
    private readonly MidiStreamParser parser;
    private int state;
    private int nativeClosed;
    private int queuedBytes;
    private Exception pendingError;

    internal MidiInput(MidiPortInfo port, IMidiInputConnection connection, MidiInputOptions options)
    {
        Port = port;
        this.connection = connection;
        this.options = options;
        queue = System.Threading.Channels.Channel.CreateBounded<MidiMessageReceivedEventArgs>(
            new BoundedChannelOptions(options.QueueCapacity) { SingleReader = true, FullMode = BoundedChannelFullMode.Wait });
        parser = new MidiStreamParser(options.MaxSystemExclusiveBytes, Enqueue, Report);
        Completion = Task.Run(DispatchAsync);
    }

    /// <summary>Port from which messages are received.</summary>
    public MidiPortInfo Port { get; }
    /// <summary>Whether the native port is open and has not faulted.</summary>
    public bool IsOpen => Volatile.Read(ref state) < 2;
    /// <summary>Completes after dispatch and native cleanup finish. Do not synchronously wait inside an event handler.</summary>
    public Task Completion { get; }
    /// <summary>Complete incoming messages, in receive order.</summary>
    public event EventHandler<MidiMessageReceivedEventArgs> MessageReceived;
    /// <summary>Asynchronous errors, including subscriber exceptions and input overflow.</summary>
    public event EventHandler<MidiErrorEventArgs> Error;

    /// <summary>Begins receiving. Repeated calls while running have no effect.</summary>
    public void Start()
    {
        lock (lifecycle)
        {
            ObjectDisposedException.ThrowIf(!IsOpen, this);
            if (state == 1) return;
            Volatile.Write(ref state, 1);
            try { connection.Start(Receive, Fail); }
            catch { Dispose(); throw; }
        }
    }

    private void Receive(ReadOnlySpan<byte> bytes, TimeSpan timestamp)
    {
        if (Volatile.Read(ref state) != 1) return;
        try { lock (parserGate) parser.Feed(bytes, timestamp); }
        catch (Exception exception) { Fail(exception); }
    }

    private void Enqueue(MidiPacket packet, TimeSpan timestamp)
    {
        if (Volatile.Read(ref state) != 1) return;
        int length = packet.Data.Length;
        if (Interlocked.Add(ref queuedBytes, length) <= options.MaxQueuedBytes &&
            queue.Writer.TryWrite(new MidiMessageReceivedEventArgs(packet, timestamp))) return;
        Interlocked.Add(ref queuedBytes, -length);
        Fail(new MidiDeviceException("MIDI input dispatch queue overflowed. Capture stopped; make handlers faster or increase MidiInputOptions limits."));
    }

    private void Report(Exception exception)
    {
        if (!IsOpen) return;
        Interlocked.CompareExchange(ref pendingError, exception, null);
        queue.Writer.TryWrite(null); // Wake an idle dispatcher; repeated errors are coalesced.
    }

    internal void Fail(Exception exception)
    {
        int previous;
        do
        {
            previous = Volatile.Read(ref state);
            if (previous >= 2) return;
        } while (Interlocked.CompareExchange(ref state, 2, previous) != previous);
        // A fatal failure must replace a pending recoverable parser diagnostic.
        Interlocked.Exchange(ref pendingError, exception);
        queue.Writer.TryWrite(null);
        queue.Writer.TryComplete();
    }

    private async Task DispatchAsync()
    {
        try
        {
            await foreach (var item in queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                if (item != null)
                {
                    Interlocked.Add(ref queuedBytes, -item.Message.Data.Length);
                    if (Volatile.Read(ref state) != 3 && MessageReceived is { } handlers)
                        foreach (EventHandler<MidiMessageReceivedEventArgs> handler in handlers.GetInvocationList())
                        {
                            if (Volatile.Read(ref state) == 3) break;
                            try { handler(this, item); }
                            catch (Exception exception) { RaiseError(exception); }
                        }
                }
                var error = Interlocked.Exchange(ref pendingError, null);
                if (error != null) RaiseError(error);
            }
        }
        finally
        {
            var error = Interlocked.Exchange(ref pendingError, null);
            if (error != null) RaiseError(error);
            CloseNative();
        }
    }

    private void RaiseError(Exception exception)
    {
        if (Error is not { } handlers) return;
        foreach (EventHandler<MidiErrorEventArgs> handler in handlers.GetInvocationList())
            try { handler(this, new MidiErrorEventArgs(exception)); } catch { /* Never recurse on an error-handler failure. */ }
    }

    private void CloseNative()
    {
        lock (lifecycle)
        {
            if (Interlocked.Exchange(ref nativeClosed, 1) == 0)
                try { connection.Dispose(); } catch (Exception exception) { RaiseError(exception); }
        }
    }

    /// <summary>Stops reception, discards queued callbacks, and closes native resources. Idempotent.</summary>
    public void Dispose()
    {
        lock (lifecycle)
        {
            Interlocked.Exchange(ref state, 3);
            queue.Writer.TryComplete();
            CloseNative();
        }
    }
}
