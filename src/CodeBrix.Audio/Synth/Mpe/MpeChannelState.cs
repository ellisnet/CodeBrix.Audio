using System;
using System.Numerics;

namespace CodeBrix.Audio.Synth.Mpe;

// The MIDI Polyphonic Expression contract, written once and shared by every synthesizer in this
// package: per-channel controller, bend, tuning and pressure state for all sixteen channels, the
// registered-parameter state machine that configures them, and the zone model that says which
// channels combine with which master.
//
// Nothing here is measured against the Decent Sampler reference player, because none of it is a
// Decent Sampler invention: it is the MIDI 1.0 registered-parameter rules plus the MPE
// specification. What IS a judgement call is how a master channel's CONTINUOUS controllers combine
// with a member's - the specification is explicit only about pitch bend, which adds - and each of
// those choices is marked below.
//
// Channel numbers in this class are 0-BASED, as MIDI messages carry them. MpeZoneInfo hands them
// back 1-based, as the specification writes them.
internal sealed class MpeChannelState
{
    public const int ChannelCount = 16;
    public const int ControllerCount = 128;
    public const int KeyCount = 128;

    // CC 74 is "timbre" in MPE - the sideways finger movement an expressive controller calls slide.
    public const int TimbreController = 74;

    // MIDI's own default for pitch-bend sensitivity, which RPN 0 overrides.
    public const double DefaultBendRange = 2.0;

    // What an expressive controller ships with on its member channels, and what the MPE
    // specification names as the default a receiver assumes for members of an active zone.
    public const double DefaultMemberBendRange = 48.0;

    private const int LowerMaster = 0;
    private const int UpperMaster = 15;
    private const int RpnBendSensitivity = 0;
    private const int RpnFineTuning = 1;
    private const int RpnCoarseTuning = 2;
    private const int RpnMpeConfiguration = 6;
    private const int RpnNone = (127 << 7) | 127;

    private readonly int[] _controllers = new int[ChannelCount * ControllerCount];
    private readonly bool[] _controllerWritten = new bool[ChannelCount * ControllerCount];
    private readonly int[] _polyPressure = new int[ChannelCount * KeyCount];
    private readonly bool[] _polyPressureWritten = new bool[ChannelCount * KeyCount];
    private readonly int[] _releaseVelocity = new int[ChannelCount * KeyCount];
    private readonly bool[] _held = new bool[ChannelCount * KeyCount];
    private readonly long[] _heldStamp = new long[ChannelCount * KeyCount];

    private readonly double[] _bend = new double[ChannelCount];
    private readonly double[] _bendRange = new double[ChannelCount];
    private readonly double[] _fineTuneCents = new double[ChannelCount];
    private readonly double[] _coarseTuneSemitones = new double[ChannelCount];
    private readonly int[] _channelPressure = new int[ChannelCount];
    private readonly int[] _rpnMsb = new int[ChannelCount];
    private readonly int[] _rpnLsb = new int[ChannelCount];
    private readonly int[] _newestKey = new int[ChannelCount];

    private MpeMode _mode;
    private double _memberBendRange = DefaultMemberBendRange;
    private int _lowerMemberCount;
    private int _upperMemberCount;

    private bool _lowerActive;
    private bool _upperActive;
    private int _lowerMembers;
    private int _upperMembers;
    private double _lowerMemberBendRange = DefaultMemberBendRange;
    private double _upperMemberBendRange = DefaultMemberBendRange;
    private bool _lowerMemberRangeFromRpn;
    private bool _upperMemberRangeFromRpn;

    private bool _configurationSeen;
    private int _autoNoteMask;
    private int _autoBendMask;
    private bool _autoChannelOneNotes;

    private long _stamp;

    public MpeChannelState()
    {
        Reset();
    }

    // How the zones are chosen. Changing it reconfigures immediately.
    public MpeMode Mode
    {
        get => _mode;
        set
        {
            _mode = value;
            _configurationSeen = false;
            ClearAutoDetection();
            Configure();
        }
    }

