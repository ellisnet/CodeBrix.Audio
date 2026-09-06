================================================================================
MAINTAINER-README: CodeBrix.Audio
Notes for people and agents MAINTAINING this repository - not for package
consumers
================================================================================

If you are CONSUMING the NuGet package, read AGENT-README.txt instead. Nothing
in this file is needed to use the package.


PURPOSE AND SCOPE
=================
This repository produces TWO NuGet packages. The first ships TWO assemblies:

  CodeBrix.Audio.MitLicenseForever
      Assemblies:    CodeBrix.Audio  (src/CodeBrix.Audio/)
                     CodeBrix.Audio.Engine  (src/CodeBrix.Audio.Engine/),
                     bundled into the same package rather than published
                     separately
      Native payload: codebrix_miniaudio, for seven runtime identifiers
      License:       MIT
      Consumer doc:  AGENT-README.txt (repo root) - covers BOTH assemblies

  CodeBrix.Audio.ModestSynth.MitLicenseForever
      Assembly:      CodeBrix.Audio.ModestSynth
                     (src/CodeBrix.Audio.ModestSynth/)
      Native payload: none
      License:       MIT
      Consumer doc:  src/CodeBrix.Audio.ModestSynth/AGENT-README.txt, packed with
                     ITS package in place of the repo-root pair

The Engine is not a package: it is referenced with PrivateAssets="all" so it is
never surfaced as a NuGet dependency; a custom TargetsForTfmSpecificBuildOutput
target injects its .dll and .xml into lib/, and a None item packs its
runtimes/<rid>/native/ payload. The ADD-ON is a real package with a real
dependency on the core, and the two are versioned and published together - see
PACKAGING AND PUBLISHING.

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
  src/CodeBrix.Audio.ModestSynth/  the synthesis add-on - its OWN package, its
                                 own README.md and AGENT-README.txt packed with
                                 it in place of the repo-root pair
  native/miniaudio/              C sources and CMake project for the native
                                 backend, plus BUILD-PROVENANCE.txt
  tools/build_native_libraries/  the native build + verification driver
  tools/make_test_fixtures/      fixture generators (see EXTRAS-README.txt)
  tools/sfz_opcode_survey/       SFZ opcode coverage tool (see EXTRAS-README.txt)
  tools/ds_feature_survey/       Decent Sampler feature coverage tool - see THE
                                 DECENT SAMPLER ENGINE, and its own README.txt
  tests/CodeBrix.Audio.Tests/        the main test project
  tests/CodeBrix.Audio.Engine.Tests/ native-decode-path tests
  tests/CodeBrix.Audio.ModestSynth.Tests/  the add-on's test project
  tests/Assets/                      audio, soundfont and synth fixtures
  CodeBrix.Audio.slnx                the solution. The Solution Items folder
                                     carries .gitignore, AGENT-README.txt,
                                     EXTRAS-README.txt, global.json,
                                     icon-codebrix-128.png, LICENSE,
                                     MAINTAINER-README.txt, README-INDEX.txt,
                                     README.md, THIRD-PARTY-NOTICES.txt and
                                     Directory.Build.props; the Tests folder
                                     carries the three test projects
  Directory.Build.props              computes $(BuildVersion) once for the whole
                                     repository - see BUILDING
  global.json                        selects the test runner. It does NOT pin an
                                     SDK version - see BUILDING and TESTING
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
AGENT-README.txt, "TWO SOUNDFONT PATHS", before touching any of it.

The multi-track player and the stems loader follow the same shape again:
CodeBrix.Audio.Playback holds MultiTrackPlayer, its two track types and the
alignment estimator, with the renderers, the mix and the merged-MIDI builder
internal under Playback/Internal; CodeBrix.Audio.Playback.Suno holds one
LOADER for one file layout, with the zip/folder stores, the WAV header probe
and the alignment seam internal under Playback/Suno/Internal. The loader knows
about the player and the player knows nothing about the loader, which is the
point: any other stems layout becomes a second loader over the same tracks.

The synthesis add-on follows the shape the family uses for an add-on package,
the one CodeBrix.Audio.Opus set: its own csproj under src/, its own
InternalsVisibleTo.cs, an ordinary ProjectReference to CodeBrix.Audio so pack
emits a real dependency, and one static class whose Register() plugs it into the
core. Source sits in sub-folders that map to sub-namespaces - Oscillators/ for
the classic generators, Fm/ for the six-operator engine, Wavetable/ and
Harmonic/ for the other two, Effects/ for the seven creative processors, Patch/
for the parameter model, Integration/ for the sampler-side voice adapter, and
Internal/ for the band-limiting maths, the seeded noise source and the
registration list. Its docs are
its own: src/CodeBrix.Audio.ModestSynth/README.md and AGENT-README.txt are what
its package carries, and the repo-root pair stay CodeBrix.Audio's. The two things
its oscillators were calibrated against, rather than guessed at, are recorded in
the code where the constants are: the noise level and the plucked string's decay
and brightness, all measured from recordings of the reference sampler.

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

The MIDI Polyphonic Expression rules are written ONCE, in
CodeBrix.Audio.Synth.Mpe, and every synthesizer in the package uses that one
copy. MpeMode and MpeZoneInfo are public; MpeChannelState - the whole contract -
is internal. It owns per-channel controller, bend, tuning and pressure state for
all sixteen channels, the registered-parameter state machine that configures
them (RPN 0 bend sensitivity, RPN 1 and 2 tuning, RPN 6 zone configuration, RPN
null, and the data increment/decrement controllers), the zone model that says
which channels combine with which master, automatic zone detection for a file
carrying no configuration message, the newest-note-owns-the-channel rule, and
the lift velocity. None of it is a format's invention, which is why it does not
live inside any one format's namespace: it is the MIDI 1.0 registered-parameter
rules plus the MPE specification. Channel numbers are 0-BASED throughout the
class, as MIDI messages carry them, while MpeZoneInfo reports them 1-based, as
the specification writes them; getting that wrong is off by one zone.

An engine consumes it in two steps. It feeds every channel-voice message to
ProcessMessage, and then, wherever it reads per-channel state to render a voice,
it asks the contract instead of its own channel object: PitchOffsetSemitones for
the bend and the tunings, Timbre and Pressure for the note's expression,
OwnsChannelExpression for the freeze rule, and ControllerSourceChannel to decide
whether a controller belongs to the member or to its zone master. That last one
exists because both the SoundFont and the SFZ engine store controllers at a
higher resolution than the contract's seven-bit copy - fourteen-bit coarse-and-
fine pairs and normalised floats with set_hd_ccN initial values - so they pick
the channel from the contract and then read their OWN state for it.

The reads are gated on HasActiveZone, and no zone is ever active in MpeMode.Off.
That is not an optimisation, it is the guarantee: with the zones switched off
every engine takes the code path it took before any of this existed, and the MPE
tests fence it, the SFZ pair and the SoundFont pair alike: one test renders the
same performance twice on the one machine, with the settings applied and
switched off and without them, and demands the two agree bit for bit; the other
holds the render to values pinned with a tolerance. Any new read of MPE state
must sit behind the same gate. See PINNED RENDERS AND THE PLATFORM MATHS LIBRARY,
under TESTING, for why a committed digest is the wrong fence for either pair.


BUILDING
========
    dotnet build CodeBrix.Audio.slnx

The managed build needs nothing beyond the .NET SDK: the native binaries are
already committed under
src/CodeBrix.Audio.Engine/Backends/MiniAudio/runtimes/<rid>/native/, so an
ordinary build does not compile any C.

GeneratePackageOnBuild is ON in both packable csprojs, so an ordinary build also
produces two .nupkg files - see PACKAGING AND PUBLISHING.

THE SHARED VERSION. Directory.Build.props at the repository root computes
$(BuildVersion) - 1.<years since 2026>.<day of year>.<minute of day>, UTC - and
every project imports it, so CodeBrix.Audio, CodeBrix.Audio.Engine and
CodeBrix.Audio.ModestSynth are stamped with one value instead of three. That
matters twice over: the Engine assembly is bundled inside the core package and
would look odd carrying a version a minute away from it, and the add-on's
dependency on the core has to resolve exactly. Only $(BuildVersion) is defined
there; mapping it onto Version/AssemblyVersion/FileVersion is each packable
project's business, so the test projects and tools/sfz_opcode_survey keep the
SDK's default 1.0.0 and are unaffected. MSBuild evaluates that file once per
PROJECT rather than once per build, so a publish pins the value instead of
trusting the clock - see PACKAGING AND PUBLISHING.

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
tests/CodeBrix.Audio.ModestSynth.Tests/ covers the synthesis add-on with the
same xUnit v3 and SilverAssertions versions (it needs no mocking, so it takes no
CodeBrix.TestMocks reference) and reaches CodeBrix.Audio transitively through the
add-on's project reference, which is how it gets the FFT its spectrum
measurements are built on. Run everything with:

    dotnet test CodeBrix.Audio.slnx

THE TEST RUNNER IS Microsoft.Testing.Platform (MTP), selected by global.json at
the repo root:

    { "test": { "runner": "Microsoft.Testing.Platform" } }

