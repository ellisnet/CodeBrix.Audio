================================================================================
MAINTAINER-README: CodeBrix.Audio
Notes for people and agents MAINTAINING this repository - not for package
consumers
================================================================================

If you are CONSUMING the NuGet package, read AGENT-README.txt instead. Nothing
in this file is needed to use the package.


PURPOSE AND SCOPE
=================
This repository produces exactly ONE NuGet package, which ships TWO assemblies:

  CodeBrix.Audio.MitLicenseForever
      Assemblies:    CodeBrix.Audio  (src/CodeBrix.Audio/)
                     CodeBrix.Audio.Engine  (src/CodeBrix.Audio.Engine/),
                     bundled into the same package rather than published
                     separately
      Native payload: codebrix_miniaudio, for seven runtime identifiers
      License:       MIT
      Consumer doc:  AGENT-README.txt (repo root) - covers BOTH assemblies

There is no second package. The Engine is referenced with PrivateAssets="all"
so it is never surfaced as a NuGet dependency; a custom
TargetsForTfmSpecificBuildOutput target injects its .dll and .xml into lib/, and
a None item packs its runtimes/<rid>/native/ payload.

THE LICENCE BAR, AND WHY IT MATTERS TO EVERY DECISION HERE. The bar for
CodeBrix.Audio is MIT or more permissive, and the package id -
CodeBrix.Audio.MitLicenseForever - states it publicly. Everything vendored so
far clears it (NAudio, NLayer, NVorbis, SoundFlow and MeltySynth are MIT;
miniaudio and stb_vorbis are Unlicense/MIT-0). The first licence to add a
condition - BSD-3, for Opus - was pushed into a SEPARATE package,
CodeBrix.Audio.Opus.BsdLicenseForever, in its own repository. That is the
standing precedent for the family: a licence that adds a condition gets its own
package, and this one stays what its id claims.


REPOSITORY LAYOUT
=================
  src/CodeBrix.Audio/            the main library
  src/CodeBrix.Audio.Engine/     the bundled engine (vendored; see PROVENANCE)
  native/miniaudio/              C sources and CMake project for the native
                                 backend, plus BUILD-PROVENANCE.txt
  tools/build_native_libraries/  the native build + verification driver
  tools/make_test_fixtures/      fixture generators (see EXTRAS-README.txt)
  tools/sfz_opcode_survey/       SFZ opcode coverage tool (see EXTRAS-README.txt)
  tests/CodeBrix.Audio.Tests/        the main test project
  tests/CodeBrix.Audio.Engine.Tests/ native-decode-path tests
  tests/Assets/                      audio, soundfont and synth fixtures
  CodeBrix.Audio.slnx                the solution
  global.json                        pins the test runner
  THIRD-PARTY-NOTICES.txt            the authoritative provenance record

SOURCE ORGANISATION (moved here from the old AGENT-README's ARCHITECTURE
section):

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



BUILDING
========
    dotnet build CodeBrix.Audio.slnx

The managed build needs nothing beyond the .NET SDK: the native binaries are
already committed under
src/CodeBrix.Audio.Engine/Backends/MiniAudio/runtimes/<rid>/native/, so an
ordinary build does not compile any C.

GeneratePackageOnBuild is ON in the library csproj, so an ordinary build also
produces a .nupkg - see PACKAGING AND PUBLISHING.

REBUILDING THE NATIVE LIBRARY
-----------------------------
Only needed when the C sources, the CMake project or a runtime identifier
changes.

The native library is built and verified by tools/build_native_libraries - READ
ITS README.txt BEFORE TOUCHING ANYTHING NATIVE. It builds all three Linux RIDs
in manylinux containers on one machine (arm64 and riscv64 under emulation) and
carries host scripts for Windows and macOS; every build must pass a verification
gate (required exports, codec coverage, dependency policy, compatibility floor,
target architecture, and a dlopen + decode smoke test) before it is written to
output/. Nothing there installs anything on your machine: every script checks
for what it needs, names anything missing, prints the command that installs it,
and stops.

Why containers even for x64: glibc symbol versioning is forward-only, so
building on a current desktop distro would quietly restrict the package to the
newest distributions, and the failure would only show up on a user's machine.
The manylinux images are old userlands with modern compilers, which fixes the
floor. macOS has the same problem in Mach-O form and is solved with an explicit
CMAKE_OSX_DEPLOYMENT_TARGET from pins.env rather than a container. Windows has
no equivalent problem and is built natively.

