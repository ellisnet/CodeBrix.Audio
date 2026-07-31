================================================================================
AGENT-README: CodeBrix.Audio
A Comprehensive Guide for AI Coding Agents
================================================================================

OVERVIEW
--------------------------------------------------------------------------------
CodeBrix.Audio is a fully managed, cross-platform audio file library for .NET.
It reads WAV, MP3, Ogg Vorbis and FLAC waveform audio, reads and writes Standard
MIDI Files, reads MP3 ID3v2 and Vorbis-comment tags, plays audio (media playback
and sound effects) through the bundled engine, renders SoundFonts (.sf2) and SFZ
instruments (.sfz) and plays MIDI music through either, and exposes a set of DSP
primitives for audio analysis. All file DECODING
and all SYNTHESIS is managed code with no
platform-specific interop, so it behaves identically on Windows, macOS, and
Linux; PLAYBACK goes through the bundled engine and its native backend.

NOTE: the CodeBrix.Audio.MitLicenseForever package ALSO bundles a second
assembly, CodeBrix.Audio.Engine — a full audio engine WITH a bundled native
backend — documented in its own section below ("CODEBRIX.AUDIO.ENGINE"). Unless a
passage says otherwise, the rest of this document describes the CodeBrix.Audio
assembly.

Parts of the library are adapted from open-source projects (NAudio, NLayer,
NVorbis and MeltySynth, all MIT); the FLAC decoder was written here from the
format specification. THIRD-PARTY-NOTICES.txt is the authoritative record of what came
from where, what was changed, and under which licenses - consult it rather than
this file for provenance questions.

================================================================================
CODEBRIX.AUDIO.ENGINE (BUNDLED IN THE SAME PACKAGE)
================================================================================
The CodeBrix.Audio.MitLicenseForever package ships a SECOND assembly,
CodeBrix.Audio.Engine, alongside CodeBrix.Audio. It is a full cross-platform
audio ENGINE — device playback and recording, SoundFont/synthesis, sequencing,
effects, and editing/mixing all live here, plus MIDI, metadata, and visualization.

  - Namespaces: CodeBrix.Audio.Engine.* — entirely separate from
    CodeBrix.Audio.*. The two assemblies share no types, and there is deliberate
    feature overlap (both read audio files, both have MIDI, both have an FFT).
    Picking which
    library to use for a given task is left to the consumer. For ordinary
    playback you do NOT need to touch the Engine at all: WaveOutEvent,
    AudioFilePlayer and SharedAudioOutput in CodeBrix.Audio wrap it for you.
  - Native dependency: unlike CodeBrix.Audio, the Engine P/Invokes a bundled
    native library, codebrix_miniaudio (built from miniaudio). It is shipped for
    seven runtime identifiers — win-x64, win-arm64, linux-x64, linux-arm64,
    linux-riscv64, osx-x64, osx-arm64 — and the right one is loaded at runtime
    with no configuration on your part. What this means for your application:
      * Those seven RIDs are the supported set. An app published for any other
        RID will start, but will throw as soon as it opens an audio device.
        (linux-riscv64 is there for the experimental .NET builds for RISC-V;
        it is not a supported .NET platform upstream yet.)
      * The native payload must travel with your app. A normal framework-
        dependent or self-contained publish handles this; if you publish
        single-file, make sure your publish settings keep native libraries
        available to the host.
      * No system audio package or system-wide codec is required on Windows,
        macOS, or Linux — playback is self-contained.
  - Attribution: derived from SoundFlow (MIT) with the namespaces renamed, and
    from miniaudio for the native backend. See THIRD-PARTY-NOTICES.txt.


TWO SOUNDFONT PATHS — WHICH ONE YOU WANT
--------------------------------------------------------------------------------
READ THIS BEFORE WRITING ANY SOUNDFONT OR MIDI-SYNTHESIS CODE. This package
contains TWO things that can play a SoundFont, and they are not interchangeable.
Choosing wrong does not fail loudly — it produces audio that is subtly wrong.

  To PLAY a .sf2 file            ->  CodeBrix.Audio.Synth
  (the renderer of record)           SoundFontSynthesizer, MidiSequence,
                                     MidiSequencer, MidiMusicPlayer

  To BUILD a synthesised           ->  CodeBrix.Audio.Engine.Synthesis
  instrument — oscillators,          Synthesizer, Sequencer, SoundFontBank,
  custom banks, MPE, MIDI            MultiInstrumentBank, the Generators/ and
  modifiers, arpeggiators            Voices/ types

Why both exist: CodeBrix.Audio.Synth is a spec-faithful SF2 renderer — it
implements the SoundFont generator AND modulator model, per-voice LFOs, a
per-voice lowpass filter, volume and modulation envelopes, reverb and chorus.
That modulator model is central to how a SoundFont is meant to sound.
CodeBrix.Audio.Engine.Synthesis is a general-purpose synthesis architecture that
can sample-play SF2 presets; it has no modulators, no per-voice LFO and no
per-voice filter. It is the better tool for building instruments, and the wrong
tool for faithfully reproducing somebody's .sf2.

Neither was retired in favour of the other. They do different jobs, and the
Engine's version is vendored SoundFlow code that is deliberately left unedited
so re-vendoring stays cheap.

SFZ HAS EXACTLY ONE PATH, and it is the first one: CodeBrix.Audio.Synth.Sfz
(SfzInstrument, SfzSynthesizer). The Engine has no SFZ support at all, so there
is no wrong turn to take. SfzSynthesizer and SoundFontSynthesizer implement the
same IMidiSynthesizer contract, which is why MidiSequencer, MidiMusicPlayer and
SoundFontRenderer drive either format without caring which - consumers choose a
file format, not an API.

TWO TYPES NAMED FOR MIDI FILES. Same rule, same reason:

  CodeBrix.Audio.Midi.MidiFile      The editable file model. Read it, edit the
                                    event collection, write it back out.
  CodeBrix.Audio.Synth.MidiSequence The immutable decoded sequence. You play it.
                                    Flattened absolute-time messages; no tracks,
                                    no meta events, no editing, no writing.

Convert with MidiSequence.FromEvents(MidiEventCollection) — build or edit in the
Midi model, then play it. There is deliberately no reverse conversion: the
sequence has already discarded track structure and non-playable meta events, so
converting back would silently lose them.


INSTALLATION
--------------------------------------------------------------------------------
NuGet package:   CodeBrix.Audio.MitLicenseForever
Command:         dotnet add package CodeBrix.Audio.MitLicenseForever

Note that the PACKAGE id carries the ".MitLicenseForever" suffix, but the
NAMESPACE is simply "CodeBrix.Audio" (no suffix).

Target framework: .NET 10.0 or higher.


