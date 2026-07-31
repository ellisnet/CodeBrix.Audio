using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.Sfz;

// One sounding SFZ voice. Per block it runs: region delay gate, amplifier envelope (shapes, vel2 and
// ampeg_dynamic included), LFOs (v1 blocks and v2 lfoN, with cross-LFO frequency modulation), the
// modulation envelopes (fileg/pitcheg) and flexible envelopes (egN), pitch assembly, the oscillator,
// two filters in series, up to three EQ bands, stereo width, and the gain assembly - volume dB, the
// multiplicative amplitude fader (with smoothing), pan, and the key/velocity/controller crossfades.
// CC modulation resolves through the extended-source table (velocity, key delta, per-voice randoms).
internal sealed class SfzVoice
{
    private readonly SfzSynthesizer synthesizer;

    private readonly SfzEnvelope envelope;
    private readonly SfzFilter filterLeft;
    private readonly SfzFilter filterRight;
    private readonly SfzFilter filter2Left;
    private readonly SfzFilter filter2Right;
    private readonly SfzOscillator oscillator;
    private readonly float blockSeconds;

    private readonly float[] blockLeft;
    private readonly float[] blockRight;

    // Same anti-pop scheme as the SoundFont voice: the previous block's mix gains are kept and the
    // synthesizer ramps between them while writing.
    private float previousMixGainLeft;
    private float previousMixGainRight;
    private float currentMixGainLeft;
    private float currentMixGainRight;

    private SfzRegion region;
    private SfzSampleData sample;
    private SfzChannel channelState;
    private int channel;
    private int key;
    private int velocity;

    private SfzLoopMode loopMode;
    private bool oneShot;
    private bool hasFilter;
    private bool hasFilter2;

    private float staticGain;
    private float staticVolumeDb;
    private float staticPan;
    private float baseCutoff;
    private float baseCutoff2;
    private float pitchStaticCents;
    private double sampleRateRatio;
    private float staticXfGain;

    private long delayFramesRemaining;

    // Per-voice extended modulation sources, drawn or latched at Start.
    private float unipolarRandom;
    private float bipolarRandom;
    private float alternate;
    private float keyDelta;

    private SfzLfoUnit[] lfoUnits;
    private SfzModEnvelopeUnit filEgUnit;
    private SfzModEnvelopeUnit pitchEgUnit;
    private SfzFlexEgUnit[] flexEgUnits;
    private SfzEqFilter[] eqLeft;
    private SfzEqFilter[] eqRight;
    private bool[] eqActive;
    private float[] amplitudeSmoothState;

    private VoiceState voiceState;
    private int voiceLength;

    internal SfzVoice(SfzSynthesizer synthesizer)
    {
        this.synthesizer = synthesizer;

        envelope = new SfzEnvelope(synthesizer.SampleRate, synthesizer.BlockSize);
        filterLeft = new SfzFilter(synthesizer.SampleRate);
        filterRight = new SfzFilter(synthesizer.SampleRate);
        filter2Left = new SfzFilter(synthesizer.SampleRate);
        filter2Right = new SfzFilter(synthesizer.SampleRate);
        oscillator = new SfzOscillator();
        blockSeconds = (float)synthesizer.BlockSize / synthesizer.SampleRate;

        blockLeft = new float[synthesizer.BlockSize];
        blockRight = new float[synthesizer.BlockSize];
    }