Build inputs live under native/miniaudio/ - library.c (the thin C wrapper with
the sf_* entry points and the sf_has_vorbis capability probe), library.h,
CMakeLists.txt, the vendored miniaudio single header, and the vendored
stb_vorbis. miniaudio keeps its Vorbis support switched off unless stb_vorbis is
compiled into the same translation unit, which is exactly what library.c does.
native/miniaudio/BUILD-PROVENANCE.txt records what produced each shipped binary,
and the folder names of the vendored sources record the exact upstream commits
they came from.

All seven shipped RIDs are self-built from the vendored sources and all seven
include the Ogg Vorbis decoder, so sf_has_vorbis() is present everywhere. The
managed Vorbis fallback now only covers a binary that lacks it - for example a
RID added later, before one is built for it.


TESTING
=======
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


PACKAGING AND PUBLISHING
========================
  PackageId              CodeBrix.Audio.MitLicenseForever
  License expression     MIT, with PackageRequireLicenseAcceptance set
  GeneratePackageOnBuild true - every build writes a fresh .nupkg
  NuGet dependencies     none

  WHAT SHIPS IN THE NUPKG
    lib/net10.0/CodeBrix.Audio.dll (+ .xml)
    lib/net10.0/CodeBrix.Audio.Engine.dll (+ .xml), injected by the
        IncludeEngineAssemblyInPackage target rather than by a package reference
    runtimes/<rid>/native/...    the seven native backends, packed from
        src/CodeBrix.Audio.Engine/Backends/MiniAudio/runtimes/
    icon-codebrix-128.png        the package icon
    README.md                    the nuget.org / GitHub landing page
    AGENT-README.txt             the consumer guide - THIS is the file that
        reaches consumers, so keep it consumer-only
    THIRD-PARTY-NOTICES.txt      required by the vendored sources' licences

  MAINTAINER-README.txt, EXTRAS-README.txt and README-INDEX.txt are NOT packed.
  They exist for this repository only.

  The Engine csproj is referenced with PrivateAssets="all". Do not turn that into
  an ordinary ProjectReference or a PackageReference: the whole point is that
  consumers get one package id and two assemblies.

VERSIONING. The csproj computes the version from the UTC clock at build time:
1.<years since 2026>.<day of year>.<minute of day>. It is strictly increasing
over time and is NOT SemVer - major is pinned to 1 and minor encodes the year,
so major and minor say nothing about API compatibility. Two builds in the same
UTC minute produce the SAME version, so never publish two packages from within
one minute. The full rationale is in a comment block in the csproj; re-baseline
by changing _VersionBaseYear there.

Publishing follows the family rule: tag the repository at the version that was
published, so the latest git tag and the latest nuget.org version agree.

DOWNSTREAM. CodeBrix.Audio.Opus.BsdLicenseForever pins a version of this package
in its own csproj, and the CodeBrix.Platform AudioPlayer add-in and GameEngine
build on it. A breaking change to the codec-registration seams, to
SharedAudioOutput or to AudioFileReaderRegistry is a breaking change for them.


THE PACKET SEAM
===============
Audio lifted out of a media container arrives as bare codec packets, not as a
file, so alongside the stream seam (ICodecFactory / ISoundDecoder) there is a
packet seam: IPacketCodecFactory / IPacketSoundDecoder, with
AudioEngine.RegisterPacketCodecFactory / UnregisterPacketCodecFactory /
SetPacketCodecPriority / GetRegisteredPacketCodecs / CreatePacketDecoder behind
them and SharedAudioOutput.RegisterPacketCodecFactory as the process-wide front
door. Consumer documentation is in AGENT-README.txt.

WHY IT LIVES IN THE ENGINE. The interfaces sit in
CodeBrix.Audio.Engine.Interfaces beside the two they mirror, and the registry is
the same machinery in the same class, because that gives implementers ONE
registration model, ONE priority model, and one place to look; a second registry
somewhere else would have been a second set of rules to learn and to keep in
step. The Engine is ordinary CodeBrix code and is edited freely, so there was no
reason to route around it. Two things deliberately did NOT follow: the registry
keys on CODEC identifiers ("vorbis") rather than container format identifiers
("ogg") and therefore uses its own dictionary, and PacketAudioPlayer stays in
CodeBrix.Audio because it is built on ManagedSoundDecoder, which lives there.