KEY NAMESPACES
--------------------------------------------------------------------------------
  using CodeBrix.Audio.Wave;       // readers/writers, WaveFormat, MP3 frames, ID3,
                                   //   playback (WaveOutEvent, SharedAudioOutput)
  using CodeBrix.Audio.Playback;   // media player (AudioFilePlayer) and one-shot
                                   //   sound effects (SoundEffectClip)
  using CodeBrix.Audio.Midi;       // MIDI file read/write + event hierarchy
  using CodeBrix.Audio.Dsp;        // FFT, biquad filters, analysis primitives
  using CodeBrix.Audio.Synth;      // SoundFont (.sf2) rendering + MIDI music
                                   //   playback — see "TWO SOUNDFONT PATHS"

(Additional sub-namespaces exist for plumbing — sample/wave providers, codecs,
utilities. The managed MP3, Ogg Vorbis and FLAC decoders — CodeBrix.Audio.Mpeg,
CodeBrix.Audio.Vorbis and CodeBrix.Audio.Flac — are entirely internal; consumers
reach those formats only through the readers in CodeBrix.Audio.Wave. The one
public type in CodeBrix.Audio.Codecs a consumer might touch is ManagedCodecs,
and only when driving an engine they created themselves — see COMMON PITFALLS.)


CORE API REFERENCE
--------------------------------------------------------------------------------
Reading audio (WAV, MP3, Ogg Vorbis, FLAC):
  - WaveFileReader        : reads a .wav stream/file as a WaveStream.
  - Mp3FileReader         : reads a .mp3 stream/file as a WaveStream, decoding
                            MPEG audio to PCM via the managed NLayer decoder.
  - OggVorbisFileReader   : reads a .ogg stream/file as a WaveStream of 32-bit
                            float. Exact TotalTime and sample-accurate seeking,
                            because a Vorbis stream records its own length.
                            Exposes the stream's Vorbis comments (.Tags) and
                            .EncoderVendor.
  - FlacFileReader        : reads a .flac stream/file as a WaveStream of PCM, at
                            the file's own bit depth widened to the next standard
                            container (16/24/32-bit). Lossless: the PCM is exactly
                            what was encoded. Exact TotalTime and seeking; exposes
                            .Tags and .SourceBitsPerSample.
  - AudioFileReader       : convenience reader that opens .wav, .mp3, .ogg or
                            .flac by file extension and exposes 32-bit float
                            samples.

Writing audio:
  - WaveFileWriter        : writes PCM/IEEE-float samples to a .wav file.

Playback (cross-platform, via the bundled engine):
  - WaveOutEvent          : plays an IWaveProvider/ISampleProvider to the default
                            output device (Init/Play/Pause/Stop/Volume/PlaybackStopped).
                            NAudio-shaped, but cross-platform (Windows/macOS/Linux).
                            Every instance is a VOICE in one shared output device
                            (not a device of its own), so overlapping many sounds is
                            cheap mixing rather than many device opens. Best for short,
                            possibly-overlapping sound effects.
  - SoundEffectClip       : (namespace CodeBrix.Audio.Playback) a short sound decoded
                            ONCE into memory and then played as often as you like,
                            including many times at once. Load(path/bytes/stream),
                            Play(volume), StopAll, Duration, ActiveVoiceCount. Takes
                            any supported format at any sample rate — the decode step
                            converts to the output device's format — so an asset pack
                            that mixes rates just works. Holds decoded PCM in memory:
                            right for effects, wrong for a soundtrack.
  - AudioFilePlayer       : (namespace CodeBrix.Audio.Playback) a long-running audio
                            file player with media-transport controls — Load, Play/Pause/
                            Stop, Seek to a timecode, Volume, and readable Position and
                            Duration (TimeSpan) for a scrubber/tracker UI. Plays any
                            supported format at any sample rate, streaming from disk
                            (low memory for long tracks) and mixing into the same
                            SharedAudioOutput. A friendly wrapper over the engine's
                            SoundPlayer, so consumers never touch CodeBrix.Audio.Engine.*.
  - SharedAudioOutput     : the one shared output WaveOutEvent and AudioFilePlayer mix
                            into. Optional: Configure(sampleRate[, channels]) once at
                            start to pin the format; Shutdown() to release it.

WaveFormat:
  - WaveFormat            : sample rate, channel count, bit depth, encoding.

Metadata:
  - Id3v2Tag              : reads an ID3v2 tag block from an MP3 stream.
  - Vorbis comments       : exposed as .Tags on OggVorbisFileReader and
                            FlacFileReader (uppercase field name -> values).

MIDI:
  - MidiFile              : reads a Standard MIDI File; MidiFile.Export(...)
                            writes one.
  - MidiEvent (hierarchy) : NoteOnEvent, NoteEvent, TextEvent, MetaEvent,
                            TempoEvent, TimeSignatureEvent, etc.
  - MidiEventCollection   : per-track event collection used for read and write.

SoundFont rendering and MIDI music (CodeBrix.Audio.Synth) — read "TWO SOUNDFONT
PATHS" above first:
  - SoundFont             : a parsed .sf2. Public object model: SoundFontInfo,
                            Preset, PresetRegion, SoundFontInstrument,
                            InstrumentRegion, SampleHeader, LoopMode. Enumerate a
                            SoundFont's presets and key ranges without rendering.
  - SoundFontCache        : loads a .sf2 once and shares it. SoundFonts are tens
                            of megabytes; never reload one per track.
  - SoundFontSynthesizer  : the renderer of record. NOT thread-safe by design -
                            rendering and note events must not overlap.
  - MidiSequence          : an immutable, playable sequence.
                            MidiSequence.FromEvents(MidiEventCollection) converts
                            from the editable CodeBrix.Audio.Midi model.
  - MidiSequencer         : drives a synthesizer from a sequence; Play/Stop/Seek.
  - SoundFontRenderer     : offline rendering - Render(...) to a float buffer, or
                            RenderToWavFile(...) / RenderToWavStream(...). No
                            audio device involved, and faster than real time.
  - MidiMusicPlayer       : (CodeBrix.Audio.Playback) the transport-style player.
                            Load / Play / Pause / Stop / Seek / Volume /
                            IsLooping / Position / Duration / PlaybackEnded,
                            shaped exactly like AudioFilePlayer.

