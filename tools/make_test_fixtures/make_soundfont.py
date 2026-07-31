#!/usr/bin/env python3
# ==============================================================================================
# make_soundfont.py - generate the checked-in synthetic SoundFont test fixture
# ==============================================================================================
#
# WHAT THIS IS
#   tests/Assets/soundfont/codebrix-test.sf2 is NOT a third-party SoundFont. It is a minimal,
#   fully synthetic SF2 built here from sine tones, so the CodeBrix.Audio.Synth tests have a
#   real SoundFont to parse and render without the repository shipping licensed sample content.
#
#   Real GM SoundFonts are tens of megabytes and are variously licensed; none can be committed
#   to an MIT repository. The upstream MeltySynth suite tested against TimGM6mb (GPL-2), which
#   is why the five tests that compare against its parameter dumps did not come across with the
#   port. Everything else needs only *a* valid SoundFont, and this is it.
#
#   Deliberately shaped for the tests that use it:
#     * two instruments, so preset -> instrument -> region traversal has more than one path;
#     * a looping instrument and a non-looping one, so Oscillator covers both LoopMode branches;
#     * two regions per instrument split across the key range, so RegionPair sees real zones;
#     * a global instrument zone, so global-then-local generator precedence is exercised;
#     * four distinct samples at different root keys, so region -> sample resolution is real.
#
# USAGE
#   cd tools/make_test_fixtures
#   ./make_soundfont.py                  # writes ../../tests/Assets/soundfont/codebrix-test.sf2
#   OUT_DIR=/tmp/fixtures ./make_soundfont.py
#
# PREREQUISITES (installed by YOU - this script never installs anything)
#   python3 (standard library only; no packages required)
#
# NOTE ON REPRODUCIBILITY
#   Output is byte-identical on every run: the waveforms are computed from fixed formulas with
#   no randomness, and no timestamps or generator versions are written into the file.
# ==============================================================================================

import math
import os
import struct
import sys

# SF2 generator operators used here (SoundFont 2.04 spec, section 8.1.2).
GEN_INSTRUMENT = 41
GEN_KEY_RANGE = 43
GEN_VELOCITY_RANGE = 44
GEN_SAMPLE_ID = 53
GEN_SAMPLE_MODES = 54
GEN_OVERRIDING_ROOT_KEY = 58

# SF2 sample link types (spec section 7.10).
SAMPLE_TYPE_MONO = 1

# The spec requires at least 46 zero sample points between samples in the smpl chunk.
INTER_SAMPLE_GAP = 46

SAMPLE_RATE = 22050


def fourcc(tag):
    assert len(tag) == 4
    return tag.encode("ascii")


def chunk(tag, payload):
    """A RIFF sub-chunk: id, little-endian size, payload, pad byte to even length."""
    data = fourcc(tag) + struct.pack("<I", len(payload)) + payload
    if len(payload) % 2:
        data += b"\x00"
    return data


def list_chunk(list_type, payload):
    return chunk("LIST", fourcc(list_type) + payload)


def zstr(text, size):
    """Fixed-width, zero-padded ASCII name field (20 bytes for phdr/inst/shdr)."""
    raw = text.encode("ascii")[: size - 1]
    return raw + b"\x00" * (size - len(raw))


def sine(freq, seconds, amplitude=0.5):
    """A whole number of cycles, so the loop points join without a discontinuity."""
    cycles = max(1, round(freq * seconds))
    count = int(round(cycles * SAMPLE_RATE / freq))
    return [
        int(amplitude * 32767 * math.sin(2 * math.pi * i * freq / SAMPLE_RATE))
        for i in range(count)
    ]


# ----------------------------------------------------------------------------------------------
# Sample data
# ----------------------------------------------------------------------------------------------

