================================================================================
AGENT-README: CodeBrix.Audio
A Guide for AI Coding Agents - CONSUMING the CodeBrix.Audio.MitLicenseForever
NuGet package
================================================================================

OVERVIEW
========
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

It targets .NET 10 or later.


INSTALLATION
============
NuGet package:   CodeBrix.Audio.MitLicenseForever
Command:         dotnet add package CodeBrix.Audio.MitLicenseForever

Note that the PACKAGE id carries the ".MitLicenseForever" suffix, but the
NAMESPACE is simply "CodeBrix.Audio" (no suffix).

  License:        MIT (licence acceptance is required)
  Target:         .NET 10 or later
  NuGet deps:     NONE. The package has no PackageReference dependencies of its
                  own - everything it needs, including the bundled
                  CodeBrix.Audio.Engine assembly and its native backend, is
                  inside the package.
  Assemblies:     TWO, from this one package - CodeBrix.Audio and
                  CodeBrix.Audio.Engine. Both are referenced automatically; you
                  do not add a second PackageReference for the Engine, and there
                  is no separate Engine package to find.
  Native payload: the Engine ships codebrix_miniaudio for seven runtime
                  identifiers - win-x64, win-arm64, linux-x64, linux-arm64,
                  linux-riscv64, osx-x64, osx-arm64 - under runtimes/<rid>/
                  native/. The right one is loaded at runtime with no
                  configuration on your part. See the ENGINE section below for
                  what that means for publishing.
  System deps:    none. No system audio package and no system-wide codec is
                  required on Windows, macOS or Linux.

ADD-ON PACKAGES IN THE FAMILY

  CodeBrix.Audio.Opus.BsdLicenseForever
      Adds Ogg Opus (.opus) decoding and encoding. A separate package because
      Opus is BSD-3-Clause rather than MIT; fully managed, no native code. One
      call - CodeBrixAudioOpus.Register() - and .opus reaches every path a
      built-in format reaches. Its guide is at
      https://github.com/ellisnet/CodeBrix.Audio.Opus/blob/main/AGENT-README.txt

  Any other codec package built on the seams described in ADDING A CODEC FROM
  ANOTHER PACKAGE below plugs in the same way.


KEY NAMESPACES / USINGS
=======================
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


TWO SOUNDFONT PATHS - WHICH ONE YOU WANT
========================================
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

Neither was retired in favour of the other. They do different jobs.

SFZ HAS EXACTLY ONE PATH, and it is the first one: CodeBrix.Audio.Synth.Sfz
(SfzInstrument, SfzSynthesizer). The Engine has no SFZ support at all, so there
is no wrong turn to take. SfzSynthesizer and SoundFontSynthesizer implement the
same IMidiSynthesizer contract, which is why MidiSequencer, MidiMusicPlayer and
SoundFontRenderer drive either format without caring which - consumers choose a
file format, not an API.

THE TWO MIDI MESSAGE HOOKS - WHICH ONE YOU WANT
===============================================
MidiMusicPlayer exposes two hooks onto the messages a sequence plays. They look
similar and do opposite things, so this is the same kind of trap as the two
SoundFont paths above.

  To REACT to the music          ->  .MidiMessageProcessed
  (a drum hit shaking the             (MidiMessageObserver)
  screen, a note spawning a           Observe-only. Runs AFTER the message has
  particle, karaoke, a rhythm         been delivered. It cannot break playback.
  game)                               THIS IS ALMOST ALWAYS THE ONE YOU WANT.

  To CHANGE the music as it      ->  .MidiMessageFilter
  plays (transpose, re-channel,       (MidiSequencer.MessageHook)
  suppress, remap)                    REPLACES delivery. Your hook now owns
                                      sending the message on.

THE TRAP: MidiSequencer's hook was always a MODIFYING hook — when it is set, the
sequencer does NOT call the synthesizer itself (MidiSequencer.ProcessEvents). So
a filter that inspects a message and returns without calling
ProcessMidiMessage on the synthesizer it was handed SILENCES THE MUSIC
COMPLETELY. That reads as a bug in the player rather than in the hook, which is
exactly why the observe-only hook exists next to it — it cannot do this.

Both run on the REAL-TIME AUDIO THREAD, so both must be fast, allocation-free,
and must never block or touch UI. Do not call back into the player from either
one; it takes the same lock the audio thread is already holding, and deadlocks.
Hand data to your own thread and act on it there.

The synthesizer passed to a FILTER is safe to use FROM INSIDE THAT CALL ONLY —
the lock that serializes it against rendering is held for the duration. Never
store it for later. To send messages from your own thread, use
MidiMusicPlayer.SendMidiMessage (and the SetChannel* helpers), which take the
lock properly. There is deliberately NO property handing back the
IMidiSynthesizer: it is not thread-safe, and the lock that makes it safe is not
reachable from outside this library, so such a property could not be used
correctly.

WHAT THE PLAYER DELIBERATELY DOES NOT GIVE YOU: tempo, time signature and
markers. MidiSequence does not carry them — it consumes tempo changes while
merging tracks (they are baked into the message time stamps and dropped) and
never parses time signature, markers or track names at all. Read those from the
OTHER MIDI model instead, which parses all of them:

    var file = new MidiFile(path, strictChecking: false);   // CodeBrix.Audio.Midi
    var bpm  = file.Events[0].OfType<TempoEvent>().First().Tempo;
    var sig  = file.Events[0].OfType<TimeSignatureEvent>().FirstOrDefault();

Parsing the same file twice — once as MidiSequence to play, once as MidiFile to
inspect — is the intended pattern. MIDI files are kilobytes; this costs nothing.

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