    public void Start(SfzRegion newRegion, SfzSampleData newSample, SfzChannel newChannelState, int newChannel, int newKey, int newVelocity, float rtDecayGain)
    {
        region = newRegion;
        sample = newSample;
        channelState = newChannelState;
        channel = newChannel;
        key = newKey;
        velocity = newVelocity;

        loopMode = region.LoopMode ?? SfzLoopMode.NoLoop;
        oneShot = loopMode == SfzLoopMode.OneShot;

        // The per-voice modulation sources, latched for the voice's lifetime. The random draws come
        // from the synthesizer's seeded random, so identical input still renders identically. The
        // voice starts before its note's KeyDown, so the channel's last note-on is the PREVIOUS note,
        // which is exactly what the key-delta sources measure from.
        alternate = synthesizer.AlternateFlag ? 1f : 0f;
        keyDelta = channelState.LastNoteOnKey >= 0 ? key - channelState.LastNoteOnKey : 0;
        unipolarRandom = synthesizer.NextRandomValue();
        bipolarRandom = synthesizer.NextRandomValue() * 2f - 1f;

        // Envelope stage times latch their CC modulation at note start (unless ampeg_dynamic retimes
        // them later); vel2 opcodes add their full value at velocity 127.
        var velocityFraction = velocity / 127f;
        envelope.Start(
            Math.Max(0f, EnvelopeDelay(velocityFraction)),
            Math.Max(0f, EnvelopeAttack(velocityFraction)),
            Math.Max(0f, EnvelopeHold(velocityFraction)),
            Math.Max(0f, EnvelopeDecay(velocityFraction)),
            Math.Clamp(EnvelopeSustain(velocityFraction), 0f, 100f) / 100f,
            Math.Max(0f, EnvelopeRelease(velocityFraction)),
            region.AmpegAttackShape,
            region.AmpegDecayShape,
            region.AmpegReleaseShape);

        // offset CC modulation and offset_random latch at note start, before the oscillator sees them.
        var offset = region.Offset + (long)SumCc(region.OffsetCc);
        if (region.OffsetRandom > 0)
        {
            offset += (long)(synthesizer.NextRandomValue() * region.OffsetRandom);
        }

        oscillator.Start(
            sample,
            loopMode,
            Math.Max(0, offset),
            region.End ?? -1,
            region.LoopStart ?? 0,
            region.LoopEnd ?? (sample.Frames - 1));

        var delaySeconds = region.Delay;
        if (region.DelayRandom > 0f)
        {
            delaySeconds += synthesizer.NextRandomValue() * region.DelayRandom;
        }
        delayFramesRemaining = (long)(delaySeconds * synthesizer.SampleRate);

        hasFilter = region.Cutoff.HasValue;
        if (hasFilter)
        {
            filterLeft.Start(region.FilType);
            filterRight.Start(region.FilType);

            // Key and velocity tracking are fixed for the voice's lifetime, and fil_random draws once;
            // only CC, LFO, envelope and variator movement retunes the filter after this.
            var trackingCents =
                region.FilKeytrack * (key - region.FilKeycenter) +
                region.FilVeltrack * velocityFraction;
            if (region.FilRandom > 0f)
            {
                trackingCents += synthesizer.NextRandomValue() * region.FilRandom;
            }
            baseCutoff = region.Cutoff.Value * SoundFontMath.CentsToMultiplyingFactor(trackingCents);
        }

        hasFilter2 = region.Cutoff2.HasValue;
        if (hasFilter2)
        {
            filter2Left.Start(region.Fil2Type);
            filter2Right.Start(region.Fil2Type);
            baseCutoff2 = region.Cutoff2.Value *
                SoundFontMath.CentsToMultiplyingFactor(region.Fil2Keytrack * (key - region.Fil2Keycenter));
        }

        pitchStaticCents =
            (key - region.PitchKeycenter) * region.PitchKeytrack +
            100f * region.Transpose +
            region.Tune +
            region.ScopeTune +
            region.PitchVeltrack * velocityFraction;
        sampleRateRatio = (double)sample.SampleRate / synthesizer.SampleRate;

        staticGain = VelocityGain(velocity) * (region.Amplitude / 100f) * rtDecayGain;

        staticVolumeDb = region.Volume + region.ScopeVolume +
            region.AmpKeytrack * (key - region.AmpKeycenter);
        if (region.AmpRandom > 0f)
        {
            staticVolumeDb += synthesizer.NextRandomValue() * region.AmpRandom;
        }

        staticPan = region.Pan + region.PanKeytrack * (key - region.PanKeycenter);

        // The key and velocity crossfades are properties of the note, fixed for the voice's lifetime;
        // controller crossfades stay live and multiply in per block.
        staticXfGain =
            XfInGain(velocity, region.XfInLoVel, region.XfInHiVel, region.XfVelCurve) *
            XfOutGain(velocity, region.XfOutLoVel, region.XfOutHiVel, region.XfVelCurve) *
            XfInGain(key, region.XfInLoKey, region.XfInHiKey, region.XfKeyCurve) *
            XfOutGain(key, region.XfOutLoKey, region.XfOutHiKey, region.XfKeyCurve);

        StartModulationUnits();

        previousMixGainLeft = 0;
        previousMixGainRight = 0;
        currentMixGainLeft = 0;
        currentMixGainRight = 0;

        voiceState = VoiceState.Playing;
        voiceLength = 0;
    }

    public void End()
    {
        // A one-shot region plays through to the end of its sample whatever the note does.
        if (oneShot)
        {
            return;
        }

        if (voiceState == VoiceState.Playing)
        {
            voiceState = VoiceState.ReleaseRequested;
        }
    }

    // An off-group choke. Fast is the hi-hat behaviour: a milliseconds-long fade regardless of the
    // region's release time. Time fades over the victim's off_time with its off_shape. Normal runs
    // the ordinary release. All bypass the hold pedal and the minimum-duration wait - a choked voice
    // is being silenced by another voice, not by the player.
    public void Choke(SfzOffMode mode)
    {
        if (voiceState == VoiceState.Released && mode == SfzOffMode.Normal)
        {
            return;
        }

        switch (mode)
        {
            case SfzOffMode.Fast:
                envelope.ReleaseFast();
                break;

            case SfzOffMode.Time:
                envelope.ReleaseTimed(region.OffTime, region.OffShape);
                break;

            default:
                envelope.Release();
                break;
        }

        ReleaseModulationUnits();
        oscillator.Release(loopMode);
        voiceState = VoiceState.Released;
    }

