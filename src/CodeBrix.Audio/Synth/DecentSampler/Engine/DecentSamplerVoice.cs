using System;
using CodeBrix.Audio.Synth.DecentSampler.Effects;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

// One sounding zone. The voice owns the block buffers, the amplitude envelope, the glide ramp and the
// per-output mix gains; the synthesizer owns the pool and the mixing.
//
// What is read WHEN, and why:
//   at note-on  - everything structural: the envelope times and curves, the loop bounds, the sample
//                 start and end, velocity, the round-robin choice, the delay.
//   every block - the values the guide says a binding can move while a note sounds: the group's
//                 enabled flag, the three volume levels, pan, the tunings, the tag volumes, and the
//                 output routing. That is what makes a knob move a held note.
internal sealed class DecentSamplerVoice
{
    private readonly DecentSamplerSynthesizer _synthesizer;
    private readonly int _blockSize;
    private readonly float[] _blockLeft;
    private readonly float[] _blockRight;
    private readonly DecentSamplerEnvelope _envelope;
    private readonly DecentSamplerSampleVoiceSource _sampleSource;
    private readonly DecentSamplerStreamingVoiceSource _streamingSource;
    private readonly int[] _outputSlots = new int[8];
    private readonly float[] _outputVolumes = new float[8];

    private DecentSamplerZoneRuntime _runtime;
    private IVoiceSource _source;
    private IVoiceSource _rentedOscillator;
    private DecentSamplerEffectChain _groupChain;

    private int _channel;
    private int _key;
    private int _velocity;
    private float _velocityGain;
    private float _triggerGain;
    private bool _isReleaseTrigger;
    private bool _isContinuous;
    private bool _oneShot;

    private long _delayFramesRemaining;
    private long _voiceLength;
    private long _startStamp;

    private double _glideSemitones;
    private double _glidePerBlock;

    private float _previousAmplitude;

    private float _previousMixGainLeft;
    private float _previousMixGainRight;
    private float _currentMixGainLeft;
    private float _currentMixGainRight;

    private bool _finished;
    private bool _killed;
    private bool _hasSounded;

    private double _pitchOffsetSemitones;
    private int _releaseVelocity;

    // Whether the source's own envelopes end the note, so the group envelope must not release.
    private bool _sourceOwnsRelease;

    // What the sample source was prepared with, so a SAMPLE_END binding can be spotted mid-note.
    private bool _sampleEndFollowsLive;
    private long? _preparedZoneEnd;
    private long _preparedStartFrame;
    private long _preparedLastFrame;

    public DecentSamplerVoice(
        DecentSamplerSynthesizer synthesizer, int sampleRate, int blockSize, int id)
    {
        _synthesizer = synthesizer;
        Id = id;
        _blockSize = blockSize;
        _blockLeft = new float[blockSize];
        _blockRight = new float[blockSize];
        _envelope = new DecentSamplerEnvelope(sampleRate, blockSize);
        _sampleSource = new DecentSamplerSampleVoiceSource(sampleRate);
        _streamingSource = new DecentSamplerStreamingVoiceSource(sampleRate);
    }

    // The voice's permanent slot in the pool, which the modulation runtime uses to key this voice's
    // own modulator state and its contributions to every parameter it reaches.
    public int Id { get; }

    public DecentSamplerZoneRuntime Runtime => _runtime;

    public int Channel => _channel;

    public int Key => _key;

    public bool IsReleaseTrigger => _isReleaseTrigger;

    public bool IsContinuous => _isContinuous;

    public bool IsReleased => _envelope.IsReleased;

    public int Velocity => _velocity;

    // The note-off ("lift") velocity that ended this note, 0 until the key comes up. The Decent
    // Sampler format defines no consumer for it, so it changes nothing about the sound; it is here
    // for hosts and for any future binding source.
    public int ReleaseVelocity => _releaseVelocity;

    public long StartStamp => _startStamp;

    public long VoiceLength => _voiceLength;

    public float[] BlockLeft => _blockLeft;

    // A mono source fills only the left buffer, so both sides of the mix read the same audio - the
    // same arrangement SfzVoice uses. A GROUP CHAIN forces the two buffers apart, because an effect
    // processes a stereo pair in place and would run twice over one buffer.
    public float[] BlockRight => IsStereoSource || _groupChain != null ? _blockRight : _blockLeft;

    public bool IsStereoSource => _source != null && _source.IsStereo;

