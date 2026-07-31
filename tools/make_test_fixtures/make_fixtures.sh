#!/usr/bin/env bash
# ==============================================================================================
# make_fixtures.sh - regenerate the checked-in audio test fixtures
# ==============================================================================================
#
# WHAT THIS IS
#   The .ogg / .flac / .wav files under tests/Assets/audio/ are NOT third-party audio. They are
#   synthesized here - sine tones, sweeps, noise and silence - and encoded with ffmpeg. This
#   script is what produced them, so a year from now they can be regenerated identically
#   instead of being mystery binaries in the repo.
#
#   Every fixture is deliberately chosen to exercise a decoder path:
#     * the .ogg files cover mono/stereo, three sample rates, and a long sweep for seek tests;
#     * the .flac files cover 16- and 24-bit, mono and stereo, all four stereo decorrelation
#       modes, constant / fixed-predictor / LPC / verbatim subframes, and a short final block;
#     * every .flac ships with the exact .wav it was encoded from, because FLAC is lossless -
#       a decoder is correct only if it reproduces that PCM sample for sample.
#
# USAGE
#   cd tools/make_test_fixtures
#   ./make_fixtures.sh              # regenerate everything into ../../tests/Assets/audio
#   OUT_DIR=/tmp/fixtures ./make_fixtures.sh
#
# PREREQUISITES (installed by YOU - this script never installs anything)
#   ffmpeg, built with the libvorbis encoder and the native flac encoder.
#     Debian-based Linux:  sudo apt install ffmpeg
#     macOS (Homebrew):    brew install ffmpeg
#     Windows (winget):    winget install Gyan.FFmpeg
#   Verify with:           ffmpeg -hide_banner -encoders | grep -E 'libvorbis|flac'
#
# NOTE ON REPRODUCIBILITY
#   Byte-identical output requires the same ffmpeg/libvorbis build; the encoder writes its
#   version into the Ogg vendor string, and encoder tuning changes between releases. The
#   AUDIO-FIXTURES.txt manifest records the versions used for the committed files. Fixtures
#   regenerated with a different ffmpeg remain valid test inputs - they are simply not
#   byte-identical to the previous ones.
# ==============================================================================================

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
OUT_DIR="${OUT_DIR:-$SCRIPT_DIR/../../tests/Assets/audio}"
MANIFEST="$OUT_DIR/AUDIO-FIXTURES.txt"

# ---------------------------------------------------------------------------------------------
# Prerequisite check. Never install - report and stop.
# ---------------------------------------------------------------------------------------------
if ! command -v ffmpeg > /dev/null 2>&1; then
    cat >&2 <<'EOF'
ERROR: ffmpeg was not found on PATH.

  This script does not install anything. Install ffmpeg yourself, then re-run:

    Debian-based Linux:  sudo apt install ffmpeg
    macOS (Homebrew):    brew install ffmpeg
    Windows (winget):    winget install Gyan.FFmpeg
EOF
    exit 1
fi

# Read the encoder list once. (Piping it into `grep -q` would make grep exit early, kill
# ffmpeg with SIGPIPE, and - under `set -o pipefail` - fail the check even on a match.)
FFMPEG_ENCODERS="$(ffmpeg -hide_banner -encoders 2>/dev/null || true)"

for enc in libvorbis flac; do
    if ! printf '%s\n' "$FFMPEG_ENCODERS" | grep -E "^ [A-Z.]+ ${enc}( |\$)" > /dev/null; then
        echo "ERROR: this ffmpeg has no '${enc}' encoder. Install a full ffmpeg build." >&2
        exit 1
    fi
done

FFMPEG_VERSION="$(ffmpeg -hide_banner -version | head -1)"

mkdir -p "$OUT_DIR"
FF="ffmpeg -hide_banner -loglevel error -y"

echo "Writing fixtures to: $OUT_DIR"
echo "Using: $FFMPEG_VERSION"
echo

# ---------------------------------------------------------------------------------------------
# Source generators.
#   tone   <out.wav> <freq> <rate> <channels> <seconds> <pcm_fmt>
#   sweep  <out.wav> <rate> <channels> <seconds>
#   noise  <out.wav> <rate> <channels> <seconds>
#   silent <out.wav> <rate> <channels> <seconds>
# A stereo tone puts a different frequency in each channel so channel swaps cannot hide.
#
# NOTE ON LEVEL: ffmpeg's `sine` source emits at roughly -18 dBFS (amplitude ~0.125), not full
# scale. `volume=4` brings that to about -6 dBFS (amplitude ~0.5), which is what the tests
# assert against and what keeps enough bits in play for the lossless FLAC comparisons.
# ---------------------------------------------------------------------------------------------
tone() {
    local out="$1" freq="$2" rate="$3" ch="$4" secs="$5" fmt="$6"
    if [ "$ch" = "1" ]; then
        $FF -f lavfi -i "sine=frequency=${freq}:sample_rate=${rate}:duration=${secs}" \
            -af "volume=4" -c:a "$fmt" "$out"
    else
        local freq2=$((freq * 3 / 2))
        $FF -f lavfi -i "sine=frequency=${freq}:sample_rate=${rate}:duration=${secs}" \
            -f lavfi -i "sine=frequency=${freq2}:sample_rate=${rate}:duration=${secs}" \
            -filter_complex "[0:a][1:a]amerge=inputs=2,volume=4[a]" -map "[a]" -c:a "$fmt" "$out"
    fi
}

