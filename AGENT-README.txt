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
and sound effects) through the bundled engine, renders SoundFonts (.sf2), SFZ
instruments (.sfz) and Decent Sampler instruments (.dspreset, .dslibrary,
.dsbundle) and plays MIDI music through any of them, plays a multi-track song
whose parts can each come from a recording or from a MIDI performance (including
a Suno stems download), and exposes a set of DSP primitives for audio analysis. All file DECODING
and all SYNTHESIS is managed code with no
platform-specific interop, so it behaves the same way on Windows, macOS, and
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
                  A LICENSE-MiniAudio.txt sits beside each of the seven binaries
                  and travels with them into your application's output folder -
                  the licence notice for the native code, carried next to the
                  code it covers. Keep it there when you publish; it is small,
                  and THIRD-PARTY-NOTICES.txt in the package root remains the
                  authoritative record for everything else.
  System deps:    none. No system audio package and no system-wide codec is
                  required on Windows, macOS or Linux.

ADD-ON PACKAGES IN THE FAMILY

  CodeBrix.Audio.Opus.BsdLicenseForever
      Adds Ogg Opus (.opus) decoding and encoding. A separate package because
      Opus is BSD-3-Clause rather than MIT; fully managed, no native code. One
      call - CodeBrixAudioOpus.Register() - and .opus reaches every path a
      built-in format reaches. Its guide is at
      https://github.com/ellisnet/CodeBrix.Audio.Opus/blob/main/AGENT-README.txt

  CodeBrix.Audio.ModestSynth.MitLicenseForever
      Adds SYNTHESIS: oscillators that generate sound instead of playing
      recorded samples - band-limited saw, square and triangle, sine, white
      noise, a waveguide plucked string, a multi-frame wavetable player, a
      64-partial additive oscillator and a six-operator FM engine - plus the
      creative effects a synth is expected to have (phaser, pitch shifter, wave
      folder, wave shaper, stereo simulator, bit crusher, gate). Use them on
      their own, play a whole patch from MIDI through its ModestSynthesizer, or
      let them be the sound generators and effects a Decent Sampler preset asks
      for when a group holds an <oscillator> rather than a <sample> or a chain
      names an effect the core does not carry. One call -
      ModestSynth.Register() - before an instrument is loaded hands both sets
      over. Built and published from THIS repository at the same version as
      CodeBrix.Audio, so the two always match. Its guide is at
      https://github.com/ellisnet/CodeBrix.Audio/blob/main/src/CodeBrix.Audio.ModestSynth/AGENT-README.txt

  Any other codec package built on the seams described in ADDING A CODEC FROM
  ANOTHER PACKAGE below plugs in the same way.


KEY NAMESPACES / USINGS
=======================
  using CodeBrix.Audio.Wave;       // readers/writers, WaveFormat, MP3 frames, ID3,
                                   //   playback (WaveOutEvent, SharedAudioOutput)
  using CodeBrix.Audio.Playback;   // media player (AudioFilePlayer), one-shot
                                   //   sound effects (SoundEffectClip), the
                                   //   multi-track song player
                                   //   (MultiTrackPlayer) and the MIDI/audio
                                   //   alignment estimator
  using CodeBrix.Audio.Playback.Suno;  // loads a Suno stems download into a song
                                       //   the multi-track player plays
  using CodeBrix.Audio.Midi;       // MIDI file read/write + event hierarchy
  using CodeBrix.Audio.Dsp;        // FFT, biquad filters, analysis primitives
  using CodeBrix.Audio.Synth;      // SoundFont (.sf2) rendering + MIDI music
                                   //   playback — see "TWO SOUNDFONT PATHS"
  using CodeBrix.Audio.Synth.Sfz;  // SFZ instruments (.sfz)
  using CodeBrix.Audio.Synth.DecentSampler;   // Decent Sampler instruments
                                   //   (.dspreset / .dslibrary / .dsbundle) —
                                   //   see "PLAYING DECENT SAMPLER INSTRUMENTS"
  using CodeBrix.Audio.Synth.Mpe;  // MpeMode and the zone model an MPE
                                   //   performance in a MIDI file is read with

The Decent Sampler engine keeps five more namespaces, one per job, and a file
that reaches for one of their types has to import it - the type names all begin
"DecentSampler", so nothing collides:

  using CodeBrix.Audio.Synth.DecentSampler.Containers;  // DecentSamplerContainer,
                                   //   DecentSamplerLibraryInfo
  using CodeBrix.Audio.Synth.DecentSampler.Engine;      // the registration seam
                                   //   (DecentSamplerExtensions, IVoiceSource,
                                   //   IInstrumentEffect) and DecentSamplerDefaults
  using CodeBrix.Audio.Synth.DecentSampler.Streaming;   // DecentSamplerStreamingMode
  using CodeBrix.Audio.Synth.DecentSampler.Effects;     // DecentSamplerCoreEffects
  using CodeBrix.Audio.Synth.DecentSampler.Model;       // the typed document, for
                                   //   tooling that reads a preset

(Additional sub-namespaces exist for plumbing — sample/wave providers, codecs,
utilities. The managed MP3, Ogg Vorbis and FLAC decoders — CodeBrix.Audio.Mpeg,
CodeBrix.Audio.Vorbis and CodeBrix.Audio.Flac — are entirely internal; consumers
reach those formats only through the readers in CodeBrix.Audio.Wave. The
public types in CodeBrix.Audio.Codecs a consumer might touch are ManagedCodecs
(only when driving an engine they created themselves — see COMMON PITFALLS),
ManagedSoundDecoder and OggCodecSniffer (what an add-on codec package builds on
— see ADDING A CODEC FROM ANOTHER PACKAGE), the built-in factories
VorbisCodecFactory, FlacCodecFactory and VorbisPacketCodecFactory, the OggCodec
enum, and the A-law/mu-law encoders and decoders, which you call directly.)


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
  custom banks, MIDI modifiers,      MultiInstrumentBank, the Generators/ and
  arpeggiators                       Voices/ types

Why both exist: CodeBrix.Audio.Synth is a spec-faithful SF2 renderer — it
implements the SoundFont generator AND modulator model, per-voice LFOs, a
per-voice lowpass filter, volume and modulation envelopes, reverb and chorus.
That modulator model is central to how a SoundFont is meant to sound.
CodeBrix.Audio.Engine.Synthesis is a general-purpose synthesis architecture that
can sample-play SF2 presets; it has no modulators, no per-voice LFO and no
per-voice filter. It is the better tool for building instruments, and the wrong
tool for faithfully reproducing somebody's .sf2.

PLAYING A MIDI FILE THAT CARRIES AN EXPRESSIVE PERFORMANCE is the FIRST path's
job — see "MPE FROM MIDI FILES" below. The Engine's Synthesizer has an MpeEnabled
mode of its own, but it belongs to the instrument-building path and is driven by
that side's routing rather than by a MidiSequence.

Neither was retired in favour of the other. They do different jobs.

SFZ AND DECENT SAMPLER HAVE EXACTLY ONE PATH EACH, and it is the first one:
CodeBrix.Audio.Synth.Sfz (SfzInstrument, SfzSynthesizer) and
CodeBrix.Audio.Synth.DecentSampler (DecentSamplerInstrument,
DecentSamplerSynthesizer). The Engine has no support for either, so there is no
wrong turn to take. All THREE sampled formats implement the same IMidiSynthesizer
contract, which is why MidiSequencer, MidiMusicPlayer, MultiTrackPlayer and
SoundFontRenderer drive any of them without caring which - consumers choose a
file format, not an API.

  a .sf2 file       ->  SoundFont / SoundFontSynthesizer
  a .sfz file       ->  SfzInstrument / SfzSynthesizer
  a .dspreset,      ->  DecentSamplerInstrument / DecentSamplerSynthesizer
  .dslibrary or         (see "PLAYING DECENT SAMPLER INSTRUMENTS")
  .dsbundle

MidiMusicPlayer.Load(instrumentPath, midiFilePath) dispatches on the extension,
and a FOLDER holding a .dspreset works there too, so switching a project from one
sampled format to another is a change of file name.

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


FIVE TYPE NAMES COLLIDE ACROSS THE TWO BUNDLED ASSEMBLIES. This package ships
CodeBrix.Audio and CodeBrix.Audio.Engine, and each has its own type for several
of the same ideas. The names are identical; only the namespaces differ:

  NAME                    CodeBrix.Audio               CodeBrix.Audio.Engine
  ----                    --------------               ---------------------
  MidiFile                CodeBrix.Audio.Midi          CodeBrix.Audio.Engine
                                                       .Metadata.Midi
  MidiSequence            CodeBrix.Audio.Synth         CodeBrix.Audio.Engine
                                                       .Editing
  PlaybackState           CodeBrix.Audio.Wave          CodeBrix.Audio.Engine
                                                       .Enums
  VoiceActivityDetector   CodeBrix.Audio.Dsp           CodeBrix.Audio.Engine
                                                       .Components
  MetaEventType           CodeBrix.Audio.Midi          CodeBrix.Audio.Engine
                                                       .Metadata.Midi.Enums