CORE API REFERENCE
==================
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
                            shaped exactly like AudioFilePlayer. Plus the
                            controls a sequence needs that a decoded file does
                            not:
                              .Speed              tempo multiplier, 1.0 default;
                                                  scales tempo without pitch.
                              .Sequence           the loaded MidiSequence (the
                                                  only way to reach it after the
                                                  Load(path, path) overload).
                              SendMidiMessage()   send alongside the sequence,
                                                  from any thread, safely.
                              SetChannelVolume()  CC7 - how a layered
                                                  arrangement is mixed live.
                              SetChannelPan()     CC10.
                              SetChannelProgram() program change.
                              .MidiMessageProcessed  observe-only note hook.
                              .MidiMessageFilter     modify/replace hook.
                            See "THE TWO MIDI MESSAGE HOOKS" above before using
                            either hook - they are not interchangeable.

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


MORE OF THE TOOLBOX (named here so you know it exists; these are the NAUDIO-
SHAPED plumbing types, and they behave the way their NAudio counterparts do):

  Other file formats:
  - AiffFileReader / AiffFileWriter : .aiff read and write, alongside the WAV
                                      pair. AiffFileWriter.CreateAiffFile(
                                      filename, WaveStream) is the one-liner.
  - WaveFormatExtensible, Mp3WaveFormat, AdpcmWaveFormat : the WaveFormat
                                      subclasses you meet when inspecting a
                                      file's header rather than its samples.

  Wave/sample providers (compose these between a reader and an output):
  - BufferedWaveProvider     push audio in from one thread, pull it out from the
                             audio thread.
  - MixingWaveProvider32     sum several 32-bit float sources into one.
  - VolumeSampleProvider     gain.        PanningSampleProvider   stereo pan.
  - OffsetSampleProvider     skip/pad/take a section of a source.
  - FadeInOutSampleProvider  timed fades.
  - ConcatenatingSampleProvider  play sources back to back.
  - MultiplexingSampleProvider   route input channels to output channels.
  - MonoToStereoSampleProvider / StereoToMonoSampleProvider : channel count.
  - SilenceProvider          a source of silence.
  - SignalGenerator          sine/square/saw/noise/sweep test tones.
  - WaveChannel32            a WaveStream promoted to 32-bit float with volume
                             and panning.
  - RawSourceWaveStream      wrap headerless PCM (a byte[] or Stream) plus a
                             WaveFormat as a WaveStream.
  - WaveRecorder             a pass-through IWaveProvider that also writes
                             everything read through it to a .wav file.
  - IWavePlayer / IWavePosition : the interfaces WaveOutEvent implements.

  Codecs you can call directly (CodeBrix.Audio.Codecs):
  - ALawEncoder / ALawDecoder, MuLawEncoder / MuLawDecoder : companded 8-bit
    telephony audio, sample-at-a-time (LinearToALawSample / ALawToLinearSample)
    or in bulk (Decode(ReadOnlySpan<byte>, Span<short>)). These are NOT wired
    into the WAV reader - see COMMON PITFALLS - so converting an A-law WAV means
    calling them yourself.

  More MIDI events (CodeBrix.Audio.Midi), all under MidiEvent:
  - ControlChangeEvent (with the MidiController enum), PatchChangeEvent,
    PitchWheelChangeEvent, ChannelAfterTouchEvent, SysexEvent, and the MetaEvent
    subclasses TextEvent, TempoEvent, TimeSignatureEvent, KeySignatureEvent.
    MidiCommandCode is the status-byte enum, and MidiMessage builds a raw
    message from its parts.

  More DSP and utilities:
  - FftProcessor / FftWindowType : windowed FFT over a stream of samples, when
    you want a spectrum rather than a single transform.
  - Decibels : linear amplitude <-> dB.
  - CircularBuffer : the ring buffer BufferedWaveProvider is built on.
  - IgnoreDisposeStream : hand a Stream to something that disposes what it is
    given, without losing the stream.


ADDING A CODEC FROM ANOTHER PACKAGE
===================================
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
    the file's format is known. The library's own VorbisSoundDecoder and
    FlacSoundDecoder are the two worked examples; both are internal, so read
    them as source rather than deriving from them:
    https://github.com/ellisnet/CodeBrix.Audio/tree/main/src/CodeBrix.Audio/Codecs
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

A THIRD SEAM exists for audio that never arrives as a file at all - codec
packets out of a media container - with its own factory interface and its own
player. See PLAYING AUDIO THAT ARRIVES AS PACKETS below.