    public void Kill()
    {
        staticGain = 0;
    }

    public bool Process()
    {
        if (staticGain < SoundFontMath.NonAudible)
        {
            return false;
        }

        // The region delay gate: silent blocks that do not consume the sample. A voice released
        // before its sample ever started has nothing left to say.
        if (delayFramesRemaining >= synthesizer.BlockSize)
        {
            if (voiceState != VoiceState.Playing)
            {
                return false;
            }

            delayFramesRemaining -= synthesizer.BlockSize;
            Array.Clear(blockLeft, 0, blockLeft.Length);
            Array.Clear(blockRight, 0, blockRight.Length);
            previousMixGainLeft = 0;
            previousMixGainRight = 0;
            currentMixGainLeft = 0;
            currentMixGainRight = 0;
            voiceLength += synthesizer.BlockSize;
            return true;
        }
        delayFramesRemaining = 0;

        ReleaseIfNecessary();

        if (region.AmpegDynamic)
        {
            RetimeEnvelope();
        }

        if (!envelope.Process())
        {
            return false;
        }

        var lfoPitchCents = 0f;
        var lfoVolumeDb = 0f;
        var lfoCutoffCents = 0f;
        var lfoPan = 0f;
        AdvanceLfos(ref lfoPitchCents, ref lfoVolumeDb, ref lfoCutoffCents, ref lfoPan);

        var egPitchCents = 0f;
        var egCutoffHz = 0f;
        var egAmplitude = 1f;
        AdvanceEnvelopeUnits(ref egPitchCents, ref egCutoffHz, ref egAmplitude);

        var cents = pitchStaticCents + BendCents() + SumCc(region.TuneCc) + lfoPitchCents + egPitchCents;
        var pitchRatio = sampleRateRatio * Math.Pow(2, cents / 1200.0);

        if (!oscillator.Process(blockLeft, blockRight, pitchRatio))
        {
            return false;
        }

        if (hasFilter)
        {
            var modulationCents = SumCc(region.CutoffCc) + lfoCutoffCents + VariatorCutoffCents();
            var cutoff = baseCutoff * SoundFontMath.CentsToMultiplyingFactor(modulationCents) + egCutoffHz;
            var resonance = region.Resonance + SumCc(region.ResonanceCc);
            filterLeft.SetCutoff(cutoff, resonance);
            filterLeft.Process(blockLeft);

            if (oscillator.IsStereo)
            {
                filterRight.SetCutoff(cutoff, resonance);
                filterRight.Process(blockRight);
            }
        }

        if (hasFilter2)
        {
            var cutoff = baseCutoff2 * SoundFontMath.CentsToMultiplyingFactor(SumCc(region.Cutoff2Cc));
            var resonance = region.Resonance2 + SumCc(region.Resonance2Cc);
            filter2Left.SetCutoff(cutoff, resonance);
            filter2Left.Process(blockLeft);

            if (oscillator.IsStereo)
            {
                filter2Right.SetCutoff(cutoff, resonance);
                filter2Right.Process(blockRight);
            }
        }

        ProcessEq();

        if (oscillator.IsStereo)
        {
            ApplyWidth();
        }

        previousMixGainLeft = currentMixGainLeft;
        previousMixGainRight = currentMixGainRight;

        var volumeDb = staticVolumeDb + SumCc(region.VolumeCc) + lfoVolumeDb;
        var mixGain = staticGain * SoundFontMath.DecibelsToLinear(volumeDb) *
            AmplitudeCcFactor() * egAmplitude * envelope.Value *
            staticXfGain * CcXfGain();

        var pan = Math.Clamp(staticPan + SumCc(region.PanCc) + lfoPan, -100f, 100f);
        if (oscillator.IsStereo)
        {
            // Stereo sources are balanced rather than re-panned: the center position leaves both
            // channels untouched.
            currentMixGainLeft = mixGain * Math.Min(1f, 1f - pan / 100f);
            currentMixGainRight = mixGain * Math.Min(1f, 1f + pan / 100f);
        }
        else
        {
            // Mono sources use the equal-power law, the same -3 dB center as the SoundFont voice.
            var angle = (MathF.PI / 400f) * (pan + 100f);
            currentMixGainLeft = mixGain * MathF.Cos(angle);
            currentMixGainRight = mixGain * MathF.Sin(angle);
        }

        if (voiceLength == 0)
        {
            previousMixGainLeft = currentMixGainLeft;
            previousMixGainRight = currentMixGainRight;
        }

        voiceLength += synthesizer.BlockSize;

        return true;
    }

    public float Priority
    {
        get
        {
            if (staticGain < SoundFontMath.NonAudible)
            {
                return 0f;
            }

            return envelope.Priority;
        }
    }

