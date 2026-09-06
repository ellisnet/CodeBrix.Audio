namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// One <c>&lt;oscillator&gt;</c>: a playable zone that generates a waveform instead of reading a file.
/// </summary>
/// <remarks>
/// The element itself carries only the oscillator attributes; everything else - the note range, the
/// envelope, the volume, the tuning - is inherited from the enclosing <c>&lt;group&gt;</c> and
/// <c>&lt;groups&gt;</c>, which is also where the harmonic and FM parameter families are normally
/// written.
/// </remarks>
public sealed class DecentSamplerOscillatorElement : DecentSamplerSoundElement
{
    /// <summary>The oscillator's 0-based position within its group.</summary>
    public int Index { get; internal set; }
}
