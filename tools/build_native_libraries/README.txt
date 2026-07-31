================================================================================
tools/build_native_libraries - building the codebrix_miniaudio native libraries
================================================================================

WHAT THIS IS
--------------------------------------------------------------------------------
Everything needed to rebuild the native library that CodeBrix.Audio.Engine
P/Invokes into - libcodebrix_miniaudio.so / .dylib / codebrix_miniaudio.dll -
for all seven runtime identifiers the package ships.

Nothing here reaches outside this repository. The C sources (miniaudio and
stb_vorbis) are vendored in ../../native/miniaudio/, and no build step downloads
source code. The only things fetched are the Linux build container images.

NOTHING HERE INSTALLS ANYTHING ON YOUR MACHINE. Every script checks for what it
needs, and if something is missing it names it, prints the command that installs
it, and stops. Installing is your decision, not the script's.


WHY A CONTAINER - EVEN FOR X64
--------------------------------------------------------------------------------
glibc symbol versioning is forward-only. A binary compiled against the glibc on
a current desktop distro will not load on an older one, so building the "native"
x64 library on the development workstation would quietly restrict the package to
the newest distributions - and the failure only shows up on a user's machine.

The manylinux images exist to solve exactly this: they are old userlands with
modern compilers. Building in them fixes the floor. The current binaries came
out with a glibc ceiling of 2.17 for x64/arm64, which covers essentially every
Linux still in service, and 2.34 for riscv64 (nothing older exists on that
architecture).

macOS and Windows have no equivalent problem, so those two are built natively on
their own hardware - see the sections below.


ARCHITECTURE MATRIX (7 RIDs)
--------------------------------------------------------------------------------
  RID             built where                            script
  --------------  ------------------------------------  --------------------------
  linux-x64       container, manylinux_2_28_x86_64       ./build.sh x64
  linux-arm64     container, manylinux_2_28_aarch64      ./build.sh arm64
  linux-riscv64   container, manylinux_2_39_riscv64      ./build.sh riscv64
  win-x64         Windows x64 host, MSVC                 windows\build.ps1 x64
  win-arm64       Windows x64 host, MSVC ARM64 tools     windows\build.ps1 arm64
  osx-arm64       Apple Silicon Mac                      macos/build.sh arm64
  osx-x64         Apple Silicon Mac (cross-compile)      macos/build.sh x64

The three Linux builds all run on ONE Linux machine: arm64 and riscv64 run under
qemu user-mode emulation. codebrix_miniaudio is a single C translation unit, so
even an emulated build takes a couple of minutes - there is no reason to find
real hardware for it. (Testing on real hardware is a separate, worthwhile thing;
building is not.)

All version, commit and image pins live in ONE file: pins.env. Edit pins there
and nowhere else; both scripts source it and echo the resolved values at the top
of every run.


================================================================================
PREREQUISITES
================================================================================
Listed in full for every platform, including things that happen to be installed
on the machine used in 2026 - a year from now this may be a different machine.

--------------------------------------------------------------------------------
LINUX HOST (builds linux-x64, linux-arm64, linux-riscv64)
--------------------------------------------------------------------------------
  1. A container engine - podman (preferred) or docker.
       Debian-based:   sudo apt install podman
       Fedora/RHEL:    sudo dnf install podman
       Verify:         podman --version
       Used in 2026:   podman 5.4.2

  2. qemu user-mode emulation + binfmt registration, needed ONLY for the
     architectures that do not match your host (on an x64 host: arm64, riscv64).
       Debian-based:   sudo apt install qemu-user-static binfmt-support
       Verify:         ls /proc/sys/fs/binfmt_misc | grep qemu
                       (you want qemu-aarch64 and qemu-riscv64 listed)
       Alternative that installs nothing permanently - register the handlers
       from a container:
                       sudo podman run --rm --privileged \
                            docker.io/multiarch/qemu-user-static --reset -p yes

  3. ffmpeg, ONLY if the audio test fixtures need regenerating. The build's
     decode smoke test reads tests/Assets/audio/vorbis-tone-stereo-44100.ogg,
     which is committed to the repository, so a normal build does NOT need
     ffmpeg. See ../make_test_fixtures/make_fixtures.sh.
       Debian-based:   sudo apt install ffmpeg
       Verify:         ffmpeg -hide_banner -encoders | grep -E 'libvorbis|flac'
       Used in 2026:   ffmpeg 7.1.5

  4. Disk: about 3 GB for the three container images. The build itself needs
     almost nothing - one .c file compiles in seconds.

  NOT required on the host: cmake, gcc, make. They live inside the container.
  (A host cmake/gcc is handy for a quick local build - see "A QUICK LOCAL BUILD"
  at the end - but that binary is for experimenting, never for shipping.)