    // The per-voice chain built from the group's <effects>, or null when the group declares none.
    public DecentSamplerEffectChain GroupChain => _groupChain;

    public float PreviousMixGainLeft => _previousMixGainLeft;

    public float PreviousMixGainRight => _previousMixGainRight;

    public float CurrentMixGainLeft => _currentMixGainLeft;

    public float CurrentMixGainRight => _currentMixGainRight;

    public int OutputSlot(int index) => _outputSlots[index];

    public float OutputVolume(int index) => _outputVolumes[index];

    // Voice-stealing priority. A delayed voice that has not started yet outranks everything, because
    // dropping it would lose a note the listener has not heard; otherwise the envelope decides.
    public float Priority
    {
        get
        {
            if (_killed)
            {
                return 0f;
            }

            return _delayFramesRemaining > 0 ? 5f : _envelope.Priority;
        }
    }

    public void Start(
        DecentSamplerZoneRuntime runtime,
        int channel,
        int key,
        int velocity,
        float triggerGain,
        long delayFrames,
        double glideFromSemitones,
        double glideSeconds,
        long stamp)
    {
        _runtime = runtime;
        _channel = channel;
        _key = key;
        _velocity = velocity;
        _triggerGain = triggerGain;
        _startStamp = stamp;
        _voiceLength = 0;
        _finished = false;
        _killed = false;
        _hasSounded = false;

        var zone = runtime.Zone;

        _isReleaseTrigger = zone.Trigger == DecentSamplerTrigger.Release;
        _isContinuous = zone.Trigger == DecentSamplerTrigger.Continuous;
        _oneShot = !zone.AmpEnvEnabled;

        // Measured: gain = (1 - ampVelTrack) + ampVelTrack * velocity/127, a linear amplitude blend,
        // with ampVelTrack defaulting to 1 and clamped to [0,1] (plan section 7 item 2).
        var track = Math.Clamp(zone.AmpVelTrack, 0.0, 1.0);
        _velocityGain = (float)((1.0 - track) + track * (Math.Clamp(velocity, 0, 127) / 127.0));

        _delayFramesRemaining = Math.Max(0, delayFrames);

        _glideSemitones = glideSeconds > 0.0 ? glideFromSemitones : 0.0;
        _glidePerBlock = glideSeconds > 0.0 && Math.Abs(glideFromSemitones) > 0.0
            ? Math.Abs(glideFromSemitones) / (glideSeconds * _synthesizer.SampleRate) * _blockSize
            : 0.0;

        RefreshOutputs();

        // The guide: a group chain is instantiated per note. The pool hands out a chain that has been
        // reset, so a voice always starts with cleared filter and delay state.
        _groupChain = runtime.GroupChains?.Rent();

        if (runtime.IsOscillator)
        {
            _sampleEndFollowsLive = false;
            _rentedOscillator = runtime.RentOscillator();
            _source = _rentedOscillator;
        }
        else
        {
            _rentedOscillator = null;
            PrepareSampleSource(runtime, zone);
            _source = runtime.IsStreaming ? _streamingSource : (IVoiceSource)_sampleSource;
        }

        _releaseVelocity = 0;
        _pitchOffsetSemitones = _synthesizer.BendSemitones(_channel);

        var release = _synthesizer.EffectiveRelease(runtime);

        _envelope.Start(
            zone.Attack, zone.Decay, zone.Sustain, release,
            zone.AttackCurve, zone.DecayCurve, zone.ReleaseCurve,
            zone.AmpEnvEnabled);

        _source?.Start(_key, _velocity, CurrentPitchHz());

        // Read AFTER the source has started: an oscillator adapter builds its generator on the first
        // note, so it cannot answer this until then.
        _sourceOwnsRelease = _source != null && _source.OwnsRelease;

        _previousAmplitude = 0f;
        _previousMixGainLeft = 0f;
        _previousMixGainRight = 0f;
        _currentMixGainLeft = 0f;
        _currentMixGainRight = 0f;
    }

