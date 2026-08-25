================================================================================
EXTRAS-README: CodeBrix.Audio
Samples, tools and other content in this repository that is not part of a NuGet
package
================================================================================

This repository ships one NuGet package and has no sample applications and no
demo projects. Everything below is developer tooling and test data: none of it
is packed, and none of it is needed to consume
CodeBrix.Audio.MitLicenseForever.

Each tool carries its own README.txt with the full detail; the entries here say
what it is, how to run it, and why it exists. NOTHING IN tools/ INSTALLS
ANYTHING ON YOUR MACHINE - every script checks for what it needs, names anything
missing, prints the command that would install it, and stops. Installing is your
decision.


tools/build_native_libraries/ - build the native audio backend
==============================================================
  Path:   tools/build_native_libraries/  (read its README.txt first)

  WHAT IT IS
    Everything needed to rebuild libcodebrix_miniaudio.so / .dylib /
    codebrix_miniaudio.dll - the library CodeBrix.Audio.Engine P/Invokes into -
    for all seven runtime identifiers the package ships. Nothing here reaches
    outside this repository: the C sources are vendored in
    ../../native/miniaudio/, and no build step downloads source code. The only
    things fetched are the Linux build container images.

  HOW TO RUN IT
      # Linux (all three Linux RIDs, arm64 and riscv64 under emulation)
      cd tools/build_native_libraries
      ./build.sh x64                  # or arm64 / riscv64 / all
      CONTAINER_ENGINE=docker ./build.sh x64      # force an engine
      PIN_DIGEST=1 ./build.sh all                 # pull images by digest

      # Windows
      cd tools\build_native_libraries\windows
      .\build.ps1 x64                 # or arm64 / all

      # macOS
      cd tools/build_native_libraries/macos
      ./build.sh arm64                # or x64 / all

    Output is git-ignored, under output/<rid>/, alongside a build-info.txt
    recording sha256, pins, compiler, glibc ceiling and dynamic dependencies.

  WHAT IT DEMONSTRATES / WHY IT MATTERS
    Every build must pass a verification gate - required exports, codec
    coverage, dependency policy, compatibility floor, target architecture, and a
    dlopen + decode smoke test - before anything is written to output/. The
    containers are not convenience: glibc symbol versioning is forward-only, so
    building on a current desktop distro would quietly restrict the package to
    the newest distributions and the failure would only appear on a user's
    machine. macOS has the same problem in Mach-O form and is fixed with an
    explicit deployment target from pins.env instead.

  RELATED: native/miniaudio/ holds the vendored C sources, the CMake project and
  BUILD-PROVENANCE.txt, which records what produced each shipped binary.


tools/make_test_fixtures/ - regenerate the audio and SoundFont fixtures
=======================================================================
  Path:   tools/make_test_fixtures/make_fixtures.sh
          tools/make_test_fixtures/make_soundfont.py

  WHAT THEY ARE
    make_fixtures.sh produced every .ogg / .flac / .wav / .opus file under
    tests/Assets/audio/. They are NOT third-party audio: they are synthesized
    here - sine tones, sweeps, noise and silence - and encoded with ffmpeg, so a
    year from now they can be regenerated instead of being mystery binaries.

    make_soundfont.py produced tests/Assets/soundfont/codebrix-test.sf2, a
    minimal fully synthetic SF2 built from sine tones. No real SoundFont is
    committed here and none should be: they run to tens of megabytes and are
    variously licensed, and this package is MIT.

  HOW TO RUN THEM
      cd tools/make_test_fixtures
      ./make_fixtures.sh              # into ../../tests/Assets/audio
      ./make_soundfont.py             # into ../../tests/Assets/soundfont
      OUT_DIR=/tmp/fixtures ./make_fixtures.sh

  PREREQUISITES (installed by YOU)
    make_fixtures.sh: ffmpeg with the libvorbis and libopus encoders and the
    native flac encoder.
      Debian-based Linux:  sudo apt install ffmpeg
      macOS (Homebrew):    brew install ffmpeg
      Windows (winget):    winget install Gyan.FFmpeg
    Verify with: ffmpeg -hide_banner -encoders | grep -E 'libvorbis|libopus|flac'
    make_soundfont.py: python3, standard library only.

  WHAT THEY DEMONSTRATE / WHY IT MATTERS
    Every fixture is chosen to exercise a decoder path: the .ogg files cover
    mono/stereo, three sample rates and a long sweep for seek tests; the .flac
    files cover 16- and 24-bit, mono and stereo, all four stereo decorrelation
    modes, constant / fixed-predictor / LPC / verbatim subframes and a short
    final block, and each ships with the exact .wav it was encoded from because
    a lossless decoder is correct only if it reproduces that PCM sample for
    sample; the .opus files exist for the METADATA layer, since this library
    does not decode Opus.

    REGENERATE DELIBERATELY, not as a side effect of adding one file. The .flac
    and .wav files reproduce byte-identically on the same ffmpeg build, but the
    Ogg ones never do: an Ogg muxer picks a random stream serial number per run,
    and the encoder version lands in the vendor string.

    The SoundFont fixture is shaped for the tests that use it - two instruments,
    one looping and one one-shot, split key ranges, a global instrument zone and
    four samples at different root keys - so region traversal, both LoopMode
    branches and generator precedence are all real.


