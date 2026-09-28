# MIDI device harness

From the repository root, using the installed .NET 10 SDK:

```sh
dotnet run --project samples/MidiConnect -p:GeneratePackageOnBuild=false -- list
dotnet run --project samples/MidiConnect --no-build -- monitor "Maschine Mikro MK3" 30
dotnet run --project samples/MidiConnect --no-build -- record "Maschine Mikro MK3" TestResults/take.mid 30
dotnet run --project samples/MidiConnect --no-build -- inspect TestResults/take.mid
dotnet run --project samples/MidiConnect --no-build -- play "output name or ID" song.mid
```

Names match case-insensitively and must select exactly one port. Use an exact ID to disambiguate. Ctrl+C stops and cleans up. `panic <output>` releases notes on all channels. Output commands send to the selected port; use a device that supports MIDI input/sound generation.

On Jeremy's LMDE workstation, run this separately before a Maschine test:

```sh
maschine-driver -c ~/GitHome/maschine-mikro-mk3-driver/example_config.toml
```

The port called `Maschine Mikro MK3 MIDI Out` is an input to this application. Its ALSA client number can change. The driver is external and is not shipped with CodeBrix.

`check-missing-alsa` runs an isolated missing-library simulation through the actual native resolver/exception path. It does not change system libraries. Linux requires `libasound.so.2` and access to `/dev/snd/seq`; a sandbox can hide that device even when the host has it.

Automated ALSA loopback (creates/removes only its own temporary port):

```sh
CODEBRIX_MIDI_TEST_ALSA=1 dotnet test --project tests/CodeBrix.Audio.MidiConnect.Tests/CodeBrix.Audio.MidiConnect.Tests.csproj -p:GeneratePackageOnBuild=false
```

Never install prerequisites automatically on Jeremy's computer. Android testing uses the separate Android app and requires his permission before device access.