    // A note-off for this voice's key: the envelope releases. A one-shot ignores it, as measured.
    public void End()
    {
        _releaseVelocity = _synthesizer.ReleaseVelocity(_channel, _key);

        // A voice-scope modulator's envelope releases whatever the zone's own amplitude envelope
        // does: a one-shot ignores note-off for its own level, but an envelope modulating a filter
        // still has to come back down.
        _synthesizer.Modulation?.VoiceReleased(Id);

        if (_oneShot)
        {
            return;
        }

        _source?.NoteOff();

        // MEASURED (round 4, item 56): a fm6op voice's tail is the longest OPERATOR release, not the
        // group envelope's, so a source that owns its release keeps the level it had at note-off and
        // ends the voice itself. Voice stealing and silencedByTags still fade it out, because those
        // go through SilenceNormal, SilenceFast and SilenceTimed rather than through the key.
        if (_sourceOwnsRelease)
        {
            return;
        }

        _envelope.Release();
    }

    // silencedByTags with silencingMode="normal": the zone's own release phase.
    public void SilenceNormal()
    {
        _source?.NoteOff();
        _envelope.ReleaseNormal();
    }

    // silencedByTags with silencingMode="fast", and the choke a stolen voice gets.
    public void SilenceFast()
    {
        _source?.NoteOff();
        _envelope.ReleaseFast();
    }

    // silencingDecay: an exact fade-out time in seconds, overriding silencingMode.
    public void SilenceTimed(double seconds)
    {
        _source?.NoteOff();
        _envelope.ReleaseTimed(seconds);
    }

    public void Kill() => _killed = true;

    // Renders one block into the voice's own buffers and works out this block's mix gains. Returns
    // false when the voice is finished and may be recycled.
    public bool Process()
    {
        if (_killed || _finished)
        {
            Recycle();
            return false;
        }

        // The delay gate: silent blocks that do not consume the sample.
        if (_delayFramesRemaining >= _blockSize)
        {
            _delayFramesRemaining -= _blockSize;
            Array.Clear(_blockLeft, 0, _blockLeft.Length);
            Array.Clear(_blockRight, 0, _blockRight.Length);
            _previousMixGainLeft = 0f;
            _previousMixGainRight = 0f;
            _currentMixGainLeft = 0f;
            _currentMixGainRight = 0f;
            _voiceLength += _blockSize;
            return true;
        }

        // What is left of the delay is shorter than a block, so this block is part silence and part
        // sound. The source fills the tail of the buffers and the head is cleared, which is what makes
        // a note DELAY, a SEQUENCED note and an ARPEGGIATED note land on the exact frame they are due
        // rather than on the block boundary that follows it.
        var startOffset = (int)_delayFramesRemaining;
        _delayFramesRemaining = 0;

        if (!_envelope.Process())
        {
            Recycle();
            return false;
        }

        AdvanceGlide();
        RefreshPitchOffset();
        RefreshSampleEnd();

        _source.SetPitch(CurrentPitchHz());

        if (!_source.Render(_blockLeft, _blockRight, _blockSize - startOffset))
        {
            Recycle();
            return false;
        }

        if (startOffset > 0)
        {
            ShiftIntoPlace(startOffset);
        }

        RefreshOutputs();

        _previousMixGainLeft = _currentMixGainLeft;
        _previousMixGainRight = _currentMixGainRight;

        var amplitude = (float)_synthesizer.LiveVoiceGain(_runtime) * _velocityGain * _triggerGain *
                        _envelope.Value;

        // Measured pan law (plan section 7 item 3): constant power NORMALISED TO UNITY AT CENTRE, so a
        // centred zone is 0 dB in both channels and a hard-panned one is +3.01 dB in the surviving
        // channel and digital silence in the other. An inner pan replaces an outer one, which the
        // resolved zone already did.
        //
        // AWAITING MEASUREMENT: the law was measured with MONO samples. A stereo source gets the same
        // pair of gains here, which reads pan as a balance - hard left keeps the left channel and mutes
        // the right. Whether the reference player instead collapses a stereo zone to mono and re-pans it
        // has not been recorded.
        var pan = Math.Clamp(_runtime.Zone.Pan, -100.0, 100.0);
        var theta = (pan + 100.0) / 200.0 * (Math.PI / 2.0);
        var sqrt2 = 1.4142135623730951;
        var panLeft = (float)(sqrt2 * Math.Cos(theta));
        var panRight = (float)(sqrt2 * Math.Sin(theta));

        // The first block a voice actually sounds in does not ramp: it starts at its own level, on its
        // own frame. Counting from the first SOUNDING block rather than the first block of the voice is
        // what keeps a DELAYED note - and every sequenced and arpeggiated note - from fading in over a
        // block after landing exactly on its frame.
        var first = !_hasSounded;

        if (first)
        {
            _previousAmplitude = amplitude;
        }

        if (_groupChain != null)
        {
            // MEASURED (round 2, item 24): A GROUP'S <effects> CHAIN RUNS AFTER THE AMPLITUDE ENVELOPE.
            // A ten-point attack ramp through a group compressor showed no gain reduction at all while
            // the ENVELOPED signal was below the threshold and reduction growing exactly as the envelope
            // opened, which a chain in front of the envelope cannot produce. So the whole per-voice
            // amplitude stage - the three volume levels, the tag volumes, velocity, the trigger gain
            // and the envelope - is applied to the block here, and only the PAN is left for the mix.
            //
            // A mono source only filled the left buffer; the chain needs a real pair.
            if (!_source.IsStereo)
            {
                Array.Copy(_blockLeft, _blockRight, _blockSize);
            }

            ApplyAmplitude(_previousAmplitude, amplitude);
            _groupChain.Process(_blockLeft, _blockRight, _blockSize);

            _currentMixGainLeft = panLeft;
            _currentMixGainRight = panRight;
        }
        else
        {
            // With no chain in the way the amplitude is a plain factor on the mix gain, which costs a
            // pass over the block less and leaves this path bit-for-bit what it always was.
            _currentMixGainLeft = amplitude * panLeft;
            _currentMixGainRight = amplitude * panRight;
        }

        _previousAmplitude = amplitude;

        if (first)
        {
            _previousMixGainLeft = _currentMixGainLeft;
            _previousMixGainRight = _currentMixGainRight;
            _hasSounded = true;
        }

        _voiceLength += _blockSize;
        return true;
    }