THE VORBIS SINGLE-PACKET ENTRY POINT. The managed Vorbis decoder is pull-model:
StreamDecoder asks an IPacketProvider for the next packet, and a null answer
means PERMANENT end of stream (_eosFound latches, and only a seek clears it). A
provider fed one packet at a time would therefore poison the decoder the first
time the demultiplexer ran dry, so the packet path pushes instead:

  - StreamDecoder.DecodeNextPacket was split in two. The fetch half still pulls
    from the provider; the decode half, DecodePacketBody, takes the packet as an
    argument. Behaviour of the pulling path is unchanged - it is the same code,
    one call deeper.
  - StreamDecoder.DecodeSinglePacket(IPacket, Span<float>) is the packet entry
    point: it runs DecodePacketBody, then repeats ReadNextPacket's overlap
    bookkeeping and Read's copy-out, MINUS the granule-position trim and the
    end-of-stream drain. Both of those are the CONTAINER's business - Ogg states
    the exact end in its granule position, a media container in its own fields -
    so on this path the caller applies them.
  - ResetOverlapState() exposes the private ResetDecoder for a seek, and
    MaxPacketSampleCount reports the largest number of samples one packet can
    make final: (3 * block1 / 4) - (block0 / 4) per channel, which is a long
    block with a long left neighbour and a short right one. That is MORE than
    the block1/2 figure a first estimate suggests, and sizing a buffer to the
    smaller number would throw on real streams.
  - The two supporting types are small: MemoryDataPacket is a DataPacket over
    ReadOnlyMemory<byte>, reused for every packet so the path allocates nothing
    per packet, and HeaderPacketProvider hands the decoder's constructor the
    three setup headers un-laced out of the container's codec-private data and
    nothing more. Header parsing therefore stays in the one place it already
    was; there is no second header parser.

WHAT THE ROUND-TRIP TEST PINS. VorbisPacketCodecFactoryTests takes an Ogg
fixture apart with a test-side page reader, re-frames its three headers the way
a container carries them, and decodes every audio packet through the seam: the
result must equal VorbisReader's output on the same file SAMPLE FOR SAMPLE. The
two lengths differ by less than one window at the very end, which is exactly the
trailing trim described above, so the comparison runs over the common prefix and
bounds the difference. The reset test pins the other half: one packet of
pre-roll after Reset() and the audio is identical to the uninterrupted decode,
not merely close to it.

PACKETAUDIOPLAYER AND THE LIVE-STREAM PATH. Its data provider reports a Length
of 0, which the engine's SoundPlayerBase reads as "live stream": when a read
comes back empty it clears the buffer and carries on rather than ending
playback. That is what makes an underrun harmless - the pump returns silence and
the voice stays in the mixer - but it also means the engine never raises
PlaybackEnded for this player, so PacketAudioPlayer raises its own from the
provider's end-of-stream event, once, marshalled off the audio thread (the
captured SynchronizationContext if there is one, otherwise the thread pool -
never inline, because the handler stops the voice). The clock counts frames
handed over plus frames discarded as priming or pre-roll, and never counts
underrun silence.


PROVENANCE AND VENDORED SOURCES
===============================
THIRD-PARTY-NOTICES.txt is the authoritative record of what came from where,
what was changed, and under which licences. Consult it rather than this file for
any provenance question.

Summary: parts of CodeBrix.Audio are adapted from NAudio, NLayer, NVorbis and
MeltySynth (all MIT); the FLAC decoder and the SFZ engine were written here from
their specifications; CodeBrix.Audio.Engine is vendored SoundFlow (MIT); the
native backend is built from miniaudio and stb_vorbis (Unlicense / MIT-0).

MAINTAINING CODEBRIX.AUDIO.ENGINE
---------------------------------
src/CodeBrix.Audio.Engine/ began as a ~35k-line verbatim vendoring of SoundFlow
v1.4.1 (LSXPrime/SoundFlow, MIT) with namespaces renamed SoundFlow.* ->
CodeBrix.Audio.Engine.* (each namespace line carries a `//was previously:`
comment). It is CodeBrix code now: it is maintained and modified here and is NOT
kept in sync with upstream. Native build inputs live under native/miniaudio/ (miniaudio.h vendored
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

It keeps SoundFlow's own project settings: NRT is ON (the code uses `?` and
`!`), ImplicitUsings is ON, AllowUnsafeBlocks is ON. Code added or changed in
the Engine follows the Engine's local style rather than family style. Editing
the Engine source is FINE. The vendoring kept changes minimal in the interest
of completing that project quickly, but that was a scheduling decision, not a
policy - there is nothing special about this code that should discourage
modifying it going forward.