    // The bend range a member channel is assumed to reach when no RPN 0 says otherwise, in semitones.
    public double MemberBendRange
    {
        get => _memberBendRange;
        set
        {
            _memberBendRange = value <= 0.0 ? DefaultMemberBendRange : value;

            if (!_lowerMemberRangeFromRpn)
            {
                _lowerMemberBendRange = _memberBendRange;
            }

            if (!_upperMemberRangeFromRpn)
            {
                _upperMemberBendRange = _memberBendRange;
            }
        }
    }

    // How many member channels the lower zone gets in an explicit mode. Zero means "the sensible
    // default for the mode": fifteen when only the lower zone is on, seven when both are.
    public int LowerZoneMemberCount
    {
        get => _lowerMemberCount;
        set
        {
            _lowerMemberCount = Math.Clamp(value, 0, 15);
            Configure();
        }
    }

    // The upper zone's equivalent.
    public int UpperZoneMemberCount
    {
        get => _upperMemberCount;
        set
        {
            _upperMemberCount = Math.Clamp(value, 0, 15);
            Configure();
        }
    }

    // Whether either zone is switched on, which is the cheap test the engine makes before doing any
    // master-and-member arithmetic.
    public bool HasActiveZone => _lowerActive || _upperActive;

    public MpeZoneInfo LowerZone =>
        new MpeZoneInfo(
            _lowerActive, LowerMaster + 1, 2, _lowerMembers, _bendRange[LowerMaster], _lowerMemberBendRange);

    public MpeZoneInfo UpperZone =>
        new MpeZoneInfo(
            _upperActive, UpperMaster + 1, 15, _upperMembers, _bendRange[UpperMaster], _upperMemberBendRange);

    public void Reset()
    {
        Array.Clear(_controllers, 0, _controllers.Length);
        Array.Clear(_controllerWritten, 0, _controllerWritten.Length);
        Array.Clear(_polyPressure, 0, _polyPressure.Length);
        Array.Clear(_polyPressureWritten, 0, _polyPressureWritten.Length);
        Array.Clear(_releaseVelocity, 0, _releaseVelocity.Length);
        Array.Clear(_held, 0, _held.Length);
        Array.Clear(_heldStamp, 0, _heldStamp.Length);
        Array.Clear(_bend, 0, _bend.Length);
        Array.Clear(_fineTuneCents, 0, _fineTuneCents.Length);
        Array.Clear(_coarseTuneSemitones, 0, _coarseTuneSemitones.Length);
        Array.Clear(_channelPressure, 0, _channelPressure.Length);

        for (var channel = 0; channel < ChannelCount; channel++)
        {
            _bendRange[channel] = DefaultBendRange;
            _rpnMsb[channel] = 127;
            _rpnLsb[channel] = 127;
            _newestKey[channel] = -1;
        }

        _stamp = 0;
        _configurationSeen = false;
        _lowerMemberRangeFromRpn = false;
        _upperMemberRangeFromRpn = false;
        _lowerMemberBendRange = _memberBendRange;
        _upperMemberBendRange = _memberBendRange;
        ClearAutoDetection();
        Configure();
    }

    // MIDI Reset All Controllers on one channel. Bend, pressure and controllers return to their
    // defaults; the zone layout and the bend RANGES do not, because a receiver that forgot its
    // configuration on every reset message would drop out of MPE mid-performance.
    public void ResetControllers(int channel)
    {
        if (!IsChannel(channel))
        {
            return;
        }

        var offset = channel * ControllerCount;
        Array.Clear(_controllers, offset, ControllerCount);
        Array.Clear(_controllerWritten, offset, ControllerCount);
        Array.Clear(_polyPressure, channel * KeyCount, KeyCount);
        Array.Clear(_polyPressureWritten, channel * KeyCount, KeyCount);

        _bend[channel] = 0.0;
        _channelPressure[channel] = 0;
        _rpnMsb[channel] = 127;
        _rpnLsb[channel] = 127;
    }

