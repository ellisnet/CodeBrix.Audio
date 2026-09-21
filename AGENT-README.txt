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

It also carries two naming seams. INSTRUMENT LIBRARIES let an application say
what its music sounds like by NAME, so the whole sound changes by naming a
different library; this package ships the seam and NO instruments, so a consumer
registers what it wants to hear. The AUDIO-FILE WRITER registry does the same
for output, so an offline render goes to whatever format the file name asks for.

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
      over.
      IT ALSO CARRIES THE INSTRUMENTS. GeneralMidiSynthesizer plays any .mid
      with no configuration at all, and GeneralMidiInstrumentLibrary.Register()
      puts the complete General MIDI sound set - all 128 programs and the
      47-note percussion kit, synthesized - into the instrument registry
      described under "INSTRUMENT LIBRARIES" below, under the name
      "ModestSynthGm". CodeBrix.Audio ships no instruments of its own, so with
      nothing registered nothing sounds; this is the one line that fixes that.
      Built and published from THIS repository at the same version as
      CodeBrix.Audio, so the two always match. Its guide is at
      https://github.com/ellisnet/CodeBrix.Audio/blob/main/src/CodeBrix.Audio.ModestSynth/AGENT-README.txt

  Any other codec package built on the seams described in ADDING A CODEC FROM
  ANOTHER PACKAGE below plugs in the same way.


KEY NAMESPACES / USINGS
=======================
  using CodeBrix.Audio.Wave;       // readers/writers, WaveFormat, MP3 frames, ID3,
                                   //   playback (WaveOutEvent, SharedAudioOutput)
                                   //   and the audio-file WRITER seam
                                   //   (AudioFileWriterRegistry) — see "WRITING A
                                   //   RENDER TO A FILE"
  using CodeBrix.Audio.Playback;   // media player (AudioFilePlayer), one-shot
                                   //   sound effects (SoundEffectClip), the
                                   //   multi-track song player
                                   //   (MultiTrackPlayer) and the MIDI/audio
                                   //   alignment estimator
  using CodeBrix.Audio.Playback.Suno;  // loads a Suno stems download into a song
                                       //   the multi-track player plays
  using CodeBrix.Audio.Midi;       // MIDI file read/write + event hierarchy,
                                   //   and the General MIDI sound-set names
  using CodeBrix.Audio.Instruments;// instrument libraries, their coverage and
                                   //   the registry that names them — see
                                   //   "INSTRUMENT LIBRARIES"
  using CodeBrix.Audio.Abc;        // reads abc notation (.abc) and converts a
                                   //   tune to the MIDI model — see "READING ABC
                                   //   NOTATION"; on its own it is a complete
                                   //   abc-to-MIDI file converter — see
                                   //   "CONVERTING ABC TO A MIDI FILE"
  using CodeBrix.Audio.Dsp;        // FFT, biquad filters, analysis primitives
  using CodeBrix.Audio.Synth;      // SoundFont (.sf2) rendering + MIDI music
                                   //   playback — see "TWO SOUNDFONT PATHS";
                                   //   also RoutingSynthesizer, and the Reverb
                                   //   and Chorus send effects
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

TWO TYPES NAMED FOR MIDI FILES, AND A THIRD FOR MUSIC THAT IS NOT WRITTEN YET.
Same rule, same reason:

  CodeBrix.Audio.Midi.MidiFile      The editable file model. Read it, edit the
                                    event collection, write it back out.
  CodeBrix.Audio.Synth.MidiSequence The immutable decoded sequence. You play it,
                                    and it is finished before you do. Flattened
                                    absolute-time messages; no tracks, no meta
                                    events, no editing, no writing.
  CodeBrix.Audio.Synth.MidiStream   The growing timeline. You play it WHILE it is
                                    still being written, and it keeps an editable
                                    recording of everything appended — see
                                    "PLAYING MIDI THAT IS STILL BEING WRITTEN".

Convert with MidiSequence.FromEvents(MidiEventCollection) — build or edit in the
Midi model, then play it. There is deliberately no reverse conversion: the
sequence has already discarded track structure and non-playable meta events, so
converting back would silently lose them. A stream goes both ways round:
stream.ToMidiEventCollection() for the Midi model, stream.ToSequence() for the
finished sequence.


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



PLAYING MIDI THAT IS STILL BEING WRITTEN
========================================
A MidiSequence is finished before you play it. A MidiStream is not: it is a MIDI
timeline you can play WHILE it is still being written, so a producer can append a
bar at a time while the music is already sounding.

  stream        the growing timeline (CodeBrix.Audio.Synth.MidiStream)
  producer      whatever appends to it — any thread, any pace
  head          where playback has got to, in ticks and in time
  horizon       the latest tick appended so far; how far ahead the head may go
  starved       the head has caught up with the horizon and the stream is not
                complete: playback HOLDS its position and plays silence (plus the
                ring-out of whatever was sounding) until more arrives
  pre-roll      how much has to be written ahead of the head before playback
                starts, and before it starts again after a starvation
  settled rest  music the producer has decided and that has nothing in it:
                AdvanceHorizon carries the horizon over it, so the head plays
                the silence instead of waiting in it
  late event    an event appended at a tick the head has already passed: it is
                delivered at the next block and counted, never dropped
  complete      the producer's "no more": Append refuses from then on, and
                playback drains to the last event and ends the way a file does
  recording     the editable MidiEventCollection the stream builds as it goes, so
                what was heard can be saved as a .mid

A COMPLETE EXAMPLE

    using CodeBrix.Audio.Midi;
    using CodeBrix.Audio.Playback;
    using CodeBrix.Audio.Synth;

    var stream = new MidiStream(ticksPerQuarterNote: 480);
    stream.Preroll = TimeSpan.FromSeconds(1);   // wait for a second of music, then go

    using var player = new MidiMusicPlayer();
    player.Load(new SoundFont("piano.sf2"), stream);
    player.Play();                 // playing, and starved: silence until the first bar lands

    // The producer. Any thread; this one writes a bar at a time as it works it out.
    await Task.Run(() =>
    {
        stream.AppendTempo(0, 96);
        stream.Append(new TimeSignatureEvent(0, 3, 2, 24, 8));

        var tick = 0L;
        foreach (var bar in NextBars())                  // your own code
        {
            foreach (var note in bar.Notes)
            {
                // channel 1..16 — this is the CodeBrix.Audio.Midi model, not the
                // synthesizer's 0-based one
                stream.AppendNote(tick + note.Offset, 1, note.Number, 100, note.LengthTicks);
            }

            stream.Append(new ControlChangeEvent(tick, 1, MidiController.Expression, bar.Swell));
            tick += 4 * 480;
        }

        stream.Complete();         // or the player waits, playing silence, for ever
    });

    // Everything that was heard is still here, at its own tick:
    MidiFile.Export("performance.mid", stream.ToMidiEventCollection());

    // ... and the finished piece is an ordinary sequence, for looping or an offline render:
    var sequence = stream.ToSequence();

STARVATION AND THE PRE-ROLL. Every block, the player first delivers everything
the head has reached — including the event AT the horizon, which is every bar's
last note-off — and then moves the head on, never past the horizon. When the head
reaches the horizon the stream is STARVED: the position holds, the synthesizer
keeps rendering (so release tails, and a note whose note-off has not been written
yet, keep sounding), and the state stays Playing. It is playing: it is playing
silence. Preroll is the RESUME threshold: playback starts, and starts again after
an underrun, once that much is written ahead of the head. It is not a gap the
head has to keep — once running, playback carries on even when less than a
pre-roll is left, and stops only at a true underrun. Watch player.IsStarved — it
answers from what is written ahead of the head, so it is right the moment a
stream is loaded and not only once playback has begun — and watch
stream.LateEventCount: a producer that keeps arriving late is telling you its
pre-roll is too small.

A REST IS MUSIC: DECLARE IT WITH AdvanceHorizon. The horizon is the latest tick
APPENDED, and a bar of silence has nothing to append - so a producer that has
composed a rest and written nothing leaves the head starving at the last note,
waiting through the rest instead of playing it. stream.AdvanceHorizon(tick) is
the producer saying "everything up to here is settled, and there is simply
nothing in it":

    stream.AppendNote(tick, 1, 60, 100, 240);
    tick += 4 * 480;                  // a bar of rest follows
    stream.AdvanceHorizon(tick);      // ... and it is DECIDED, not merely absent

The head then walks through that silence exactly as it walks through written
music, the pre-roll is satisfied by it, and Duration / HorizonTime grow with it.
It only ever RAISES the horizon (a tick at or behind it does nothing), it is
refused after Complete() exactly as Append is, it creates no event - EventCount
does not move - and it leaves the RECORDING alone, so a declared rest is not in
the .mid you save. A COMPLETED stream whose horizon lies past its last event
PLAYS THE TRAILING REST OUT and ends at the horizon: a piece that ends in silence
ends when the silence does. A stream you never call it on behaves exactly as it
always did.

IT IS BETTER THAN A PLACEHOLDER EVENT. Appending an empty text meta event at the
far tick also moves the horizon, and it puts a marker nobody asked for into the
saved file and moves the tick the recording's conductor track ends at, which the
playback clock steps through. AdvanceHorizon does neither.

TICKS AND TIME, BOTH WAYS. stream.TimeAtTick(tick) and stream.TickAtTime(time)
convert through the stream's own tempo map, under its own lock, safely while the
piece is playing and while the producer is appending. Both are defined BEYOND the
horizon - the last tempo carries on - so a producer can ask where a bar it has
not written yet will fall. They are exact inverses of each other, and they depend
on the MUSIC alone: tempo changes retime them, and markers, text and key
signatures do not, however many of them a producer writes. (The playback clock
takes one further step, through the tick the conductor track ends at, because a
merged MIDI file does; the two therefore differ by at most a single
hundred-nanosecond TimeSpan tick, and HorizonTime remains the value that equals
ToSequence().Length exactly.)

COMPLETE() OR IT WAITS FOR EVER. A producer that simply stops writing leaves the
player playing, and starved, with no end in sight — PlaybackEnded never fires.
That is deliberate: a stream cannot tell "nothing more yet" from "nothing more".
Call stream.Complete() when the music is finished (player.Stop() is the other way
out). PlaybackEnded is then raised exactly as it is for a sequence: after the
last event has played and the last voice has finished.

CHANNELS ARE 1-BASED ON A STREAM. Append takes the CodeBrix.Audio.Midi event
model, whose channels run 1..16, and that is the only convention on MidiStream's
surface. The 0-based form still belongs to the synthesizer, so SendMidiMessage,
the two message hooks and ProcessMidiMessage are unchanged: 0..15 there.

ONE PLAYER PER STREAM AT A TIME. Loading a stream that another player is already
playing throws InvalidOperationException. Dispose (or load something else into)
the first player and the stream is free again. player.Stop() does NOT hand it
over — it rewinds it: a stream keeps everything written so far, so the piece can
be heard again from the top, mid-production or after.

ISLOOPING IS IGNORED FOR A STREAM. A growing timeline has no end to loop at.
Setting it neither throws nor is refused — it is a property of the PLAYER and
keeps its value for the next sequence you load. To loop a finished piece, play
stream.ToSequence() the ordinary way.

DURATION GROWS. player.Duration is the horizon so far, so it moves as the
producer writes and settles only once the stream has been completed. A progress
bar built on it has a moving end until then. player.Position holds while starved.
Seeking past the horizon clamps to it.

DON'T APPEND FROM A HOOK. MidiMessageProcessed and MidiMessageFilter run on the
audio thread, and for a stream they run under the stream's own lock as well.
Appending from one is re-entrant rather than deadlocked, but it is still the
audio thread doing composition work. Hand the message to your own thread, as the
hooks' own rules already say.

LATE EVENTS ARE DELIVERED, NEVER DROPPED. An event appended at a tick the head
has already passed is played at the head on the very next block and counted in
LateEventCount — a late note-off is precisely the event that must not be lost.
The RECORDING keeps its original tick, so what you save is the composition as it
was meant, not as it was heard.

WHAT THE RECORDING LOOKS LIKE. ToMidiEventCollection() is a type 1 collection at
the stream's own resolution: track 0 is the conductor track (tempo, time
signature, key signature, text, markers, sysex) and then one track per channel
that has received a channel message, in the order the channels were first used. A
stream with no conductor events has no conductor track — empty tracks are removed
when the snapshot is prepared for export. It is a snapshot taken under the
stream's lock, safe at any time from any thread, including while the piece is
still being written and played.

NOT MIDI INPUT. This plays a timeline that your own code writes. It is not a MIDI
device input: this package reads no hardware port, and a stream adds none.



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


INSTRUMENT LIBRARIES
====================
An INSTRUMENT LIBRARY is a NAMED set of instruments, addressed the way MIDI
addresses them - by General MIDI program number, and by note number on the
percussion channel - that hands back an IMidiSynthesizer on demand. It keeps
"what this music sounds like" apart from "what notes it plays", so an
application changes its whole sound by naming a different library rather than by
rewriting the code that plays the notes.

CODEBRIX.AUDIO SHIPS NO INSTRUMENTS AND REGISTERS NOTHING. The registry starts
EMPTY, and until a consumer fills it nothing can be played or rendered through
this seam. That is deliberate - which instruments an application uses is the
application's decision, never a library's - and one line settles it:

    using CodeBrix.Audio.Instruments;
    using CodeBrix.Audio.ModestSynth;        // the add-on package

    GeneralMidiInstrumentLibrary.Register();      // registers as "ModestSynthGm"

That gives the whole General MIDI Level 1 sound set - all 128 programs and the
47-note percussion kit, synthesized - with no SoundFont to find and nothing to
download. Ask the registry for anything while it is still empty and you get an
InvalidOperationException whose message names that very call:

    No instrument library is registered, so no music can be played or rendered.
    Register the General MIDI library provided by CodeBrix.Audio.ModestSynth -
    call GeneralMidiInstrumentLibrary.Register() - or register another
    instrument library with InstrumentLibraryRegistry.Register.

TWO SHAPES, AND A LIBRARY MAY OFFER EITHER OR BOTH. It says which with
SupportsPerPart and SupportsMultiTimbral; asking for a shape it does not offer
throws NotSupportedException, naming the library and the shape it does have.

  PER-PART        CreateSynthesizer(program, sampleRate) and
                  CreatePercussionSynthesizer(sampleRate) hand back ONE
                  synthesizer per voice. This is what a voiced arrangement
                  needs, because a per-part gain and a layered second instrument
                  both require separate synthesizers - one multi-timbral
                  synthesizer mixes internally, at one level. The parts are then
                  played together through a RoutingSynthesizer (next section).
                  A per-part synthesizer is PINNED: it ignores program change
                  and bank select, because the CALLER decided what this part
                  sounds like and a program change in the music must not quietly
                  undo that. It sounds on ANY channel it is given, so a router
                  may put it wherever the music put the part.

  MULTI-TIMBRAL   CreateMultiTimbralSynthesizer(sampleRate) hands back ONE
                  synthesizer that honours the program changes the music carries
                  and treats channel 10 as percussion. This is the "play this
                  .mid with no configuration" case.

