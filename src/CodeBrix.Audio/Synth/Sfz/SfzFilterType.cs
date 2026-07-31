namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// The per-voice filter shape of an SFZ region, from the <c>fil_type</c> opcode.
/// </summary>
/// <remarks>
/// The filter runs only when the region sets <c>cutoff</c>; without it the sample plays unfiltered,
/// whatever <c>fil_type</c> says. The SFZ default when <c>cutoff</c> is set is <see cref="LowPass2P"/>.
/// </remarks>
public enum SfzFilterType
{
    /// <summary><c>lpf_1p</c> - one-pole low-pass, 6 dB/octave. Has no resonance.</summary>
    LowPass1P = 0,

    /// <summary><c>hpf_1p</c> - one-pole high-pass, 6 dB/octave. Has no resonance.</summary>
    HighPass1P,

    /// <summary><c>lpf_2p</c> (the default) - two-pole low-pass, 12 dB/octave, with resonance.</summary>
    LowPass2P,

    /// <summary><c>hpf_2p</c> - two-pole high-pass, 12 dB/octave, with resonance.</summary>
    HighPass2P,

    /// <summary><c>bpf_2p</c> - two-pole band-pass, 12 dB/octave, with resonance.</summary>
    BandPass2P,

    /// <summary><c>brf_2p</c> - two-pole band-reject (notch), 12 dB/octave, with resonance.</summary>
    BandReject2P
}
