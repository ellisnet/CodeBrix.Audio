using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.MidiConnect.Backends.Linux;
using CodeBrix.Audio.MidiConnect.Internal;
using Xunit;

namespace CodeBrix.Audio.MidiConnect.Tests;

public class MidiDeviceTests
{
    internal sealed class InputConnection : IMidiInputConnection
    {
        internal MidiReceive Receive;
        internal Action<Exception> Error;
        internal int Closed;
        public void Start(MidiReceive receive, Action<Exception> error) { Receive = receive; Error = error; }
        public void Dispose() => Interlocked.Increment(ref Closed);
    }
    internal sealed class OutputConnection : IMidiOutputConnection
    {
        internal readonly List<string> Messages = new();
        internal readonly TaskCompletionSource Sent = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal int Closed;
        public void Send(ReadOnlySpan<byte> message) { Messages.Add(Convert.ToHexString(message)); Sent.TrySetResult(); }
        public void Dispose() => Closed++;
    }
    private sealed class Backend : IMidiBackend
    {
        internal MidiPortInfo[] Ports = { new("test", "Device", MidiPortDirection.Input) };
        internal readonly InputConnection Input = new();
        public IReadOnlyList<MidiPortInfo> Enumerate() => Ports;
        public Task<IMidiInputConnection> OpenInputAsync(MidiPortInfo port, CancellationToken token) => Task.FromResult<IMidiInputConnection>(Input);
        public Task<IMidiOutputConnection> OpenOutputAsync(MidiPortInfo port, CancellationToken token) => throw new NotSupportedException();
    }

    [Fact]
    public async Task Subscriber_failures_are_reported_and_other_subscribers_continue()
    {
        //Arrange
        var native = new InputConnection();
        using var input = new MidiInput(new MidiPortInfo("test", "Test", MidiPortDirection.Input), native, new());
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failed = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        input.MessageReceived += (_, _) => throw new InvalidOperationException("subscriber");
        input.MessageReceived += (_, _) => received.TrySetResult();
        input.Error += (_, e) => failed.TrySetResult(e.Exception);
        //Act
        input.Start();
        native.Receive(new byte[] { 0x90, 60, 100 }, MidiClock.Now);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        //Assert
        Assert.IsType<InvalidOperationException>(await failed.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
        Assert.True(input.IsOpen);
        input.Dispose();
        await input.Completion;
        Assert.Equal(1, native.Closed);
    }

    [Fact]
    public async Task Queue_overflow_faults_and_closes_without_silent_loss()
    {
        //Arrange
        var native = new InputConnection();
        using var input = new MidiInput(new MidiPortInfo("test", "Test", MidiPortDirection.Input), native,
            new() { QueueCapacity = 1, MaxQueuedBytes = 100, MaxSystemExclusiveBytes = 20 });
        using var release = new ManualResetEventSlim();
        var entered = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var failure = new TaskCompletionSource<Exception>(TaskCreationOptions.RunContinuationsAsynchronously);
        input.MessageReceived += (_, _) => { entered.TrySetResult(); release.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken); };
        input.Error += (_, e) => failure.TrySetResult(e.Exception);
        input.Start();
        //Act
        try
        {
            native.Receive(new byte[] { 0x90, 60, 100 }, MidiClock.Now);
            await entered.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            native.Receive(new byte[] { 0xF7 }, MidiClock.Now); // Pending recoverable error must not hide the fatal overflow.
            native.Receive(new byte[] { 0x90, 61, 100, 0x80, 60, 0 }, MidiClock.Now);
            Assert.False(input.IsOpen);
        }
        finally { release.Set(); }
        await input.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        //Assert
        Assert.Contains("overflow", (await failure.Task).Message);
        Assert.Equal(1, native.Closed);
    }

    [Fact]
    public async Task Removed_device_faults_owned_input_and_notifies()
    {
        //Arrange
        var backend = new Backend();
        using var manager = new MidiDeviceManager(backend);
        using var input = await manager.OpenInputAsync("test", cancellationToken: TestContext.Current.CancellationToken);
        MidiPortsChangedEventArgs change = null;
        manager.PortsChanged += (_, e) => change = e;
        input.Start();
        //Act
        backend.Ports = Array.Empty<MidiPortInfo>();
        manager.Refresh();
        await input.Completion.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        //Assert
        Assert.Single(change.Removed);
        Assert.False(input.IsOpen);
        Assert.Equal(1, backend.Input.Closed);
    }

    [Fact]
    public async Task Cancellation_releases_only_used_channels_and_keeps_borrowed_output_open()
    {
        //Arrange
        var native = new OutputConnection();
        using var output = new MidiOutput(new MidiPortInfo("test", "Test", MidiPortDirection.Output), native);
        await using var player = new MidiDevicePlayer(output);
        var sequence = new MidiPlaybackSequence(new[] { MidiRecordingTests.Message(0, 0x92, 60, 100) }, TimeSpan.FromHours(1));
        //Act
        Task playing = player.PlayAsync(sequence, TestContext.Current.CancellationToken);
        await native.Sent.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        await player.StopAsync().WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        //Assert
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => playing);
        Assert.Equal(new[] { "923C64", "B24000", "B27B00", "B27800" }, native.Messages);
        Assert.True(output.IsOpen);
        Assert.Equal(0, native.Closed);
    }

    [Fact]
    public async Task Recorder_capacity_failure_preserves_partial_take_without_closing_input()
    {
        //Arrange
        var native = new InputConnection();
        using var input = new MidiInput(new MidiPortInfo("test", "Test", MidiPortDirection.Input), native, new());
        using var recorder = new MidiRecorder(input, maxMessages: 1);
        var received = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        int count = 0;
        input.MessageReceived += (_, _) => { if (++count == 2) received.TrySetResult(); };
        input.Start();
        //Act
        native.Receive(new byte[] { 0x90, 60, 100, 0x80, 60, 0 }, MidiClock.Now);
        await received.Task.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var take = recorder.Stop();
        //Assert
        Assert.False(recorder.IsRecording);
        Assert.Single(take.Messages);
        Assert.IsType<MidiDeviceException>(take.Failure);
        Assert.True(take.Notes[0].IsTruncated);
        Assert.True(input.IsOpen);
        input.Dispose();
        await input.Completion;
    }

    [Fact]
    public void Linux_loader_error_names_runtime_packages_and_preserves_original_error()
    {
        //Arrange
        var cause = new DllNotFoundException("loader detail");
        //Act
        var error = AlsaNative.MissingLibrary(cause);
        //Assert
        Assert.Same(cause, error.InnerException);
        Assert.Contains("libasound.so.2", error.Message);
        Assert.Contains("sudo apt install libasound2t64", error.Message);
        Assert.Contains("sudo apt install libasound2;", error.Message);
        Assert.Contains("sudo dnf install alsa-lib", error.Message);
        Assert.Contains("Development headers are not required", error.Message);
        Assert.DoesNotContain("apt install", AlsaNative.FailureGuidance(-13, "open"));
        Assert.Contains("libasound loaded", AlsaNative.FailureGuidance(-2, "open"));
    }
}