    // Feeds one channel-voice message. Everything the class knows arrives through here, which is why
    // a synthesizer only ever needs the one call.
    public void ProcessMessage(int channel, int command, int data1, int data2)
    {
        if (!IsChannel(channel))
        {
            return;
        }

        switch (command)
        {
            case 0x80:
                NoteOff(channel, data1, data2);
                break;

            case 0x90:
                if (data2 <= 0)
                {
                    NoteOff(channel, data1, 0);
                }
                else
                {
                    NoteOn(channel, data1);
                }

                break;

            case 0xA0:
                SetPolyPressure(channel, data1, data2);
                break;

            case 0xB0:
                ControlChange(channel, data1, data2);
                break;

            case 0xD0:
                _channelPressure[channel] = Math.Clamp(data1, 0, 127);
                break;

            case 0xE0:
                SetPitchBend(channel, data1, data2);
                break;
        }
    }

    public void NoteOn(int channel, int key)
    {
        if (!IsChannel(channel) || !IsKey(key))
        {
            return;
        }

        var index = (channel * KeyCount) + key;
        _held[index] = true;
        _heldStamp[index] = ++_stamp;
        _polyPressureWritten[index] = false;
        _polyPressure[index] = 0;

        // The MPE rule: the newest note on a member channel owns that channel's expression. A note
        // that has been superseded keeps whatever it had when it lost the channel.
        _newestKey[channel] = key;

        if (_mode != MpeMode.Auto || _configurationSeen)
        {
            return;
        }

        if (channel == LowerMaster)
        {
            _autoChannelOneNotes = true;
        }
        else
        {
            _autoNoteMask |= 1 << channel;
        }

        TryAutoDetect();
    }

    public void NoteOff(int channel, int key, int releaseVelocity)
    {
        if (!IsChannel(channel) || !IsKey(key))
        {
            return;
        }

        var index = (channel * KeyCount) + key;
        _releaseVelocity[index] = Math.Clamp(releaseVelocity, 0, 127);
        _held[index] = false;

        if (_newestKey[channel] == key)
        {
            _newestKey[channel] = NewestHeldKey(channel);
        }
    }

    public void SetPitchBend(int channel, int data1, int data2)
    {
        if (!IsChannel(channel))
        {
            return;
        }

        _bend[channel] = (((data2 << 7) | data1) - 8192) / 8192.0;

        if (_mode == MpeMode.Auto && !_configurationSeen && channel != LowerMaster)
        {
            _autoBendMask |= 1 << channel;
            TryAutoDetect();
        }
    }

    public void SetPolyPressure(int channel, int key, int value)
    {
        if (!IsChannel(channel) || !IsKey(key))
        {
            return;
        }

        var index = (channel * KeyCount) + key;
        _polyPressure[index] = Math.Clamp(value, 0, 127);
        _polyPressureWritten[index] = true;
    }

    public void ControlChange(int channel, int controller, int value)
    {
        if (!IsChannel(channel) || controller < 0 || controller >= ControllerCount)
        {
            return;
        }

        var clamped = Math.Clamp(value, 0, 127);
        var index = (channel * ControllerCount) + controller;
        _controllers[index] = clamped;
        _controllerWritten[index] = true;

        switch (controller)
        {
            case 6:
                DataEntryMsb(channel, clamped);
                break;

            case 38:
                DataEntryLsb(channel, clamped);
                break;

            case 96:
                DataStep(channel, 1);
                break;

            case 97:
                DataStep(channel, -1);
                break;

            case 98:
            case 99:
                // A non-registered parameter was selected: registered-parameter data entry stops
                // pointing anywhere, so a stray CC 6 cannot rewrite a bend range.
                _rpnMsb[channel] = 127;
                _rpnLsb[channel] = 127;
                break;

            case 100:
                _rpnLsb[channel] = clamped;
                break;

            case 101:
                _rpnMsb[channel] = clamped;
                break;

            case 121:
                ResetControllers(channel);
                break;
        }
    }

    // ---- queries the engine makes ----------------------------------------------------------------

    // The pitch a voice on this channel must add, in semitones: its own bend over its own range,
    // plus its zone master's bend over the master's range, plus the channel's registered tuning.
    public double PitchOffsetSemitones(int channel) =>
        BendSemitones(channel) + TuningSemitones(channel);