    public float[] BlockLeft => blockLeft;
    public float[] BlockRight => oscillator.IsStereo ? blockRight : blockLeft;

    public float PreviousMixGainLeft => previousMixGainLeft;
    public float PreviousMixGainRight => previousMixGainRight;
    public float CurrentMixGainLeft => currentMixGainLeft;
    public float CurrentMixGainRight => currentMixGainRight;

    public SfzRegion Region => region;
    public int Channel => channel;
    public int Key => key;
    public int Velocity => velocity;
    public int VoiceLength => voiceLength;

    // Which note-on (or CC-trigger) event started this voice. Off-group chokes spare voices born of
    // the same event, so layered regions that share a group do not silence each other at birth.
    public long EventStamp { get; set; }

    // ---- modulation units ---------------------------------------------------

    private void StartModulationUnits()
    {
        var lfos = region.Lfos;
        if (lfos.Count > 0)
        {
            if (lfoUnits == null || lfoUnits.Length < lfos.Count)
            {
                lfoUnits = new SfzLfoUnit[lfos.Count];
                for (var i = 0; i < lfoUnits.Length; i++)
                {
                    lfoUnits[i] = new SfzLfoUnit();
                }
            }

            for (var i = 0; i < lfos.Count; i++)
            {
                var lfo = lfos[i];
                var needsRandom = lfo.Wave == SfzLfoWave.RandomSampleHold;
                var seed = needsRandom ? (uint)(synthesizer.NextRandomValue() * uint.MaxValue) : 1u;
                lfoUnits[i].Start(
                    lfo,
                    lfo.Delay + SumCc(lfo.DelayCc),
                    lfo.Fade + SumCc(lfo.FadeCc),
                    seed);
            }
        }

        if (region.FilEg != null)
        {
            filEgUnit ??= new SfzModEnvelopeUnit();
            StartModEnvelopeUnit(filEgUnit, region.FilEg);
        }

        if (region.PitchEg != null)
        {
            pitchEgUnit ??= new SfzModEnvelopeUnit();
            StartModEnvelopeUnit(pitchEgUnit, region.PitchEg);
        }

        var flexEgs = region.FlexEgs;
        if (flexEgs.Count > 0)
        {
            if (flexEgUnits == null || flexEgUnits.Length < flexEgs.Count)
            {
                flexEgUnits = new SfzFlexEgUnit[flexEgs.Count];
                for (var i = 0; i < flexEgUnits.Length; i++)
                {
                    flexEgUnits[i] = new SfzFlexEgUnit();
                }
            }

            for (var i = 0; i < flexEgs.Count; i++)
            {
                flexEgUnits[i].Start(flexEgs[i]);
            }
        }

        var eqBands = region.EqBands;
        if (eqBands.Count > 0)
        {
            if (eqLeft == null || eqLeft.Length < eqBands.Count)
            {
                eqLeft = new SfzEqFilter[eqBands.Count];
                eqRight = new SfzEqFilter[eqBands.Count];
                eqActive = new bool[eqBands.Count];
                for (var i = 0; i < eqLeft.Length; i++)
                {
                    eqLeft[i] = new SfzEqFilter(synthesizer.SampleRate);
                    eqRight[i] = new SfzEqFilter(synthesizer.SampleRate);
                }
            }

            for (var i = 0; i < eqBands.Count; i++)
            {
                eqLeft[i].Start();
                eqRight[i].Start();
                eqActive[i] = false;
            }
        }

        var amplitudeCc = region.AmplitudeCc;
        var needsSmoothing = false;
        for (var i = 0; i < amplitudeCc.Count; i++)
        {
            if (amplitudeCc[i].SmoothMilliseconds > 0f)
            {
                needsSmoothing = true;
                break;
            }
        }

        if (needsSmoothing)
        {
            if (amplitudeSmoothState == null || amplitudeSmoothState.Length < amplitudeCc.Count)
            {
                amplitudeSmoothState = new float[amplitudeCc.Count];
            }

            // Start each smoother at its current target so the note begins at the right level
            // instead of fading in from stale state.
            for (var i = 0; i < amplitudeCc.Count; i++)
            {
                amplitudeSmoothState[i] = CurveValue(amplitudeCc[i]);
            }
        }
        else
        {
            amplitudeSmoothState = null;
        }
    }

    private void StartModEnvelopeUnit(SfzModEnvelopeUnit unit, SfzModEnvelope model)
    {
        unit.Start(
            Math.Max(0f, model.Delay + SumCc(model.DelayCc)),
            Math.Max(0f, model.Attack + SumCc(model.AttackCc)),
            Math.Max(0f, model.Hold + SumCc(model.HoldCc)),
            Math.Max(0f, model.Decay + SumCc(model.DecayCc)),
            Math.Clamp(model.Sustain + SumCc(model.SustainCc), 0f, 100f) / 100f,
            Math.Max(0f, model.Release + SumCc(model.ReleaseCc)));
    }

