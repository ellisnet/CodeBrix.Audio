# CodeBrix.Audio (Core package)

The shared managed core of CodeBrix.Audio: a fully managed audio file library for .NET that reads WAV, MP3, Ogg Vorbis and FLAC waveform audio, reads and writes Standard MIDI Files, reads MP3 ID3v2 and Vorbis-comment tags, plays sampled instruments in three formats, and exposes a set of DSP primitives (FFT, biquad filters, envelope follower, voice-activity detection) for audio analysis - together with **CodeBrix.Audio.Engine**, a full audio engine for playback, recording, effects, editing/mixing, MIDI and synthesis.
The `CodeBrix.Audio.Core.MitLicenseForever` NuGet package carries the two managed assemblies, `CodeBrix.Audio` and `CodeBrix.Audio.Engine`, and no native code. A platform package supplies the native audio backend and native codecs; see Installation.

CodeBrix.Audio supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

Applications normally do not reference this package directly. Reference the platform package for the target operating system instead, and it brings this one in automatically:

```
dotnet add package CodeBrix.Audio.MitLicenseForever
```

for Windows, macOS and Linux, or `CodeBrix.Audio.Android.ApacheLicenseForever` for Android (see [the Android guide](https://github.com/ellisnet/CodeBrix.Audio.Android)).

Reference `CodeBrix.Audio.Core.MitLicenseForever` directly only from a class library or add-on that uses the managed APIs - file reading and writing, MIDI, offline synthesis, DSP - and leaves the choice of playback platform to the application that consumes it. `CodeBrix.Audio.ModestSynth.MitLicenseForever` and `CodeBrix.Audio.Opus.BsdLicenseForever` are built this way.

Note that the NuGet package ID and the namespaces are different - there is no package named plain `CodeBrix.Audio`:

* NuGet package ID: `CodeBrix.Audio.Core.MitLicenseForever`
* Assemblies and primary namespaces: `CodeBrix.Audio` (`using CodeBrix.Audio.Wave;`, `CodeBrix.Audio.Playback`, `CodeBrix.Audio.Midi`, `CodeBrix.Audio.Dsp`, `CodeBrix.Audio.Synth`) and `CodeBrix.Audio.Engine` (`using CodeBrix.Audio.Engine.*;`).

Licence acceptance is required at install time. XML documentation (IntelliSense) ships alongside both assemblies.

## CodeBrix.Audio.Core supports:

Everything the CodeBrix.Audio library supports; the complete feature list, the engine description and the sample code are in the [CodeBrix.Audio README](https://github.com/ellisnet/CodeBrix.Audio/blob/main/README.md). Without a platform package, operations that need an audio device or a native codec are unavailable; managed file processing and offline rendering work on their own.

## Documentation

The NuGet package includes `AGENT-README.txt`, a complete API reference and usage guide written for AI coding agents - point your agent at that file when it is writing code against this library. One file covers both assemblies, because this package ships both.

Additional sample code and usage examples are available in the `CodeBrix.Audio.Tests` project:
https://github.com/ellisnet/CodeBrix.Audio/tree/main/tests/CodeBrix.Audio.Tests

## License

CodeBrix.Audio is licensed under the MIT License - see the
[LICENSE](https://github.com/ellisnet/CodeBrix.Audio/blob/main/LICENSE) file.

For licensing and provenance information about the open source code included in
this package, see [THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.Audio/blob/main/THIRD-PARTY-NOTICES.txt).
