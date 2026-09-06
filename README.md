# CodeBrix.Audio

A fully managed, cross-platform audio file library for .NET. CodeBrix.Audio reads WAV, MP3, Ogg Vorbis and FLAC waveform audio, reads and writes Standard MIDI Files, reads MP3 ID3v2 and Vorbis-comment tags, plays sampled instruments in three formats, plays audio through a bundled cross-platform engine, and exposes a set of DSP primitives (FFT, biquad filters, envelope follower, voice-activity detection) for audio analysis — and it behaves properly on Windows, macOS, and Linux.
CodeBrix.Audio is provided as a .NET 10 library and associated `CodeBrix.Audio.MitLicenseForever` NuGet package, which also bundles **CodeBrix.Audio.Engine**, a cross-platform audio engine with a native backend (see below). File decoding is fully managed; playback goes through that engine.

CodeBrix.Audio supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

```
dotnet add package CodeBrix.Audio.MitLicenseForever
```

Note that the NuGet package ID and the namespace are different - there is no package named plain `CodeBrix.Audio`:

* NuGet package ID: `CodeBrix.Audio.MitLicenseForever`
* Assembly and primary namespace: `CodeBrix.Audio` - the public types live in its sub-namespaces, i.e. `using CodeBrix.Audio.Wave;` (readers, writers, `WaveFormat`, `SharedAudioOutput`), `using CodeBrix.Audio.Playback;` (the players and sound effects), and `using CodeBrix.Audio.Midi;` / `using CodeBrix.Audio.Dsp;` / `using CodeBrix.Audio.Synth;`.

The package has no NuGet dependencies. Everything it needs is inside it, including the second assembly it ships - `CodeBrix.Audio.Engine` - and that engine's native backend. Both assemblies are referenced automatically; there is no separate Engine package to add. Licence acceptance is required at install time.

XML documentation (IntelliSense) ships alongside both assemblies.

## CodeBrix.Audio supports:

