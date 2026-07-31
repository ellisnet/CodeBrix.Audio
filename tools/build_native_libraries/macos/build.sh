#!/usr/bin/env bash
# ==============================================================================================
# build.sh - build + verify libcodebrix_miniaudio.dylib for osx-arm64 and osx-x64
# ==============================================================================================
#
# USAGE (on an Apple Silicon Mac)
#
#     cd tools/build_native_libraries/macos
#     ./build.sh arm64          # or x64 / all
#
# Both macOS binaries come from the one Apple Silicon machine - osx-x64 is a cross-compile via
# -DCMAKE_OSX_ARCHITECTURES=x86_64. An Intel Mac is not needed.
#
# PREREQUISITES - installed by YOU. This script never installs anything; if something is
# missing it says what, prints the install command, and stops.
#
#   1. Xcode Command Line Tools:   xcode-select --install
#   2. CMake 3.26+:                brew install cmake     (or cmake.org)
#
# See ../README.txt for the full prerequisite list and the verification-gate description.
# ==============================================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
TOOLS_DIR="$(cd "$SCRIPT_DIR/.." && pwd)"
REPO_ROOT="$(cd "$TOOLS_DIR/../.." && pwd)"
NATIVE_SRC="$REPO_ROOT/native/miniaudio"
LIB_NAME=libcodebrix_miniaudio.dylib

if [ ! -f "$TOOLS_DIR/pins.env" ]; then
    echo "ERROR: $TOOLS_DIR/pins.env was not found - every version pin lives in it." >&2
    echo "       The root .gitignore has a blanket '*.env' rule that has excluded this file" >&2
    echo "       from a commit before. Check with:" >&2
    echo "         git check-ignore -v tools/build_native_libraries/pins.env" >&2
    exit 1
fi
# shellcheck source=../pins.env
. "$TOOLS_DIR/pins.env"

# ----------------------------------------------------------------------------------------------
# Prerequisites
# ----------------------------------------------------------------------------------------------
require() {
    command -v "$1" > /dev/null 2>&1 && return 0
    cat >&2 <<EOF

ERROR: '$1' is not on PATH.

  This script does not install anything. Install it yourself:
    $2

EOF
    exit 1
}

require cc    "xcode-select --install"
require cmake "brew install cmake   (or download from cmake.org)"

if [ "$(uname -s)" != "Darwin" ]; then
    echo "ERROR: this script builds the macOS binaries and must run on macOS." >&2
    exit 1
fi

HOST_ARCH="$(uname -m)"
echo "host      : macOS $(sw_vers -productVersion 2>/dev/null || echo '?') ($HOST_ARCH)"
echo "compiler  : $(cc --version | head -1)"
echo "cmake     : $(cmake --version | head -1)"
echo "repository: $REPO_ROOT"

# ----------------------------------------------------------------------------------------------
# Verification gate - the same checks as the Linux container build, using macOS tools.
# ----------------------------------------------------------------------------------------------
REQUIRED_SYMBOLS="
sf_has_vorbis sf_free sf_allocate_decoder sf_allocate_decoder_config sf_allocate_encoder
sf_allocate_encoder_config sf_allocate_device sf_allocate_device_config sf_allocate_context
sf_get_devices sf_free_device_infos sf_context_get_backend
ma_decoder_init ma_decoder_init_memory ma_decoder_uninit ma_decoder_read_pcm_frames
ma_decoder_seek_to_pcm_frame ma_decoder_get_length_in_pcm_frames
ma_encoder_init ma_encoder_uninit ma_encoder_write_pcm_frames
ma_context_init ma_context_uninit ma_device_init ma_device_uninit ma_device_start ma_device_stop
"