SFZ (CodeBrix.Audio.Synth.Sfz) — the .sfz counterparts of the SoundFont types:
  - SfzInstrument         : a playable SFZ instrument - typed regions, decoded
                            samples (WAV/FLAC/Ogg via the reader registry, with
                            wrong-case and backslash paths resolved), modulation
                            curves, and initial controller state. Loading is
                            tolerant: missing samples land in .Problems, opcodes
                            the engine does not implement land in
                            .UnsupportedOpcodes (and the Debug log, once per
                            name) - the first thing to check when a library
                            sounds off. Samples decode eagerly, so memory
                            follows the library's size.
  - SfzInstrumentCache    : loads an instrument once and shares it, keyed by
                            path - the SoundFontCache of SFZ. Use it.
  - SfzSynthesizer        : the SFZ renderer, a peer of SoundFontSynthesizer on
                            the same IMidiSynthesizer contract and equally NOT
                            thread-safe. Implements the SFZ articulation model:
                            key/velocity/controller/program region selection,
                            round robins and random layers (deterministic by
                            seed - see SfzSynthesizerSettings.RandomSeed), key
                            switches incl. sw_lolast/sw_hilast ranges and
                            sw_vel=previous, trigger modes incl. release samples
                            with rt_decay, off groups with fast/normal/timed
                            chokes (off_time/off_shape), polyphony and
                            note_polyphony limits, CC-triggered regions, and
                            key/velocity/controller crossfades (xfin/xfout,
                            gain or equal-power law).
                            Per voice: the DAHDSR amplifier envelope with shape
                            curvature, vel2* velocity timing and ampeg_dynamic
                            retiming; the filter and pitch envelopes (fileg/
                            pitcheg); flexible envelopes (egN, incl. the
                            key-delta portamento idiom); the v1 amplfo/fillfo/
                            pitchlfo blocks and v2 lfoN LFOs (sub-waveforms,
                            cross-LFO frequency modulation, EQ routing); two
                            filters in series; a three-band parametric EQ; ARIA
                            variators (varNN); stereo width; region delay and
                            the delay/offset/amp/fil randoms; and the
                            _onccN/_curveccN/_smoothccN modulation matrix with
                            the ARIA extended sources (128 pitch bend, 129/130
                            aftertouch, 131 velocity, 133 note, 134 key gate,
                            135/136 per-voice randoms, 137 alternate, 140/141
                            key delta).
  - SfzRegion             : one region with opcodes resolved and typed, spec
                            defaults filled in; the block families come typed
                            too (SfzEqBand, SfzLfo, SfzFlexEg, SfzModEnvelope,
                            SfzVariator). SfzSupportedOpcodes is the exact
                            implemented set (canonical, index-folded names -
                            block indices fold too, so lfo01_freq and lfo3_freq
                            are both lfoN_freq).
  - MidiMusicPlayer and SoundFontRenderer take an SfzInstrument wherever they
    take a SoundFont; MidiMusicPlayer.Load(path, midi) picks the synthesizer by
    extension.

  The structural layer underneath, for tooling rather than playback:
  - SfzParser             : ParseFile(...) / ParseText(...) read SFZ structure -
                            headers, opcodes, #define and #include.
  - SfzFile / SfzSection / SfzOpcode : the parsed result. SfzFile.Resolve(region)
                            applies region -> group -> master -> global
                            inheritance. Unknown opcodes are carried, never
                            fatal - files routinely carry opcodes meant for other
                            players, and a file must load with what is understood.

DSP / analysis primitives (CodeBrix.Audio.Dsp):
  - FastFourierTransform, Complex   : forward/inverse FFT.
  - BiQuadFilter                    : low/high/band-pass, peaking, shelving.
  - EnvelopeFollower                : amplitude envelope tracking.
  - VoiceActivityDetector           : energy-based activity detection.

Error model: invalid/corrupt files throw standard exceptions (e.g.
FormatException, EndOfStreamException, ArgumentException). Readers/writers are
IDisposable; dispose them (or use `using`) to release the underlying stream.


SAMPLE CODE
--------------------------------------------------------------------------------
Read a WAV or MP3 file as 32-bit float samples (simplest path):

    using CodeBrix.Audio.Wave;

    using var reader = new AudioFileReader("track.ogg");   // .wav / .mp3 / .ogg / .flac
    // reader.WaveFormat is 32-bit IEEE float; .SampleRate, .Channels available.
    var buffer = new float[reader.WaveFormat.SampleRate * reader.WaveFormat.Channels];
    int samplesRead;
    while ((samplesRead = reader.Read(buffer)) > 0)
    {
        // buffer[0..samplesRead] holds interleaved float samples in [-1, 1]
    }
    // reader.Volume = 0.5f;  // optional gain applied to returned samples

Read a WAV with the lower-level reader, converting to float samples:

    using var wav = new WaveFileReader("clip.wav");
    var samples = wav.ToSampleProvider();                  // ISampleProvider (float)
    var buf = new float[4096];
    int n = samples.Read(buf);

Write a WAV file:

    var format = new WaveFormat(sampleRate: 44100, bits: 16, channels: 1);
    using (var writer = new WaveFileWriter("out.wav", format))
    {
        float[] mono = GenerateSamples();                  // your samples in [-1, 1]
        writer.WriteSamples(mono, 0, mono.Length);
    }
    // Or pipe an ISampleProvider straight to disk:
    // WaveFileWriter.CreateWaveFile16("out.wav", someSampleProvider);

Play a sound to the speakers (cross-platform, via the bundled engine):

    using CodeBrix.Audio.Wave;

    var player = new WaveOutEvent();
    player.Init(new WaveFileReader("clip.wav"));   // any IWaveProvider/ISampleProvider
    player.PlaybackStopped += (s, e) => { /* ended; e.Exception is null on normal end */ };
    player.Play();                                 // Play / Pause / Stop; player.Volume = 0.5f;
    // ... player.Dispose() when finished.

    // Overlap many short sounds cheaply — each WaveOutEvent is a voice in ONE shared
    // output device, not a separate device. Apps that overlap many sounds should pin
    // the output format ONCE at start-up so no source is rejected for a rate mismatch:
    SharedAudioOutput.Configure(sampleRate: 48000);   // call before the first Play()

Play a sound effect many times, overlapping, without re-decoding it:

    using CodeBrix.Audio.Playback;

    using var laser = SoundEffectClip.Load("laser.ogg");  // decoded once, to the output format
    laser.Play();                                          // fire and forget
    laser.Play(0.4f);                                      // again, quieter, over the first
    // laser.Duration, laser.ActiveVoiceCount, laser.StopAll()
    //
    // Unlike the WaveOutEvent path below, a clip's own sample rate does not have to
    // match the output device: the decode step converts it. This is the right type for
    // asset packs, which mix rates freely.

Play a long audio file with transport / seek (a media player):

    using CodeBrix.Audio.Playback;

    var media = new AudioFilePlayer();
    media.Load("song.flac");                 // any supported format; Duration is available now
    media.PlaybackEnded += (s, e) => { /* reached the natural end */ };
    media.Play();
    // media.Position and media.Duration are TimeSpans → drive a scrubber/tracker UI.
    // media.Seek(TimeSpan.FromSeconds(83));  // jump to 1:23
    // media.Volume = 0.7f;  media.Pause();  media.Stop();  media.IsLooping = true;
    // media.Dispose() when finished.

