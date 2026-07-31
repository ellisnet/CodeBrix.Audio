#!/usr/bin/env bash
# ==============================================================================================
# build.sh - build the Linux codebrix_miniaudio native libraries, portably
# ==============================================================================================
#
#   ./build.sh x64            build linux-x64
#   ./build.sh arm64          build linux-arm64
#   ./build.sh riscv64        build linux-riscv64
#   ./build.sh all            build all three
#
# Options (environment variables):
#   CONTAINER_ENGINE=podman|docker   force an engine (default: podman, then docker)
#   PIN_DIGEST=1                     pull images by the digest recorded in pins.env
#   KEEP_IMAGE=1                     do not remove the pulled image afterwards (default: keep)
#
# WHY A CONTAINER, EVEN FOR X64
#   glibc symbol versioning is forward-only: a binary built against glibc 2.41 (what a current
#   Debian/LMDE desktop has) refuses to load on anything older, so building on the workstation
#   would quietly restrict the library to the newest distros. The manylinux images fix the
#   baseline at glibc 2.28 (2.39 for riscv64, where nothing older exists), which is why every
#   Linux RID - x64 included - is built this way.
#
# THIS SCRIPT NEVER INSTALLS ANYTHING ON YOUR MACHINE. If a prerequisite is missing it says
# what it is, prints the command to install it, and stops. See README.txt for the full list.
# ==============================================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
REPO_ROOT="$(cd "$SCRIPT_DIR/../.." && pwd)"

# shellcheck source=pins.env
. "$SCRIPT_DIR/pins.env"

# ----------------------------------------------------------------------------------------------
# Prerequisites
# ----------------------------------------------------------------------------------------------
pick_engine() {
    if [ -n "${CONTAINER_ENGINE:-}" ]; then
        command -v "$CONTAINER_ENGINE" > /dev/null 2>&1 || {
            echo "ERROR: CONTAINER_ENGINE=$CONTAINER_ENGINE is not on PATH." >&2; exit 1; }
        echo "$CONTAINER_ENGINE"
        return
    fi
    if command -v podman > /dev/null 2>&1; then echo podman; return; fi
    if command -v docker > /dev/null 2>&1; then echo docker; return; fi
    cat >&2 <<'EOF'
ERROR: neither podman nor docker is installed.

  This script does not install anything. Install a container engine yourself:

    Debian-based Linux:  sudo apt install podman
    Fedora/RHEL:         sudo dnf install podman

  Then re-run. See README.txt (PREREQUISITES) for the complete list.
EOF
    exit 1
}

ENGINE="$(pick_engine)"

# The .ogg the in-container smoke test decodes. Generated, not third-party - see
# tools/make_test_fixtures/. Without it the build cannot be verified, so this is fatal.
if [ ! -f "$REPO_ROOT/$SMOKE_TEST_OGG" ]; then
    cat >&2 <<EOF
ERROR: the smoke-test input is missing: $SMOKE_TEST_OGG

  Regenerate the audio test fixtures first:

    cd $REPO_ROOT/tools/make_test_fixtures && ./make_fixtures.sh

  (That script needs ffmpeg; it does not install anything either.)
EOF
    exit 1
fi

# ----------------------------------------------------------------------------------------------
# Architecture table
# ----------------------------------------------------------------------------------------------
host_machine="$(uname -m)"

arch_rid()      { case "$1" in x64) echo linux-x64;; arm64) echo linux-arm64;; riscv64) echo linux-riscv64;; esac; }
arch_platform() { case "$1" in x64) echo linux/amd64;; arm64) echo linux/arm64;; riscv64) echo linux/riscv64;; esac; }
arch_native()   { case "$1" in x64) echo x86_64;; arm64) echo aarch64;; riscv64) echo riscv64;; esac; }
arch_image()    {
    case "$1" in
        x64)     [ "${PIN_DIGEST:-0}" = "1" ] && [ -n "$IMAGE_X64_DIGEST" ]     && { echo "${IMAGE_X64%%:*}@$IMAGE_X64_DIGEST"; return; };     echo "$IMAGE_X64" ;;
        arm64)   [ "${PIN_DIGEST:-0}" = "1" ] && [ -n "$IMAGE_ARM64_DIGEST" ]   && { echo "${IMAGE_ARM64%%:*}@$IMAGE_ARM64_DIGEST"; return; };   echo "$IMAGE_ARM64" ;;
        riscv64) [ "${PIN_DIGEST:-0}" = "1" ] && [ -n "$IMAGE_RISCV64_DIGEST" ] && { echo "${IMAGE_RISCV64%%:*}@$IMAGE_RISCV64_DIGEST"; return; }; echo "$IMAGE_RISCV64" ;;
    esac
}