verify() {
    local dylib="$1" target_arch="$2"
    local failed=0
    pass() { echo "  [ok] $1"; }
    fail() { echo "  [FAIL] $1"; failed=1; }

    local expect_arch min_target
    case "$target_arch" in
        arm64) expect_arch=arm64;  min_target="$MACOS_MIN_ARM64" ;;
        x64)   expect_arch=x86_64; min_target="$MACOS_MIN_X64" ;;
    esac

    # Architecture. osx-x64 is a cross-compile, so the one thing that must never be assumed is
    # that the file matches the target that was asked for - nothing else here would notice.
    local got_arch
    got_arch="$(lipo -archs "$dylib" 2>/dev/null || echo '?')"
    [ "$got_arch" = "$expect_arch" ] && pass "architecture is $got_arch" \
                                     || fail "architecture is '$got_arch', expected '$expect_arch'"

    # Minimum macOS - this platform's version of the glibc ceiling. See pins.env.
    # A target of 10.14 or newer is recorded as LC_BUILD_VERSION/minos; anything older (the
    # osx-x64 floor is 10.13) uses the legacy LC_VERSION_MIN_MACOSX/version instead. Read both.
    local minos
    minos="$(otool -l "$dylib" | awk '/LC_BUILD_VERSION/ {f=1} f && /^ *minos / {print $2; exit}')"
    [ -n "$minos" ] || minos="$(otool -l "$dylib" |
        awk '/LC_VERSION_MIN_MACOSX/ {f=1} f && /^ *version / {print $2; exit}')"
    [ "$minos" = "$min_target" ] && pass "minimum macOS $minos (runs on $minos and newer)" \
                                 || fail "minimum macOS is ${minos:-unknown}, expected $min_target"

    # Mach-O symbols carry a leading underscore; strip it before comparing.
    local exports
    exports="$(nm -gU "$dylib" | awk '{print $3}' | sed 's/^_//')"

    local missing="" count=0
    for sym in $REQUIRED_SYMBOLS; do
        count=$((count + 1))
        printf '%s\n' "$exports" | grep -qx "$sym" || missing="$missing $sym"
    done
    [ -z "$missing" ] && pass "all $count required symbols exported" || fail "missing exports:$missing"

    local vorbis flac mp3 wav
    vorbis="$(printf '%s\n' "$exports" | grep -c '^ma_stbvorbis_' || true)"
    flac="$(printf '%s\n' "$exports" | grep -c '^ma_dr_flac_' || true)"
    mp3="$(printf '%s\n' "$exports" | grep -c '^ma_dr_mp3_' || true)"
    wav="$(printf '%s\n' "$exports" | grep -c '^ma_dr_wav_' || true)"
    [ "$vorbis" -gt 0 ] && pass "Ogg Vorbis decoder present ($vorbis symbols)" || fail "no Vorbis decoder"
    [ "$flac"   -gt 0 ] && pass "FLAC decoder present ($flac symbols)"        || fail "no FLAC decoder"
    [ "$mp3"    -gt 0 ] && pass "MP3 decoder present ($mp3 symbols)"          || fail "no MP3 decoder"
    [ "$wav"    -gt 0 ] && pass "WAV decoder present ($wav symbols)"          || fail "no WAV decoder"

    # Dependencies: system frameworks and libSystem only. miniaudio loads nothing else at
    # link time - CoreAudio/AudioToolbox are the frameworks CMakeLists.txt links deliberately.
    # otool -L prints the dylib's own LC_ID_DYLIB first; drop it, or the library is reported as
    # a dependency of itself and that lands in build-info.txt.
    local deps forbidden
    deps="$(otool -L "$dylib" | tail -n +2 | awk '{print $1}' | grep -v "/$LIB_NAME\$" || true)"
    forbidden="$(printf '%s\n' "$deps" | grep -E 'libasound|libpulse|libjack|libvorbis|libogg|libFLAC' || true)"
    [ -z "$forbidden" ] && pass "dependencies are system-only" || fail "unexpected dependency: $forbidden"
    printf '%s\n' "$deps" | sed 's/^/       /'

    # Ad-hoc code signature. NOTE: CMakeLists.txt's CODE_SIGNING_REQUIRED / CODE_SIGN_IDENTITY
    # target properties apply to the Xcode generator ONLY and do nothing here; and Apple's linker
    # auto-signs arm64 output but leaves x86_64 unsigned. build_arch therefore signs explicitly -
    # this checks that it took, because an unsigned dylib is refused on Apple Silicon.
    if codesign -dv "$dylib" > /dev/null 2>&1; then
        pass "code signature present ($(codesign -dv "$dylib" 2>&1 | grep -m1 '^Signature' || echo 'ad-hoc'))"
    else
        fail "no code signature - macOS will refuse to load this"
    fi

    # Decode smoke test. arm64 runs natively; an x86_64 build runs under Rosetta 2 when it is
    # installed. Whether it CAN run is decided up front by probing Rosetta - never by treating a
    # failure as a skip, which would let a genuine decode regression (or a wrong-arch dylib) be
    # published behind a friendly message.
    local ogg="$REPO_ROOT/$SMOKE_TEST_OGG"
    local smoke_dir="/tmp/codebrix-smoke-$target_arch"
    if [ ! -f "$ogg" ]; then
        fail "smoke-test input missing: $ogg (run tools/make_test_fixtures/make_fixtures.sh)"
    elif [ "$target_arch" = "x64" ] && [ "$HOST_ARCH" = "arm64" ] \
         && ! arch -x86_64 /usr/bin/true > /dev/null 2>&1; then
        echo "  [--] decode smoke test not run: this is an x86_64 build on an Apple Silicon host"
        echo "       and Rosetta 2 is not installed. Install it to make this check run here:"
        echo "         softwareupdate --install-rosetta"
        echo "       The static checks above still applied; run the managed test suite on x64"
        echo "       hardware (or under Rosetta) to exercise the decode path."
    else
        local smoke_arch_flag=""
        [ "$target_arch" = "x64" ] && smoke_arch_flag="-arch x86_64"
        [ "$target_arch" = "arm64" ] && smoke_arch_flag="-arch arm64"
        echo "  --- decode smoke test ---"
        rm -rf "$smoke_dir" && mkdir -p "$smoke_dir"
        # shellcheck disable=SC2086
        cc -O2 $smoke_arch_flag -o "$smoke_dir/smoke_test" "$TOOLS_DIR/smoke_test.c"
        if "$smoke_dir/smoke_test" "$dylib" "$ogg" 2>"$smoke_dir/err" | sed 's/^/    /'; then
            pass "smoke test"
        else
            sed 's/^/    /' < "$smoke_dir/err"
            fail "smoke test"
        fi
    fi

    return $failed
}