That file does NOT pin an SDK version, so the newest installed .NET 10 SDK is
still used; selecting the runner is all it does. Because the setting lives in
global.json rather than in the test csproj, it applies to every `dotnet test`
run anywhere in the repository. Keep it committed - without it `dotnet test`
falls back to the older VSTest bridge. You can tell which one ran: MTP output
ends in a "Test run summary:" block, while the VSTest bridge invokes MSBuild
with `--target:VSTest`.

NO COVERAGE COLLECTOR IS REFERENCED. Neither test project carries
coverlet.collector any more; `dotnet test` produces test results and nothing
else. If you want coverage, add the collector locally for the run rather than
committing it back into the csproj files.

There are three test projects: tests/CodeBrix.Audio.Tests (the bulk of them),
tests/CodeBrix.Audio.Engine.Tests (~70, which exercise the native decode path
without opening a device) and tests/CodeBrix.Audio.ModestSynth.Tests (the
synthesis add-on). The core project deliberately does NOT reference the add-on:
what a consumer who never installed it hears is only testable from a project that
cannot see it, so the "without the add-on" cases live in the core project and the
"with it" cases live in the add-on's. Most of the core project's tests are
adapted from the upstream
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

  - AudibleTestScope wraps every sounding test. The test assemblies run as separate
    PROCESSES, so without the named mutex inside it they play over each other and the
    tune turns to mush. It also leaves a short silence on the way out so consecutive
    tests stay distinct. There is a copy of the class in each test project, all three
    naming the same mutex; that is deliberate, because the projects share no assembly.
    The motif is played through a SoundFont, an SFZ instrument, a Decent Sampler preset
    (Synth/DecentSampler/Engine/DecentSamplerAudibleTests) and, in the add-on's project,
    a Decent Sampler preset whose group holds an oscillator rather than a sample
    (Integration/ModestSynthAudibleTests) - four renderings of the same five notes, so
    a run tells you by ear which engine broke.
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

PINNED RENDERS AND THE PLATFORM MATHS LIBRARY
---------------------------------------------
A RENDER CANNOT BE PINNED BIT FOR BIT ACROSS OPERATING SYSTEMS. Four tests used
to try - two in SfzRenderRegressionTests and two in MpeSfzEngineTests - and they
passed on Linux, where their numbers were recorded, and failed on Windows the
first time anyone ran them there. Nothing was wrong with the library. The numbers
were only ever a description of one machine's C runtime. Two more, in
MpeSoundFontEngineTests, pinned the same kind of digest and happened to agree on
both machines: the SoundFont voice path reaches powf, sinf and exp exactly as the
SFZ path does, so that agreement was luck, and they were converted to the same
fences before a third platform could prove it.

WHY. MathF.Sin and MathF.Pow compile to the platform's sinf and powf: UCRT on
Windows, glibc on Linux, Apple's libm on macOS. None of the three is correctly
rounded and they do not agree. Measured on Windows against a correctly-rounded
reference, sinf differs by one ulp on 34 arguments in 20,000, and powf on about
600 in 1,000. Both reach these tests - sinf built the sine WAV the fixtures play,
and powf is in the gain and envelope paths of every render. There is no build
switch for this and no way to opt out; the functions are the platform's.

The same applies to any Linux-versus-Linux pair, which is the part that makes
per-platform constants a dead end rather than merely ugly: glibc has changed sinf
across releases, musl is not glibc, and arm64 macOS is neither. Pinning a set of
numbers per platform means pinning them per libc version per architecture, and
finding out which by watching CI break.

WHAT IS SAFE TO PIN, AND WHAT IS NOT. Two things in the engines are exactly
specified everywhere, and both are worth knowing before anyone tries to "fix"
them:

  - SfzOscillator resamples in FIXED POINT (position_fp, fracBits = 24), so
    playback phase cannot drift between platforms. A looped render measured
    identical at 30 turns and at 220. Do not convert it to a float accumulator.
  - ArrayMath.MultiplyAdd is the only SIMD in the library and it is elementwise,
    so Vector<float>.Count going from 8 on AVX2 to 4 on NEON to 16 on AVX-512
    changes nothing. Do not add a horizontal reduction to it without thinking
    about this. .NET does not contract multiply-add into FMA on its own, so arm64
    does not diverge there either.

Everything else - anything downstream of sinf or powf - needs a tolerance.

HOW THESE TESTS ARE FENCED NOW. Three mechanisms, in the order to reach for them:

  1. COMPARE TWO RENDERS ON THE SAME MACHINE where the test's claim allows it.
     the_mpe_settings_do_not_reach_a_render_with_the_zones_switched_off is not
     really claiming "the render is this number", it is claiming the MPE settings
     never reach the render. So it renders the performance twice - once with the
     settings applied and switched off, once on a synthesizer that never had them
     - and demands the digests match. That is stricter than a pinned digest, and
     it cannot care what platform it runs on. Prefer this shape wherever a test
     is really asserting an invariance.

  2. PIN SAMPLES WITH A TOLERANCE for the genuine regression fences. See
     tests/CodeBrix.Audio.Tests/Synth/PinnedRender.cs - one place, one tolerance,
     so there is one number to argue with. Samples are read at a fixed stride
     ACROSS the render rather than from its first block, because loop drift shows
     up nowhere near the start.

     Individual samples, NOT an aggregate. RMS over a block is the tempting shape
     and the wrong one: the RMS of a sine does not depend on its phase, so it is
     blind to exactly the loop drift these tests exist to catch. A sum over the
     whole render is kept alongside the samples, with its own looser tolerance,
     as aggregate cover over the frames not spot-checked.

  3. KEEP THE BIT-EXACT ASSERTIONS THAT ARE ACTUALLY BIT-EXACT. Two renders on
     one machine must still agree exactly - SfzRenderRegressionTests still opens
     with first.Should().Equal(second), and that is not a candidate for a
     tolerance.

WHY 1e-4, AND THE ROOM EITHER SIDE OF IT. The tolerance was not guessed. On these
renders, whose signal peaks around 0.11:

    cross-platform noise, no filter in path         7.5e-9
    cross-platform noise, through a resonant biquad  5.4e-7   worst measured
    ------------------------------------------------------ SampleTolerance 1e-4
    one cent of detune                               1.6e-2   subtlest defect
    one frame of loop drift                          1.1e-1

That is about four orders of margin on each side. The biquad row is the one to
remember: a low-pass usually rounds the one-ulp differences away to nothing, but
its recursion can sustain one instead, so error downstream of a filter is NOT
bounded at an ulp. The measured amplification ranged from 0 to 85x and was not
monotonic in cutoff or resonance - do not assume a well-behaved bound.

The fence was checked in both directions, which is the part worth repeating if
the tolerance is ever changed: a 0.5% gain change injected into SfzVoice makes
all three pinned tests fail, and a 0.01% change - 1.1e-5, inside the tolerance -
correctly does not.

THE FIXTURES ARE LIBM-FREE. SfzTestInstruments.WriteSineWav no longer calls
MathF.Sin. It computes the sine from a phase in REVOLUTIONS with a Taylor series,
using only IEEE-754 add, subtract, multiply, divide and floor - all exactly
specified, all identical on every platform and architecture - so the WAV it
writes is byte-identical everywhere. This is not what makes the tests pass; the
tolerance does that, and powf in the render path would still diverge with a
perfect fixture. It is what makes a FAILURE mean something: with the input
identical everywhere, a pinned render that moves has moved because the DSP moved.

IF A PLATFORM LANDS OUTSIDE THE TOLERANCE. Do not re-pin the numbers to whichever
machine you are sitting at, and do not add a per-platform branch. Measure the
actual delta first. Anything in the 1e-7 range is the libm noise described above
and the tolerance should widen a little; anything in the 1e-3 range or beyond is
a real difference in the DSP and wants finding, not absorbing.

CORPORA NEVER ENTER THE REPOSITORY
----------------------------------
Some of this library was measured against real material - stems downloads, and
sampler libraries - and NONE of it is committed, referenced by a fixture, or
named in a shipped document. The tests that need it are OPT-IN and are SKIPPED,
not failed, when the environment variable that points at it is unset:

    CODEBRIX_AUDIO_SUNO_CORPUS    a folder of "<Title> Stems.zip" downloads.
    CODEBRIX_AUDIO_GM_SOUNDFONT   a General MIDI SoundFont, for the one test
                                  that builds a player from each of those and
                                  renders a bar of its mix.
    CODEBRIX_AUDIO_DS_CORPUS      a folder of unpacked Decent Sampler libraries -
                                  .dspreset folders, .dslibrary and .dsbundle
                                  archives, in any mixture. See THE DECENT
                                  SAMPLER ENGINE for what the tests over it
                                  prove and for the survey tool that reads the
                                  same folder.

    CODEBRIX_AUDIO_SUNO_CORPUS=~/somewhere/corpus dotnet test CodeBrix.Audio.slnx

