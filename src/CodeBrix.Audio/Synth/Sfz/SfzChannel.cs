using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.Sfz;

// Per-MIDI-channel playback state: controller values, pitch bend, the sustain pedal, the program,
// which keys are down, and the keyswitch / previous-note state region selection tests against.
//
// The controller array extends past 127 to hold the storable ARIA extended sources: 128 pitch bend is
// derived, 129 channel aftertouch and 130 polyphonic aftertouch arrive as messages, and the per-voice
// sources (131 velocity, 135-137 randoms and alternate, 140/141 key delta) are resolved by the voice.
internal sealed class SfzChannel
{
    private const int ccCount = 160;

    private readonly SfzInstrument instrument;

    // Controllers are stored normalized 0..1 so 7-bit CCs and high-definition set_hd_ccN initial
    // values live in one place. Region range tests convert back to 0..127.
    private readonly float[] ccValues = new float[ccCount];

    private readonly bool[] heldKeys = new bool[128];
    private readonly int[] noteOnVelocities = new int[128];
    private readonly long[] noteOnFrames = new long[128];

    private float pitchBend;
    private int heldKeyCount;
    private int lastKeyswitch;
    private int previousNote;
    private int lastNoteOnKey;
    private int lastNoteOnVelocity;

    internal SfzChannel(SfzInstrument instrument)
    {
        this.instrument = instrument;
        Reset();
    }

    public float PitchBend => pitchBend;
    public int HeldKeyCount => heldKeyCount;
    public int LastKeyswitch => lastKeyswitch;
    public int PreviousNote => previousNote;

    // The MIDI program selected by the last program change; 0 before any - what loprog/hiprog test.
    public int Program { get; set; }

    // The key and velocity of the last COMPLETED note-on. Region matching and voice starts run before
    // the current note's KeyDown, so at that moment these are the previous note's values - what
    // sw_vel=previous checks and what the key-delta sources (CC 140/141) measure from.
    public int LastNoteOnKey => lastNoteOnKey;
    public int LastNoteOnVelocity => lastNoteOnVelocity;

    // Whether the sustain pedal is down, per the region's sustain_cc (64 unless remapped).
    public bool IsSustainDown(int sustainCc) => GetCc(sustainCc) >= 0.5f;

    public void Reset()
    {
        Array.Clear(ccValues, 0, ccValues.Length);
        Array.Clear(heldKeys, 0, heldKeys.Length);
        Array.Clear(noteOnVelocities, 0, noteOnVelocities.Length);
        Array.Clear(noteOnFrames, 0, noteOnFrames.Length);

        pitchBend = 0f;
        heldKeyCount = 0;
        lastKeyswitch = -1;
        previousNote = -1;
        Program = 0;
        lastNoteOnKey = -1;
        lastNoteOnVelocity = 0;

        foreach (KeyValuePair<int, float> pair in instrument.InitialControllers)
        {
            ccValues[pair.Key] = pair.Value;
        }
    }

    // MIDI Reset All Controllers: controller and bend state return to the instrument's defaults, but
    // which keys are physically down is a fact about the player's hands, not controller state.
    public void ResetControllers()
    {
        Array.Clear(ccValues, 0, ccValues.Length);
        pitchBend = 0f;

        foreach (KeyValuePair<int, float> pair in instrument.InitialControllers)
        {
            ccValues[pair.Key] = pair.Value;
        }
    }

    public float GetCc(int cc) => 0 <= cc && cc < ccCount ? ccValues[cc] : 0f;

    public int GetCcMidiValue(int cc) => (int)MathF.Round(GetCc(cc) * 127f);

    public void SetCc(int cc, float normalizedValue)
    {
        if (0 <= cc && cc < ccCount)
        {
            ccValues[cc] = Math.Clamp(normalizedValue, 0f, 1f);
        }
    }

    // data1/data2 are the 14-bit pitch bend halves; the result is normalized to -1..1.
    public void SetPitchBend(int data1, int data2)
    {
        pitchBend = (1f / 8192f) * (((data2 << 7) | data1) - 8192);
    }

    public bool IsKeyHeld(int key) => 0 <= key && key <= 127 && heldKeys[key];

    public void KeyDown(int key, int velocity, long currentFrame)
    {
        if (key < 0 || key > 127)
        {
            return;
        }

        if (!heldKeys[key])
        {
            heldKeyCount++;
        }

        heldKeys[key] = true;
        noteOnVelocities[key] = velocity;
        noteOnFrames[key] = currentFrame;

        lastNoteOnKey = key;
        lastNoteOnVelocity = velocity;
    }

    public void KeyUp(int key)
    {
        if (key < 0 || key > 127)
        {
            return;
        }

        if (heldKeys[key])
        {
            heldKeyCount--;
        }

        heldKeys[key] = false;
        previousNote = key;
    }

    public int NoteOnVelocity(int key) => 0 <= key && key <= 127 ? noteOnVelocities[key] : 0;

    public long NoteOnFrame(int key) => 0 <= key && key <= 127 ? noteOnFrames[key] : 0;

    public void SetLastKeyswitch(int key) => lastKeyswitch = key;
}