sweep() {
    local out="$1" rate="$2" ch="$3" secs="$4"
    local layout="mono"; [ "$ch" = "2" ] && layout="stereo"
    $FF -f lavfi -i "sine=frequency=200:beep_factor=0:sample_rate=${rate}:duration=${secs}" \
        -af "aeval=val(0)*0.5,asetrate=${rate}" -ac "$ch" -c:a pcm_s16le "$out.tmp.wav"
    # A linear sweep makes any playback position identifiable from the audio itself, which is
    # what the seek tests rely on.
    $FF -f lavfi -i "aevalsrc=0.5*sin(2*PI*(200+1800*t/${secs})*t):s=${rate}:d=${secs}:c=${layout}" \
        -c:a pcm_s16le "$out"
    rm -f "$out.tmp.wav"
}

noise() {
    local out="$1" rate="$2" ch="$3" secs="$4"
    # Fixed seed: the "random" fixture must be the same random every time.
    $FF -f lavfi -i "anoisesrc=sample_rate=${rate}:duration=${secs}:amplitude=0.6:seed=20260730" \
        -ac "$ch" -c:a pcm_s16le "$out"
}

silent() {
    local out="$1" rate="$2" ch="$3" secs="$4"
    local layout="mono"; [ "$ch" = "2" ] && layout="stereo"
    $FF -f lavfi -i "anullsrc=sample_rate=${rate}:channel_layout=${layout}" -t "$secs" \
        -c:a pcm_s16le "$out"
}

# ---------------------------------------------------------------------------------------------
# 1. OGG / VORBIS fixtures
# ---------------------------------------------------------------------------------------------
echo "--- Ogg Vorbis ---"

# Mono 22.05 kHz - the low-rate sound-effect shape found in game asset packs.
tone "$OUT_DIR/vorbis-tone-mono-22050.wav" 440 22050 1 0.25 pcm_s16le
$FF -i "$OUT_DIR/vorbis-tone-mono-22050.wav" -c:a libvorbis -qscale:a 5 \
    "$OUT_DIR/vorbis-tone-mono-22050.ogg"
rm -f "$OUT_DIR/vorbis-tone-mono-22050.wav"
echo "  vorbis-tone-mono-22050.ogg"

# Stereo 44.1 kHz - the everyday case, and the fixture the native build harness decodes.
tone "$OUT_DIR/vorbis-tone-stereo-44100.wav" 440 44100 2 0.25 pcm_s16le
$FF -i "$OUT_DIR/vorbis-tone-stereo-44100.wav" -c:a libvorbis -qscale:a 5 \
    "$OUT_DIR/vorbis-tone-stereo-44100.ogg"
rm -f "$OUT_DIR/vorbis-tone-stereo-44100.wav"
echo "  vorbis-tone-stereo-44100.ogg"

# Stereo 48 kHz, 2 seconds, linear sweep 200 Hz -> 2 kHz. Long enough to seek around in, and
# its instantaneous frequency identifies the position, so a seek can be checked from the audio.
sweep "$OUT_DIR/vorbis-sweep-stereo-48000.wav" 48000 2 2.0
$FF -i "$OUT_DIR/vorbis-sweep-stereo-48000.wav" -c:a libvorbis -qscale:a 5 \
    "$OUT_DIR/vorbis-sweep-stereo-48000.ogg"
rm -f "$OUT_DIR/vorbis-sweep-stereo-48000.wav"
echo "  vorbis-sweep-stereo-48000.ogg"

# Truncated mid-stream: decoders must fail cleanly rather than hang or read past the end.
head -c 3000 "$OUT_DIR/vorbis-tone-stereo-44100.ogg" > "$OUT_DIR/vorbis-truncated.ogg"
echo "  vorbis-truncated.ogg"

# ---------------------------------------------------------------------------------------------
# 2. FLAC fixtures - each with the .wav it was encoded from (lossless: they must match exactly)
# ---------------------------------------------------------------------------------------------
echo "--- FLAC ---"

# flac_case <name> <source-kind> <rate> <channels> <seconds> <pcm_fmt> [extra ffmpeg flac args...]
flac_case() {
    local name="$1" kind="$2" rate="$3" ch="$4" secs="$5" fmt="$6"; shift 6
    local wav="$OUT_DIR/${name}.wav"
    local flac="$OUT_DIR/${name}.flac"

    case "$kind" in
        tone)   tone   "$wav" 440 "$rate" "$ch" "$secs" "$fmt" ;;
        noise)  noise  "$wav" "$rate" "$ch" "$secs" ;;
        silent) silent "$wav" "$rate" "$ch" "$secs" ;;
        *) echo "unknown source kind: $kind" >&2; exit 1 ;;
    esac

    $FF -i "$wav" -c:a flac "$@" "$flac"
    echo "  ${name}.flac (+ .wav reference)"
}

