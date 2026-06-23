================================================================================
AGENT-README: CodeBrix.Audio
A Comprehensive Guide for AI Coding Agents
================================================================================

OVERVIEW
--------------------------------------------------------------------------------
CodeBrix.Audio is a fully managed, cross-platform audio file library for .NET.
It reads WAV and MP3 waveform audio, reads and writes Standard MIDI Files,
reads MP3 ID3v2 metadata tags, and exposes a set of DSP primitives for audio
analysis. It contains no native code and no platform-specific interop, so it
behaves identically on Windows, macOS, and Linux.

The library is adapted from the open-source NAudio and NLayer projects (both
MIT-licensed). Only the fully managed, cross-platform file-handling, codec,
DSP, and MIDI code was incorporated; all audio-device playback/recording code
and all Windows-only interop were intentionally left out. See
THIRD-PARTY-NOTICES.txt for full attribution.

NON-GOALS (intentionally NOT in this library):
  - Audio playback or recording to/from sound devices. Use CodeBrix's media
    playback library for that.
  - MP3 decoding via Windows ACM/DMO. MP3 decoding is fully managed (NLayer).
  - SoundFont/SFZ parsing, software synthesis/sampling, sequencing, effects.
  - Waveform formats other than WAV and MP3.
  - Audio-to-MIDI transcription (onset/pitch detection). The DSP primitives
    needed to build it are present, but the transcriber itself is future work.


INSTALLATION
--------------------------------------------------------------------------------
NuGet package:   CodeBrix.Audio.MitLicenseForever
Command:         dotnet add package CodeBrix.Audio.MitLicenseForever

Note that the PACKAGE id carries the ".MitLicenseForever" suffix, but the
NAMESPACE is simply "CodeBrix.Audio" (no suffix).

Target framework: .NET 10.0 or higher.


KEY NAMESPACES
--------------------------------------------------------------------------------
  using CodeBrix.Audio.Wave;       // readers/writers, WaveFormat, MP3 frames, ID3
  using CodeBrix.Audio.Midi;       // MIDI file read/write + event hierarchy
  using CodeBrix.Audio.Dsp;        // FFT, biquad filters, analysis primitives

(Additional sub-namespaces exist for plumbing — sample/wave providers, codecs,
utilities. The managed MP3 decoder under CodeBrix.Audio.Mpeg is entirely
internal; consumers reach MP3 only through the readers in CodeBrix.Audio.Wave.
Consumers normally only touch the three namespaces above.)


CORE API REFERENCE
--------------------------------------------------------------------------------
Reading audio (WAV or MP3):
  - WaveFileReader        : reads a .wav stream/file as a WaveStream.
  - Mp3FileReader         : reads a .mp3 stream/file as a WaveStream, decoding
                            MPEG audio to PCM via the managed NLayer decoder.
  - AudioFileReader       : convenience reader that opens .wav or .mp3 by file
                            extension and exposes 32-bit float samples.

Writing audio:
  - WaveFileWriter        : writes PCM/IEEE-float samples to a .wav file.

WaveFormat:
  - WaveFormat            : sample rate, channel count, bit depth, encoding.

MP3 metadata:
  - Id3v2Tag              : reads an ID3v2 tag block from an MP3 stream.

MIDI:
  - MidiFile              : reads a Standard MIDI File; MidiFile.Export(...)
                            writes one.
  - MidiEvent (hierarchy) : NoteOnEvent, NoteEvent, TextEvent, MetaEvent,
                            TempoEvent, TimeSignatureEvent, etc.
  - MidiEventCollection   : per-track event collection used for read and write.

DSP / analysis primitives (CodeBrix.Audio.Dsp):
  - FastFourierTransform, Complex   : forward/inverse FFT.
  - BiQuadFilter                    : low/high/band-pass, peaking, shelving.
  - EnvelopeFollower                : amplitude envelope tracking.
  - VoiceActivityDetector           : energy-based activity detection.

Error model: invalid/corrupt files throw standard exceptions (e.g.
FormatException, EndOfStreamException, ArgumentException). Readers/writers are
IDisposable; dispose them (or use `using`) to release the underlying stream.


CODING CONVENTIONS (CodeBrix family)
--------------------------------------------------------------------------------
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
  - Files adapted from NAudio/NLayer carry a `//was previously: <ns>;`
    provenance comment on the namespace line and preserve upstream license
    headers where present.
  - Tests use xUnit v3 + SilverAssertions; see TESTING.


ARCHITECTURE
--------------------------------------------------------------------------------
Source lives under src/CodeBrix.Audio/, organized into sub-folders that map to
sub-namespaces (e.g. Midi, Dsp, Codecs, and internal wave/MP3 plumbing). Only
the entry-point readers/writers and WaveFormat sit at or near the root
namespace; the rest is implementation detail. The MP3 read path is built on the
managed NLayer decoder wired to an MP3 frame-decompressor interface adapted
from NAudio, so no native MP3 codec is ever required.


TESTING
--------------------------------------------------------------------------------
Tests live under tests/CodeBrix.Audio.Tests/ and use xUnit v3 with
SilverAssertions (fluent assertions) and CodeBrix.TestMocks (mocking +
AutoFixture; Moq/AutoFixture-identical API under CodeBrix.TestMocks.* names).
Run them with:

    dotnet test CodeBrix.Audio.slnx

The suite has ~550 tests. Most are adapted from the upstream NAudio.Core.Tests
project (converted from NUnit to xUnit v3 + SilverAssertions); the remainder are
authored for the CodeBrix-specific entry points (AudioFileReader, Mp3FileReader)
and the analysis primitives. Coverage includes: WAV reading/writing round-trips,
MP3 frame parsing and full managed decode, MIDI read/write round-trips and the
event hierarchy, ID3v2 tag reading, the codecs, and the DSP primitives. All
audio fixtures are generated in code (synthesized WAV, hand-built silent MP3
frames) - no binary test assets are vendored.

A handful of tests carry [Fact(Skip = "...")]: NUnit [Explicit] tests carried
over as skipped, plus two manual performance tests.
================================================================================
