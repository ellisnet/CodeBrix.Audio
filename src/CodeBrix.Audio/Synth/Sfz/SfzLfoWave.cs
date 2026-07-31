namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// The waveform of an SFZ v2 LFO (<c>lfoN_wave</c>), using the SFZ v2 numbering. All waveforms run
/// -1 to 1.
/// </summary>
public enum SfzLfoWave
{
    /// <summary>Wave 0 - triangle, the SFZ v2 default.</summary>
    Triangle = 0,

    /// <summary>Wave 1 - sine (also the wave of the SFZ v1 <c>amplfo</c>/<c>fillfo</c>/<c>pitchlfo</c> blocks).</summary>
    Sine = 1,

    /// <summary>Wave 2 - pulse, 75% high.</summary>
    Pulse75 = 2,

    /// <summary>Wave 3 - square (50% pulse).</summary>
    Square = 3,

    /// <summary>Wave 4 - pulse, 25% high.</summary>
    Pulse25 = 4,

    /// <summary>Wave 5 - pulse, 12.5% high.</summary>
    Pulse12 = 5,

    /// <summary>Wave 6 - saw, rising.</summary>
    SawUp = 6,

    /// <summary>Wave 7 - saw, falling.</summary>
    SawDown = 7,

    /// <summary>
    /// Wave 12 - random sample-and-hold: a new random level in -1..1 twice per period. Wave -1, the
    /// deprecated ARIA random, maps here too.
    /// </summary>
    RandomSampleHold = 12
}
