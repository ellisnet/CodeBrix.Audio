namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// An <c>&lt;oscilloscope&gt;</c>: a live waveform display of the instrument's output.
/// </summary>
public sealed class DecentSamplerUiOscilloscope : DecentSamplerUiElement
{
    /// <summary>The background colour, eight hexadecimal ARGB digits (<c>backgroundColor</c>). Default FF000000.</summary>
    public string BackgroundColor { get; internal set; }

    /// <summary>The waveform colour, eight hexadecimal ARGB digits (<c>waveColor</c>). Default FF00FF00.</summary>
    public string WaveColor { get; internal set; }

    /// <summary>The waveform stroke width in pixels (<c>lineThickness</c>). Default 1.5.</summary>
    public double? LineThickness { get; internal set; }

    /// <summary>Whether a faint zero line is drawn (<c>showCenterLine</c>). Default false.</summary>
    public bool? ShowCenterLine { get; internal set; }
}
