using System;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Effects;

// The delay effect: one feedback delay line per channel, in seconds or in musical time.
//
// FROM THE GUIDE:
//   delayTime       0 to 20 seconds, default 0.7, or a musical subdivision index when
//                   delayTimeFormat="musical_time".
//   feedback        0 to 1, default 0.2.
//   stereoOffset    -10 to 10 seconds, default 0. HALF of it is subtracted from the left channel's
//                   delay and half added to the right channel's, so the two are offset by the whole
//                   amount - the guide's own worked example.
//   wetLevel        0 to 1, default 0.5, "the volume of the delay signal". Read the same way the
//                   reverb's measured wetLevel reads: a linear gain on the wet path, with the dry
//                   path untouched.
//
// MUSICAL TIME (measured, plan section 7 item 8): the subdivision table has 25 entries and index 13 is
// a quarter note. Seconds come from DecentSamplerTempo against the synthesizer's TempoSource, so a
// tempo change moves the echoes while the instrument plays.
internal sealed class DecentSamplerDelayEffect : DecentSamplerEffectBase
{
    // The largest delay the format documents, plus the largest stereo offset.
    private const double MaximumDelaySeconds = 20.0;
    private const double MaximumOffsetSeconds = 10.0;

    private readonly TempoSource _tempo;

    private float[] _lineLeft;
    private float[] _lineRight;
    private int _capacity;
    private int _writeIndex;

    private float _feedback;
    private float _wetLevel;
    private double _delayLeftFrames;
    private double _delayRightFrames;

    public DecentSamplerDelayEffect(EffectContext context)
        : base(
            context,
            "FX_DELAY_TIME", "FX_DELAY_TIME_FORMAT", "FX_FEEDBACK", "FX_STEREO_OFFSET", "FX_WET_LEVEL")
    {
        _tempo = context.TempoSource;
    }

    // How long the delay lines are, in frames. A binding that asks for longer than this is clamped.
    public int CapacityFrames => _capacity;

    // The delay each channel is running at this block, in frames.
    public double DelayLeftFrames => _delayLeftFrames;

    public double DelayRightFrames => _delayRightFrames;

    protected override void OnPrepare()
    {
        _capacity = Math.Max(2, (int)Math.Ceiling(CapacitySeconds() * SampleRate) + 2);
        _lineLeft = new float[_capacity];
        _lineRight = new float[_capacity];
        _writeIndex = 0;
    }

    protected override void OnReset()
    {
        if (_lineLeft != null)
        {
            Array.Clear(_lineLeft, 0, _lineLeft.Length);
            Array.Clear(_lineRight, 0, _lineRight.Length);
        }

        _writeIndex = 0;
    }

    protected override void Refresh(bool force)
    {
        _feedback = (float)Math.Clamp(Effect.Feedback ?? 0.2, 0.0, 1.0);
        _wetLevel = (float)Math.Max(0.0, Effect.WetLevel ?? 0.5);

        var seconds = DelaySeconds();
        var offset = Math.Clamp(Effect.StereoOffset ?? 0.0, -MaximumOffsetSeconds, MaximumOffsetSeconds);

        var left = Math.Max(0.0, seconds - offset / 2.0) * SampleRate;
        var right = Math.Max(0.0, seconds + offset / 2.0) * SampleRate;

        // A line only as long as the preset's own declaration can be asked for more by a binding; the
        // honest answer is the longest echo the line holds rather than a wrapped one.
        var ceiling = _capacity - 2;
        _delayLeftFrames = Math.Clamp(left, 1.0, ceiling);
        _delayRightFrames = Math.Clamp(right, 1.0, ceiling);
    }

    protected override void ProcessCore(float[] left, float[] right, int frames)
    {
        var feedback = _feedback;
        var wet = _wetLevel;
        var delayLeft = _delayLeftFrames;
        var delayRight = _delayRightFrames;

        for (var i = 0; i < frames; i++)
        {
            var echoLeft = Read(_lineLeft, delayLeft);
            var echoRight = Read(_lineRight, delayRight);

            _lineLeft[_writeIndex] = left[i] + echoLeft * feedback;
            _lineRight[_writeIndex] = right[i] + echoRight * feedback;

            left[i] += echoLeft * wet;
            right[i] += echoRight * wet;

            _writeIndex++;
            if (_writeIndex == _capacity)
            {
                _writeIndex = 0;
            }
        }
    }

    // Linear interpolation across the read point, so a delay time that is not a whole number of frames
    // - which every musical subdivision at an arbitrary tempo is - stays in tune.
    private float Read(float[] line, double delayFrames)
    {
        var position = _writeIndex - delayFrames;
        while (position < 0.0)
        {
            position += _capacity;
        }

        var index = (int)position;
        var fraction = (float)(position - index);

        if (index >= _capacity)
        {
            index -= _capacity;
        }

        var next = index + 1;
        if (next >= _capacity)
        {
            next -= _capacity;
        }

        return line[index] + (line[next] - line[index]) * fraction;
    }

    private double DelaySeconds()
    {
        var format = Effect.DelayTimeFormat ?? DecentSamplerDelayTimeFormat.Seconds;
        var value = Effect.DelayTime ?? 0.7;

        if (format != DecentSamplerDelayTimeFormat.MusicalTime)
        {
            return Math.Clamp(value, 0.0, MaximumDelaySeconds);
        }

        var index = (int)Math.Round(value, MidpointRounding.AwayFromZero);
        return DecentSamplerTempo.SecondsForSubdivision(index, _tempo);
    }

    // How long a line to allocate. A delay written in seconds gets four times its declared time, so a
    // knob bound to FX_DELAY_TIME has room to move; one written in musical time gets the longest
    // subdivision at the tempo the instrument starts at. Both are capped at the format's own maximum.
    private double CapacitySeconds()
    {
        var offset = Math.Abs(Math.Clamp(Effect.StereoOffset ?? 0.0, -MaximumOffsetSeconds, MaximumOffsetSeconds));

        if ((Effect.DelayTimeFormat ?? DecentSamplerDelayTimeFormat.Seconds) ==
            DecentSamplerDelayTimeFormat.MusicalTime)
        {
            var longest = DecentSamplerTempo.SecondsForSubdivision(
                DecentSamplerTempo.SubdivisionCount - 1, _tempo);
            return Math.Clamp(longest + offset / 2.0, 1.0, MaximumDelaySeconds + MaximumOffsetSeconds);
        }

        var declared = Math.Clamp(Effect.DelayTime ?? 0.7, 0.0, MaximumDelaySeconds);
        return Math.Clamp(
            declared * 4.0 + offset / 2.0 + 0.25, 1.0, MaximumDelaySeconds + MaximumOffsetSeconds);
    }
}
