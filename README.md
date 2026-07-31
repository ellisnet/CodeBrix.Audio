# CodeBrix.Audio

A fully managed, cross-platform audio file library for .NET. CodeBrix.Audio reads WAV, MP3, Ogg Vorbis and FLAC waveform audio, reads and writes Standard MIDI Files, reads MP3 ID3v2 and Vorbis-comment tags, plays audio through a bundled cross-platform engine, and exposes a set of DSP primitives (FFT, biquad filters, envelope follower, voice-activity detection) for audio analysis — and it behaves identically on Windows, macOS, and Linux.
CodeBrix.Audio has no NuGet dependencies. It is provided as a .NET 10 library in the `CodeBrix.Audio.MitLicenseForever` NuGet package — which also bundles **CodeBrix.Audio.Engine**, a cross-platform audio engine with a native backend (see below). File decoding is fully managed; playback goes through that engine.

CodeBrix.Audio supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## CodeBrix.Audio supports:

* Reading WAV (`.wav`) waveform audio files: 8/16/24/32-bit PCM and 32/64-bit IEEE float, including WAVE_FORMAT_EXTENSIBLE files, plus A-law/μ-law.
* Reading MP3 (`.mp3`) waveform audio files via a fully managed MPEG audio decoder (no ACM/DMO, no native code).
* Reading Ogg Vorbis (`.ogg`) files, with exact duration and sample-accurate seeking.
* Reading FLAC (`.flac`) files losslessly, at 16-, 24- and 32-bit depths.
* Writing WAV (`.wav`) files.
* Reading and writing Standard MIDI Files (`.mid`).
* Reading MP3 ID3v2 and Ogg/FLAC Vorbis-comment metadata tags.
* Playing audio: a media player with transport and seeking (`AudioFilePlayer`), and decode-once sound effects that can overlap freely (`SoundEffectClip`).
* Rendering SoundFonts (`.sf2`) and playing MIDI music: a spec-faithful SoundFont renderer with the full generator **and modulator** model, per-voice LFOs and filter, reverb and chorus — driven by a transport-style player (`MidiMusicPlayer`) or rendered offline to a WAV file (`SoundFontRenderer`).
* Reading SFZ (`.sfz`) instrument definitions: parsing only for now — headers, opcodes, `#define` and `#include`, with unknown opcodes carried rather than rejected. SFZ rendering is not implemented yet.
* Audio analysis building blocks: fast Fourier transform, biquad filters, envelope follower, and voice-activity detection.

## Sample Code

### Read an audio file into samples

```csharp
using CodeBrix.Audio.Wave;

// .wav, .mp3, .ogg or .flac - the extension picks the decoder
using var reader = new AudioFileReader("track.ogg");
float[] buffer = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
int read = reader.Read(buffer);
```

### Play a sound effect, as often as you like

```csharp
using CodeBrix.Audio.Playback;

using var laser = SoundEffectClip.Load("laser.ogg");   // decoded once, into memory
laser.Play();                                          // fire and forget
laser.Play(0.4f);                                      // again, quieter, overlapping the first
```

### Read and write a MIDI file

```csharp
using CodeBrix.Audio.Midi;

var midi = new MidiFile("song.mid", strictChecking: false);
MidiFile.Export("song-copy.mid", midi.Events);
```

### Play MIDI music through a SoundFont

```csharp
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Synth;

var soundFonts = new SoundFontCache();          // a .sf2 is tens of MB - load it once, share it

var music = new MidiMusicPlayer();
music.Load(soundFonts.Get("GeneralUser.sf2"), new MidiSequence("level1.mid"));
music.IsLooping = true;
music.Play();                                   // same transport surface as AudioFilePlayer
```

### Render MIDI music to a WAV file, with no audio device

```csharp
using CodeBrix.Audio.Synth;

SoundFontRenderer.RenderToWavFile(
    new SoundFont("GeneralUser.sf2"),
    new MidiSequence("level1.mid"),
    "level1.wav",
    tail: TimeSpan.FromSeconds(2));             // let the reverb decay rather than cutting it
```

> **Two SoundFont paths, on purpose.** `CodeBrix.Audio.Synth` is the renderer of record for playing a `.sf2`. The bundled Engine's `CodeBrix.Audio.Engine.Synthesis` is a general-purpose synthesis architecture — oscillators, custom banks, MPE, arpeggiators — that can sample-play SF2 presets but has no modulators, per-voice LFO or per-voice filter. Use the first to reproduce somebody's SoundFont, the second to build an instrument. `AGENT-README.txt` covers the split in detail.

## CodeBrix.Audio.Engine (bundled audio engine)

The same `CodeBrix.Audio.MitLicenseForever` package also ships **CodeBrix.Audio.Engine**, a full cross-platform audio engine: audio playback and recording, effects, editing/mixing, MIDI, synthesis, and visualization. Its types live under the `CodeBrix.Audio.Engine.*` namespaces (separate from `CodeBrix.Audio.*`).

The Engine has a **native dependency**: a bundled `codebrix_miniaudio` backend (built from [miniaudio](https://github.com/mackron/miniaudio), with an Ogg Vorbis decoder compiled in) shipped for seven runtime identifiers — Windows, macOS and Linux on x64 and ARM64, plus Linux on RISC-V 64. The correct native binary is selected automatically at runtime. It can be rebuilt from the sources in this repository; see `tools/build_native_libraries/README.txt`.

Parts of this library are adapted from open-source projects, and the FLAC decoder was written here from the format specification. See `THIRD-PARTY-NOTICES.txt` for the full provenance and license terms of everything incorporated.

## Formats not included

Opus is deliberately not part of this package: it is BSD-3-Clause rather than MIT, so it ships separately as `CodeBrix.Audio.Opus.BsdLicenseForever`, which depends on this package and registers itself through the public extension points (`SharedAudioOutput.RegisterCodecFactory` and `AudioFileReaderRegistry.Register`). An Opus file opened without that package installed is recognised — duration, sample rate and channels all read — and fails with a message saying it is Opus.

## License

The project is licensed under the MIT License. see: https://en.wikipedia.org/wiki/MIT_License