# Fixed predictors only, independent channels: -compression_level 0 disables the LPC search.
flac_case flac-tone-mono-16bit-22050 tone 22050 1 0.25 pcm_s16le \
    -compression_level 0 -ch_mode indep

# Full LPC search with mid/side stereo - the ordinary encoder output most files in the wild use.
flac_case flac-tone-stereo-16bit-44100-midside tone 44100 2 0.25 pcm_s16le \
    -compression_level 8 -ch_mode mid_side

# The two asymmetric decorrelation modes, which a decoder is easy to get subtly wrong.
flac_case flac-tone-stereo-16bit-44100-leftside tone 44100 2 0.25 pcm_s16le \
    -compression_level 8 -ch_mode left_side
flac_case flac-tone-stereo-16bit-44100-rightside tone 44100 2 0.25 pcm_s16le \
    -compression_level 8 -ch_mode right_side

# Noise resists prediction, so this is where VERBATIM subframes and wide Rice parameters show up.
flac_case flac-noise-stereo-16bit-44100 noise 44100 2 0.25 pcm_s16le \
    -compression_level 0 -ch_mode indep

# Digital silence encodes as CONSTANT subframes.
flac_case flac-silence-stereo-16bit-44100 silent 44100 2 0.25 pcm_s16le \
    -compression_level 5

# 24-bit: exercises the wider sample path end to end.
flac_case flac-tone-stereo-24bit-48000 tone 48000 2 0.25 pcm_s24le \
    -compression_level 5 -sample_fmt s32

# A duration that is not a whole number of blocks, so the final frame is a short block.
flac_case flac-tone-stereo-16bit-44100-oddlength tone 44100 2 0.333 pcm_s16le \
    -compression_level 5

# Truncated mid-frame: must fail cleanly.
head -c 4000 "$OUT_DIR/flac-tone-stereo-16bit-44100-midside.flac" > "$OUT_DIR/flac-truncated.flac"
echo "  flac-truncated.flac"

# ---------------------------------------------------------------------------------------------
# 3. Manifest
# ---------------------------------------------------------------------------------------------
{
    echo "=============================================================================="
    echo "tests/Assets/audio - generated audio test fixtures"
    echo "=============================================================================="
    echo
    echo "These files are NOT third-party audio. Every one is synthesized (sine tones,"
    echo "a frequency sweep, seeded noise, digital silence) and encoded locally by"
    echo "tools/make_test_fixtures/make_fixtures.sh. Re-run that script to regenerate them."
    echo
    echo "Generated by : tools/make_test_fixtures/make_fixtures.sh"
    echo "Encoder      : $FFMPEG_VERSION"
    echo
    echo "WHAT EACH FIXTURE IS FOR"
    echo "------------------------------------------------------------------------------"
    echo "  vorbis-tone-mono-22050.ogg          low-rate mono, the game-SFX shape"
    echo "  vorbis-tone-stereo-44100.ogg        everyday stereo; also the native build"
    echo "                                      harness's decode smoke test input"
    echo "  vorbis-sweep-stereo-48000.ogg       2 s sweep 200 Hz -> 2 kHz; the instantaneous"
    echo "                                      frequency identifies the position, so seeks"
    echo "                                      can be verified from the audio itself"
    echo "  vorbis-truncated.ogg                truncated stream; must fail cleanly"
    echo "  flac-*.flac                         one per decoder path (see the table below);"
    echo "                                      each ships with the .wav it was encoded from,"
    echo "                                      and a correct decoder reproduces that PCM"
    echo "                                      sample for sample, because FLAC is lossless"
    echo "  flac-truncated.flac                 truncated stream; must fail cleanly"
    echo
    echo "  flac-tone-mono-16bit-22050          fixed predictors only, independent channels"
    echo "  flac-tone-stereo-16bit-44100-*side  the four stereo decorrelation modes"
    echo "  flac-noise-stereo-16bit-44100       verbatim subframes / wide Rice parameters"
    echo "  flac-silence-stereo-16bit-44100     constant subframes"
    echo "  flac-tone-stereo-24bit-48000        24-bit sample path"
    echo "  flac-*-oddlength                    short final block"
    echo
    echo "NOTE: ffmpeg does not write a SEEKTABLE metadata block, so these fixtures exercise"
    echo "the reader's frame-search seek path. The SEEKTABLE path is covered by a test that"
    echo "splices a synthetic SEEKTABLE into one of these files at run time."
    echo
    echo "SHA256"
    echo "------------------------------------------------------------------------------"
} > "$MANIFEST"

(cd "$OUT_DIR" && sha256sum ./*.ogg ./*.flac ./*.wav | sed 's|\./||') >> "$MANIFEST"

echo
echo "Manifest: $MANIFEST"
echo "Total size: $(du -sh "$OUT_DIR" | cut -f1)"