Play MIDI music through a SoundFont (the same transport as above):

    using CodeBrix.Audio.Playback;
    using CodeBrix.Audio.Synth;

    // Share one SoundFont across every player: a .sf2 runs to tens of megabytes.
    var soundFonts = new SoundFontCache();

    var music = new MidiMusicPlayer();
    music.Load(soundFonts.Get("GeneralUser.sf2"), new MidiSequence("level1.mid"));
    music.IsLooping = true;
    music.Play();
    // Same surface as AudioFilePlayer: Position, Duration, Seek, Volume, Pause, Stop, Dispose.

Build a sequence in code and play it (the bridge between the two MIDI models):

    using CodeBrix.Audio.Midi;
    using CodeBrix.Audio.Synth;

    var events = new MidiEventCollection(1, 120);
    events.AddEvent(new NoteOnEvent(0, 1, 60, 100, 120), 1);
    events.AddEvent(new NoteEvent(120, 1, MidiCommandCode.NoteOff, 60, 0), 1);
    events.PrepareForExport();

    var sequence = MidiSequence.FromEvents(events);   // editable model -> playable sequence
    // ...then hand `sequence` to MidiMusicPlayer.Load, exactly as above.

Render MIDI music to a WAV file with no audio device (bounce / offline export):

    using CodeBrix.Audio.Synth;

    var soundFont = new SoundFont("GeneralUser.sf2");
    var sequence = new MidiSequence("level1.mid");

    SoundFontRenderer.RenderToWavFile(soundFont, sequence, "level1.wav", 44100,
                                     tail: TimeSpan.FromSeconds(2));  // let reverb decay
    // Or SoundFontRenderer.Render(...) for interleaved stereo floats in memory.

Play MIDI music through an SFZ instrument (same transport, other format):

    using CodeBrix.Audio.Playback;
    using CodeBrix.Audio.Synth;
    using CodeBrix.Audio.Synth.Sfz;

    // Samples decode once at load: share one instrument across every player.
    var instruments = new SfzInstrumentCache();
    var piano = instruments.Get("VirtualPiano.sfz");

    // If a library sounds wrong, look here FIRST: these are the opcodes it uses
    // that the engine does not implement (canonical names, e.g. "eq1_freq").
    foreach (var missing in piano.UnsupportedOpcodes) Console.WriteLine(missing);
    foreach (var problem in piano.Problems) Console.WriteLine(problem);

    var music = new MidiMusicPlayer();
    music.Load(piano, new MidiSequence("song.mid"));
    music.Play();
    // SoundFontRenderer.Render / RenderToWavFile also accept an SfzInstrument
    // for offline bounces, and SfzSynthesizer can be driven directly with
    // ProcessMidiMessage / NoteOn / NoteOff / Render for interactive use.

Read an SFZ file's structure (the layer under the engine; right for tooling):

    using CodeBrix.Audio.Synth.Sfz;

    var sfz = SfzParser.ParseFile("piano.sfz");
    foreach (var region in sfz.Regions)
    {
        var resolved = sfz.Resolve(region);      // region -> group -> master -> global
        var sample = resolved["sample"].Value;
        var lowKey = resolved.TryGetValue("lokey", out var lo) ? lo.AsNoteNumber() : 0;
    }
    // sfz.Problems lists anything odd (a missing #include, an opcode outside any header).
    // Unknown opcodes are carried, not rejected.

Decode a specific format explicitly (all fully managed; no native codec needed):

    using var mp3 = new Mp3FileReader("song.mp3");           // WaveStream of PCM
    var floats = mp3.ToSampleProvider();

    using var ogg = new OggVorbisFileReader("music.ogg");    // WaveStream of 32-bit float
    var duration = ogg.TotalTime;                            // exact, no scanning
    ogg.Position = ogg.WaveFormat.AverageBytesPerSecond * 30; // seek to 0:30

    using var flac = new FlacFileReader("album-track.flac"); // WaveStream of PCM, lossless
    var depth = flac.SourceBitsPerSample;                    // 16 / 24 / ...
    var title = flac.Tags.TryGetValue("TITLE", out var t) ? t[0] : null;

Read MP3 ID3v2 metadata:

    using var fs = File.OpenRead("song.mp3");
    var tag = Id3v2Tag.ReadTag(fs);                         // null if no ID3v2 tag
    if (tag != null) { /* tag.RawData is the raw tag bytes */ }

Write and read a Standard MIDI File:

    using CodeBrix.Audio.Midi;
    using System.Linq;

    var events = new MidiEventCollection(midiFileType: 0, deltaTicksPerQuarterNote: 480);
    var track = events.AddTrack();
    track.Add(new TempoEvent(microsecondsPerQuarterNote: 500000, absoluteTime: 0)); // 120 BPM
    track.Add(new NoteOnEvent(absoluteTime: 0, channel: 1, noteNumber: 60,
                              velocity: 100, duration: 480));                        // middle C
    events.PrepareForExport();              // REQUIRED before Export (adds note-offs + end-of-track)
    MidiFile.Export("out.mid", events);

    var midi = new MidiFile("out.mid", strictChecking: false);
    foreach (var noteOn in midi.Events[0].OfType<NoteOnEvent>())
        Console.WriteLine($"{noteOn.NoteName} vel={noteOn.Velocity} @ {noteOn.AbsoluteTime}");

DSP / analysis primitives:

    using CodeBrix.Audio.Dsp;

    // FFT magnitude spectrum (size must be a power of two; m = log2(size))
    const int m = 10, size = 1 << m;
    var bins = new Complex[size];
    for (int i = 0; i < size; i++) bins[i].X = samples[i];   // .Y left 0 for real input
    FastFourierTransform.FFT(forward: true, m, bins);
    double mag0 = Math.Sqrt(bins[8].X * bins[8].X + bins[8].Y * bins[8].Y);

    // Biquad filter (e.g. isolate a frequency band before onset detection)
    var lowPass = BiQuadFilter.LowPassFilter(sampleRate: 44100, cutoffFrequency: 1000f, q: 0.707f);
    float filtered = lowPass.Transform(inputSample);

    // Envelope follower (good basis for drum-hit / onset detection)
    var env = new EnvelopeFollower(attackMilliseconds: 5f, releaseMilliseconds: 50f, sampleRate: 44100);
    float amplitude = env.ProcessSample(inputSample);

    // Voice/activity detection (energy-based; needs a quiet stretch first to learn the floor)
    var vad = new VoiceActivityDetector(sampleRate: 44100);
    bool active = vad.Process(inputSample);