    private void ReleaseModulationUnits()
    {
        if (region.FilEg != null)
        {
            filEgUnit.Release();
        }

        if (region.PitchEg != null)
        {
            pitchEgUnit.Release();
        }

        var flexEgs = region.FlexEgs;
        for (var i = 0; i < flexEgs.Count; i++)
        {
            flexEgUnits[i].Release();
        }
    }

    private void AdvanceLfos(ref float pitchCents, ref float volumeDb, ref float cutoffCents, ref float pan)
    {
        var lfos = region.Lfos;
        if (lfos.Count == 0)
        {
            return;
        }

        for (var i = 0; i < lfos.Count; i++)
        {
            var lfo = lfos[i];

            var frequency = lfo.Frequency + SumCc(lfo.FrequencyCc);

            // Cross-LFO frequency modulation: the source's value from this block if it already
            // advanced, else its previous-block value. Depth is Hz.
            var frequencyMods = lfo.FrequencyLfoModulations;
            for (var m = 0; m < frequencyMods.Count; m++)
            {
                var mod = frequencyMods[m];
                var sourceIndex = IndexOfLfoNumber(lfos, mod.SourceNumber);
                if (sourceIndex >= 0)
                {
                    frequency += (mod.Depth + SumCc(mod.DepthCc)) * lfoUnits[sourceIndex].Value;
                }
            }

            var value = lfoUnits[i].Advance(blockSeconds, frequency);
            if (value == 0f)
            {
                continue;
            }

            var pitchDepth = lfo.Pitch + SumCc(lfo.PitchCc);
            if (pitchDepth != 0f)
            {
                pitchCents += value * pitchDepth;
            }

            var volumeDepth = lfo.Volume + SumCc(lfo.VolumeCc);
            if (volumeDepth != 0f)
            {
                volumeDb += value * volumeDepth;
            }

            var cutoffDepth = lfo.Cutoff + SumCc(lfo.CutoffCc);
            if (cutoffDepth != 0f)
            {
                cutoffCents += value * cutoffDepth;
            }

            var panDepth = lfo.Pan + SumCc(lfo.PanCc);
            if (panDepth != 0f)
            {
                pan += value * panDepth;
            }
        }
    }

    private static int IndexOfLfoNumber(IReadOnlyList<SfzLfo> lfos, int number)
    {
        for (var i = 0; i < lfos.Count; i++)
        {
            if (lfos[i].Number == number)
            {
                return i;
            }
        }

        return -1;
    }

    private void AdvanceEnvelopeUnits(ref float pitchCents, ref float cutoffHz, ref float amplitude)
    {
        var velocityFraction = velocity / 127f;

        if (region.FilEg != null)
        {
            var level = filEgUnit.Advance(blockSeconds);
            var depth = region.FilEg.Depth + region.FilEg.Vel2Depth * velocityFraction + SumCc(region.FilEg.DepthCc);
            if (depth != 0f && level != 0f && hasFilter)
            {
                // The filter envelope's depth is cents; convert against the base cutoff so it composes
                // with the multiplicative cents modulation the filter already applies.
                cutoffHz += baseCutoff * (SoundFontMath.CentsToMultiplyingFactor(level * depth) - 1f);
            }
        }

        if (region.PitchEg != null)
        {
            var level = pitchEgUnit.Advance(blockSeconds);
            var depth = region.PitchEg.Depth + region.PitchEg.Vel2Depth * velocityFraction + SumCc(region.PitchEg.DepthCc);
            pitchCents += level * depth;
        }

        var flexEgs = region.FlexEgs;
        for (var i = 0; i < flexEgs.Count; i++)
        {
            var eg = flexEgs[i];
            var level = flexEgUnits[i].Advance(blockSeconds);

            var pitchDepth = eg.Pitch + SumCc(eg.PitchCc);
            if (pitchDepth != 0f)
            {
                pitchCents += level * pitchDepth;
            }

            var cutoffDepth = eg.Cutoff + SumCc(eg.CutoffCc);
            if (cutoffDepth != 0f)
            {
                cutoffHz += level * cutoffDepth;
            }

            var amplitudeDepth = eg.Amplitude + SumCc(eg.AmplitudeCc);
            if (amplitudeDepth != 0f)
            {
                // Amplitude depth is a percentage, the fader semantics of amplitude_onccN.
                amplitude *= Math.Max(0f, level * amplitudeDepth / 100f);
            }
        }
    }

    private float VariatorCutoffCents()
    {
        var variators = region.Variators;
        if (variators.Count == 0)
        {
            return 0f;
        }

        var cents = 0f;
        for (var i = 0; i < variators.Count; i++)
        {
            var variator = variators[i];
            if (variator.Cutoff != 0f)
            {
                cents += variator.Cutoff * VariatorValue(variator);
            }
        }

        return cents;
    }