The corpus tests read the zips IN PLACE and, with one exception, extract nothing
and write nothing: a full pass over seven four-minute exports takes under a
second because only the MIDI entries and the WAV headers are decompressed. Keep
it that way - asking one stem for its audio puts 40 MB in the temp folder. The
exception is the end-to-end test gated by the SoundFont variable, which has to
extract a song's audio to build a player and calls song.ClearCache() in a finally
to take it back out again.

Whatever a General MIDI SoundFont on this machine happens to be, it is a LOCAL
convenience. TimGM6mb is what most Linux distributions install and it is GPL:
nothing in this family ships it, no fixture names it, and the documentation
recommends FluidR3_GM (MIT) instead.

THE SUNO STEMS FIXTURE, AND THE ALIGNMENT SEAM
----------------------------------------------
tests/CodeBrix.Audio.Tests/Playback/Suno/FakeSongStems.cs writes a synthetic
export - as a FOLDER and as a ZIP, from one list of files, so the two forms are
byte-identical by construction. Add a case to FakeSongOptions rather than writing
a second generator. It carries every shape a real export has: emoji in the title
and in the file names, the mangled track name the exporter writes for such a
title, a key signature outside the specification, one tempo event per beat,
zero-length drum notes on channel 10 with a kit program, a near-empty MIDI stem
beside a full-length WAV, and one stem whose audio deliberately sits 40 ms behind
its own MIDI. Nothing in it comes from a real export; only the shape is copied.
TemporaryFolder, in the same file, is the suite's only self-cleaning temp folder.

The estimator reaches the loader through a PROCESS-WIDE STATIC seam,
Playback/Suno/Internal/SunoAlignmentSeam.Estimator, which starts out as the real
MidiAudioAlignment and can be replaced by a stub. Every test that replaces it
calls SunoAlignmentSeam.ResetToDefault() in a finally, and every class that loads
a song is in the non-parallel xUnit collection "SunoStems"
(Playback/Suno/SunoSeamCollection.cs) so that a class installing a stub cannot
run beside a class that needs the real one. A new test class that loads a song
belongs in that collection.

THE TITLE TRANSFORM
-------------------
A stems exporter writes the track-name meta by taking, for each UTF-16 code unit
of "<Title> (<Stem>)", the low byte of the CODE POINT that unit belongs to.
U+1F344 is the surrogate pair D83C DF44 and comes out as 44 44, not 3C 44,
because both halves of a surrogate pair share the code point's low byte.
SunoTitleCodec (internal) reproduces it, measured byte for byte against every
.mid in seven real exports. It is only ever used to MATCH a mangled name against
the true title from the file name; nothing displays the decoded form, and the
file names are the authority on the title.

HOW ALIGNMENT IS MEASURED, AND WHERE IT STOPS
---------------------------------------------
MidiAudioAlignment reduces the audio to an onset envelope sampled every
millisecond - the RMS of a TRAILING 20 ms window, then a half-wave-rectified
first difference - reduces the MIDI to unit impulses at the note-on times, and
cross-correlates the two over +/- the search window with a Pearson-style
normalisation and a parabolic peak refinement. Three choices were settled by
measurement over seven real songs and not by taste: a time-domain envelope rather
than spectral flux; a 20 ms window rather than 1 ms (a millisecond of a bass note
is a piece of a waveform, not a level); and a LINEAR envelope rather than a
log-compressed one, which pulled every estimate 10 to 40 ms early. The constants
are private consts in MidiAudioAlignment.cs with the measurement that chose them
written beside them. Changing one changes the corpus numbers: run the opt-in
corpus tests before and after.

THE LIMIT IS BEAT-MULTIPLE AMBIGUITY, and it cannot be removed, only arbitrated.
On a repeating pattern every subdivision of the beat is a peak of the same comb.
Two things are done about it, and a third was tried and rejected:

  - Peaks within CandidatePeakFraction (60%) of the tallest are treated as rivals
    and scored a second time on a COARSE measure: the stem's note density and the
    audio's energy, each smoothed to a quarter of a second, correlated at that
    lag. A DECISIVE win moves the answer; an indecisive one leaves the tallest
    peak alone. Either way the presence of rivals multiplies the confidence down,
    which is what demotes a comb nobody can read.
  - The confidence is always measured about the peak that WON, against everything
    else in the window. Letting a "family" of near-equal peaks share one
    confidence was tried and is WRONG: on one real percussion stem it promoted a
    verified-wrong answer (a sixteenth note away from the truth) from confidence
    0.04 to 1.00.
  - SunoLoadOptions.TempoAwareAlignmentWindow narrows the search to half an
    eighth note at the song's slowest tempo (floor 80 ms, capped at
    MaximumAlignmentOffset). It is OFF BY DEFAULT because the corpus says it
    costs more than it buys: real offsets reach a quarter of a second, a 110 to
    175 ms window then cannot express the right answer, and what it finds instead
    is a small bump with the window to itself - a clean, confident, wrong answer.
    Measured: it turned three verified offsets (+192, +240, +216 ms) into
    -28, -96 and +40 ms, two of them still marked reliable.

  ON THE "BEARDED DRUMS" CASE, which earlier notes recorded as aliased: it is
  NOT. The estimator's +192.3 ms is correct and the -120 ms hand measurement in
  the findings file is wrong. Verified three ways: the first six drum hits of the
  recording sit +187 to +194 ms after the note-ons that transcribe them, with the
  spacings agreeing to a millisecond; the coarse envelope agreement has a broad
  maximum at +240 ms and reads 0.007 at -120 ms; and the correlation's only other
  peak is at -439 ms, one QUARTER note away, not one eighth. Anyone re-opening
  this should re-run the corpus probe rather than trusting the older figure.

THE PER-SONG FALLBACK is loader policy, not an estimator feature: a MIDI stem
whose own estimate is not reliable, or that has no audio of its own, takes the
MEDIAN of the offsets of the stems that WERE measured reliably (zero when none
were). What each stem's own measurement said is kept in
SunoStem.MeasuredAlignmentOffset, and SunoStem.AlignmentIsFallback says which of
the two SunoStem.AlignmentOffset is holding.

MP3 GAPLESS TRIMMING
--------------------
Mp3FileReaderBase reads the LAME-style extension after the Xing/Info fields and,
by default, discards EncoderDelay + 529 samples at the front and stops at
totalSamples - EncoderDelay - EncoderPadding. The 529 is the Layer III decoder's
own latency, which no file records and every gapless implementation assumes; it
was CONFIRMED for this decoder (the untrimmed decode of the fixture leads its
source WAV by exactly 576 + 529 = 1,105 samples). Re-check that number if the
managed MPEG decoder is ever replaced.

The class now has TWO POSITION SPACES. The private `position` field,
ApplyPendingReposition, the frame index and ReadNextFrame() are RAW; Position,
Length and Read are TRIMMED; TrimStartBytes is the bridge. Anything added there
has to pick a side deliberately. Also: the Xing `Frames` field EXCLUDES the
header frame, and the frame index is seeded from the first AUDIO frame for the
same reason.

The fixture is a PAIR - tests/Assets/audio/mp3-gapless-sweep-stereo-44100.mp3 and
the .wav it was encoded from. Regenerating one without the other makes every
gapless assertion meaningless, and a bare run of make_fixtures.sh rewrites all
the .ogg and .opus bytes for no reason: point OUT_DIR somewhere else and copy the
two files you meant to change.

THE OTHER FIXTURE HELPERS
-------------------------
    Midi/SyntheticMidi.cs        an SMF byte builder that produces malformed
                                 shapes on purpose (MetaWithDeclaredLength,
                                 AlienChunk, FileDeclaring, RawBytes). Reuse it
                                 rather than writing a second one.
    Midi/SunoShapedMidi.cs       the export-shaped MIDI the leniency tests read.
    Utils/SyntheticMp3.cs        a hand-built frame carrying a Xing header and a
                                 LAME extension, for the gapless tests.
    Utils/MinimalSmfReader.cs    a test-only SMF reader, so an alignment corpus
                                 test can still read a file while the real
                                 parsers are being changed underneath it.
    MultiTrackTestSong.cs        constant-valued stereo WAVs and hand-assembled
                                 sequences for the multi-track tests. Constant
                                 values are deliberate: every gain, pan, mute and
                                 offset assertion is then arithmetic a reader can
                                 check by eye.

SHARP EDGES IN THE MULTI-TRACK PLAYER
-------------------------------------
  - Loading a MidiSequence APPLIES its tempo events and DISCARDS them, so
    Messages/Times carry no tempo information at all. MidiSequence.TempoMap - a
    partial file, Synth/MidiSequenceTempoMap.cs, filled in by MergeTracks - is
    the only place the map survives, and the merged export's time-to-tick
    conversion and TempoSource both depend on it. Do not "fix" this by asserting
    the sequence carries tempo messages. The Suno loader reads its tempo map with
    MidiFile instead, because it wants the events themselves.
  - A track holding two sources renders BOTH every block. That is what makes a
    source switch seamless and it is the player's main CPU cost.
  - MinimumNoteHold defers a release and must NEVER release a note whose note-off
    has not arrived; MidiSourceRenderer keeps a "deferred" flag per pending note
    for exactly that reason. Without it every long note would be cut at 60 ms.
  - MidiSourceOffset is applied by wrapping the MIDI renderer in a
    DelayedSourceRenderer, whose delay is re-read whenever the transport seeks -
    in step with the way PlayerTrack.Offset is re-read. The wrapper is built even
    when the offset is zero, so it can change later.
  - MultiTrackDataProvider always returns a FULL buffer: the engine treats a short
    read as "that is all there was" and stops asking, so the end of the song is
    signalled only by the NEXT call returning zero.
  - The level measurement renders at MultiTrackPlayer.LevelMeasurementSampleRate
    (22050). A test that plants a loudness ratio must build its reference at that
    rate too.
  - 60 ms at 120 BPM is 0.12 of a beat, which is 58 ticks at 480 ppq, not 48.
  - A synthesizer factory may be called several times and from a worker thread.
    Share the SoundFont or the SfzInstrument, never the synthesizer.