def build_samples():
    """Four sine tones. Returns (smpl bytes, list of header dicts)."""
    specs = [
        ("Tone A3 220Hz", 220.0, 57),
        ("Tone A4 440Hz", 440.0, 69),
        ("Tone A5 880Hz", 880.0, 81),
        ("Tone E5 659Hz", 659.255, 76),
    ]

    pcm = []
    headers = []
    for name, freq, root_key in specs:
        start = len(pcm)
        body = sine(freq, 0.25)
        pcm.extend(body)
        end = len(pcm)

        # Loop over the interior, one period in from each end, so loop-start and loop-end are
        # genuinely distinct from the sample bounds and the looping tests have something to bite.
        period = int(round(SAMPLE_RATE / freq))
        headers.append(
            {
                "name": name,
                "start": start,
                "end": end,
                "start_loop": start + period,
                "end_loop": end - period,
                "sample_rate": SAMPLE_RATE,
                "original_pitch": root_key,
                "pitch_correction": 0,
            }
        )
        pcm.extend([0] * INTER_SAMPLE_GAP)

    # SoundFont.CheckSamples() treats the last four sample points as out-of-range guard space.
    pcm.extend([0] * 4)

    return struct.pack("<%dh" % len(pcm), *pcm), headers


# ----------------------------------------------------------------------------------------------
# pdta records
# ----------------------------------------------------------------------------------------------

def phdr(presets):
    out = b""
    for name, preset_no, bank, bag_index in presets:
        out += zstr(name, 20) + struct.pack("<HHHIII", preset_no, bank, bag_index, 0, 0, 0)
    return out


def bag(entries):
    """pbag / ibag: (generator index, modulator index) pairs."""
    return b"".join(struct.pack("<HH", g, m) for g, m in entries)


def gen(entries):
    """pgen / igen: (operator, amount) pairs. Amount is written as raw 16 bits."""
    return b"".join(struct.pack("<Hh", op, amount) for op, amount in entries)


def mod_terminal():
    """pmod / imod: this fixture defines no modulators, so only the terminal record."""
    return struct.pack("<HHhHH", 0, 0, 0, 0, 0)


def inst(instruments):
    return b"".join(zstr(name, 20) + struct.pack("<H", bag_index) for name, bag_index in instruments)


def shdr(headers):
    out = b""
    for h in headers:
        out += zstr(h["name"], 20) + struct.pack(
            "<IIIIIBbHH",
            h["start"],
            h["end"],
            h["start_loop"],
            h["end_loop"],
            h["sample_rate"],
            h["original_pitch"],
            h["pitch_correction"],
            0,
            SAMPLE_TYPE_MONO,
        )
    return out


def key_range(lo, hi):
    return (hi << 8) | lo