    private float VariatorValue(SfzVariator variator)
    {
        var inputs = variator.Inputs;
        if (inputs.Count == 0)
        {
            return 0f;
        }

        if (variator.Multiply)
        {
            var product = 1f;
            for (var i = 0; i < inputs.Count; i++)
            {
                product *= inputs[i].Depth * CurveValue(inputs[i]);
            }

            return product;
        }

        var sum = 0f;
        for (var i = 0; i < inputs.Count; i++)
        {
            sum += inputs[i].Depth * CurveValue(inputs[i]);
        }

        return Math.Clamp(sum, 0f, 1f);
    }

    private void ProcessEq()
    {
        var eqBands = region.EqBands;
        if (eqBands.Count == 0)
        {
            return;
        }

        var stereo = oscillator.IsStereo;

        for (var i = 0; i < eqBands.Count; i++)
        {
            var band = eqBands[i];

            var gain = band.Gain + SumCc(band.GainCc) + VariatorEqGain(band.Number) + LfoEqGain(band.Number);

            if (MathF.Abs(gain) < 0.01f)
            {
                eqActive[i] = false;
                continue;
            }

            var frequency = band.Frequency + SumCc(band.FrequencyCc) +
                VariatorEqFrequency(band.Number) + LfoEqFrequency(band.Number);
            var bandwidth = band.Bandwidth + SumCc(band.BandwidthCc);

            if (!eqActive[i])
            {
                // The band just switched on: clear stale filter state from an earlier activation.
                eqLeft[i].ClearBuffer();
                eqRight[i].ClearBuffer();
                eqActive[i] = true;
            }

            eqLeft[i].SetPeaking(frequency, bandwidth, gain);
            eqLeft[i].Process(blockLeft);

            if (stereo)
            {
                eqRight[i].SetPeaking(frequency, bandwidth, gain);
                eqRight[i].Process(blockRight);
            }
        }
    }

    private float VariatorEqGain(int band)
    {
        var variators = region.Variators;
        var gain = 0f;
        for (var i = 0; i < variators.Count; i++)
        {
            var depth = variators[i].EqGain[band - 1];
            if (depth != 0f)
            {
                gain += depth * VariatorValue(variators[i]);
            }
        }

        return gain;
    }

    private float VariatorEqFrequency(int band)
    {
        var variators = region.Variators;
        var frequency = 0f;
        for (var i = 0; i < variators.Count; i++)
        {
            var depth = variators[i].EqFrequency[band - 1];
            if (depth != 0f)
            {
                frequency += depth * VariatorValue(variators[i]);
            }
        }

        return frequency;
    }

    private float LfoEqGain(int band)
    {
        var lfos = region.Lfos;
        var gain = 0f;
        for (var i = 0; i < lfos.Count; i++)
        {
            var targets = lfos[i].EqTargets;
            for (var t = 0; t < targets.Count; t++)
            {
                if (targets[t].Band == band)
                {
                    var depth = targets[t].Gain + SumCc(targets[t].GainCc);
                    if (depth != 0f)
                    {
                        gain += depth * lfoUnits[i].Value;
                    }
                }
            }
        }

        return gain;
    }

    private float LfoEqFrequency(int band)
    {
        var lfos = region.Lfos;
        var frequency = 0f;
        for (var i = 0; i < lfos.Count; i++)
        {
            var targets = lfos[i].EqTargets;
            for (var t = 0; t < targets.Count; t++)
            {
                if (targets[t].Band == band)
                {
                    var depth = targets[t].Frequency + SumCc(targets[t].FrequencyCc);
                    if (depth != 0f)
                    {
                        frequency += depth * lfoUnits[i].Value;
                    }
                }
            }
        }

        return frequency;
    }

    // Stereo width, mid/side: 100 leaves the image, 0 collapses to mono, negative swaps the sides.
    private void ApplyWidth()
    {
        var width = Math.Clamp(region.Width + SumCc(region.WidthCc), -100f, 100f);
        if (width == 100f)
        {
            return;
        }

        var side = width / 100f;
        for (var t = 0; t < blockLeft.Length; t++)
        {
            var mid = 0.5f * (blockLeft[t] + blockRight[t]);
            var sideValue = 0.5f * (blockLeft[t] - blockRight[t]) * side;
            blockLeft[t] = mid + sideValue;
            blockRight[t] = mid - sideValue;
        }
    }

    // ---- envelope helpers ---------------------------------------------------

    private float EnvelopeDelay(float velocityFraction) =>
        region.AmpegDelay + SumCc(region.AmpegDelayCc) + region.AmpegVel2Delay * velocityFraction;

    private float EnvelopeAttack(float velocityFraction) =>
        region.AmpegAttack + SumCc(region.AmpegAttackCc) + region.AmpegVel2Attack * velocityFraction;

