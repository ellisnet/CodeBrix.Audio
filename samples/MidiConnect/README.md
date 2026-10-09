# MidiConnect

A small console harness for `CodeBrix.Audio.MidiConnect`: it lists the MIDI ports the platform
reports, monitors or records an input, plays a MIDI file to an output, sends a panic, and reads
a `.mid` file back in strict mode.

## Running it

From the repository root, using the installed .NET 10 SDK:

```sh
dotnet run --project samples/MidiConnect -p:GeneratePackageOnBuild=false -- list
dotnet run --project samples/MidiConnect --no-build -- monitor "input name or ID" 30
dotnet run --project samples/MidiConnect --no-build -- record "input name or ID" TestResults/take.mid 30
dotnet run --project samples/MidiConnect --no-build -- inspect TestResults/take.mid
dotnet run --project samples/MidiConnect --no-build -- play "output name or ID" song.mid
dotnet run --project samples/MidiConnect --no-build -- panic "output name or ID"
```

`help` (or no arguments) prints the command list. A port is chosen by its exact ID or by part
of its name, matched case-insensitively, and the choice must select exactly one port; use the
exact ID that `list` shows to disambiguate. The seconds argument defaults to 30. Ctrl+C stops
and cleans up. `panic` releases notes on all channels. Output commands send to the selected
port, so use a device that accepts MIDI input or generates sound.

`check-missing-alsa` runs an isolated missing-library simulation through the real native
resolver and exception path. It does not change system libraries.

## What it needs

* A MIDI device, or a software port, visible to the operating system's MIDI service: ALSA
  sequencer on Linux, CoreMIDI on macOS, the Windows multimedia MIDI API on Windows.
* On Linux, `libasound.so.2` and access to `/dev/snd/seq`; a sandbox can hide that device even
  when the host has it.

## Automated ALSA loopback test

The test project's ALSA integration test is opt-in. It creates and removes only its own
temporary port:

```sh
CODEBRIX_MIDI_TEST_ALSA=1 dotnet test --project tests/CodeBrix.Audio.MidiConnect.Tests/CodeBrix.Audio.MidiConnect.Tests.csproj -p:GeneratePackageOnBuild=false
```