COVERAGE SAYS WHAT IS REALLY THERE, which matters most for sampled libraries: an
instrument recorded from C3 up plays nothing below it, and that is silence in
the middle of an arrangement rather than an exception. A synthesized bank
usually covers everything; a library built out of sample packs usually does not,
so ask before you voice a part with one.

    if (!library.Coverage.CoversNote(73, 48))      // flute, a low C
    {
        // nothing down there - voice that part differently, or transpose it
    }

  coverage.Programs / .PercussionNotes   what is covered, ascending
  coverage.CoversProgram(program)        is that program there at all
  coverage.CoversPercussionNote(note)    is that kit piece there
  coverage.KeyRangeOf(program)           an InstrumentKeyRange - LowestKey,
                                         HighestKey, KeyCount, IsEmpty,
                                         Contains(key), UnionWith(other), and
                                         "35-81" or "(none)" from ToString
  coverage.CoversNote(program, note)     both questions at once
  InstrumentCoverage.General             all 128 programs over the whole
                                         keyboard plus percussion 35-81 - what a
                                         complete General MIDI library reports
  InstrumentCoverage.None                nothing at all

A library that does not track key ranges reports the whole keyboard for every
program it covers, which is the right answer for a synthesized bank.

SO THE CHECK ONLY BITES ON A SAMPLED LIBRARY, and knowing which kind you have is
the difference between a useful check and a check that always passes. A
SYNTHESIZED bank - one that builds its sound from oscillators - covers 0 to 127
on every program it has, and CoversNote is then true for everything. A library
built from a SoundFont or from sample packs reports the ranges its samples really
cover, program by program, and those are often narrower than the music: an
instrument recorded from C3 up plays nothing below it. Ask before voicing a part
with a sampled library; with a synthesized one the answer is free and always yes.

THE REGISTRY, AND ITS RULES. InstrumentLibraryRegistry is static and
process-wide, exactly as AudioFileReaderRegistry is.

  - Every library registers under a UNIQUE NAME, matched case-insensitively.
  - Registering the SAME library instance again is a NO-OP. That is what makes a
    package's Register() safe to call on every start-up path.
  - A DIFFERENT library under a name already taken is an error.
  - THE FIRST LIBRARY REGISTERED IS THE DEFAULT. There is no priority and no
    other ordering.
  - SetDefault(name) makes any registered library the default instead.
  - Resolve(name) asks for a particular one. A name that is not registered is an
    error listing what IS registered.

      InstrumentLibraryRegistry.Register(library)
      InstrumentLibraryRegistry.Registered        every library, in registration
                                                  order
      InstrumentLibraryRegistry.RegisteredNames   their names, same order
      InstrumentLibraryRegistry.IsRegistered(name)  asks without throwing
      InstrumentLibraryRegistry.Resolve(name)
      InstrumentLibraryRegistry.Default           the first registered, unless
                                                  SetDefault moved it
      InstrumentLibraryRegistry.DefaultName       null when empty; NEVER throws
      InstrumentLibraryRegistry.SetDefault(name)

  THE DEFAULT DEPENDS ON REGISTRATION ORDER, so in an application that registers
  more than one it depends on which start-up path ran first. Code that cares
  which library it gets NAMES it:

      var library = InstrumentLibraryRegistry.Resolve("ModestSynthGm");

  Every registry member is safe to call from several threads at once. The
  SYNTHESIZERS a library hands back are not: IMidiSynthesizer is single-threaded
  by contract, here as everywhere else in this package.

BRING YOUR OWN SOUNDFONT - SoundFontInstrumentLibrary
  A .sf2 becomes a named library in one line. It is code, not content: this
  package ships no .sf2 of its own.

      new SoundFontInstrumentLibrary("MyBank", "The bank my game ships with",
                                     "bank.sf2").Register();

  It takes a path, a Stream or an already-loaded SoundFont, offers BOTH shapes,
  and holds ONE SoundFont that every synthesizer it creates shares - five parts
  never mean five copies of a large file in memory. Its .SoundFont property is
  that shared instance. Coverage is read from the file's own presets and
  instrument regions, so the key ranges it reports are the ranges that really
  sound; melodic programs come from bank 0 and percussion from the drum bank.

SWAPPING VOICES ONE AT A TIME - MappedInstrumentLibrary
  This is the shape of an ordinary working session, and this library exists for
  it. START from a General MIDI library you already have - "ModestSynthGm", or a
  SoundFont library of your own. LISTEN to the piece. Then REPLACE ONE VOICE
  with an instrument of your own - a Decent Sampler pack, an .sfz, one program
  of another library, or a synthesizer you wrote - and listen again. Everything
  you have not replaced keeps coming from where it came from.

    using System;
    using CodeBrix.Audio.Instruments;
    using CodeBrix.Audio.Midi;
    using CodeBrix.Audio.ModestSynth;
    using CodeBrix.Audio.Synth;

    // 1. START FROM A GENERAL MIDI LIBRARY.
    GeneralMidiInstrumentLibrary.Register();          // "ModestSynthGm"

    var voices = new MappedInstrumentLibrary(
        "MyVoices",
        "ModestSynthGm, with a few voices of my own",
        "ModestSynthGm");                             // resolved on first use
    voices.Register();                                // "MyVoices" resolves too

    var piece = new MidiSequence("piece.mid");

    // Hear it as it stands: every voice is ModestSynthGm's.
    SoundFontRenderer.RenderToFile(
        voices.CreateMultiTimbralSynthesizer(44100), piece, "take-1.wav",
        TimeSpan.FromSeconds(2));

    // 2. SWAP ONE VOICE, AND LISTEN AGAIN.
    voices.SetInstrument(
        GeneralMidiProgram.ChoirAahs,                 // program 52
        "/home/me/packs/Whisper Choir/Whisper Choir.dspreset");

    SoundFontRenderer.RenderToFile(
        voices.CreateMultiTimbralSynthesizer(44100), piece, "take-2.wav",
        TimeSpan.FromSeconds(2));

    // Everything except Choir Aahs is what take 1 was. Only program 52 moved.

    // 3. SWAP ANOTHER ONE.
    voices.SetInstrument(GeneralMidiProgram.AcousticBass, "/home/me/sfz/upright.sfz");

    // 4. CHANGE YOUR MIND, OR GO FURTHER.
    voices.ClearInstrument(GeneralMidiProgram.AcousticBass);   // back to the base

    voices.SetInstrumentFromSoundFont(GeneralMidiProgram.Flute, "my-bank.sf2",
                                      soundFontProgram: 73);
    voices.SetInstrumentFromLibrary(GeneralMidiProgram.Celesta, "FluidR3Gm",
                                    libraryProgram: 8);
    voices.SetPercussion("/home/me/packs/My Kit/kit.dspreset");

    // 5. AND IT KNOWS WHAT IT CAN PLAY.
    if (!voices.Coverage.CoversNote((int)GeneralMidiProgram.ChoirAahs, 36))
    {
        // That choir is sampled from C3 up. This is how you find out without
        // listening for a hole in the arrangement.
    }

  WHAT IT IS MADE OF. The BASE LIBRARY is what plays the voices you have not set
  yourself - a starting point, not a consolation prize - given as an instance or
  as a registry NAME that is resolved on FIRST USE, so you may name a library
  that is registered later. The other side of that: reading Coverage,
  SupportsPerPart or SupportsMultiTimbral on a library whose named base is still
  not registered throws the registry's own error, exactly as creating a
  synthesizer would. With no base at all, only what you set will play:
  CreateSynthesizer for an unset program then throws a message that says what to
  do, while the multi-timbral shape leaves that channel silent rather than
  stopping the music.

    voices.BaseLibraryName / .HasBaseLibrary      null and false when there is none
    voices.SubstitutedPrograms                    what you have replaced, ascending
    voices.HasSubstitute(program) / .HasPercussionSubstitute

  FOUR WAYS TO SET A VOICE, each taking an int program or a GeneralMidiProgram,
  and each with an optional InstrumentKeyRange when you want to state the range
  rather than let the file say:

    SetInstrument(program, sampleRate => new MySynthesizer(sampleRate))
    SetInstrument(program, "path")   .dspreset / .dslibrary / .dsbundle / a
                                     Decent Sampler library FOLDER / .sfz / .sf2
    SetInstrumentFromSoundFont(program, "bank.sf2", soundFontProgram)
    SetInstrumentFromLibrary(program, library or "LibraryName", libraryProgram)

  and the same four for the whole kit - SetPercussion, SetPercussionFromSoundFont,
  SetPercussionFromLibrary - plus ClearInstrument(program) and ClearPercussion().
  A file type it cannot load is refused ON THE LINE THAT NAMED IT:

    '.wav' is not an instrument file this library knows how to load. It
    understands .dspreset, .dslibrary and .dsbundle (Decent Sampler), a Decent
    Sampler library folder, .sfz (SFZ) and .sf2 (SoundFont). Anything else is
    one line of your own: SetInstrument(program, sampleRate => new
    MySynthesizer(sampleRate)).

  FOUR THINGS TO KNOW.
    - A CHANGE TAKES EFFECT FOR SYNTHESIZERS BUILT AFTERWARDS. The multi-timbral
      shape takes a snapshot when it is created, so swapping a voice while a
      piece is already sounding is safe and changes the NEXT render, not this
      one.
    - IT SUBSTITUTES BY PROGRAM - "everything that plays Choir Aahs". Replacing
      ONE PART when two parts share a program is a per-CHANNEL question, and
      that is RoutingSynthesizer.SetChannel's job (next section).
    - EVERY SUBSTITUTE IS PINNED, so a sampled instrument that answers to
      program change of its own cannot re-voice itself out from under you. The
      synthesizer you get back is therefore not necessarily the type your
      factory built: a cast to DecentSamplerSynthesizer will not succeed, and
      the MPE and multi-output surfaces those types implement are not reachable
      through it. Keep hold of what your own factory built if you need them.
    - THE PERCUSSION CHANNEL FOLLOWS THE KIT you set here, whatever program is
      selected on it. With a kit substitution in place, a melodic part written
      onto channel 10 sounds as drums.

  A file is loaded WHERE YOU WROTE THE PATH, not lazily at play time, so a typo
  is an error on that line. One loaded instrument serves every program taken
  from it, and a loaded instrument is held for the life of the library.


ROUTING THE PARTS OF AN ARRANGEMENT
===================================
RoutingSynthesizer is an IMidiSynthesizer that is really SIXTEEN of them: each
MIDI channel gets its own child synthesizer, its own gain, and optionally a
second child LAYERED on top of it. Everything that drives one synthesizer drives
a router - MidiSequencer, MidiStreamSequencer, MidiMusicPlayer and
SoundFontRenderer - so one voiced arrangement plays live, follows a MidiStream
that is still being written, and renders offline through exactly the same code.

    using CodeBrix.Audio.Instruments;
    using CodeBrix.Audio.Midi;
    using CodeBrix.Audio.Synth;

    var library = InstrumentLibraryRegistry.Resolve("ModestSynthGm");

    var router = new RoutingSynthesizer(44100);
    router.SetChannel(1, library.CreateSynthesizer(8, 44100), gain: 0.8F);   // celesta
    router.SetChannel(2, () => library.CreateSynthesizer(89, 44100), 0.5F);  // lazy
    router.SetLayer(2, () => library.CreateSynthesizer(52, 44100), 0.3F);    // doubled
    router.SetChannel(GeneralMidi.PercussionChannel,
                      library.CreatePercussionSynthesizer(44100));
    router.MasterVolume = 0.9F;

    // Play it now - the sequencer drives it as ONE synthesizer, so it follows a
    // stream that is still being written:
    var sequencer = new MidiStreamSequencer(router);

    // Or render it offline - the same router, no device:
    var samples = SoundFontRenderer.Render(router, sequence, TimeSpan.FromSeconds(2));

THE CHANNEL NUMBERING PITFALL, and it is the one to remember here. The ROUTING
TABLE is addressed 1-16 - SetChannel, SetLayer, ClearChannel, ClearLayer,
IsRouted, HasLayer and the gain accessors - the way MidiEvent.Channel and
GeneralMidi.PercussionChannel (which is 10) count. ProcessMidiMessage's channel
is the WIRE number, 0-15, because that is what every sequencer hands every
IMidiSynthesizer. PERCUSSION IS CHANNEL 10 IN THE TABLE AND WIRE CHANNEL 9 IN A
MESSAGE. A channel outside 1-16 in the table throws:

    A MIDI channel is 1 to 16, the way MidiEvent counts them.

Messages are forwarded to the child UNRENUMBERED, so a child keeps its
per-channel controller state where the music put it, and a SoundFont child still
reads wire channel 9 as its drum bank.

  router.SetChannel(channel, synthesizer[, gain])   a child, built now
  router.SetChannel(channel, factory[, gain])       a child, built on first use
  router.SetLayer(channel, synthesizer or factory[, gain])
  router.ClearChannel(channel)        drops the channel AND its layer, at once
  router.ClearChannel(channel, ringOut)             ... or lets them ring out
  router.ClearLayer(channel[, ringOut])
  router.IsRouted / .HasLayer(channel)
  router.GetChannel(channel) / .GetLayer(channel)   the child itself, or null
  router.GetChannelGain / .SetChannelGain / .GetLayerGain / .SetLayerGain
  router.MasterVolume                 1.0 by default - unity, so an arrangement
                                      is not quietly re-balanced
  router.RingOutReplacedChildren      true by default - see below
  router.RingOutLimit                 how long a retired child may go on
  router.RetiredChildCount            how many are still ringing out
  router.ActiveVoiceCount             summed over the children that exist AND
                                      whatever is still ringing out
  router.Synthesizers                 only the children already built, and only
                                      the ones in the table - never a retired one
  router.UnroutedMessageCount         how many messages arrived for a channel
                                      with no instrument on it
  RoutingSynthesizer.ChannelCount (16) / .DefaultBlockSize (64)
  RoutingSynthesizer.DefaultRingOutLimit (ten seconds)

