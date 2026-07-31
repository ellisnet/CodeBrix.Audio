#!/usr/bin/env bash
# ==============================================================================================
# container_build.sh - builds + verifies libcodebrix_miniaudio.so INSIDE the build container
# ==============================================================================================
#
# Do not run this on your workstation; build.sh runs it inside the manylinux container for the
# requested architecture. It expects:
#
#   /repo          the repository, read-write (only tools/build_native_libraries/output is written)
#   $TARGET_RID    linux-x64 | linux-arm64 | linux-riscv64
#   pins.env       sourced by build.sh and passed through the environment
#
# Everything it needs beyond a C compiler is in the repository already: miniaudio and stb_vorbis
# are vendored, and there is nothing to download.
# ==============================================================================================

set -euo pipefail

REPO=/repo
NATIVE_SRC="$REPO/native/miniaudio"
TOOLS="$REPO/tools/build_native_libraries"
OUT_DIR="$TOOLS/output/$TARGET_RID"
BUILD_DIR=/tmp/ma-build
LIB_NAME=libcodebrix_miniaudio.so

echo "=============================================================================="
echo " codebrix_miniaudio - $TARGET_RID"
echo "=============================================================================="
echo " container : $(cat /etc/os-release 2>/dev/null | grep -m1 PRETTY_NAME | cut -d= -f2- | tr -d '\"')"
echo " arch      : $(uname -m)"
echo " miniaudio : $MINIAUDIO_VERSION ($MINIAUDIO_COMMIT)"
echo " stb_vorbis: $STB_VORBIS_VERSION ($STB_VORBIS_COMMIT)"
echo

# ----------------------------------------------------------------------------------------------
# 1. Toolchain. The manylinux images ship a C toolchain; cmake is present in most of them, and
#    where it is not we install it INSIDE THE CONTAINER (never on your machine).
# ----------------------------------------------------------------------------------------------
if ! command -v cmake > /dev/null 2>&1; then
    echo "--- cmake is not in this image; installing it inside the container ---"
    if command -v dnf > /dev/null 2>&1; then
        dnf install -y cmake > /dev/null
    elif command -v apt-get > /dev/null 2>&1; then
        apt-get update > /dev/null && apt-get install -y --no-install-recommends cmake > /dev/null
    else
        echo "ERROR: no cmake and no known package manager in this image." >&2
        exit 1
    fi
fi

command -v cc > /dev/null 2>&1 || { echo "ERROR: no C compiler in this image." >&2; exit 1; }

echo "cmake     : $(cmake --version | head -1)"
echo "compiler  : $(cc --version | head -1)"
echo

# ----------------------------------------------------------------------------------------------
# 2. Build. CMakeLists.txt in native/miniaudio is the single source of truth for compiler and
#    linker settings - this script deliberately adds none of its own.
# ----------------------------------------------------------------------------------------------
echo "--- building ---"
rm -rf "$BUILD_DIR"
cmake -S "$NATIVE_SRC" -B "$BUILD_DIR" -DCMAKE_BUILD_TYPE="${CMAKE_BUILD_TYPE:-Release}"
cmake --build "$BUILD_DIR" --config "${CMAKE_BUILD_TYPE:-Release}" -j "$(nproc)"

BUILT="$BUILD_DIR/$LIB_NAME"
[ -f "$BUILT" ] || { echo "ERROR: expected $BUILT, which was not produced." >&2; exit 1; }
echo

# ----------------------------------------------------------------------------------------------
# 3. Verification. A build that fails ANY of these does not get adopted.
# ----------------------------------------------------------------------------------------------
echo "--- verifying ---"
FAILED=0
fail() { echo "  [FAIL] $1"; FAILED=1; }
pass() { echo "  [ok] $1"; }

# 3a. Required exports. These are the entry points Backends/MiniAudio/Native.cs binds to, plus
#     the Vorbis capability probe. A missing one means the managed side breaks at run time.
REQUIRED_SYMBOLS="
sf_has_vorbis
sf_free
sf_allocate_decoder
sf_allocate_decoder_config
sf_allocate_encoder
sf_allocate_encoder_config
sf_allocate_device
sf_allocate_device_config
sf_allocate_context
sf_get_devices
sf_free_device_infos
sf_context_get_backend
ma_decoder_init
ma_decoder_init_memory
ma_decoder_uninit
ma_decoder_read_pcm_frames
ma_decoder_seek_to_pcm_frame
ma_decoder_get_length_in_pcm_frames
ma_encoder_init
ma_encoder_uninit
ma_encoder_write_pcm_frames
ma_context_init
ma_context_uninit
ma_device_init
ma_device_uninit
ma_device_start
ma_device_stop
"
EXPORTS="$(nm -D --defined-only "$BUILT" | awk '{print $3}')"
MISSING=""
for sym in $REQUIRED_SYMBOLS; do
    printf '%s\n' "$EXPORTS" | grep -qx "$sym" || MISSING="$MISSING $sym"
done
if [ -n "$MISSING" ]; then
    fail "missing exports:$MISSING"
else
    pass "all $(printf '%s\n' "$REQUIRED_SYMBOLS" | grep -c .) required symbols exported"
fi