WHAT TOLERANT MIDI READING ACTUALLY DOES
----------------------------------------
Both readers default to MidiReadMode.Tolerant and report departures in
.Problems; MidiReadMode.Strict still throws. Eight things a maintainer should
know before touching either reader:

  G1. Both readers REQUIRE A SEEKABLE STREAM. They always did (running status
      pushes a byte back), and tolerance leans on it harder: a malformed meta
      event is recovered by rewinding to the start of its payload.
  G2. Tolerant reading MUTATES the event list - a dangling note-on gets a
      synthesised NoteOff INSERTED before the end-of-track event, which is what
      makes the collection exportable and NoteLength usable. Anything counting
      events must read Strict or expect it.
  G3. The two readers report DIFFERENT problems for one file, correctly:
      MidiFile models key signatures, MidiSequence does not. On the real corpus
      MidiFile reports exactly one problem per file and MidiSequence none. Never
      assert the two lists are equal.
  G4. KeySignatureEvent's BinaryReader constructors do NOT set MetaEventType -
      MetaEvent.ReadMetaEvent sets it after the switch, as it always has for
      TextEvent and TempoEvent too. Use KeySignatureEvent.FromRawValues to author
      one, or read it through a MidiFile.
  G5. SunoTitleCodec is INTERNAL and its XML documentation is already written, so
      making it public is a one-line change if a consumer ever needs it.
  G6. The corpus tests copy nothing into the repository; the unit fixtures copy
      only the SHAPE of an export.
  G7. src/CodeBrix.Audio/Synth/MidiSequence.cs keeps its UTF-8 BOM - it is a
      ported file and the other ported files have one. An editor that strips it
      makes the diff noisy for no reason.
  G8. Only channel-voice messages set the running-status anchor, in BOTH readers.
      A running-status byte with nothing to run from is a hard error: Strict
      throws, Tolerant reports it and abandons the rest of that track, because
      there is no way to resynchronise inside one.


THE DECENT SAMPLER ENGINE
=========================
src/CodeBrix.Audio/Synth/DecentSampler/, namespace
CodeBrix.Audio.Synth.DecentSampler. Like the SFZ engine it is NOT a port: it was
written from the published Decent Sampler developer guide and from MEASUREMENT
of the reference player's output. No player's source was read - the reference
plugin is closed, and the frameworks it is built on are licensed in ways this
repository cannot take from. Where the guide leaves a behaviour undefined, the
answer came from a recording, and the code says so at the constant.

  Top level        parser, model, containers, instrument, cache, load options,
                   supported features, residual table, synthesizer, settings.
  Model/           the whole document typed. Unknown attributes and elements are
                   KEPT on the model, never dropped.
  Containers/      folder and zip containers. An archive is read IN PLACE.
  Bindings/        the parameter and binding engine - entirely internal, six
                   public types (the controls, the tag state, the tempo helper)
                   are its whole surface.
  Engine/          the zone and voice runtime, the extension registry, the
                   contracts (IVoiceSource, IInstrumentEffect, the contexts).
  Effects/         the thirteen core effect types, the chains and the mixer.
  Modulation/      the seven modulator kinds, both scopes, four behaviours.
  Sequencing/      the <midi> handlers, the note sequencer, the arpeggiator.
  Streaming/       the streamed sample source, the ring-buffer pool, the shared
                   reader thread and the memory policy.
  Samples/         ISampleSource and the in-memory implementation.

SHARED DSP PRIMITIVES. The Decent Sampler voice reuses the SFZ engine's sample
playback unchanged and internally: SfzOscillator does the fixed-point position,
the linear interpolation, the loop wrap and the loop crossfade for BOTH engines,
and SfzSampleData is the decoded-audio container for both. The Sfz prefix is
therefore load-bearing beyond the SFZ engine, and those types are extended
ADDITIVELY - SfzOscillator has gained a crossfade-taking Start overload and a
count-taking Process overload, each with the old signature delegating to it, so
that the whole SFZ suite passing unchanged is the fence. Reverb.cs gained
count-taking Process overloads the same way. Keep that discipline: an
in-place change to either file has to be proved byte-identical for SFZ.

THE CORPUS RULE. Sampler libraries never enter this repository - not as a
fixture, not as an asset, not named in a shipped document. The tests that need
one are OPT-IN through CODEBRIX_AUDIO_DS_CORPUS and are SKIPPED, not failed,
when it is unset. See CORPORA NEVER ENTER THE REPOSITORY above. Every fixture in
the Decent Sampler tests is synthetic and written to a temp folder by
DecentSamplerEngineFixtures, DecentSamplerTestPresets or the add-on's own
DecentSamplerFixtures.

  CODEBRIX_AUDIO_DS_CORPUS=~/somewhere/libraries dotnet test CodeBrix.Audio.slnx

  The corpus tests are spread over the phases that needed them, each proving one
  thing: the parser recognises everything (DecentSamplerCorpusTests), every
  binding resolves (Bindings/DecentSamplerBindingCorpusTests), every preset
  builds a synthesizer and renders (Engine/DecentSamplerEngineCorpusTests), the
  effect chains build (Effects/DecentSamplerEffectCorpusTests), an unbound
  controller changes nothing and a bound one changes something
  (Sequencing/DecentSamplerSequencingCorpusTests), the modulator runtime is
  transparent for a preset with no modulators
  (Modulation/DecentSamplerModulationCorpusTests), the memory policy holds
  (Streaming/DecentSamplerStreamingCorpusTests), and the MILESTONE test -
  DecentSamplerMilestoneCorpusTests - which asserts zero unsupported features,
  no problem line outside two documented lists, a memory budget that holds, and
  a bar rendered through the offline path for every preset.

  TWO OF THOSE COMPARE RENDERS BYTE FOR BYTE, and neither render is an audio
  callback, so both build their synthesizer with
  DecentSamplerStreamingMode.Offline. In the real-time mode a render that runs
  faster than real time outruns the background reader, a starved block is
  written as silence, and the comparison is then against a race. Any new test
  that compares two renders of a streamed preset must do the same.

  THE ONE PLACE THE REAL-TIME STREAMING PATH IS EXERCISED AGAINST A REAL CLOCK is
  Engine/DecentSamplerAudibleTests.plays_the_close_encounters_motif_from_a_
  streamed_preset, which is device-gated on CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1
  and takes a 4,096-frame preload head - 93 ms against a 44,100-frame file, so
  everything past the first tenth of a second comes off the reader thread, and
  the head is still long enough that a device callback cannot outrun the first
  buffer the way the unit tests' 256 frames would. Starvation there is audible
  as a gap; no offline test can show
  it. Both audible Decent Sampler tests play the same five-note motif every other
  audible test in the suite plays, and both take AudibleTestScope so the two test
  processes never sound at once.

WHAT A STREAMED INSTRUMENT HOLDS OPEN
  One OS file handle per streamed FILE (not per zone: the sample cache shares one
  source between the zones that name the same path), for the life of the
  instrument. That is the design - reopening the file on the reader thread for
  every ring-buffer refill would put a syscall on the path the audio thread waits
  behind - and it is fenced by Streaming/DecentSamplerStreamingHandleTests, which
  counts the process's own descriptors through /proc/self/fd and SKIPS where that
  does not exist. Disposal has to close every one; see GOTCHAS FOR MAINTAINERS on
  WaveFileReader not owning its stream.

  SAMPLE_START, SAMPLE_END, LOOP_START AND LOOP_END take effect at the NEXT
  NOTE-ON and never on a sounding voice, streamed or in memory. The start and end
  are read off the zone when the voice starts; the loop is re-resolved there too,
  by DecentSamplerZoneRuntime.RefreshLoopIfMoved, which is three nullable
  comparisons when nothing has moved. Nothing about this was measured - the guide
  says only that the four parameters need in-memory playback and says nothing
  about a sounding voice - so the engine takes the cheaper reading rather than
  re-resolving running voices from the render callback. Fence:
  Engine/DecentSamplerSamplePointBindingTests.