A REPLACED CHILD RINGS OUT. Routing something new onto a slot that already holds
a built child - a part re-voiced in the middle of a piece, a follow-up prompt
whose music puts other instruments on the same channels - does NOT cut the old
child off. It is RETIRED: released (a note-off-all that lets the notes GO of the
key, not an all-sound-off that stops them dead), sent no further messages, and
KEPT IN THE MIX at the gain it had until it has nothing left to say. So a held
note decays through its release and a reverb tail finishes, instead of the part
vanishing mid-note at the seam.

  - "NOTHING LEFT TO SAY" is the child's ActiveVoiceCount reaching zero AND its
    output falling silent, both. A child carrying a reverb is still audible after
    its last voice has ended, so the voice count alone would cut the tail off.
  - RingOutLimit is the backstop: ten seconds unless you change it, after which a
    retired child is let go whatever it claims to still be sounding. Nothing can
    cost CPU for ever. It is read per block, so lowering it releases children that
    are already retired; TimeSpan.Zero lets each go at the next block.
  - RetiredChildCount counts them, and ActiveVoiceCount includes their voices.
    Synthesizers does not list them - that is the routing TABLE, which they have
    left.
  - NoteOffAll(immediate: true) and Reset() DROP every retired child at once -
    both are asking for silence now. NoteOffAll(immediate: false) leaves them
    alone: each was released when it was retired.
  - SET RingOutReplacedChildren = false FOR THE OLD BEHAVIOUR, where a replaced
    child leaves the mix at the moment the new one takes the slot.

  router.SetChannel(1, warmPad, 0.8F);
  // ... the pad is sounding a held chord ...
  router.SetChannel(1, brightLead, 0.8F);   // the pad releases and rings out
  router.RetiredChildCount;                 // 1, until the pad has finished

CLEARING IS STILL IMMEDIATE. ClearChannel(channel) and ClearLayer(channel) mean
what they always meant - the channel goes silent - whatever
RingOutReplacedChildren says. Ask for a ring-out by name if you want one:
ClearChannel(channel, ringOut: true), and that request is honoured even with the
switch off. The switch governs REPLACEMENT.

THE SAME INSTANCE BACK INTO ITS OWN SLOT IS A GAIN UPDATE, not a replacement:
nothing is retired, the child hears no note-off, and the new gain simply applies.
Putting it into a DIFFERENT slot is still the error it always was. A RETIRED
instance routed again COMES OUT OF RETIREMENT - it goes back into the table and
is rendered once, from there.

READING THE TABLE BACK. GetChannel(channel) and GetLayer(channel) hand back the
child itself. Both are null when the slot is empty AND when a factory is routed
there whose child has not been built yet; IsRouted / HasLayer tell those two
apart, and the child appears as soon as the channel is first played.

FIVE RULES IT ENFORCES OR RELIES ON:

  - EVERY CHILD SHARES THE ROUTER'S SAMPLE RATE. One that does not is refused by
    the setter with an ArgumentException:
        This router renders at 44100 Hz and the synthesizer renders at 48000 Hz.
        Every child of a router must share its sample rate.
    A lazy factory that builds one at the wrong rate says the same thing when it
    is first called, as an InvalidOperationException - as does a factory that
    returns null.
  - ONE SYNTHESIZER INSTANCE FILLS ONE SLOT. A child is rendered once per block
    at one gain, so an instance in two slots has no single answer to either:
        That synthesizer is already routed to another channel or layer. A router
        renders each child once per block at one gain, so an instance may fill
        only one slot; create a second synthesizer for the second part.
  - A MESSAGE FOR A CHANNEL WITH NO INSTRUMENT IS DROPPED SILENTLY and counted
    in UnroutedMessageCount. A piece carrying a part nobody voiced must not stop
    the music; the counter is how a diagnostic notices.
  - A LAZY CHILD IS BUILT WHEN THE MUSIC FIRST PLAYS ON ITS CHANNEL, not when it
    is routed and not when the router renders - so a large sampled library pays
    only for the parts the music actually uses.
  - BlockSize IS FIXED AT CONSTRUCTION (64 unless you say otherwise) and does
    not follow the children. Children with any block size mix correctly.
    Reset() clears the unrouted count and every child's state but NOT the
    routing table: routing is configuration, not state.


REVERB AND CHORUS
=================
Reverb and Chorus (CodeBrix.Audio.Synth) are the two SEND effects the SoundFont
renderer uses, and they are public so a synthesizer of your own can use the same
ones rather than carrying a second reverb.

    var reverb = new Reverb(44100);           // RoomSize, Damp, Wet, Width
    var chorus = new Chorus(44100, 0.002, 0.0019, 0.4);   // delay, depth, rate

BOTH ARE SENDS, NOT INSERTS. You give them the send bus - everything you want
wet, summed and scaled - and they write the WET SIGNAL ALONE, OVERWRITING the
output buffers you pass. Adding the wet back to the dry is the caller's job.

    reverb.Process(sendBus, wetLeft, wetRight);           // or (..., count)
    chorus.Process(sendLeft, sendRight, wetLeft, wetRight);

Reverb takes a MONO send and writes stereo; Chorus takes a stereo send and
writes stereo. Reverb.InputGain is the scale every source should be multiplied
by on its way into the bus, which is how the reverb stays at a sane level
however many voices feed it. Mute() clears the tails - use it when a transport
stops, so the next piece does not start inside the last one's room.

SoundFontSynthesizer drives both from CC 91 and CC 93, and ModestSynth's General
MIDI synthesizer drives the same two classes the same way, which is why a piece
sounds like it is in one room whichever of them plays it.


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
  - AudioFileWriterRegistry : writing BY FILE NAME, the mirror of
                            AudioFileReaderRegistry. Register / Supports /
                            SupportedExtensions / Resolve / Create. .wav
                            (32-bit float by default) and .aif / .aiff (16-bit
                            PCM) are built in; another package adds its own
                            format from its own Register() call. See "WRITING A
                            RENDER TO A FILE".
  - IAudioFileWriter      : what a format writes through - Write(float[],
                            offset, count), Write(ReadOnlySpan<float>),
                            SamplesWritten, WaveFormat, Finish(). It NEVER
                            closes the stream it was handed.
  - IAudioFileWriterFactory : what a format registers - Extensions,
                            RequiresSeekableStream, DefaultFormat(rate,
                            channels), Create(stream, format).
  - WavAudioFileWriterFactory / AiffAudioFileWriterFactory : the two built in,
                            each taking a default bit depth in its constructor.

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
                            TempoEvent, TimeSignatureEvent, etc. NoteOnEvent
                            carries its paired note-off (OffEvent, NoteLength) -
                            move the pair with noteOn.MoveTo(tick), because
                            assigning AbsoluteTime moves only the start; see
                            "COMMON PITFALLS TO AVOID".
  - MidiEventCollection   : per-track event collection used for read and write.
  - MidiReadMode          : Tolerant (the default everywhere) or Strict. The one
                            option both MIDI readers share.
  - GeneralMidiProgram    : the 128 General MIDI Level 1 sounds as an enum, valued
                            0..127 the way a program change carries them, so
                            (int)GeneralMidiProgram.Violin IS the patch number.
  - GeneralMidiPercussion : the GM Level 1 drum kit, valued 35..81 - the note
                            numbers that select a drum on the percussion channel.
  - GeneralMidiProgramFamily : the sixteen families of eight the patch map is
                            grouped into (Piano ... SoundEffects).
  - GeneralMidi           : the names, exactly as the MIDI Association publishes
                            them - DisplayName(program/percussion/family) - plus
                            FamilyOf(program) and PercussionChannel (10, in this
                            library's 1-based channel numbering; a status byte on
                            the wire carries the same channel as 9).

Instrument libraries (CodeBrix.Audio.Instruments) — see "INSTRUMENT LIBRARIES"
above. This package ships the seam and registers NOTHING:
  - IInstrumentLibrary    : a named set of instruments - Name, Description,
                            Coverage, SupportsPerPart, SupportsMultiTimbral,
                            CreateSynthesizer(program, rate),
                            CreatePercussionSynthesizer(rate),
                            CreateMultiTimbralSynthesizer(rate).
  - InstrumentLibraryRegistry : the process-wide map from name to library.
                            Register, Registered, RegisteredNames, IsRegistered,
                            Resolve, Default, DefaultName, SetDefault. THE FIRST
                            LIBRARY REGISTERED IS THE DEFAULT; ask by name when
                            it matters.
  - InstrumentCoverage / InstrumentKeyRange : which programs, which percussion
                            notes, and over which part of the keyboard.
                            InstrumentCoverage.General is a complete General
                            MIDI library's answer.
  - SoundFontInstrumentLibrary : any .sf2 as a named library, in one line, with
                            ONE SoundFont shared by every synthesizer it makes.
  - MappedInstrumentLibrary : a base library plus voices of your own, set and
                            cleared ONE AT A TIME - a Decent Sampler pack, an
                            .sfz, one program of another library, or a factory
                            of your own. SetInstrument / SetInstrumentFromSoundFont
                            / SetInstrumentFromLibrary / ClearInstrument and the
                            same for the kit, plus BaseLibraryName,
                            SubstitutedPrograms, HasSubstitute and Register.

ABC:
  - AbcReader             : reads abc notation - Parse(text), Read(path),
                            Read(stream) - into an AbcTuneBook. TOLERANT: content
                            problems are listed, never thrown. See below.
  - AbcTuneBook           : the tunes one file or one piece of text held, plus
                            file-level Problems.
  - AbcTune               : one tune as the text wrote it - ReferenceNumber,
                            Titles, Composer, Meter, UnitNoteLength, Tempo, Key,
                            Voices, Problems. Repeats are NOT unrolled here.
  - AbcVoice / AbcBar     : a voice's bars, and what each bar holds: AbcNote,
                            AbcRest, AbcChord, AbcGraceGroup, AbcTupletGroup,
                            AbcInlineField, AbcProgramChange - with AbcBarLine
                            and the ending numbers around them.
  - AbcDuration           : an exact fraction of a whole note. Every length and
                            every position in the model is one of these, never a
                            double.
  - AbcToMidi             : Convert(tune) / Convert(tune, options) ->
                            MidiEventCollection (type 1, PrepareForExport already
                            applied). Repeats ARE unrolled here.
  - AbcToMidiOptions      : TicksPerQuarterNote (480), DefaultBeatsPerMinute
                            (120), Velocity (100), GraceNoteLength (1/64),
                            VoiceChannels, HonourMidiDirectives (true).

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

