using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using static CodeBrix.Audio.MidiConnect.Backends.Linux.AlsaNative;

namespace CodeBrix.Audio.MidiConnect.Tests;

public class AlsaIntegrationTests
{
    [Fact]
    public async Task Virtual_alsa_port_round_trips_messages_and_long_sysex_with_capture_timestamps()
    {
        Assert.SkipUnless(OperatingSystem.IsLinux() && Environment.GetEnvironmentVariable("CODEBRIX_MIDI_TEST_ALSA") == "1",
            "Opt in with CODEBRIX_MIDI_TEST_ALSA=1 on Linux with access to /dev/snd/seq. Creates only a temporary test port.");
        //Arrange: own temporary bidirectional port; echo received events to its subscribers.
        IntPtr echo = Open("CodeBrix MidiConnect integration test");
        using var stop = new CancellationTokenSource();
        Task echoTask = Task.CompletedTask;
        try
        {
            Check(snd_seq_set_client_pool_input(echo, 2000), "size test input pool");
            Check(snd_seq_set_input_buffer_size(echo, 64 * 1024), "size test input buffer");
            int port = Check(snd_seq_create_simple_port(echo, "Test echo", 1 | 2 | 32 | 64, 2 | (1 << 20)), "create test port");
            int client = snd_seq_client_id(echo);
            echoTask = Task.Run(async () =>
            {
                while (!stop.IsCancellationRequested)
                {
                    int result = snd_seq_event_input(echo, out var pointer);
                    if (result == -11) { await Task.Delay(1, TestContext.Current.CancellationToken); continue; }
                    Check(result, "test echo input");
                    var ev = Marshal.PtrToStructure<Event>(pointer);
                    ev.Source = new Address { Client = (byte)client, Port = (byte)port };
                    ev.Destination = new Address { Client = 254, Port = 253 };
                    ev.Queue = 253;
                    Check(snd_seq_event_output_direct(echo, ref ev), "test echo output");
                }
            }, TestContext.Current.CancellationToken);
            using var manager = new MidiDeviceManager();
            using var input = await manager.OpenInputAsync($"alsa:in:{client}:{port}", cancellationToken: TestContext.Current.CancellationToken);
            using var output = await manager.OpenOutputAsync($"alsa:out:{client}:{port}", TestContext.Current.CancellationToken);
            var received = new List<MidiMessageReceivedEventArgs>();
            var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            byte[] sysex = new byte[10000];
            sysex[0] = 0xF0;
            sysex[1] = 0x7D;
            for (int i = 2; i < sysex.Length - 1; i++) sysex[i] = (byte)(i & 127);
            sysex[^1] = 0xF7;
            var sent = new[] { new MidiPacket(new byte[] { 0x90, 60, 100 }), new MidiPacket(new byte[] { 0xE0, 0, 64 }),
                new MidiPacket(new byte[] { 0xF2, 1, 2 }), new MidiPacket(new byte[] { 0xF8 }), new MidiPacket(sysex),
                new MidiPacket(new byte[] { 0x80, 60, 40 }) };
            input.MessageReceived += (_, e) => { received.Add(e); if (received.Count == sent.Length) done.TrySetResult(); };
            input.Error += (_, e) => done.TrySetException(e.Exception);
            //Act
            TimeSpan before = MidiClock.Now;
            input.Start();
            foreach (var message in sent) output.Send(message);
            await done.Task.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
            TimeSpan after = MidiClock.Now;
            input.Dispose();
            await input.Completion;
            //Assert
            Assert.Equal(sent.Select(p => p.ToString()), received.Select(p => p.Message.ToString()));
            Assert.All(received, e => Assert.InRange(e.Timestamp, before - TimeSpan.FromMilliseconds(10), after));
            Assert.True(received.Zip(received.Skip(1), (a, b) => b.Timestamp >= a.Timestamp).All(value => value));
        }
        finally
        {
            stop.Cancel();
            try { await echoTask; } finally { snd_seq_close(echo); }
        }
    }

    [Fact]
    public void Native_abi_layouts_match_64_bit_contracts()
    {
        //Arrange, Act, Assert
        Assert.Equal(28, Marshal.SizeOf<Event>());
        Assert.Equal(new IntPtr(20), Marshal.OffsetOf<Event>(nameof(Event.ExternalData)));
        Assert.Equal(120, Marshal.SizeOf<Backends.Windows.WinMmNative.Header>());
        Assert.Equal(76, Marshal.SizeOf<Backends.Windows.WinMmNative.InputCaps>());
        Assert.Equal(84, Marshal.SizeOf<Backends.Windows.WinMmNative.OutputCaps>());
    }
}
