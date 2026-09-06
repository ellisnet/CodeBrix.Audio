namespace CodeBrix.Audio.ModestSynth.Oscillators;

/// <summary>
/// The oscillator shapes a patch can ask for. The names mirror the Decent Sampler
/// <c>&lt;oscillator waveform="..."&gt;</c> values one for one; see
/// <see cref="ModestWaveforms" /> for the string spellings and the parser.
/// </summary>
public enum ModestWaveform
{
    /// <summary>A pure sine wave with no harmonics. The default. Spelled <c>sine</c>.</summary>
    Sine = 0,

    /// <summary>A band-limited sawtooth, rich in both odd and even harmonics. Spelled <c>saw</c>.</summary>
    Saw = 1,

    /// <summary>A band-limited square (rectangular) wave. Spelled <c>square</c>.</summary>
    Square = 2,

    /// <summary>A band-limited triangle wave, mellower than a saw. Spelled <c>triangle</c>.</summary>
    Triangle = 3,

    /// <summary>
    /// White noise. Spelled <c>noise</c>, and <c>white_noise</c> is accepted as a synonym.
    /// </summary>
    Noise = 4,

    /// <summary>
    /// A plucked-string waveguide model. Spelled <c>pluck1</c>. Shaped by
    /// <c>damping</c> and <c>pluckType</c> rather than by a fixed harmonic recipe.
    /// </summary>
    Pluck1 = 5,

    /// <summary>
    /// A multi-frame wavetable read from a .wav file. Spelled <c>wavetable</c>. The parameter
    /// model carries its attributes; the sound generator arrives in a later release.
    /// </summary>
    Wavetable = 6,

    /// <summary>
    /// An additive oscillator summing up to 64 harmonic partials. Spelled <c>harmonic</c>. The
    /// parameter model carries its attributes; the sound generator arrives in a later release.
    /// </summary>
    Harmonic = 7,

    /// <summary>
    /// A six-operator FM oscillator with the 32 classic algorithm topologies. Spelled
    /// <c>fm6op</c>. The parameter model carries its attributes; the sound generator arrives in
    /// a later release.
    /// </summary>
    Fm6Op = 8,

    /// <summary>
    /// A formant tone. Spelled <c>formant</c>.
    /// </summary>
    /// <remarks>
    /// The developer guide does not document it, but the reference player accepts the name and
    /// makes sound with it - a harmonically rich tone whose fundamental sits on the played note
    /// (measurements document, item 13). Its ATTRIBUTES are an open documentation gap: twenty-one
    /// guesses were all accepted silently and none could be shown to change the sound. The name is
    /// recognised here so a preset using it can be reported accurately rather than as an unknown
    /// waveform; there is no sound generator for it yet.
    /// </remarks>
    Formant = 9,
}