THE MEASUREMENT PROGRAMME
  The format has no specification: the reference player defines the sound. THREE
  measurement rounds recorded it, and their results are the reason most of the
  engine's constants are what they are.

    ~/ClaudeHome/decent-sampler/MEASUREMENTS_ds_reference_semantics_*.txt
        the measured truth. An index at the top, a summary per round at the
        bottom, and one section per item: the purpose, the exact preset XML, the
        MIDI content, the recordings, the number tables, and a conclusion an
        implementer can code from with a confidence rating.
    ~/ClaudeHome/decent-sampler/measure/
        one folder per item, each with the build script, the preset, the MIDI
        file, the recording, the player's log and the analysis. Everything
        replays.
    ~/ClaudeHome/decent-sampler/FIDELITY_*.txt
        the comparison tables per phase: our render against the reference's,
        RMS envelope per bar, filter transfer functions, reverb RT60 per band.

  TO RE-MEASURE ONE THING: write a synthetic preset that isolates it, render it
  through the reference player against a short MIDI file, render the same thing
  through SoundFontRenderer, and compare. The harness in measure/tools does the
  launching and the level tables. Four things cost hours if they are not known
  in advance: a sample whose FIRST FRAME carries the signal does not sound (lead
  every transient probe with silence); a zone declared loNote="0" hiNote="127"
  sounds on every key and contaminates every other case in the recording;
  groupTuning is clamped to plus or minus 36 semitones, so the "one group per
  case, all at one pitch" trick only spans 73 keys; and a <cc> binding fires on a
  CHANGE only, so a probe that sends the same value twice measures nothing.

  MARKING A FEATURE IMPLEMENTED. DecentSamplerSupportedFeatures is the single
  source of truth and every entry starts as Parsed. EACH PHASE ADDS ITS OWN
  private Mark*Implemented() method and calls it from the static constructor;
  nobody edits another's. That is what kept six agents' work mergeable, and it
  is still the rule. MarkImplemented THROWS on a name the list does not declare,
  so a typo fails at type initialisation rather than lying.

  THE RESIDUAL TABLE IS THE OTHER HALF OF THAT CONTRACT.
  DecentSamplerResidualTable classifies every Parsed feature into one of nine
  groups with a written reason, and DecentSamplerResidualTableTests asserts both
  directions: every Parsed feature is accounted for by exactly one entry, and no
  Implemented feature is claimed by any. Adding a name to the format's
  vocabulary without either implementing it or explaining it fails the build.
  When you implement something, promote it AND the residual table shrinks by
  itself - the table is derived, not written.

  THE SURVEY TOOL. tools/ds_feature_survey parses a folder of real libraries
  with the engine's own parser and reports, per library and per preset, every
  element and attribute they use and how much of it the engine recognises. Run
  it after any parser change and after adopting a new library:

      cd tools/ds_feature_survey
      dotnet run -- <corpus-directory> [output-directory]

  It writes libraries.md (per-preset breakdown and every problem in full),
  attributes.md (every attribute ranked by how many libraries use it) and
  coverage.md (the table below). It is a console project referencing the
  library, not packable, and not in the solution - the same shape as
  tools/sfz_opcode_survey.

  CURRENT COVERAGE, nine libraries and twenty-five presets:

      category                       corpus   known   unknown
      ---------------------------------------------------------
      presets fully recognised          25      25         0
      binding parameters                10      10         0
      effect types                       2       2         0
      oscillator waveforms               0       0         0
      attribute names                  101     101         0

  Twelve problems in total across the twenty-five presets, every one an
  authoring typo in a shipped library and correctly reported: damping="O.2" with
  a capital letter O on a reverb, in five separate libraries. Do NOT teach the
  parser to accept them. That is what Problems is for.

PARALLEL-MERGE SHARP EDGES
  The engine was built by several agents at once, and three seams still show.

  1. TWO TAG-STATE TYPES. The engine-side seam is the internal interface
     IDecentSamplerTagState (Engine/). DecentSamplerStaticTagState is the fallback
     that reads the parsed <tag> elements and was renamed at the merge;
     DecentSamplerLiveTagState is the one the binding engine supplies and the one
     that actually runs. The PUBLIC DecentSamplerTagState is a third thing again -
     the per-tag state a consumer reads. Three names, one concept, and the middle
     one is the only one that matters at run time.
  2. DecentSamplerExtensions HAS A STATIC CONSTRUCTOR that registers the thirteen
     core effect types. Touching ANY member of it - including Shared - runs it. A
     test that needs an empty registry must build its own
     DecentSamplerExtensionRegistry, and one that needs the core set without the
     add-on fills it with DecentSamplerCoreEffects.RegisterInto.
  3. THE ADD-ON'S REGISTRATION IS PROCESS-WIDE AND CANNOT BE UNDONE. A test in
     the add-on's project that needs "the package is not present" must use an
     isolated registry too; ModestSynthRegistrationTests shows both halves.

GOTCHAS WORTH A MAINTAINER'S ATTENTION
  G1. THE ENGINE RENDERS IN 64-FRAME BLOCKS and ramps a voice's gain across a
      block. Consecutive measurements on one synthesizer need block-aligned
      frame counts or a fresh instance, and anything asserting an envelope, a
      fade or a choke must measure RMS over a window and skip a settling block -
      a PEAK cannot tell a fade from a steady level. DecentSamplerRenderProbe has
      the helpers.
  G2. THE ZONE RUNTIME DELIBERATELY CACHES NOTHING A BINDING CAN MOVE. Adding a
      cached copy of a bindable value silently stops live control of it. THE ONE
      EXCEPTION is the resolved loop, which folds the zone's attributes together
      with the file's embedded markers and cannot be recomputed per frame:
      RefreshLoopIfMoved re-runs the resolution at note-on when the zone's own
      loop attributes have changed since it last ran, and the fields it compares
      against have to be updated whenever ResolveLoop grows an input.
  G2b. WaveFileReader, AiffFileReader AND FlacFileReader DO NOT OWN A STREAM they
      are handed - the Stream constructors all pass ownInput = false - so
      disposing one leaves the FileStream, and the OS handle, open until a
      finalizer happens to run. StreamingSampleSource therefore keeps the stream
      it was given and disposes it itself. Any other place that builds a reader
      over a stream it opened has the same duty.
  G3. THE BINDING ENGINE BELONGS TO THE INSTRUMENT, NOT THE SYNTHESIZER. Two
      synthesizers over one instrument share every parameter, and the LAST one
      built is the one sequence triggers reach. Inherent to a format whose
      bindings mutate the instrument.
  G4. VOICE SOURCES ARE POOLED PER ZONE. A source is rented at note-on and
      returned when the voice finishes, so an implementation must fully
      re-initialise itself in Start and must not assume it is fresh.
  G5. IVoiceSource.Render(left, right, frames) MAY BE CALLED WITH FEWER THAN
      blockSize FRAMES on a voice's first sounding block - a note delay, or any
      sequenced or arpeggiated note. A source that ignores the count runs on
      through the silence in front of the note and swallows that much of the
      sample. Both built-in sources (in-memory and streamed) had that defect once
      each; DecentSamplerSubBlockTimingTests and
      DecentSamplerStreamingSubBlockTests are the fences.
  G6. THE STREAMED PATH'S CONTRACT IS DecentSamplerStreamingIdentityTests: the
      same instrument, one decoded and one streamed, must produce the same
      numbers exactly. Any change to DecentSamplerStreamingOscillator,
      StreamingVoiceBuffer's stream-to-file mapping, or SfzOscillator must leave
      all of them green. The in-memory path is the definition.
  G7. THE RING IS INDEXED IN STREAM SPACE, NOT FILE SPACE. A window of file
      frames cannot hold both sides of a loop seam; that is why the reader wraps
      the loop and the audio thread plays a straight line.
  G8. ONE OPEN FILE HANDLE PER STREAMED FILE, for the life of the instrument.
      A library with more streamed samples than the process's file limit - macOS
      defaults to 256 - will fail to open the rest and report them. A lazily
      opened decoder with an idle timeout inside StreamingSampleSource is the fix
      when it matters. Disposing the instrument closes every one of them, which
      DecentSamplerStreamingHandleTests proves by counting the process's own
      descriptors; see G2b for the trap that used to leak them.
  G9. THE OFFLINE STREAMING MODE READS FILES ON THE RENDER THREAD. That is what
      it is for, and it must never be set on a synthesizer feeding a live device.
      SoundFontRenderer sets it (and puts it back); MidiMusicPlayer does not.
  G10. THE SHARED READER THREAD HOLDS WEAK REFERENCES, so a dropped synthesizer
      simply stops being serviced. TWO FRAMES ARE LOAD-BEARING in making that
      true. The test must build the synthesizer in a NoInlining method, or the
      reference stays live in the test's own frame; and the reader's own
      servicing round must live in a method that RETURNS before the thread
      waits, or the last context serviced stays live in a register or a stack
      slot of a frame that lives for the whole process - which pinned one
      dropped synthesizer, and its whole instrument, indefinitely. Anything that
      takes a strong reference to a context in DecentSamplerStreamingReader.Run
      itself reintroduces that leak, and the fence is
      the_shared_reader_forgets_a_synthesizer_nobody_holds_any_more RUN ON ITS
      OWN: in a full-suite run an earlier test's leftover context can make its
      assertion pass for the wrong reason.
  G11. Problems IS NOT FIXED AFTER LOADING - a streaming underrun or a
      first-use decode adds to it. It is REPLACED rather than mutated, so
      enumerating it is safe and a cached reference goes stale.
  G12. THE MIXER CLEARS A SLOT BUFFER the first time something touches it in a
      block, so reading a slot nothing has written gives last block's audio.
      Always ask IsTouched first.
  G13. A GROUP CHAIN FORCES A MONO VOICE INTO STEREO. Read a voice through
      BlockLeft/BlockRight rather than assuming they are the same array.
  G14. BUS ORDER AND AuxiliaryOutputCount ARE COMPUTED ONCE, at construction,
      from the DECLARED routing. A binding that moves a target at run time
      changes where audio goes but not the processing order, so a cycle created
      at run time is not detected.
  G15. A SWALLOWED NOTE LEAVES NO TRACE - no voice, no held key, no MPE note-on -
      so trigger="first" and trigger="legato" do not count key switches.
  G16. THE FEATURE LIST HAS TWO "playbackMode" ATTRIBUTES: the sample one
      (memory / disk_streaming / auto) and multiFrameImage's animation one
      (forward_loop, ping_pong_loop, ...). They share an enumeration owner, and
      only the first three values are Implemented.
  G17. CONTROLLER-INDEXED ATTRIBUTES (loCCN, hiCCN, onLoCCN, onHiCCN) are FOLDED
      in the feature list; the real names carry a number. Use
      CanonicalAttributeName before looking one up and TryReadControllerAttribute
      to read the number back.
  G18. THE UI CONTROL INDEX COUNTS EVERY ELEMENT under every tab, labels and
      images included, because that is what a binding's controlIndex means. Do
      not filter DecentSamplerUi.Controls to interactive controls.
  G19. A MENU'S value IS 1-BASED - the one index in the format that is. 0 means
      nothing selected.
  G20. DecentSamplerDeclaredAttributeTests ENFORCES BOTH DIRECTIONS between the
      parser and the feature list. Add an attribute to the list and you must read
      it in the parser, or that test fails.