def build():
    smpl, headers = build_samples()

    # -- instruments -----------------------------------------------------------------------
    # Instrument 0 "Looping Tones": a global zone plus two key-split looping regions.
    # Instrument 1 "OneShot Tones": two non-looping regions, no global zone.
    #
    # igen is a flat list; ibag indexes into it. Each region's generator run must end with
    # sampleID, which is what binds the zone to a sample header.
    igen_entries = []
    ibag_entries = []

    def add_zone(gens):
        ibag_entries.append((len(igen_entries), 0))
        igen_entries.extend(gens)

    # Instrument 0, zone 0: global. Velocity range only - applies to every following zone.
    add_zone([(GEN_VELOCITY_RANGE, key_range(0, 127))])
    # Instrument 0, zone 1: lower half, looping, sample 0.
    add_zone([
        (GEN_KEY_RANGE, key_range(0, 63)),
        (GEN_OVERRIDING_ROOT_KEY, 57),
        (GEN_SAMPLE_MODES, 1),          # LoopMode.Continuous
        (GEN_SAMPLE_ID, 0),
    ])
    # Instrument 0, zone 2: upper half, looping, sample 1.
    add_zone([
        (GEN_KEY_RANGE, key_range(64, 127)),
        (GEN_OVERRIDING_ROOT_KEY, 69),
        (GEN_SAMPLE_MODES, 1),
        (GEN_SAMPLE_ID, 1),
    ])

    instrument1_bag_index = len(ibag_entries)

    # Instrument 1, zone 0: lower half, no loop, sample 2.
    add_zone([
        (GEN_KEY_RANGE, key_range(0, 63)),
        (GEN_OVERRIDING_ROOT_KEY, 81),
        (GEN_SAMPLE_MODES, 0),          # LoopMode.NoLoop
        (GEN_SAMPLE_ID, 2),
    ])
    # Instrument 1, zone 1: upper half, no loop, sample 3.
    add_zone([
        (GEN_KEY_RANGE, key_range(64, 127)),
        (GEN_OVERRIDING_ROOT_KEY, 76),
        (GEN_SAMPLE_MODES, 0),
        (GEN_SAMPLE_ID, 3),
    ])

    ibag_terminal = len(igen_entries)
    ibag_entries.append((ibag_terminal, 0))
    igen_entries.append((0, 0))          # terminal igen record

    instruments = [
        ("Looping Tones", 0),
        ("OneShot Tones", instrument1_bag_index),
        ("EOI", len(ibag_entries) - 1),  # terminal inst record
    ]

    # -- presets ---------------------------------------------------------------------------
    pgen_entries = []
    pbag_entries = []

    def add_preset_zone(gens):
        pbag_entries.append((len(pgen_entries), 0))
        pgen_entries.extend(gens)

    add_preset_zone([(GEN_INSTRUMENT, 0)])   # preset 0 -> instrument 0
    add_preset_zone([(GEN_INSTRUMENT, 1)])   # preset 1 -> instrument 1

    pbag_entries.append((len(pgen_entries), 0))
    pgen_entries.append((0, 0))              # terminal pgen record

    presets = [
        ("Looping Tone Preset", 0, 0, 0),
        ("OneShot Tone Preset", 1, 0, 1),
        ("EOP", 255, 255, len(pbag_entries) - 1),   # terminal phdr record
    ]

    # -- assemble --------------------------------------------------------------------------
    info = (
        chunk("ifil", struct.pack("<HH", 2, 1))
        + chunk("isng", zstr("EMU8000", 8))
        + chunk("INAM", zstr("CodeBrix.Audio Test SoundFont", 32))
    )

    sdta = chunk("smpl", smpl)

    pdta = (
        chunk("phdr", phdr(presets))
        + chunk("pbag", bag(pbag_entries))
        + chunk("pmod", mod_terminal())
        + chunk("pgen", gen(pgen_entries))
        + chunk("inst", inst(instruments))
        + chunk("ibag", bag(ibag_entries))
        + chunk("imod", mod_terminal())
        + chunk("igen", gen(igen_entries))
        + chunk("shdr", shdr(headers + [{
            "name": "EOS",
            "start": 0, "end": 0, "start_loop": 0, "end_loop": 0,
            "sample_rate": 0, "original_pitch": 0, "pitch_correction": 0,
        }]))
    )

    body = (
        fourcc("sfbk")
        + list_chunk("INFO", info)
        + list_chunk("sdta", sdta)
        + list_chunk("pdta", pdta)
    )
    return fourcc("RIFF") + struct.pack("<I", len(body)) + body


def main():
    script_dir = os.path.dirname(os.path.abspath(__file__))
    out_dir = os.environ.get(
        "OUT_DIR", os.path.join(script_dir, "..", "..", "tests", "Assets", "soundfont")
    )
    out_dir = os.path.abspath(out_dir)
    os.makedirs(out_dir, exist_ok=True)

    path = os.path.join(out_dir, "codebrix-test.sf2")
    data = build()
    with open(path, "wb") as f:
        f.write(data)

    print(f"wrote {path} ({len(data):,} bytes)")
    return 0


if __name__ == "__main__":
    sys.exit(main())