PLAYING AUDIO THAT ARRIVES AS PACKETS
=====================================
Everything above assumes the audio is a FILE - something with a container around
it that a reader can open and seek in. Audio pulled out of a video container
does not arrive that way: a demultiplexer hands out bare codec packets, a few
hundred a second, with no framing of their own. That is a different seam, and it
has its own three pieces.

  1. IPacketSoundDecoder (CodeBrix.Audio.Engine.Interfaces) decodes ONE packet
     at a time:

         int DecodePacket(ReadOnlySpan<byte> packet, Span<float> output)
         int MaxSamplesPerPacket { get; }   // size `output` to this
         int PreSkipSamples { get; }        // codec priming, per channel
         void Reset()                       // after the source jumps
         int Channels { get; } int SampleRate { get; }
         int ConcealLoss(int lostFrames, Span<float> output)   // a gap
         bool SupportsLossConcealment { get; }

     TWO THINGS SURPRISE PEOPLE. DecodePacket may return ZERO and that is
     success, not an ending: a lapped-transform codec finalises a packet's
     samples only once the NEXT packet has been overlapped onto it, so the first
     packet after construction or Reset() yields nothing. And the decoder does
     not trim the end of the stream - the container knows where the audio really
     stops (a total-sample count, an end-trim field), so applying that is the
     caller's job. PacketAudioPlayer.SetTrailingTrim does it for you; see
     TRIMMING THE END OF A TRACK below.

     ConcealLoss and SupportsLossConcealment have DEFAULT IMPLEMENTATIONS, so an
     existing decoder keeps compiling and working untouched. The default
     forwards to DecodePacket with an EMPTY packet, which is the long-standing
     way of saying "one packet was lost", and reports SupportsLossConcealment as
     false. A decoder whose codec really can synthesise audio across a gap
     overrides both, so it can conceal the length the container actually lost
     instead of assuming one packet. lostFrames counts FRAMES PER CHANNEL at the
     decoder's own rate, like PreSkipSamples; `output` is sized to
     MaxSamplesPerPacket as usual, and a call may cover less than was asked for
     and be called again. The built-in Vorbis decoder has no concealment - it
     answers a gap with SILENCE of exactly the right length, so the timeline
     keeps its shape - and the CodeBrix.Audio.Opus package conceals for real.

  2. IPacketCodecFactory (same namespace) is how a codec gets registered, and it
     mirrors ICodecFactory exactly - FactoryId, Priority, and SupportedCodecIds,
     which name the CODEC ("vorbis", "opus"), not the container:

         SharedAudioOutput.RegisterPacketCodecFactory(new SomePacketCodecFactory());

     Same rules as the stream seam: call it once at start-up, the registration
     lasts for the process, registering the same instance twice is ignored, and
     a factory that cannot serve a request returns null rather than throwing so
     the next one gets its turn. A consumer driving its OWN AudioEngine calls
     engine.RegisterPacketCodecFactory(...) instead. Vorbis packets are built in
     and always registered; Opus packets come with the CodeBrix.Audio.Opus
     add-on package.

     To ASK WHETHER A CODEC IS AVAILABLE, without starting anything:

         bool ok = SharedAudioOutput.IsPacketCodecSupported("opus");
         IReadOnlyCollection<string> all = SharedAudioOutput.SupportedPacketCodecIds;

     Both are matched case-insensitively, neither starts the shared output, and
     neither opens the audio device. They answer for the SHARED OUTPUT: the
     packet codecs built into this package plus everything registered with
     SharedAudioOutput.RegisterPacketCodecFactory, in that order. A factory
     registered directly on an AudioEngine of your own with
     engine.RegisterPacketCodecFactory(...) is NOT visible to them, because that
     engine is not the shared output's - register through SharedAudioOutput to
     be seen by both. It is a question about the seam, not about one track: a
     factory may still decline a particular piece of codecPrivate.

     To decode packets yourself, without playing them:

         var decoder = SharedAudioOutput.CreatePacketDecoder("vorbis", codecPrivate);

     CREATEPACKETDECODER STARTS THE SHARED OUTPUT - it opens the audio device,
     because the codec registry lives on the running engine (at 48 kHz unless
     Configure pinned a rate). Use it only when you are actually going to decode
     something. If all you want to know is whether a codec is available, use
     IsPacketCodecSupported, which starts nothing.

     `codecPrivate` is whatever the container carries for the track: for Vorbis
     the three Xiph-laced setup headers (a count byte, the lengths of the first
     two headers as 255-continuation bytes, then the identification, comment and
     setup headers back to back); for Opus the identification-header bytes.

  3. PacketAudioPlayer (CodeBrix.Audio.Playback) plays them. It is the supported
     route from packet audio to the speakers, because WaveOutEvent refuses a
     source whose rate does not match the running output while this player
     decodes through the engine's own conversion.

THE PACKET FEED IS PULLED, NOT PUSHED. You implement IAudioPacketSource and the
player asks it for the next packet ON THE AUDIO THREAD, exactly when it needs
one:

    public interface IAudioPacketSource
    {
        bool TryReadPacket(out AudioPacket packet);   // false = none ready
        bool EndOfStream { get; }                     // true = no more, ever
    }

  - Both members must return IMMEDIATELY. Read ahead on your own thread into a
    bounded queue and hand packets out of that queue; never block, never do I/O
    here.
  - RUNNING DRY IS NOT AN ERROR. Return false with EndOfStream still false and
    the player plays silence for that moment and keeps the voice alive, ready
    for the packets that follow. Playback ends only when EndOfStream is true and
    the decoded audio has run out, at which point PlaybackEnded is raised away
    from the audio thread.
  - AudioPacket is a small struct carrying ReadOnlyMemory<byte> Data, an
    optional Timestamp, an optional DiscardPadding (see TRIMMING THE END OF A
    TRACK) and, for a packet that reports a gap rather than delivering audio,
    IsLoss / LossDuration / LossFrames (see REPORTING PACKET LOSS). The memory
    must stay valid until the next TryReadPacket call - the player decodes each
    packet before asking for another and never keeps one - so handing out slices
    of a rolling buffer is fine.

Playing:

    using CodeBrix.Audio.Playback;
    using CodeBrix.Audio.Wave;

    SharedAudioOutput.Configure(48000);          // see the rate advice below

    var player = new PacketAudioPlayer();
    player.PlaybackEnded += (s, e) => { /* the track finished */ };
    player.Open("vorbis", codecPrivate, myPacketSource);
    player.Volume = 0.8f;
    player.Play();

    TimeSpan where = player.Position;             // the clock; any thread

POSITION IS THE CLOCK. It counts the audio actually handed to the mixer since
the last Seek, at the codec's own sample rate, and is readable from any thread -
so anything being synchronised to the audio should read it rather than keeping a
clock of its own. Silence played through an underrun does NOT advance it;
samples discarded as codec priming or seek pre-roll DO, because they are media
time.

