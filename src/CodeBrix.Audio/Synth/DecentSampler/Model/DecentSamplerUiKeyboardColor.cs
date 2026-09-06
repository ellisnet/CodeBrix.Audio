namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// One <c>&lt;color&gt;</c> range of the on-screen keyboard.
/// </summary>
/// <remarks>
/// The reference player tints only the white keys, and overlays the colour at 75% transparency so the
/// key labels stay readable.
/// </remarks>
public sealed class DecentSamplerUiKeyboardColor : DecentSamplerElement
{
    /// <summary>The range's 0-based position, which a binding's <c>colorIndex</c> counts.</summary>
    public int Index { get; internal set; }

    /// <summary>The bottom of the range (<c>loNote</c>).</summary>
    public int? LoNote { get; internal set; }

    /// <summary>The top of the range, inclusive (<c>hiNote</c>).</summary>
    public int? HiNote { get; internal set; }

    /// <summary>The colour, eight hexadecimal ARGB digits (<c>color</c>).</summary>
    public string Color { get; internal set; }

    /// <summary>Whether the range is drawn. Bindings change this through the <c>ENABLED</c> parameter.</summary>
    public bool? Enabled { get; internal set; }
}
