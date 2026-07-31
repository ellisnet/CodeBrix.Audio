================================================================================
native/miniaudio — the codebrix_miniaudio native backend
================================================================================

This folder contains everything needed to (re)build the native library that
CodeBrix.Audio.Engine's MiniAudio backend P/Invokes into. Nothing here reaches
outside this repository: the miniaudio source is vendored in-tree.

CONTENTS
--------------------------------------------------------------------------------
  library.c                     SoundFlow's thin C wrapper (sf_* entry points),
                                compiled together with miniaudio AND stb_vorbis
                                in one TU via #define MINIAUDIO_IMPLEMENTATION.
                                Also defines sf_has_vorbis(), the capability
                                probe managed code uses to tell whether a given
                                binary has an Ogg Vorbis decoder.
  library.h                     Wrapper header + the C-marshalled config structs.
  miniaudio-80cf7b2/miniaudio.h Vendored miniaudio single-header library,
                                mackron/miniaudio @ commit 80cf7b2 (v0.11.24),
                                dual Unlicense / MIT-0. The folder name records
                                the exact upstream commit it came from.
  stb_vorbis-31c1ad3/           Vendored stb_vorbis 1.22 (Ogg Vorbis decoder),
    stb_vorbis.c                nothings/stb @ commit 31c1ad3, dual MIT /
                                public domain. miniaudio keeps its Vorbis
                                support switched off unless stb_vorbis is
                                compiled into the same translation unit, which
                                is exactly what library.c does.
  CMakeLists.txt                CMake project. Produces:
                                  Windows: codebrix_miniaudio.dll
                                  Linux:   libcodebrix_miniaudio.so
                                  macOS:   libcodebrix_miniaudio.dylib
  BUILD-PROVENANCE.txt          What produced each shipped binary: date, host,
                                toolchain, container digest, source commits,
                                sha256, glibc ceiling.

  See ../../tools/build_native_libraries/README.txt for the build scripts, the
  full per-platform prerequisites, and the verification gate. That is the
  document to read first when rebuilding anything here.

PROVENANCE / LICENSES
--------------------------------------------------------------------------------
See ../../THIRD-PARTY-NOTICES.txt at the repo root for the complete provenance
and license text (SoundFlow MIT for the wrapper; miniaudio Unlicense / MIT-0).

BUILDING ONE RID LOCALLY
--------------------------------------------------------------------------------
Requires CMake >= 3.26 and a C11 toolchain.

  cd native/miniaudio
  cmake -B build -DCMAKE_BUILD_TYPE=Release
  cmake --build build --config Release

The resulting library lands in native/miniaudio/build/ (or build/Release/ on
Windows). To make the managed library pick it up, copy it into:

  src/CodeBrix.Audio.Engine/Backends/MiniAudio/runtimes/<rid>/native/

where <rid> is one of: win-x64, win-arm64, linux-x64, linux-arm64,
linux-riscv64, osx-x64, osx-arm64. The DllImportResolver in Backends/MiniAudio/Native.cs loads the
library from that runtimes/<rid>/native/ layout at runtime.

BUILDING ALL SEVEN RIDS BY HAND (no CI / GitHub required)

NOTE: prefer tools/build_native_libraries, which wraps all of this, adds the
verification gate, and records provenance. The raw recipes are kept here for
reference and for one-off experiments.
--------------------------------------------------------------------------------
Each RID is just the "BUILDING ONE RID" recipe above with platform/arch flags.
All seven can be produced from three machines — a Windows x64 box, an Apple
Silicon Mac, and a Linux x64 box — where the extra arches on each are
cross-compiled or emulated.
Use a fresh build/ directory per arch (or `rm -rf build` between configs).

  Windows (x64 host; Visual Studio 2022 with the ARM64 build tools installed):
    win-x64:    cmake -B build -A x64   -DCMAKE_BUILD_TYPE=Release
    win-arm64:  cmake -B build -A ARM64 -DCMAKE_BUILD_TYPE=Release
    build:      cmake --build build --config Release
    output:     build\Release\codebrix_miniaudio.dll

  macOS (Apple Silicon host):
    osx-arm64:  cmake -B build -DCMAKE_OSX_ARCHITECTURES=arm64  -DCMAKE_BUILD_TYPE=Release
    osx-x64:    cmake -B build -DCMAKE_OSX_ARCHITECTURES=x86_64 -DCMAKE_BUILD_TYPE=Release
    build:      cmake --build build --config Release
    output:     build/libcodebrix_miniaudio.dylib
    (or one universal binary: -DCMAKE_OSX_ARCHITECTURES="arm64;x86_64")

  Linux (x64 host):
    linux-x64:  cmake -B build -DCMAKE_BUILD_TYPE=Release
    linux-arm64 (cross-compile; needs the aarch64 toolchain, e.g. on Debian/
      Ubuntu: apt-get install gcc-aarch64-linux-gnu g++-aarch64-linux-gnu):
                cmake -B build -DCMAKE_BUILD_TYPE=Release \
                      -DCMAKE_SYSTEM_PROCESSOR=aarch64 \
                      -DCMAKE_C_COMPILER=aarch64-linux-gnu-gcc \
                      -DCMAKE_CXX_COMPILER=aarch64-linux-gnu-g++
                (or just build natively on an ARM64 Linux box)
    build:      cmake --build build --config Release
    output:     build/libcodebrix_miniaudio.so

Copy each output into src/CodeBrix.Audio.Engine/Backends/MiniAudio/runtimes/<rid>/
native/ as described above. (No GitHub Actions / CI is used or required.)

THE COMMITTED BINARIES
--------------------------------------------------------------------------------
The three LINUX binaries (linux-x64, linux-arm64, linux-riscv64) are built from
the sources in this folder by tools/build_native_libraries, in manylinux
containers so they run on old glibc as well as new. They include the Ogg Vorbis
decoder. See BUILD-PROVENANCE.txt for the exact details of each.

The WINDOWS and macOS binaries are still SoundFlow v1.4.1's own prebuilt
miniaudio libraries (built from miniaudio @ 80cf7b2), renamed to
codebrix_miniaudio.*. They predate the Vorbis work and therefore CANNOT decode
Ogg Vorbis; sf_has_vorbis() is absent from them, which is how the managed side
detects the situation and falls back to its own Vorbis decoder. Replacing them
is a matter of running tools/build_native_libraries/windows/build.ps1 on a
Windows x64 machine and tools/build_native_libraries/macos/build.sh on an Apple
Silicon Mac, then recording the results in BUILD-PROVENANCE.txt.