COMMON PITFALLS
--------------------------------------------------------------------------------
  - Float vs bytes: WaveFileReader/Mp3FileReader are WaveStreams that yield raw
    PCM BYTES. To get normalized float samples call .ToSampleProvider(), or just
    use AudioFileReader (which always exposes 32-bit float).
  - WAV encodings: AudioFileReader and the float pipeline support PCM and IEEE
    float WAV - 8/16/24/32-bit PCM and 32/64-bit float, including files written as
    WAVE_FORMAT_EXTENSIBLE, which is how most 24- and 32-bit WAVs are produced.
    A-law / mu-law (and other genuinely non-PCM) WAV files THROW
    (InvalidOperationException) - there is no managed codec conversion. (A-law /
    mu-law decoders exist under CodeBrix.Audio.Codecs but are not auto-wired.)
  - MP3 coverage: decoding is fully managed (NLayer) and covers MPEG-1/2/2.5
    Layer I/II/III. There is no Windows ACM/DMO/Media Foundation path.
  - Ogg Vorbis seeking, managed reader only: OggVorbisFileReader.Position is
    exact, but seeking into the middle of a Vorbis packet leaves the decoder
    without the previous packet's overlap history, so up to one block (~2048
    frames, roughly 40 ms) after a seek can differ from a sequential read of the
    same region before the two converge. Fine for scrubbing a transport; if you
    need a seamless loop point, play through the engine (AudioFilePlayer /
    SoundEffectClip), whose native decoder reconstructs the overlap. FLAC has no
    such caveat - it is lossless and seeks exactly.
  - FLAC bit depth: FlacFileReader hands back the file's own depth widened to the
    next standard container (16-bit for depths up to 16, 24-bit for 17-24,
    32-bit above). Depths that are not a whole number of bytes are left-shifted
    into that container, so a 12-bit file's values are scaled up by 16 - the same
    thing other FLAC-to-WAV converters do.
  - Dispose readers and writers (use `using`). A WaveFileWriter only flushes a
    valid RIFF header on Dispose - an undisposed writer produces a corrupt file.
  - Files opened THROUGH THE READER REGISTRY stay locked until you dispose what
    you got back. AudioFileReaderRegistry.OpenFile opens the file itself and
    hands the stream to the registered factory, which by contract does NOT own
    it - so the registry keeps the handle and returns a FileOwningWaveStream
    whose Dispose closes the reader and then the file. `using` it. Drop the
    reference without disposing and on Windows the file stays locked until the
    finalizer runs, so a later File.Delete or File.Move throws IOException
    "because it is being used by another process" - and it looks intermittent,
    because it depends on GC timing. This covers SfzSampleData.Load and
    AudioFileReader for any extension added with Register (the four built-in
    extensions take a different path inside AudioFileReader and were never
    affected). If you need the concrete reader type - WaveFileReader.Chunks, for
    instance - reach it through the .Reader property rather than casting the
    returned stream.
  - MIDI export: call MidiEventCollection.PrepareForExport() before
    MidiFile.Export(). A type-0 collection may contain only one track (Export
    throws otherwise); use type 1 for multi-track files. NoteOnEvent
    auto-creates its paired note-off.
  - No resampling in the managed reader layer: the WaveStream readers hand back
    audio at the file's own rate and never convert it. The playback types DO
    convert — AudioFilePlayer and SoundEffectClip both take any rate — so the only
    path that requires a rate match is WaveOutEvent (see the next point). If you
    are overlapping sound effects, prefer SoundEffectClip: it converts on load,
    decodes once, and costs nothing per play.
  - Shared playback output: every WaveOutEvent is a voice in ONE shared device
    (32-bit float; stereo; sample rate adopted from the first sound played, or pinned
    with SharedAudioOutput.Configure). Because there is no resampler, a source whose
    sample rate differs from the running output is rejected by Init (rather than played
    at the wrong pitch) — pre-convert it, or standardise your sound-effect rate. Mono
    and stereo sources are matched to the output automatically. The audio callback runs
    on a real-time thread, so a source's Read should not block or do disk I/O (preload
    short, frequently-triggered effects into memory).
  - Opus is NOT included. .opus files (and any Ogg stream carrying Opus rather
    than Vorbis) are recognised - metadata, duration, channels and rate all read
    correctly - but do not decode, and fail with a message saying so. Opus is
    BSD-3-Clause rather than MIT, so it ships as a separate add-on package
    instead of being folded into this one. See ADDING A CODEC below for how such
    a package plugs in.
  - Which decoder plays your .ogg: the engine prefers the bundled native library,
    which has an Ogg Vorbis decoder on every RID built from this repository's own
    sources (currently the three Linux RIDs). Where it does not — a native binary
    that predates Vorbis support — the managed decoder takes over automatically,
    so .ogg and .flac play everywhere either way; the managed path simply costs
    more CPU. Nothing to configure: SharedAudioOutput registers the managed
    fallbacks itself. Only if you construct your OWN engine do you need
    ManagedCodecs.RegisterAll(engine) to get the same safety net.
  - DSP is primitives only: there is no turnkey onset/pitch/beat detector or
    audio-to-MIDI transcriber - build those on top of the FFT / filters /
    envelope follower.
  - Threading: a single reader/stream instance is not thread-safe; give each
    thread its own reader.
  - UI threads and the Engine's synchronous APIs: a few Engine entry points are
    synchronous wrappers that do async I/O internally - SoundMetadataReader.Read,
    SoundMetadataWriter.WriteTags/RemoveTags, Recorder.StopRecording, and anything
    that opens a source through them (AudioFormat.GetFormatFromStream, the data
    providers, and therefore AudioFilePlayer.Load). They still do BLOCKING disk or
    network I/O, so on a UI thread prefer the *Async overloads where they exist, or
    do the work on a background thread.
    IMPORTANT if you are pinned to package 1.0.199.38 or earlier: on those versions
    the same calls can DEADLOCK a UI thread outright - the window never paints, and
    there is no exception and no log entry to tell you why. It is file-dependent, so
    it looks intermittent: a read served from the stream buffer completes
    synchronously and slips through, while an MP3 carrying a large ID3 tag (embedded
    album art, say) hangs. On those versions, always open audio sources from a
    background thread and marshal the result back to the UI.
  - Opening a file is cheap no matter how big it is: AudioFilePlayer streams
    through a chunked decoder instead of reading the file into memory, so Load
    reads the headers plus roughly five seconds of audio. A 50 MB WAV opens as
    fast as a 1 MB MP3 (milliseconds either way), and Duration is available as
    soon as Load returns. So do not pre-load a media library at start-up - load
    the one track you are about to play, when you are about to play it.


CODING CONVENTIONS (CodeBrix family)
--------------------------------------------------------------------------------
Nothing from here to the end of the file is needed to CONSUME the package - the
rest of this document is for people and agents working ON this repository.