# 3b. Codec coverage. Vorbis is the codec this whole exercise added; FLAC/WAV/MP3 come from
#     miniaudio's built-in dr_* decoders and must not have been switched off by accident.
VORBIS_SYMS="$(printf '%s\n' "$EXPORTS" | grep -c '^ma_stbvorbis_' || true)"
FLAC_SYMS="$(printf '%s\n' "$EXPORTS" | grep -c '^ma_dr_flac_' || true)"
MP3_SYMS="$(printf '%s\n' "$EXPORTS" | grep -c '^ma_dr_mp3_' || true)"
WAV_SYMS="$(printf '%s\n' "$EXPORTS" | grep -c '^ma_dr_wav_' || true)"
[ "$VORBIS_SYMS" -gt 0 ] && pass "Vorbis decoder present ($VORBIS_SYMS ma_stbvorbis_* symbols)" \
                         || fail "no Vorbis decoder in this binary"
[ "$FLAC_SYMS"   -gt 0 ] && pass "FLAC decoder present ($FLAC_SYMS ma_dr_flac_* symbols)" \
                         || fail "no FLAC decoder in this binary"
[ "$MP3_SYMS"    -gt 0 ] && pass "MP3 decoder present ($MP3_SYMS ma_dr_mp3_* symbols)" \
                         || fail "no MP3 decoder in this binary"
[ "$WAV_SYMS"    -gt 0 ] && pass "WAV decoder present ($WAV_SYMS ma_dr_wav_* symbols)" \
                         || fail "no WAV decoder in this binary"

# 3c. Dependencies. Only universal system libraries may be dynamically linked. The audio
#     backends (ALSA, PulseAudio, JACK) are dlopen'd by miniaudio at run time by design, so
#     they must NOT appear here - if one did, the binary would demand that library be
#     installed on every user's machine.
DEPS="$(objdump -p "$BUILT" | awk '/NEEDED/ {print $2}' | sort)"
FORBIDDEN="$(printf '%s\n' "$DEPS" | grep -E 'libasound|libpulse|libjack|libvorbis|libogg|libFLAC' || true)"
if [ -n "$FORBIDDEN" ]; then
    fail "binary dynamically links what it should dlopen or embed: $FORBIDDEN"
else
    pass "dependencies are system-only: $(printf '%s ' $DEPS)"
fi

# 3d. glibc ceiling - the oldest distro this binary can run on. Informational, but recorded.
GLIBC_CEILING="$(objdump -T "$BUILT" | grep -oE 'GLIBC_[0-9]+\.[0-9]+' | sort -V -u | tail -1)"
pass "glibc ceiling: ${GLIBC_CEILING:-none}"

# 3e. The real test: load it and decode an .ogg through the pull-mode path.
echo "  --- decode smoke test ---"
cc -O2 -o /tmp/smoke_test "$TOOLS/smoke_test.c" -ldl
if /tmp/smoke_test "$BUILT" "$REPO/$SMOKE_TEST_OGG" | sed 's/^/    /'; then
    pass "smoke test"
else
    fail "smoke test"
fi

if [ "$FAILED" -ne 0 ]; then
    echo
    echo "VERIFICATION FAILED - nothing was written to output/. Fix the build before adopting."
    exit 1
fi
echo

# ----------------------------------------------------------------------------------------------
# 4. Publish to output/ with a provenance record.
# ----------------------------------------------------------------------------------------------
mkdir -p "$OUT_DIR"
cp "$BUILT" "$OUT_DIR/$LIB_NAME"
SHA="$(sha256sum "$OUT_DIR/$LIB_NAME" | cut -d' ' -f1)"

cat > "$OUT_DIR/build-info.txt" <<EOF
codebrix_miniaudio - build information
==============================================================================
RID            : $TARGET_RID
Built          : $(date -u '+%Y-%m-%d %H:%M:%S UTC')
Built by       : tools/build_native_libraries/build.sh (container build)
Container      : ${CONTAINER_IMAGE:-unknown}
Container OS   : $(grep -m1 PRETTY_NAME /etc/os-release 2>/dev/null | cut -d= -f2- | tr -d '"')
Machine        : $(uname -m)
Compiler       : $(cc --version | head -1)
CMake          : $(cmake --version | head -1)
Build type     : ${CMAKE_BUILD_TYPE:-Release}

Sources (all vendored in-repo, nothing fetched at build time)
------------------------------------------------------------------------------
miniaudio      : $MINIAUDIO_VERSION  (mackron/miniaudio @ $MINIAUDIO_COMMIT)
stb_vorbis     : $STB_VORBIS_VERSION  (nothings/stb @ $STB_VORBIS_COMMIT)
wrapper        : native/miniaudio/library.c + library.h

Result
------------------------------------------------------------------------------
File           : $LIB_NAME
Size           : $(stat -c %s "$OUT_DIR/$LIB_NAME") bytes
SHA256         : $SHA
glibc ceiling  : ${GLIBC_CEILING:-none}
Dynamic deps   : $(printf '%s ' $DEPS)
Codecs         : WAV, MP3, FLAC, Ogg Vorbis
EOF

echo "--- done ---"
echo "  $OUT_DIR/$LIB_NAME"
echo "  sha256 $SHA"
