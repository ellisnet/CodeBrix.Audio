# CodeBrix.Audio.MidiConnect

MIDI device connections for [CodeBrix.Audio](https://github.com/ellisnet/CodeBrix.Audio): discover MIDI ports, receive the notes and other messages a player performs, record performances to Standard MIDI Files, and send messages or play MIDI files out to hardware instruments. MIDI device communication needs no audio engine initialization; MIDI output sends instructions to the selected device, which must provide its own sound generation if you expect to hear music.
CodeBrix.Audio.MidiConnect depends only on .NET and CodeBrix.Audio, and is provided as a .NET 10 library and associated `CodeBrix.Audio.MidiConnect.MitLicenseForever` NuGet package.

CodeBrix.Audio.MidiConnect supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

```
dotnet add package CodeBrix.Audio.MidiConnect.MitLicenseForever
```

Note that the NuGet package ID and the namespace are different - there is no package named plain `CodeBrix.Audio.MidiConnect`:

* NuGet package ID: `CodeBrix.Audio.MidiConnect.MitLicenseForever`
* Assembly and namespace: `CodeBrix.Audio.MidiConnect` - i.e. `using CodeBrix.Audio.MidiConnect;`

XML documentation (IntelliSense) ships alongside the assembly. The package targets `net10.0` and `net10.0-android` (Android 13 / API 33 and later).

The package pulls in the following automatically; no version pinning is needed in the consuming project:

* `CodeBrix.Audio.Core.MitLicenseForever` - the shared audio library whose MIDI file model this package records to and plays from. Both packages are MIT and are published together at the same version.

To also synthesize or play audio through speakers, the application references `CodeBrix.Audio.MitLicenseForever` on Windows/Linux/macOS, or `CodeBrix.Audio.Android.ApacheLicenseForever` on Android. MidiConnect itself needs neither.

### Platforms and prerequisites

| Platform | Architectures | MIDI service |
|---|---|---|
| Windows | x64, ARM64 | Built-in WinMM |
| macOS | Intel x64, Apple Silicon ARM64 | Built-in CoreMIDI |
| Linux, including 64-bit Raspberry Pi OS | x64, ARM64 | System ALSA `libasound.so.2` and accessible `/dev/snd/seq` |
| Linux RISC-V | RISC-V 64, experimental | Same ALSA bindings; requires a compatible .NET 10 runtime |
| Android 13+ | x64, ARM64 | Built-in Android MIDI framework; device must advertise `android.software.midi` |

Device-specific drivers or configuration may still be necessary. Bluetooth pairing and opening unregistered Bluetooth devices are outside this API; discovery lists the ports the system already exposes. No native MIDI binaries are bundled, and **the library never installs software.**

Linux requires the ALSA **runtime** library, not development headers. If it cannot load, `MidiDeviceException` names `libasound.so.2`, preserves the loader exception, and includes package guidance:

| Distribution | Installation command for the administrator |
|---|---|
| Debian 13, LMDE 7, Raspberry Pi OS Trixie, Ubuntu 24.04 | `sudo apt install libasound2t64` |
| Debian 12, Raspberry Pi OS Bookworm | `sudo apt install libasound2` |
| Fedora | `sudo dnf install alsa-lib` |
| Arch Linux | `sudo pacman -S alsa-lib` |

A missing sequencer or denied access to `/dev/snd/seq` gets separate guidance about `snd_seq` and sandbox permissions.

## CodeBrix.Audio.MidiConnect supports:

* Port discovery - `MidiDeviceManager.GetInputs()`, `GetOutputs()` and `Refresh()` return fresh snapshots; `StartWatching()` polls (every 500 ms by default) and raises `PortsChanged`. Port IDs are session addresses, not durable hardware serial numbers - refresh and let the user reselect after a reconnect.
* Receiving - complete MIDI 1.0 messages as owned `MidiPacket`s, parsed from fragmented input with running status and interleaved realtime messages. Channels are **1-16**; notes, velocities, controllers and programs are **0-127**; a zero-velocity note-on is a note-off. Events run serially on a background worker, so keep handlers quick and marshal UI changes yourself.
* Bounded input - queue and SysEx limits (one MiB of incoming SysEx by default); overflow faults the input instead of silently losing note-offs.
* Timestamps - `MidiClock.Now`, a process-local monotonic clock, taken at input arrival rather than callback dispatch.
* Recording - `MidiRecorder` keeps every MIDI 1.0 message, pairs notes (FIFO for repeated keys, `IsTruncated` for notes still held at the end), and saves a Standard MIDI File at a constant tempo that preserves elapsed time.
* Sending - `Send` for one complete message, plus `NoteOn`, `NoteOff`, `ControlChange`, `ProgramChange`, `PitchBend` (0-16383, center 8192) and `Panic`.
* Timed playback - `MidiDevicePlayer` plays type 0/1 files with PPQN tempo changes, SMPTE timing, complete SysEx and escaped complete messages. Scheduling is best effort on a monotonic clock, not sample-accurate. Type 2 songs must be selected first, timed split SysEx is rejected before playback, and MIDI 2.0/UMP is not supported.
* MPE - MIDI 1.0 MPE is preserved through reception, recording, file export and output: per-channel pitch bend, channel pressure, CC74, attack/release velocities and RPN configuration. MidiConnect does not interpret MPE zones; configure the receiving synthesizer to match the controller.

## Sample Code

### Receive and record

```csharp
using System;
using System.Linq;
using CodeBrix.Audio.MidiConnect;

using var devices = new MidiDeviceManager();
var ports = devices.GetInputs();
foreach (var port in ports)
    Console.WriteLine($"{port.Id}: {port.Name}");

var selected = ports.First(); // In an app, let the user choose; handle an empty list.
using var input = await devices.OpenInputAsync(selected.Id);
input.MessageReceived += (_, e) =>
{
    if (e.Message.IsNoteOn)
        Console.WriteLine($"Channel {e.Message.Channel}, note {e.Message.Data1}, velocity {e.Message.Data2}");
};
input.Error += (_, e) => Console.Error.WriteLine(e.Exception.Message);
using var recorder = new MidiRecorder(input);
input.Start(); // Subscribe first so the first note is not missed.

Console.ReadLine();
MidiRecording take = recorder.Stop();
take.Save("performance.mid");
foreach (var note in take.Notes)
    Console.WriteLine($"{note.Note}: {note.Start} + {note.Duration}");
if (take.Failure != null)
    Console.Error.WriteLine($"Partial recording: {take.Failure.Message}");
```

### Send and play

```csharp
using System.Linq;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.MidiConnect;

using var devices = new MidiDeviceManager();
var selected = devices.GetOutputs().First();
using var output = await devices.OpenOutputAsync(selected.Id);
output.ProgramChange(1, 0);
output.NoteOn(1, 60, 100);
output.NoteOff(1, 60);
output.Send(new byte[] { 0xF0, 0x7D, 0x01, 0xF7 }); // Only send SysEx understood by your device.

var file = new MidiFile("song.mid");
// Review file.Problems if the file was read in tolerant mode.
var sequence = MidiPlaybackSequence.FromMidiFile(file);
await using var player = new MidiDevicePlayer(output);
await player.PlayAsync(sequence); // Or supply a CancellationToken.
```

The player borrows the output: await `DisposeAsync` before closing the output. At completion, failure or cancellation it releases sustain, notes and sound on the channels it used. Disposing an output directly does not send panic.

## Documentation

The NuGet package includes `AGENT-README.txt`, a complete API reference and usage guide written for AI coding agents - point your agent at that file when it is writing code against this library.

CodeBrix.Audio's own `AGENT-README.txt` covers the MIDI file model (`MidiFile`, `MidiEventCollection`) and the synthesizers this package's recordings and playback work with; read that one for them.

Additional sample code and usage examples are available in the `CodeBrix.Audio.MidiConnect.Tests` project:
https://github.com/ellisnet/CodeBrix.Audio/tree/main/tests/CodeBrix.Audio.MidiConnect.Tests

## License

CodeBrix.Audio.MidiConnect is licensed under the MIT License - see the
[LICENSE](https://github.com/ellisnet/CodeBrix.Audio/blob/main/LICENSE) file.

For licensing and provenance information about the open source code included in
this package, see [THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.Audio/blob/main/THIRD-PARTY-NOTICES.txt).
