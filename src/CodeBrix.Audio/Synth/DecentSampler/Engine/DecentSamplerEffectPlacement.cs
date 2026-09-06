namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

/// <summary>Where in the signal path an effect chain sits.</summary>
public enum DecentSamplerEffectPlacement
{
    /// <summary>The instrument-wide chain, after every bus has been folded in.</summary>
    Instrument,

    /// <summary>A bus chain, processing everything routed to that bus.</summary>
    Bus,

    /// <summary>A group chain, instantiated once per voice.</summary>
    Group,
}