DELIBERATE DIVERGENCES FROM UPSTREAM SOUNDFLOW - nine changes made during the
vendoring, recorded here for provenance:

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

  9. ChunkedDataProvider.Length counts the DECODER's channels, not the file's.
     Upstream multiplies FormatInfo.Duration by SampleRate and
     FormatInfo.ChannelCount - mixing units, because SampleRate is already the
     decoder's while ChannelCount is the FILE's. SoundPlayerBase.Duration then
     divides that length by the DEVICE's channel count, so every mono file
     reported exactly half its true duration on a stereo device: a two-minute
     podcast showed as one minute, and any transport bound to it scrubbed the
     wrong range. Stereo files hid it, because there the two counts agree.
     tests/CodeBrix.Audio.Engine.Tests/ChunkedDataProviderTests.cs pins it, mono
     and stereo, and at a sample rate that forces conversion as well.
     The same expression appears as a FALLBACK in StreamDataProvider.Length and
     in AssetDataProvider.Decode, reached when the decoder reports no length of
     its own - which the native decoder does whenever it runs through read
     callbacks rather than from memory (see divergence 6). Both were fixed the
     same way, and BOTH must be re-applied.
     AssetDataProvider's is the one that really bites: its fallback sizes the
     buffer the whole asset is decoded into, in a single Decode call, and that
     code only ever resizes the buffer DOWN. Taken from the file's layout the
     value does not merely mis-describe the clip, it CUTS IT OFF - a mono sound
     effect came back half as long, a 22.05 kHz one under a quarter.
     tests/CodeBrix.Audio.Engine.Tests/ProviderLengthFallbackTests.cs pins both,
     forcing the path with a test codec that decodes normally but reports a
     length of 0 (LengthlessCodec.cs).


WHAT MAY BE READ WHEN PORTING, AND WHAT MAY BE TAKEN
---------------------------------------------------
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



CODING CONVENTIONS
==================
These conventions govern the CodeBrix.Audio assembly and its tests. They do NOT
govern CodeBrix.Audio.Engine, which keeps the upstream project's own settings -
see MAINTAINING CODEBRIX.AUDIO.ENGINE above.

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
  - Tests use xUnit v3 + SilverAssertions; see TESTING above. Test files are
    named <Class>Tests.cs, methods are snake_case, and bodies mark
    //Arrange //Act //Assert.



NOTES
=====
  - A HISTORICAL DEADLOCK, KEPT HERE BECAUSE IT EXPLAINS OLD BUG REPORTS. A few
    Engine entry points are synchronous wrappers around async I/O -
    SoundMetadataReader.Read, SoundMetadataWriter.WriteTags/RemoveTags,
    Recorder.StopRecording, and anything that opens a source through them. In
    packages published before the ConfigureAwait(false) sweep (divergence 3
    above), those calls could DEADLOCK a UI thread outright:
    the same calls can DEADLOCK a UI thread outright - the window never paints, and
    there is no exception and no log entry to tell you why. It is file-dependent, so
    it looks intermittent: a read served from the stream buffer completes
    synchronously and slips through, while an MP3 carrying a large ID3 tag (embedded
    album art, say) hangs. On those versions, always open audio sources from a
    background thread and marshal the result back to the UI.
    The sweep fixed it, so the AGENT-README states the current behaviour as a
    fact and does not carry a version pin. If someone reports this symptom, the
    first question is which package version they are on.

  - THE OPUS SPLIT IS THE FAMILY PRECEDENT. It is recorded in PURPOSE AND SCOPE
    above and in CodeBrix.Audio.Opus's own MAINTAINER-README.txt. Do not fold
    that codec back in.

  - AGENT-README.txt IS SHIPPED TO CONSUMERS. It must stay consumer-only, must
    carry no version numbers (a vendored upstream's version in a one-line
    provenance statement is the only exception), and must not describe building,
    testing, packaging or repository layout. Anything of that kind belongs in
    this file.
================================================================================
