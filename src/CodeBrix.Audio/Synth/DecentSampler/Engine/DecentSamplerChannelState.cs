using System;

namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

// Per-MIDI-channel playback state for the Decent Sampler engine: every controller value, pitch bend
// and its range, the sustain pedal, channel and polyphonic pressure, which keys are down, and when and
// how hard each was struck.
//
// The controller values are what loCC/hiCC zone filters test and what onLoCC/onHiCC triggers watch:
// this class holds each channel's RAW state, with no MPE zone combining, which is what a filter
// written for ordinary MIDI expects to see.
//
// MPE IS SOMEWHERE ELSE. CodeBrix.Audio.Synth.Mpe.MpeChannelState is the authority on the pitch a
// voice bends to, on the timbre and pressure the modulators read, and on the zone that ties a member
// channel to its master; the synthesizer feeds both from the same messages. The bend kept here is the
// plain per-channel wheel with no range from RPN 0 and no master added, and nothing in the engine
// reads it - it stays because a filter or a future consumer may want the unadorned value.
internal sealed class DecentSamplerChannelState
{
    public const int ControllerCount = 128;
    public const int SustainController = 64;

    // The MIDI default for pitch-bend sensitivity. RPN 0 overrides it in the MPE phase.
    public const double DefaultBendSemitones = 2.0;

    private readonly int[] _controllers = new int[ControllerCount];
    private readonly bool[] _heldKeys = new bool[128];
    private readonly bool[] _sustainedKeys = new bool[128];
    private readonly long[] _sustainedFrames = new long[128];
    private readonly int[] _noteOnVelocities = new int[128];
    private readonly long[] _noteOnFrames = new long[128];
    private readonly bool[] _startedVoice = new bool[128];
    private readonly int[] _polyPressure = new int[128];

    private double _pitchBend;
    private int _heldKeyCount;

    public DecentSamplerChannelState()
    {
        Reset();
    }

    // The bend wheel, -1 to 1.
    public double PitchBend => _pitchBend;

    // How far the wheel reaches, in semitones.
    public double BendSemitones { get; set; } = DefaultBendSemitones;

    // The bend expressed in semitones, which is what a voice adds to its pitch.
    public double BendInSemitones => _pitchBend * BendSemitones;

    // Channel pressure (0xD0). Stored; no consumer until the modulator phase.
    public int ChannelPressure { get; private set; }

    // How many keys are physically down. trigger="first" and trigger="legato" test this.
    public int HeldKeyCount => _heldKeyCount;

    // Whether the sustain pedal is down. A note-off arriving while it is held is deferred.
    public bool IsSustainDown => _controllers[SustainController] >= 64;

    public void Reset()
    {
        Array.Clear(_controllers, 0, _controllers.Length);
        Array.Clear(_heldKeys, 0, _heldKeys.Length);
        Array.Clear(_sustainedKeys, 0, _sustainedKeys.Length);
        Array.Clear(_sustainedFrames, 0, _sustainedFrames.Length);
        Array.Clear(_noteOnVelocities, 0, _noteOnVelocities.Length);
        Array.Clear(_noteOnFrames, 0, _noteOnFrames.Length);
        Array.Clear(_startedVoice, 0, _startedVoice.Length);
        Array.Clear(_polyPressure, 0, _polyPressure.Length);

        _pitchBend = 0.0;
        _heldKeyCount = 0;
        ChannelPressure = 0;
        BendSemitones = DefaultBendSemitones;
    }

    // MIDI Reset All Controllers (CC 121): controller and bend state return to their defaults. Which
    // keys are physically down is a fact about the player's hands, not controller state, so it stands.
    public void ResetControllers()
    {
        Array.Clear(_controllers, 0, _controllers.Length);
        Array.Clear(_polyPressure, 0, _polyPressure.Length);

        _pitchBend = 0.0;
        ChannelPressure = 0;
        BendSemitones = DefaultBendSemitones;
    }

    public int GetController(int controller) =>
        0 <= controller && controller < ControllerCount ? _controllers[controller] : 0;

    public void SetController(int controller, int value)
    {
        if (0 <= controller && controller < ControllerCount)
        {
            _controllers[controller] = Math.Clamp(value, 0, 127);
        }
    }

    public void SetPitchBend(int data1, int data2) =>
        _pitchBend = (((data2 << 7) | data1) - 8192) / 8192.0;

    public void SetChannelPressure(int value) => ChannelPressure = Math.Clamp(value, 0, 127);

    public int GetPolyPressure(int key) => 0 <= key && key <= 127 ? _polyPressure[key] : 0;

    public void SetPolyPressure(int key, int value)
    {
        if (0 <= key && key <= 127)
        {
            _polyPressure[key] = Math.Clamp(value, 0, 127);
        }
    }

    public bool IsKeyHeld(int key) => 0 <= key && key <= 127 && _heldKeys[key];

    public bool IsKeySustained(int key) => 0 <= key && key <= 127 && _sustainedKeys[key];

    public void KeyDown(int key, int velocity, long frame)
    {
        if (key < 0 || key > 127)
        {
            return;
        }

        if (!_heldKeys[key])
        {
            _heldKeyCount++;
        }

        _heldKeys[key] = true;
        _sustainedKeys[key] = false;
        _sustainedFrames[key] = frame;
        _noteOnVelocities[key] = velocity;
        _noteOnFrames[key] = frame;
    }

    // Every key comes up at once: what All Notes Off and All Sound Off mean for the trigger="first" and
    // trigger="legato" tests, which count physically held keys.
    public void ReleaseAllKeys()
    {
        Array.Clear(_heldKeys, 0, _heldKeys.Length);
        Array.Clear(_sustainedKeys, 0, _sustainedKeys.Length);
        _heldKeyCount = 0;
    }

    public void KeyUp(int key)
    {
        if (key < 0 || key > 127)
        {
            return;
        }

        if (_heldKeys[key])
        {
            _heldKeyCount--;
        }

        _heldKeys[key] = false;
    }

    // Marks a key whose note-off arrived while the pedal was down, so lifting the pedal releases it.
    // The frame is when the KEY came up, which is what releaseTriggerDecay measures the hold against -
    // not when the pedal was lifted.
    public void SustainKey(int key, long frame)
    {
        if (0 <= key && key <= 127)
        {
            _sustainedKeys[key] = true;
            _sustainedFrames[key] = frame;
        }
    }

    public long SustainedFrame(int key) => 0 <= key && key <= 127 ? _sustainedFrames[key] : 0;

    // Whether the last note-on for this key actually started an attack voice. MEASURED (round 2,
    // item 25): a release trigger only fires on a key that started one - a group holding ONLY a
    // trigger="release" zone, on a key with no attack zone anywhere, never fires in the reference,
    // and putting a silent attack zone on the same key makes it fire at once.
    public void SetStartedVoice(int key, bool started)
    {
        if (0 <= key && key <= 127)
        {
            _startedVoice[key] = started;
        }
    }

    public bool DidStartVoice(int key) => 0 <= key && key <= 127 && _startedVoice[key];

    public void ClearSustainedKey(int key)
    {
        if (0 <= key && key <= 127)
        {
            _sustainedKeys[key] = false;
        }
    }

    public int NoteOnVelocity(int key) => 0 <= key && key <= 127 ? _noteOnVelocities[key] : 0;

    public long NoteOnFrame(int key) => 0 <= key && key <= 127 ? _noteOnFrames[key] : 0;
}
