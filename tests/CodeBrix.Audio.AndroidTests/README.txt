Core-only Android decoder regression checks
==========================================

This separate test app consumes a PACKED CodeBrix.Audio.Core.MitLicenseForever
package. It references no Android audio backend, Opus, VideoPlayback or native
codec, and never opens an audio device. It is deliberately outside the desktop
solution so ordinary builds do not require the Android workload.

Requires .NET 10 with Android workload, Android SDK 36/build-tools, JDK 21 and
an explicitly selected Android 13/API 33+ ARM64 or x64 device. Both ABIs are
packaged. Release uses the SDK's default trimming and profiled AOT; the test
manifest is debuggable so adb run-as can retrieve results in either configuration.

From the repository root, build Core with a UNIQUE numeric version, then use it:

  dotnet build CodeBrix.Audio.slnx -c Release -p:BuildVersion=YOUR_VERSION
  dotnet build tests/CodeBrix.Audio.AndroidTests -c Release \
    -p:AudioCoreVersion=YOUR_VERSION \
    -p:RestoreAdditionalProjectSources="$PWD/src/CodeBrix.Audio/bin/Release" \
    -p:AndroidSdkDirectory="$HOME/Android/Sdk" \
    -p:JavaSdkDirectory=/usr/lib/jvm/java-21-openjdk-amd64

NuGet caches versions: choose a new version for changed code. AudioCoreVersion
is required, so the app cannot silently test an arbitrary published dependency.
The app reports the loaded Core assembly version in its results.

  python3 tests/CodeBrix.Audio.AndroidTests/run-device.py \
    tests/CodeBrix.Audio.AndroidTests/bin/Release/net10.0-android36.1/com.codebrix.audio.coretests-Signed.apk \
    --adb "$HOME/Android/Sdk/platform-tools/adb" --serial DEVICE_SERIAL \
    --result artifacts/vorbis-android/release-arm64.json

Repeat with an authorized x64 device and a different result filename. The
runner installs/replaces only com.codebrix.audio.coretests. It checks ABI/API,
records the APK hash and Core version, and fails on a crash or timeout. It never
selects a device or starts an emulator. Optional cleanup:
  adb -s DEVICE_SERIAL uninstall com.codebrix.audio.coretests

Checks
------
* All legal Vorbis MDCT block sizes, 64 through 8192, against independent direct
  cosine sums, an impulse checked at every output sample, and an output guard.
  The same helper is run by desktop MdctTests. The app uses the existing
  CodeBrix.Audio.Tests friend assembly identity to reach this internal transform.
* Three existing synthetic Vorbis fixtures (mono/stereo, 22050/44100/48000 Hz),
  decoded using the public packet factory. Results must match the managed Ogg
  stream decoder sample for sample over their common prefix; the differing
  trailing container trim is bounded.

Regression record - 2026-09-28 UTC
----------------------------------
Devices: Samsung SM-G781U1 (ARM64) and HP 87FE Android-x86 laptop (x64), both
Android 13/API 33. Runtime .NET 10.0.12, Android workload 36.1.69. No emulator.

Published Core 1.0.269.1270: the initial Core-only Release reproducer passes a
64-sample impulse transform, then SIGSEGVs at size 128 on BOTH devices. No audio
backend is referenced. The negative variable Unsafe.Add offsets in Mdct's
step-3 helpers lose their sign on the optimized Mono path; crash addresses are
consistent with a zero-extended 32-bit offset scaled by sizeof(float).

Fixed local Core 1.0.271.274: the full numerical checks and all three Vorbis
fixtures pass in Release on BOTH devices. Five buffer offsets now explicitly
use signed native-sized arithmetic. Optimization stays enabled. Local evidence
is in artifacts/vorbis-android/{baseline,fixed,reference}-{arm64,x64}.json.

The sibling Dav1d Android consumer also passed full Release AV1 + Vorbis and
AV1 + Opus playback, pause, seek and drain on both devices using fixed Core,
the unchanged published Audio.Android backend, and the packed Android Dav1d
libraries. Its README contains the dependency versions and commands.

This fix is in Core, built from this repository. Publish the fixed Core package
before raising downstream published dependency pins. Consumers can reference
the fixed Core directly alongside the existing Audio.Android package; the
backend requires no source or native-library change for this fix.
