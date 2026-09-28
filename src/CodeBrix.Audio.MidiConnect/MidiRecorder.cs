using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.MidiConnect;

/// <summary>Captures from one input without owning or starting it. Attach before calling MidiInput.Start.</summary>
/// <remarks>Capture is bounded by message count and byte count. A capacity or fatal input error stops
/// recording and is retained in MidiRecording.Failure; the partial performance remains available.</remarks>
public sealed class MidiRecorder : IDisposable
{
    private readonly object gate = new();
    private readonly MidiInput input;
    private readonly int maxMessages;
    private readonly long maxBytes;
    private readonly TimeSpan origin;
    private readonly List<MidiTimedMessage> messages = new();
    private long bytes;
    private bool recording = true;
    private TimeSpan ended;
    private Exception failure;

    /// <summary>Starts recording. Defaults to one million messages and 64 MiB of message bytes.</summary>
    public MidiRecorder(MidiInput input, int maxMessages = 1000000, long maxBytes = 64 * 1024 * 1024)
    {
        ArgumentNullException.ThrowIfNull(input);
        if (maxMessages < 1) throw new ArgumentOutOfRangeException(nameof(maxMessages));
        if (maxBytes < 3) throw new ArgumentOutOfRangeException(nameof(maxBytes));
        if (!input.IsOpen) throw new ObjectDisposedException(nameof(input));
        this.input = input;
        this.maxMessages = maxMessages;
        this.maxBytes = maxBytes;
        origin = MidiClock.Now;
        input.MessageReceived += Receive;
        input.Error += InputError;
    }

    /// <summary>Whether messages are still being recorded.</summary>
    public bool IsRecording { get { lock (gate) return recording; } }

    private void Receive(object sender, MidiMessageReceivedEventArgs args)
    {
        lock (gate)
        {
            if (!recording || args.Timestamp < origin) return;
            if (messages.Count == maxMessages || args.Message.Data.Length > maxBytes - bytes)
            {
                failure = new MidiDeviceException("MIDI recording capacity exceeded. Capture stopped; increase the recorder's limits for a longer performance.");
                End();
                return;
            }
            bytes += args.Message.Data.Length;
            messages.Add(new MidiTimedMessage(args.Timestamp - origin, args.Message));
        }
    }

    private void InputError(object sender, MidiErrorEventArgs args)
    {
        lock (gate)
        {
            if (!recording || input.IsOpen) return;
            failure = args.Exception;
            End();
        }
    }

    private void End()
    {
        recording = false;
        ended = MidiClock.Now - origin;
        input.MessageReceived -= Receive;
        input.Error -= InputError;
    }

    /// <summary>Stops and returns a snapshot. Repeated calls return equivalent snapshots.
    /// Includes messages dispatched before this call acquired the recording lock.</summary>
    public MidiRecording Stop()
    {
        lock (gate)
        {
            if (recording) End();
            return new MidiRecording(messages, ended, failure);
        }
    }

    /// <summary>Stops recording and detaches from the input. The input remains open; Stop still returns the capture.</summary>
    public void Dispose() { lock (gate) { if (recording) End(); } }
}