These conventions govern the CodeBrix.Audio assembly and its tests. They do NOT
govern CodeBrix.Audio.Engine; see MAINTAINING CODEBRIX.AUDIO.ENGINE below.

  - Target framework net10.0 only; no multi-targeting.
  - Nullable reference types are OFF. Do NOT add `?` to reference types and do
    NOT use the null-forgiveness `!` operator. Value-type nullables (int?,
    bool?, enum?) are fine.
  - No global usings; no ImplicitUsings. All usings are explicit, at the top of
    the file, System.* first.
  - File-scoped namespaces only.
  - <GenerateDocumentationFile> is ON; every public/protected member carries an
    XML doc comment. Never suppress CS1591 — fix it at source.
  - No project-level warning suppression (no <NoWarn>, no warning-level changes).
  - Files adapted from another project (NAudio, NLayer, NVorbis) carry a
    `//was previously: <ns>;` provenance comment on the namespace line and
    preserve upstream license headers where present. The vendored decoders are
    internal; consumers reach them through the readers in CodeBrix.Audio.Wave.
    THIRD-PARTY-NOTICES.txt records what was vendored and what was changed.
  - Tests use xUnit v3 + SilverAssertions; see TESTING.


ADDING A CODEC FROM ANOTHER PACKAGE
--------------------------------------------------------------------------------
CodeBrix.Audio is MIT and stays that way, so a codec under a different licence
(Opus is BSD-3-Clause) belongs in its own package that depends on this one.
Everything such a package needs is public API; nothing here has to change to
accept one.

There are TWO seams, because there are two ways audio gets opened:

  1. PLAYBACK goes through the audio engine, which identifies formats by
     CONTENT. Supply an ICodecFactory and register it:

         SharedAudioOutput.RegisterCodecFactory(new OpusCodecFactory());

     That reaches AudioFilePlayer, SoundEffectClip, WaveOutEvent and the
     GameEngine's audio stack. The registration is remembered for the process,
     so it survives SharedAudioOutput.Shutdown() and is re-applied to every
     engine started afterwards. A consumer driving its OWN AudioEngine calls
     engine.RegisterCodecFactory(...) directly (ManagedCodecs.RegisterAll does
     this for the built-in managed codecs).

  2. READING BY FILE NAME goes through AudioFileReader, which dispatches on
     EXTENSION. Register a WaveStream factory:

         AudioFileReaderRegistry.Register(".opus", s => new OpusFileReader(s));

     AudioFileReader then opens .opus, as do AudioFileReaderRegistry.OpenFile
     and anything else built on the registry.

     STREAM OWNERSHIP - the factory is handed a stream it does NOT own. Do not
     close it, and do not make your reader close it either; the registry opened
     the file and keeps the handle. OpenFile therefore returns a
     FileOwningWaveStream pairing the two: disposing it disposes your reader and
     THEN closes the file, and its .Reader property gets callers back to your
     concrete type. A reader that takes ownership anyway is tolerated (the second
     Dispose is a no-op), but a handle nobody closes leaves the file locked on
     Windows until GC, which surfaces much later as File.Delete/File.Move failing
     with "used by another process".

Both are idempotent-ish and cheap; call them once at start-up (a static
Register() entry point on the add-on package is the friendliest shape - do not
rely on module initializers, which only run once something in the assembly is
touched).

WHAT TO BUILD ON:

  - ManagedSoundDecoder (public, CodeBrix.Audio.Codecs) is the base class for a
    managed ISoundDecoder. It handles the part every codec otherwise
    reimplements: converting the file's channel count and sample rate to what
    the engine asked for. Derive from it, supply ReadSourceSamples / SeekSource
    / DisposeCore, and call Initialize(channels, sampleRate, totalFrames) once
    the file's format is known. VorbisSoundDecoder and FlacSoundDecoder are the
    two worked examples in this repo.
  - OggCodecSniffer (public) identifies which codec an Ogg container carries -
    Vorbis, Opus, Ogg FLAC, or unknown - without disturbing the stream position.

THE OGG FORMAT-ID SHARING RULE (this is the one that bites):
The metadata layer reports the format identifier "ogg" for EVERY Ogg stream,
whatever codec is inside. So an Ogg-capable factory is offered Vorbis, Opus and
Ogg FLAC alike. Two consequences:

  - Your factory MUST check what it was actually handed (OggCodecSniffer) and
    return NULL for anything else. Returning null lets the engine move on to the
    next factory; throwing, or accepting and then failing, does not.
  - Reset the stream position on entry (if stream.CanSeek). The engine does not
    rewind between factories on the format-id path, so an earlier factory may
    have moved it.

PRIORITY CONVENTION: the built-in native factory is 0; the managed fallbacks are
-10. An add-on codec for a format the native library cannot decode can sit
anywhere below 0 - use -10 to match, or lower to defer to the built-ins.


MAINTAINING CODEBRIX.AUDIO.ENGINE
--------------------------------------------------------------------------------
src/CodeBrix.Audio.Engine/ is a ~35k-line verbatim vendoring of SoundFlow v1.4.1
(LSXPrime/SoundFlow, MIT) with namespaces renamed SoundFlow.* ->
CodeBrix.Audio.Engine.* (each namespace line carries a `//was previously:`
comment). Native build inputs live under native/miniaudio/ (miniaudio.h vendored
at native/miniaudio/miniaudio-80cf7b2/, stb_vorbis at stb_vorbis-31c1ad3/); see
native/miniaudio/README.txt.

The native library is built and verified by tools/build_native_libraries — read
its README.txt before touching anything native. It builds all three Linux RIDs
in manylinux containers on one machine (arm64 and riscv64 under emulation) and
carries host scripts for Windows and macOS; every build must pass a verification
gate (required exports, codec coverage, dependency policy, compatibility floor,
target architecture, and a dlopen + decode smoke test) before it is written to
output/. native/miniaudio/BUILD-PROVENANCE.txt records what produced each shipped
binary. All seven shipped RIDs are now self-built from the vendored sources and
all seven include the Ogg Vorbis decoder, so sf_has_vorbis() is present
everywhere; the managed Vorbis fallback now only covers a binary that lacks it
(for example a RID added later, before one is built for it).

It deliberately keeps SoundFlow's own project settings so re-syncs stay
mechanical: NRT is ON (the code uses `?` and `!`), ImplicitUsings is ON,
AllowUnsafeBlocks is ON. Do not rewrite Engine source to match family style - to
take a newer SoundFlow, re-vendor and re-apply the renames rather than editing in
place.

