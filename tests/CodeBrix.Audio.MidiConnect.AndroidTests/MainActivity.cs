using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Android.App;
using Android.OS;
using Android.Util;
using Android.Widget;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.MidiConnect.AndroidTests;

[Activity(Name = "com.codebrix.midiconnect.tests.MainActivity", Label = "MidiConnect Tests", MainLauncher = true, Exported = true)]
public sealed class MainActivity : Activity
{
    private readonly CancellationTokenSource lifetime = new();
    private TextView results;
    private Button run;
    protected override void OnCreate(Bundle savedInstanceState)
    {
        base.OnCreate(savedInstanceState);
        var layout = new LinearLayout(this) { Orientation = Orientation.Vertical };
        run = new Button(this) { Text = "Run MIDI loopback test" };
        results = new TextView(this) { Text = "Uses only this app's virtual MIDI echo ports. No audio output.\n" };
        var scroll = new ScrollView(this);
        scroll.AddView(results);
        layout.AddView(run);
        layout.AddView(scroll);
        SetContentView(layout);
        run.Click += async (_, _) => await RunTest();
        if (Intent.GetBooleanExtra("autorun", false)) _ = RunTest();
    }

    private void Write(string text)
    {
        Log.Info("CodeBrixMidiTest", text);
        RunOnUiThread(() => results.Append(text + "\n"));
    }

    private async Task RunTest()
    {
        run.Enabled = false;
        try
        {
            Write($"Android API {(int)Build.VERSION.SdkInt}, {RuntimeInformation.ProcessArchitecture}");
            using var manager = new MidiDeviceManager();
            var ports = manager.Refresh();
            foreach (var port in ports) Write($"{port.Direction}: {port.Id} {port.Name}");
            var source = ports.Single(p => p.Direction == MidiPortDirection.Input && p.Name.Contains("MidiConnect test echo", StringComparison.Ordinal));
            var destination = ports.Single(p => p.Direction == MidiPortDirection.Output && p.Name.Contains("MidiConnect test echo", StringComparison.Ordinal));
            // Repeated open/start/send/close also checks exclusive input-port ownership is released.
            for (int pass = 1; pass <= 3; pass++)
            {
                using var input = await manager.OpenInputAsync(source.Id, cancellationToken: lifetime.Token);
                using var output = await manager.OpenOutputAsync(destination.Id, lifetime.Token);
                using var recorder = new MidiRecorder(input);
                var messages = new List<MidiMessageReceivedEventArgs>();
                var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
                byte[] sysex = new byte[5000];
                sysex[0] = 0xF0;
                sysex[1] = 0x7D;
                for (int i = 2; i < sysex.Length - 1; i++) sysex[i] = (byte)(i & 127);
                sysex[^1] = 0xF7;
                var sent = new[] { new MidiPacket(new byte[] { 0x93, 60, 100 }), new MidiPacket(new byte[] { 0xE3, 0, 80 }),
                    new MidiPacket(new byte[] { 0xD3, 25 }), new MidiPacket(new byte[] { 0xF8 }), new MidiPacket(sysex),
                    new MidiPacket(new byte[] { 0x83, 60, 40 }) };
                input.MessageReceived += (_, e) => { messages.Add(e); if (messages.Count == sent.Length) done.TrySetResult(); };
                input.Error += (_, e) => done.TrySetException(e.Exception);
                TimeSpan before = MidiClock.Now;
                input.Start();
                await Task.Run(() => { foreach (var message in sent) output.Send(message); }, lifetime.Token);
                await done.Task.WaitAsync(TimeSpan.FromSeconds(10), lifetime.Token);
                var take = recorder.Stop();
                input.Dispose();
                await input.Completion;
                if (!sent.Select(m => m.ToString()).SequenceEqual(messages.Select(m => m.Message.ToString())))
                    throw new InvalidOperationException("Message bytes or ordering changed during loopback.");
                if (messages.Any(m => m.Timestamp < before - TimeSpan.FromMilliseconds(10) || m.Timestamp > MidiClock.Now))
                    throw new InvalidOperationException("Input timestamps are outside the capture window.");
                using var stream = new MemoryStream();
                MidiFile.Export(stream, take.ToMidiEventCollection(), leaveOpen: true);
                stream.Position = 0;
                var read = new MidiFile(stream, MidiReadMode.Strict);
                if (take.Notes.Count != 1 || take.Notes[0].IsTruncated || read.Problems.Count != 0)
                    throw new InvalidOperationException("Captured note pairing or exported MIDI file validation failed.");
                Write($"Pass {pass}: bytes, 5 KB SysEx, timestamps, note pairing, file export and close/reopen OK.");
            }
            Write("RESULT: PASS");
        }
        catch (Exception exception) { Write("RESULT: FAIL " + exception); }
        finally { if (!IsFinishing && !IsDestroyed) run.Enabled = true; }
    }
    protected override void OnDestroy()
    {
        lifetime.Cancel();
        base.OnDestroy();
    }
}