DIVERGENCES FROM THE REFERENCE PLAYER, AND WHAT WOULD CLOSE EACH
  Published in the consumer guide as well, because the plan's rule is that a miss
  is a listed divergence rather than a silent difference. Everything below has
  been MEASURED; nothing is a guess. Three measurement rounds settled the format,
  and the retune pass applied what they found.

  MEASURED AND APPLIED, with the number each was fenced at:

    compressor        the threshold scale 2*sqrt(2) (+9.031 dB), the hard knee,
                      the peak follower on max(|L|,|R|), the ratio clamped below
                      at 1 and not above, inputGain in front of the detector, and
                      autoBypass crossfading the whole effect. The eleven
                      attack/release settings match to 0.1 dB against a 0.5 dB
                      target; the input and threshold sweeps to 0.1 dB.
    reverb            stock Freeverb - eight combs at 1116 1188 1277 1356 1422
                      1491 1557 1617 into four allpasses at 556 441 341 225 with
                      feedback 0.5, the right channel exactly 23 samples later,
                      width 1, no pre-delay, input scale 0.015 and wet scale 3 -
                      with damp1 = 0.375 * damping. Every comb echo lands within
                      0.6 dB of the measured -26.9 dB and every RT60 cell of the
                      five measured room/damping settings within 20 %.
    chorus            one modulated tap per channel at 624 and 441 samples,
                      delay = base * (1 + modDepth * lfo), the RIGHT oscillator
                      at (10/9) * modRate, and mix a plain linear crossfade
                      (measured to three decimals).
    phaser            six first-order allpasses at centerFrequency, the notches
                      landing on the reference's own 108/401/1486, 269/999/3655
                      and 1098/4000/11639 Hz; the sweep exponential at five
                      octaves each way, downward first; feedback SUBTRACTED, so
                      0.7 turns the notches into +1.34 dB peaks as measured;
                      negative feedback treated as none.
    modBehavior       set = u, add = b + u, multiply = b * u and modulate =
                      b + u - a * translate(the modulator's resting raw output),
                      with u = modAmount * translatedValue. The whole 24-cell
                      reference table is a theory in
                      DecentSamplerModBehaviorTests.
    tag volume        a <tag>'s volume has its own parser now
                      (DecentSamplerValues.TryTagVolume): the absolute value of a
                      plain linear number, no "dB" suffix, no clamp. Tag volumes
                      multiply over the union of the group's and the sample's
                      tags, order-independent.
    effect tags       a disabled tag silences the ZONES carrying it and no longer
                      bypasses an effect carrying it. The chain's tag plumbing is
                      gone entirely; tags on an effect exist for bindings.
    group chains      run AFTER the amplitude envelope. The whole per-voice
                      amplitude stage - the three volume levels, the tag volumes,
                      velocity, the trigger gain and the envelope - is applied to
                      the block before the chain and only the PAN is left for the
                      mix. A group with no chain keeps the old path exactly.
    buses             a group routed to an undeclared bus is SILENT, and a bus
                      whose own output names another bus is silent too. There is
                      therefore no routing graph to order and no cycle to break.
    silencingDecay    is the amplitude envelope's release with that time and the
                      group's releaseCurve; the reference's own decay table is
                      reproduced within 1.5 dB.
    release triggers  fire only on a key that also started a voice, and under a
                      held pedal both the trigger and its decay measure to the
                      PEDAL LIFT.
    groupTuning       clamped to plus or minus 36 semitones.
    fm6op             beta = 4*pi*level (the Bessel sideband pattern matches to
                      1.5 dB over six lines), fmOpNRatio defaulting to N, no
                      carrier-count normalisation, the five-point velocity table
                      with its neutral point at velocity 96, the DX7 rate scale
                      at 5.5 units per halving with rate 0 crawling, the detune
                      power law 0.005341 * f0^0.630, feedback only on the
                      algorithm's own operator and clamped at 1.
    harmonic          normalization is (1 - n) + n / sum(gains), a sum divide.
    wavetable         a stereo file is read as its LEFT channel.
    noise             the anti-imaging rolloff is reproduced by two cascaded
                      biquads (a double zero at Nyquist over a pole pair at
                      0.364 * rate, radius 0.58), within 0.8 dB at every measured
                      band.
    pluck1            the loss per trip is CUBIC in (1 - damping), which lands
                      all three measured -60 dB times within 3 %.
    formant           the add-on now renders the reference's fixed tone from its
                      measured twenty-partial envelope, read in HERTZ so the
                      resonance holds still under a moving pitch.
    wave_folder       the closed form out = foldOnce(in*k)/k with
                      k = drive/(2*sqrt(2)*threshold) - a single reflection, not
                      clamped, and a bit-identical pass-through below the fold.
    arpeggiator       the first step fires on the key-down, the step already
                      started runs its whole gate after the last key lifts, and
                      the position register survives between chords.
    note sequencer    the FIRST note fires on the key-down whatever beat it is
                      written at, with the grid shifted to match (eight trigger
                      phases over the 0.500 s beat all gave 3-12 ms, one block,
                      with no correlation to the phase); the grid is
                      sample-accurate from there; a key lift cuts the sounding
                      note (a 2 s note under a 1 s key sounded 0.9955 s); and
                      the emitted velocity is
                      127 * noteVelocity * ((1-track) + track*trigVel/127),
                      fenced against the measured 0/-6.02/-12.04 and
                      0/-2.45/-4.05 dB rows.
    envelope modulator  every stage runs gain = (1-exp(-4x))/(1-exp(-4)) -
                      DecentSamplerCurves.ModulatorShape, whose constant is 4.0
                      rather than the amplitude envelope's 4.1 - with a
                      LINEAR-AMPLITUDE sustain and a release that runs its own
                      time and ends at zero. The twelve measured points of a
                      two-second attack are a theory in
                      DecentSamplerEnvelopeModulatorTests.

  Also applied and unchanged since the earlier rounds: the note-name convention,
  the volume parser and its [0,16] clamp, the pan law, the envelope curve law and
  the undocumented 0.5 s release default, ampVelTrack, the musical-time table,
  instrument-wide seqLength auto-detection, both releaseTriggerDecay forms, the
  equal-power loop crossfade, "lowpass" being one two-pole biquad with
  resonance = Q, the reverb's comb feedback, a <cc> firing on change only,
  modAmount being ignored on a permanent binding, the fm operator level defaults,
  the 2 ms fast choke, a controller-triggered zone playing at full velocity, the
  noise level relative to a sine, and the harmonic tilt law.

  RESIDUALS THAT REMAIN, with the number:
    noise centroid    1.16 dB darker than the reference's measured 8877 Hz. The
                      reference's own two figures disagree: read literally its
                      third-octave band table implies a centroid near 6.5 kHz, so
                      no single response can meet both. The band table is the
                      direct measurement and is what the filter follows.
    formant phase     the twenty partial MAGNITUDES are the reference's; their
                      phases could not be measured and are Schroeder's, which is
                      the lowest crest factor a given spectrum can have.
    fm6op detune      the power law was fitted over MIDI 24 to 84 (+/-3 %);
                      outside that range it extrapolates.
    Global Swarm      still about 1 dB hot on the whole-preset comparison. Round
                      3 item 47 ruled out the tag arithmetic - tag volumes driven
                      by UI bindings multiply exactly - and did not find it.

  DIVERGENCES OURS IS WIDER ON, deliberately, every one a place where the
  reference does LESS than its own documentation promises:
    - a <velocity> binding reaches AMP_VOLUME here and does nothing there, and it
      stays inside the group its groupIndex names where the reference applies it
      to every group (round 3 item 46 confirmed both).
    - a <midiCC> modulator with scope="voice" reads its controller here and reads
      zero there, so the DOCUMENTED DEFAULT is inert in the reference.
    - a modulator binding at level="instrument" moves GLOBAL_TUNING here; the
      reference ignores it.
    - trigger="continuous" sounds here; the reference produced no sound at all.
    - a bus routed to an undeclared bus is reported in Problems here; the
      reference is silent about it as well as silent.
    - the retrigger interval is exact here; the reference rounds it up to a whole
      512-frame block.
    - a sample whose first frame carries the signal sounds here; the reference
      ramps a voice in over its first frames and loses it.
    - seqLoopMode="no_loop" stops after one pass here; the reference fails to
      stop a sequence whose declared length is 2. Both random loop modes there
      never draw one note of a four-note sequence.
    - an <envelope> modulator's curve attributes work here; the reference accepts
      and ignores them. The DEFAULT shape is the reference's either way, so a
      preset that writes no curves sounds the same in both.
  All of them are listed in the consumer guide too. The two note-sequencer
  entries above and this one are DELIBERATE non-adoptions: the measurement says
  in as many words not to copy them.

  STILL UNMEASURED after three rounds: whether re-triggering a note sequence that
  is already running restarts or ignores it; what a UI-button "on"/"off" pair does
  to a sequence; the real boundary of the no_loop defect; fm6op's
  modulator-to-modulator chains; the <envelope> modulator under two voices of one
  group; whether other permanent <midi> bindings leak the way <velocity> does;
  a bit_crusher input-level sweep; and WHETHER A SOUNDING VOICE FOLLOWS A
  SAMPLE_START, SAMPLE_END, LOOP_START or LOOP_END binding (this engine honours
  one at the next note-on only - see THE DECENT SAMPLER ENGINE - because the
  guide says nothing about it and that is the cheaper of the two readings).

  Three more readings the code carries an AWAITING MEASUREMENT note against, in
  the file where each is decided: whether a retrigger's first interval runs from
  the note-on or from the end of the longest delay, and whether the pattern
  survives a key release (DecentSamplerSynthesizer.AdvanceRetriggers); whether a
  delay written in the `samples` time unit counts frames at the OUTPUT rate or at
  the source file's (DecentSamplerSynthesizer's time-unit conversion); and
  whether the constant-power pan law applies to a STEREO zone as a balance, which
  is what is done here, or by collapsing it to mono and re-panning
  (DecentSamplerVoice). The pan law itself was measured with mono samples only.

  WHERE GLOBAL SWARM'S 1.12 dB GOES, if a fourth round happens. The residual is
  NOT the tag arithmetic (round 3 item 47) and NOT the effects: with the reverb's
  wet level driven to 0 it is 1.16 dB, and correcting the preset's own
  damping="O.2" typo moves it by 0.01 dB. Of the preset's nine AMP_VOLUME-bound
  tag controls only Strings and Synths are loud enough to carry it alone, and
  putting the STRINGS family down 6.65 dB both closes the level exactly and cuts
  the per-bar shape error from 0.80/1.55 dB to 0.45/0.77 - which no other single
  control does. Level-matched octave bands say it is not a flat gain either: the
  render is 3.3 dB QUIET at 80-160 Hz and 2.0 dB HOT at 320-640 Hz. What that
  needs is a reference recording of the Strings groups alone, not more fitting.