RE-VENDOR CHECKLIST - eight deliberate divergences must be re-applied, or they
silently regress:

  1. Namespace rename SoundFlow.* -> CodeBrix.Audio.Engine.*, with the
     `//was previously:` provenance comment on each namespace line.

  2. De-branding. "SoundFlow" is allowed only in comments, license text, and
     provenance markers - never in a live namespace, type, member, or XML-doc.
     Includes the type rename SoundFlowJsonContext -> CompositionProjectJsonContext
     and the string values FactoryId "CodeBrix.MiniAudio.Default", Vorbis
     VendorString "CodeBrix.Audio", and watermark key "DefaultCodeBrixAudioKey".

  3. ConfigureAwait(false) on EVERY await. Upstream has none, and its metadata
     layer blocks on its own async reads (BaseSoundFormatReader.Read is
     `ReadAsync(...).GetAwaiter().GetResult()`), so on a thread with a
     SynchronizationContext the continuation is posted to the very thread that is
     blocked waiting for it and the process deadlocks with no exception and no
     log. Currently 163 call sites across 24 files.
     Verify with:
         grep -rn "await " --include=*.cs src/ | grep -v ConfigureAwait
     Only multi-line awaits whose suffix landed on a later line should remain (at
     the time of writing, 3 in Editing/Persistence/CompositionProjectManager.cs).
     Note the two non-obvious forms: the suffix belongs on the end of the awaited
     EXPRESSION, not the end of the line (awaits appear inside `if` conditions and
     ternaries), and `await using var x = expr;` has to become `var x = expr;`
     plus `await using var xScope = x.ConfigureAwait(false);` so that x keeps its
     original type.

  4. MiniAudioCodecFactory.SupportedFormatIds must include "ogg". The metadata
     layer stamps an Ogg stream with the format id "ogg" and every data provider
     asks the engine for that id, so without it opening an .ogg fails with "no
     registered codec factory" and the native library is never even consulted.

  5. MiniAudioDecoder.Seek must not refuse when Length is 0. Upstream returns
     false in that case, which disables seeking entirely for any format that does
     not report a length; worse, ChunkedDataProvider/StreamDataProvider ignored
     that false and advanced their own position anyway, so a media transport
     would drift out of step with the audio. Both providers now honour a failed
     seek, and the decoder asks the native library instead of guessing.

  6. MiniAudioDecoder's memory path. When the source is a seekable Ogg stream
     under a size cap, the decoder reads it into native memory and calls
     ma_decoder_init_memory rather than feeding it through read callbacks. That
     is what puts stb_vorbis in PULL mode, where it knows the stream length and
     can seek directly; through callbacks it runs in PUSH mode, reports a length
     of zero, and can only seek by decoding forward from the start. Requires the
     Native.cs import of ma_decoder_init_memory, and NativeMemory that outlives
     the decoder (miniaudio does not copy the data it is given).

  7. OggReader computes its duration for BOTH accuracy settings, and Native.cs's
     Linux architecture switch has a RiscV64 case. An Ogg stream has no usable
     first-frame estimate, so honouring DurationAccuracy.FastEstimate literally
     meant reporting a duration of zero to anything using the format-specifying
     ChunkedDataProvider constructor - which is what AudioFilePlayer uses. Reading
     the last page's granule position is a single 64 KB tail read, so it is fast
     enough to always do. Also in Native.cs: sf_has_vorbis is imported as the
     capability probe (a missing entry point means an older binary, not an error).

  8. OggReader's OpusHead handling: report 48000, and subtract the pre-skip from
     the duration. Upstream does neither, and both matter more here than they do
     upstream, because this package's whole Opus story is handing the stream to a
     separately licensed decoder package.
       * An Opus stream ALWAYS decodes at 48 kHz. OpusHead's "input sample rate"
         (offset 12) is the rate the ENCODER was fed - 16000 for a typical
         messenger voice note, and permitted to be 0 - and RFC 7845 marks it
         informational. Reporting it is not a cosmetic slip: the data providers
         build the decoder's TARGET format from SoundFormatInfo.SampleRate, so a
         16 kHz voice note would have its 48 kHz output resampled as though it
         were 16 kHz. ffprobe reports 48000 for these files too.
       * An Ogg Opus granule position counts the pre-skip (offset 10, uint16 LE) -
         priming samples the decoder discards - so the duration must subtract it
         or every file reads a few milliseconds long.
     tests/CodeBrix.Audio.Engine.Tests/OggOpusMetadataTests.cs pins both, against
     .opus fixtures whose pre-skip and granule are known exactly.


ARCHITECTURE
--------------------------------------------------------------------------------
Source lives under src/CodeBrix.Audio/, organized into sub-folders that map to
sub-namespaces (e.g. Midi, Dsp, Codecs, Playback, and the internal wave/MP3/
Vorbis/FLAC plumbing). Only the entry-point readers/writers and WaveFormat sit
at or near the root namespace; the rest is implementation detail.

Each compressed format follows the same shape: an internal decoder in its own
namespace behind a public WaveStream reader in CodeBrix.Audio.Wave.

  - MP3   : CodeBrix.Audio.Mpeg    (NLayer-derived)      -> Mp3FileReader
  - Vorbis: CodeBrix.Audio.Vorbis  (NVorbis-derived)     -> OggVorbisFileReader
  - FLAC  : CodeBrix.Audio.Flac    (written from spec)   -> FlacFileReader

No native codec is ever required for reading. CodeBrix.Audio.Codecs additionally
holds the engine-facing side: VorbisCodecFactory and FlacCodecFactory register
those same managed decoders with the audio engine at a priority BELOW its native
factory, so the native decoder is used wherever it can be and the managed one is
the fallback. ManagedSoundDecoder carries the channel-mapping and linear
rate-conversion both of them need.

The SoundFont renderer follows the same shape one level up: CodeBrix.Audio.Synth
holds a MeltySynth-derived SF2 parser, object model and voice engine, of which
only the SoundFont object model and a small playback facade are public. See
"TWO SOUNDFONT PATHS" above before touching any of it.