They are not the same type and there is no conversion between them. A file that
imports both namespaces of a pair gets CS0104 ("... is an ambiguous reference
between ... and ...") on the bare name — which is a compile error, not a wrong
result, so it cannot bite silently. The remedy is a using ALIAS for the one you
need less often, and NOT a second namespace import — exactly what this library's
own source does when it has to hold both:

    using CodeBrix.Audio.Playback;
    using CodeBrix.Audio.Wave;      // PlaybackState, the one the players return
    using EnginePlaybackState = CodeBrix.Audio.Engine.Enums.PlaybackState;
    // and deliberately NOT: using CodeBrix.Audio.Engine.Enums;

    PlaybackState state = player.PlaybackState;        // CodeBrix.Audio.Wave
    EnginePlaybackState engineState = EnginePlaybackState.Playing;

Importing BOTH namespaces and adding the alias does not help: the alias gives the
other type a second name, it does not remove the first one from the bare name's
candidates. Import one, alias the other.

For ordinary playback you never need the Engine namespaces at all: AudioFilePlayer,
PacketAudioPlayer, SoundEffectClip, WaveOutEvent and SharedAudioOutput all speak
CodeBrix.Audio types. Import an Engine namespace only for something the Engine
alone offers (recording, effects, editing/mixing), and alias the collision.



MPE FROM MIDI FILES
===================
A performance played on an expressive controller — one where a finger can bend,
brighten and swell each note on its own — is recorded as MIDI Polyphonic
Expression: every note gets its own MIDI channel, and the bends, the slide (CC
74) and the press (channel pressure) on that channel belong to that note alone.
A Standard MIDI File carries all of it, and every sampled instrument format in
this package plays such a file the same way, so one recording sounds alike
through a SoundFont, an SFZ library and a Decent Sampler instrument.

TWO PROPERTIES TURN IT ON, on MidiMusicPlayer and on each synthesizer:

    music.MpeMode = MpeMode.Auto;         // CodeBrix.Audio.Synth.Mpe
    music.MpeMemberBendRange = 48;        // semitones; the controller default

  MpeMode.Off        Every channel is ordinary MIDI: two semitones of bend
                     unless RPN 0 says otherwise, and no zones. THE DEFAULT, and
                     the way to insist a file plays as plain MIDI.
  MpeMode.Auto       Read the zones from the file's own configuration message;
                     failing that, infer a lower zone from notes spread over
                     channels 2 to 16 with per-channel bends and nothing on
                     channel 1. START HERE.
  MpeMode.LowerZone  Channel 1 is the master, 2 upward its members.
  MpeMode.UpperZone  Channel 16 is the master, 15 downward its members.
  MpeMode.Both       Both zones, splitting the fourteen middle channels.

  .MpeLowerZoneMemberCount / .MpeUpperZoneMemberCount pin how many member
  channels an explicit mode gets (0, the default, means fifteen for one zone and
  seven each for two). .MpeLowerZone / .MpeUpperZone read back what a
  configuration message, or the automatic detector, actually decided:
  MpeZoneInfo carries IsActive, MasterChannel, FirstMemberChannel, MemberCount,
  MasterBendRange and MemberBendRange, with channel numbers 1-based as every
  controller manual writes them.

  A file exported from a sequencer NORMALLY CARRIES NO CONFIGURATION MESSAGE —
  the notes are simply spread across channels 2 upward. That is what
  MpeMode.Auto is for, and it is the case to expect. If the detector reads a
  particular file differently from how it was played, pin it with
  MpeMode.LowerZone and a member count.

WHAT A FILE CAN SAY, AND WHAT IS DONE WITH IT:

  Zones              RPN 6 on channel 1 or 16 (the MPE Configuration Message),
                     honoured live, mid-file, in every mode but Off.
  Bend               Per channel, fourteen-bit. A member's bend and its zone
                     master's ADD, each over its own range, which is how a
                     global glide rides on top of a per-note one.
  Bend range         RPN 0, per channel, semitones and cents. Sent on a member
                     it configures every member of that zone. Without it,
                     members of an active zone use MpeMemberBendRange and
                     everything else uses MIDI's own two semitones.
  Tuning             RPN 1 (fine) and RPN 2 (coarse), added to the note's pitch.
                     RPN null and the data increment/decrement controllers (96
                     and 97) behave as the MIDI specification says.
  Slide              CC 74 per channel, read as that note's timbre.
  Press              Channel pressure per channel, and polyphonic key pressure
                     honoured as per-note pressure when a file carries it.
  Master gestures    A zone master's controllers reach every note in the zone: a
                     pedal or other switch applies zone-wide, and a continuous
                     controller applies to any member that has not sent its own.
  Lift               Note-off velocity, captured per note and readable through
                     GetReleaseVelocity(channel, key) — 0-based channels, as
                     MIDI messages carry them, and -1 when nothing is loaded.
                     MidiMessageProcessed sees it live, as a note-off's second
                     data byte.

  Overlapping notes on one member channel follow the MPE rule that the NEWEST
  note owns the channel: an older note freezes its bend and its expression where
  they were when it lost the channel, and gets them back if it outlives the
  newer one.

WHAT EACH FORMAT DOES WITH THE EXPRESSION, since a format can only use what it
declares. Bend, tuning, zones, master combination and lift are identical in all
three; the difference is what a preset can wire the slide and the press TO:

  SFZ                Everything. CC 74 and the aftertouch sources feed the
                     region's own _onccN modulation matrix and its locc/hicc
                     ranges, so a library that already responds to a
                     controller's slide and press responds to a performance's.
                     A region's bend_up/bend_down still says how far it bends;
                     an MPE range SCALES that against MIDI's two semitones, so a
                     region asking for an octave still bends six times as far as
                     a plain one.
  SoundFont          Press deepens a note's vibrato, which is the destination
                     the SoundFont modulator model itself names for channel
                     pressure. The format names no destination for CC 74, so the
                     slide is stored and readable (MpeTimbre) but changes no
                     sound. Volume, expression, pan, modulation and the pedal
                     follow the zone rules.
  Decent Sampler     The mpeTimbre and mpePressure modulators read the note's
                     own slide and press, and a midiCC modulator written with
                     channel="voice" reads any controller the same way.

  No sampled format defines what release velocity should DO, so a lift changes
  no sound anywhere; it is captured for the host to use.

A synthesizer of your own joins in by implementing
CodeBrix.Audio.Synth.Mpe.IMpeSynthesizer — MidiMusicPlayer then forwards the
same settings to it and reads its zones and lift velocities.

NO MIDI DEVICE INPUT. This is about MIDI FILES. Record the performance in a
sequencer, export the clip, and play the file.


CORE API REFERENCE
==================
Reading audio (WAV, MP3, Ogg Vorbis, FLAC):
  - WaveFileReader        : reads a .wav stream/file as a WaveStream.
  - Mp3FileReader         : reads a .mp3 stream/file as a WaveStream, decoding
                            MPEG audio to PCM via the managed NLayer decoder.
                            GAPLESS BY DEFAULT: where the file carries the
                            Xing/LAME/Lavc encoder-delay fields, the priming
                            samples at the front and the padding at the end are
                            trimmed, so sample position 0 is the first sample of
                            the original audio and Length excludes the padding -
                            a decoded MP3 then lines up with the WAV it was
                            encoded from, sample for sample. .EncoderDelay and
                            .EncoderPadding report what the file declared, and
                            .GaplessTrimming turns it off (before the first read;
                            afterwards it throws). A file with no such fields is
                            unaffected.
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
  - PacketAudioPlayer     : (namespace CodeBrix.Audio.Playback) plays audio that arrives
                            as bare codec packets out of a media container rather than as
                            a file - Open, Play/Pause/Stop, Seek, Volume, Position, and
                            end-of-track trimming. Mixes into the same SharedAudioOutput.
                            See "PLAYING AUDIO THAT ARRIVES AS PACKETS" below.
  - SharedAudioOutput     : the one shared output WaveOutEvent, AudioFilePlayer and
                            PacketAudioPlayer mix into. Optional: Configure(sampleRate
                            [, channels]) once at start to pin the format; Shutdown() to
                            release it.

WaveFormat:
  - WaveFormat            : sample rate, channel count, bit depth, encoding.

Metadata:
  - Id3v2Tag              : reads an ID3v2 tag block from an MP3 stream.
  - Vorbis comments       : exposed as .Tags on OggVorbisFileReader and
                            FlacFileReader (uppercase field name -> values).

MIDI:
  - MidiFile              : reads a Standard MIDI File; MidiFile.Export(...)
                            writes one. TOLERANT BY DEFAULT - see below.
  - MidiEvent (hierarchy) : NoteOnEvent, NoteEvent, TextEvent, MetaEvent,
                            TempoEvent, TimeSignatureEvent, etc.
  - MidiEventCollection   : per-track event collection used for read and write.
  - MidiReadMode          : Tolerant (the default everywhere) or Strict. The one
                            option both MIDI readers share.

READING A MIDI FILE THAT BREAKS THE RULES
  Plenty of real MIDI files depart from the specification, and a machine-written
  one almost always does: a key signature outside the legal range, a note-on with
  no note-off, a meta event whose payload does not match its declared length, a
  chunk that is not MTrk. Both readers - MidiFile and MidiSequence - now read
  such a file rather than refusing it, and say what was odd about it:

    var file = new MidiFile("machine-written.mid");     // does not throw
    foreach (var problem in file.Problems) { ... }      // one line each

  The Problems contract, shared with the SFZ engine and with SunoSong:
    - one human-readable line per departure, in the order they were found;
    - empty means the file followed the specification;
    - NEVER thrown, and capped at 200 entries so a corrupt file cannot become a
      memory problem;
    - floods are aggregated per track ("Track 1: 12 note on event(s) had no note
      off"), not one line per note;
    - a meta event this package does not MODEL is not a problem: its bytes are
      kept verbatim and written back unchanged, so nothing was lost.

  The two readers legitimately report DIFFERENT problems for one file, because
  they model different things: MidiFile models key signatures and reports an
  out-of-range one, MidiSequence does not model them at all and has nothing to
  say. Do not compare the two lists.

  Tolerance CHANGES what you get back where that is the only way to be useful: a
  dangling note-on has a note-off synthesised at the end of its track and
  inserted into the collection, so the file exports again as a valid file and
  NoteLength works. Anything counting events must expect that.

  STRICT MODE IS STILL THERE, for a tool that wants to VALIDATE rather than play:
    new MidiFile(path, MidiReadMode.Strict)        // throws on the first fault
    new MidiSequence(path, MidiReadMode.Strict)
  The older `new MidiFile(path, strictChecking: true)` means exactly the same.

  MidiSequence also exposes .TextMetas - the textual meta events the file
  carried, bytes preserved - so a caller can identify a file without reading it
  twice, and .TempoMap, the tempo map the loader applied while flattening the
  file to absolute time (loading a sequence CONSUMES the tempo events, so this is
  the only place the map survives).

SoundFont rendering and MIDI music (CodeBrix.Audio.Synth) — read "TWO SOUNDFONT
PATHS" above first:
  - SoundFont             : a parsed .sf2. Public object model: SoundFontInfo,
                            Preset, PresetRegion, SoundFontInstrument,
                            InstrumentRegion, SampleHeader, LoopMode. Enumerate a
                            SoundFont's presets and key ranges without rendering.
  - SoundFontCache        : loads a .sf2 once and shares it. SoundFonts are tens
                            of megabytes; never reload one per track.
  - SoundFontSynthesizer  : the renderer of record. NOT thread-safe by design -
                            rendering and note events must not overlap. Reads
                            the MPE content of a performance: MpeMode,
                            MpeMemberBendRange, the member counts, MpeLowerZone
                            / MpeUpperZone, ReleaseVelocity, MpeTimbre and
                            MpePressure - see "MPE FROM MIDI FILES".
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
                              .MpeMode               how an expressive
                                                     performance is read; Off
                                                     by default.
                              .MpeMemberBendRange, .MpeLowerZoneMemberCount,
                              .MpeUpperZoneMemberCount, .MpeLowerZone,
                              .MpeUpperZone, GetReleaseVelocity()
                                                     the rest of the MPE
                                                     surface, applied to
                                                     whichever instrument format
                                                     is loaded.
                            See "THE TWO MIDI MESSAGE HOOKS" above before using
                            either hook - they are not interchangeable.

Multi-track songs and stems (CodeBrix.Audio.Playback) — see "PLAYING A
MULTI-TRACK SONG" and "PLAYING SUNO STEMS" below:
  - MultiTrackPlayer      : several tracks on one transport, sample-accurate.
                            Add/Remove, Prepare/Play/Pause/Stop/Seek, Position,
                            Duration, Volume, IsLooping, PlaybackEnded,
                            AutoSetRelativeTrackLevels, TempoSource, offline
                            Render / RenderToWav, and ExportMergedMidi.
                            MultiTrackPlayer.Load(SunoSong) builds one from a
                            stems download.
  - PlayerTrack           : the base of both track types. Name, Gain,
                            MidiSourceGain, Mute, Solo, Pan, Offset,
                            MidiSourceOffset, ActiveSource, MinimumNoteHold,
                            IgnoreNoteOff, GmProgram, IsPercussion, Duration.
  - AudioTrack / MidiTrack: a track born with a recording, or with a performance
                            and the factory that builds its instrument. Either
                            can be given the other source as well.
  - TrackSource           : Audio or Midi - which of a track's two sources is
                            heard.
  - MidiAudioAlignment    : estimates how far a MIDI transcription sits from the
                            audio it was transcribed from, by cross-correlating
                            note-on times against an onset envelope. Estimate
                            (mono or interleaved, list or MidiSequence) returns a
                            MidiAudioAlignmentResult carrying OffsetSeconds
                            (audioTime = midiTime + offset), Confidence,
                            IsReliable and CandidatePeakCount. Call it on a
                            worker: it is far too slow for a render callback.

Suno stems downloads (CodeBrix.Audio.Playback.Suno):
  - SunoStemsLoader       : Load / LoadAsync a "<Title> Stems.zip" or the same
                            files extracted to a folder; DefaultCacheRoot,
                            ClearCache.
  - SunoSong / SunoStem   : the model - title, stems, tempo map, duration, full
                            mix, Problems; per stem the recording, the MIDI, the
                            General MIDI program and channel, the note count and
                            coverage, and the measured alignment.
  - SunoLoadOptions       : what to do while loading (extraction, alignment,
                            note hold, tolerances, the General MIDI SoundFont).
  - SunoPlayerOptions     : what to do when building a player from the song
                            (instruments, alignment, percussion, level matching).
  - SunoStemDefaults      : the twelve known stem names and their General MIDI
                            defaults. The vocabulary is open.

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
                            aftertouch, 131 velocity, 132 note-off velocity,
                            133 note, 134 key gate, 135/136 per-voice randoms,
                            137 alternate, 140/141 key delta). Reads the MPE
                            content of a performance on the same surface as
                            SoundFontSynthesizer - see "MPE FROM MIDI FILES".
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

DECENT SAMPLER (CodeBrix.Audio.Synth.DecentSampler) — the .dspreset counterparts,
and a live parameter model the other two formats have no equivalent of. The
section "PLAYING DECENT SAMPLER INSTRUMENTS" is the guide; this is the index.
The namespace is named after each type below that does NOT live in
CodeBrix.Audio.Synth.DecentSampler itself:
  - DecentSamplerInstrument   : a playable instrument from a .dspreset, a
                            .dslibrary, a .dsbundle or a folder. Loading is
                            tolerant: only malformed XML throws. Groups, Zones,
                            Effects, Buses, MidiHandlers, Modulators, Sequences,
                            Arpeggiator, Tags and Ui are the parsed model;
                            Controls and TagStates are the LIVE one; Problems and
                            UnsupportedFeatures say what could not be honoured;
                            MemoryPolicySummary, DecodedByteCount and
                            StreamedSampleCount say what it costs; Container
                            resolves a file the preset names beside itself.
                            IDisposable, and it owns its decoded audio.
  - DecentSamplerInstrumentCache : loads an instrument once and shares it, and
                            shares SAMPLE DATA between the instruments it holds.
  - DecentSamplerLoadOptions : which preset, whether to decode now or on first
                            use, and the streaming threshold, memory budget and
                            preload head the memory policy decides with.
  - DecentSamplerSynthesizer : the renderer, on the same IMidiSynthesizer
                            contract as the other two and equally NOT
                            thread-safe. Also IMultiOutputRenderer, for a preset
                            that uses auxiliary stereo pairs. Carries the MPE
                            settings, the streaming mode, and the tempo source
                            behind musical-time delays, sequences and the
                            arpeggiator.
  - DecentSamplerSynthesizerSettings : sample rate, block size, polyphony, seed,
                            master volume, EnableModulators, StreamingMode and
                            its buffers, the MPE settings, and the extension
                            registry to resolve oscillators and effects against.
  - DecentSamplerControl    : one live element of the preset's interface.
                            SetValue / Select fire its bindings exactly as a user
                            turning it would; Changed reports what moved.
  - DecentSamplerTagState   : a tag's live Enabled, Volume, Pan and Polyphony.
  - DecentSamplerStreamingMode : (.Streaming) RealTime or Offline - which one an
                            offline render must use. See MEMORY, STREAMING AND
                            LAZY LOADING below.
  - DecentSamplerContainer  : (.Containers) Open(path).FindPresets() lists what a
                            library holds; TryResolve and OpenFile read a file
                            out of it.
  - DecentSamplerLibraryInfo : (.Containers) a library's DSLibraryInfo.xml - name,
                            version, cover art, preset menu, and the store
                            productId that is reported and never acted on.
  - DecentSamplerExtensions : (.Engine) the registration seam.
                            RegisterOscillator(waveform, factory) and
                            RegisterEffect(type, factory) against IVoiceSource and
                            IInstrumentEffect, which live there too; the
                            ModestSynth add-on fills it with one call.
  - DecentSamplerCoreEffects : (.Effects) the thirteen effect types this package
                            implements.
  - DecentSamplerSupportedFeatures : every name the format documents, each marked
                            Parsed or Implemented, with StatusOf answering for a
                            given set of registered extensions.
  - DecentSamplerResidualTable : everything not implemented, grouped with a
                            reason. Derived from the feature list, so it cannot
                            drift from the code.
  - MidiMusicPlayer and SoundFontRenderer take a DecentSamplerInstrument wherever
    they take a SoundFont; MidiMusicPlayer.Load(path, midi) picks the synthesizer
    by extension, and a folder holding a .dspreset works there too.

  The structural layer underneath, for tooling rather than playback:
  - DecentSamplerParser     : ParseFile / Parse / ParseText read a preset into a
                            DecentSamplerPreset without opening one audio file.
  - DecentSamplerPreset and the (.Model) types:
                            the whole document typed, with every unknown
                            attribute and element KEPT rather than dropped.
                            preset.ResolveGroups() applies groups -> group ->
                            sample inheritance and hands back the effective
                            values, touching no file.

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
     mirrors ICodecFactory exactly - FactoryId, Priority, SupportedCodecIds,
     which name the CODEC ("vorbis", "opus") rather than the container, and the
     one method that does the work:

         string FactoryId { get; }
         IReadOnlyCollection<string> SupportedCodecIds { get; }
         int Priority { get; }
         IPacketSoundDecoder CreateDecoder(string codecId,
                                           ReadOnlyMemory<byte> codecPrivate,
                                           AudioFormat? hint)

     CreateDecoder returns NULL for anything this factory does not want, which
     lets the next factory have its turn; throwing does not. `hint` is the format
     the caller would like back and may be ignored.

         SharedAudioOutput.RegisterPacketCodecFactory(new SomePacketCodecFactory());

     Same rules as the stream seam: call it once at start-up, the registration
     lasts for the process, and registering the same instance twice is ignored -
     so keep one factory instance per add-on package. To see what has been added
     that way:

         IReadOnlyList<IPacketCodecFactory> added =
             SharedAudioOutput.RegisteredPacketCodecFactories;

     which lists what you registered, in registration order, and deliberately
     NOT the built-ins, which are always present.

     Vorbis packets are built in and always registered - the public
     VorbisPacketCodecFactory (namespace CodeBrix.Audio.Codecs), FactoryId
     "CodeBrix.Audio.ManagedVorbis.Packets", serving codec id "vorbis" at
     Priority 0. You do not register it; it is named here so a factory of your
     own can be given a priority relative to it. Opus packets come with the
     CodeBrix.Audio.Opus add-on package.

     A CONSUMER DRIVING ITS OWN AudioEngine uses the engine's own surface
     instead, and gets more of it than the shared output exposes:

         engine.RegisterPacketCodecFactory(IPacketCodecFactory factory)
         bool engine.UnregisterPacketCodecFactory(string factoryId)
         bool engine.SetPacketCodecPriority(string factoryId, int newPriority)
         IReadOnlyList<IPacketCodecFactory>
             engine.GetRegisteredPacketCodecs(string codecId)
         IPacketSoundDecoder engine.CreatePacketDecoder(string codecId,
             ReadOnlyMemory<byte> codecPrivate, AudioFormat? hint = null)

     Unregister and SetPacketCodecPriority both match on FactoryId and both
     return false when nothing carried that id; SetPacketCodecPriority overrides
     the factory's own Priority for that engine only. GetRegisteredPacketCodecs
     answers for ONE codec id, highest priority first, and returns an empty list
     rather than null. Call ManagedCodecs.RegisterAll(engine) on an engine of
     your own to get the built-in Vorbis packet factory on it as well.
     Registering the same factory twice on an engine ADDS IT TWICE - the
     de-duplication above belongs to SharedAudioOutput, not to the engine.

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

THE WHOLE SURFACE, since it is small (all of it is thread-safe; Position and the
events aside, everything takes the player's own lock):

    void Open(string codecId, ReadOnlyMemory<byte> codecPrivate,
              IAudioPacketSource source)
        Resolves a decoder for codecId through the shared output and opens the
        feed. Replaces anything already open. STARTS THE SHARED OUTPUT (it has
        to build a decoder), so it opens the audio device; the voice is added to
        the mixer stopped, and Play() starts it.
    void Open(IPacketSoundDecoder decoder, IAudioPacketSource source,
              bool leaveOpen = false)
        The same, with a decoder you already have - one you built yourself, or
        one from SharedAudioOutput.CreatePacketDecoder. leaveOpen: false (the
        default) means this player disposes the decoder; true keeps it yours.
    void Play()      Starts or resumes. Throws InvalidOperationException if
                     nothing is open.
    void Pause()     Pauses, keeping the position. The source is not asked for
                     packets while paused. Harmless when nothing is open.
    void Stop()      Stops and consumes no more packets; the position is left
                     where it is, so Play() resumes there. Seek first to start
                     somewhere else. Harmless when nothing is open.
    void Seek(TimeSpan firstPacketTimestamp, TimeSpan preRoll = default)
                     See SEEKING IS A CONTRACT below - move your source first.
    void Dispose()   Stops, removes the voice from the mixer, and releases the
                     decoder unless leaveOpen kept it.

    bool IsOpen                  A feed is open and ready to play.
    PlaybackState PlaybackState  Stopped / Playing / Paused. This is
                                 CodeBrix.Audio.Wave.PlaybackState - see "FIVE
                                 TYPE NAMES COLLIDE" near the top.
    TimeSpan Position            The clock (below). Readable from any thread.
    float Volume                 1.0 is unity gain; persists across opens.
    int SampleRate               The open audio's rate in Hz; 0 when nothing is
                                 open. The CODEC's rate, not the device's.
    int Channels                 The open audio's channel count; 0 when nothing
                                 is open.
    TimeSpan TrailingTrim        What end-of-track trim is in effect (below).
    void SetTrailingTrim(TimeSpan) / void SetTrailingTrimFrames(int)
                                 Set it (below).
    event EventHandler PlaybackEnded
                                 Raised once the source has reported EndOfStream
                                 and the last decoded audio has been played -
                                 never on the audio thread, so a handler may take
                                 locks or dispose the player.

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



PLAYING A MULTI-TRACK SONG
==========================
MultiTrackPlayer (namespace CodeBrix.Audio.Playback) plays several tracks as one
song on one transport. A track is a recording, a MIDI performance, or BOTH - and
when it holds both, which one is heard is a property you can change while the
music runs. That is what a stems set is: one track per part, each with the
recording of that part and the transcription of it side by side.

  var player = new MultiTrackPlayer();
  player.Add(new AudioTrack("drums.wav", "Drums"));
  player.Add(new MidiTrack(sequence,
      rate => new SoundFontSynthesizer(generalMidi, rate), "Bass"));
  player.Prepare();
  player.Play();

THE TRACK MODEL
  AudioTrack(path or Stream)  a recording: WAV, MP3, Ogg Vorbis or FLAC,
                              identified from the file's CONTENT rather than its
                              extension, plus anything registered with
                              AudioFileReaderRegistry.
  MidiTrack(sequence, factory) a performance plus the instrument that renders it.
                              The factory is called with the SAMPLE RATE the
                              synthesizer must render at, and may be called more
                              than once and from a worker thread.
  track.SetAudioSource(...)   gives a MIDI track a recording as well.
  track.SetMidiSource(...)    gives an audio track a performance as well.
  track.ActiveSource          TrackSource.Audio or TrackSource.Midi. Setting it
                              while playing is a 20 ms crossfade, not a restart:
                              both sources are rendered every block, so the
                              silent one is always at the right position.

PER-TRACK CONTROLS
  Gain            linear, 1.0 unity. Not decibels.
  MidiSourceGain  an extra linear gain applied only while the MIDI source is the
                  one being heard, so a synthesized part can be matched to the
                  recording without disturbing Gain.
  Mute / Solo     while ANY track is soloed only soloed tracks sound; Mute wins.
  Pan             -1 left .. 0 centre .. +1 right. A BALANCE law: it attenuates
                  the far channel and leaves the near one alone, so a centred
                  stereo track passes through untouched.
  Offset          signed; positive DELAYS the whole track. Read at Prepare, Play,
                  Stop and Seek, not per block.
  MidiSourceOffset signed; positive delays the MIDI rendition RELATIVE TO the
                  track's own recording. This is the alignment lever - see
                  PLAYING SUNO STEMS - and it is separate from Offset because a
                  set of recordings is already in step with itself and must not
                  be moved.
  MinimumNoteHold the percussion rule, 60 ms by default: a note-off that arrives
                  sooner than this is DEFERRED until the note has sounded that
                  long. A note written longer is untouched.
  IgnoreNoteOff   discards note-offs entirely. Right for one-shot percussion
                  whose samples already have the correct length; wrong for a
                  sustaining instrument, which will then never stop a note.
  GmProgram / IsPercussion   used by the merged MIDI export, not by playback.

THE PERCUSSION RULE, AND WHY IT EXISTS
  Drum parts are routinely written with ZERO-LENGTH notes: the note-off arrives
  a tick or two after the note-on, because a hit has no duration. Played
  literally, every drum is a click cut off in its attack. MinimumNoteHold is on
  by default at 60 ms and fixes it for any instrument; IgnoreNoteOff is the other
  answer, for kits whose samples are already one-shots.

REACHING ONE TRACK
  player["Drums"]        the track of that name. Case-insensitive, surrounding
                         space ignored - the same matching SunoSong uses for
                         song["Drums"]. Two tracks of one name resolve to the
                         FIRST one added.
  player.FindTrack(name) the same, returning null when there is no such track.
  player.TryGetTrack(name, out var track)   the same again, as a bool.
  player.Tracks          every track, in the order they were added.

  The indexer THROWS KeyNotFoundException when the name is not in the song, and
  the message lists the names that are - a typed stem name is a mistake in your
  code, not a state to handle. Use FindTrack or TryGetTrack when the name might
  legitimately be absent. (SunoSong's indexer returns null instead: a stems
  export genuinely may not contain the part you asked for.)

    player["Bass"].ActiveSource = TrackSource.Midi;
    if (player.TryGetTrack("Backing Vocals", out var backing))
    {
        backing.Gain = 0.6f;
    }

THE TRANSPORT
  Prepare / Play / Pause / Stop / Seek / Position / Duration / IsLooping /
  Volume / PlaybackEnded, and every track renders in lockstep inside one
  component on the shared output, so tracks cannot drift and a seek moves all of
  them at once.
  Duration is the last moment any track reaches, offsets included, and a track
  contributes the LONGER of its two sources - so switching a source never
  changes the song's length. Tail (400 ms by default) is how much longer the mix
  keeps rendering past Duration so release tails ring out; set it to
  TimeSpan.Zero when you want exact arithmetic.
  TempoSource carries live musical time, fed from the first MIDI track's tempo
  map. A song with no MIDI in it reports 120 BPM.

MATCHING THE LEVELS
  AutoSetRelativeTrackLevels (false by default) measures, on a worker, the RMS of
  each track's recording against an offline render of that same track's MIDI, and
  writes the ratio into MidiSourceGain. The balance between the parts then follows
  the original recording whichever source each track is playing. Gain is never
  touched, and while the option is off nothing writes a gain you did not set.
  It costs a full decode and a full synthesis pass per track - seconds, not
  milliseconds - so with the option on, Prepare starts it on a worker and does
  not wait for it. LevelMeasurement is the task it runs on, and awaiting that is
  how you know the gains are in:

    player.AutoSetRelativeTrackLevels = true;
    player.Prepare();
    await player.LevelMeasurement;              // the gains are in when this
    player.Play();                              //   returns

  LevelMeasurement is NEVER null. Until a measurement starts it is an
  already-completed task, so that await is safe on any player and simply returns
  at once when there is nothing to wait for - no null check, no guard.

  Two other ways to run it, with the option left off:

    await player.MeasureRelativeTrackLevelsAsync();   // on a worker; the task it
                                                      //   returns IS
                                                      //   LevelMeasurement
    player.MeasureRelativeTrackLevels();              // BLOCKS this thread until
                                                      //   done; LevelMeasurement
                                                      //   is completed afterwards

  Call either again after changing an instrument, when the gains they wrote no
  longer describe what a track renders.

OFFLINE, WITH NO AUDIO DEVICE
  Render(sampleRate)              the whole mix as interleaved stereo float.
  RenderToWav(path or Stream)     the same, written out.
  Both build their own decoders and synthesizers at the rate you ask for, render,
  and release them, so rendering while the same song plays is legitimate. Volume
  and every per-track control apply; nothing is limited or normalised, so a mix
  that adds up past 1.0 comes back past 1.0. A four-minute song at 44.1 kHz is
  about 84 MB of float in one array.

ONE MERGED GENERAL MIDI FILE
  ExportMergedMidi(path or Stream) writes every MIDI track into one type 1 SMF at
  480 ticks per quarter note: the shared tempo map, one track per part on its own
  channel with a track-name meta and a program change, percussion on channel 10,
  and each track's Offset and MidiSourceOffset already applied. The percussion
  rule is applied to the exported notes as well, because a file full of
  zero-length notes is silent in every sequencer that opens it. It returns one
  human-readable line per thing that could not be honoured and throws nothing.


PLAYING SUNO STEMS
==================
A Suno stems download is a "<Title> Stems.zip" holding one WAV (and optionally
one MP3, and for some parts one .mid) per instrument. SunoStemsLoader reads that
zip in place - or the same files extracted to a folder - and hands you a model
that MultiTrackPlayer plays. "Suno" is Suno, Inc.'s name and appears here only to
say what the files are.

Nothing here is needed to play a whole Suno song: that is an ordinary MP3 or WAV
download and AudioFilePlayer plays it. The next heading shows exactly how.

PLAYING A WHOLE SUNO SONG (WAV OR MP3)
  A whole song is the ordinary "<Title>.wav" or "<Title>.mp3" download, and it
  is a long recording like any other. Play it with AudioFilePlayer, the media
  player with a transport. Do not use SoundEffectClip for it: that type decodes
  a clip whole into memory for short, overlapping one-shots.

    using CodeBrix.Audio.Playback;

    using var player = new AudioFilePlayer();
    player.PlaybackEnded += (sender, e) => Console.WriteLine("finished");
    player.Load("/music/My Song.wav");        // or "/music/My Song.mp3"
    player.Volume = 0.8f;                     // 1.0 is unity gain
    player.Play();

    // The transport, whenever you want it:
    player.Pause();                           // Play() resumes from here
    player.Seek(TimeSpan.FromSeconds(60));    // jump to 1:00
    Console.WriteLine($"{player.Position} of {player.Duration}");
    player.IsLooping = true;                  // wrap at the end instead of ending
    player.Stop();                            // back to the start, stopped

  What to expect:
    - Load identifies the format from the file's content, not its extension, so
      a WAV, an MP3, an Ogg Vorbis or a FLAC download all work the same way.
      Duration is known as soon as Load returns.
    - The file's sample rate does not matter: the player resamples to the output
      device, so a 48 kHz download plays on a 44.1 kHz device.
    - The file is decoded in chunks as it plays. A four-minute WAV is not held
      in memory.
    - An MP3's encoder delay and padding are trimmed, so Position zero is the
      first real sample and Duration excludes the padding. For a whole song
      either format is fine; the WAV advice below matters for STEMS, which have
      to line up with each other.
    - PlaybackEnded is raised on the SynchronizationContext that was current when
      the file was loaded (the UI thread, in an application that loaded it from
      the UI) and on the engine's thread when there was none. Do not do slow work
      in the handler.
    - Volume and IsLooping persist across loads. Load a second file on the same
      player to move on to the next song; Dispose the player when done.
    - In a CodeBrix.Platform application, the AudioPlayer add-in wraps this same
      player as a XAML element with a bindable position; see that add-in's own
      AGENT-README.

GETTING THE DOWNLOAD RIGHT
  In Suno choose "Extract Stems and MIDI".
    - Split mode: "Auto split" (Suno's own list of twelve instruments). Only
      Auto split is supported.
    - Range: "Full Song".
    - Download options: check WAV and MIDI. MP3 is optional.
    - Tempo: always "Follow tempo changes". It keeps every tempo shift and is
      truer to the song; "Fixed tempo" flattens the map.
    - Click Download, wait through "Preparing...", and keep the resulting
      "<Title> Stems.zip" as it is or extract it. Either form loads.

  GET THE WAV FILES. This is the one piece of advice worth repeating. The WAVs
  are a significant quality-of-life improvement over the MP3s and they line up
  exactly with each other; an MP3 carries encoder delay and padding, which this
  package now trims (see the MP3 note below) but which is one more thing between
  you and the music. A stems set with no WAV loads and plays; it is simply worse.

LOADING
  var song = SunoStemsLoader.Load(path);                 // zip or folder
  var song = SunoStemsLoader.Load(path, options);
  var song = await SunoStemsLoader.LoadAsync(path);      // use this from a UI

  Loading lists the files, reads each .mid, and reads each WAV's HEADER. It does
  not decompress any audio: seven real four-minute exports - about 1.2 GB of
  zips - load in well under a second. A stem's audio is materialised the first
  time something asks for it.

  Load throws only for a path that is not there, a blank path, and a file that is
  not a readable zip. Nothing about the CONTENT of an export throws: everything
  that could not be honoured is one human-readable line in song.Problems (and,
  for a line about one part, on that stem's own Problems).

WHAT YOU GET
  SunoSong    Title (taken from the FILE NAMES, never from the MIDI meta, which
              the exporter mangles), Stems, AudioStems, MidiStems, Duration,
              TempoMap, InitialBeatsPerMinute, FullMixPath, CacheFolder,
              Problems, Options, song["Drums"] by name, ClearCache().
  SunoStem    Name, HasWav / HasMp3 / HasAudio / HasMidi, Midi (a MidiSequence),
              GmProgram, Channel, IsPercussion, NoteCount, MidiCoverage,
              Duration, AudioSampleRate, AudioChannels, the alignment members
              below, GetAudioPath() / OpenAudio() (WAV preferred, MP3 fallback)
              and the WAV- and MP3-specific forms of both.

  The stem vocabulary is OPEN. The twelve known names - Vocals, Backing Vocals,
  Drums, Percussion, Bass, Guitar, Keyboard, Piano, Synth, Strings, Brass, FX -
  carry a General MIDI program and channel each; a name outside the list still
  loads and plays, gets program 0 on channel 1, and is reported once. Where a
  stem HAS MIDI, the program change and channel in that file win over the
  vocabulary, and a channel of 10 also sets IsPercussion.

NEAR-EMPTY MIDI STEMS, AND WHAT MidiCoverage IS FOR
  A .mid beside a stem says nothing about whether it is worth playing. Real
  exports contain a Backing Vocals transcription of THREE notes next to a
  full-length recording. MidiCoverage is the fraction of the song during which
  that stem has a note sounding, 0 to 1, and it is the number to decide with:
  three notes in four minutes measures about 0.001, while a complete part sits
  between 0.1 and 0.9. NoteCount is the raw count. Every note counts as sounding
  for at least SunoLoadOptions.MinimumNoteHold, or a complete drum transcription
  would measure as covering none of the song.

ALIGNMENT
  A machine transcription does not land exactly on the audio it was transcribed
  from. The offset is CONSTANT across a song - it does not drift - but it differs
  from song to song and from part to part within one song, by as much as a
  quarter of a second either way. So the loader measures it, per stem, against
  that stem's own recording, on worker threads.

    stem.AlignmentOffset          the offset to apply. Settable: this is where
                                  you put a hand-measured or user-entered value.
    stem.MeasuredAlignmentOffset  what this stem's own measurement said.
    stem.AlignmentMeasured        whether it was measured at all.
    stem.AlignmentConfidence      0 to 1.
    stem.AlignmentIsReliable      whether the estimator stands behind it.
    stem.AlignmentIsFallback      whether AlignmentOffset holds the song's
                                  fallback rather than this stem's own answer.
    stem.AlignmentWindow          how far the search looked, either way.

  THE SIGN CONVENTION, which is the one thing to get right:
      audioTime = midiTime + AlignmentOffset
  A POSITIVE offset means the audio LAGS the MIDI, so the MIDI has to be DELAYED
  by that much to line up. A player built from the song puts it straight into
  each track's MidiSourceOffset, which moves the synthesized rendition and leaves
  the recording where it is.

  A stem whose own measurement is not reliable - and a stem with MIDI and no
  audio of its own - takes the song's FALLBACK: the median of the offsets of the
  stems that WERE measured reliably, or zero when none were. In practice the
  drum stem is the one that measures reliably and the rest of the song follows
  it, which is usually right and is always visible through AlignmentIsFallback.

  THE CAVEAT, honestly. Cross-correlating a part against its own recording cannot
  always tell one beat from the next: on a repeating pattern every subdivision is
  a peak of the same comb, and the tallest is not always the true one. The
  estimator arbitrates between near-equal peaks on a coarser measure - the shape
  of where the part PLAYS against where the recording has energy - and where that
  cannot decide, it lowers the confidence rather than pretending. What it cannot
  do is be certain. Treat a reliable measurement on a drum or percussion part as
  trustworthy, treat anything else as advisory, and remember that the value is
  exposed and overridable per stem (stem.AlignmentOffset) and per track
  (track.MidiSourceOffset). Setting MeasureAlignment = false turns the whole
  thing off and leaves every offset at zero.

BUILDING THE PLAYER
  var player = song.CreatePlayer();                    // recordings only
  var player = song.CreatePlayer(instrumentFactory);   // parts can play as MIDI
  var player = song.CreatePlayer(new SunoPlayerOptions { ... });
  var player = MultiTrackPlayer.Load(song, options);   // the same thing

  One track per stem, in the model's own order, named after the stem. Every track
  starts on its stem's RECORDING, which is the default mix: the song as it was
  downloaded. Where a stem also has MIDI and an instrument can be built for it,
  the same track carries the MIDI as its second source, so switching a part to a
  synthesized rendition is a property change. A stem with MIDI and no recording
  becomes a MIDI-only track; a stem with MIDI, no recording and no instrument is
  not added at all.

  SunoPlayerOptions
    InstrumentFactory           Func<SunoStem, int, IMidiSynthesizer>: build the
                                instrument for one stem at one sample rate. It
                                may be called more than once and from a worker,
                                so never hand out the same synthesizer twice -
                                share the SoundFont or the SFZ instrument behind
                                them instead.
    GeneralMidiSoundFontPath    used when there is no factory. Falls back to the
                                path on SunoLoadOptions, so a song loaded with
                                one needs no player options at all. The SoundFont
                                is loaded ONCE through a SoundFontCache and
                                shared by every track.
    SoundFontCache              which cache to load it through; null uses
                                SunoPlayerOptions.SharedSoundFonts, a
                                process-wide one you can Clear().
    IncludeMidiSources          attach the MIDI at all. True by default. A track
                                holding two sources renders both on every block,
                                which is what makes a switch seamless and is also
                                the main cost; turn it off for a player that will
                                only ever play the recordings.
    ApplyAlignmentOffsets       true by default; see ALIGNMENT above.
    IgnoreNoteOffOnPercussion   false by default: percussion parts hold each note
                                for SunoLoadOptions.MinimumNoteHold instead.
    AutoSetRelativeTrackLevels  passed to the player; see PLAYING A MULTI-TRACK
                                SONG above.

THE GENERAL MIDI SOUNDFONT
  A MIDI stem needs an instrument, and the ordinary choice is one General MIDI
  SoundFont for the whole song, applying each stem's own program and channel.
  RECOMMENDED: FluidR3_GM. It is MIT licensed, may be redistributed with your
  application, and covers all 128 programs and the standard drum kit that a
  stems export's program numbers refer to. TimGM6mb is smaller and is what many
  Linux distributions install, but it is GPL: nothing in this family ships it,
  no fixture references it, and you should not put it in a package either. Point
  the loader or the player at whichever file YOUR application is licensed to
  distribute.

  Percussion goes to CHANNEL 10 - a stems export writes its drum and percussion
  parts there already - and those parts get the percussion rule, because their
  notes are written with no length at all.

WHERE THE FILES GO
  Audio readers need a seekable stream, so a zip's entries are extracted on
  demand. By default they go to a per-song folder under
  SunoStemsLoader.DefaultCacheRoot (<temp>/CodeBrix.Audio/SunoStems), keyed by
  the download's path, size and last-write time, so loading the same download
  again extracts nothing. song.CacheFolder says where; song.ClearCache() removes
  what that song extracted and SunoStemsLoader.ClearCache() removes the whole
  root. Point SunoLoadOptions.CacheFolder somewhere else if you want the files
  kept with your project.
  SunoZipExtraction.Memory decompresses entries into memory instead and writes
  nothing to disk. A four-minute export is several hundred megabytes of WAV, and
  GetWavPath / GetAudioPath then throw because there is no path to give, so this
  is for hosts that cannot write to disk rather than a default.

TAKING THE SONG SOMEWHERE ELSE
  song.ExportMergedMidi(path) writes every MIDI stem into ONE General MIDI file -
  each part on its own channel, percussion on channel 10, the shared tempo map,
  each part's alignment already applied. It needs no instrument and no audio, so
  it works on a song loaded with nothing configured. Nothing outside this package
  opens twelve MIDI files as one song.

A COMPLETE EXAMPLE
  Play the vocal from its recording and every other part through a General MIDI
  SoundFont, with the levels of the original mix:

    using CodeBrix.Audio.Playback;
    using CodeBrix.Audio.Playback.Suno;

    var song = SunoStemsLoader.Load(@"D:\Downloads\My Song Stems.zip");
    foreach (var problem in song.Problems)
    {
        Console.WriteLine(problem);          // never thrown; usually empty
    }

    using var player = song.CreatePlayer(new SunoPlayerOptions
    {
        GeneralMidiSoundFontPath = @"D:\SoundFonts\FluidR3_GM.sf2",
        AutoSetRelativeTrackLevels = true,
    });

    foreach (var track in player.Tracks)
    {
        var stem = song[track.Name];
        var worthPlaying = track.HasMidiSource && stem.MidiCoverage > 0.02;
        if (worthPlaying && track.Name != "Vocals")
        {
            track.ActiveSource = TrackSource.Midi;
        }
    }

    player.Prepare();
    await player.LevelMeasurement;           // the levels are in when this returns
    player.Play();

  One named part, without walking the list - player["Bass"] matches the way
  song["Bass"] does, and throws with the available names when there is no such
  track:

    var bass = player["Bass"];
    if (bass.HasMidiSource)                  // not every stem has a .mid beside it
    {
        bass.ActiveSource = TrackSource.Midi;
    }

  Give one part an instrument of its own instead of the General MIDI SoundFont:

    var strings = sfzCache.Get(@"D:\Libraries\Strings\strings.sfz");
    using var player = song.CreatePlayer((stem, rate) => stem.Name == "Synth"
        ? new SfzSynthesizer(strings, rate)
        : new SoundFontSynthesizer(generalMidi, rate));

  Correct one part's alignment by hand, then bounce the result to a file:

    song["Drums"].AlignmentOffset = TimeSpan.FromMilliseconds(-120);
    using var bounce = song.CreatePlayer(options);
    bounce.RenderToWav("mix.wav", 44100);


PLAYING DECENT SAMPLER INSTRUMENTS
==================================
A Decent Sampler instrument is an XML preset plus the samples beside it. This
package reads that format and plays it: the whole sampler, the parameter and
binding model behind a preset's knobs, its effect chains and buses, its
modulators, its MIDI handlers, its note sequences and its arpeggiator. "Decent
Sampler" is Decidedly LLC's name for the format and its player, and appears here
only to say what the files are.

  using CodeBrix.Audio.Synth.DecentSampler;

  using var instrument = DecentSamplerInstrument.Load(@"D:\Libraries\Choir.dslibrary");
  using var player = new MidiMusicPlayer();
  player.Load(instrument, new MidiSequence("song.mid"));
  player.Play();

Everything below is the detail behind those four lines.

WHAT YOU CAN HAND TO LOAD
  .dspreset   the preset file itself. Samples/ and Resources/ sit beside it.
  a FOLDER    holding a .dspreset. The first one found is used.
  .dslibrary  a zip container. Read IN PLACE - nothing is ever unpacked to disk.
  .dsbundle   the same, and the macOS folder form of it also works.

  A container may hold several presets. DecentSamplerContainer.Open(path)
  .FindPresets() lists them, and DecentSamplerLoadOptions.PresetName picks one:

    using CodeBrix.Audio.Synth.DecentSampler.Containers;   // the container lives here

    using var container = DecentSamplerContainer.Open(path);
    foreach (var entry in container.FindPresets())
    {
        Console.WriteLine(Path.GetFileNameWithoutExtension(entry));
    }

    using var chosen = DecentSamplerInstrument.Load(
        path, new DecentSamplerLoadOptions { PresetName = "Cantores Oohs" });

  A library that ships a DSLibraryInfo.xml beside or above its preset gets it
  read for free: instrument.LibraryInfo carries the library's name, version,
  cover art and preset menu. Its productId marks a library distributed through
  the store; it is REPORTED and never acted on - nothing here activates,
  licenses or unlocks anything.

  instrument.Container is the container the preset came from, so a host can
  resolve and open a file the preset names beside itself - an image, a text
  file - exactly the way the engine resolves a sample: TryResolve turns a
  preset-relative path into a key (capitalisation need not match the disk and
  either slash works), and OpenFile reads it. The instrument OWNS it; do not
  dispose it.

THE INSTRUMENT, AND SHARING IT
  DecentSamplerInstrument is IDisposable and owns its decoded audio. Loading the
  same library twice decodes it twice, which for a sampled library is the
  expensive thing in the whole system - so share instruments through a cache:

    var cache = new DecentSamplerInstrumentCache();
    var choir = cache.Get(@"D:\Libraries\Choir.dslibrary");

  A cache shares SAMPLE DATA between the instruments it holds, so a library
  whose presets differ only in their interface costs its recordings once however
  many of its presets are open. SharedSampleByteCount and SharedSampleCount say
  what that comes to. Clear() keeps the shared audio; Dispose() releases it.

  MidiMusicPlayer.SharedDecentSamplerCache is the process-wide cache behind the
  path form of Load, exposed so an application can pre-load a library or clear
  it. The player never disposes it.

  ONE INSTRUMENT PER INDEPENDENT PERFORMANCE. A preset's knob positions,
  controller values and modulated parameters are INSTRUMENT state, because that
  is where the format puts them: a binding writes the group's volume, not the
  synthesizer's. Two synthesizers over one instrument therefore share every knob
  and every live parameter. That is exactly right for sharing one loaded library
  across several players of the same sound, and wrong when two parts must move
  their own knobs - give each its own instrument then.

MEMORY, STREAMING AND LAZY LOADING
  Sample libraries are large. A preset's samples decode to 32-bit float, and a
  96 kHz stereo library reaches gigabytes, so the loader decides per FILE
  whether to hold it in memory or stream it from disk:

    playbackMode="memory"          held, whatever its size
    playbackMode="disk_streaming"  streamed, however small
    playbackMode="auto" (default)  streamed when its decoded size passes
                                   StreamingSampleThresholdBytes (8 MB)
    then, if what is left still passes InstrumentMemoryBudgetBytes (1 GB), the
    "auto" files that save the most stream until it fits, largest saving first.

  Both numbers are on DecentSamplerLoadOptions, both are tunable, and 0 turns a
  rule off. PlaybackModeOverride replaces every playbackMode in the preset
  before any of it runs. instrument.MemoryPolicySummary is one line saying what
  was decided, DecodedByteCount is what the instrument HOLDS (a streamed
  sample counts only its preload head), and StreamedSampleCount says how many
  files stream. When the budget forced files to stream that the format's own
  default would have kept in memory, that summary is also added to Problems -
  something the preset asked for was not done as written.

    using var big = DecentSamplerInstrument.Load(path, new DecentSamplerLoadOptions
    {
        InstrumentMemoryBudgetBytes = 512L * 1024 * 1024,
        StreamingSampleThresholdBytes = 4L * 1024 * 1024,
    });

    Console.WriteLine(big.MemoryPolicySummary);

  A streamed file keeps a PRELOAD HEAD in memory - its first
  StreamingPreloadFrames frames, 65,536 by default - so a note starting at the
  beginning of the file sounds without waiting for a disk read. A FOLDER library
  streams strictly better than the same library inside a .dslibrary, because a
  backward seek inside a zip entry costs a walk from the start of it.

  DecodeSamples = false means LOAD ON FIRST USE, not "never": every path is
  resolved and every problem is still found, the whole model is built, and no
  audio file is opened until a note wants one. That is what a library browser or
  a validity check wants. The first note that needs a file is SILENT and the
  instrument says so in Problems; every note after it sounds.

  SAMPLE_START, SAMPLE_END, LOOP_START AND LOOP_END TAKE EFFECT ON THE NEXT
  NOTE. A binding that moves one of them changes what the next note-on reads; a
  voice already sounding keeps the bounds it started with, so dragging the knob
  under a held chord is silent until the next note. That is the same rule
  whether the sample is held in memory or streamed - the format's own guide says
  these four need in-memory playback, and a streamed instrument additionally
  says so once in Problems.

  STREAMING MODE - THE ONE SETTING AN OFFLINE RENDER MUST GET RIGHT.
  DecentSamplerSynthesizerSettings.StreamingMode decides who reads a streamed
  sample off the disk (the enumeration is
  CodeBrix.Audio.Synth.DecentSampler.Streaming.DecentSamplerStreamingMode):

    RealTime (the default)  a background reader thread fills the voices'
                            buffers, so the render call never touches a file.
                            The only safe choice when the render call is a real
                            audio callback.
    Offline                 the render call fills its own buffers first, so a
                            streamed voice can never fall behind however fast
                            the renderer runs.

  A render that runs faster than real time OUTRUNS the background reader, and a
  starved block is written as silence and reported, so a non-callback render in
  the real-time mode is not reproducible. Set Offline for a WAV export, a render
  into a buffer, or a test:

    using CodeBrix.Audio.Synth.DecentSampler;
    using CodeBrix.Audio.Synth.DecentSampler.Streaming;

    var settings = new DecentSamplerSynthesizerSettings(48000)
    {
        StreamingMode = DecentSamplerStreamingMode.Offline,
    };

  SoundFontRenderer does it for you - every render it makes is offline, and it
  switches a synthesizer you hand it over for the render and back afterwards.
  DecentSamplerSynthesizer.StreamingMode is settable, so a synthesizer built for
  a device can be borrowed for a bounce. Never leave Offline on a synthesizer
  feeding a live device: the render call then opens and reads files.

PLAYING IT
  Through the transport, with a device:

    using var player = new MidiMusicPlayer();
    player.Load(instrument, sequence);          // prefer this overload
    player.Load(@"D:\Libraries\Choir.dslibrary", "song.mid");   // or by path
    player.Play();

  Load(instrumentPath, midiFilePath) dispatches on the extension, so .dspreset,
  .dslibrary, .dsbundle and a folder join .sf2 and .sfz with no other change.
  The path form goes through SharedDecentSamplerCache.

  Offline, with no device:

    var samples = SoundFontRenderer.Render(instrument, sequence, 48000,
                                           TimeSpan.FromSeconds(2));
    SoundFontRenderer.RenderToWavFile(instrument, sequence, "bounce.wav", 48000,
                                      TimeSpan.FromSeconds(2));

  Or drive the synthesizer yourself - it is an IMidiSynthesizer like the other
  two, so MidiSequencer, MultiTrackPlayer and the Suno stems player all take it:

    var synthesizer = new DecentSamplerSynthesizer(instrument, settings);
    synthesizer.NoteOn(0, 60, 100);
    synthesizer.Render(left, right);

  A synthesizer never disposes its instrument, and is not thread-safe. The
  defaults worth knowing: 192-voice polyphony (a preset routinely layers a dozen
  groups), 64-frame blocks, master volume 0.5, and a seeded random stream, so
  the same instrument and the same events render the same bytes every run on
  the same machine (see "Renders are repeatable per machine" under COMMON
  PITFALLS before comparing renders made on different operating systems).

NOTE NAMES: C3 IS 60, WHICH IS NOT WHAT SFZ SAYS
  A Decent Sampler preset writes note names in the YAMAHA convention:
  midi = (octave + 2) * 12 + pitch class. So rootNote="C3" is MIDI 60, "C4" is
  72 and "A4" is 81. The SFZ engine in this same package uses scientific pitch
  notation, where C4 is 60. The two parsers are deliberately separate. A bare
  integer is a MIDI number in both.

WHAT COULD NOT BE HONOURED: Problems AND UnsupportedFeatures
  Loading a preset throws only for XML that is not well formed
  (DecentSamplerParseException, with the line and column). Everything else is
  reported and the instrument still plays:

    instrument.Problems             one human-readable line per thing that could
                                    not be honoured, in the order it was found.
    instrument.UnsupportedFeatures  the NAMES of what the engine did not
                                    understand - an element, an attribute, an
                                    effect type, a waveform - sorted, each once.

  Unknown attributes and elements are also kept verbatim on the model, so
  nothing in a preset is lost even when nothing here reads it. Problems can gain
  lines AFTER loading (a streaming underrun, a sample decoded on first use), so
  read the property rather than caching the list.

  THE ENGINE FALLS BACK THE WAY THE REFERENCE PLAYER DOES, on purpose. An
  unreadable attribute value uses the documented default and says so. A missing
  sample leaves its zone in the model and silent. An unknown effect type is
  BYPASSED, not fatal. An unknown oscillator waveform sounds a sine when the
  add-on is registered, and is silent without it. A round-robin position with no
  zone plays nothing. Real libraries carry real typos - a reverb written
  damping="O.2" with a capital letter O turns up in five separate libraries -
  and each is reported rather than worked around.

THE CONTROL MODEL: A PRESET'S KNOBS
  A preset's <ui> section is parsed into a LIVE control surface. Nothing here
  draws an interface, but every control is real: its value drives the
  instrument's initial state at load, and writing it fires the same bindings a
  user turning the knob would.

    foreach (var control in instrument.Controls)
    {
        Console.WriteLine($"{control.Index} {control.Kind} {control.Name} = {control.Value}");
    }

    var attack = instrument.GetControl("ATTACK");
    attack.SetValue(0.4);                       // fires its bindings at once
    attack.Changed += (_, e) => Console.WriteLine(e.PropertyName);

  Controls is EVERY element under every tab, in the order a binding's
  controlIndex counts them - labels, images and rectangles included. Do not
  filter it. GetControl(name) finds the first by name, case-insensitively.

    SetValue(double)         a knob or a slider
    SetXValue / SetYValue    an X-Y pad's axes, 0 to 1
    Select(int) / Select(string)   a button state, a multi-state, or a menu
                                   option. The index is 0-based for every kind.

  A control also exposes what a renderer would need - Visible, Enabled, Text,
  Path, Opacity, CurrentFrame, the colours, the geometry - and a binding moves
  all of it. Reading and writing those changes nothing about the sound.

  TAGS. instrument.TagStates has one entry per tag named anywhere in the preset,
  with Enabled, Volume, Pan and Polyphony. TAG_ENABLED switches a whole layer -
  its zones and the effects carrying that tag - off together; TAG_VOLUME scales
  it; TAG_POLYPHONY caps how many of its voices may sound, oldest first. Several
  tag volumes on one zone multiply.

  WATCHING FOR CHANGES. instrument.ParameterChanged fires off the audio thread
  with the target, the parameter and the new value. instrument.ParameterVersion
  is an integer that counts every change; read it once a block and compare with
  the last value you saw - that is the allocation-free way to notice that
  anything moved.

THE MIDI ELEMENT AND KEY SWITCHES
  A preset's <midi> section listens for controllers, notes and velocity, and
  runs its bindings BEFORE the note is played. It is live:

    <cc number="1">      fires on a CHANGE of that controller's value only, and
                         every controller starts at 0, so a first message of 0
                         does nothing. Not channel-specific.
    <note note="24-35" swallowNotes="true">
                         a KEY SWITCH: the note works the bindings and is then
                         CONSUMED. No voice starts, the key is not counted as
                         held, and nothing downstream ever sees it.
    <velocity>           fires on every note-on with the velocity as its source.

  Send controllers through the player or the synthesizer as usual:

    player.SendMidiMessage(0, 0xB0, 1, 96);     // CC 1 -> the preset's knob
    synthesizer.ProcessMidiMessage(0, 0xB0, 1, 96);

NOTE SEQUENCES AND THE ARPEGGIATOR
  Both generate notes into the same note path a MIDI file's notes take, so they
  work a key switch and reach every group.

  A <noteSequences> sequence is started by a binding with no `parameter` - an
  ACTION rather than a value. seqTriggerBehavior decides what starts it:
  midi_key (start on the key down, stop on the key up, the default), on, or off.
  A player is tracked under seqPlayerIdentifier, or one per (channel, key) when
  the binding names none, so a handler covering a range of key switches runs a
  different sequence for every key. Speed is the sequence's own rate against the
  synthesizer's TempoSource, read every block, so a RATE binding changes the
  speed of a sequence that is already running.

  THREE THINGS ABOUT A SEQUENCE'S TIMING THAT SURPRISE PEOPLE, all of them the
  reference player's own behaviour:
    - the FIRST note sounds the instant the key goes down, whatever beat it is
      written at, and the rest of the grid moves with it. A sequence whose first
      note is at beat 2 does not wait two beats.
    - a note's `position` and its `length` are TRUNCATED TO WHOLE BEATS. A
      sequence written with fractional positions plays CHORDS, not a rhythm, and
      a length of 0 means one step. The sequence's declared `length` truncates it
      too: a note written at or past it never plays.
    - lifting the key CUTS the note the sequence is sounding, even one written
      longer than the key was held, and schedules no further step.
  Emitted velocity is
      127 * noteVelocity * ((1 - seqTrackMidiInputVelocity)
                            + seqTrackMidiInputVelocity * triggerVelocity / 127)
  so the documented default of 1.0 is the velocity-following case and 0 plays
  the sequence at its own written velocities.

  The <arpeggiator> is a singleton and, when enabled, CONSUMES every note played
  and emits its own instead - it takes over rather than layering. Its nine
  orders, its octave range and mode, its step count, its gate length and its
  clock are all live and modulatable, and a change is heard on the next step.
  An MPE member channel travels with the notes it generates, so a bend or a
  slide still reaches them.

  An ALL_NOTES_OFF binding releases every voice, stops every sequence player and
  empties the arpeggiator.

EFFECTS, BUSES AND AUXILIARY OUTPUTS
  A preset can put an effect chain in three places, and each is honoured:

    <effects> under a <group>   ONE INSTANCE PER SOUNDING VOICE. That is what the
                                format says and what the reference player does,
                                and it is the expensive one: a chain with a
                                reverb in it costs a reverb per voice. Chains
                                are pooled and reset rather than rebuilt, so a
                                steady stream of notes costs one per concurrent
                                voice and no more.
    <effects> under a <bus>     once per block, on the bus's own mix.
    <effects> at the top level  once per block, on the finished main mix.

  This package carries the mixing and room effects: lowpass (and its legacy
  spelling lowpass_4pl), lowpass_1pl, bandpass, highpass, notch, peak, gain,
  reverb, delay, chorus, convolution and the compressor. The creative ones -
  phaser, pitch_shift, wave_folder, wave_shaper, stereo_simulator, bit_crusher
  and gate - come from the add-on package. An effect type nothing supplies is
  BYPASSED and named in Problems; the preset still plays.

  Effect parameters are live: the parsed <effect> element is the single source
  of truth, and a knob, a MIDI CC or a modulator reaches the audio within one
  block. An effect whose tag is disabled is bypassed while that tag is off.

  BUSES. Up to sixteen, each with its own chain, its own volume and up to eight
  sends. A bus may feed another bus, and they are processed in an order that
  makes every feed arrive before the bus that receives it, whatever order they
  are declared in. A routing cycle drops the send that closes it and says so; a
  send to a bus the preset never declares is folded into the main output and
  said so.

  AUXILIARY OUTPUTS. A preset may route a group or a bus to
  AUX_STEREO_OUTPUT_1..16 - a wet layer, a mic position, a stem. By default they
  are FOLDED into the stereo mix, so nothing an instrument makes is lost:

    player.DropAuxiliaryOutputs = true;   // hear only the main output instead

  To take them separately, the synthesizer implements IMultiOutputRenderer
  (CodeBrix.Audio.Synth). The main output is a pair of spans; the auxiliary
  channels are ONE BUFFER PER PAIR, so both auxiliary arguments are float[][]:

    var renderer = (IMultiOutputRenderer)synthesizer;
    var pairs = renderer.AuxiliaryOutputCount;      // the HIGHEST pair named

    var auxLeft = new float[pairs][];
    var auxRight = new float[pairs][];
    for (int i = 0; i < pairs; i++)
    {
        auxLeft[i] = new float[left.Length];        // every buffer the same length
        auxRight[i] = new float[left.Length];       //   as the main output's
    }

    renderer.RenderWithAuxiliary(left, right, auxLeft, auxRight);

  A shorter array discards the pairs beyond it and null discards every pair.
  RenderWithAuxiliary never folds, whatever FoldAuxiliaryOutputs says: asking
  for the pairs separately is the whole point of calling it.

MODULATORS
  A preset's <modulators> run whenever the instrument does: an LFO, an envelope,
  a MIDI CC, note velocity, MPE timbre, MPE pressure and a random source, each
  with its own bindings, its own depth and one of four behaviours (set, add,
  modulate, multiply). Scope is global (one value for the instrument) or voice
  (its own value per note); the defaults differ per element, as the format
  documents them - lfo, midiCC and random are global, envelope, midiVelocity,
  mpeTimbre and mpePressure are per voice.

  A voice-scope modulator reaches anything read INSIDE that voice's render - its
  gain, its pan, its tuning, its own group effect chain. Something read after
  every voice has rendered, such as the instrument's own effect chain, sees the
  global value.

  An <envelope> modulator is a four-stage ADSR in seconds whose `sustain` is a
  LINEAR AMPLITUDE and whose release runs for exactly its own time and ends at
  zero. With no curve attributes written every stage follows the shape the
  reference uses, gain = (1 - exp(-4x)) / (1 - exp(-4)) across the stage; unlike
  the reference, this engine also HONOURS attackCurve, decayCurve and
  releaseCurve when a preset writes them.

  A modulator's own parameters are themselves bindable, so a knob can move a
  running LFO's rate or an envelope's attack.

    settings.EnableModulators = false;   // play the preset as the sampler alone

  Every random draw is seeded, so a modulated preset renders the same bytes from
  the same events on the same machine.

MPE
  DecentSamplerSynthesizer reads an MPE performance out of a MIDI file - zone
  configuration, per-channel bend and bend range, per-note pressure and timbre,
  and the master/member combination rules. See the MPE FROM MIDI FILES section
  for the whole contract, which is shared with the SoundFont and SFZ engines.
  The two settings to know here:

    synthesizer.MpeMode = MpeMode.Auto;        // also on MidiMusicPlayer
    synthesizer.MpeMemberBendRange = 48;       // semitones, when the file is silent

  A clip exported from a sequencer normally carries no configuration message, so
  Auto is the setting for one; LowerZone, UpperZone and Both pin an ambiguous
  file, and Off insists it plays as ordinary MIDI. mpeTimbre and mpePressure
  modulators read the voice's own channel. The format defines no consumer for
  release velocity, so it changes nothing about the sound; it is available
  through synthesizer.ReleaseVelocity(channel, key) and
  player.GetReleaseVelocity(channel, key).

THE ADD-ON: OSCILLATORS AND CREATIVE EFFECTS
  A group can hold an <oscillator> instead of a <sample>, and a chain can name
  an effect this package does not carry. Both come from a second package:

    dotnet add package CodeBrix.Audio.ModestSynth.MitLicenseForever

    using CodeBrix.Audio.ModestSynth;

    ModestSynth.Register();                    // ONCE, BEFORE you load anything
    using var instrument = DecentSamplerInstrument.Load(path);

  REGISTER BEFORE LOADING. Factories are looked up when an instrument is BUILT,
  not when its file is parsed, so a late registration does not retrofit an
  instrument that is already loaded - or a synthesizer already built over one.
  Nothing throws; the oscillator group is simply silent and the effect bypassed.
  Reload the instrument if you registered too late.

  Without the add-on a preset still loads and every sample group still plays.
  Problems then carries one line per missing feature, naming the package and the
  call - "oscillator waveform 'fm6op' needs CodeBrix.Audio.ModestSynth:
  reference it and call ModestSynth.Register() before loading" - and
  UnsupportedFeatures lists the names, so a host can decide whether to warn.

  Registering also changes what the feature list answers (the registry and the
  seam are in CodeBrix.Audio.Synth.DecentSampler.Engine):

    DecentSamplerSupportedFeatures.StatusOf(
        DecentSamplerFeatureCategory.Waveform, string.Empty, "fm6op",
        DecentSamplerExtensions.Shared);      // Implemented once registered

  A host that wants its own oscillator or effect registers it the same way:
  DecentSamplerExtensions.RegisterOscillator(name, factory) and
  RegisterEffect(type, factory), against IVoiceSource and IInstrumentEffect.

WHAT IS SUPPORTED, AND WHAT IS NOT
  DecentSamplerSupportedFeatures is the single source of truth: every element,
  attribute, binding type, binding level, binding parameter, translation mode,
  effect type, modulator type, waveform and enumeration value the format
  documents, each marked Parsed or Implemented.

    DecentSamplerSupportedFeatures.IsAttribute("group", "silencingMode");
    DecentSamplerSupportedFeatures.StatusOf(
        DecentSamplerFeatureCategory.EffectType, string.Empty, "reverb");

  Everything NOT implemented is grouped and explained in the residual table, and
  the table is derived from the feature list rather than written by hand, so it
  cannot drift:

    Console.WriteLine(DecentSamplerResidualTable.Describe());

    foreach (var entry in DecentSamplerResidualTable.Entries)
    {
        Console.WriteLine($"{entry.Name}: {entry.Features.Count}");
    }

  THE RESIDUAL TABLE, in one paragraph each:

    user interface appearance - the <ui> section becomes a live control model
      and nothing here draws it, so positions, colours, images, fonts, skins,
      tooltips and frame animations are stored rather than rendered.
    sound generation and creative effects - the add-on package supplies them;
      the entry's IsSuppliedByAddOn is true and one call turns them all on.
    library catalogue and store distribution - DSLibraryInfo is read and
      exposed; productId is reported and never acted on.
    format version negotiation - minVersion and pluginVersion are read and
      reported when a preset asks for more than this engine documents.
    sample attributes an oscillator zone inherits - the start and end offsets,
      the loop points and playbackMode have nothing to act on when a zone
      generates its audio instead of reading it.
    the filter envelope - a filter's five envelope_* attributes are parsed and
      kept; the reference player has no audible consumer for them.
    effect spellings the guide's tables never define - shape and wetDryMix are
      recognised so a preset carrying one is not called broken, and not read.
    tag pan - parsed and exposed on the tag state; no voice is moved by it.
    editor bookkeeping - a <sample>'s length is what the editor recorded; the
      engine reads the real length from the file.

  One waveform is outside the list entirely: a newer version of the reference
  player adds a "formant" oscillator that the published format guide does not
  document. A preset naming it loads and reports the waveform as unrecognised,
  and - with the add-on registered - sounds the fixed tone the reference makes
  for it: one resonant region near 2.4 kHz over the note's own fundamental. It
  has NO attributes; the reference ignores every formant-* attribute written
  beside it, and so does this engine, which reports each one.

WHERE THIS ENGINE AND THE REFERENCE PLAYER DIFFER
  The format has no specification: the reference player defines the sound, and
  this engine was measured against it rather than guessed at. Whole presets were
  compared against recordings of the reference player bar by bar: a straight
  sampled instrument lands within a tenth of a decibel of its absolute level and
  under a decibel on every bar, which is what most libraries are made of. These
  are the differences that remain, published rather than hidden:

    undeclared bus  a group sent to a bus the preset never declares is SILENT in
                  both, but this engine also reports it in Problems. So is a bus
                  whose own output target names another bus: the format does not
                  let a bus feed a bus, and the reference says nothing about it.
    <velocity> bindings  reach the group their groupIndex names, and reach a
                  group's or the instrument's AMP_VOLUME. The reference applies
                  such a binding to EVERY group whatever its groupIndex says,
                  and does nothing at all on AMP_VOLUME. Both are reference
                  defects and doing less was not worth reproducing.
    midiCC scope  a voice-scope <midiCC> modulator reads its controller here;
                  the reference reads zero for one, so a preset written the
                  documented way does nothing there and works here.
    GLOBAL_TUNING  a modulator binding at level="instrument" moves the tuning
                  here; the reference ignores it and leaves the pitch alone.
    continuous zones  trigger="continuous" sounds here, and with no loop points
                  of its own it loops the whole file. The reference produced no
                  sound at all for such a zone.
    retrigger     the interval is exact here; the reference rounds it up to a
                  whole 512-frame processing block, which stretches a one-second
                  retrigger to 1.0095 s.
    no_loop       a note sequence set to no_loop stops after one pass here, at
                  every declared length. The reference fails to stop one whose
                  declared length is 2 - measured on two independent sequences,
                  while lengths 3 and 4 stopped - which is a defect and is
                  deliberately not reproduced.
    random sequences  draw from every note. Both of the reference's random loop
                  modes measurably never draw one note of a four-note sequence
                  (26 and 30 draws, odds of 0.05 % and 0.1 %), which is likewise
                  a defect and deliberately not reproduced.
    envelope curves  attackCurve, decayCurve and releaseCurve on an <envelope>
                  MODULATOR bend its stages here. The reference accepts and
                  ignores all three - a two-second attack at -100 and at +100
                  agreed to 0.1 dB at thirteen points - even though the group
                  AMPLITUDE envelope's own curves do work there. The default
                  shape is the reference's either way.
    first sample frame  a sample whose very first frame carries the signal
                  sounds here; the reference ramps a voice in over its first
                  frames and loses it.
    noise         the anti-imaging rolloff above 8 kHz is reproduced, and the
                  power centroid lands 1.16 dB darker than the reference's -
                  which is as close as the reference's own two figures allow.
    formant       the fixed tone's twenty-partial spectrum is reproduced within
                  1.5 dB; the PHASE of each partial could not be measured, so
                  the waveform's shape is this engine's own.
    fm6op detune  follows the measured power law over MIDI 24 to 84; outside
                  that range it is an extrapolation.
    heavily layered presets  a preset built almost entirely of tag-gated layers -
                  a dozen families of groups, each scaled by its own control -
                  measured about 1 dB louder than the reference over eight bars,
                  with a per-bar spread up to 1.6 dB. Every individual rule the
                  preset uses was measured and matches on its own (the tag
                  volumes multiply exactly, the effects account for 0.05 dB of
                  it), so what is left is a level relationship between one
                  family of layers and the rest. A master gain closes it if a
                  render has to match the reference exactly.

  None of these is silent: each is either audible in a way the table describes
  or reported in Problems. Most of them are places where the reference does LESS
  than the format documents and this engine does what the documentation says.

A COMPLETE EXAMPLE
  Open a library, list its presets, load one, set a knob, play a MIDI file, and
  export the same performance to a WAV file.

    using CodeBrix.Audio.Midi;
    using CodeBrix.Audio.ModestSynth;
    using CodeBrix.Audio.Playback;
    using CodeBrix.Audio.Synth;
    using CodeBrix.Audio.Synth.DecentSampler;
    using CodeBrix.Audio.Synth.DecentSampler.Containers;
    using CodeBrix.Audio.Synth.Mpe;

    // The add-on, once, before anything is loaded. Skip it if you know the
    // libraries you play hold no oscillators and no creative effects.
    ModestSynth.Register();

    const string libraryPath = @"D:\Libraries\Winter Voices.dslibrary";

    // What is in it?
    using (var container = DecentSamplerContainer.Open(libraryPath))
    {
        foreach (var entry in container.FindPresets())
        {
            Console.WriteLine(Path.GetFileNameWithoutExtension(entry));
        }
    }

    // Load one, under a memory budget of your choosing.
    using var instrument = DecentSamplerInstrument.Load(libraryPath,
        new DecentSamplerLoadOptions
        {
            PresetName = "WV NATURAL TONES",
            InstrumentMemoryBudgetBytes = 512L * 1024 * 1024,
        });

    Console.WriteLine(instrument.MemoryPolicySummary);

    foreach (var problem in instrument.Problems)
    {
        Console.WriteLine(problem);            // never thrown; often empty
    }

    foreach (var name in instrument.UnsupportedFeatures)
    {
        Console.WriteLine("not understood: " + name);
    }

    // Turn a knob the preset offers, exactly as a user would.
    var attack = instrument.GetControl("ATTACK");
    attack?.SetValue(0.35);

    // Play it.
    var sequence = new MidiSequence("song.mid");

    using (var player = new MidiMusicPlayer())
    {
        player.MpeMode = MpeMode.Auto;         // the file may be an MPE clip
        player.Volume = 0.8f;
        player.Load(instrument, sequence);
        player.Play();

        Console.ReadLine();
    }

    // Export the same performance. The renderer runs offline, so it puts the
    // instrument's streamed samples into the offline mode for the render.
    SoundFontRenderer.RenderToWavFile(
        instrument, sequence, "bounce.wav", 48000, TimeSpan.FromSeconds(3));


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

Play MIDI music through a Decent Sampler instrument (same transport again):

    using CodeBrix.Audio.Playback;
    using CodeBrix.Audio.Synth.DecentSampler;

    using var instrument = DecentSamplerInstrument.Load(@"D:\Libraries\Choir.dslibrary");
    using var music = new MidiMusicPlayer();
    music.Load(instrument, new MidiSequence("song.mid"));
    music.Play();
    // or, straight from the two paths:
    // music.Load(@"D:\Libraries\Choir.dslibrary", "song.mid");

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
  - Renders are repeatable per machine, not across operating systems: every
    synthesizer is deterministic (seeded randomness, fixed-point resampling), so
    the same instrument and the same events produce the same bytes on the same
    machine every time. The pitch, gain and envelope arithmetic reaches the
    operating system's maths library, and Windows, Linux and macOS round those
    functions differently by a hair, so a render made on one operating system
    matches the same render made on another to about one part in a million,
    not bit for bit. Compare cross-machine renders with a tolerance (a per-
    sample difference of 1e-4 is generous), and never cache a render keyed on a
    hash of its bytes if the cache is shared between operating systems.
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
  - MIDI leniency changes the event list: reading tolerantly (the default) closes
    a dangling note-on by INSERTING a synthesised note-off before the end of its
    track. Code that counts events, or that assumes the reader hands back exactly
    what the file held, must read with MidiReadMode.Strict or expect the extra
    event. Both readers need a SEEKABLE stream, and always did.
  - One synthesizer per track, never one shared: a synthesizer factory is called
    once per track per render context and may be called from a worker thread, and
    IMidiSynthesizer is explicitly not thread-safe. Share the SoundFont or the
    SfzInstrument behind them - those are the expensive part and are safe to
    share - and return a NEW synthesizer every time.
  - A multi-track track holding two sources renders BOTH on every block, whichever
    one is heard. That is what makes a source switch a crossfade rather than a
    restart, and it is the player's main cost: a track that will never switch
    should hold only the source it needs.
  - Offset moves a whole track; MidiSourceOffset moves only its MIDI rendition.
    An alignment measurement belongs in MidiSourceOffset. Putting it in Offset
    drags the recording out of step with the rest of the song, which is exactly
    what a stems set must not have done to it.
  - An alignment estimate is advisory outside drums and percussion. A repeating
    pattern gives the search several equally good answers a beat apart, and
    confidence measures how much one peak stands out, not whether it is the right
    one. Check IsReliable, look at CandidatePeakCount, and remember the value is
    overridable per stem and per track.
  - Extracting a stems zip to memory (SunoZipExtraction.Memory) makes every stem
    a Stream, and a Stream audio source is copied into memory when the track is
    built. Several hundred megabytes of WAV, twice. Use the default cache folder
    unless the host genuinely cannot write to disk.
  - Decent Sampler: REGISTER THE ADD-ON BEFORE YOU LOAD. A preset whose group
    holds an <oscillator>, or whose chain names phaser, pitch_shift, wave_folder,
    wave_shaper, stereo_simulator, bit_crusher or gate, needs the
    CodeBrix.Audio.ModestSynth package - reference it and call
    ModestSynth.Register() BEFORE DecentSamplerInstrument.Load. Factories are
    resolved when an instrument is BUILT, so registering afterwards does not
    retrofit one that is already loaded, or a synthesizer already built over it.
    Nothing throws: the oscillator group is silent, the effect is bypassed, and
    Problems names the package and the call. Reload the instrument if you were
    late.
  - Decent Sampler note names are YAMAHA, not scientific: rootNote="C3" is MIDI
    60 here, where the SFZ engine in this same package reads C4 as 60. The two
    parsers are deliberately separate, and a bare integer is a MIDI number in
    both.
  - An offline render of a Decent Sampler instrument that STREAMS its samples
    must use DecentSamplerStreamingMode.Offline. The real-time mode reads ahead
    on a background thread, which a render running faster than real time
    outruns; a starved block is written as silence and reported. SoundFontRenderer
    sets it for you, including on a synthesizer you hand it; a render loop of
    your own has to say so. Never set it on a synthesizer feeding a live device -
    the render call then opens and reads files.
  - One Decent Sampler instrument per independent performance. A preset's knob
    positions and its live parameters belong to the INSTRUMENT, because that is
    where the format puts them, so two synthesizers over one instrument share
    every knob. Sharing one loaded library across players of the same sound is
    the point of the cache; two parts that must move their own knobs need two
    instruments.
  - A Decent Sampler group with its own <effects> costs ONE INSTANCE PER SOUNDING
    VOICE, which is what the format specifies. A reverb at group level is a
    reverb per voice; put it on the instrument or on a bus unless the per-voice
    behaviour is what the preset is for.
  - Do not dispose a DecentSamplerInstrument a synthesizer is still playing, and
    do not dispose the Container it hands you: the instrument owns it and closes
    it. Note that MidiMusicPlayer.SharedDecentSamplerCache holds instruments for
    the life of the process.
  - A Decent Sampler instrument's Problems list can GAIN lines after loading - a
    streaming underrun, a sample decoded on first use. Read the property; do not
    cache the list you got at load time.
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
    envelope follower. (MidiAudioAlignment is not one: it measures how far an
    EXISTING transcription sits from its audio, and does not produce notes.)

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

  - No instrument USER INTERFACE. A Decent Sampler preset's <ui> section is
    parsed completely and becomes a live control model - every knob, button,
    menu, pad, label, image and colour is readable and writable, and writing one
    drives the sound exactly as a user would - but nothing here DRAWS it.
    Rendering a preset's own interface is a UI framework's job, and the control
    model is what it would draw.

  - No store, no licensing and no copy protection. A library's DSLibraryInfo
    productId marks it as distributed through a store; it is reported and never
    acted on. Nothing here activates, unlocks, downloads or validates anything,
    and a preset that expects to be unlocked simply plays.

  - No plugin hosting and no plugin format. This is a library, not a VST, AU or
    AAX host and not a plugin. It plays instrument FILES; it does not load
    anyone's processor binaries and cannot be loaded as one.

  - No MIDI devices. Nothing here opens a MIDI input or output port. MIDI arrives
    as a Standard MIDI File, or as messages your code sends - SendMidiMessage on
    the player, ProcessMidiMessage on a synthesizer - which is also how an MPE
    performance reaches the engine: recorded, exported as a file, and played.


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

  PACKET AUDIO
    PacketAudioPlayerTests.cs   the player end to end - the guards before Open,
                                an underrun playing silence and recovering, the
                                end of the stream, the clock, Seek and its
                                pre-roll, the codec's own pre-skip, and a real
                                Vorbis asset played through the packet path.
    PacketAudioPlayerTrimTests.cs   the trailing trim: the hold-back, which
                                frames are dropped, a trim set after Open, a trim
                                longer than the track, per-packet DiscardPadding
                                and the larger-of-the-two rule, and the clock not
                                counting trimmed audio.
    PacketAudioPlayerLossTests.cs   AudioPacket.Loss - a gap coming out the
                                length it really was, concealment in helpings,
                                silence where a decoder offers nothing, a gap as
                                media time, and a gap forgotten by a reposition.
    PacketCodecProbeTests.cs    IsPacketCodecSupported and
                                SupportedPacketCodecIds: case-insensitive, they
                                start nothing, and they agree with what
                                CreatePacketDecoder then resolves.
    VorbisPacketCodecFactoryTests.cs   the built-in factory - its identity, the
                                codec-private data it declines, decoding that
                                matches the stream decoder sample for sample, the
                                empty first packet, and Reset mid-stream.
    VorbisPacketLossTests.cs    the built-in decoder has no concealment of its
                                own and answers a gap with silence of exactly the
                                right length.
    Utils/FakePacketAudio.cs, Utils/OggPacketReader.cs   the test doubles: a
                                scripted IAudioPacketSource / IPacketSoundDecoder
                                pair, and the Ogg de-framer that turns a .ogg
                                fixture into the packets a container would hand
                                over. Read these first if you are writing a
                                source of your own.

  MIDI
    Midi/MidiFileTests.cs, MidiFileTests.cs     read/write round trips.
    Midi/MidiEventCollectionTest.cs             tracks, PrepareForExport.
    Midi/NoteOnEventTests.cs, Midi/NoteEventTests.cs,
    Midi/ControlChangeEventTests.cs, Midi/PitchWheelChangeEventTests.cs,
    Midi/SysexEventTests.cs, Midi/TimeSignatureEventTests.cs,
    Midi/KeySignatureEventTests.cs, Midi/MidiEventCloneTests.cs
        the event hierarchy, one file per event type.
    Midi/MidiFileLeniency.cs, Synth/MidiSequenceLeniency.cs,
    Midi/KeySignatureEventLeniency.cs
        tolerant reading, one test per departure a real file makes, with the
        strict reader's refusal beside each.
    Midi/SyntheticMidi.cs, Midi/SunoShapedMidi.cs   the byte-level SMF builders
                                the leniency tests use, including the malformed
                                shapes they need on purpose.

  MULTI-TRACK SONGS, STEMS AND ALIGNMENT
    MultiTrackPlayerTests.cs    tracks, the transport, gain/pan/mute/solo/offset,
                                the percussion rule, level matching, the merged
                                export.
    MultiTrackMixTests.cs, PlayerTrackTests.cs   the mix and the track model on
                                their own, with no audio device.
    MidiSourceOffsetScenarios.cs   the alignment lever: the rendition moves and
                                the recording does not, in both directions, in
                                the duration, and in the merged export.
    MidiAudioAlignmentTests.cs  the estimator recovering a planted offset,
                                refusing a decoy a beat away, counting rival
                                peaks and trusting none of them, and choosing
                                between rivals on the coarse shape of the part.
    Playback/Suno/SunoStemsLoaderTests.cs   the loader on a zip and on a folder,
                                the vocabulary, coverage, the cache, the
                                Problems report, and alignment end to end
                                including the per-song fallback.
    Playback/Suno/SunoPlayerTests.cs   the song-to-player mapping: the default
                                mix, source switching, the instrument factory,
                                offline render equalling the sum of the tracks,
                                and the merged export round-tripping.
    Playback/Suno/FakeSongStems.cs   the synthetic export the two files above
                                load - a stems set carrying every shape a real
                                one has, built in code.

  SOUNDFONT, SFZ, DECENT SAMPLER AND MIDI MUSIC
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
    Synth/Mpe/MpeSoundFontEngineTests.cs, Synth/Mpe/MpeSfzEngineTests.cs,
    Synth/Mpe/MpeMidiFileTests.cs, Synth/Mpe/MpeChannelStateTests.cs,
    Synth/Mpe/MpeBendRangeTests.cs, Synth/Mpe/MpeMidiMusicPlayerTests.cs
        an expressive performance played through each instrument format, with
        the pitches measured against the bend arithmetic to within a cent.
    Synth/DecentSampler/                   the Decent Sampler engine: the parser
                                       and the model at the top level, then
                                       Bindings/ for the parameter model,
                                       Engine/ for the zone and voice runtime,
                                       Effects/ and Buses/ for the chains,
                                       Modulation/, Sequencing/ and Streaming/.
                                       Engine/DecentSamplerIntegrationTests.cs and
                                       Engine/DecentSamplerConsumerSurfaceTests.cs
                                       are the ones that read like a consumer.

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
    PacketCodecRegistryTests.cs, FakePacketCodec.cs
        the engine-level packet registry - RegisterPacketCodecFactory,
        UnregisterPacketCodecFactory, SetPacketCodecPriority,
        GetRegisteredPacketCodecs and CreatePacketDecoder - including
        highest-priority-first order, the later registration winning a tie,
        case-insensitive codec ids, reordering with SetPacketCodecPriority, and
        CreatePacketDecoder moving on from a factory that declines or throws.
        It also shows the packet registry is separate from the stream one.
    OggOpusMetadataTests.cs
        that an Ogg Opus stream reports 48 kHz and a pre-skip-corrected
        duration even though this package cannot decode it.
    AudioFormatTests.cs, MiniAudioEngineTests.cs

A test that opens a real audio device and MAKES SOUND is opt-in behind an
environment variable - CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS for the main suite and
CODEBRIX_AUDIO_ENGINE_RUN_PLAYBACK_TESTS for the Engine's - so a file that looks
as though it plays something is usually reading it back instead. That is worth
knowing when you copy an example out of one.

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
  play a .mid through a .sf2, .sfz or a   new MidiMusicPlayer()
    Decent Sampler preset
  load a Decent Sampler instrument        DecentSamplerInstrument.Load(path)
  list the presets in a .dslibrary        DecentSamplerContainer.Open(path)
                                              .FindPresets()
  turn one of a preset's knobs            instrument.GetControl("ATTACK")
                                              .SetValue(0.4)
  turn on oscillators and creative        ModestSynth.Register()   // BEFORE Load
    effects in a preset
  bounce a Decent Sampler preset offline  SoundFontRenderer.RenderToWavFile(
                                              instrument, sequence, path, rate)
  keep a preset's auxiliary pairs out     player.DropAuxiliaryOutputs = true
    of the main mix
  take the auxiliary pairs separately     ((IMultiOutputRenderer)synth)
                                              .RenderWithAuxiliary(...)
  play an MPE clip                        player.MpeMode = MpeMode.Auto
  see what a format feature costs         DecentSamplerResidualTable.Describe()
  read a .mid that breaks the rules       new MidiFile(path)   // tolerant
                                          file.Problems
  validate a .mid instead of playing it   new MidiFile(path, MidiReadMode.Strict)
  play several tracks as one song         new MultiTrackPlayer()
  play a Suno stems download              SunoStemsLoader.Load(path)
                                          song.CreatePlayer(options)
  swap one part to its MIDI, live         track.ActiveSource = TrackSource.Midi
  line a transcription up with its audio  MidiAudioAlignment.Estimate(...)
                                          track.MidiSourceOffset = result.Offset
  match a synthesized part to the mix     player.AutoSetRelativeTrackLevels = true
                                          await player.LevelMeasurement
  reach one track of a song by name       player["Drums"]
  merge a stems set into one .mid         song.ExportMergedMidi(path)
  bounce a multi-track song to a file     player.RenderToWav(path, 44100)
  bounce a .mid to .wav, no device        SoundFontRenderer.RenderToWavFile(...)
  share a big instrument                  SoundFontCache / SfzInstrumentCache
  analyse audio                           FastFourierTransform / BiQuadFilter /
                                          EnvelopeFollower / VoiceActivityDetector
  add a codec from another package        SharedAudioOutput.RegisterCodecFactory
                                          AudioFileReaderRegistry.Register
  play codec packets from a container     new PacketAudioPlayer()
  decode packets without playing them     SharedAudioOutput
                                              .CreatePacketDecoder(id, priv)
                                          packets.Open(decoder, source, true)
  add a packet codec from another package SharedAudioOutput
                                              .RegisterPacketCodecFactory
  re-order or remove packet codecs        engine.SetPacketCodecPriority /
    (on an AudioEngine of your own)           .UnregisterPacketCodecFactory /
                                              .GetRegisteredPacketCodecs
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
    music.Load(DecentSamplerInstrument instrument, MidiSequence sequence)
    music.Load(IMidiSynthesizer synthesizer, MidiSequence sequence)
    music.Load(Func<int, IMidiSynthesizer> factory, MidiSequence sequence)
    music.DropAuxiliaryOutputs = true                   // MidiMusicPlayer
    music.MpeMode = MpeMode.Auto / music.MpeMemberBendRange = 48
    music.GetReleaseVelocity(int channel, int key)
    MidiMusicPlayer.SharedDecentSamplerCache            // the path form's cache
    music.SendMidiMessage(int channel, int command, int data1, int data2)
    music.SetChannelVolume(int channel, float volume)   // also Pan, Program
    SoundFontRenderer.Render(SoundFont, MidiSequence,
                             int sampleRate = 44100, TimeSpan tail = default)
    SoundFontRenderer.RenderToWavFile(SoundFont, MidiSequence, string outputPath,
                             int sampleRate = 44100, TimeSpan tail = default)
    SoundFontRenderer.RenderToWavStream(...)            // same, to a Stream
    cache.Get(string path)                  // SoundFontCache / SfzInstrumentCache
                                            //   / DecentSamplerInstrumentCache
    DecentSamplerInstrument.Load(string path)           // also (path, options)
    DecentSamplerInstrument.Load(string path, DecentSamplerLoadOptions options)
    DecentSamplerContainer.Open(string path).FindPresets()
    instrument.Problems / .UnsupportedFeatures / .MemoryPolicySummary
    instrument.Controls / .GetControl(string name) / .TagStates
    control.SetValue(double) / control.Select(int) / control.Select(string)
    new DecentSamplerSynthesizer(DecentSamplerInstrument instrument, int rate)
    new DecentSamplerSynthesizerSettings(int rate) { StreamingMode = ... }
    synthesizer.StreamingMode = DecentSamplerStreamingMode.Offline  // for a render
    SoundFontRenderer.Render(DecentSamplerInstrument, MidiSequence,
                             int sampleRate = 44100, TimeSpan tail = default)
    SoundFontRenderer.Render(IMidiSynthesizer, MidiSequence, TimeSpan tail = default)
    ModestSynth.Register()                  // the add-on, before any Load
    DecentSamplerExtensions.RegisterOscillator(string waveform, factory)
    DecentSamplerExtensions.RegisterEffect(string type, factory)
    DecentSamplerSupportedFeatures.StatusOf(category, owner, name)
    DecentSamplerResidualTable.Entries / .Describe() / .Explain(feature)
    new MidiFile(string path, MidiReadMode readMode)
    new MidiSequence(string path, MidiReadMode readMode)
    file.Problems / sequence.Problems / sequence.TempoMap / sequence.TextMetas
    reader.GaplessTrimming = false          // Mp3FileReader, before the first read
    new MultiTrackPlayer()
    player.Add(new AudioTrack(string filePath, string name = null))
    player.Add(new MidiTrack(MidiSequence sequence,
                             Func<int, IMidiSynthesizer> factory,
                             string name = null))
    track.SetMidiSource(MidiSequence sequence, Func<int, IMidiSynthesizer> factory)
    track.ActiveSource = TrackSource.Midi
    track.MidiSourceOffset = TimeSpan.FromMilliseconds(17.7)
    player["Drums"]                         // throws if absent, naming the rest
    player.FindTrack(string name)           // null if absent
    player.TryGetTrack(string name, out PlayerTrack track)
    player.MeasureRelativeTrackLevelsAsync()    // MeasureRelativeTrackLevels()
                                                //   is the blocking form
    player.LevelMeasurement                 // never null; completed until one runs
    player.Render(int sampleRate = 44100, TimeSpan? tailLength = null)
    player.RenderToWav(string path, int sampleRate = 44100)
    player.ExportMergedMidi(string path)
    MidiAudioAlignment.Estimate(IReadOnlyList<double> noteOnTimesSeconds,
                                ReadOnlySpan<float> monoAudio, int sampleRate,
                                double maxOffsetSeconds = 0.3)
    MidiAudioAlignment.GetNoteOnTimesSeconds(MidiSequence sequence)
    SunoStemsLoader.Load(string path)       // also (path, SunoLoadOptions)
    SunoStemsLoader.LoadAsync(string path, CancellationToken ct = default)
    song.CreatePlayer()                     // also (instrumentFactory) and (options)
    song.CreatePlayer(Func<SunoStem, int, IMidiSynthesizer> instruments)
    song.ExportMergedMidi(string path)
    song["Drums"].AlignmentOffset           // settable; audio = midi + offset
    song.ClearCache() / SunoStemsLoader.ClearCache()
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
    packets.Open(IPacketSoundDecoder decoder, IAudioPacketSource source,
                 bool leaveOpen = false)               // decoder you already have
    packets.Play() / packets.Pause() / packets.Stop() / packets.Dispose()
    packets.Seek(TimeSpan firstPacketTimestamp, TimeSpan preRoll = default)
    packets.Position                                   // the audio clock
    packets.IsOpen                                     // a feed is open
    packets.PlaybackState                              // CodeBrix.Audio.Wave
    packets.SampleRate / packets.Channels              // the CODEC's, 0 if closed
    packets.Volume                                     // 1.0 = unity gain
    packets.PlaybackEnded                              // event, off the audio thread
    SharedAudioOutput.RegisteredPacketCodecFactories   // what YOU registered
    engine.UnregisterPacketCodecFactory(string factoryId)     // your OWN engine
    engine.SetPacketCodecPriority(string factoryId, int newPriority)
    engine.GetRegisteredPacketCodecs(string codecId)
    engine.CreatePacketDecoder(string codecId, ReadOnlyMemory<byte> codecPrivate,
                               AudioFormat? hint = null)
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
   11. MIDI reading is TOLERANT by default and reports what it could not honour
       in .Problems instead of throwing. Ask for MidiReadMode.Strict when you
       want a file validated rather than played.
   12. A synthesizer factory must return a NEW synthesizer every time. Share the
       SoundFont, never the synthesizer.
   13. audioTime = midiTime + offset. A positive alignment offset means the audio
       LAGS the MIDI, so the MIDI is what gets delayed - through
       MidiSourceOffset, which moves the rendition and leaves the recording where
       the mix put it.
================================================================================