    public double BendSemitones(int channel)
    {
        if (!IsChannel(channel))
        {
            return 0.0;
        }

        var master = MasterOf(channel);

        if (master < 0)
        {
            return _bend[channel] * _bendRange[channel];
        }

        if (master == channel)
        {
            return _bend[channel] * _bendRange[channel];
        }

        // The MPE specification is explicit here: a member's bend and its master's bend ADD, each
        // over its own range. This is how a controller's global glide rides on top of a per-note one.
        var memberRange = master == LowerMaster ? _lowerMemberBendRange : _upperMemberBendRange;
        return (_bend[channel] * memberRange) + (_bend[master] * _bendRange[master]);
    }

    public double TuningSemitones(int channel)
    {
        if (!IsChannel(channel))
        {
            return 0.0;
        }

        var own = _coarseTuneSemitones[channel] + (_fineTuneCents[channel] / 100.0);
        var master = MasterOf(channel);

        if (master < 0 || master == channel)
        {
            return own;
        }

        // UNMEASURED, and unstated by the specification: a zone master's registered tuning is read
        // here as a zone-wide offset that adds to a member's own, matching the way bends add.
        return own + _coarseTuneSemitones[master] + (_fineTuneCents[master] / 100.0);
    }

    // How far a bend on this channel reaches, in semitones - the value RPN 0 writes.
    public double BendRange(int channel)
    {
        if (!IsChannel(channel))
        {
            return DefaultBendRange;
        }

        var master = MasterOf(channel);

        if (master < 0 || master == channel)
        {
            return _bendRange[channel];
        }

        return master == LowerMaster ? _lowerMemberBendRange : _upperMemberBendRange;
    }

    // Timbre, 0 to 1: CC 74 of the channel, combined with its zone master's.
    public double Timbre(int channel)
    {
        if (!IsChannel(channel))
        {
            return 0.0;
        }

        var own = Raw(channel, TimbreController) / 127.0;
        var master = MasterOf(channel);

        if (master < 0 || master == channel || !IsWritten(master, TimbreController))
        {
            return own;
        }

        // UNMEASURED: timbre rests at the CENTRE of its range, so a master's CC 74 is read as a
        // bipolar offset around 64 that shifts every note in the zone. Summing the two outright
        // would push a resting zone to full brightness the moment the master sent its first value.
        var offset = (Raw(master, TimbreController) - 64) / 127.0;
        return Math.Clamp(own + offset, 0.0, 1.0);
    }

    // Pressure for one sounding note, 0 to 1: the note's own polyphonic pressure when the file
    // carries it, otherwise the channel's, combined with the zone master's channel pressure.
    public double Pressure(int channel, int key)
    {
        if (!IsChannel(channel))
        {
            return 0.0;
        }

        var index = (channel * KeyCount) + Math.Clamp(key, 0, KeyCount - 1);
        var own = IsKey(key) && _polyPressureWritten[index]
            ? _polyPressure[index] / 127.0
            : _channelPressure[channel] / 127.0;

        var master = MasterOf(channel);

        if (master < 0 || master == channel)
        {
            return own;
        }

        // UNMEASURED: pressure rests at zero, so a master's channel pressure ADDS to a member's.
        return Math.Clamp(own + (_channelPressure[master] / 127.0), 0.0, 1.0);
    }

    // One controller's value for a channel, 0 to 127, with the zone master folded in.
    public int Controller(int channel, int controller)
    {
        if (!IsChannel(channel) || controller < 0 || controller >= ControllerCount)
        {
            return 0;
        }

        var own = Raw(channel, controller);
        var master = MasterOf(channel);

        if (master < 0 || master == channel)
        {
            return own;
        }

        if (IsSwitch(controller))
        {
            // Switches apply zone-wide: a master's sustain pedal holds every note in the zone.
            return Math.Max(own, Raw(master, controller));
        }

        if (controller == TimbreController)
        {
            return (int)Math.Round(Timbre(channel) * 127.0);
        }

        // UNMEASURED: for any other continuous controller a member's own value wins when it has sent
        // one, and the master's applies to the whole zone otherwise. Adding the two would break
        // controllers that rest at their maximum, such as expression.
        return IsWritten(channel, controller) ? own : Raw(master, controller);
    }