    // Applies the voice's amplitude stage to the block in place, ramped over the block so that a moving
    // envelope or a knob under a binding does not step.
    private void ApplyAmplitude(float from, float to)
    {
        if (from == to)
        {
            if (to != 1f)
            {
                for (var f = 0; f < _blockSize; f++)
                {
                    _blockLeft[f] *= to;
                    _blockRight[f] *= to;
                }
            }

            return;
        }

        var step = (to - from) / _blockSize;

        for (var f = 0; f < _blockSize; f++)
        {
            var gain = from + (step * f);
            _blockLeft[f] *= gain;
            _blockRight[f] *= gain;
        }
    }

    // Moves the block the source just rendered along by the sub-block start delay, and clears the
    // silence in front of it. Array.Copy handles the overlap.
    private void ShiftIntoPlace(int offset)
    {
        var frames = _blockSize - offset;

        Array.Copy(_blockLeft, 0, _blockLeft, offset, frames);
        Array.Clear(_blockLeft, 0, offset);

        if (_source.IsStereo || _groupChain != null)
        {
            Array.Copy(_blockRight, 0, _blockRight, offset, frames);
            Array.Clear(_blockRight, 0, offset);
        }
    }

    // Returns any pooled oscillator and drops the source. Safe to call more than once.
    public void Recycle()
    {
        if (!_finished)
        {
            _synthesizer.Modulation?.VoiceEnded(Id);
        }

        _finished = true;

        if (_rentedOscillator != null)
        {
            _runtime.ReturnOscillator(_rentedOscillator);
            _rentedOscillator = null;
        }

        if (_groupChain != null)
        {
            _runtime.GroupChains?.Return(_groupChain);
            _groupChain = null;
        }

        // A streamed voice hands its ring buffer back to the pool here, whether it ended on its own or
        // was stolen.
        _streamingSource.Recycle();

        _source = null;
    }

    private void AdvanceGlide()
    {
        if (_glidePerBlock <= 0.0 || _glideSemitones == 0.0)
        {
            _glideSemitones = 0.0;
            return;
        }

        // Measured: the ramp is LINEAR IN PITCH over glideTime seconds, whatever the interval
        // (plan section 7 item 10b).
        if (_glideSemitones > 0.0)
        {
            _glideSemitones = Math.Max(0.0, _glideSemitones - _glidePerBlock);
        }
        else
        {
            _glideSemitones = Math.Min(0.0, _glideSemitones + _glidePerBlock);
        }
    }

    // The bend and registered tuning of the voice's own channel, plus its zone master's. A note that
    // has lost its member channel to a newer one keeps the offset it had, which is the MPE rule.
    private void RefreshPitchOffset()
    {
        if (_synthesizer.OwnsChannelExpression(_channel, _key))
        {
            _pitchOffsetSemitones = _synthesizer.BendSemitones(_channel);
        }
    }

