using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// One parametric equalizer band of a region (<c>eqN_freq</c>, <c>eqN_bw</c>, <c>eqN_gain</c> and
/// their CC modulations). A region carries up to three bands, processed in series as peaking filters.
/// </summary>
/// <remarks>
/// The SFZ defaults follow the band number: band 1 centers at 50 Hz, band 2 at 500 Hz and band 3 at
/// 5000 Hz, each one octave wide with no gain. A band with zero effective gain is skipped entirely, so
/// carrying a band whose gain only moves under CC control costs nothing until the controller moves.
/// </remarks>
public sealed class SfzEqBand
{
    internal SfzEqBand(
        int number, float frequency, float bandwidth, float gain,
        IReadOnlyList<SfzCcModulation> frequencyCc,
        IReadOnlyList<SfzCcModulation> bandwidthCc,
        IReadOnlyList<SfzCcModulation> gainCc)
    {
        Number = number;
        Frequency = frequency;
        Bandwidth = bandwidth;
        Gain = gain;
        FrequencyCc = frequencyCc;
        BandwidthCc = bandwidthCc;
        GainCc = gainCc;
    }

    /// <summary>The band number as written (<c>eq1</c>, <c>eq2</c>, <c>eq3</c>).</summary>
    public int Number { get; }

    /// <summary>The center frequency in Hz (<c>eqN_freq</c>; defaults 50/500/5000 by band).</summary>
    public float Frequency { get; }

    /// <summary>The bandwidth in octaves (<c>eqN_bw</c>, default 1).</summary>
    public float Bandwidth { get; }

    /// <summary>The band gain in decibels (<c>eqN_gain</c>, default 0).</summary>
    public float Gain { get; }

    /// <summary>CC modulations of <see cref="Frequency"/>, in additive Hz (<c>eqN_freq_onccX</c>).</summary>
    public IReadOnlyList<SfzCcModulation> FrequencyCc { get; }

    /// <summary>CC modulations of <see cref="Bandwidth"/>, in additive octaves (<c>eqN_bw_onccX</c>).</summary>
    public IReadOnlyList<SfzCcModulation> BandwidthCc { get; }

    /// <summary>CC modulations of <see cref="Gain"/>, in additive decibels (<c>eqN_gain_onccX</c>).</summary>
    public IReadOnlyList<SfzCcModulation> GainCc { get; }
}
