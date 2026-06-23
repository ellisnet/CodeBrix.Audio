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
  - Sample-rate conversion / resampling. The NAudio WDL resampler was not
    incorporated, so there is no built-in resampler; convert sample rates with
    your own code or another library if you need it.
  - General N-source mixing of sample providers. The float MixingSampleProvider
    (which needed System.Numerics.Tensors) was not incorporated; mixing is
    available at the IWaveProvider level via MixingWaveProvider32.


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


SAMPLE CODE
--------------------------------------------------------------------------------
Read a WAV or MP3 file as 32-bit float samples (simplest path):

    using CodeBrix.Audio.Wave;

    using var reader = new AudioFileReader("track.mp3");   // or "clip.wav"
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

Decode MP3 explicitly (fully managed; no native codec needed):

    using var mp3 = new Mp3FileReader("song.mp3");          // WaveStream of PCM
    var floats = mp3.ToSampleProvider();

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
    float WAV only. A-law / mu-law (and other non-PCM) WAV files THROW
    (InvalidOperationException) - there is no managed codec conversion. (A-law /
    mu-law decoders exist under CodeBrix.Audio.Codecs but are not auto-wired.)
  - MP3 coverage: decoding is fully managed (NLayer) and covers MPEG-1/2/2.5
    Layer I/II/III. There is no Windows ACM/DMO/Media Foundation path.
  - Dispose readers and writers (use `using`). A WaveFileWriter only flushes a
    valid RIFF header on Dispose - an undisposed writer produces a corrupt file.
  - MIDI export: call MidiEventCollection.PrepareForExport() before
    MidiFile.Export(). A type-0 collection may contain only one track (Export
    throws otherwise); use type 1 for multi-track files. NoteOnEvent
    auto-creates its paired note-off.
  - No resampling: there is no built-in sample-rate converter (see NON-GOALS).
  - DSP is primitives only: there is no turnkey onset/pitch/beat detector or
    audio-to-MIDI transcriber - build those on top of the FFT / filters /
    envelope follower.
  - Threading: a single reader/stream instance is not thread-safe; give each
    thread its own reader.


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
