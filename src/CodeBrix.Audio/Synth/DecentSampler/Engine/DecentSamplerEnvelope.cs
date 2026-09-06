using System;

namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

// The Decent Sampler amplitude envelope: attack, decay, sustain, release in SECONDS with a linear
// sustain level, each falling or rising segment shaped by DecentSamplerCurves.
//
// Measured defaults (plan section 7 item 4): attack 0, decay 0, sustain 1, RELEASE 0.5 SECONDS -
// the release default is not documented anywhere and matters, because a preset that sets no release
// still rings for half a second. attackCurve defaults to -100, decayCurve and releaseCurve to +100.
//
// The envelope is evaluated once per render block, exactly as SfzEnvelope is, so a block is the
// resolution of every stage boundary. That is the same 64-frame grain the rest of the engine works on.
internal sealed class DecentSamplerEnvelope
{
    private readonly int _sampleRate;
    private readonly int _blockSize;

    private double _attackSeconds;
    private double _decaySeconds;
    private double _releaseSeconds;
    private double _attackCurve;
    private double _decayCurve;
    private double _releaseCurve;
    private float _sustainLevel;

    private bool _enabled;

    private long _frames;
    private double _releaseStartTime;
    private float _releaseLevel;
    private double _releaseOverrideSeconds;
    private bool _releaseLengthOverridden;

    private Stage _stage;
    private float _value;
    private float _priority;

    public DecentSamplerEnvelope(int sampleRate, int blockSize)
    {
        _sampleRate = sampleRate;
        _blockSize = blockSize;
    }

    // The current amplitude, 0..1.
    public float Value => _value;

    // Voice-stealing priority: higher survives. Attack outranks decay outranks sustain outranks
    // release, and within a stage the louder voice wins - the SFZ collection's rule.
    public float Priority => _priority;

    public bool IsReleased => _stage >= Stage.Release;

    // Starts the envelope. Every time is in seconds and every curve is the raw attribute value.
    public void Start(
        double attack, double decay, double sustain, double release,
        double attackCurve, double decayCurve, double releaseCurve, bool enabled)
    {
        _attackSeconds = Math.Max(0.0, attack);
        _decaySeconds = Math.Max(0.0, decay);
        _releaseSeconds = Math.Max(0.0, release);
        _attackCurve = attackCurve;
        _decayCurve = decayCurve;
        _releaseCurve = releaseCurve;
        _sustainLevel = (float)Math.Clamp(sustain, 0.0, 1.0);
        _enabled = enabled;

        _frames = 0;
        _releaseStartTime = 0.0;
        _releaseLevel = 0f;
        _releaseLengthOverridden = false;
        _releaseOverrideSeconds = 0.0;

        _stage = Stage.Attack;
        _value = 0f;

        Advance(0);
    }

    // The key came up. An amp-envelope-disabled zone is a one-shot and ignores this, as measured.
    public void Release()
    {
        if (!_enabled || _stage >= Stage.Release)
        {
            return;
        }

        BeginRelease();
    }

    // silencingMode="normal": the release phase, whatever the zone's release time says.
    public void ReleaseNormal()
    {
        if (_stage >= Stage.Release)
        {
            return;
        }

        BeginRelease();
    }

    // silencingMode="fast", and the choke a stolen voice gets. MEASURED (round 2, item 25): the last
    // sample above -40 dB of the peak is 1.84 ms after the killer note-on, which is inside the MIDI
    // jitter, so the choke is at most about 2 ms.
    public void ReleaseFast() => ReleaseTimed(DecentSamplerDefaults.FastSilencingSeconds);

    // silencingDecay: an exact fade-out time in seconds, overriding silencingMode's own time.
    //
    // MEASURED (round 2, item 25): silencingDecay="0.5" and silencingMode="normal" with release="0.5"
    // are THE SAME CURVE to within 0.2 dB at every measured point, so silencingDecay is the amplitude
    // envelope's release phase with that time and the group's own releaseCurve - not a linear fade.
    // Only the LENGTH is overridden here; the shape stays the release's.
    public void ReleaseTimed(double seconds)
    {
        _releaseLevel = _value;
        _releaseStartTime = (double)_frames / _sampleRate;
        _releaseOverrideSeconds = Math.Max(seconds, 0.0);
        _releaseLengthOverridden = true;
        _stage = Stage.Release;
    }

    // Advances one block. Returns false once the envelope has finished and the voice may be recycled.
    public bool Process() => Advance(_blockSize);

    private void BeginRelease()
    {
        _releaseLevel = _value;
        _releaseStartTime = (double)_frames / _sampleRate;
        _releaseLengthOverridden = false;
        _stage = Stage.Release;
    }

    private bool Advance(int frames)
    {
        _frames += frames;

        var time = (double)_frames / _sampleRate;

        if (!_enabled)
        {
            // ampEnvEnabled="false" is a one-shot: full level for the whole sample, no shaping, and
            // note-off ignored. The voice ends when the sample data runs out.
            _value = 1f;
            _priority = 2f + _value;
            return true;
        }

        switch (_stage)
        {
            case Stage.Attack:
                if (time < _attackSeconds)
                {
                    _value = (float)DecentSamplerCurves.Shape(-_attackCurve, time / _attackSeconds);
                    _priority = 3f + _value;
                    return true;
                }

                _stage = Stage.Decay;
                goto case Stage.Decay;

            case Stage.Decay:
            {
                var elapsed = time - _attackSeconds;
                if (elapsed < _decaySeconds)
                {
                    var fallen = DecentSamplerCurves.Shape(_decayCurve, elapsed / _decaySeconds);
                    _value = (float)(1.0 - (1.0 - _sustainLevel) * fallen);
                    _priority = 1f + _value;
                    return _value > SoundFontMath.NonAudible || _sustainLevel > 0f;
                }

                _stage = Stage.Sustain;
                goto case Stage.Sustain;
            }

            case Stage.Sustain:
                _value = _sustainLevel;
                _priority = 1f + _value;

                // A zone with sustain 0 and a finished decay has nothing left to say.
                return _value > SoundFontMath.NonAudible;

            case Stage.Release:
            {
                var elapsed = time - _releaseStartTime;
                var length = _releaseLengthOverridden ? _releaseOverrideSeconds : _releaseSeconds;

                if (length <= 0.0)
                {
                    _value = 0f;
                    _priority = 0f;
                    return false;
                }

                if (elapsed >= length)
                {
                    _value = 0f;
                    _priority = 0f;
                    return false;
                }

                // The group's releaseCurve shapes an overridden length too: MEASURED, silencingDecay
                // is the release phase with a different time, not a fade of its own.
                var fallen = DecentSamplerCurves.Shape(_releaseCurve, elapsed / length);

                _value = (float)(_releaseLevel * (1.0 - fallen));
                _priority = _value;
                return _value > SoundFontMath.NonAudible;
            }

            default:
                throw new InvalidOperationException("Invalid envelope stage.");
        }
    }

    private enum Stage
    {
        Attack,
        Decay,
        Sustain,
        Release,
    }
}