* Reading WAV (`.wav`) waveform audio files: 8/16/24/32-bit PCM and 32/64-bit IEEE float, including WAVE_FORMAT_EXTENSIBLE files, plus A-law/μ-law.
* Reading MP3 (`.mp3`) waveform audio files via a fully managed MPEG audio decoder (no ACM/DMO, no native code).
* Reading Ogg Vorbis (`.ogg`) files, with exact duration and sample-accurate seeking.
* Reading FLAC (`.flac`) files losslessly, at 16-, 24- and 32-bit depths.
* Writing WAV (`.wav`) files.
* Reading and writing Standard MIDI Files (`.mid`).
* Reading MP3 ID3v2 and Ogg/FLAC Vorbis-comment metadata tags.
* Playing audio: a media player with transport and seeking (`AudioFilePlayer`), and decode-once sound effects that can overlap freely (`SoundEffectClip`).
* Rendering SoundFonts (`.sf2`) and playing MIDI music: a spec-faithful SoundFont renderer with the full generator **and modulator** model, per-voice LFOs and filter, reverb and chorus — driven by a transport-style player (`MidiMusicPlayer`) or rendered offline to a WAV file (`SoundFontRenderer`). The player carries the controls a sequence needs and a decoded file does not: playback **speed** (tempo without pitch change), **per-channel** volume/pan/program for mixing a layered arrangement live, arbitrary MIDI messages sent safely from any thread, and two message hooks — an observe-only one for driving game events off the notes, and a modifying one for transposing or suppressing them.
* Playing SFZ (`.sfz`) instruments: a fully managed SFZ engine measured against a corpus of real free instruments at **zero unimplemented opcodes** — region selection (key/velocity/controller/program, round robins, random layers, key switches including ranges and `sw_vel`, release triggers with `rt_decay`, exclusive off groups with fast/normal/timed chokes, `polyphony` limits, crossfades), the full modulation stack (amplifier envelope with shapes, `vel2*` and `ampeg_dynamic`; filter, pitch and flexible envelopes; SFZ v1 and v2 LFOs with sub-waveforms and cross-modulation; two filters plus a three-band parametric EQ; ARIA variators, stereo width, per-voice randoms), and the `_onccN`/`_curveccN`/`_smoothccN` CC matrix with `<curve>` support and the ARIA extended sources (velocity, key delta, alternate, per-voice random…). The same `MidiMusicPlayer` and offline renderer drive `.sf2` and `.sfz` interchangeably; unknown opcodes are carried and reported (`SfzInstrument.UnsupportedOpcodes`), never fatal.
* Playing Decent Sampler instruments (`.dspreset`, `.dslibrary`, `.dsbundle`, or the folder holding one): the whole documented format, measured against the reference player rather than guessed at, and verified over a corpus of real libraries at **zero unsupported features**. The sampler (every trigger mode, round robins, tags with polyphony and voice muting, legato and glide, loops with crossfades, envelopes with curves, CC filters, release triggers, note delays and retriggers); the parameter and binding model behind a preset's knobs, so `instrument.GetControl("ATTACK").SetValue(0.4)` drives the sound exactly as a user turning it would; effect chains at instrument, bus and per-voice group level with sixteen buses and sixteen auxiliary stereo pairs; the seven modulator types with four behaviours and two scopes; the `<midi>` element with key switches, note sequences and the arpeggiator; and MPE read out of a MIDI file. Samples stream from disk under a memory budget, so a multi-gigabyte library plays inside a few hundred megabytes, and an archive is read in place rather than unpacked. Anything not honoured is reported per instrument (`Problems`, `UnsupportedFeatures`) and never thrown, and everything the engine does not implement is grouped and explained in `DecentSamplerResidualTable`. Oscillators and creative effects come from the add-on package below. "Decent Sampler" is Decidedly LLC's name for the format and its player, and is used here only to say what the files are.
* Playing audio that arrives as **codec packets** rather than as a file (`PacketAudioPlayer`) — the shape a media container's demultiplexer hands out. The player pulls packets from your `IAudioPacketSource` on the audio thread, owns the playback clock, seeks by contract (you move your source, then tell it where it now is), trims the encoder padding off the end of a track, and turns a reported gap into audio of exactly the length that was lost. Ogg Vorbis packets are built in; other codecs plug into the same packet codec seam.
* Playing a **multi-track song** on one transport (`MultiTrackPlayer`): one track per part, where a part can be a recording, a MIDI performance through a SoundFont, SFZ or Decent Sampler instrument, or **both at once** — switching between the two is a crossfade rather than a restart, because both are always rendered in step. Per-track gain, pan, mute, solo and signed offsets; a percussion rule for the zero-length notes drum parts are written with; optional level matching that makes a synthesized part sit where the recording sat in the mix; offline rendering with no audio device; and an export that merges every MIDI part into one General MIDI file. A Suno stems download loads straight into one (`SunoStemsLoader`) — the zip or the extracted folder, tolerant MIDI reading that reports what it could not honour rather than throwing, note counts and coverage so a near-empty transcription can be spotted, and a measurement of how far each transcription sits from its own recording, exposed and overridable so the synthesized parts line up with the recorded ones.
* Audio analysis building blocks: fast Fourier transform, biquad filters, envelope follower, and voice-activity detection.

## CodeBrix.Audio.Engine (bundled audio engine)

The same `CodeBrix.Audio.MitLicenseForever` package also ships **CodeBrix.Audio.Engine**, a full cross-platform audio engine: audio playback and recording, effects, editing/mixing, MIDI, synthesis, and visualization. Its types live under the `CodeBrix.Audio.Engine.*` namespaces (separate from `CodeBrix.Audio.*`).

The Engine has a **native dependency**: a bundled native backend, with an Ogg Vorbis decoder compiled in, shipped for seven runtime identifiers — Windows, macOS and Linux on x64 and ARM64, plus Linux on RISC-V 64. The correct native binary is selected automatically at runtime, and its licence notice travels beside it into your application's output folder. The backend is built from sources vendored in this repository and can be rebuilt from them; see `tools/build_native_libraries/README.txt`.