# ----------------------------------------------------------------------------------------------
# Build one architecture
# ----------------------------------------------------------------------------------------------
build_arch() {
    local target_arch="$1" rid osx_arch min_macos build_dir out_dir
    case "$target_arch" in
        arm64) rid=osx-arm64; osx_arch=arm64;  min_macos="$MACOS_MIN_ARM64" ;;
        x64)   rid=osx-x64;   osx_arch=x86_64; min_macos="$MACOS_MIN_X64" ;;
        *) echo "usage: $0 arm64|x64|all" >&2; exit 2 ;;
    esac
    build_dir="/tmp/ma-build-$rid"
    out_dir="$TOOLS_DIR/output/$rid"

    echo
    echo "=============================================================================="
    echo " BUILD $rid"
    echo "=============================================================================="
    echo "  miniaudio  : $MINIAUDIO_VERSION ($MINIAUDIO_COMMIT)"
    echo "  stb_vorbis : $STB_VORBIS_VERSION ($STB_VORBIS_COMMIT)"
    echo "  min macOS  : $min_macos"
    echo

    echo "--- building ---"
    rm -rf "$build_dir"
    # CMakeLists.txt in native/miniaudio is the single source of truth for compiler, linker and
    # code-signing settings; this script passes only the architecture and build type.
    cmake -S "$NATIVE_SRC" -B "$build_dir" \
          -DCMAKE_BUILD_TYPE="${CMAKE_BUILD_TYPE:-Release}" \
          -DCMAKE_OSX_ARCHITECTURES="$osx_arch" \
          -DCMAKE_OSX_DEPLOYMENT_TARGET="$min_macos"
    cmake --build "$build_dir" --config "${CMAKE_BUILD_TYPE:-Release}" -j "$(sysctl -n hw.ncpu)"

    local built="$build_dir/$LIB_NAME"
    [ -f "$built" ] || { echo "ERROR: expected $built, which was not produced." >&2; exit 1; }

    # Ad-hoc sign. Apple's linker does this automatically for arm64 but NOT for x86_64, and
    # CMakeLists.txt's CODE_SIGN_* properties are Xcode-generator-only, so neither can be relied
    # on. Signing here keeps both architectures identical; verify() then checks it took.
    codesign --force --sign - "$built"

    echo
    echo "--- verifying ---"
    if ! verify "$built" "$target_arch"; then
        echo
        echo "VERIFICATION FAILED - nothing written to output/. Fix the build before adopting."
        exit 1
    fi

    mkdir -p "$out_dir"
    cp "$built" "$out_dir/$LIB_NAME"
    local sha
    sha="$(shasum -a 256 "$out_dir/$LIB_NAME" | cut -d' ' -f1)"

    cat > "$out_dir/build-info.txt" <<EOF
codebrix_miniaudio - build information
==============================================================================
RID            : $rid
Built          : $(date -u '+%Y-%m-%d %H:%M:%S UTC')
Built by       : tools/build_native_libraries/macos/build.sh (host build)
Host           : macOS $(sw_vers -productVersion) ($HOST_ARCH)
Compiler       : $(cc --version | head -1)
CMake          : $(cmake --version | head -1)
Build type     : ${CMAKE_BUILD_TYPE:-Release}
Target arch    : $osx_arch
Minimum macOS  : $min_macos

Sources (all vendored in-repo, nothing fetched at build time)
------------------------------------------------------------------------------
miniaudio      : $MINIAUDIO_VERSION  (mackron/miniaudio @ $MINIAUDIO_COMMIT)
stb_vorbis     : $STB_VORBIS_VERSION  (nothings/stb @ $STB_VORBIS_COMMIT)
wrapper        : native/miniaudio/library.c + library.h

Result
------------------------------------------------------------------------------
File           : $LIB_NAME
Size           : $(stat -f %z "$out_dir/$LIB_NAME") bytes
SHA256         : $sha
Dynamic deps   : $(otool -L "$out_dir/$LIB_NAME" | tail -n +2 | awk '{print $1}' | grep -v "/$LIB_NAME\$" | tr '\n' ' ')
Codecs         : WAV, MP3, FLAC, Ogg Vorbis
EOF

    echo
    echo "--- done ---"
    echo "  $out_dir/$LIB_NAME"
    echo "  sha256 $sha"
}

if [ $# -lt 1 ]; then
    echo "usage: $0 arm64|x64|all" >&2
    exit 2
fi

case "$1" in
    arm64|x64) build_arch "$1" ;;
    all)       build_arch arm64; build_arch x64 ;;
    *) echo "usage: $0 arm64|x64|all" >&2; exit 2 ;;
esac

echo
echo "=============================================================================="
echo " Outputs are in tools/build_native_libraries/output/<rid>/"
echo " To adopt them, follow ADOPTING A BUILT BINARY in ../README.txt."
echo "=============================================================================="
