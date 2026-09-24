using System;
using CodeBrix.Audio.ModestSynth.Oscillators;

namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// One sounding note of the General MIDI synthesizer.
//
// It is the voice ModestSynth did not have: up to three layered components, each with its own
// oscillator, level, tuning and envelope; a resonant state-variable filter with its own envelope,
// key tracking and velocity-to-cutoff; exponential amplitude and filter envelopes; a pitch envelope;
// a delayed low-frequency oscillator routed to pitch, level and cutoff; and velocity and key scaling
// that reach past loudness into brightness and speed.
//
// A voice belongs to the SHARED pool and plays whichever voicing it was started with, borrowing that
// voicing's oscillators from its GmProgramRuntime. Everything a render needs is allocated when the
// voice is built, so rendering allocates nothing; the only thing that ever allocates is the first
// few notes of a voicing, building the oscillators its pool then recycles forever - and not even
// those when the voicing was PREPARED (GeneralMidiSynthesizer.Prepare), which builds them at
// creation instead. The order this method rents oscillators in is what preparation reproduces to
// stay bit-identical, so change it only together with GmProgramRuntime.Prebuild.
internal sealed class GmVoice
{
    private const double CentsPerSemitone = 100.0;

    private readonly int sampleRate;
    private readonly int blockSize;

    private readonly IModestVoiceOscillator[] oscillators = new IModestVoiceOscillator[GmVoiceSpec.MaximumOscillators];
    private readonly int[] oscillatorLayers = new int[GmVoiceSpec.MaximumOscillators];
    private readonly double[] oscillatorCents = new double[GmVoiceSpec.MaximumOscillators];
    private readonly float[] oscillatorGainLeft = new float[GmVoiceSpec.MaximumOscillators];
    private readonly float[] oscillatorGainRight = new float[GmVoiceSpec.MaximumOscillators];
    private readonly double[] oscillatorFrequency = new double[GmVoiceSpec.MaximumOscillators];

    private readonly GmEnvelope[] layerEnvelopes = new GmEnvelope[GmVoiceSpec.MaximumLayers];
    private readonly bool[] layerHasEnvelope = new bool[GmVoiceSpec.MaximumLayers];

    private readonly GmEnvelope amplitude;
    private readonly GmEnvelope filterEnvelope;
    private readonly GmFilter filterLeft;
    private readonly GmFilter filterRight;
    private readonly GmLfo lfo;

    private readonly float[] scratch;
    private readonly float[] sumLeft;
    private readonly float[] sumRight;
    private readonly double[] layerGains;

    private GmVoiceSpec spec;
    private GmProgramRuntime runtime;
    private GmVoiceAdjustment adjustment;
    private int oscillatorCount;
    private int layerCount;
    private bool stereo;
    private bool ownFilterEnvelope;
    private double voiceKey;
    private double velocityGain;
    private double levelGain;
    private double timeScale;
    private double velocityCutoffOctaves;
    private double pitchEnvelopeCoefficient;
    private double pitchEnvelopeValue;
    private float panLeft = 1f;
    private float panRight = 1f;

    internal GmVoice(int sampleRate, int blockSize)
    {
        this.sampleRate = sampleRate;
        this.blockSize = blockSize;

        amplitude = new GmEnvelope(sampleRate);
        filterEnvelope = new GmEnvelope(sampleRate);
        filterLeft = new GmFilter(sampleRate);
        filterRight = new GmFilter(sampleRate);
        lfo = new GmLfo(sampleRate);

        for (int i = 0; i < layerEnvelopes.Length; i++)
        {
            layerEnvelopes[i] = new GmEnvelope(sampleRate);
        }

        scratch = new float[blockSize];
        sumLeft = new float[blockSize];
        sumRight = new float[blockSize];
        layerGains = new double[blockSize];
    }

    internal bool IsActive { get; private set; }

    internal bool IsKeyDown { get; private set; }

    internal bool IsSustained { get; set; }

    internal int Channel { get; private set; }

    internal int Key { get; private set; }

    internal int ExclusiveGroup { get; private set; }

    internal long StartStamp { get; private set; }

    // How much of this voice goes to the shared reverb and chorus buses. It is settled at note-on -
    // from the voicing, from the consumer's adjustment, or from a CC 91 or CC 93 the music sent -
    // because a voice belongs to a VOICING while a controller belongs to a channel, and a program
    // change under a sustained note must not re-wet the note that is already sounding.
    internal double ReverbSend { get; private set; }

