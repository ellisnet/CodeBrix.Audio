namespace CodeBrix.Audio.ModestSynth.Patch;

/// <summary>
/// How an FM operator gets its frequency - the <c>fmOpNMode</c> attribute.
/// </summary>
public enum ModestFmOperatorMode
{
    /// <summary>
    /// The operator runs at a RATIO of the played note, so it tracks the keyboard. Spelled
    /// <c>ratio</c>, and the default.
    /// </summary>
    Ratio = 0,

    /// <summary>
    /// The operator runs at an absolute frequency in Hz regardless of the note played, which is
    /// what gives bells and other metallic timbres their fixed inharmonic partials. Spelled
    /// <c>fixed</c>.
    /// </summary>
    Fixed = 1,
}