READING ABC NOTATION
  Abc notation is music written as plain text: a header of information fields,
  then music code where a letter is a note - "CDEF|GABc|" is a C major scale.
  Sessions, tune collections and folk databases carry hundreds of thousands of
  tunes in it, and one file holds one tune or many.

  Three calls take a .abc file to sound:

    using CodeBrix.Audio.Abc;
    using CodeBrix.Audio.Synth;
    using CodeBrix.Audio.Playback;

    var book = AbcReader.Read("session-tunes.abc");   // does not throw on content
    var midi = AbcToMidi.Convert(book.Tunes[0]);      // MidiEventCollection
    var music = new MidiMusicPlayer();
    music.Load(soundFont, MidiSequence.FromEvents(midi));
    music.Play();

  AbcToMidi.Convert returns the ordinary editable model, with PrepareForExport
  already applied, so the same collection saves as a standard MIDI file:

    MidiFile.Export("session-tunes.mid", midi);

  WHAT IS READ:
    HEADER  X: reference number, T: title (repeatable - the first is the title),
            C: composer, M: meter (n/m, C, C|, none, and a complex numerator such
            as (2+3+2)/8, whose parts are summed), L: unit note length - and when
            there is none, the standard's default from the meter: below 0.75 a
            sixteenth note, at 0.75 or above an eighth, and an eighth for C, C|
            and free meter. Q: tempo in every 2.1 form - "1/4=120", "3/8=50", up
            to four beats that are summed ("1/4 3/8 1/4 3/8=40"), the deprecated
            bare number (unit note lengths per minute), a text label with or
            without a value. K: key - a letter with an optional accidental, a
            mode in full or abbreviated to three letters (major/maj, minor/min/m,
            mixolydian/mix, dorian/dor, phrygian/phr, lydian/lyd, locrian/loc,
            ionian/ion, aeolian/aeo), K:none, and accidentals written after the
            key, with or without "exp". V: voices, by id and name=.
    INLINE  [K:..] [M:..] [L:..] [Q:..] [V:..] inside a line, and the same fields
            on a line of their own inside the body, change the state from that
            point on.
    BODY    notes C D E F G A B c d e f g a b, accidentals ^ ^^ = _ __, octave
            marks ' and , in any number and any order; lengths n, /n, n/m and the
            / and // shorthands; broken rhythm > < >> << >>> <<<; rests z, the
            invisible x, and the multi-measure Z; chords [CEG] with a length
            inside, outside or both; ties -, which merge two notes into one;
            slurs ( ), which change no tick; grace notes {..} and {/..}; tuplets
            (p, (p:q and (p:q:r, with the standard's table for an omitted q;
            bar lines | || |] [| [|] .| |: :| :: and the extra dots that repeat
            a section more than twice; first and second (and further) endings in
            both spellings, [1 and |1, including lists and ranges such as [1,3
            and [1-3; comments %; line continuation \.
    %%MIDI  two of abc2midi's directives, because they are the only common way an
            abc file names an instrument: "%%MIDI program n" and
            "%%MIDI program c n" become a program change at the point they stand,
            and "%%MIDI channel n" puts the voice on that channel. The program
            number is 0 to 127, as abc2midi counts programs - program 0 is the
            acoustic grand piano - which is exactly what GeneralMidiProgram
            names, so (int)GeneralMidiProgram.Violin is the 40 in
            "%%MIDI program 40". The channel is 1 to 16, as abc2midi counts
            channels. "[I:MIDI program n]" is the same directive inline.

  WHAT IS SKIPPED, AND LISTED ONCE: decorations (!..!, +..+ and the shorthands
  ~ . H L M O P S T u v), chord symbols "Am", annotations "^text", lyrics w: and
  W:, parts P:, clefs and transposition, the voice overlay operator &, every
  other information field, and every stylesheet directive other than the two
  %%MIDI ones. Each KIND is named once, never once per occurrence, so a real tune
  full of ornaments produces two lines rather than four hundred.

  VOICES AND CHANNELS: voices take channels in the order they first appear - 1,
  2, 3 ... - SKIPPING CHANNEL 10, which General MIDI reserves for percussion. A
  %%MIDI channel directive overrides that, and AbcToMidiOptions.VoiceChannels
  overrides both; the single voice of a tune with no V: field has an empty id, so
  options.VoiceChannels[""] = 10 is how a one-voice tune is put on the drums.
  More than fifteen voices wrap round, and that is said once.

  GRACE NOTES TAKE THEIR TIME FROM THE NOTE THEY PRECEDE, which is the convention
  every abc-to-MIDI converter follows: each grace lasts
  AbcToMidiOptions.GraceNoteLength (1/64 of a whole note by default), the note
  after them starts that much later and is shortened by the same amount, and the
  graces are squeezed if they would take more than half of it. Graces before a
  rest, or at the very end with nothing after them, have nothing to take time
  from: they sound at their own length and what follows keeps its place.

  ACCIDENTALS follow the standard's DEFAULT rule, %%propagate-accidentals pitch:
  an accidental holds for the same note letter in EVERY octave until the end of
  the bar, and a written natural = cancels both it and the key signature. The
  directive itself is skipped, and listed once.

  TIMING IS TICK-EXACT. Every length and every position is carried as an exact
  fraction of a whole note and converted to ticks once, at the moment an event is
  created, from the accumulated exact position - never by adding up rounded
  ticks. A triplet therefore cannot push the rest of the tune off the beat.
  Where a tuplet does not divide into whole ticks at the chosen resolution, that
  is said once and the notes are rounded to the nearest tick.

  REPEATS ARE UNROLLED in Convert, because MIDI has no repeat marks: |: ... :|
  appears twice in the events, numbered endings select which bars belong to which
  pass, and a :| with no |: before it repeats from the start of the tune, as the
  standard recommends.

  THE PROBLEMS HABIT is the MIDI reader's, unchanged: one human-readable line per
  departure, in the order found; empty means the tune held nothing that could not
  be honoured; capped; NEVER thrown. File-level problems are on
  AbcTuneBook.Problems and a tune's own on AbcTune.Problems, and Convert adds to
  the tune's list the things only the conversion can know. A null argument or a
  missing file DOES throw - those are the caller's mistake, not the content's.

CONVERTING ABC TO A MIDI FILE (NO AUDIO NEEDED)
  This package is a complete abc-to-MIDI converter on its own, and that use has
  NOTHING to do with playing sound: no audio device is opened, no instrument, no
  SoundFont and no sample library is loaded, and nothing native is touched. It
  is three calls from CodeBrix.Audio.Abc and CodeBrix.Audio.Midi, it runs the
  same in a console tool, a server or a build step, and it suits batch work - a
  whole tunebook in one pass. Read "READING ABC NOTATION" above for what the
  reader understands, what it skips, and how voices, repeats, grace notes and
  the %%MIDI directives are treated; everything said there applies here.

    AbcReader.Read(path)        the file -> an AbcTuneBook (one tune or many)
    AbcToMidi.Convert(tune)     one tune -> a MidiEventCollection
    MidiFile.Export(path, midi) the collection -> a standard MIDI file

  A WHOLE TUNEBOOK, ONE .mid PER TUNE:

    using System;
    using System.IO;
    using CodeBrix.Audio.Abc;
    using CodeBrix.Audio.Midi;

    AbcTuneBook book = AbcReader.Read("session-tunes.abc");   // never throws on content
    Directory.CreateDirectory("midi");

    foreach (AbcTune tune in book.Tunes)
    {
        MidiEventCollection midi = AbcToMidi.Convert(tune);
        MidiFile.Export(Path.Combine("midi", $"tune-{tune.ReferenceNumber}.mid"), midi);

        // Read a tune's Problems AFTER converting it: Convert ADDS to the list.
        foreach (string problem in tune.Problems)
        {
            Console.WriteLine($"X:{tune.ReferenceNumber} {tune.Title}: {problem}");
        }
    }

    foreach (string problem in book.Problems)                 // file-level departures
    {
        Console.WriteLine($"(file) {problem}");
    }

  Text that is already in memory goes through AbcReader.Parse(text) instead of
  Read(path); the rest is identical. Name the files however suits you: a tune's
  ReferenceNumber is its X: field, and Title is its first T: field, which may be
  empty and may hold characters a file name cannot.

  WHAT IS WRITTEN: a type 1 standard MIDI file with PrepareForExport already
  applied. The tune's title is its track name and its composer a text event.
  Repeats are UNROLLED, because MIDI has no repeat marks, so the file
  plays the tune the length it is performed at. Each voice has its own channel -
  1, 2, 3 ... in the order the voices first appear, skipping channel 10, which
  General MIDI keeps for percussion - and tempo, meter and key changes stand
  where the notation put them. Timing is exact: lengths and positions are
  fractions of a whole note, rounded to ticks once.

  THE OPTIONS THAT SHAPE THE FILE, through Convert(tune, options):

    var options = new AbcToMidiOptions
    {
        TicksPerQuarterNote   = 960,   // the file's resolution; 480 by default
        DefaultBeatsPerMinute = 96,    // ONLY for a tune with no Q: field; 120 by default
        Velocity              = 90,    // how hard every note is struck; 100 by default
    };
    options.VoiceChannels["T1"] = 4;   // the voice whose id is T1 goes on channel 4
    options.VoiceChannels[""]   = 10;  // a tune with no V: fields has ONE voice, whose id is empty

    MidiEventCollection midi = AbcToMidi.Convert(tune, options);

    TicksPerQuarterNote     the resolution of the file written. Raise it when the
                            tunes carry fast tuplets and the file is going to a
                            notation program; every common tool reads 480.
    DefaultBeatsPerMinute   a tune's own Q: field always wins; this is what a
                            tune WITHOUT one is given.
    Velocity                abc has no dynamics the reader honours, so every note
                            takes this.
    GraceNoteLength         the length of one grace note, an exact fraction of a
                            whole note (1/64 by default).
    VoiceChannels           voice id -> channel 1 to 16. It wins over a
                            %%MIDI channel directive, which wins over the
                            automatic assignment.
    HonourMidiDirectives    true by default: "%%MIDI program n" becomes a program
                            change and "%%MIDI channel n" moves the voice. Set it
                            false to get the notes WITHOUT the instrument choices
                            the transcriber made: the voices then take their
                            automatic channels and no program change is written.

  THE COLLECTION IS THE ORDINARY EDITABLE MODEL, so anything else this package
  does with MIDI is open to it BEFORE it is written: add a program change to
  choose an instrument per channel, transpose, merge several tunes into one
  file. If you change it, call PrepareForExport() again before Export.

  WHAT NEVER REACHES THE FILE is what the reader skips, because MIDI has no place
  for most of it and the rest is not honoured: chord symbols "Am" (no
  accompaniment is invented), lyrics, decorations, annotations, parts and clefs.
  Each KIND is named once in the tune's Problems, so a converter can tell its
  user exactly what was left behind. NOTHING about a tune's CONTENT throws - not
  in Read, not in Convert. A missing file, a null tune and a path that cannot be
  written DO throw: those are the caller's mistakes, not the tune's.

  TO HEAR THE SAME COLLECTION instead of saving it, hand it to
  MidiSequence.FromEvents(midi) and any player here - see "READING ABC NOTATION".
  One Convert serves both: save it AND play it.

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
  - MidiStream            : a MIDI timeline that can be PLAYED WHILE IT IS STILL
                            BEING WRITTEN. Append(MidiEvent) / Append(events) /
                            AppendNote / AppendTempo from any thread, Complete()
                            when the music is finished; Preroll, HorizonTicks,
                            HorizonTime, IsCompleted, EventCount, LateEventCount,
                            Problems; ToMidiEventCollection() for the editable
                            recording and ToSequence() for the finished piece.
                            AdvanceHorizon(tick) declares a SETTLED REST - music
                            decided and empty - so the head plays it rather than
                            waiting in it; it records nothing and is refused after
                            Complete(). TimeAtTick(tick) and TickAtTime(time)
                            convert through the stream's own tempo map, under its
                            own lock, and are defined past the horizon.
                            Channels are 1-based, as everywhere in the Midi model.
                            See "PLAYING MIDI THAT IS STILL BEING WRITTEN".
  - MidiStreamSequencer   : drives a synthesizer from a MidiStream; Play/Stop/
                            Seek, Position, PositionTicks, Length (the horizon,
                            which grows), IsStarved, EndOfStream, Speed,
                            BeatsPerBar. MidiMusicPlayer uses it for you.
  - RoutingSynthesizer    : ONE IMidiSynthesizer that is really sixteen - a
                            child synthesizer per channel, each with its own
                            gain and an optional layered second child, built
                            eagerly or on first use. Every sequencer, player and
                            renderer drives it as one synthesizer, so a voiced
                            arrangement plays live, follows a MidiStream still
                            being written, and renders offline through one code
                            path. A child REPLACED in an occupied slot is
                            released and RINGS OUT rather than being cut off
                            (RingOutReplacedChildren, RingOutLimit,
                            RetiredChildCount); clearing a channel stays
                            immediate unless asked otherwise. GetChannel /
                            GetLayer read the table back. THE ROUTING TABLE
                            COUNTS 1-16 while ProcessMidiMessage takes the
                            wire's 0-15 - see "ROUTING THE PARTS OF AN
                            ARRANGEMENT".
  - Reverb / Chorus       : the two SEND effects SoundFontSynthesizer uses,
                            public so a synthesizer of your own can use the same
                            ones. They take the send bus and OVERWRITE the
                            output buffers with the wet signal alone; adding it
                            to the dry is yours. Reverb.InputGain is the scale
                            every source goes into the bus with, and Mute()
                            clears the tails.
  - SoundFontRenderer     : offline rendering - Render(...) to a float buffer, or
                            RenderToWavFile(...) / RenderToWavStream(...). No
                            audio device involved, and faster than real time.
                            RenderToFile(...) / RenderToStream(...) write ANY
                            registered format, chosen by the path's extension or
                            by an extension you name, in the writer's default
                            format or a WaveFormat of your own - see "WRITING A
                            RENDER TO A FILE". Both take a SoundFont with a
                            sample rate, or any IMidiSynthesizer.
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
                              .Stream             the loaded MidiStream, for
                                                  music still being written -
                                                  null when a sequence is loaded,
                                                  and Sequence is null when a
                                                  stream is. Duration then GROWS
                                                  with the producer.
                              .IsStarved          whether a loaded stream has
                                                  caught up with its producer and
                                                  is playing silence.
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
                            Every Load overload that takes a MidiSequence has a
                            twin that takes a MidiStream.
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


WRITING A RENDER TO A FILE, AND ADDING A FORMAT FROM ANOTHER PACKAGE
====================================================================
Reading dispatches on extension through AudioFileReaderRegistry. WRITING has the
matching seam, AudioFileWriterRegistry, and everything that renders audio
offline goes through it - so "render this to a .wav" and "render this to a
.opus" are the same call with a different file name.

    using CodeBrix.Audio.Synth;

    SoundFontRenderer.RenderToFile(synthesizer, sequence, "tune.wav");
    SoundFontRenderer.RenderToFile(synthesizer, sequence, "tune.aiff");

    // 16-bit PCM instead of the 32-bit float a .wav is written as by default
    SoundFontRenderer.RenderToFile(synthesizer, sequence, "tune.wav",
                                   new WaveFormat(44100, 16, 2));

    // Straight to a stream; the extension names the format, and the stream is
    // NEVER closed for you
    using var stream = File.Create("tune.wav");
    SoundFontRenderer.RenderToStream(synthesizer, sequence, stream, ".wav");

RenderToFile and RenderToStream take either a SoundFont (with a sample rate) or
any IMidiSynthesizer - a SoundFont, SFZ or Decent Sampler synthesizer, ModestSynth's
General MIDI synthesizer, a RoutingSynthesizer carrying a whole voiced
arrangement, or one of your own. The render is always STEREO; a format whose
Channels is not 2 is refused, and the IMidiSynthesizer overloads refuse a format
whose sample rate is not the synthesizer's, because nothing here resamples.

MIDI TO A FILE, AND ABC TO A FILE, ARE A FEW LINES EACH - no audio device, no
player, nothing native:

    using CodeBrix.Audio.Abc;
    using CodeBrix.Audio.Instruments;
    using CodeBrix.Audio.ModestSynth;
    using CodeBrix.Audio.Synth;

    GeneralMidiInstrumentLibrary.Register();
    var library = InstrumentLibraryRegistry.Resolve("ModestSynthGm");

    // a .mid, honouring the file's own program changes
    SoundFontRenderer.RenderToFile(
        library.CreateMultiTimbralSynthesizer(44100),
        new MidiSequence("tune.mid"), "tune.wav", TimeSpan.FromSeconds(2));

    // an .abc, through the same road
    var tune = AbcReader.Read("session.abc").Tunes[0];
    SoundFontRenderer.RenderToFile(
        library.CreateMultiTimbralSynthesizer(44100),
        MidiSequence.FromEvents(AbcToMidi.Convert(tune)), "tune.aiff",
        TimeSpan.FromSeconds(2));

WHAT IS BUILT IN:

  .wav            WavAudioFileWriterFactory, 32-BIT IEEE FLOAT by default, which
                  is what RenderToWavFile has always written. 16- and 24-bit PCM
                  on request, either per call through a WaveFormat or as an
                  application-wide default:
                      AudioFileWriterRegistry.Register(
                          new WavAudioFileWriterFactory(16));
  .aif / .aiff    AiffAudioFileWriterFactory, 16-BIT PCM by default, 24-bit on
                  request. AIFF refuses IEEE float: the container carries no
                  AIFF-C compression type, so a float AIFF would read back as
                  noise.

BOTH NEED A SEEKABLE STREAM, because both patch their header once the length of
the audio is known. Handing either one a forward-only stream throws:

    A .wav file needs a stream that can seek: the length is written into the
    header once the audio is known, which means going back to the start of the
    file. Write to a FileStream or a MemoryStream, or choose a format that is
    written strictly forwards.

A format nobody has registered is refused by name, with the alternatives:

    No audio writer is registered for '.opus'. Registered formats: .aif, .aiff,
    .wav. Add one with AudioFileWriterRegistry.Register.

DRIVING THE SEAM BY HAND, when you have samples rather than a sequence:

    using CodeBrix.Audio.Wave;

    using var writer = AudioFileWriterRegistry.Create("tune.wav", stream, 44100, 2);
    writer.Write(samples, 0, samples.Length);   // interleaved floats
    writer.Finish();                            // idempotent; Dispose calls it

  AudioFileWriterRegistry.Register(factory)            all of its extensions
  AudioFileWriterRegistry.Register(extension, factory) one of them
  AudioFileWriterRegistry.Supports(fileNameOrExtension)
  AudioFileWriterRegistry.SupportedExtensions
  AudioFileWriterRegistry.Resolve(fileNameOrExtension) the factory
  AudioFileWriterRegistry.Create(fileNameOrExtension, stream, format)
  AudioFileWriterRegistry.Create(fileNameOrExtension, stream, rate, channels)

  writer.WaveFormat / .SamplesWritten
  writer.Write(float[] samples, int offset, int count) / .Write(ReadOnlySpan<float>)
  writer.Finish()

AN IAudioFileWriter NEVER CLOSES THE STREAM IT WAS HANDED. Finishing the FILE -
flushing, patching the header - and closing the STREAM are separate jobs, and
the caller owns the stream throughout. That is why there is no leaveOpen
parameter anywhere in this seam, and why RenderToStream has none where the older
RenderToWavStream does.

ADDING A FORMAT FROM ANOTHER PACKAGE is the writing side of ADDING A CODEC FROM
ANOTHER PACKAGE above, and it works the same way. Implement
IAudioFileWriterFactory - the extensions it answers to, whether it needs a
seekable stream, its default WaveFormat for a rate and channel count, and a
Create that wraps a stream - and register it from your package's static
Register() entry point:

    public sealed class MyFormatWriterFactory : IAudioFileWriterFactory
    {
        public IReadOnlyList<string> Extensions => new[] { ".myf" };
        public bool RequiresSeekableStream => false;     // written strictly forwards
        public WaveFormat DefaultFormat(int sampleRate, int channels) =>
            WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, channels);
        public IAudioFileWriter Create(Stream stream, WaveFormat format) =>
            new MyFormatWriter(stream, format);
    }

    AudioFileWriterRegistry.Register(new MyFormatWriterFactory());

Extensions are lower case with a leading dot, and are matched that way whether a
caller writes ".myf", "myf" or "tune.myf". Register from a static Register()
call, as the reader seam does - not from a module initializer, which only runs
once something in the assembly is touched.

A package that already registers its READER from one call adds its WRITER to the
same call, so a consumer who was calling it gets both. That is what
CodeBrixAudioOpus.Register() does for .opus: one call, and .opus reaches every
extension-driven road in this package - AudioFileReader and the reader registry
for reading, AudioFileWriterRegistry and SoundFontRenderer.RenderToFile for
writing. Opus needs no seeking, so it writes to a network stream or a pipe that
WAV and AIFF would refuse.


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
  It costs a full decode and a full synthesis pass per track, and then one render
  of the matched mix - seconds, not milliseconds - so with the option on, Prepare
  starts it on a worker and does not wait for it. LevelMeasurement is the task it
  runs on, and awaiting that is how you know the gains are in:

    player.AutoSetRelativeTrackLevels = true;
    player.Prepare();
    await player.LevelMeasurement;              // the gains are in when this
    player.Play();                              //   returns

  LevelMeasurement is NEVER null. Until a measurement starts it is an
  already-completed task, so that await is safe on any player and simply returns
  at once when there is nothing to wait for - no null check, no guard.

  Two other ways to run it, with the option left off. NEITHER OPENS THE AUDIO
  DEVICE, which is what makes them the offline answer:

    await player.MeasureRelativeTrackLevelsAsync();   // on a worker; the task it
                                                      //   returns IS
                                                      //   LevelMeasurement
    player.MeasureRelativeTrackLevels();              // BLOCKS this thread until
                                                      //   done; LevelMeasurement
                                                      //   is completed afterwards

  Call either again after changing an instrument, when the gains they wrote no
  longer describe what a track renders.

  THE RATE IT MEASURES AT MATTERS, and this is the one thing to understand about
  the feature. A sample rate carries no energy above half of itself, so an
  instrument whose sound sits mostly up there - a synthesized cymbal, a bright
  hi-hat - measures as almost silent at a low rate and is then handed a make-up
  gain many times too large. So the measurement follows the rate the music will
  really be heard at: the prepared device's rate when the player is prepared,
  MultiTrackPlayer.DefaultRenderSampleRate when it is not, and the render's own
  rate when a render starts one. Both measuring calls take an explicit rate when
  you want to choose:

    player.MeasureRelativeTrackLevels(48000);
    await player.MeasureRelativeTrackLevelsAsync(48000);

  LevelMeasurementSampleRate is now only the FLOOR under that - the lowest rate a
  measurement ever runs at.

  HEADROOM: WHAT THE MATCHED MIX WILL PEAK AT. Matching reliably pushes a mix past
  full scale and there is nothing wrong with that measurement - a General MIDI kit
  really is far quieter than a mastered drum recording, so the make-up gain on that
  track really is large, and several large gains add up. The measurement has
  rendered everything anyway, so it renders the matched mix once more and reports
  what it peaks at:

    player.MeasureRelativeTrackLevels();

    var match = player.LastLevelMatch;          // null until a measurement that
    if (match != null && match.WouldClip)       //   matched a track completes
    {
        player.Volume = match.SuggestedVolume;  // lands the mix at full scale
    }

    match.MixPeak             the largest absolute sample the matched mix reaches,
                              at unity master volume, with each track on the source
                              it is on. It is the peak of the SUMMED mix, measured
                              by rendering it - per-track peaks do not add up to it.
    match.SuggestedVolume     the Volume that would put that peak exactly at 1.0.
                              Below 1 when the mix would clip, above 1 when there
                              is headroom going spare.
    match.WouldClip           whether MixPeak is past 1.0.
    match.MatchedTrackCount   how many tracks had a gain written.
    match.SampleRate          the rate the measurement ran at.

  NOTHING IS APPLIED. This is a report; the rule that nothing is limited or
  normalised still holds. FitVolumeAfterLevelMatching (false by default) is the
  opt-in that acts on it: with it set, a completed measurement writes Volume as
  well, in both directions - a mix that would clip is turned down, a quiet one is
  turned up to use its headroom.

OFFLINE, WITH NO AUDIO DEVICE
  Render(sampleRate)              the whole mix as interleaved stereo float.
  RenderToWav(path or Stream)     the same, written out as 32-bit float stereo.
  RenderToFile(path)              the format the EXTENSION names, through
                                  AudioFileWriterRegistry - .wav and .aif / .aiff
                                  out of the box, and anything else registered.
  RenderToFile(path, WaveFormat)  a format of your choosing, so a 16-bit PCM
                                  bounce is one argument:
                                    player.RenderToFile("mix.wav",
                                        new WaveFormat(48000, 16, 2));
  RenderToStream(stream, ".wav")               the same, on a stream you own. The
  RenderToStream(stream, ".wav", WaveFormat)   stream is never closed, and must be
                                               seekable when the writer says so.
  All of them build their own decoders and synthesizers at the rate you ask for,
  render, and release them, so rendering while the same song plays is legitimate.
  Volume and every per-track control apply; nothing is limited or normalised, so a
  mix that adds up past 1.0 comes back past 1.0 - see HEADROOM above for what to
  set Volume to. A four-minute song at 44.1 kHz is about 84 MB of float in one
  array.

  MATCHING THE LEVELS OFFLINE: do NOT call Prepare() - it opens the audio device,
  which an offline render has no use for and which may fail outright on a machine
  where something else holds it. Call MeasureRelativeTrackLevels() (or await
  MeasureRelativeTrackLevelsAsync()) and then render:

    player.MeasureRelativeTrackLevels(48000);   // no device; the render's rate
    var samples = player.Render(48000);

  With AutoSetRelativeTrackLevels set and no measurement yet run, a render MEASURES
  FIRST, on the calling thread, at the rate it is about to render at. A render
  never quietly produces a mix whose gains were never written.

  A DECENT SAMPLER SYNTHESIZER AMONG THE TRACKS is switched to the offline
  streaming mode for the render and put back afterwards, exactly as
  SoundFontRenderer does it - see PLAYING DECENT SAMPLER INSTRUMENTS. You do not
  have to set it yourself, and a synthesizer you built in the offline mode stays
  in it.

WHAT COULD NOT BE HONOURED
  player.Problems is one human-readable line per thing a LOADER could not honour
  when it built this player - a per-stem instrument naming a part the song does
  not have, a part whose notes the chosen instrument library does not cover.
  Empty for a clean build and on a player you assembled yourself. Never thrown:
  a silent part is otherwise indistinguishable from a fault in the music.

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
              UsedNotes / LowestNote / HighestNote, Duration, AudioSampleRate,
              AudioChannels, the alignment members below, GetAudioPath() /
              OpenAudio() (WAV preferred, MP3 fallback) and the WAV- and
              MP3-specific forms of both, and ReadMonoAudio(out rate).

WHAT NOTES A TRANSCRIPTION HOLDS
  stem.UsedNotes is every distinct MIDI note number the stem's .mid plays,
  ascending, with LowestNote and HighestNote either end of it (-1 each when there
  are no notes). On a percussion stem they are KIT PIECES rather than pitches.
  They are gathered while the file is read, so asking costs nothing and needs no
  second parse - which is what makes the coverage check two lines:

    foreach (var note in stem.UsedNotes)
    {
        if (!library.Coverage.CoversNote(stem.GmProgram, note))
        {
            // that note will not sound: voice the part differently, or transpose
        }
    }

  A player built with an instrument library does this for you and reports what it
  finds in player.Problems.

READING A STEM'S WAVEFORM
  stem.ReadMonoAudio(out var sampleRate) decodes the whole recording to mono float
  samples - the channels averaged - at the file's own rate, for a level meter, a
  waveform view, an energy search or a measurement of your own. It decodes the
  WHOLE stem and caches nothing: a four-minute part at 48 kHz is about 46 MB in
  one array, so hold what you get rather than calling it twice, and never call it
  on the audio thread. A stem with no recording returns an empty array and a rate
  of zero rather than throwing.

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

  USE BOTH NUMBERS, not coverage alone. A two-note synth pad whose two notes are
  very long passes an ordinary coverage floor and is still not the part - playing
  it in place of the recording removes that part from the mix. A dense burst of
  very short notes passes a note count while covering almost nothing. A floor on
  each is the rule that holds up, and SunoStemSelection applies both for you, at
  twelve notes and 0.02 coverage by default.

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

BUILDING THE PLAYER - THE ONE-LINE WAY
  Changing what a whole song sounds like is a NAME, and keeping the vocals as the
  recording is a selection. This is the shape to start from:

    using CodeBrix.Audio.Instruments;
    using CodeBrix.Audio.ModestSynth;          // the add-on package
    using CodeBrix.Audio.Playback;
    using CodeBrix.Audio.Playback.Suno;

    GeneralMidiInstrumentLibrary.Register();   // registers as "ModestSynthGm"

    var song = SunoStemsLoader.Load("/downloads/My Song Stems.zip");

    using var player = song.CreatePlayer(new SunoPlayerOptions
    {
        InstrumentLibraryName = "ModestSynthGm",
        MidiStems = SunoStemSelection.EverythingBut("Vocals", "Backing Vocals"),
        AutoSetRelativeTrackLevels = true,
    });

  That is the whole swap: every stem with a usable transcription plays through
  that library at its own General MIDI program, percussion through the library's
  kit, the vocals stay as Suno recorded them, and the parts are level-matched to
  the original mix. Change the one name to change the whole sound.

  var player = song.CreatePlayer();                    // recordings only
  var player = song.CreatePlayer(instrumentFactory);   // the escape hatch
  var player = song.CreatePlayer(new SunoPlayerOptions { ... });
  var player = MultiTrackPlayer.Load(song, options);   // the same thing

  One track per stem, in the model's own order, named after the stem. Every track
  starts on its stem's RECORDING, which is the default mix: the song as it was
  downloaded - MidiStems is what moves a track off it. Where a stem also has MIDI
  and an instrument can be built for it, the same track carries the MIDI as its
  second source, so switching a part to a synthesized rendition is a property
  change. A stem with MIDI and no recording becomes a MIDI-only track; a stem with
  MIDI, no recording and no instrument is not added at all.

  SunoPlayerOptions
    InstrumentLibraryName       a REGISTERED instrument library name, resolved
                                through InstrumentLibraryRegistry when the player
                                is built. Each stem gets
                                CreateSynthesizer(stem.GmProgram, rate), or
                                CreatePercussionSynthesizer(rate) when the stem is
                                percussion.
    InstrumentLibrary           the same, as an INSTANCE, for a library that was
                                never registered. Wins over the name.
    StemInstruments             an instrument for ONE NAMED STEM - see the next
                                heading.
    MidiStems                   which stems start on their transcription rather
                                than their recording; a SunoStemSelection. Null by
                                default, which leaves every track on its
                                recording.
    InstrumentFactory           Func<SunoStem, int, IMidiSynthesizer>: build the
                                instrument for one stem at one sample rate. It
                                may be called more than once and from a worker,
                                so never hand out the same synthesizer twice -
                                share the SoundFont or the SFZ instrument behind
                                them instead. This is the escape hatch for
                                anything the library and the per-stem overrides
                                cannot say.
    GeneralMidiSoundFontPath    used when there is no library and no factory.
                                Falls back to the path on SunoLoadOptions, so a
                                song loaded with one needs no player options at
                                all. The SoundFont is loaded ONCE through a
                                SoundFontCache and shared by every track.
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

  WHICH INSTRUMENT A STEM GETS - most specific first, and the order is fixed:
    1. StemInstruments, for a stem named there;
    2. InstrumentLibrary, or InstrumentLibraryName resolved through the registry;
    3. InstrumentFactory;
    4. GeneralMidiSoundFontPath, or the path the song was loaded with.
  With none of the first two set, a player behaves exactly as it always has.
  Setting BOTH a library and an InstrumentFactory is not an error - the library
  wins - but it is reported in player.Problems, because a factory that is never
  called is almost always a leftover.

  ERRORS WHEN THE PLAYER IS BUILT, not from a worker in the middle of a song. A
  name nothing is registered under is the registry's own error, listing what IS
  registered; an empty registry is the registry's own error, naming what to
  register; and a library that does not offer the PER-PART shape is refused by
  name, because each stem is a part with a gain and a source of its own and one
  multi-timbral synthesizer mixes internally at one level.

  WHAT COULD NOT BE HONOURED IS REPORTED, never thrown - one line each in
  player.Problems: a per-stem instrument or a selection naming a part the song
  does not have, and a part whose program or notes the chosen library does not
  cover. A part the library cannot play comes out SILENT, which is otherwise
  indistinguishable from a fault in the music.

    foreach (var problem in player.Problems) { Console.WriteLine(problem); }

AN INSTRUMENT FOR ONE STEM - SunoPlayerOptions.StemInstruments
  Keyed by STEM NAME, and that is the point. MappedInstrumentLibrary substitutes
  by General MIDI PROGRAM, which is the wrong axis here: a stems export puts Drums
  and Percussion both on channel 10 with a kit number for a program, and two
  melodic parts of one song can carry the same program. The part is the stem.

    var options = new SunoPlayerOptions { InstrumentLibraryName = "ModestSynthGm" };

    options.StemInstruments["Bass"] = rate => new MyBassSynthesizer(rate);
    options.StemInstruments.SetFromFile("Synth", "/packs/Grand/Grand.dspreset");
    options.StemInstruments.SetPercussionFromFile("Drums", "/packs/Kit/kit.dspreset");

  Set(name, factory)                     a synthesizer of your own, built on
                                         demand at the rate it is handed.
  SetFromFile(name, path, program = 0)   .dspreset / .dslibrary / .dsbundle
                                         (Decent Sampler), a Decent Sampler
                                         library FOLDER, .sfz, or .sf2 - whose
                                         program is the third argument.
  SetPercussionFromFile(name, path)      the same, taking a .sf2's KIT rather than
                                         a melodic program.
  Contains / TryGet / Remove / Clear / Count / StemNames, and the indexer, which
  returns null for a stem that has none and removes one when set to null.

  Names match the way song["Bass"] matches: case-insensitively, with surrounding
  space ignored. A name the song has no stem for is reported, not thrown.
  A FILE IS LOADED WHERE YOU WROTE THE PATH, so a typo is an error on that line;
  one loaded instrument is shared by every synthesizer built from it.

WHICH STEMS PLAY FROM MIDI - SunoStemSelection
  SunoStemSelection.EverythingBut("Vocals", "Backing Vocals")
  SunoStemSelection.Only("Drums", "Bass")
  SunoStemSelection.Everything()
  SunoStemSelection.Nothing()          // every track on its recording

  A stem is included only when it is on the right side of the names AND its
  transcription clears both floors - MinimumNoteCount (12) and MinimumMidiCoverage
  (0.02), each settable:

    var selection = SunoStemSelection.EverythingBut("Vocals");
    selection.MinimumNoteCount = 40;       // this arrangement wants real parts only

  selection.Includes(stem) is the same rule, for your own loop. Setting MidiStems
  replaces the walk over player.Tracks every consumer otherwise writes; each
  track's ActiveSource is still a property you can change afterwards.

THE GENERAL MIDI SOUNDFONT
  An instrument library is the shorter way to say all of this, and the rest of
  this heading is what happens when you point the player at a .sf2 directly
  instead. A MIDI stem needs an instrument, and this is the older choice: one
  General MIDI SoundFont for the whole song, applying each stem's own program and
  channel.
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
  Play the vocals from their recordings and every other part through a named
  instrument library, with the levels of the original mix:

    using CodeBrix.Audio.Instruments;
    using CodeBrix.Audio.ModestSynth;
    using CodeBrix.Audio.Playback;
    using CodeBrix.Audio.Playback.Suno;

    GeneralMidiInstrumentLibrary.Register();          // "ModestSynthGm"

    var song = SunoStemsLoader.Load(@"D:\Downloads\My Song Stems.zip");
    foreach (var problem in song.Problems)
    {
        Console.WriteLine(problem);          // never thrown; usually empty
    }

    using var player = song.CreatePlayer(new SunoPlayerOptions
    {
        InstrumentLibraryName = "ModestSynthGm",
        MidiStems = SunoStemSelection.EverythingBut("Vocals", "Backing Vocals"),
        AutoSetRelativeTrackLevels = true,
    });

    foreach (var problem in player.Problems)
    {
        Console.WriteLine(problem);          // coverage, absent stem names
    }

    player.Prepare();
    await player.LevelMeasurement;           // the levels are in when this returns
    player.Play();

  THE SAME SONG, OFFLINE, with no audio device anywhere near it. Render at 48000:
  a stems export is 48 kHz throughout, and the default render rate is 44100, so
  taking the default resamples every stem for no reason.

    using var bounce = song.CreatePlayer(new SunoPlayerOptions
    {
        InstrumentLibraryName = "ModestSynthGm",
        MidiStems = SunoStemSelection.EverythingBut("Vocals", "Backing Vocals"),
    });

    bounce.MeasureRelativeTrackLevels(48000);   // NOT Prepare() - see OFFLINE above
    var match = bounce.LastLevelMatch;
    if (match != null && match.WouldClip)
    {
        bounce.Volume = match.SuggestedVolume;  // matching routinely peaks past 1.0
    }

    bounce.RenderToFile("mix.wav", new WaveFormat(48000, 16, 2));

  WRITING THE SELECTION YOURSELF, when the rule is not one a selection can state.
  Use a floor on the NOTE COUNT as well as on the coverage - a two-note pad passes
  a coverage floor on its own:

    foreach (var track in player.Tracks)
    {
        var stem = song[track.Name];
        var worthPlaying = track.HasMidiSource
            && stem.NoteCount >= 12
            && stem.MidiCoverage >= 0.02;

        if (worthPlaying && !track.Name.Contains("Vocal", StringComparison.OrdinalIgnoreCase))
        {
            track.ActiveSource = TrackSource.Midi;
        }
    }

  One named part, without walking the list - player["Bass"] matches the way
  song["Bass"] does, and throws with the available names when there is no such
  track:

    var bass = player["Bass"];
    if (bass.HasMidiSource)                  // not every stem has a .mid beside it
    {
        bass.ActiveSource = TrackSource.Midi;
    }

  Give one part an instrument of its own, with everything else still coming from
  the library:

    var options = new SunoPlayerOptions { InstrumentLibraryName = "ModestSynthGm" };
    options.StemInstruments.SetFromFile("Synth", @"D:\Libraries\Strings\strings.sfz");
    using var player = song.CreatePlayer(options);

  The same thing written by hand, which is what InstrumentFactory is for when the
  rule is more than "this stem gets this instrument":

    var strings = sfzCache.Get(@"D:\Libraries\Strings\strings.sfz");
    using var player = song.CreatePlayer((stem, rate) => stem.Name == "Synth"
        ? new SfzSynthesizer(strings, rate)
        : new SoundFontSynthesizer(generalMidi, rate));

  Correct one part's alignment by hand, then bounce the result to a file:

    song["Drums"].AlignmentOffset = TimeSpan.FromMilliseconds(-120);
    using var bounce = song.CreatePlayer(options);
    bounce.RenderToWav("mix.wav", 48000);


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

  SAMPLE_START, LOOP_START AND LOOP_END TAKE EFFECT ON THE NEXT NOTE; SAMPLE_END
  ALSO STOPS A SOUNDING VOICE. A binding that moves the start or either loop
  point changes what the next note-on reads, and a voice already sounding keeps
  the bounds it started with, so dragging that knob under a held chord is silent
  until the next note. SAMPLE_END is the exception, measured on the reference: a
  voice whose read position is already past the new end stops AT ONCE, and one
  still short of it plays on and stops there. That is the same rule whether the
  sample is held in memory or streamed, except that a streamed voice keeps the
  next-note rule for all four - the format's own guide says these four need
  in-memory playback, and a streamed instrument says so once in Problems.

  HOW MANY STREAMED NOTES CAN SOUND AT ONCE. Each streamed voice needs a ring
  buffer of its own, and the pool is built when the synthesizer is - so it is
  sized from DecentSamplerSynthesizerSettings.StreamingVoiceCount, which is
  automatic by default and then means one per voice of MaximumPolyphony. Left
  alone, a streamed preset therefore sounds EXACTLY like the same preset held in
  memory, however wide the chord. The price is two channels times
  StreamingRingFrames times four bytes per buffer - 12 MB at the default
  polyphony and ring size, against the hundreds of megabytes of sample data it
  stands in for. Pin a smaller number on a memory-tight device and accept that
  the surplus notes of a very wide chord play silence (the instrument says so in
  Problems, once).

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
  MultiTrackPlayer does the same for every Decent Sampler synthesizer among its
  tracks, in Render, RenderToWav, RenderToFile, RenderToStream and the level
  measurement: switched for the render, put back when it ends, however it ends. A
  synthesizer you built in the Offline mode stays in it.
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
  A KEY-TRIGGERED player is tracked by its (channel, key), seqPlayerIdentifier or
  not, so a handler covering a range of key switches runs a different sequence
  for every key and two keys naming the same identifier run two independent
  players. Every other form - an "on" or an "off" fired from a controller or a
  user-interface control - is tracked under seqPlayerIdentifier, which is what
  lets a second "on" restart the one player a first "on" started. Speed is the
  sequence's own rate against the synthesizer's TempoSource, read every block, so
  a RATE binding changes the speed of a sequence that is already running.

  RE-TRIGGERING A SEQUENCE THAT IS ALREADY RUNNING restarts it: the same key
  pressed again, or the same identifier fired "on" again, cuts the sounding note,
  goes back to the first note and re-bases the grid on the new trigger. A <cc>
  binding fires on every CHANGE of its controller, so each change restarts the
  sequence; a user-interface button's state binding fires only when the STATE
  ITSELF CHANGES, so re-selecting "On" in the middle of a sequence leaves it
  running. That is the practical difference between the two ways of latching a
  sequence on and off with no key held.

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

  THE TWO SCOPES OF AN <envelope> ARE AUDIBLY DIFFERENT UNDER A CHORD, and the
  difference is the reference's. scope="voice" gives every note its own envelope
  instance, gated by its own note-on, and a note already sounding is undisturbed
  when the next one starts. scope="global" keeps ONE instance for the modulator
  and EVERY note-on restarts it from zero, so a note that is already sounding
  drops out and re-attacks with the new one. One note alone sounds identical
  either way, which is why the choice looks harmless until a second key goes
  down.

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
                  defects and doing less was not worth reproducing. They belong
                  to <velocity> ALONE: a <cc> or <note> binding honours
                  groupIndex, position and level="tag", and reaches AMP_VOLUME,
                  in the reference exactly as it does here.
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
                  declared length is EXACTLY 2, and only that: declared lengths
                  1, 3, 4, 6 and 8 all stop after one pass there and 2 loops for
                  ever, whatever the note count. It is a defect and deliberately
                  not reproduced.
    sequence length  the declared length of a note sequence truncates it here at
                  every length: a note written at or past the length never
                  plays. The reference truncates from length 2 upward but not at
                  length 1, where a sequence declaring length="1" with notes on
                  beats 0 and 1 played both and then stopped.
    LOOP_START and LOOP_END  a binding that moves either one is honoured here at
                  the next note-on. The reference accepts both and does NOTHING
                  with them - not on a sounding voice, not at the next note-on,
                  and through none of the three ways a binding can name its
                  target, even with playbackMode="memory" as its own guide
                  requires. SAMPLE_START and SAMPLE_END do work there, and they
                  are the control that proves it.
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
                  eighteen groups in six instrument families, each scaled by its
                  own control - measures 1.09 dB louder than the reference over
                  eight bars, with a per-bar spread up to 1.5 dB. Recording the
                  reference one family at a time puts three families above it
                  and two below: strings +4.2 dB, the odds and ends +6.3,
                  percussion +1.2, synths +0.2, guitars -1.3 and winds -2.3. The
                  synth family is EXACT - every octave band inside 0.24 dB - so
                  the shared signal path is right and what is left is in what
                  five families' zones do. Every individual rule the preset uses
                  was measured and matches on its own (the tag volumes multiply
                  exactly and the effects account for 0.05 dB), and the other two
                  comparison presets are inside 0.1 dB. A master gain closes it
                  if a render has to match the reference exactly.

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
    // Or RenderToFile(...), which picks the writer from the path's extension:
    // SoundFontRenderer.RenderToFile(soundFont, sequence, "level1.aiff");

Play a .mid with no SoundFont, no configuration and nothing to download:

    using CodeBrix.Audio.Instruments;
    using CodeBrix.Audio.ModestSynth;     // the add-on package
    using CodeBrix.Audio.Synth;

    GeneralMidiInstrumentLibrary.Register();          // registers "ModestSynthGm"

    var library = InstrumentLibraryRegistry.Resolve("ModestSynthGm");
    var synthesizer = library.CreateMultiTimbralSynthesizer(44100);

    SoundFontRenderer.RenderToFile(synthesizer, new MidiSequence("level1.mid"),
                                   "level1.wav", TimeSpan.FromSeconds(2));
    // The file's own program changes choose the instruments, and channel 10 is
    // the drum kit. To play it instead, hand the same synthesizer - or the
    // factory form - to MidiMusicPlayer.

Voice an arrangement part by part, and play or render it either way:

    using CodeBrix.Audio.Instruments;
    using CodeBrix.Audio.Midi;
    using CodeBrix.Audio.Playback;
    using CodeBrix.Audio.Synth;

    var library = InstrumentLibraryRegistry.Resolve("ModestSynthGm");

    RoutingSynthesizer BuildArrangement(int rate)     // the table counts 1-16
    {
        var router = new RoutingSynthesizer(rate);
        router.SetChannel(1, library.CreateSynthesizer(8, rate), gain: 0.8F);
        router.SetChannel(2, () => library.CreateSynthesizer(52, rate), 0.5F);
        router.SetChannel(GeneralMidi.PercussionChannel,
                          library.CreatePercussionSynthesizer(rate));
        return router;
    }

    using var music = new MidiMusicPlayer();          // live, at the device's rate
    music.Load(rate => BuildArrangement(rate), sequence);
    music.Play();

    // ...or offline, through exactly the same arrangement:
    // SoundFontRenderer.RenderToFile(BuildArrangement(44100), sequence,
    //                                "arrangement.wav");

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

With no SoundFont to find at all, add the ModestSynth package and register its
instruments - one line, and the whole General MIDI sound set is there:

    using CodeBrix.Audio.Instruments;
    using CodeBrix.Audio.ModestSynth;
    using CodeBrix.Audio.Synth;

    GeneralMidiInstrumentLibrary.Register();

    SoundFontRenderer.RenderToFile(
        InstrumentLibraryRegistry.Resolve("ModestSynthGm")
            .CreateMultiTimbralSynthesizer(44100),
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
    SoundFont / SfzInstrument yourself. An INSTRUMENT LIBRARY does the same job
    for you: SoundFontInstrumentLibrary holds ONE SoundFont behind every
    synthesizer it creates, and MappedInstrumentLibrary holds one loaded copy of
    each instrument file however many programs are taken from it.

  - LET THE ROUTER BUILD ITS PARTS LAZILY. RoutingSynthesizer.SetChannel takes a
    Func<IMidiSynthesizer> as well as a synthesizer, and a factory is not called
    until the music first plays on that channel. Over a large sampled library
    that is the difference between loading the parts a piece uses and loading
    all sixteen.

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
    real time with no device involved, so bouncing a sequence to a file once and
    playing the file is cheaper than synthesising it on every playthrough.
    RenderToFile picks the format from the path's extension, so the bounce can
    go straight to a compressed format when one is registered.


COMMON PITFALLS TO AVOID
========================
  - Abc voices SKIP CHANNEL 10. Voices take channels in the order they first
    appear - 1, 2, 3 ... - and channel 10 is left out, because General MIDI
    reserves it for percussion and a melody landing there plays as drums. If you
    WANT a voice on the drums, say so: options.VoiceChannels["Drums"] = 10, or
    options.VoiceChannels[""] = 10 for the single voice of a tune with no V:
    field. Do not count on the third voice being on channel 3 once a %%MIDI
    channel directive is in play.
  - Abc grace notes MOVE THE NOTE THEY PRECEDE. They are not extra notes squeezed
    in before the beat: the note after them starts later by the graces' total
    length and is shortened by the same amount, so the bar still adds up. Code
    that expects the note after "{g}" to land on the beat will be off by
    AbcToMidiOptions.GraceNoteLength. Set that option to shorten or lengthen the
    effect; there is no way to make a grace note cost nothing, because something
    has to sound.
  - An abc tune's Problems list keeps growing: AbcToMidi.Convert ADDS to the
    tune's list what only the conversion can know - a tuplet that does not divide
    into whole ticks, a tie between two different pitches, more voices than
    channels. Read AbcTune.Problems AFTER converting, not between the read and
    the convert. (The same line is never added twice, so converting one tune
    repeatedly is safe.)
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
  - RE-TIMING A NOTE: assigning NoteOnEvent.AbsoluteTime moves only the START.
    A note is TWO events - the note-on and the NoteEvent its OffEvent points at -
    and both normally sit in the same collection. NoteNumber and Channel push
    their new value into the off event; AbsoluteTime deliberately does not, or a
    loop that shifts every event of a collection would move each note-off twice,
    once in its own right and once dragged along. So this silently shortens the
    note:
        note.AbsoluteTime += 480;        // NoteLength is now 480 ticks shorter
    Use the method that exists for it, which carries the off event along:
        note.MoveTo(note.AbsoluteTime + 480);        // NoteLength unchanged
    — or build a fresh NoteOnEvent(tick, channel, noteNumber, velocity, length).
    NoteLength is DERIVED from the pair, and it never reads as negative: a note
    dragged past its own note-off reads as length 0, because a note that ends
    before it starts is no note at all. (It still THROWS when there is no off
    event to measure against, which only a file that breaks the rules produces.)
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
  - Prepare() opens the audio device, so it is the WRONG way to start a level
    measurement for an offline render - and on a machine where something else
    holds the device it may fail outright. Call MeasureRelativeTrackLevels() (or
    await MeasureRelativeTrackLevelsAsync()), which needs no device, then render.
  - Level matching routinely puts a mix PAST full scale, and nothing limits or
    normalises it. That is not a fault in the measurement: a General MIDI kit is
    far quieter than a mastered drum recording, so the make-up gain on that track
    is genuinely large. Read LastLevelMatch.MixPeak and set Volume to
    SuggestedVolume - or set FitVolumeAfterLevelMatching and have it done.
  - Render a Suno song at 48000. A stems export is 48 kHz throughout, and
    MultiTrackPlayer.DefaultRenderSampleRate is 44100, so taking the default
    resamples every stem for nothing.
  - MidiCoverage alone is not enough to decide whether a transcription is worth
    playing: a pad of two very long notes passes any sensible coverage floor.
    Put a floor on NoteCount beside it - SunoStemSelection applies both.
  - A MidiStream that is never COMPLETED never ends. The player stays Playing and
    starved, Position holds, and PlaybackEnded never fires - the stream cannot
    tell "nothing more yet" from "nothing more". Call stream.Complete() when the
    music is finished; player.Stop() is the only other way out.
  - A stream's Duration and the provider's Length GROW. Anything that caches a
    duration once - a progress bar's maximum, a scheduler's end time - reads a
    number that was true when it asked and is now too small. Re-read it, or wait
    for IsCompleted.
  - Channels on a MidiStream are 1-based (the Midi event model); channels on
    SendMidiMessage, the two hooks and ProcessMidiMessage are 0-based (the
    synthesizer). Appending a note "on channel 0" throws; sending a message "on
    channel 16" throws. This is the same split the package has always had, and a
    stream keeps to the Midi side of it.
  - Do not append to a stream from inside a message hook. The hook is already on
    the audio thread and already under the stream's lock. It will not deadlock,
    but composing from a render callback is not a thing to do on purpose.
  - One player per stream at a time. A second Load of the same stream throws
    InvalidOperationException; player.Stop() rewinds the stream rather than
    handing it over, and disposing the player is what releases it.
  - IsLooping does nothing to a stream. It neither throws nor turns itself off -
    it is a player property and applies to the next sequence loaded. Loop
    stream.ToSequence() instead.
  - NOTHING SOUNDS UNTIL AN INSTRUMENT LIBRARY IS REGISTERED. CodeBrix.Audio
    ships no instruments, so InstrumentLibraryRegistry starts empty and Default,
    Resolve and SetDefault all throw InvalidOperationException until a consumer
    registers something. The message names
    GeneralMidiInstrumentLibrary.Register(), from the ModestSynth add-on, which
    is one line and covers the whole General MIDI sound set. DefaultName is the
    one member that answers on an empty registry, returning null.
  - THE DEFAULT INSTRUMENT LIBRARY IS WHICHEVER REGISTERED FIRST, so in an
    application that registers more than one it depends on which start-up path
    ran first. Name the one you mean -
    InstrumentLibraryRegistry.Resolve("ModestSynthGm") - or call SetDefault
    explicitly. Registering the same library twice is a no-op; a DIFFERENT
    library under a name already taken throws.
  - A PER-PART SYNTHESIZER IGNORES PROGRAM CHANGE, on purpose.
    IInstrumentLibrary.CreateSynthesizer hands back a synthesizer pinned to the
    program you asked for, on every channel, so a program change in the music
    cannot re-voice a part you voiced deliberately. Use
    CreateMultiTimbralSynthesizer when you WANT the music's own program changes.
    The same pinning means the object you get back from
    MappedInstrumentLibrary is not necessarily the type your factory built - a
    cast to DecentSamplerSynthesizer will not succeed, and its MPE and
    multi-output surfaces are not reachable through the wrapper.
  - A ROUTER'S TABLE IS 1-16; ITS MESSAGES ARE 0-15. RoutingSynthesizer.
    SetChannel, SetLayer, ClearChannel, IsRouted and the gain accessors count
    channels the way MidiEvent does, 1 to 16, so percussion is 10.
    ProcessMidiMessage takes the WIRE channel, 0 to 15, so the same percussion
    part arrives as 9. Routing a drum part to "channel 9" puts it where a
    sequencer will never send it.
  - One synthesizer instance may fill only ONE router slot. A child is rendered
    once per block at one gain, so an instance in two slots has no single answer
    to either and is refused. Create a second synthesizer for the second part -
    a library hands one out per call anyway.
  - A router drops messages for a channel with no instrument, SILENTLY, and
    counts them in UnroutedMessageCount. That is what stops a piece carrying an
    unvoiced part from stopping the music; read the counter if you want to know.
  - AN IAudioFileWriter NEVER CLOSES THE STREAM IT WAS HANDED, and neither does
    SoundFontRenderer.RenderToStream. Finish() completes the FILE - flushing and
    patching the header - and the caller still owns and closes the stream. There
    is no leaveOpen parameter anywhere in the writer seam. The older
    RenderToWavStream keeps its own flag and is untouched.
  - .wav AND .aiff NEED A SEEKABLE STREAM, because both write their length into
    a header they go back and patch. A forward-only stream is refused with a
    message saying so; a format written strictly forwards is not affected, and
    an IAudioFileWriterFactory declares which it is with RequiresSeekableStream.
  - AIFF DOES NOT TAKE IEEE FLOAT. The container this package writes carries no
    AIFF-C compression type, so a float AIFF would be read back as PCM and come
    out as noise. AIFF is 16- or 24-bit PCM; .wav is where 32-bit float lives,
    and is what a .wav gets by default.
  - A RENDER IS ALWAYS STEREO, and RenderToFile / RenderToStream refuse a
    WaveFormat whose Channels is not 2. The IMidiSynthesizer overloads also
    refuse a format whose sample rate is not the synthesizer's own: nothing here
    resamples, and rendering at the wrong rate would transpose the music.
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
    Ogg Vorbis and FLAC but writes only .wav, .aif / .aiff and Standard MIDI
    Files (MidiFile.Export). A compressed format comes from an add-on package
    that registers its writer with AudioFileWriterRegistry - for .opus, take the
    CodeBrix.Audio.Opus add-on package. See "WRITING A RENDER TO A FILE".

  - NO INSTRUMENTS, AND NO INSTRUMENT LIBRARY REGISTERED. This package ships the
    ENGINES that play a .sf2, a .sfz or a Decent Sampler preset, the instrument
    library seam and a generic library over any .sf2 - and no sound of its own.
    With an empty registry nothing can be played or rendered through that seam,
    and the exception says so. The complete General MIDI sound set is one
    Register() call away, in the CodeBrix.Audio.ModestSynth add-on package; a
    recorded one is a .sf2 of your choosing behind a SoundFontInstrumentLibrary.

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
    Midi/MidiEventCollectionTests.cs            Clone(), and the note-on to
                                                note-off links it keeps.
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
    Playback/Suno/SunoInstrumentLibraryTests.cs   a stems song played through a
                                NAMED instrument library: the per-part and
                                percussion calls, the registry's errors at build
                                time, a multi-timbral-only library refused,
                                coverage reported rather than thrown, the
                                per-stem override, the precedence pair by pair,
                                and which stems play from MIDI.
    Playback/Suno/SunoStemTests.cs   what a stem says about itself: the notes its
                                transcription holds, and its recording as mono
                                samples.
    Playback/Suno/FakeSongStems.cs   the synthetic export the files above load -
                                a stems set carrying every shape a real one has,
                                built in code.
    MultiTrackPlayerLevelMatchTests.cs   level matching: the rate it measures at
                                (a bright instrument measures 25 times louder at
                                the rate it will be played at than below its own
                                energy), an offline render honouring the option
                                with no Prepare, and the headroom it reports.
    MultiTrackPlayerOfflineRenderTests.cs   what an offline render must get right
                                beyond the samples: a Decent Sampler synthesizer
                                switched to the offline streaming mode and put
                                back even when the render throws, and the render
                                written out through the registered writers.
    Synth/BandLimitedTestSynthesizer.cs   the instrument those level tests need:
                                one that renders a partial only where the sample
                                rate can carry it.

  SOUNDFONT, SFZ, DECENT SAMPLER AND MIDI MUSIC
    Synth/MidiMusicPlayerTests.cs      the transport, Speed, the channel
                                       helpers, BOTH message hooks, and a stream
                                       played while it is still being written.
    Synth/MidiSequenceTests.cs, Synth/MidiSequenceBridgeTests.cs
                                       MidiSequence, and FromEvents(...) as the
                                       bridge from the editable MIDI model.
    Synth/MidiStreamTests.cs           what a growing timeline takes, in what
                                       order it keeps it, what it refuses, and
                                       the recording it builds as it goes.
    Synth/MidiStreamSequencerTests.cs  playing one: starvation and the pre-roll,
                                       late events, seeking, the transport - and
                                       the assertion that a stream completed
                                       before playback renders exactly what its
                                       sequence renders, sample for sample.
    StreamingTestProducer.cs           the producer those tests drive: the motif
                                       a bar at a time, on demand, with no timer
                                       and no real time anywhere.
    Synth/SoundFontRendererTests.cs    offline Render / RenderToWavFile, and
                                       RenderToFile / RenderToStream through the
                                       writer registry.
    Synth/SoundFontCacheTests.cs       sharing one .sf2.
    Synth/RoutingSynthesizerTests.cs   a child per channel with its own gain and
                                       an optional layer, lazy children, the
                                       1-16 table against the wire's 0-15, and
                                       an offline render through the router
                                       compared with the same parts mixed by
                                       hand.
    Synth/ReverbTests.cs, Synth/ChorusTests.cs   the two send effects.

  INSTRUMENT LIBRARIES AND THE WRITER SEAM
    Instruments/InstrumentLibraryRegistryTests.cs   registering and resolving by
                                       name, the no-op, the taken name, and what
                                       each error says.
    Instruments/InstrumentLibraryRegistryDefaultTests.cs   the default and
                                       registration order - gated, because they
                                       change process-wide state.
    Instruments/InstrumentCoverageTests.cs   coverage and key ranges.
    Instruments/SoundFontInstrumentLibraryTests.cs   any .sf2 as a library, both
                                       shapes, ONE shared SoundFont, and a
                                       per-part synthesizer staying pinned.
    Instruments/MappedInstrumentLibraryTests.cs   the swap-one-voice workflow:
                                       one program changing and nothing else,
                                       clearing it again, where an instrument
                                       may come from, the coverage arithmetic,
                                       and a channel changing hands without a
                                       stuck note.
    AudioFileWriterRegistryTests.cs    what is registered out of the box, WAV in
                                       float and in PCM, AIFF round trips, both
                                       refusing a forward-only stream, and a
                                       format of the test's own reaching the
                                       registry by extension.
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
  get instruments at all                  GeneralMidiInstrumentLibrary.Register()
                                          // CodeBrix.Audio.ModestSynth;
                                          // registers as "ModestSynthGm"
  make any .sf2 a named library           new SoundFontInstrumentLibrary(
                                              "MyBank", "...", "bank.sf2")
                                              .Register()
  ask for a library by name               InstrumentLibraryRegistry
                                              .Resolve("ModestSynthGm")
  choose which library is the default     InstrumentLibraryRegistry
                                              .SetDefault("ModestSynthGm")
  play a whole .mid with one synthesizer  library.CreateMultiTimbralSynthesizer(44100)
  voice one part of an arrangement        library.CreateSynthesizer(program, 44100)
                                          library.CreatePercussionSynthesizer(44100)
  find out what a library can play        library.Coverage.CoversNote(program, note)
                                          library.Coverage.KeyRangeOf(program)
  swap ONE voice for an instrument of     var voices = new MappedInstrumentLibrary(
    your own, and keep the rest               "MyVoices", "...", "ModestSynthGm");
                                          voices.SetInstrument(
                                              GeneralMidiProgram.ChoirAahs,
                                              "Whisper Choir.dspreset")
  put that voice back                     voices.ClearInstrument(
                                              GeneralMidiProgram.ChoirAahs)
  play the parts together, each at its    var router = new RoutingSynthesizer(44100);
    own gain                              router.SetChannel(1, synth, gain: 0.8F)
  double a part with a second instrument   router.SetLayer(2, factory, 0.3F)
  build a part only when the music         router.SetChannel(2, () => ..., 0.5F)
    reaches it
  re-voice a part without cutting the     router.SetChannel(1, other, 0.8F)
    note it is holding                      // the old child rings out; it is
                                            // the default
  play music that is still being written  var stream = new MidiStream(480);
                                          music.Load(soundFont, stream)
  say a rest is settled, so the head      stream.AdvanceHorizon(tick)
    plays it instead of waiting in it
  place a tick in time, or a moment on    stream.TimeAtTick(tick)
    the timeline                          stream.TickAtTime(time)
  move a note and keep its length         note.MoveTo(newTick)
  say the music is finished               stream.Complete()
  save what was played as a .mid          MidiFile.Export(path,
                                              stream.ToMidiEventCollection())
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
  read an .abc file                       AbcReader.Read(path)
  read abc from a string                  AbcReader.Parse(text)
  play an abc tune                        AbcToMidi.Convert(book.Tunes[0])
                                          MidiSequence.FromEvents(collection)
  save an abc tune as a .mid              MidiFile.Export(path,
                                              AbcToMidi.Convert(tune))
  convert a whole .abc tunebook to .mid   foreach tune in AbcReader.Read(path).Tunes:
    files - no audio device involved          MidiFile.Export(p, AbcToMidi.Convert(tune))
                                          see "CONVERTING ABC TO A MIDI FILE"
  put an abc voice on a chosen channel    options.VoiceChannels["T1"] = 4
  ignore an abc tune's instrument choices options.HonourMidiDirectives = false
  see what an abc tune did not carry      tune.Problems / book.Problems
  name a General MIDI program             GeneralMidi.DisplayName(
                                              GeneralMidiProgram.Violin)
  set a track's instrument by name        new PatchChangeEvent(tick, channel,
                                              (int)GeneralMidiProgram.Flute)
  name a drum on the percussion channel   GeneralMidi.DisplayName(
                                              GeneralMidiPercussion.Cowbell)
  put a part on the drum channel          channel = GeneralMidi.PercussionChannel
  read a .mid that breaks the rules       new MidiFile(path)   // tolerant
                                          file.Problems
  validate a .mid instead of playing it   new MidiFile(path, MidiReadMode.Strict)
  play several tracks as one song         new MultiTrackPlayer()
  play a Suno stems download              SunoStemsLoader.Load(path)
                                          song.CreatePlayer(options)
  play a stems song through a library     options.InstrumentLibraryName = "..."
  keep the vocals, swap the rest          options.MidiStems =
                                            SunoStemSelection.EverythingBut(
                                              "Vocals", "Backing Vocals")
  give one part its own instrument        options.StemInstruments["Synth"] = ...
  swap one part to its MIDI, live         track.ActiveSource = TrackSource.Midi
  line a transcription up with its audio  MidiAudioAlignment.Estimate(...)
                                          track.MidiSourceOffset = result.Offset
  match a synthesized part to the mix     player.AutoSetRelativeTrackLevels = true
                                          await player.LevelMeasurement
  match the levels with no audio device   player.MeasureRelativeTrackLevels(48000)
  find out what the matched mix peaks at  player.LastLevelMatch.MixPeak
  bounce a song as 16-bit PCM             player.RenderToFile(path,
                                            new WaveFormat(48000, 16, 2))
  reach one track of a song by name       player["Drums"]
  merge a stems set into one .mid         song.ExportMergedMidi(path)
  bounce a multi-track song to a file     player.RenderToWav(path, 44100)
  bounce a .mid to .wav, no device        SoundFontRenderer.RenderToWavFile(...)
  bounce to whatever the extension says   SoundFontRenderer.RenderToFile(
                                              synthesizer, sequence, "tune.aiff")
  bounce as 16-bit PCM instead of float   SoundFontRenderer.RenderToFile(...,
                                              new WaveFormat(44100, 16, 2))
  bounce straight into a stream           SoundFontRenderer.RenderToStream(
                                              synth, sequence, stream, ".wav")
  write float samples as a file yourself  AudioFileWriterRegistry.Create(
                                              "tune.wav", stream, 44100, 2)
  add a writable format from a package    AudioFileWriterRegistry.Register(factory)
  ask what can be written                 AudioFileWriterRegistry.Supports(".opus")
                                          AudioFileWriterRegistry.SupportedExtensions
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
    music.Load(SoundFont soundFont, MidiStream stream)   // and the same four
    music.Load(SfzInstrument instrument, MidiStream stream)          //   twins
    music.Load(DecentSamplerInstrument instrument, MidiStream stream)
    music.Load(IMidiSynthesizer synthesizer, MidiStream stream)
    music.Load(Func<int, IMidiSynthesizer> factory, MidiStream stream)
    music.Stream / music.IsStarved                      // MidiMusicPlayer
    AbcReader.Parse(string text)                    // also Read(string path)
    AbcReader.Read(Stream stream)                   //   and Read(Stream)
    AbcToMidi.Convert(AbcTune tune)
    AbcToMidi.Convert(AbcTune tune, AbcToMidiOptions options)
    new MidiStream(int ticksPerQuarterNote = 480)
    stream.Preroll = TimeSpan.FromSeconds(1)
    stream.AppendNote(long tick, int channel, int note, int velocity, long length)
    stream.AppendTempo(long tick, double beatsPerMinute)
    stream.Append(MidiEvent midiEvent)      // also (IEnumerable<MidiEvent>)
    stream.AdvanceHorizon(long tick)        // a settled rest, recorded nowhere
    stream.TimeAtTick(long tick) / stream.TickAtTime(TimeSpan time)
    stream.Complete()
    stream.HorizonTicks / .HorizonTime / .LateEventCount / .Problems
    stream.ToMidiEventCollection() / stream.ToSequence()
    noteOn.MoveTo(long absoluteTime)        // moves the off event with it
    router.GetChannel(int channel) / router.GetLayer(int channel)
    router.ClearChannel(int channel, bool ringOut)   // also ClearLayer
    router.RingOutReplacedChildren / .RingOutLimit / .RetiredChildCount
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
    SoundFontRenderer.RenderToFile(SoundFont, MidiSequence, string outputPath,
                             int sampleRate = 44100, TimeSpan tail = default)
    SoundFontRenderer.RenderToFile(SoundFont, MidiSequence, string outputPath,
                             WaveFormat format, TimeSpan tail = default)
    SoundFontRenderer.RenderToFile(IMidiSynthesizer, MidiSequence,
                             string outputPath, TimeSpan tail = default)
    SoundFontRenderer.RenderToFile(IMidiSynthesizer, MidiSequence,
                             string outputPath, WaveFormat format,
                             TimeSpan tail = default)
    SoundFontRenderer.RenderToStream(SoundFont, MidiSequence, Stream output,
                             string fileNameOrExtension,
                             int sampleRate = 44100, TimeSpan tail = default)
    SoundFontRenderer.RenderToStream(SoundFont, MidiSequence, Stream output,
                             string fileNameOrExtension, WaveFormat format,
                             TimeSpan tail = default)
    SoundFontRenderer.RenderToStream(IMidiSynthesizer, MidiSequence,
                             Stream output, string fileNameOrExtension,
                             TimeSpan tail = default)
    SoundFontRenderer.RenderToStream(IMidiSynthesizer, MidiSequence,
                             Stream output, string fileNameOrExtension,
                             WaveFormat format, TimeSpan tail = default)
    AudioFileWriterRegistry.Register(IAudioFileWriterFactory factory)
    AudioFileWriterRegistry.Register(string extension,
                                     IAudioFileWriterFactory factory)
    AudioFileWriterRegistry.Supports(string fileNameOrExtension)
    AudioFileWriterRegistry.SupportedExtensions
    AudioFileWriterRegistry.Resolve(string fileNameOrExtension)
    AudioFileWriterRegistry.Create(string fileNameOrExtension, Stream stream,
                                   WaveFormat format)
    AudioFileWriterRegistry.Create(string fileNameOrExtension, Stream stream,
                                   int sampleRate, int channels)
    writer.Write(float[] samples, int offset, int count)   // IAudioFileWriter
    writer.Write(ReadOnlySpan<float> samples)
    writer.Finish() / writer.SamplesWritten / writer.WaveFormat
    new WavAudioFileWriterFactory(int defaultBitsPerSample)    // 16, 24 or 32
    new AiffAudioFileWriterFactory(int defaultBitsPerSample)   // 16 or 24
    InstrumentLibraryRegistry.Register(IInstrumentLibrary library)
    InstrumentLibraryRegistry.Resolve(string name)
    InstrumentLibraryRegistry.SetDefault(string name)
    InstrumentLibraryRegistry.Default / .DefaultName / .Registered
    InstrumentLibraryRegistry.RegisteredNames / .IsRegistered(string name)
    library.CreateSynthesizer(int program, int sampleRate)   // IInstrumentLibrary
    library.CreatePercussionSynthesizer(int sampleRate)
    library.CreateMultiTimbralSynthesizer(int sampleRate)
    library.Coverage.CoversProgram(int program)
    library.Coverage.CoversPercussionNote(int noteNumber)
    library.Coverage.CoversNote(int program, int noteNumber)
    library.Coverage.KeyRangeOf(int program)    // an InstrumentKeyRange
    new SoundFontInstrumentLibrary(string name, string description,
                                   string soundFontPath)   // also Stream, SoundFont
    new MappedInstrumentLibrary(string name, string description,
                                string baseLibraryName)  // also IInstrumentLibrary,
                                                         //   or no base at all
    voices.SetInstrument(program, Func<int, IMidiSynthesizer> factory,
                         InstrumentKeyRange keyRange = default)
    voices.SetInstrument(program, string instrumentPath,
                         InstrumentKeyRange keyRange = default)
    voices.SetInstrumentFromSoundFont(program, string soundFontPath,
                                      int soundFontProgram,
                                      InstrumentKeyRange keyRange = default)
    voices.SetInstrumentFromLibrary(program, string libraryName,
                                    int libraryProgram,
                                    InstrumentKeyRange keyRange = default)
    voices.ClearInstrument(program) / voices.ClearPercussion()
    voices.SetPercussion(...) / .SetPercussionFromSoundFont(...)
                              / .SetPercussionFromLibrary(...)
    voices.BaseLibraryName / .HasBaseLibrary / .SubstitutedPrograms
    voices.HasSubstitute(program) / .HasPercussionSubstitute / .Register()
    new RoutingSynthesizer(int sampleRate)      // also (sampleRate, blockSize)
    router.SetChannel(int channel, IMidiSynthesizer synthesizer, float gain)
    router.SetChannel(int channel, Func<IMidiSynthesizer> factory,
                      float gain = 1.0F)
    router.SetLayer(int channel, IMidiSynthesizer synthesizer, float gain = 1.0F)
    router.SetLayer(int channel, Func<IMidiSynthesizer> factory, float gain = 1.0F)
    router.ClearChannel(int channel) / router.ClearLayer(int channel)
    router.IsRouted(int channel) / router.HasLayer(int channel)
    router.GetChannelGain / .SetChannelGain / .GetLayerGain / .SetLayerGain
    router.MasterVolume / .ActiveVoiceCount / .Synthesizers
    router.UnroutedMessageCount
    new Reverb(int sampleRate)                          // CodeBrix.Audio.Synth
    reverb.Process(float[] input, float[] left, float[] right)   // also (..., count)
    reverb.InputGain / .RoomSize / .Damp / .Wet / .Width / .Mute()
    new Chorus(int sampleRate, double delay, double depth, double frequency)
    chorus.Process(float[] inputLeft, float[] inputRight,
                   float[] outputLeft, float[] outputRight)      // also Mute()
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
                                                //   is the blocking form; both
                                                //   take an explicit sample rate
    player.LevelMeasurement                 // never null; completed until one runs
    player.LastLevelMatch                   // MixPeak / SuggestedVolume / WouldClip
    player.FitVolumeAfterLevelMatching = true   // opt in to acting on it
    player.Problems                         // what a loader could not honour
    player.Render(int sampleRate = 44100, TimeSpan? tailLength = null)
    player.RenderToWav(string path, int sampleRate = 44100)
    player.RenderToFile(string path, WaveFormat format, TimeSpan? tailLength = null)
    player.RenderToStream(Stream output, string fileNameOrExtension, ...)
    player.ExportMergedMidi(string path)
    MidiAudioAlignment.Estimate(IReadOnlyList<double> noteOnTimesSeconds,
                                ReadOnlySpan<float> monoAudio, int sampleRate,
                                double maxOffsetSeconds = 0.3)
    MidiAudioAlignment.GetNoteOnTimesSeconds(MidiSequence sequence)
    SunoStemsLoader.Load(string path)       // also (path, SunoLoadOptions)
    SunoStemsLoader.LoadAsync(string path, CancellationToken ct = default)
    song.CreatePlayer()                     // also (instrumentFactory) and (options)
    song.CreatePlayer(Func<SunoStem, int, IMidiSynthesizer> instruments)
    song.CreatePlayer(new SunoPlayerOptions
    {
        InstrumentLibraryName = "ModestSynthGm",     // or InstrumentLibrary
        MidiStems = SunoStemSelection.EverythingBut("Vocals"),
    })
    options.StemInstruments["Synth"] = rate => new MySynthesizer(rate)
    options.StemInstruments.SetFromFile("Synth", "/packs/Grand/Grand.dspreset")
    song.ExportMergedMidi(string path)
    song["Drums"].AlignmentOffset           // settable; audio = midi + offset
    song["Bass"].UsedNotes / .LowestNote / .HighestNote
    song["Bass"].ReadMonoAudio(out int sampleRate)
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
   14. This package ships NO instruments and registers NO instrument library, so
       nothing sounds through that seam until a consumer registers one.
       GeneralMidiInstrumentLibrary.Register() - from the ModestSynth add-on -
       is the one line that covers all of General MIDI.
   15. The first instrument library registered is the default. Name the one you
       mean with Resolve, or say SetDefault, rather than relying on the order
       two start-up paths happened to run in.
   16. A RoutingSynthesizer's table counts channels 1-16; ProcessMidiMessage
       takes the wire's 0-15. Percussion is 10 in the table and 9 in a message.
   17. An IAudioFileWriter never closes the stream it was handed, and .wav and
       .aiff both need one that can seek.
================================================================================