SEEKING IS A CONTRACT, because the player has no container to seek in. Move your
own source FIRST, then tell the player where it now is:

    myPacketSource.MoveTo(keyframeBefore(target));      // your reader
    player.Seek(firstPacketTimestamp, preRoll: gap);    // then the player

  - firstPacketTimestamp is the timestamp of the very next packet the source
    will hand over. Position starts counting again from there.
  - preRoll is how much audio to decode and throw away before any is heard. A
    codec carrying state between packets cannot decode correctly at a jump, so
    start a little BEFORE the real target - one packet for Vorbis, about 80 ms
    for Opus - and pass the gap as preRoll. Once it has been worked through,
    Position reads the position you were aiming at.
  - Calling Seek while the OLD packets are still queued dates the clock to the
    new position and then plays the old audio against it. Order matters.
  - A source that had reported EndOfStream is expected to report false again
    once it has been repositioned.

TRIMMING THE END OF A TRACK. An encoder pads the end of what it encodes, and the
CONTAINER - not the codec - records how much: a discard-padding value on the last
block, a trailing sample count in the track header. Without that being applied,
the padding plays: tens of milliseconds of encoder tail at the end of every
track. Two ways to apply it, and you can use either or both:

    player.SetTrailingTrim(TimeSpan.FromMilliseconds(12));   // a duration
    player.SetTrailingTrimFrames(576);                       // frames/channel
    TimeSpan trim = player.TrailingTrim;                     // what is in effect

or let the packets carry it, which suits a container that states it per block:

    packet = new AudioPacket(bytes, timestamp, discardPadding);

  - HOW IT WORKS. The last `trim` worth of everything the source will ever
    deliver is held back and then thrown away. The player cannot know which
    packet is the last one until the source says so, so it keeps the most recent
    `trim` worth of decoded audio in hand and releases a sample only once more
    than that much has been decoded behind it; when the source reports the end
    of the stream, what is still in hand is discarded. The cost is latency of
    exactly `trim` - normally less than one packet - and no allocation while
    playing.
  - SET IT BEFORE OR AFTER Open, and at any time before the source ends. A trim
    of zero plays everything, which is the default.
  - FRAMES ARE THE EXACT FORM, counted per channel at the decoder's own rate -
    the same unit as PreSkipSamples at the other end of the track. A duration is
    rounded to the nearest frame.
  - POSITION NEVER COUNTS TRIMMED AUDIO, because it counts what reached the
    mixer.
  - SEEK clears what is in hand - the audio around a jump is not the end of the
    track - but keeps the trim itself, which belongs to the track. So does Open:
    set the trim again, or to TimeSpan.Zero, when you open a different track.
  - A trim longer than the whole track leaves nothing to hear and still ends
    cleanly, raising PlaybackEnded.
  - PER-PACKET PADDING is applied as the LARGER of AudioPacket.DiscardPadding on
    the packet just delivered and the track-level trim, so passing a container's
    per-block value straight through works. One caveat: a per-packet value is
    only learned when that packet arrives, so it can only hold back what is
    still in hand plus what that packet decodes to. A padding that can exceed
    one packet should be set with SetTrailingTrim instead, which applies from
    the start and is therefore always exact. A padding on a packet that is NOT
    the last one merely delays audio - the next packet lets it out again -
    rather than dropping it.
  - The codec's own PreSkipSamples discard at the START of a track and this
    trim at the END are independent of each other.

REPORTING PACKET LOSS. When your demultiplexer can see that packets are missing
- a jump in the timestamps, a container-level loss marker, a network read that
gave up - say so, with the LENGTH:

    packet = AudioPacket.Loss(TimeSpan.FromMilliseconds(60));   // a duration
    packet = AudioPacket.Loss(2880);                            // frames/channel

  - The player asks the decoder to conceal exactly that much (ConcealLoss, in
    helpings of at most MaxSamplesPerPacket) and fills whatever the decoder
    cannot with silence. Either way the gap comes out the length it really was,
    so the audio after it keeps the position it had instead of sliding earlier.
  - CONCEALED AUDIO IS MEDIA TIME: it advances Position, and it flows through
    the trailing-trim hold-back like any other audio.
  - DO NOT USE IT FOR AN UNDERRUN. A moment when your reader has not kept up is
    not lost audio: return false from TryReadPacket, which costs nothing and
    consumes none of the timeline.
  - THE LENGTHLESS FORM STILL WORKS: a packet with empty Data and no loss marker
    means one packet was lost without saying how long it was, and is passed to
    the decoder as an empty packet - what it makes of that is its own business
    (the built-in Vorbis decoder produces nothing, since it cannot know the
    length).
  - Seek forgets a gap that had not been covered yet: it belonged to the
    position you left behind.

