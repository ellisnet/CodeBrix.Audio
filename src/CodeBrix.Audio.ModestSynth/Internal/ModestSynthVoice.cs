using System;
using CodeBrix.Audio.ModestSynth.Oscillators;

namespace CodeBrix.Audio.ModestSynth.Internal;

// One sounding note of the standalone synthesizer: an oscillator, an amplitude envelope, a glide ramp
// and the velocity gain. The synthesizer owns the pool and the mixing; a voice never allocates once it
// exists.
internal sealed class ModestSynthVoice
{
    private readonly IModestVoiceOscillator oscillator;
    private readonly ModestEnvelope envelope;
    private readonly float[] scratch;
    private readonly int sampleRate;

    private double attack;
    private double decay;
    private double sustain = 1.0;
    private double release;
    private double glideSemitones;
    private double glidePerBlock;
    private float velocityGain = 1f;

    internal ModestSynthVoice(IModestVoiceOscillator oscillator, int sampleRate, int blockSize)
    {
        this.oscillator = oscillator;
        this.sampleRate = sampleRate;
        envelope = new ModestEnvelope(sampleRate);
        scratch = new float[blockSize];
    }

    internal IModestVoiceOscillator Oscillator => oscillator;

    internal bool IsActive { get; private set; }

    internal bool IsKeyDown { get; private set; }

    internal bool IsSustained { get; set; }

    internal int Channel { get; private set; }

    internal int Key { get; private set; }

    internal long StartStamp { get; private set; }

    internal void Start(
        int channel,
        int key,
        int velocity,
        long stamp,
        double startPhase,
        double attackSeconds,
        double decaySeconds,
        double sustainLevel,
        double releaseSeconds,
        double velocityTracking,
        double glideFromSemitones,
        double glideSeconds)
    {
        Channel = channel;
        Key = key;
        StartStamp = stamp;
        IsActive = true;
        IsKeyDown = true;
        IsSustained = false;

        attack = attackSeconds;
        decay = decaySeconds;
        sustain = sustainLevel;
        release = releaseSeconds;

        velocityGain = (float)((1.0 - velocityTracking) + (velocityTracking * Math.Clamp(velocity, 0, 127) / 127.0));

        glideSemitones = glideSeconds > 0.0 ? glideFromSemitones : 0.0;
        glidePerBlock = glideSeconds > 0.0 && Math.Abs(glideFromSemitones) > 0.0
            ? Math.Abs(glideFromSemitones) / (glideSeconds * sampleRate) * scratch.Length
            : 0.0;

        oscillator.SetFrequency(ModestPitch.Clamp(oscillator, sampleRate, ModestPitch.ToHertz(key + glideSemitones)));
        oscillator.Reset(startPhase);
        oscillator.NoteOn(velocity);

        envelope.Start(attack, decay, sustain, release);
    }

    internal void Release()
    {
        IsKeyDown = false;
        oscillator.NoteOff();
        envelope.Release();
    }

    internal void Kill()
    {
        IsKeyDown = false;
        oscillator.NoteOff();
        envelope.Kill();
    }

    internal void Stop()
    {
        IsActive = false;
        IsKeyDown = false;
        IsSustained = false;
        envelope.Reset();
    }

    // Adds this voice's block into the mix. Returns false when the voice is finished and may be reused.
    internal bool Render(float[] left, float[] right, int frames, double bendSemitones)
    {
        if (!IsActive) { return false; }

        if (envelope.IsFinished || oscillator.IsFinished)
        {
            Stop();
            return false;
        }

        AdvanceGlide();

        double hertz = ModestPitch.ToHertz(Key + glideSemitones + bendSemitones);
        oscillator.SetFrequency(ModestPitch.Clamp(oscillator, sampleRate, hertz));

        Span<float> block = new Span<float>(scratch, 0, frames);
        oscillator.Render(block);

        for (int i = 0; i < frames; i++)
        {
            float value = (float)(block[i] * envelope.Next()) * velocityGain;
            left[i] += value;
            right[i] += value;
        }

        return true;
    }

    private void AdvanceGlide()
    {
        if (glidePerBlock <= 0.0 || glideSemitones == 0.0)
        {
            glideSemitones = 0.0;
            return;
        }

        glideSemitones = glideSemitones > 0.0
            ? Math.Max(0.0, glideSemitones - glidePerBlock)
            : Math.Min(0.0, glideSemitones + glidePerBlock);
    }
}