    // The channel's own controller value, with no master combining - what a zone filter written for
    // ordinary MIDI expects to see.
    public int RawController(int channel, int controller) =>
        IsChannel(channel) && controller >= 0 && controller < ControllerCount
            ? Raw(channel, controller)
            : 0;

    // Whether this channel has ever been sent this controller. A zone master's value only stands in
    // for a member that has not spoken for itself, so an engine that keeps its own (higher
    // resolution) controller state needs to know which of the two channels to read.
    public bool HasController(int channel, int controller) =>
        IsChannel(channel) && controller >= 0 && controller < ControllerCount &&
        IsWritten(channel, controller);

    // Which channel an engine should read a controller from for a voice playing on this one: the
    // channel itself when it is in no zone or has sent the controller, and its zone master otherwise.
    //
    // This exists so an engine that stores controllers at a higher resolution than seven bits - the
    // SoundFont engine's fourteen-bit coarse-and-fine pairs, the SFZ engine's normalised floats and
    // set_hd_ccN initial values - can apply the master/member rules WITHOUT losing that resolution:
    // it picks the channel here and then reads its own state for that channel. The rules are the
    // ones Controller() applies, so the two agree: a switch takes whichever of the pair is higher, so
    // a master's sustain pedal holds every note in the zone, and any other controller belongs to the
    // member when the member has sent one.
    public int ControllerSourceChannel(int channel, int controller)
    {
        if (!IsChannel(channel) || controller < 0 || controller >= ControllerCount)
        {
            return channel;
        }

        var master = MasterOf(channel);

        if (master < 0 || master == channel)
        {
            return channel;
        }

        if (IsSwitch(controller))
        {
            return Raw(master, controller) > Raw(channel, controller) ? master : channel;
        }

        return IsWritten(channel, controller) ? channel : master;
    }

    public int ChannelPressure(int channel) => IsChannel(channel) ? _channelPressure[channel] : 0;

    public int PolyPressure(int channel, int key) =>
        IsChannel(channel) && IsKey(key) ? _polyPressure[(channel * KeyCount) + key] : 0;

    // The note-off ("lift") velocity the last note-off for this key on this channel carried.
    public int ReleaseVelocity(int channel, int key) =>
        IsChannel(channel) && IsKey(key) ? _releaseVelocity[(channel * KeyCount) + key] : 0;

    // Whether a sounding note still owns its channel's expression. A newer note on the same member
    // channel takes it, which is the MPE rule for a controller that reuses a channel too soon.
    public bool OwnsChannelExpression(int channel, int key)
    {
        if (!IsChannel(channel))
        {
            return true;
        }

        var newest = _newestKey[channel];
        return newest < 0 || newest == key;
    }

    // Whether a channel is a member of an active zone, which is what makes the expression rules apply.
    public bool IsMember(int channel)
    {
        var master = MasterOf(channel);
        return master >= 0 && master != channel;
    }

    // The master channel of the zone a channel belongs to, the channel itself when it IS a master,
    // and -1 when it belongs to no zone.
    public int MasterOf(int channel)
    {
        if (!IsChannel(channel))
        {
            return -1;
        }

        if (_lowerActive)
        {
            if (channel == LowerMaster)
            {
                return LowerMaster;
            }

            if (channel >= 1 && channel <= _lowerMembers)
            {
                return LowerMaster;
            }
        }

        if (_upperActive)
        {
            if (channel == UpperMaster)
            {
                return UpperMaster;
            }

            if (channel <= 14 && channel >= 15 - _upperMembers)
            {
                return UpperMaster;
            }
        }

        return -1;
    }

    // ---- the registered-parameter state machine ---------------------------------------------------