check_emulation() {
    local arch="$1" want
    want="$(arch_native "$arch")"
    [ "$want" = "$host_machine" ] && return 0

    # Non-native architecture: the kernel needs a binfmt_misc handler registered for it,
    # otherwise the container starts and every command inside it dies with "exec format error".
    if [ -d /proc/sys/fs/binfmt_misc ] && ls /proc/sys/fs/binfmt_misc 2>/dev/null | grep -qi "qemu-$want"; then
        echo "  emulation: qemu-$want binfmt handler registered"
        return 0
    fi
    cat >&2 <<EOF

ERROR: building $arch on a $host_machine host needs qemu user-mode emulation, and no
       binfmt handler for qemu-$want is registered.

  Set it up ONE of these ways (neither is done for you):

    1. Install the static qemu binaries and their binfmt registrations:
         sudo apt install qemu-user-static binfmt-support
       (On Debian-based systems this registers the handlers automatically.)

    2. Or register the handlers from a container, without installing anything permanently:
         sudo $ENGINE run --rm --privileged docker.io/multiarch/qemu-user-static --reset -p yes

  Verify with:  ls /proc/sys/fs/binfmt_misc | grep qemu

EOF
    exit 1
}

build_arch() {
    local arch="$1" rid image platform
    rid="$(arch_rid "$arch")"
    image="$(arch_image "$arch")"
    platform="$(arch_platform "$arch")"

    echo
    echo "=============================================================================="
    echo " BUILD $rid"
    echo "=============================================================================="
    echo "  image    : $image"
    echo "  platform : $platform"
    echo "  host     : $host_machine"
    check_emulation "$arch"

    echo "--- pulling image (cached after the first run) ---"
    "$ENGINE" pull --platform "$platform" "$image"

    local digest
    digest="$("$ENGINE" image inspect "$image" --format '{{index .RepoDigests 0}}' 2>/dev/null || echo "$image")"
    echo "  digest   : $digest"

    "$ENGINE" run --rm \
        --platform "$platform" \
        -v "$REPO_ROOT":/repo:Z \
        -e TARGET_RID="$rid" \
        -e CONTAINER_IMAGE="$digest" \
        -e MINIAUDIO_VERSION="$MINIAUDIO_VERSION" \
        -e MINIAUDIO_COMMIT="$MINIAUDIO_COMMIT" \
        -e STB_VORBIS_VERSION="$STB_VORBIS_VERSION" \
        -e STB_VORBIS_COMMIT="$STB_VORBIS_COMMIT" \
        -e CMAKE_BUILD_TYPE="$CMAKE_BUILD_TYPE" \
        -e SMOKE_TEST_OGG="$SMOKE_TEST_OGG" \
        "$image" \
        bash /repo/tools/build_native_libraries/container_build.sh
}

# ----------------------------------------------------------------------------------------------
# Main
# ----------------------------------------------------------------------------------------------
if [ $# -lt 1 ]; then
    echo "usage: $0 x64|arm64|riscv64|all" >&2
    exit 2
fi

echo "container engine : $ENGINE"
echo "repository       : $REPO_ROOT"

case "$1" in
    x64|arm64|riscv64) build_arch "$1" ;;
    all)               for a in x64 arm64 riscv64; do build_arch "$a"; done ;;
    *) echo "usage: $0 x64|arm64|riscv64|all" >&2; exit 2 ;;
esac

echo
echo "=============================================================================="
echo " Outputs are in tools/build_native_libraries/output/<rid>/"
echo " To adopt them into the package, follow ADOPTING A BUILT BINARY in README.txt."
echo "=============================================================================="