THE ADD-ON'S SIDE OF THE JOIN
  CodeBrix.Audio cannot reference CodeBrix.Audio.ModestSynth, so the add-on
  registers itself - the same pattern the Opus codec package uses. The core owns
  the registry (DecentSamplerExtensions over DecentSamplerExtensionRegistry) and
  the two contracts (IVoiceSource, IInstrumentEffect); the add-on owns everything
  it registers. Two rules follow, and both are tested:

    RESOLUTION HAPPENS WHEN AN INSTRUMENT IS BUILT, not when its file is parsed.
    Registering afterwards does not retrofit a loaded instrument.
    DecentSamplerSupportedFeatures.Features always describes the CORE. The
    registry-aware StatusOf overload is what answers "is this waveform live
    here", and every_listed_feature_has_a_status asks it with a NULL registry so
    that running both test assemblies in one process cannot make it lie.

  Both packages are published together at one version - see PACKAGING AND
  PUBLISHING, "TWO PACKAGES, ONE VERSION, PUBLISHED TOGETHER". The add-on's own
  README.md and AGENT-README.txt are what its package carries; the repo-root pair
  stay CodeBrix.Audio's.

PACKAGING AND PUBLISHING
========================
  TWO PACKAGES, ONE VERSION, PUBLISHED TOGETHER. This repository builds
  CodeBrix.Audio.MitLicenseForever and CodeBrix.Audio.ModestSynth.MitLicenseForever.
  They carry the same version (see BUILDING, "the shared version") because the
  add-on's dependency on the core has to resolve exactly, and they go to
  nuget.org in the same session: core first, then the add-on. Never publish the
  add-on against a core version that is not on nuget.org yet.

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
    runtimes/<rid>/native/LICENSE-MiniAudio.txt   one beside each of the seven
        binaries, packed by the same glob. It carries both the miniaudio and the
        stb_vorbis grants, and it lands in every consuming application's output
        folder alongside the binary it covers. A new RID folder must get a copy
        too - see tools/build_native_libraries/README.txt, "ADOPTING A BUILT
        BINARY INTO THE PACKAGE".
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

  THE ADD-ON PACKAGE
    PackageId              CodeBrix.Audio.ModestSynth.MitLicenseForever
    License expression     MIT, with PackageRequireLicenseAcceptance set
    GeneratePackageOnBuild true
    NuGet dependencies     CodeBrix.Audio.MitLicenseForever, at the shared version

    WHAT SHIPS IN ITS NUPKG
      lib/net10.0/CodeBrix.Audio.ModestSynth.dll (+ .xml)
      icon-codebrix-128.png        from the repo root
      THIRD-PARTY-NOTICES.txt      from the repo root; it covers both packages
      README.md                    ITS OWN, from src/CodeBrix.Audio.ModestSynth/
      AGENT-README.txt             ITS OWN, from the same folder

    The two doc files are the add-on's, NOT the repo-root pair - the root ones
    describe CodeBrix.Audio and are packed into the core package only. Because
    they live inside the project folder they are packed with None Update rather
    than None Include; switching to Include is a duplicate-item error.

    The ProjectReference to CodeBrix.Audio is deliberately ORDINARY - no
    PrivateAssets - because the dependency is the point. CodeBrix.Audio.ModestSynth
    .Tests carries three tests that read both built .nupkg files and check that
    the declared dependency version, the two package versions and the two
    assembly versions all agree; they SKIP rather than fail if the packages are
    not there.

VERSIONING. Directory.Build.props at the repository root computes the version
from the UTC clock at build time: 1.<years since 2026>.<day of year>.<minute of
day>. It is strictly increasing over time and is NOT SemVer - major is pinned to
1 and minor encodes the year, so major and minor say nothing about API
compatibility. Two builds in the same UTC minute produce the SAME version, so
never publish two packages from within one minute. The full rationale is in a
comment block in that file; re-baseline by changing _VersionBaseYear there.

PIN THE VERSION WHEN YOU PUBLISH. MSBuild evaluates Directory.Build.props once
per PROJECT, not once per build, so two projects whose evaluations straddle a UTC
minute boundary during a long build would differ in the last field. For a release
build, supply the value instead - a global property reaches every project in the
build, including the inner project-reference builds:

    dotnet build CodeBrix.Audio.slnx -c Release -p:BuildVersion=1.0.249.640

Ordinary development builds need none of that; the version they stamp is
throwaway either way, and the two projects agree in practice because the whole
solution evaluates inside a second.

Publishing follows the family rule: tag the repository at the version that was
published, so the latest git tag and the latest nuget.org version agree. Both
packages are published at that one version and the tag names it once.

DOWNSTREAM. CodeBrix.Audio.Opus.BsdLicenseForever pins a version of this package
in its own csproj, CodeBrix.Audio.ModestSynth takes it by project reference from
inside this repository, and the CodeBrix.Platform AudioPlayer add-in and
GameEngine build on it. A breaking change to the codec-registration seams, to
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