    private float EnvelopeHold(float velocityFraction) =>
        region.AmpegHold + SumCc(region.AmpegHoldCc) + region.AmpegVel2Hold * velocityFraction;

    private float EnvelopeDecay(float velocityFraction) =>
        region.AmpegDecay + SumCc(region.AmpegDecayCc) + region.AmpegVel2Decay * velocityFraction;

    private float EnvelopeSustain(float velocityFraction) =>
        region.AmpegSustain + SumCc(region.AmpegSustainCc) + region.AmpegVel2Sustain * velocityFraction;

    private float EnvelopeRelease(float velocityFraction) =>
        region.AmpegRelease + SumCc(region.AmpegReleaseCc) + region.AmpegVel2Release * velocityFraction;

    // ampeg_dynamic=1: stage times and sustain follow their CC modulation while the note plays.
    private void RetimeEnvelope()
    {
        if (region.AmpegDelayCc.Count == 0 && region.AmpegAttackCc.Count == 0 &&
            region.AmpegHoldCc.Count == 0 && region.AmpegDecayCc.Count == 0 &&
            region.AmpegSustainCc.Count == 0 && region.AmpegReleaseCc.Count == 0)
        {
            return;
        }

        var velocityFraction = velocity / 127f;
        envelope.Retime(
            Math.Max(0f, EnvelopeDelay(velocityFraction)),
            Math.Max(0f, EnvelopeAttack(velocityFraction)),
            Math.Max(0f, EnvelopeHold(velocityFraction)),
            Math.Max(0f, EnvelopeDecay(velocityFraction)),
            Math.Clamp(EnvelopeSustain(velocityFraction), 0f, 100f) / 100f,
            Math.Max(0f, EnvelopeRelease(velocityFraction)));
    }

    private void ReleaseIfNecessary()
    {
        if (voiceLength < synthesizer.MinimumVoiceDuration)
        {
            return;
        }

        if (voiceState == VoiceState.ReleaseRequested && !channelState.IsSustainDown(region.SustainCc))
        {
            envelope.Release();
            ReleaseModulationUnits();
            oscillator.Release(loopMode);

            voiceState = VoiceState.Released;
        }
    }

    private float BendCents()
    {
        var bend = channelState.PitchBend;
        if (bend > 0f)
        {
            return bend * region.BendUp;
        }
        if (bend < 0f)
        {
            return -bend * region.BendDown;
        }
        return 0f;
    }

    // ---- crossfades ---------------------------------------------------------

    private static float ApplyXfCurve(float gain, SfzXfCurve curve) =>
        curve == SfzXfCurve.Gain ? gain : MathF.Sqrt(gain);

    private static float XfInGain(int position, int? low, int? high, SfzXfCurve curve)
    {
        if (!low.HasValue && !high.HasValue)
        {
            return 1f;
        }

        var lo = low ?? high.Value;
        var hi = high ?? low.Value;
        if (position < lo)
        {
            return 0f;
        }

        if (position >= hi || hi <= lo)
        {
            return 1f;
        }

        return ApplyXfCurve((float)(position - lo) / (hi - lo), curve);
    }

    private static float XfOutGain(int position, int? low, int? high, SfzXfCurve curve)
    {
        if (!low.HasValue && !high.HasValue)
        {
            return 1f;
        }

        var lo = low ?? high.Value;
        var hi = high ?? low.Value;
        if (position <= lo || hi <= lo)
        {
            return 1f;
        }

        if (position > hi)
        {
            return 0f;
        }

        return ApplyXfCurve(1f - (float)(position - lo) / (hi - lo), curve);
    }

    // The controller crossfades, evaluated per block against live controller values.
    private float CcXfGain()
    {
        var inRanges = region.XfInCcRanges;
        var outRanges = region.XfOutCcRanges;
        if (inRanges.Count == 0 && outRanges.Count == 0)
        {
            return 1f;
        }

        var gain = 1f;
        for (var i = 0; i < inRanges.Count; i++)
        {
            var range = inRanges[i];
            gain *= XfInGain(channelState.GetCcMidiValue(range.CcNumber), range.Low, range.High, region.XfCcCurve);
        }

        for (var i = 0; i < outRanges.Count; i++)
        {
            var range = outRanges[i];
            gain *= XfOutGain(channelState.GetCcMidiValue(range.CcNumber), range.Low, range.High, region.XfCcCurve);
        }

        return gain;
    }

    // ---- modulation sources -------------------------------------------------

    // The additive CC modulation sum: depth x source, in the target's own units. Sources 0-127 are
    // channel controllers through the modulation's curve; 128 and above are the extended sources.
    private float SumCc(IReadOnlyList<SfzCcModulation> modulations)
    {
        var count = modulations.Count;
        if (count == 0)
        {
            return 0f;
        }

        var sum = 0f;
        for (var i = 0; i < count; i++)
        {
            sum += Contribution(modulations[i]);
        }

        return sum;
    }

