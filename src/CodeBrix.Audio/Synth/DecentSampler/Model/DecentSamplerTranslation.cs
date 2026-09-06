namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// How a binding maps its source value onto its target.
/// </summary>
public enum DecentSamplerTranslation
{
    /// <summary>A straight line between <c>translationOutputMin</c> and <c>translationOutputMax</c> (<c>linear</c>). The default.</summary>
    Linear,

    /// <summary>A piecewise-linear table given by <c>translationTable</c> (<c>table</c>).</summary>
    Table,

    /// <summary>Always <c>translationValue</c> (<c>fixed_value</c>).</summary>
    FixedValue,
}