THE TRAILING-TRIM HOLD-BACK. A container states how much of the END of a track
is encoder padding, and the decoder cannot apply it - it has no idea which
packet is the last one until the source says so. PacketAudioPlayer therefore
holds audio back rather than trimming it afterwards:
PacketDecoderAdapter keeps a RING of the most recently decoded samples and lets
one out only once holdCapacity samples have been decoded behind it. At the end
of the stream, what is still in the ring IS the padding, and it is dropped. Two
consequences fall straight out of that shape: latency is exactly the trim (under
one packet in practice), and Position - which counts what was handed over -
never counts a trimmed frame.

  - THE RING GROWS AND NEVER SHRINKS. holdBuffer.Length is the physical modulus;
    holdCapacity is the logical window, and may be smaller. Lowering the trim
    therefore allocates nothing and leaves holdCount temporarily ABOVE
    holdCapacity; the read loop releases that surplus before it takes anything
    new in, so held audio is delayed rather than dropped. Raising the trim past
    any previous value is the only case that allocates, and SetTrailingTrimFrames
    allocates the replacement on the CALLING thread and publishes it through
    trimChangePending, so the audio thread does not allocate either. Steady state
    is allocation-free, and a test pins that with
    GC.GetAllocatedBytesForCurrentThread.
  - A TRIM OF ZERO IS THE OLD CODE PATH, byte for byte: holdCapacity == 0 takes
    the straight copy the read loop always did, with no ring involved. A test
    compares a zero-trim run against a run that never touches the feature.
  - SEEK CLEARS THE RING but keeps the trim and the buffer: the audio around a
    jump is not the end of the track, and the trim belongs to the track.
  - PER-PACKET DISCARD PADDING (AudioPacket.DiscardPadding, Matroska's term)
    feeds the SAME window: holdCapacity is max(track trim, the padding of the
    most recent packet). Applying it that way, rather than remembering the
    largest value seen, is what makes "the value on the LAST packet wins" fall
    out naturally - a padding on a packet in the middle of a stream raises the
    window only while that packet is the most recent, so its audio is delayed and
    then released, while a padding on the last packet is still raised when the
    stream ends. Its one limitation is inherent and is documented for consumers:
    a per-packet value is only learned when that packet arrives, so it can hold
    back only what is still in the ring plus what that packet decodes to. A trim
    set in advance is the exact instrument; the per-packet value is the
    convenient one.

WHY THERE IS NO DRAIN() ON IPacketSoundDecoder. The obvious alternative to the
hold-back is to have the decoder emit its final partial window and let the
caller cut it. That would mean a new member on a PUBLISHED interface, which
breaks every external implementer that does not know about it - and the packet
seam exists precisely so that other packages can implement it. If a drain is
ever genuinely needed, it goes on a SEPARATE optional interface that a decoder
opts into (`is IPacketSoundDrain drain`), never as a required member here. The
loss members added later (ConcealLoss, SupportsLossConcealment) went in as
DEFAULT INTERFACE METHODS for exactly the same reason: a default body is
additive, an abstract member is not.

THE LOSS SEAM. A demultiplexer that can see packets are missing says so with
AudioPacket.Loss(duration) or AudioPacket.Loss(frames), and the player asks the
decoder to conceal exactly that length. The design points worth keeping:

  - A DEFAULT INTERFACE METHOD, not a new required member. IPacketSoundDecoder
    .ConcealLoss defaults to DecodePacket with an EMPTY packet, so a decoder
    written before the member existed - including any third-party one - keeps
    working and keeps whatever concealment it had behind the empty-packet
    convention. SupportsLossConcealment defaults to false and is informational:
    the player does not consult it, because it falls back to silence anyway.
  - THE EMPTY-PACKET CONVENTION IS KEPT, and the player now honours it. It used
    to short-circuit an empty packet to "nothing decoded" without calling the
    decoder at all, which meant a decoder with concealment of its own never saw
    the gap. An empty, non-loss packet is now passed straight to DecodePacket -
    the lengthless way of saying one packet was lost. For the built-in Vorbis
    decoder that is a no-op (it returns 0 either way, since it cannot know the
    length), so nothing observable changed for this package's own codec.
  - THE PLAYER ALWAYS FILLS THE GAP. It asks ConcealLoss for what is still
    missing, caps what comes back at both the gap and the buffer, and writes
    silence for a call that returns nothing - so a gap is always exactly as long
    as it really was, whatever the decoder does, and the audio after it does not
    slide earlier. Concealed frames are media time: they advance Position and
    they flow through the trailing-trim hold-back like any other audio.
  - VORBIS ANSWERS WITH SILENCE OF THE EXACT LENGTH. It overrides ConcealLoss
    rather than leaving the default, so the behaviour is stated in the codec
    rather than left to the player's fallback. The test measures the re-sync
    cost: after a gap, ONE packet is decoded against the overlap window the
    packet before the gap left behind, and from the packet after that the audio
    is sample-exact against an unbroken decode.

THE NON-STARTING PROBE, AND THE ONE LIST THAT KEEPS IT HONEST.
SharedAudioOutput.CreatePacketDecoder has to start the shared output, because
the registry lives on the running engine - so merely asking whether a codec is
available opened the audio device. SharedAudioOutput.IsPacketCodecSupported and
SupportedPacketCodecIds answer that question without starting anything.

  THE SINGLE SOURCE OF TRUTH IS ManagedCodecs.BuiltInPacketCodecFactories, an
  internal static list of factory INSTANCES. ManagedCodecs.RegisterAll registers
  exactly that list (which is what EnsureStarted calls), and the probe asks
  exactly that list plus SharedAudioOutput's own ExtraPacketCodecFactories, in
  the same order EnsureStarted applies them. There is no second registry and no
  second list of codec names, so the two cannot drift: adding a built-in packet
  codec to the list registers it AND makes the probe report it, in one edit.
  Sharing one factory instance across engines is safe because a factory holds no
  per-engine state - the engine keeps priority and registration order in its own
  PacketCodecRegistration record.

  Matching is OrdinalIgnoreCase, which is what the engine's packet registry
  dictionary uses, so the probe and CreatePacketDecoder agree about case. The
  probe answers for the SHARED OUTPUT ONLY: a factory registered directly on some
  other AudioEngine is invisible to it, and that is stated in the API docs and in
  AGENT-README rather than worked around.



PROVENANCE AND VENDORED SOURCES
===============================
THIRD-PARTY-NOTICES.txt is the authoritative record of what came from where,
what was changed, and under which licences. Consult it rather than this file for
any provenance question.

Summary: parts of CodeBrix.Audio are adapted from NAudio, NLayer, NVorbis and
MeltySynth (all MIT); the FLAC decoder and the SFZ engine were written here from
their specifications; CodeBrix.Audio.Engine is vendored SoundFlow (MIT); the
native backend is built from miniaudio and stb_vorbis (Unlicense / MIT-0).

THE RECORD OF THE STEMS AND DECENT SAMPLER PROJECT. The multi-track player, the
Suno stems loader, the MIDI/audio alignment estimator, the lenient MIDI readers,
the shared MPE contract, the whole Decent Sampler engine and the ModestSynth
add-on were built to one plan and one measurement programme, and the documents
that record them live outside this repository under ~/ClaudeHome/ (they name
corpora and real libraries, so none of them can be committed here):
PLAN_codebrix_audio_suno_stems_and_decent_sampler_2026-09-06.md is the plan and
the decisions Jeremy confirmed against it; decent-sampler/MEASUREMENTS_ds_
reference_semantics_2026-09-06.txt holds all three measurement rounds with the
replay material beside it under decent-sampler/measure/; decent-sampler/
FIDELITY_p1_sampler_engine_2026-09-06.txt, FIDELITY_p2_effects_buses_2026-09-06
.txt and FIDELITY_p8c_retune_2026-09-06.txt are the fidelity tables that phase by
phase compare a render here against the reference player's own recording;
decent-sampler/DIAGNOSTIC_global_swarm_residual_2026-09-06.txt is the standing
write-up of the one whole-preset residual that did not close, with the six
reference launches that would settle it; and completion/*.txt is one report per
phase - what it built, its public API, its decisions, the bugs it fixed and what
it left. Anything in this file that says "measured" traces to one of those.
Nothing there is a licence obligation; it is the engineering record, kept
separate because a shipped document carries no dates, no phase names and no
corpus names.

MAINTAINING CODEBRIX.AUDIO.ENGINE
---------------------------------
PER-NATIVE LICENCE FILE (added 2026-08-29, Jeremy's family convention): every
runtimes/<rid>/native/ folder carries LICENSE-MiniAudio.txt - the miniaudio and
stb_vorbis licence texts beside the binary built from them. The name is
package-unique on purpose: a file named plainly LICENSE beside a native collides
in a consuming application's output folder with any other package shipping a
same-named file there (CodeBrix.VideoPlayback.Dav1d ships LICENSE-Dav1d.txt,
CodeBrix.PdfDocuments' rasterizer ships LICENSE-Pdfium.txt, for the same reason).
The file is committed beside the binaries and flows into the package through the
existing runtimes glob; a native rebuild replaces the binaries and leaves it.

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
    above), those calls could DEADLOCK a UI thread outright - the window never
    paints, and there is no exception and no log entry to tell you why. It is
    file-dependent, so it looks intermittent: a read served from the stream
    buffer completes synchronously and slips through, while an MP3 carrying a
    large ID3 tag (embedded album art, say) hangs. On those versions, always open
    audio sources from a background thread and marshal the result back to the UI.
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
