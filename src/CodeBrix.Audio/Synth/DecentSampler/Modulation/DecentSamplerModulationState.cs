namespace CodeBrix.Audio.Synth.DecentSampler.Modulation;

// One running instance of one modulator: the shared instance of a global-scope modulator, or one
// voice's own copy of a voice-scope one. Every kind stores its state here so the whole runtime is a
// pair of flat arrays and nothing is allocated while notes are sounding.
internal struct DecentSamplerModulationState
{
    // Whether the instance is running at all. A voice-scope instance runs between its voice's
    // note-on and the moment the voice is recycled.
    public bool IsRunning;

    // Whether a contribution is currently written into the binding engine, so it can be taken out
    // again when the modulator falls silent (an LFO inside its delay time, say).
    public bool IsApplied;

    // The LFO's phase, 0 to 1, or the periodic random generator's progress toward its next value.
    public double Phase;

    // Seconds left of delayTime before the modulator starts.
    public double DelayRemaining;

    // Seconds since the envelope's own delay ended.
    public double ElapsedSeconds;

    // Which segment of the envelope is running.
    public DecentSamplerModulationStage Stage;

    // The envelope's level when its release began, which is what the release falls from.
    public double ReleaseLevel;

    // The last value produced: the smoothed value of an MPE source, or the held value of a random
    // one.
    public double Value;

    // Whether Value holds anything yet, so that a smoothed source starts AT its first reading
    // instead of sliding up to it from zero.
    public bool HasValue;

    // The note-on velocity of the note this instance belongs to, 1 to 127.
    public int Velocity;
}