## CodeBrix.Audio.ModestSynth (add-on synthesis package)

Sound that is generated rather than played back — band-limited saw, square and triangle waveforms, a sine, seeded white noise, a plucked string built as a digital waveguide, a multi-frame wavetable player, a 64-partial additive oscillator and a six-operator FM engine, together with the creative effects a synth is expected to have — ships as a separate package, `CodeBrix.Audio.ModestSynth.MitLicenseForever`, built from this repository and published alongside `CodeBrix.Audio` at the same version. It is MIT like the core, depends on nothing but the core, and is separate because synthesis is not something every application that reads a WAV file needs to carry. Use it directly; play a whole patch from MIDI through its own polyphonic synthesizer, which every player and offline renderer here accepts; or let one call at start-up hand its oscillators and effects to the Decent Sampler engine, through a parameter model that mirrors a sampler preset's oscillator settings one for one. That call is `ModestSynth.Register()`, and it belongs before an instrument is loaded: without it a preset whose group holds an oscillator, or whose chain names one of the creative effects, still loads and plays every sampled group, with one line in `Problems` naming the package.

## Formats not included

Opus is deliberately not part of this package: it is BSD-3-Clause rather than MIT, so it ships separately as `CodeBrix.Audio.Opus.BsdLicenseForever`, which depends on this package and registers itself through the public extension points (`SharedAudioOutput.RegisterCodecFactory` for file playback, `SharedAudioOutput.RegisterPacketCodecFactory` for packet playback, and `AudioFileReaderRegistry.Register` for reading by file name). An Opus file opened without that package installed is recognised — duration, sample rate and channels all read — and fails with a message saying it is Opus.

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

var soundFonts = new SoundFontCache();          // a .sf2 is large - load it once, share it

var music = new MidiMusicPlayer();
music.Load(soundFonts.Get("GeneralUser.sf2"), new MidiSequence("level1.mid"));
music.IsLooping = true;
music.Play();                                   // same transport surface as AudioFilePlayer
```

### Play MIDI music through an SFZ instrument

```csharp
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.Sfz;

var instruments = new SfzInstrumentCache();     // samples decode once - load an instrument once, share it

var music = new MidiMusicPlayer();
music.Load(instruments.Get("VirtualPiano.sfz"), new MidiSequence("song.mid"));
music.Play();                                   // the same transport drives .sf2 and .sfz alike

// instruments.Get(...).UnsupportedOpcodes lists anything the file asked for that the
// engine does not implement - the first thing to check if a library sounds off.
```

### Play MIDI music through a Decent Sampler instrument

```csharp
using CodeBrix.Audio.ModestSynth;               // only if a preset generates rather than samples
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler;

ModestSynth.Register();                         // BEFORE any instrument is loaded

using var instrument = DecentSamplerInstrument.Load("Choir.dslibrary");
instrument.GetControl("ATTACK")?.SetValue(0.35);   // the preset's own knob

using var music = new MidiMusicPlayer();
music.Load(instrument, new MidiSequence("song.mid"));
music.Play();                                   // the same transport drives .sf2, .sfz and .dspreset

// instrument.Problems and instrument.UnsupportedFeatures say what a preset asked for
// that could not be honoured - the first thing to check if a library sounds off.
```

### Play a stems download as one song, and swap a part to its MIDI

```csharp
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Playback.Suno;

var song = SunoStemsLoader.Load("My Song Stems.zip");   // the zip, or the folder

using var player = song.CreatePlayer(new SunoPlayerOptions
{
    GeneralMidiSoundFontPath = "FluidR3_GM.sf2",        // instruments for the MIDI stems
    AutoSetRelativeTrackLevels = true,                  // synthesized parts sit where the recordings sat
});

var bass = player["Bass"];                              // by name, like song["Bass"]
if (bass.HasMidiSource)                                 // switching a part is a
{                                                       //   crossfade, not a restart
    bass.ActiveSource = TrackSource.Midi;
}

player.Prepare();
await player.LevelMeasurement;                          // the levels are in when this
                                                        //   returns - never null, so
                                                        //   no guard is needed
