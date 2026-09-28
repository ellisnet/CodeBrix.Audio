using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace CodeBrix.Audio.MidiConnect;

/// <summary>Plays a MIDI timeline through a borrowed output connection. Dispose never closes the output.</summary>
/// <remarks>Uses a monotonic clock and cancellable delays. This is best-effort scheduling, not a hard realtime
/// clock. Native sends, especially large SysEx transfers, can delay subsequent messages and cancellation.</remarks>
public sealed class MidiDevicePlayer : IDisposable, IAsyncDisposable
{
    private readonly object gate = new();
    private readonly MidiOutput output;
    private CancellationTokenSource cancellation;
    private Task playback = Task.CompletedTask;
    private bool disposed;

    /// <summary>Creates a player that borrows an open output.</summary>
    public MidiDevicePlayer(MidiOutput output)
    {
        ArgumentNullException.ThrowIfNull(output);
        this.output = output;
    }
    /// <summary>Whether a playback operation is still running, including stop cleanup.</summary>
    public bool IsPlaying { get { lock (gate) return !playback.IsCompleted; } }

    /// <summary>Plays once and completes at the end of the timeline. Concurrent playback is rejected.
    /// Sends sustain-off, all-notes-off and all-sound-off on channels used by this playback when it ends,
    /// fails, or is cancelled. Use a dedicated output/channel set to avoid affecting other performances.</summary>
    public Task PlayAsync(MidiPlaybackSequence sequence, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(sequence);
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            if (!playback.IsCompleted) throw new InvalidOperationException("This player is already playing. Await StopAsync before starting another sequence.");
            cancellation?.Dispose();
            cancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var token = cancellation.Token;
            playback = Task.Run(() => RunAsync(sequence, token), CancellationToken.None);
            return playback;
        }
    }

    private async Task RunAsync(MidiPlaybackSequence sequence, CancellationToken token)
    {
        bool[] usedChannels = new bool[17];
        Exception failure = null;
        try
        {
            long start = Stopwatch.GetTimestamp();
            foreach (var item in sequence.Messages)
            {
                await WaitUntil(start, item.Time, token).ConfigureAwait(false);
                token.ThrowIfCancellationRequested();
                if (item.Message.Channel != 0) usedChannels[item.Message.Channel] = true;
                output.Send(item.Message);
            }
            await WaitUntil(start, sequence.Duration, token).ConfigureAwait(false);
        }
        catch (Exception exception) { failure = exception; throw; }
        finally
        {
            Exception cleanupFailure = null;
            for (int channel = 1; channel <= 16; channel++)
                if (usedChannels[channel])
                    try { output.Panic(channel); } catch (Exception exception) { cleanupFailure ??= exception; }
            if (failure == null && cleanupFailure != null) throw cleanupFailure;
        }
    }

    private static async Task WaitUntil(long start, TimeSpan due, CancellationToken token)
    {
        while (true)
        {
            token.ThrowIfCancellationRequested();
            TimeSpan remaining = due - Stopwatch.GetElapsedTime(start);
            if (remaining <= TimeSpan.Zero) return;
            // Recompute against the original clock every time: no cumulative delay drift.
            await Task.Delay(remaining > TimeSpan.FromSeconds(1) ? TimeSpan.FromSeconds(1) : remaining, token).ConfigureAwait(false);
        }
    }

    /// <summary>Requests cancellation and awaits completion and note cleanup. Send failures still propagate.</summary>
    public async Task StopAsync()
    {
        Task pending;
        lock (gate) { cancellation?.Cancel(); pending = playback; }
        try { await pending.ConfigureAwait(false); }
        catch (OperationCanceledException) { }
    }

    /// <summary>Requests stop. Use DisposeAsync when cleanup must finish before closing the output.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed) return;
            disposed = true;
            var source = cancellation;
            cancellation = null;
            source?.Cancel();
            if (source != null)
                _ = playback.ContinueWith(_ => source.Dispose(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
    }

    /// <summary>Requests stop, awaits cleanup, and releases cancellation resources.</summary>
    public async ValueTask DisposeAsync()
    {
        Dispose();
        try { await StopAsync().ConfigureAwait(false); }
        finally { lock (gate) { cancellation?.Dispose(); cancellation = null; } }
    }
}