RATE ADVICE: call SharedAudioOutput.Configure(48000) at start-up. Media
containers carry 48 kHz (it is Opus's only rate), the only rate conversion in
this package is linear interpolation, and when the device runs at the media's
rate no conversion runs at all. Without it the shared output starts at 48 kHz
for packet audio anyway - but an application that has already played a 44.1 kHz
sound effect will have started it at 44.1 kHz, and then every video plays
through the interpolator.



COMPLETE EXAMPLES
=================
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

    var format = new WaveFormat(rate: 44100, bits: 16, channels: 1);
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

React to the notes as they play, and mix a layer live (the game-music surface):

    using CodeBrix.Audio.Playback;
    using CodeBrix.Audio.Synth;

    var music = new MidiMusicPlayer();
    music.Load(soundFonts.Get("GeneralUser.sf2"), new MidiSequence("battle.mid"));

    // Observe-only: cannot break playback. Runs on the AUDIO THREAD, so do the
    // least possible here and let your own thread do the work.
    music.MidiMessageProcessed = (channel, command, note, velocity) =>
    {
        if (command == 0x90 && velocity > 0 && channel == 9)   // channel 10 = drums
            Volatile.Write(ref _drumHitPending, 1);            // your thread reads this
    };

    music.Play();

    music.SetChannelVolume(3, 0.0f);   // drop the lead layer out...
    music.SetChannelVolume(3, 1.0f);   // ...and bring it back
    music.Speed = 0.75f;               // slow-motion, same pitch

    // Transposing the whole sequence up an octave, with the OTHER hook. Note that
    // this one owns delivery: forgetting to call ProcessMidiMessage silences it.
    music.MidiMessageFilter = (synth, channel, command, data1, data2) =>
        synth.ProcessMidiMessage(
            channel, command,
            command is 0x90 or 0x80 ? data1 + 12 : data1,
            data2);

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


Overlap sound effects without keeping a clip around (fire and forget):

    using CodeBrix.Audio.Playback;

    SoundEffectClip.PlayOnce("beep.wav");          // loads, plays, cleans up
    SoundEffectClip.PlayOnce("beep.wav", 0.4f);    // quieter
    // Also PlayOnce(Stream, float) and PlayOnce(byte[], float). Convenient, but
    // it decodes every time - for a sound you trigger often, hold a
    // SoundEffectClip.Load(...) instead.


MINIMUM VIABLE PROJECT
======================
Console application that plays an audio file to the end and prints its
duration. Two files, no other dependencies.

AudioDemo.csproj:

    <Project Sdk="Microsoft.NET.Sdk">
      <PropertyGroup>
        <OutputType>Exe</OutputType>
        <TargetFramework>net10.0</TargetFramework>
        <Nullable>disable</Nullable>
      </PropertyGroup>
      <ItemGroup>
        <PackageReference Include="CodeBrix.Audio.MitLicenseForever" />
      </ItemGroup>
    </Project>

(Version attributes are omitted here on purpose - add the current version, or
use central package management.)

Program.cs:

    using System;
    using System.Threading;
    using CodeBrix.Audio.Playback;

    var finished = new ManualResetEventSlim(false);

    using var media = new AudioFilePlayer();
    media.PlaybackEnded += (s, e) => finished.Set();
    media.Load(args[0]);                  // .wav / .mp3 / .ogg / .flac

    Console.WriteLine($"Duration: {media.Duration}");
    media.Play();
    finished.Wait();

Run it with:  dotnet run -- song.mp3

The same two files, for MIDI music through a SoundFont, differ only in the
middle - swap AudioFilePlayer for MidiMusicPlayer and Load for the two-argument
overload:

    using CodeBrix.Audio.Playback;
    using CodeBrix.Audio.Synth;

    using var music = new MidiMusicPlayer();
    music.PlaybackEnded += (s, e) => finished.Set();
    music.Load("GeneralUser.sf2", "level1.mid");   // .sf2 or .sfz, then the .mid
    music.Play();

And for offline rendering there is no player and no device at all:

    using CodeBrix.Audio.Synth;

    SoundFontRenderer.RenderToWavFile(
        new SoundFont("GeneralUser.sf2"),
        new MidiSequence("level1.mid"),
        "level1.wav");


PERFORMANCE TIPS
================
  - Opening a file is cheap no matter how big it is: AudioFilePlayer streams
    through a chunked decoder instead of reading the file into memory, so Load
    reads the headers plus roughly five seconds of audio. A 50 MB WAV opens as
    fast as a 1 MB MP3 (milliseconds either way), and Duration is available as
    soon as Load returns. So do not pre-load a media library at start-up - load
    the one track you are about to play, when you are about to play it.

  - DECODE ONCE FOR ANYTHING YOU TRIGGER REPEATEDLY. SoundEffectClip.Load
    decodes the file to the output format ONE time and then plays it as often as
    you like, overlapping itself, at no further decode cost. Re-opening a reader
    per trigger is the single most expensive mistake available in this library.
    The trade is memory: a clip holds its decoded PCM, so it is right for
    effects and wrong for a soundtrack.

  - STREAM ANYTHING LONG. AudioFilePlayer streams from disk, so a two-hour
    podcast costs about what a ten-second one does in memory.

  - SHARE THE BIG ASSETS. A .sf2 SoundFont runs to tens of megabytes and an SFZ
    library decodes all of its samples eagerly at load. SoundFontCache and
    SfzInstrumentCache exist precisely so one copy serves every player - hold
    one cache for the application, and call .Get(path) rather than constructing
    SoundFont / SfzInstrument yourself.

  - PIN THE OUTPUT FORMAT ONCE, AT START-UP. SharedAudioOutput.Configure(
    sampleRate[, channels]) before the first sound avoids both the rejection
    described in COMMON PITFALLS and the cost of the output adopting whatever
    the first sound happened to be.

  - EVERY WaveOutEvent IS A VOICE, NOT A DEVICE. Overlapping dozens of them is
    mixing inside one already-open device, not dozens of device opens. Do not
    build a pool to avoid creating them.

  - KEEP THE AUDIO THREAD CLEAN. A source's Read, and both MidiMusicPlayer
    hooks, run on the real-time audio callback. No disk I/O, no locks that some
    other thread holds for long, no allocation you can avoid, no UI marshalling.
    Hand data to your own thread and act on it there.

  - THE NATIVE DECODER IS THE FAST PATH. The engine prefers its bundled native
    library and falls back to the managed decoders only where the native one
    cannot handle a format. You get that automatically; it is a reason to play
    through AudioFilePlayer / SoundEffectClip rather than pumping a managed
    WaveStream by hand when you have the choice.

  - FFT SIZES ARE POWERS OF TWO, and FastFourierTransform.FFT transforms in
    place over a Complex[] you own. Allocate the array once and reuse it rather
    than per frame; the same goes for the float buffers you pass to Read.

  - PARSE A MIDI FILE TWICE IF YOU NEED BOTH MODELS. MidiSequence to play,
    MidiFile to inspect tempo, time signature or markers. MIDI files are
    kilobytes; this costs nothing and is the intended pattern.

  - RENDERING OFFLINE BEATS RENDERING LIVE. SoundFontRenderer runs faster than
    real time with no device involved, so bouncing a sequence to a .wav once and
    playing the .wav is cheaper than synthesising it on every playthrough.


COMMON PITFALLS TO AVOID
========================
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
  - Which decoder plays your .ogg: the engine prefers the bundled native library,
    and all seven shipped native binaries carry an Ogg Vorbis decoder. Where a
    native binary lacks one, the managed decoder takes over automatically, so
    .ogg and .flac play everywhere either way; the managed path simply costs more
    CPU. Nothing to configure: SharedAudioOutput registers the managed fallbacks
    itself. Only if you construct your OWN engine do you need
    ManagedCodecs.RegisterAll(engine) to get the same safety net.
  - Threading: a single reader/stream instance is not thread-safe; give each
    thread its own reader.
  - UI threads and the Engine's synchronous APIs: a few Engine entry points are
    synchronous wrappers that do async I/O internally - SoundMetadataReader.Read,
    SoundMetadataWriter.WriteTags/RemoveTags, Recorder.StopRecording, and anything
    that opens a source through them (AudioFormat.GetFormatFromStream, the data
    providers, and therefore AudioFilePlayer.Load). They still do BLOCKING disk or
    network I/O, so on a UI thread prefer the *Async overloads where they exist, or
    do the work on a background thread.


WHAT THIS PACKAGE DOES NOT DO
=============================
  - Opus is NOT included. .opus files (and any Ogg stream carrying Opus rather
    than Vorbis) are recognised - metadata, duration, channels and rate all read
    correctly - but do not decode, and fail with a message saying so. Opus is
    BSD-3-Clause rather than MIT, so it ships as a separate add-on package
    instead of being folded into this one. See ADDING A CODEC below for how such
    a package plugs in.

  - DSP is primitives only: there is no turnkey onset/pitch/beat detector or
    audio-to-MIDI transcriber - build those on top of the FFT / filters /
    envelope follower.

  - No general-purpose resampler is exposed. Nothing in the public API converts
    a source from one sample rate to another on demand: the playback types do it
    internally, and the reader layer does not do it at all. If you need a
    resampled buffer in your own code, you supply the conversion.

  - No operating-system codec paths. There is no Windows ACM / DMO / Media
    Foundation, no macOS AVFoundation and no GStreamer route - decoding is the
    managed decoders plus the bundled native backend, and nothing else.

  - No lossy or lossless ENCODING of compressed formats. This package reads MP3,
    Ogg Vorbis and FLAC but writes only .wav (WaveFileWriter), .aiff
    (AiffFileWriter) and Standard MIDI Files (MidiFile.Export). For .opus
    writing, take the CodeBrix.Audio.Opus add-on package.

  - No editing of tags in place. Id3v2Tag reads a tag from a stream, and
    Id3v2Tag.Create builds one from key/value pairs, but there is no
    re-tag-this-file-on-disk operation, and Vorbis comments are read-only.

  - No streaming from a URL. Readers take a file name or a Stream; fetching
    over the network is your code's job, and the result is a Stream you hand in.

  - No audio effects, mixing or multi-track editing in the CodeBrix.Audio
    assembly. The bundled CodeBrix.Audio.Engine has effects, an editing/mixing
    layer and a Recorder; CodeBrix.Audio itself is files, formats, DSP
    primitives and playback facades.

  - No capture or recording surface in CodeBrix.Audio. Recording lives in the
    Engine (Recorder over an AudioCaptureDevice) - see the ENGINE section.

  - No visualisation widgets. There is an FFT and there are filters; drawing a
    spectrum or a waveform is your UI framework's job.


WORKING EXAMPLES ON GITHUB
==========================
The test suites are the executable documentation. Every feature above has a
file that exercises it.

  https://github.com/ellisnet/CodeBrix.Audio/tree/main/tests/CodeBrix.Audio.Tests

  READING AND WRITING FILES
    WaveFileReaderTests.cs, WaveStreams/WaveFileWriterTests.cs,
    WaveFormats/WaveFormatSerializeTests.cs, WavBitDepthTests.cs
        WAV round trips across bit depths and encodings.
    WaveStreams/WaveFileWriterRf64Tests.cs      files past the 4 GB RIFF limit.
    Mp3FileReaderTests.cs, Mp3/Mp3FrameTests.cs,
    Mp3/Mp3FileReaderBaseTests.cs               MP3 frame parsing and decode.
    OggVorbisFileReaderTests.cs                 Vorbis decode, duration, seeking.
    FlacFileReaderTests.cs, FlacTestStreams.cs  FLAC decode against the .wav each
                                                fixture was encoded from.
    AudioFileReaderTests.cs                     the by-extension convenience path.
    Mp3/Id3v2TagTests.cs, Id3v2TagTests.cs      ID3v2 tag reading.

  PLAYBACK
    WaveOutEventTests.cs        Init / Play / Pause / Stop / Volume, and the
                                sample-rate rejection described in the pitfalls.
    AudioFilePlayerTests.cs     transport, Position/Duration, Seek, looping.
    SoundEffectClipTests.cs     decode-once, overlapping voices, StopAll.
    SharedAudioOutputCollection.cs   why the sounding tests are serialised.

  CODECS AND EXTENSIBILITY
    CodecExtensibilityTests.cs  the two registration seams - ICodecFactory and
                                AudioFileReaderRegistry - exactly as ADDING A
                                CODEC FROM ANOTHER PACKAGE describes them,
                                including stream ownership.
    VorbisCodecFactoryTests.cs, FlacCodecFactoryTests.cs   the managed factories
                                and the Ogg format-id sharing rule.
    Codecs/ALawDecoderTests.cs, Codecs/MuLawDecoderTests.cs   the companding
                                codecs you call directly.

  MIDI
    Midi/MidiFileTests.cs, MidiFileTests.cs     read/write round trips.
    Midi/MidiEventCollectionTest.cs             tracks, PrepareForExport.
    Midi/NoteOnEventTests.cs, Midi/NoteEventTests.cs,
    Midi/ControlChangeEventTests.cs, Midi/PitchWheelChangeEventTests.cs,
    Midi/SysexEventTests.cs, Midi/TimeSignatureEventTests.cs,
    Midi/KeySignatureEventTests.cs, Midi/MidiEventCloneTests.cs
        the event hierarchy, one file per event type.

  SOUNDFONT, SFZ AND MIDI MUSIC
    Synth/MidiMusicPlayerTests.cs      the transport, Speed, the channel
                                       helpers, and BOTH message hooks.
    Synth/MidiSequenceTests.cs, Synth/MidiSequenceBridgeTests.cs
                                       MidiSequence, and FromEvents(...) as the
                                       bridge from the editable MIDI model.
    Synth/SoundFontRendererTests.cs    offline Render / RenderToWavFile.
    Synth/SoundFontCacheTests.cs       sharing one .sf2.
    Synth/Sfz/SfzInstrumentTests.cs, Synth/Sfz/SfzInstrumentCacheTests.cs,
    Synth/Sfz/SfzSynthesizerTests.cs, Synth/Sfz/SfzRenderingTests.cs,
    Synth/Sfz/SfzRegionTests.cs, Synth/Sfz/SfzModulatorTests.cs,
    Synth/Sfz/SfzCurveTests.cs, Synth/Sfz/SfzArticulationExtrasTests.cs,
    Synth/Sfz/SfzExtendedModelTests.cs, Synth/SfzParserTests.cs
        the SFZ engine end to end, and SfzParser for the structural layer.

  DSP
    Dsp/FastFourierTransformTests.cs, Dsp/FftProcessorTests.cs,
    Dsp/BiQuadFilterTests.cs, Dsp/BiQuadFilterValidationTests.cs,
    EnvelopeFollowerTests.cs, VoiceActivityDetectorTests.cs

  PROVIDERS AND STREAM PLUMBING
    WaveStreams/  - one file per provider: BufferedWaveProviderTests.cs,
    OffsetSampleProviderTests.cs, FadeInOutSampleProviderTests.cs,
    ConcatenatingSampleProviderTests.cs, MultiplexingSampleProviderTests.cs,
    MonoToStereoSampleProviderTests.cs, StereoToMonoSampleProviderTests.cs,
    SilenceProviderTests.cs, WaveChannel32Tests.cs, WaveOffsetStreamTests.cs,
    WaveStreamTests.cs and their neighbours.

The second suite exercises the bundled Engine's native decode path without
opening a device:

  https://github.com/ellisnet/CodeBrix.Audio/tree/main/tests/CodeBrix.Audio.Engine.Tests

    MiniAudioDecoderTests.cs, OggVorbisDecodeTests.cs, FlacDecodeTests.cs
        native decoding, including seeking.
    ChunkedDataProviderTests.cs, ProviderLengthFallbackTests.cs
        the length/duration arithmetic that a media transport depends on.
    OggOpusMetadataTests.cs
        that an Ogg Opus stream reports 48 kHz and a pre-skip-corrected
        duration even though this package cannot decode it.
    AudioFormatTests.cs, MiniAudioEngineTests.cs

Tests that open a real audio device and MAKE SOUND are opt-in, so an ordinary
run is silent and headless-safe:

    CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 dotnet test          # CodeBrix.Audio.Tests
    CODEBRIX_AUDIO_ENGINE_RUN_PLAYBACK_TESTS=1 dotnet test   # Engine tests

The audio fixtures those tests read are described at
https://github.com/ellisnet/CodeBrix.Audio/blob/main/tests/Assets/audio/AUDIO-FIXTURES.txt


QUICK REFERENCE CARD
====================
  PACKAGE    CodeBrix.Audio.MitLicenseForever   (MIT, .NET 10 or later)
  ASSEMBLIES CodeBrix.Audio  +  CodeBrix.Audio.Engine   (both from this package)

  I WANT TO...                            USE
  -----------                             ---
  read a file as float samples            new AudioFileReader(path)
  read one specific format                WaveFileReader / Mp3FileReader /
                                          OggVorbisFileReader / FlacFileReader /
                                          AiffFileReader
  write a .wav                            new WaveFileWriter(path, WaveFormat)
                                          WaveFileWriter.CreateWaveFile16(
                                              path, ISampleProvider)
  play a long track with a transport      new AudioFilePlayer()
  play a short sound, often, overlapping  SoundEffectClip.Load(path)
  play something once, no bookkeeping     SoundEffectClip.PlayOnce(path)
  push an IWaveProvider at the speakers   new WaveOutEvent()
  pin the output format                   SharedAudioOutput.Configure(48000)
  read/write a .mid                       new MidiFile(path, false)
                                          MidiFile.Export(path, collection)
  play a .mid through a .sf2 or .sfz      new MidiMusicPlayer()
  bounce a .mid to .wav, no device        SoundFontRenderer.RenderToWavFile(...)
  share a big instrument                  SoundFontCache / SfzInstrumentCache
  analyse audio                           FastFourierTransform / BiQuadFilter /
                                          EnvelopeFollower / VoiceActivityDetector
  add a codec from another package        SharedAudioOutput.RegisterCodecFactory
                                          AudioFileReaderRegistry.Register
  play codec packets from a container     new PacketAudioPlayer()
  add a packet codec from another package SharedAudioOutput
                                              .RegisterPacketCodecFactory
  ask if a packet codec is available      SharedAudioOutput
    (without opening the audio device)        .IsPacketCodecSupported("opus")
  list the packet codecs available        SharedAudioOutput
                                              .SupportedPacketCodecIds
  cut the encoder padding off a track     packets.SetTrailingTrim(TimeSpan)
                                          packets.SetTrailingTrimFrames(int)
  tell the player packets went missing    AudioPacket.Loss(TimeSpan)
                                          AudioPacket.Loss(int frames)

  SIGNATURES YOU WILL REACH FOR
    new AudioFileReader(string fileName)            // 32-bit float, any of the
                                                    // four built-in formats
    reader.ToSampleProvider()                       // WaveStream -> float
    new WaveFormat(int rate, int bits, int channels)
    WaveFormat.CreateIeeeFloatWaveFormat(int rate, int channels)
    new WaveFileWriter(string path, WaveFormat format)
    writer.WriteSamples(float[] samples, int offset, int count)
    SharedAudioOutput.Configure(int sampleRate, int channels = 2)
    SharedAudioOutput.RegisterCodecFactory(ICodecFactory factory)
    SharedAudioOutput.Shutdown()
    AudioFileReaderRegistry.Register(string extension,
                                     Func<Stream, WaveStream> readerFactory)
    AudioFileReaderRegistry.OpenFile(string fileName)   // returns a
                                                        // FileOwningWaveStream
    AudioFileReaderRegistry.Supports(string fileNameOrExtension)
    SoundEffectClip.Load(string fileName)   // also (byte[]) and (Stream)
    SoundEffectClip.PlayOnce(string fileName, float volume = 1.0f)
    clip.Play(float volume = 1.0f)
    player.Init(IWaveProvider waveProvider)            // WaveOutEvent
    media.Load(string filePath)                        // AudioFilePlayer
    media.Load(Stream stream, bool leaveOpen = false)
    media.Seek(TimeSpan position)
    music.Load(string instrumentPath, string midiFilePath)   // MidiMusicPlayer
    music.Load(SoundFont soundFont, MidiSequence sequence)
    music.Load(SfzInstrument instrument, MidiSequence sequence)
    music.SendMidiMessage(int channel, int command, int data1, int data2)
    music.SetChannelVolume(int channel, float volume)   // also Pan, Program
    SoundFontRenderer.Render(SoundFont, MidiSequence,
                             int sampleRate = 44100, TimeSpan tail = default)
    SoundFontRenderer.RenderToWavFile(SoundFont, MidiSequence, string outputPath,
                             int sampleRate = 44100, TimeSpan tail = default)
    SoundFontRenderer.RenderToWavStream(...)            // same, to a Stream
    cache.Get(string path)                  // SoundFontCache / SfzInstrumentCache
    MidiSequence.FromEvents(MidiEventCollection events,
                            MidiSequenceLoopType loopType = MidiSequenceLoopType.None)
    events.PrepareForExport()               // REQUIRED before MidiFile.Export
    Id3v2Tag.ReadTag(Stream input)
    ManagedCodecs.RegisterAll(AudioEngine engine)   // only for your OWN engine
    OggCodecSniffer.Identify(Stream stream)         // Vorbis / Opus / Flac
    SharedAudioOutput.RegisterPacketCodecFactory(IPacketCodecFactory factory)
    SharedAudioOutput.CreatePacketDecoder(string codecId,
                                          ReadOnlyMemory<byte> codecPrivate)
    packets.Open(string codecId, ReadOnlyMemory<byte> codecPrivate,
                 IAudioPacketSource source)            // PacketAudioPlayer
    packets.Seek(TimeSpan firstPacketTimestamp, TimeSpan preRoll = default)
    packets.Position                                   // the audio clock
    packets.SetTrailingTrim(TimeSpan trim)             // encoder padding at the
    packets.SetTrailingTrimFrames(int frames)          // END of the track
    packets.TrailingTrim                               // what is in effect
    SharedAudioOutput.IsPacketCodecSupported(string codecId)   // starts nothing
    SharedAudioOutput.SupportedPacketCodecIds                  // starts nothing
    new AudioPacket(ReadOnlyMemory<byte> data, TimeSpan? timestamp,
                    TimeSpan discardPadding)
    AudioPacket.Loss(TimeSpan duration, TimeSpan? timestamp = null)
    AudioPacket.Loss(int frames, TimeSpan? timestamp = null)
    decoder.ConcealLoss(int lostFrames, Span<float> output)  // IPacketSoundDecoder
    decoder.SupportsLossConcealment                          // default: false

  THE RULES YOU WILL OTHERWISE BREAK
    1. WaveStream readers give you BYTES. ToSampleProvider(), or AudioFileReader.
    2. Dispose readers and writers. An undisposed WaveFileWriter writes a corrupt
       file, and a stream from AudioFileReaderRegistry.OpenFile keeps the file
       locked.
    3. There is no resampler. WaveOutEvent REJECTS a source whose rate differs
       from the running output - Configure(...) at start-up, or use
       AudioFilePlayer / SoundEffectClip, which convert.
    4. PrepareForExport() before MidiFile.Export, every time.
    5. Two SoundFont paths, two MIDI hooks, two MIDI file models. Read the
       decision guides near the top before choosing one.
    6. The audio thread is real-time. Both MidiMusicPlayer hooks and every
       source Read run on it: no blocking, no I/O, no UI.
    7. .opus needs the CodeBrix.Audio.Opus add-on package, and one
       CodeBrixAudioOpus.Register() call.
    8. A packet source is PULLED on the audio thread and must never block; an
       empty return is an underrun (silence, playback continues), not the end.
       Only EndOfStream ends it.
    9. SharedAudioOutput.CreatePacketDecoder OPENS THE AUDIO DEVICE. To ask
       whether a codec is available without starting anything, use
       IsPacketCodecSupported.
   10. The container, not the codec, knows where a track really stops. Apply it
       with SetTrailingTrim (or AudioPacket.DiscardPadding) or the encoder's
       padding plays.
================================================================================