player.Play();

// Or build the song yourself, one track per part:
// var mix = new MultiTrackPlayer();
// mix.Add(new AudioTrack("drums.wav", "Drums"));
// mix.Add(new MidiTrack(sequence, rate => new SoundFontSynthesizer(gm, rate), "Bass"));
```

### React to the music, and mix a layer live

```csharp
var music = new MidiMusicPlayer();
music.Load(soundFonts.Get("GeneralUser.sf2"), new MidiSequence("battle.mid"));

// Observe-only: runs on the audio thread and cannot break playback. This is the
// hook for driving something outside the audio - a screen shake on a drum hit,
// a particle on a note, a rhythm-game display.
music.MidiMessageProcessed = (channel, command, note, velocity) =>
{
    if (command == 0x90 && velocity > 0 && channel == 9)    // channel 10 = drums
        Volatile.Write(ref _drumHitPending, 1);             // your own thread reads this
};

music.Play();

music.SetChannelVolume(3, 0f);   // fade a layer out of the arrangement...
music.SetChannelVolume(3, 1f);   // ...and back in
music.Speed = 0.75f;             // slow motion, unchanged pitch
```

> `MidiMessageFilter` is the other hook — it **replaces** delivery, so it can transpose, re-channel or suppress messages. A filter that forgets to forward the message silences the music; use `MidiMessageProcessed` when you only want to watch. See `AGENT-README.txt`.

### Render MIDI music to a WAV file, with no audio device

```csharp
using CodeBrix.Audio.Synth;

SoundFontRenderer.RenderToWavFile(
    new SoundFont("GeneralUser.sf2"),
    new MidiSequence("level1.mid"),
    "level1.wav",
    tail: TimeSpan.FromSeconds(2));             // let the reverb decay rather than cutting it

// The same renderer takes an SfzInstrument in place of the SoundFont.
```

> **Two SoundFont paths, on purpose.** `CodeBrix.Audio.Synth` is the renderer of record for playing a `.sf2`. The bundled Engine's `CodeBrix.Audio.Engine.Synthesis` is a general-purpose synthesis architecture — oscillators, custom banks, MPE, arpeggiators — that can sample-play SF2 presets but has no modulators, per-voice LFO or per-voice filter. Use the first to reproduce somebody's SoundFont, the second to build an instrument. `AGENT-README.txt` covers the split in detail.

### Play audio that arrives as codec packets

```csharp
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Wave;

SharedAudioOutput.Configure(48000);          // what media containers carry

var player = new PacketAudioPlayer();
player.PlaybackEnded += (s, e) => { /* the track finished */ };

// codecPrivate is the setup data the container carries for the track; mySource
// is your IAudioPacketSource, which the player pulls from on the audio thread.
player.Open("vorbis", codecPrivate, mySource);
player.SetTrailingTrim(TimeSpan.FromMilliseconds(12));   // drop the encoder padding
player.Volume = 0.8f;
player.Play();

TimeSpan where = player.Position;            // the audio clock; readable from any thread
```

> Running dry is not an error: return `false` from `TryReadPacket` with `EndOfStream` still false and the player plays silence for that moment and keeps the voice alive. See `AGENT-README.txt`, "PLAYING AUDIO THAT ARRIVES AS PACKETS", for seeking, trimming and packet loss.

## Documentation

The NuGet package includes `AGENT-README.txt`, a complete API reference and usage guide written for AI coding agents - point your agent at that file when it is writing code against this library. One file covers both bundled assemblies, because one package ships both.

Additional sample code and usage examples are available in the `CodeBrix.Audio.Tests` project:
https://github.com/ellisnet/CodeBrix.Audio/tree/main/tests/CodeBrix.Audio.Tests

## License

CodeBrix.Audio is licensed under the MIT License - see the
[LICENSE](https://github.com/ellisnet/CodeBrix.Audio/blob/main/LICENSE) file.

For licensing and provenance information about the open source code included in
this package, see [THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.Audio/blob/main/THIRD-PARTY-NOTICES.txt).