    private float Contribution(SfzCcModulation modulation)
    {
        switch (modulation.CcNumber)
        {
            case 136: // bipolar per-voice random: raw bipolar value, no curve
                return modulation.Depth * bipolarRandom;

            case 140: // key delta in half-steps: raw signed value, no curve
                return modulation.Depth * keyDelta;

            case 141: // key delta, absolute
                return modulation.Depth * Math.Abs(keyDelta);

            default:
                return modulation.Depth * CurveValue(modulation);
        }
    }

    // The curved 0..1 source value for a modulation - the shared piece of the additive and
    // multiplicative paths.
    private float CurveValue(SfzCcModulation modulation)
    {
        var curve = synthesizer.Instrument.GetCurve(modulation.CurveIndex);
        return curve.Evaluate(SourceValue(modulation.CcNumber));
    }

    private float SourceValue(int cc)
    {
        switch (cc)
        {
            case 128: // pitch bend, centered at 0.5
                return 0.5f * (channelState.PitchBend + 1f);

            case 131: // note-on velocity
                return velocity / 127f;

            case 133: // note number
                return key / 127f;

            case 134: // key gate: any key held
                return channelState.HeldKeyCount > 0 ? 1f : 0f;

            case 135: // unipolar per-voice random
                return unipolarRandom;

            case 137: // alternate: flips every note-on
                return alternate;

            default: // real controllers, plus stored extended ones (129/130 aftertouch)
                return channelState.GetCc(cc);
        }
    }

    // Amplitude CC modulation is multiplicative: each controller contributes depth% x curve(cc) as a
    // gain factor, so a controller sitting at zero silences the region. That is how SFZ libraries
    // wire "CC11 is the volume fader". A negative depth inverts the signal, per ARIA. Entries with
    // amplitude_smoothccN glide toward the controller's position instead of jumping.
    private float AmplitudeCcFactor()
    {
        var modulations = region.AmplitudeCc;
        var count = modulations.Count;
        if (count == 0)
        {
            return 1f;
        }

        var factor = 1f;
        for (var i = 0; i < count; i++)
        {
            var modulation = modulations[i];
            var curveValue = CurveValue(modulation);

            if (amplitudeSmoothState != null && modulation.SmoothMilliseconds > 0f)
            {
                var coefficient = 1f - MathF.Exp(-blockSeconds * 1000f / modulation.SmoothMilliseconds);
                amplitudeSmoothState[i] += (curveValue - amplitudeSmoothState[i]) * coefficient;
                curveValue = amplitudeSmoothState[i];
            }

            factor *= (modulation.Depth / 100f) * curveValue;
        }

        return factor;
    }

    private float VelocityGain(int noteVelocity)
    {
        // amp_veltrack's own CC modulation latches at note start.
        var track = Math.Clamp(region.AmpVeltrack + SumCc(region.AmpVeltrackCc), -100f, 100f) / 100f;
        if (track == 0f)
        {
            return 1f;
        }

        var effectiveVelocity = track >= 0f ? noteVelocity : 127 - noteVelocity;
        var amount = Math.Abs(track);

        float curveValue;
        if (region.AmpVelcurve.Count > 0)
        {
            curveValue = InterpolateVelcurve(region.AmpVelcurve, effectiveVelocity);
        }
        else
        {
            // The SFZ default velocity response: Amplitude(dB) = 20 log(127^2 / velocity^2), which in
            // linear terms is (velocity / 127) squared.
            var normalized = effectiveVelocity / 127f;
            curveValue = normalized * normalized;
        }

        return 1f - amount + amount * curveValue;
    }

    private static float InterpolateVelcurve(IReadOnlyDictionary<int, float> points, int velocity)
    {
        if (points.TryGetValue(velocity, out var exact))
        {
            return Math.Clamp(exact, 0f, 1f);
        }

        // Linear interpolation between the nearest defined points, anchored at (0, 0) and (127, 1)
        // when the file does not define the ends - the standard reading of amp_velcurve_N.
        var lowerVelocity = 0;
        var lowerValue = 0f;
        var upperVelocity = 127;
        var upperValue = 1f;

        foreach (var pair in points)
        {
            if (pair.Key < velocity && pair.Key > lowerVelocity)
            {
                lowerVelocity = pair.Key;
                lowerValue = pair.Value;
            }
            else if (pair.Key > velocity && pair.Key < upperVelocity)
            {
                upperVelocity = pair.Key;
                upperValue = pair.Value;
            }
        }

        if (upperVelocity == lowerVelocity)
        {
            return Math.Clamp(lowerValue, 0f, 1f);
        }

        var t = (float)(velocity - lowerVelocity) / (upperVelocity - lowerVelocity);
        return Math.Clamp(lowerValue + t * (upperValue - lowerValue), 0f, 1f);
    }

    private enum VoiceState
    {
        Playing,
        ReleaseRequested,
        Released
    }
}