    private void DataEntryMsb(int channel, int value)
    {
        switch (SelectedRpn(channel))
        {
            case RpnBendSensitivity:
            {
                // BendRange, not the per-channel array: a member of a zone reads and writes its
                // ZONE's range, so a semitone value arriving after a cent value keeps the cents.
                var current = BendRange(channel);
                SetBendRange(channel, value + (current - Math.Floor(current)));
                break;
            }

            case RpnFineTuning:
                _fineTuneCents[channel] = (((value << 7) | 0) - 8192) / 8192.0 * 100.0;
                break;

            case RpnCoarseTuning:
                _coarseTuneSemitones[channel] = value - 64;
                break;

            case RpnMpeConfiguration:
                ApplyConfiguration(channel, value);
                break;
        }
    }

    private void DataEntryLsb(int channel, int value)
    {
        switch (SelectedRpn(channel))
        {
            case RpnBendSensitivity:
                SetBendRange(channel, Math.Floor(BendRange(channel)) + (value / 100.0));
                break;

            case RpnFineTuning:
            {
                var msb = Raw(channel, 6);
                _fineTuneCents[channel] = (((msb << 7) | value) - 8192) / 8192.0 * 100.0;
                break;
            }
        }
    }

    // CC 96 and CC 97 nudge whatever registered parameter is selected. The MIDI specification leaves
    // the step size to the receiver; one unit of the parameter's own coarse resolution is the reading
    // every sequencer assumes. UNMEASURED against any reference.
    private void DataStep(int channel, int direction)
    {
        switch (SelectedRpn(channel))
        {
            case RpnBendSensitivity:
                SetBendRange(channel, BendRange(channel) + direction);
                break;

            case RpnFineTuning:
                _fineTuneCents[channel] =
                    Math.Clamp(_fineTuneCents[channel] + (direction * 100.0 / 8192.0 * 100.0), -100.0, 100.0);
                break;

            case RpnCoarseTuning:
                _coarseTuneSemitones[channel] = Math.Clamp(_coarseTuneSemitones[channel] + direction, -64, 63);
                break;
        }
    }

    private int SelectedRpn(int channel)
    {
        var selected = (_rpnMsb[channel] << 7) | _rpnLsb[channel];
        return selected == RpnNone ? -1 : selected;
    }

    private void SetBendRange(int channel, double semitones)
    {
        var range = Math.Clamp(semitones, 0.0, 96.0);
        var master = MasterOf(channel);

        if (master >= 0 && master != channel)
        {
            // Sent on a member, RPN 0 configures EVERY member of that zone, as the MPE
            // specification requires - a controller sends it once, on its lowest member channel.
            if (master == LowerMaster)
            {
                _lowerMemberBendRange = range;
                _lowerMemberRangeFromRpn = true;
            }
            else
            {
                _upperMemberBendRange = range;
                _upperMemberRangeFromRpn = true;
            }

            return;
        }

        _bendRange[channel] = range;
    }

    // RPN 6, the MPE Configuration Message: on channel 1 it defines the lower zone and on channel 16
    // the upper one, with the data-entry value as the member count. Zero switches the zone off.
    private void ApplyConfiguration(int channel, int memberCount)
    {
        if (_mode == MpeMode.Off)
        {
            // An explicit "play this as ordinary MIDI" is not overruled by the file.
            return;
        }

        if (channel != LowerMaster && channel != UpperMaster)
        {
            return;
        }

        _configurationSeen = true;
        ClearAutoDetection();

        var count = Math.Clamp(memberCount, 0, 15);

        if (channel == LowerMaster)
        {
            _lowerActive = count > 0;
            _lowerMembers = count;

            if (_upperActive && _lowerMembers + _upperMembers > 14)
            {
                _upperMembers = Math.Max(0, 14 - _lowerMembers);
                _upperActive = _upperMembers > 0;
            }
        }
        else
        {
            _upperActive = count > 0;
            _upperMembers = count;

            if (_lowerActive && _lowerMembers + _upperMembers > 14)
            {
                _lowerMembers = Math.Max(0, 14 - _upperMembers);
                _lowerActive = _lowerMembers > 0;
            }
        }

        // A zone that has just come up assumes the member default until an RPN 0 says otherwise.
        if (channel == LowerMaster && !_lowerMemberRangeFromRpn)
        {
            _lowerMemberBendRange = _memberBendRange;
        }

        if (channel == UpperMaster && !_upperMemberRangeFromRpn)
        {
            _upperMemberBendRange = _memberBendRange;
        }
    }