tools/sfz_opcode_survey/ - measure SFZ opcode coverage over real libraries
==========================================================================
  Path:   tools/sfz_opcode_survey/  (read its README.txt first)

  WHAT IT IS
    A tool for deciding the scope of SFZ support by COUNTING rather than
    guessing. Neither the SFZ specification nor any player's support matrix
    tells you what a given set of libraries needs, so this parses a folder of
    real SFZ libraries with CodeBrix.Audio's own parser and reports which
    opcodes they use. Unlike its shell-script siblings in tools/, it needs the
    SFZ parser, so it is a small console project referencing the library; it is
    not packable and is not in CodeBrix.Audio.slnx.

  HOW TO RUN IT
      cd tools/sfz_opcode_survey
      dotnet run -- <corpus-directory> [output-directory]

    Each IMMEDIATE SUBDIRECTORY of <corpus-directory> is treated as one library;
    every .sfz under it is parsed recursively, following #include.

  PREREQUISITES (installed by YOU)
    The .NET SDK, and a corpus of SFZ libraries you supply. Nothing is
    downloaded.

  WHAT IT WRITES
    opcodes.md               every opcode found, ranked by how many LIBRARIES
                             use it, with raw occurrence counts alongside.
    coverage.md              the coverage curve: for each N, how many libraries
                             would load with zero unimplemented opcodes if the
                             top N opcodes were implemented.
    libraries.md             per-library breakdown: files, regions, distinct
                             opcodes, parse problems.
    implemented-coverage.md  coverage against what the library ACTUALLY
                             implements, read from SfzSupportedOpcodes in the
                             assembly this tool builds against - so it stays
                             truthful as the engine grows. This is the report to
                             re-run over a new library before promising it will
                             play.

  WHAT IT DEMONSTRATES / WHY IT MATTERS
    Two counting rules make the numbers mean something, and both are easy to get
    wrong. Opcodes are ranked by the number of LIBRARIES that use them, never by
    raw occurrence, or one sprawling library decides the whole ranking. And only
    ROOT .sfz files are counted - a file meant to be #included keeps its
    unresolved $variables when parsed standalone, which turns every variable
    into a phantom opcode. If a run reports an implausible number of distinct
    opcodes, or names containing '$', that is the first thing to check.


tests/Assets/ - the committed fixture sets
==========================================
  Path:   tests/Assets/audio/      (manifest: AUDIO-FIXTURES.txt)
          tests/Assets/soundfont/  codebrix-test.sf2
          tests/Assets/synth/      reference vectors for the synth tests

  The audio and SoundFont sets are generated by tools/make_test_fixtures (above)
  and AUDIO-FIXTURES.txt says what each audio file is for. tests/Assets/synth/
  holds the Freeverb (public domain) and TinySoundFont (MIT) reference vectors
  the carried-over synthesizer suite compares against.

  No third-party audio, no real SoundFont and no SFZ library is committed
  anywhere in this repository. The SFZ tests build their instruments on the fly
  into a temp directory per test.


tests/ - the test projects
==========================
  Path:   tests/CodeBrix.Audio.Tests/
          tests/CodeBrix.Audio.Engine.Tests/

  Not samples, but they are the other non-package content in the repository and
  they are the best worked examples of every public API. AGENT-README.txt's
  "WORKING EXAMPLES ON GITHUB" section maps each feature to the file that
  exercises it, and MAINTAINER-README.txt's TESTING section explains how they
  are built and what they pin.

  Run them with:
      dotnet test CodeBrix.Audio.slnx

  The tests that open a real audio device and MAKE SOUND are opt-in, so an
  ordinary run is silent and headless-safe:
      CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 dotnet test          # main suite
      CODEBRIX_AUDIO_ENGINE_RUN_PLAYBACK_TESTS=1 dotnet test   # Engine suite
================================================================================