    // MEASURED: the reference clamps groupTuning to three octaves either way.
    private const double GroupTuningLimit = 36.0;

    private double CurrentPitchHz()
    {
        var zone = _runtime.Zone;
        var group = _runtime.Group;

        var root = zone.RootNote;
        var keyOffset = (_key - root) * zone.PitchKeyTrack;

        // Measured: every tuning attribute is in SEMITONES and the levels ADD (plan section 7 item 3).
        // MEASURED (round 2, item 24's "more behaviours"): groupTuning alone is CLAMPED to plus or
        // minus 36 semitones - three octaves - while tuning and globalTuning are not.
        var groupTuning = Math.Clamp(group.GroupTuning, -GroupTuningLimit, GroupTuningLimit);

        var semitones = keyOffset + zone.Tuning + groupTuning + group.GlobalTuning +
                        _glideSemitones + _pitchOffsetSemitones;

        return DecentSamplerSampleVoiceSource.NoteToHertz(root + semitones);
    }

    private void PrepareSampleSource(DecentSamplerZoneRuntime runtime, DecentSamplerZone zone)
    {
        // A LOOP_START or LOOP_END binding takes effect HERE, at the next note-on, and never on a
        // voice already sounding - which is already more than the reference does with them (MEASURED,
        // round 4, item 54: it ignores both entirely). SAMPLE_START is the same, and measured to be
        // so; SAMPLE_END alone also follows a voice that is already sounding, in RefreshSampleEnd.
        runtime.RefreshLoopIfMoved();

        var lastFrame = Math.Max(0, runtime.SourceFrames - 1);

        var startFrame = Math.Clamp(zone.Start, 0, lastFrame);
        var endFrame = zone.End.HasValue ? Math.Clamp(zone.End.Value, startFrame, lastFrame) : lastFrame;

        // Only an in-memory zone follows SAMPLE_END live: the guide restricts all four sample-point
        // parameters to in-memory playback, and the instrument reports a streamed preset that moves
        // one.
        _sampleEndFollowsLive = !runtime.IsStreaming;
        _preparedZoneEnd = zone.End;
        _preparedStartFrame = startFrame;
        _preparedLastFrame = lastFrame;
        var crossfade = Math.Max(0, zone.LoopCrossfade);
        var equalPower = zone.LoopCrossfadeMode == DecentSamplerLoopCrossfadeMode.EqualPower;

        if (runtime.IsStreaming)
        {
            _streamingSource.Prepare(
                runtime.Streaming,
                _synthesizer.StreamingPool,
                _synthesizer.StreamingMode,
                startFrame,
                endFrame,
                runtime.LoopEnabled,
                runtime.LoopStartFrame,
                runtime.LoopEndFrameInclusive,
                crossfade,
                equalPower,
                zone.RootNote);

            return;
        }

        _sampleSource.Prepare(
            runtime.Data,
            startFrame,
            endFrame,
            runtime.LoopEnabled,
            runtime.LoopStartFrame,
            runtime.LoopEndFrameInclusive,
            crossfade,
            equalPower,
            zone.RootNote);
    }

    // MEASURED (round 4, item 54): SAMPLE_END applies to a SOUNDING voice. A voice whose read
    // position is already past the new end stops at once - the moved bound makes the next block's
    // first frame fall outside the playable data, which ends the voice - and one still short of it
    // plays on and stops there. Two nullable comparisons per block in the ordinary case.
    private void RefreshSampleEnd()
    {
        if (!_sampleEndFollowsLive)
        {
            return;
        }

        var end = _runtime.Zone.End;

        if (end == _preparedZoneEnd)
        {
            return;
        }

        _preparedZoneEnd = end;
        _sampleSource.SetEndFrame(
            end.HasValue
                ? Math.Clamp(end.Value, _preparedStartFrame, _preparedLastFrame)
                : _preparedLastFrame);
    }

    private void RefreshOutputs()
    {
        var zone = _runtime.Zone;
        var targets = zone.OutputTargets;
        var volumes = zone.OutputVolumes;

        for (var i = 0; i < 8; i++)
        {
            _outputSlots[i] = DecentSamplerOutputSlot.FromTarget(targets[i]).Index;
            _outputVolumes[i] = (float)Math.Clamp(volumes[i], 0.0, 1.0);
        }
    }
}
