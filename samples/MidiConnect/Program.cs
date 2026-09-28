using System;
using System.Globalization;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MidiConnect;

internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        try
        {
            if (args.Length == 2 && args[0] == "inspect")
            {
                var file = new MidiFile(args[1], MidiReadMode.Strict);
                var notes = file.Events.SelectMany(track => track).OfType<NoteOnEvent>().Where(n => n.Velocity > 0).ToArray();
                var sequence = MidiPlaybackSequence.FromMidiFile(file);
                Console.WriteLine($"Strict MIDI read: {file.Tracks} tracks, {notes.Length} paired notes, {sequence.Duration.TotalSeconds:F3}s.");
                if (notes.Length > 0)
                    Console.WriteLine($"Channels: {string.Join(", ", notes.Select(n => n.Channel).Distinct())}; keys: {string.Join(", ", notes.Select(n => n.NoteNumber).Distinct().Order())}; velocity: {notes.Min(n => n.Velocity)}–{notes.Max(n => n.Velocity)}; longest hold: {notes.Max(n => n.NoteLength)} ticks at {file.DeltaTicksPerQuarterNote} PPQN.");
                return 0;
            }
            // Isolated process probe: proves the real missing-library exception path without
            // removing/replacing system files. Never used during ordinary device operation.
            if (args.Length == 1 && args[0] == "check-missing-alsa")
            {
                if (!OperatingSystem.IsLinux()) throw new PlatformNotSupportedException("This probe is for Linux.");
                NativeLibrary.SetDllImportResolver(typeof(MidiDeviceManager).Assembly, (name, _, _) =>
                    name == "libasound.so.2" ? throw new DllNotFoundException("Simulated absent ALSA runtime") : IntPtr.Zero);
                try { using var probe = new MidiDeviceManager(); probe.Refresh(); }
                catch (MidiDeviceException exception) when (exception.InnerException is DllNotFoundException)
                {
                    Console.WriteLine(exception.Message);
                    return exception.Message.Contains("sudo apt install libasound2t64", StringComparison.Ordinal) ? 0 : 1;
                }
                return 1;
            }
            if (args.Length == 0 || args[0] == "help")
            {
                Console.WriteLine("MIDI device harness\n  list\n  monitor <input-id-or-name> [seconds=30]\n  record <input-id-or-name> <file.mid> [seconds=30]\n  play <output-id-or-name> <file.mid>\n  panic <output-id-or-name>\n  inspect <file.mid>\n  check-missing-alsa\nChannels are 1-16. Put names and IDs in quotes. Ctrl+C stops and cleans up.");
                return 0;
            }
            using var stop = new CancellationTokenSource();
            ConsoleCancelEventHandler cancel = (_, e) => { e.Cancel = true; stop.Cancel(); };
            Console.CancelKeyPress += cancel;
            try
            {
                using var manager = new MidiDeviceManager();
                manager.Error += (_, e) => Console.Error.WriteLine(e.Exception.Message);
                var ports = manager.Refresh();
                if (args[0] == "list")
                {
                    foreach (var port in ports) Console.WriteLine($"{port.Direction,-6} {port.Id}  {port.Name}");
                    Console.WriteLine($"{ports.Count} ports.");
                    return 0;
                }
                if (args.Length < 2) throw new ArgumentException("Specify a port. Run 'list' first.");
                bool capture = args[0] is "record" or "monitor";
                var direction = capture ? MidiPortDirection.Input : MidiPortDirection.Output;
                var matches = ports.Where(p => p.Direction == direction && (p.Id == args[1] || p.Name.Contains(args[1], StringComparison.OrdinalIgnoreCase))).ToArray();
                if (matches.Length != 1) throw new ArgumentException($"Matched {matches.Length} {direction} ports. Use the exact ID shown by 'list'.");
                if (capture)
                {
                    bool recording = args[0] == "record";
                    if (recording && args.Length < 3) throw new ArgumentException("Specify an output .mid path.");
                    int durationIndex = recording ? 3 : 2;
                    double seconds = args.Length > durationIndex ? double.Parse(args[durationIndex], CultureInfo.InvariantCulture) : 30;
                    if (!double.IsFinite(seconds) || seconds <= 0 || seconds > 86400) throw new ArgumentException("Seconds must be greater than zero and at most 86400.");
                    using var input = await manager.OpenInputAsync(matches[0].Id);
                    using var recorder = new MidiRecorder(input);
                    Exception captureError = null;
                    input.MessageReceived += (_, e) => Console.WriteLine($"{e.Timestamp.TotalSeconds:F6}  {e.Message}  ch={e.Message.Channel} note-on={e.Message.IsNoteOn} note-off={e.Message.IsNoteOff}");
                    input.Error += (_, e) => { captureError = e.Exception; Console.Error.WriteLine(e.Exception.Message); if (!input.IsOpen) stop.Cancel(); };
                    manager.StartWatching();
                    input.Start();
                    Console.WriteLine($"Listening to {input.Port.Name} for {seconds} seconds. Play some notes.");
                    Console.Out.Flush();
                    try { await Task.Delay(TimeSpan.FromSeconds(seconds), stop.Token); } catch (OperationCanceledException) { }
                    var take = recorder.Stop();
                    input.Dispose();
                    await input.Completion;
                    Console.WriteLine($"Captured {take.Messages.Count} messages and {take.Notes.Count} notes in {take.Duration.TotalSeconds:F2}s.");
                    if (recording) { take.Save(args[2]); Console.WriteLine($"Saved {args[2]}"); }
                    return take.Failure != null || captureError != null ? 1 : 0;
                }
                using var output = await manager.OpenOutputAsync(matches[0].Id);
                if (args[0] == "panic") { output.Panic(); return 0; }
                if (args[0] != "play" || args.Length < 3) throw new ArgumentException("Unknown command or missing MIDI file. Run 'help'.");
                var file = new MidiFile(args[2]);
                foreach (string problem in file.Problems) Console.Error.WriteLine(problem);
                var sequence = MidiPlaybackSequence.FromMidiFile(file);
                await using var player = new MidiDevicePlayer(output);
                Console.WriteLine($"Playing {sequence.Messages.Count} messages to {output.Port.Name} ({sequence.Duration}).");
                try { await player.PlayAsync(sequence, stop.Token); } catch (OperationCanceledException) { }
                return 0;
            }
            finally { Console.CancelKeyPress -= cancel; }
        }
        catch (Exception exception) { Console.Error.WriteLine(exception.Message); return 1; }
    }
}