The SFZ engine (CodeBrix.Audio.Synth.Sfz) is NOT a port: it was written here,
from the specification at sfzformat.com, per the porting rule below - sfizz and
the other open-source players were not read. It reuses the voice-engine SHAPE of
the MeltySynth core (block rendering, fixed-point oscillators, the anti-pop gain
ramp) but shares no code with it; the two synthesizers meet only at the
IMidiSynthesizer contract. SfzSupportedOpcodes is the authoritative implemented
set, and tools/sfz_opcode_survey reads it to report measured coverage over a
corpus of real libraries (implemented-coverage.md). As of the opcode-tail work
that coverage is 16 of 16 corpus libraries at zero unimplemented opcodes.
Where the spec describes behaviour without a formula, the engine documents its
approximation in code: the ampeg/off shape opcodes map to power curves via
2^(shape/2), curves 4-6 are x^2 / sqrt(x) / sqrt(1-x), and the flexible-envelope
levels run bipolar -1..1 (the corpus' portamento envelopes need the -1).


WHAT MAY BE READ WHEN PORTING, AND WHAT MAY BE TAKEN
--------------------------------------------------------------------------------
A standing rule for all work in this library. The bar for CodeBrix.Audio is MIT
or more permissive, and it is held deliberately: everything vendored so far
clears it (NAudio, NLayer, NVorbis, SoundFlow and MeltySynth are MIT; miniaudio
and stb_vorbis are Unlicense/MIT-0). The first license to add a condition —
BSD-3, for Opus via Concentus — was pushed into a SEPARATE package rather than
folded in. That is the precedent.

  - DOCUMENTATION AND SPECIFICATIONS ARE UNRESTRICTED. Format specs, community
    references, articles: read all of it. Documentation is not an
    implementation, so implementing from it raises no question of derivation.
    This is how the FLAC decoder here was written, and it is the intended path.

  - ANOTHER PROJECT'S SOURCE CODE IS A DIFFERENT ACT. Reading it to "see how
    they did it" and then writing something similar is derivation in substance
    even when it is not copy-paste. Taking code means taking its obligations,
    and that is a vendoring decision, not a research one.

  - IF CODE IS TAKEN, IT GETS THE FULL RITUAL — a compatible license, a
    `//was previously:` provenance comment on every file, the upstream version
    and commit recorded, and the complete license text reproduced in
    THIRD-PARTY-NOTICES.txt. Anything less is not acceptable.

Worked example, because it is the live one: for SFZ support, sfizz is the
obvious reference implementation and is BSD-2-Clause. Vendoring it would be
legally fine with attribution — but it is C++ (against the managed-only line)
and it would put a non-MIT entry in an MIT package's notices. So sfizz's SOURCE
is not read for implementation guidance. sfzformat.com is the reference, the
implementation is written from the specification, and sfizz is consulted for
exactly one narrow signal: where it marks an opcode unsupported, that is good
evidence nobody needs it.


TESTING
--------------------------------------------------------------------------------
Tests live under tests/CodeBrix.Audio.Tests/ and use xUnit v3 with
SilverAssertions (fluent assertions) and CodeBrix.TestMocks (mocking +
AutoFixture; Moq/AutoFixture-identical API under CodeBrix.TestMocks.* names).
Run them with:

    dotnet test CodeBrix.Audio.slnx

There are two test projects: tests/CodeBrix.Audio.Tests (~625 tests) and
tests/CodeBrix.Audio.Engine.Tests (~30, which exercise the native decode path
without opening a device). Most of the former are adapted from the upstream
NAudio.Core.Tests project (converted from NUnit to xUnit v3 + SilverAssertions);
the remainder are authored for the CodeBrix-specific entry points and the
analysis primitives. Coverage includes: WAV reading/writing round-trips, MP3
frame parsing and full managed decode, Ogg Vorbis and FLAC decoding, MIDI
read/write round-trips and the event hierarchy, ID3v2 tag reading, the codecs,
and the DSP primitives.

Test audio: WAV and MP3 fixtures are still built in code (TestAudio.cs). Ogg
Vorbis and FLAC cannot reasonably be hand-assembled, so those live as files under
tests/Assets/audio/ - synthesized tones, sweeps, seeded noise and silence
generated by tools/make_test_fixtures/make_fixtures.sh, NOT third-party audio.
tests/Assets/audio/AUDIO-FIXTURES.txt says what each one is for. Two .opus
fixtures are there for the METADATA reader only - nothing here decodes Opus - and
one of them is deliberately encoded from 16 kHz so its declared rate and its
decode rate disagree.

Regenerate the fixtures DELIBERATELY, not as a side effect of adding one file: an
Ogg muxer assigns a random stream serial number per run, so every .ogg and .opus
comes out with different bytes even on an identical ffmpeg build. (The .flac and
.wav files do reproduce byte for byte.)

Test SoundFont: tests/Assets/soundfont/codebrix-test.sf2, built from sine tones
by tools/make_test_fixtures/make_soundfont.py. NO real SoundFont is committed
here and none should be — they run to tens of megabytes and are variously
licensed, and this package is MIT. The fixture is deliberately shaped for the
tests that use it: two instruments, one looping and one one-shot, split key
ranges, and a global instrument zone, so region traversal and both LoopMode
branches are real. The CodeBrix.Audio.Synth tests under Synth/ are MeltySynth's
own suite, carried across with the port as its regression net; their Freeverb
(public domain) and TinySoundFont (MIT) reference vectors live under
tests/Assets/synth/. Five upstream tests that compare against parameter dumps of
the GPL-2 TimGM6mb SoundFont did NOT come across — that data cannot ship from an
MIT package. They still run in Doom.Brix, which is GPL-2, against this library's
public SoundFont object model.

How the FLAC decoder is held to account: FLAC is lossless, so every .flac fixture
ships with the .wav it was encoded from, and the decoder must reproduce that PCM
byte for byte - across bit depths, all four stereo decorrelation modes, constant/
verbatim/fixed/LPC subframes and a short final block. It is separately compared
sample for sample against the native dr_flac decoder, so two independent
implementations have to agree.

Tests that open a real audio device and make sound are opt-in:

    CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 dotnet test        # CodeBrix.Audio.Tests
    CODEBRIX_AUDIO_ENGINE_RUN_PLAYBACK_TESTS=1 dotnet test # Engine tests

Every test that is actually AUDIBLE plays the same thing: the five-tone motif from "Close
Encounters of the Third Kind" (G, A, F, F an octave below, C - see TestAudio.
BuildCloseEncountersSamples). One recognisable tune means a good run is obvious by ear
and a broken one sounds broken. Two rules keep it recognisable, and both are easy to
undo by accident:

  - AudibleTestScope wraps every sounding test. The two test assemblies run as separate
    PROCESSES, so without the named mutex inside it they play over each other and the
    tune turns to mush. It also leaves a short silence on the way out so consecutive
    tests stay distinct.
  - Where a test needs several voices at once, it starts them TOGETHER, in unison -
    louder, but still one clean statement. Staggering them proves the same thing about
    the voice count and sounds like a round.

The tests that merely need the device open (WaveOutEvent, AudioFilePlayer) play silence,
and should stay that way.

Test SFZ instruments: none are committed. The SFZ engine tests build theirs on
the fly - SfzTestInstruments writes synthetic WAV samples (constants, sines,
ramps, a hand-written smpl chunk) plus a test-authored .sfz into a temp
directory per test. Constant-value samples make gain assertions arithmetic;
ramp samples make playback speed readable. Two sharp edges those tests learned:
the synthesizer renders in 64-frame blocks, so consecutive measurements on one
instance must use block-aligned frame counts or a fresh synthesizer (a partial
block of earlier audio leaks into the next Render call - same as the SoundFont
synthesizer); and the anti-pop gain ramp means the first block after any CC
change still touches the old level, so skip a settling window before measuring.
A third: a PEAK measurement cannot tell a fade from a steady level - the first
frames of the window carry the pre-fade signal, so a 5 ms choke and a 200 ms
fade both "pass". Anything asserting fade behaviour (off_time, smoothing,
envelope shapes, tremolo depth) must measure RMS over the window instead.

A handful of other tests carry [Fact(Skip = "...")]: NUnit [Explicit] tests
carried over as skipped, plus two manual performance tests.
================================================================================
