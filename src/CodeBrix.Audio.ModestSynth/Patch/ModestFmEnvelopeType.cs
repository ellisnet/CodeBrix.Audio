namespace CodeBrix.Audio.ModestSynth.Patch;

/// <summary>
/// Which envelope an FM operator runs - the <c>fmOpNEgType</c> attribute.
/// </summary>
public enum ModestFmEnvelopeType
{
    /// <summary>
    /// The operator's own attack, decay, sustain and release, in seconds and levels. The default.
    /// </summary>
    Adsr = 0,

    /// <summary>
    /// The four-stage rate/level envelope of the classic six-operator hardware, with rates and
    /// levels as integers from 0 to 99 so that vintage patch data transfers unchanged. Spelled
    /// <c>dx7</c>. Selecting it makes the operator's ADSR values inert.
    /// </summary>
    Dx7 = 1,
}