--------------------------------------------------------------------------------
WINDOWS HOST, x64 (builds win-x64 and win-arm64)
--------------------------------------------------------------------------------
  1. Visual Studio 2022 (or newer) with these workloads/components:
       - "Desktop development with C++"
       - "MSVC v143 - VS 2022 C++ ARM64/ARM64EC build tools" (for win-arm64)
       Install via the Visual Studio Installer, or:
         winget install Microsoft.VisualStudio.2022.Community
       Verify: open "x64 Native Tools Command Prompt for VS 2022" and run
         cl
       (Community, Professional and Enterprise all work; so do the standalone
       Build Tools.)

  2. CMake 3.26 or newer.
       winget install Kitware.CMake
       Verify: cmake --version
       (Visual Studio's bundled CMake also works if it is on PATH.)

  3. PowerShell 5.1 (in-box) or PowerShell 7+.

  No container, no emulator: win-arm64 is a cross-compile from the x64 host,
  which is what the ARM64 build tools component is for.

--------------------------------------------------------------------------------
macOS HOST, Apple Silicon (builds osx-arm64 and osx-x64)
--------------------------------------------------------------------------------
  1. Xcode Command Line Tools.
       xcode-select --install
       Verify: cc --version    (Apple clang)

  2. CMake 3.26 or newer.
       brew install cmake      (or download from cmake.org)
       Verify: cmake --version

  Both macOS binaries come from the one Apple Silicon machine: osx-x64 is a
  cross-compile via -DCMAKE_OSX_ARCHITECTURES=x86_64. An Intel Mac is not
  needed. The build ad-hoc code-signs the .dylib, which is what CMakeLists.txt
  in native/miniaudio already configures.


================================================================================
USAGE
================================================================================

LINUX
--------------------------------------------------------------------------------
    cd tools/build_native_libraries
    ./build.sh x64            # or arm64 / riscv64 / all

  Options (environment variables):
    CONTAINER_ENGINE=docker ./build.sh x64      force an engine
    PIN_DIGEST=1 ./build.sh all                 pull images by digest, not tag
                                                (fully reproducible; digests are
                                                recorded in pins.env)

  Output, git-ignored:
    output/<rid>/libcodebrix_miniaudio.so
    output/<rid>/build-info.txt      sha256, pins, compiler, glibc ceiling,
                                     dynamic dependencies

WINDOWS
--------------------------------------------------------------------------------
    cd tools\build_native_libraries\windows
    .\build.ps1 x64           # or arm64 / all

  Output: ..\output\win-x64\codebrix_miniaudio.dll (+ build-info.txt)

macOS
--------------------------------------------------------------------------------
    cd tools/build_native_libraries/macos
    ./build.sh arm64          # or x64 / all

  Output: ../output/osx-arm64/libcodebrix_miniaudio.dylib (+ build-info.txt)


================================================================================
THE VERIFICATION GATE
================================================================================
A build that fails ANY of these does not get written to output/. This is the
whole point of the tooling: a binary that compiles is not necessarily a binary
that works.

  1. Required exports - all 27 entry points that
     src/CodeBrix.Audio.Engine/Backends/MiniAudio/Native.cs binds to, plus
     sf_has_vorbis. A missing symbol here is a run-time crash later.

  2. Codec coverage - counts of ma_stbvorbis_* (Ogg Vorbis), ma_dr_flac_*,
     ma_dr_mp3_* and ma_dr_wav_* symbols must all be non-zero. Catches a codec
     being switched off by accident, which is otherwise invisible until a user
     opens that kind of file.

  3. Dependency policy - the binary must not dynamically link libasound,
     libpulse, libjack, libvorbis, libogg or libFLAC. miniaudio dlopen's the
     audio backends at run time by design; linking one would force every user to
     have that library installed.

  4. glibc ceiling - reported and recorded (Linux). This is the number that says
     how old a distro the binary still runs on.

  5. Decode smoke test - smoke_test.c dlopen's the freshly built library exactly
     the way .NET does and drives it through the real decode path:
       sf_has_vorbis() == 1
       -> allocate decoder + config
       -> ma_decoder_init_memory() on an .ogg          (the PULL-mode path)
       -> ma_decoder_get_length_in_pcm_frames() > 0    (push mode reports 0, so
                                                       a zero means the pull
                                                       path has regressed)
       -> read frames, and check they are not all silence
       -> seek to the midpoint, read again
       -> clean teardown
     For emulated architectures this runs under qemu inside the container, so
     arm64 and riscv64 are genuinely exercised, not just compiled.


================================================================================
ADOPTING A BUILT BINARY INTO THE PACKAGE
================================================================================
  1. Copy it into the runtimes tree:

       cp output/<rid>/libcodebrix_miniaudio.so \
          ../../src/CodeBrix.Audio.Engine/Backends/MiniAudio/runtimes/<rid>/native/

     (.dylib for osx-*, codebrix_miniaudio.dll for win-*.) The CodeBrix.Audio
     .csproj packs runtimes/** automatically, so a new RID folder needs no
     project edits - but do check the RID is handled in Native.cs's
     GetLibraryPath() arch switch, or the resolver will never look for it.

  2. Record the build in ../../native/miniaudio/BUILD-PROVENANCE.txt - copy the
     values straight out of output/<rid>/build-info.txt. That file is how anyone
     later can tell which binary came from what.

  3. Run the managed test suites (dotnet test) and, for a RID you have hardware
     for, the opt-in playback tests:
       CODEBRIX_AUDIO_ENGINE_RUN_PLAYBACK_TESTS=1 dotnet test

  4. Ogg Vorbis is only present in binaries built from these sources. Until all
     seven RIDs are rebuilt, .ogg on the remaining ones is served by the managed
     Vorbis decoder in CodeBrix.Audio rather than the native path.


================================================================================
TROUBLESHOOTING
================================================================================
"neither podman nor docker found"
    Install one (see PREREQUISITES). The script will not install it for you.

"exec format error" / every command in the container dies
    The binfmt handler for that architecture is not registered. See
    PREREQUISITES item 2. Check with: ls /proc/sys/fs/binfmt_misc | grep qemu

"the smoke-test input is missing"
    tests/Assets/audio/vorbis-tone-stereo-44100.ogg is not in the working tree.
    Regenerate the fixtures: cd ../make_test_fixtures && ./make_fixtures.sh
    (needs ffmpeg).

Image pull fails / tag no longer exists
    quay.io/pypa retires old dated tags. Pick a current tag from
    https://quay.io/organization/pypa, update pins.env, and record the new
    digest beside it. Any manylinux_2_28 (or newer) image works; the glibc
    number in the image name IS the compatibility floor you are choosing.

The build succeeds but verification fails on missing exports
    Something in native/miniaudio/library.c or library.h was renamed. The
    required list is in container_build.sh; it must stay in step with the
    [LibraryImport] entry points in Native.cs.

Podman "permission denied" writing output/
    Rootless podman maps your user into the container; the :Z mount flag handles
    SELinux relabelling. If you are on a system with an unusual security policy,
    run with --userns=keep-id or build into a directory you own.

Slow emulated builds
    Expected: qemu emulation is roughly 5-10x slower. For one translation unit
    that is still only a couple of minutes.


================================================================================
A QUICK LOCAL BUILD (for experimenting only - never ship this binary)
================================================================================
    cd native/miniaudio
    cmake -B /tmp/ma-build -DCMAKE_BUILD_TYPE=Release
    cmake --build /tmp/ma-build

  Handy when changing library.c, because the feedback loop is seconds. The
  result is linked against YOUR distro's glibc, so it must never be copied into
  runtimes/ - use ./build.sh for anything that ships.


================================================================================
FILES
================================================================================
  README.txt            this document
  pins.env              every version / commit / image pin (edit here only)
  build.sh              Linux host entry point (container orchestration)
  container_build.sh    the build + verification, runs inside the container
  smoke_test.c          dlopen + decode verification program (C, no dependencies)
  windows/build.ps1     win-x64 and win-arm64
  macos/build.sh        osx-arm64 and osx-x64
  output/               build results (git-ignored)