    internal double ChorusSend { get; private set; }

    internal double Loudness => amplitude.Level * levelGain * velocityGain;

    internal void Start(
        int channel,
        int key,
        int velocity,
        long stamp,
        GmProgramRuntime programRuntime,
        in GmVoiceAdjustment voiceAdjustment,
        double reverbSend,
        double chorusSend,
        uint phaseSeed)
    {
        ReturnOscillators();

        runtime = programRuntime;
        spec = programRuntime.Spec;
        adjustment = voiceAdjustment;

        Channel = channel;
        Key = key;
        StartStamp = stamp;
        ReverbSend = reverbSend;
        ChorusSend = chorusSend;
        ExclusiveGroup = spec.ExclusiveGroup;
        IsActive = true;
        IsKeyDown = true;
        IsSustained = false;

        voiceKey = spec.IsFixedPitch ? spec.FixedKey : key;
        layerCount = spec.Layers.Length;
        stereo = spec.HasStereoContent || adjustment.Pan != 0.0;

        int clampedVelocity = velocity < 1 ? 1 : velocity > 127 ? 127 : velocity;
        double velocityFraction = clampedVelocity / 127.0;

        velocityGain = (1.0 - spec.VelocityToLevel) + (spec.VelocityToLevel * velocityFraction);
        levelGain = spec.Level * adjustment.Level;
        velocityCutoffOctaves = spec.Filter.VelocityOctaves * ((clampedVelocity - 64) / 63.0);

        // Higher notes run their envelopes faster - a piano's top octave dies in a fraction of the
        // time its bottom one takes, and a marimba bar is the same story.
        timeScale = spec.KeyToTime == 0.0
            ? 1.0
            : Math.Pow(2.0, -spec.KeyToTime * (voiceKey - 60.0) / 12.0);

        // A hard note opens faster. At full velocity and full depth the attack is a tenth of what it
        // is at rest.
        double attackScale = spec.VelocityToAttack == 0.0
            ? 1.0
            : 1.0 - (spec.VelocityToAttack * 0.9 * velocityFraction);

        amplitude.Start(spec.Amplitude, timeScale, attackScale * adjustment.AttackScale, adjustment.ReleaseScale);

        ownFilterEnvelope = spec.Filter.Mode != GmFilterMode.Off && spec.Filter.Envelope != null;
        if (ownFilterEnvelope)
        {
            filterEnvelope.Start(spec.Filter.Envelope, timeScale, attackScale, adjustment.ReleaseScale);
        }

        filterLeft.Reset();
        filterRight.Reset();

        lfo.Start(spec.Lfo, 0.0);

        pitchEnvelopeValue = spec.PitchEnvelopeSemitones;
        double pitchSeconds = spec.PitchEnvelopeSeconds <= 0.0 ? 0.01 : spec.PitchEnvelopeSeconds;
        pitchEnvelopeCoefficient = Math.Exp(-3.0 * blockSize / (pitchSeconds * sampleRate));

        double voicePan = spec.Pan + adjustment.Pan;
        if (voicePan < -1.0) { voicePan = -1.0; }
        if (voicePan > 1.0) { voicePan = 1.0; }

        SetPan(voicePan);

        oscillatorCount = 0;

        for (int layer = 0; layer < layerCount; layer++)
        {
            GmLayerSpec layerSpec = spec.Layers[layer];

            layerHasEnvelope[layer] = layerSpec.Envelope != null;
            if (layerHasEnvelope[layer])
            {
                layerEnvelopes[layer].Start(
                    layerSpec.Envelope, timeScale, attackScale * adjustment.AttackScale, adjustment.ReleaseScale);
            }

            int unison = layerSpec.OscillatorCount;

            for (int copy = 0; copy < unison; copy++)
            {
                IModestVoiceOscillator oscillator = runtime.Rent(layer);

                // Detune spreads symmetrically: one copy is centred, two straddle, three put one in
                // the middle and one either side.
                double offset = unison == 1 ? 0.0 : (copy / (double)(unison - 1)) - 0.5;

                oscillators[oscillatorCount] = oscillator;
                oscillatorLayers[oscillatorCount] = layer;
                oscillatorCents[oscillatorCount] =
                    layerSpec.FineCents + (offset * 2.0 * layerSpec.UnisonDetuneCents);

                double pan = stereo
                    ? voicePan + layerSpec.Pan + (offset * 2.0 * layerSpec.UnisonSpread)
                    : 0.0;

                if (pan < -1.0) { pan = -1.0; }
                if (pan > 1.0) { pan = 1.0; }

                double gain = layerSpec.Level;
                oscillatorGainLeft[oscillatorCount] = (float)(gain * Math.Sqrt(1.0 - pan));
                oscillatorGainRight[oscillatorCount] = (float)(gain * Math.Sqrt(1.0 + pan));

                double frequency = FrequencyOf(oscillatorCount, layerSpec, 0.0, 0.0);
                oscillatorFrequency[oscillatorCount] = frequency;
                oscillator.SetFrequency(ModestPitch.Clamp(oscillator, sampleRate, frequency));

                // Only a patch that asked for a random phase gets one, and the seed is drawn from a
                // counter, so layered and unison copies scatter while the render still repeats.
                oscillator.Reset(layerSpec.Patch.GetStartPhase(
                    phaseSeed + ((uint)(oscillatorCount + 1) * 2654435761u)));
                oscillator.NoteOn(clampedVelocity);

                oscillatorCount++;
            }
        }
    }

