# CodeBrix.Audio

A fully managed, cross-platform audio file library for .NET. CodeBrix.Audio reads WAV and MP3 waveform audio, reads and writes Standard MIDI Files, reads MP3 ID3v2 tags, and exposes a set of DSP primitives (FFT, biquad filters, envelope follower, voice-activity detection) for audio analysis — all with no native dependencies, so it behaves identically on Windows, macOS, and Linux.
CodeBrix.Audio has no dependencies other than .NET, and is provided as a .NET 10 library and associated `CodeBrix.Audio.MitLicenseForever` NuGet package.

CodeBrix.Audio supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## CodeBrix.Audio supports:

* Reading WAV (`.wav`) waveform audio files, including PCM, IEEE-float, and A-law/μ-law encodings.
* Reading MP3 (`.mp3`) waveform audio files via a fully managed MPEG audio decoder (no ACM/DMO, no native code).
* Writing WAV (`.wav`) files.
* Reading and writing Standard MIDI Files (`.mid`).
* Reading MP3 ID3v2 metadata tags.
* Audio analysis building blocks: fast Fourier transform, biquad filters, envelope follower, and voice-activity detection.

## Sample Code

### Read a WAV or MP3 file into samples

```csharp
using CodeBrix.Audio.Wave;

using var reader = new AudioFileReader("track.mp3");
float[] buffer = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
int read = reader.Read(buffer, 0, buffer.Length);
```

### Read and write a MIDI file

```csharp
using CodeBrix.Audio.Midi;

var midi = new MidiFile("song.mid", strictChecking: false);
MidiFile.Export("song-copy.mid", midi.Events);
```

## License

The project is licensed under the MIT License. see: https://en.wikipedia.org/wiki/MIT_License
