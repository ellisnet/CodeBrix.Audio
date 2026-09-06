namespace CodeBrix.Audio.Synth.DecentSampler.Modulation;

// Which segment of an envelope modulator is running.
internal enum DecentSamplerModulationStage
{
    // Waiting out delayTime, with the envelope at zero.
    Delay,

    Attack,

    Decay,

    Sustain,

    Release,

    // Finished; the envelope stays at zero until its note plays again.
    Finished,
}