    // force is what an all-notes-off does: a percussion one-shot ignores an ordinary note-off, but
    // never ignores the panic controller.
    internal void Release(bool force)
    {
        if (!force && spec != null && spec.IgnoreNoteOff) { return; }

        IsKeyDown = false;

        for (int i = 0; i < oscillatorCount; i++) { oscillators[i].NoteOff(); }

        amplitude.Release();
        if (ownFilterEnvelope) { filterEnvelope.Release(); }

        for (int layer = 0; layer < layerCount; layer++)
        {
            if (layerHasEnvelope[layer]) { layerEnvelopes[layer].Release(); }
        }
    }

    // A choke, for a stolen voice and for an exclusive group cutting off what it replaces. Percussion
    // ignores note-off but never ignores this.
    internal void Kill()
    {
        IsKeyDown = false;

        for (int i = 0; i < oscillatorCount; i++) { oscillators[i].NoteOff(); }

        amplitude.Kill();
    }

    internal void Stop()
    {
        IsActive = false;
        IsKeyDown = false;
        IsSustained = false;
        amplitude.Reset();
        filterEnvelope.Reset();

        for (int layer = 0; layer < layerCount; layer++) { layerEnvelopes[layer].Reset(); }

        ReturnOscillators();
    }

    // Adds this voice into a channel's block. Returns false when the voice is finished and free.
    internal bool Render(
        float[] left,
        float[] right,
        int frames,
        double bendSemitones,
        double modulationCents)
    {
        if (!IsActive) { return false; }

        if (amplitude.IsFinished || OscillatorsFinished())
        {
            Stop();
            return false;
        }

        lfo.Advance(frames);

        double lfoValue = lfo.Value;
        double vibratoCents =
            ((spec.Lfo.ToPitchCents * adjustment.VibratoScale) + modulationCents) * lfoValue;
        double bend = spec.IsFixedPitch ? 0.0 : bendSemitones;

        for (int i = 0; i < oscillatorCount; i++)
        {
            GmLayerSpec layerSpec = spec.Layers[oscillatorLayers[i]];
            double frequency = FrequencyOf(i, layerSpec, bend, vibratoCents + (pitchEnvelopeValue * CentsPerSemitone));

            // The additive and formant oscillators rebuild their partial tables whenever the pitch
            // moves, so a voicing with no modulation at all must not ask them to.
            if (frequency != oscillatorFrequency[i])
            {
                oscillatorFrequency[i] = frequency;
                oscillators[i].SetFrequency(ModestPitch.Clamp(oscillators[i], sampleRate, frequency));
            }
        }

        pitchEnvelopeValue *= pitchEnvelopeCoefficient;

        Array.Clear(sumLeft, 0, frames);
        if (stereo) { Array.Clear(sumRight, 0, frames); }

        Span<float> block = new Span<float>(scratch, 0, frames);

        for (int layer = 0; layer < layerCount; layer++)
        {
            bool hasEnvelope = layerHasEnvelope[layer];

            if (hasEnvelope)
            {
                GmEnvelope envelope = layerEnvelopes[layer];
                for (int i = 0; i < frames; i++) { layerGains[i] = envelope.Next(); }
            }

            for (int index = 0; index < oscillatorCount; index++)
            {
                if (oscillatorLayers[index] != layer) { continue; }

                oscillators[index].Render(block);

                float gainLeft = oscillatorGainLeft[index];

                if (stereo)
                {
                    float gainRight = oscillatorGainRight[index];

                    if (hasEnvelope)
                    {
                        for (int i = 0; i < frames; i++)
                        {
                            float value = (float)(scratch[i] * layerGains[i]);
                            sumLeft[i] += value * gainLeft;
                            sumRight[i] += value * gainRight;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < frames; i++)
                        {
                            sumLeft[i] += scratch[i] * gainLeft;
                            sumRight[i] += scratch[i] * gainRight;
                        }
                    }
                }
                else
                {
                    // One mono sum, so one filter rather than two: the common case pays half.
                    float gain = (float)spec.Layers[layer].Level;

                    if (hasEnvelope)
                    {
                        for (int i = 0; i < frames; i++)
                        {
                            sumLeft[i] += (float)(scratch[i] * layerGains[i]) * gain;
                        }
                    }
                    else
                    {
                        for (int i = 0; i < frames; i++)
                        {
                            sumLeft[i] += scratch[i] * gain;
                        }
                    }
                }
            }
        }

        if (spec.Filter.Mode != GmFilterMode.Off)
        {
            double envelopeLevel = ownFilterEnvelope ? AdvanceFilterEnvelope(frames) : amplitude.Level;

            double octaves =
                (spec.Filter.KeyTracking * (voiceKey - 60.0) / 12.0)
                + velocityCutoffOctaves
                + (spec.Filter.EnvelopeOctaves * envelopeLevel)
                + (spec.Lfo.ToCutoffOctaves * lfoValue)
                + adjustment.BrightnessOctaves;

            double cutoff = spec.Filter.Cutoff * Math.Pow(2.0, octaves);

            filterLeft.SetCutoff(cutoff, spec.Filter.Resonance);
            filterLeft.Process(spec.Filter.Mode, sumLeft, frames);

            if (stereo)
            {
                filterRight.SetCutoff(cutoff, spec.Filter.Resonance);
                filterRight.Process(spec.Filter.Mode, sumRight, frames);
            }
        }

        double tremolo = spec.Lfo.ToLevel == 0.0
            ? 1.0
            : 1.0 - (spec.Lfo.ToLevel * (0.5 - (0.5 * lfoValue)));

        double gainScale = levelGain * velocityGain * tremolo;

        if (stereo)
        {
            for (int i = 0; i < frames; i++)
            {
                float envelope = (float)(amplitude.Next() * gainScale);
                left[i] += sumLeft[i] * envelope;
                right[i] += sumRight[i] * envelope;
            }
        }
        else
        {
            for (int i = 0; i < frames; i++)
            {
                float value = (float)(sumLeft[i] * amplitude.Next() * gainScale);
                left[i] += value * panLeft;
                right[i] += value * panRight;
            }
        }

        return true;
    }

