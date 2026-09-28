# Android MIDI integration test app

**Ask Jeremy before any Android device commands.** A concurrent project shares the devices. Building this app locally requires no device access.

```sh
dotnet build tests/CodeBrix.Audio.MidiConnect.AndroidTests/CodeBrix.Audio.MidiConnect.AndroidTests.csproj -p:GeneratePackageOnBuild=false
```

The Debug APK includes ARM64 and x64, requires Android 13/API 33 and `android.software.midi`, and carries its own virtual MIDI echo service. It checks byte order, a 5 KB fragmented SysEx, timestamp range, recording/export and three open/close cycles. It does not send to physical outputs or initialize audio playback.

After permission, install `bin/Debug/net10.0-android/com.codebrix.midiconnect.tests-Signed.apk` on one selected device. Launch `com.codebrix.midiconnect.tests/.MainActivity` and tap **Run MIDI loopback test**, or pass boolean intent extra `autorun=true`. Read the `CodeBrixMidiTest` log tag for `RESULT: PASS` or a failure with its exception. Leave global logcat buffers intact. Force-stop only this test app when finished.

Jeremy's devices at the start of this session:

- Samsung ARM64: `RFCRB0MPHYH`
- Android x64 laptop: `192.168.86.225:5555`

A loopback pass verifies OS/framework and library integration on that architecture. It does not verify a USB controller, Bluetooth pairing, or physical unplug/replug. Use the desktop harness on Windows/macOS/Linux and a consumer UI for those hardware tests. No packages or drivers are installed by library startup.

## Validation record

On 2026-09-28, with Jeremy's explicit permission, both devices passed all three cycles on Android 13/API 33: the Samsung ran an ARM64 process and the laptop ran an x64 process. Both reported `RESULT: PASS`. Only this test app was force-stopped afterward; its process was confirmed absent on both devices, and the devices were released for the other session. The APK remains installed. Ask again before future device access.

Details: `TestResults/midiconnect-android-validation-2026-09-28.txt` at the repository root.