    // ---- zone layout ------------------------------------------------------------------------------

    private void Configure()
    {
        if (_configurationSeen)
        {
            return;
        }

        switch (_mode)
        {
            case MpeMode.LowerZone:
                _lowerActive = true;
                _lowerMembers = _lowerMemberCount > 0 ? _lowerMemberCount : 15;
                _upperActive = false;
                _upperMembers = 0;
                break;

            case MpeMode.UpperZone:
                _upperActive = true;
                _upperMembers = _upperMemberCount > 0 ? _upperMemberCount : 15;
                _lowerActive = false;
                _lowerMembers = 0;
                break;

            case MpeMode.Both:
                _lowerMembers = _lowerMemberCount > 0 ? _lowerMemberCount : 7;
                _upperMembers = _upperMemberCount > 0 ? _upperMemberCount : 7;

                if (_lowerMembers + _upperMembers > 14)
                {
                    _upperMembers = Math.Max(0, 14 - _lowerMembers);
                }

                _lowerActive = _lowerMembers > 0;
                _upperActive = _upperMembers > 0;
                break;

            default:
                _lowerActive = false;
                _upperActive = false;
                _lowerMembers = 0;
                _upperMembers = 0;
                break;
        }
    }

    // The inference the plan asks for when a file carries no configuration message: an expressive
    // performance exported from a sequencer puts every note on its own channel from 2 upward, bends
    // those channels one by one, and leaves channel 1 to the master gestures. So a lower zone is
    // inferred when nothing at all plays on channel 1, at least one note has played above it, and
    // either two or more of those channels have bent, or notes have arrived on two or more channels
    // and at least one of them has bent.
    //
    // One channel alone is never enough: an ordinary single-part file on channel 2 with a bend would
    // otherwise be mistaken for a performance and get a 48-semitone bend range. The order the file
    // writes the bends and the notes in does not matter, because an exporter that resets every
    // channel's bend before the first note is as common as one that does not.
    private void TryAutoDetect()
    {
        if (_autoChannelOneNotes || _lowerActive || _autoNoteMask == 0)
        {
            return;
        }

        var bentChannels = BitOperations.PopCount((uint)_autoBendMask);
        var playedChannels = BitOperations.PopCount((uint)_autoNoteMask);
        var bentAndPlayed = (_autoBendMask & _autoNoteMask) != 0;

        if (bentChannels < 2 && !(playedChannels >= 2 && bentAndPlayed))
        {
            return;
        }

        _lowerActive = true;
        _lowerMembers = 15;
        _upperActive = false;
        _upperMembers = 0;

        if (!_lowerMemberRangeFromRpn)
        {
            _lowerMemberBendRange = _memberBendRange;
        }
    }

    private void ClearAutoDetection()
    {
        _autoNoteMask = 0;
        _autoBendMask = 0;
        _autoChannelOneNotes = false;
    }

    private int NewestHeldKey(int channel)
    {
        var best = -1;
        var bestStamp = 0L;
        var offset = channel * KeyCount;

        for (var key = 0; key < KeyCount; key++)
        {
            if (_held[offset + key] && _heldStamp[offset + key] > bestStamp)
            {
                bestStamp = _heldStamp[offset + key];
                best = key;
            }
        }

        return best;
    }

    private int Raw(int channel, int controller) => _controllers[(channel * ControllerCount) + controller];

    private bool IsWritten(int channel, int controller) =>
        _controllerWritten[(channel * ControllerCount) + controller];

    private static bool IsSwitch(int controller) =>
        (controller >= 64 && controller <= 69) || controller >= 120;

    private static bool IsChannel(int channel) => 0 <= channel && channel < ChannelCount;

    private static bool IsKey(int key) => 0 <= key && key < KeyCount;
}