    private double AdvanceFilterEnvelope(int frames)
    {
        double level = 0.0;
        for (int i = 0; i < frames; i++) { level = filterEnvelope.Next(); }
        return level;
    }

    private double FrequencyOf(int index, GmLayerSpec layerSpec, double bendSemitones, double cents)
    {
        double key = double.IsNaN(layerSpec.FixedKey) ? voiceKey : layerSpec.FixedKey;

        double note = key + layerSpec.Transpose + bendSemitones
            + ((oscillatorCents[index] + cents) / CentsPerSemitone);

        return ModestPitch.ToHertz(note);
    }

    // Constant power, normalised so that a centred voice is at unity on both sides rather than three
    // decibels down: centre gives 1 and 1, hard over gives the square root of two and nothing.
    private void SetPan(double pan)
    {
        panLeft = (float)Math.Sqrt(1.0 - pan);
        panRight = (float)Math.Sqrt(1.0 + pan);
    }

    private bool OscillatorsFinished()
    {
        for (int i = 0; i < oscillatorCount; i++)
        {
            if (!oscillators[i].IsFinished) { return false; }
        }

        return oscillatorCount > 0;
    }

    private void ReturnOscillators()
    {
        for (int i = 0; i < oscillatorCount; i++)
        {
            runtime.Return(oscillatorLayers[i], oscillators[i]);
            oscillators[i] = null;
        }

        oscillatorCount = 0;
    }
}
