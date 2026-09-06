using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>
/// The live state of one tag: whether zones carrying it play, how loud, where, and how many voices it
/// may hold at once.
/// </summary>
/// <remarks>
/// <para>
/// A tag does not need a <c>&lt;tag&gt;</c> element to exist. Presets routinely tag their samples and
/// then bind a knob to <c>level="tag" identifier="mic1"</c> without ever declaring the tag, so the
/// engine keeps a state for every tag named anywhere: on a <c>&lt;tag&gt;</c> element, on a group, on a
/// sample, on an oscillator, or in a binding's <c>identifier</c>.
/// </para>
/// <para>
/// When the preset does declare the tag, <see cref="Source"/> points at it and the engine writes every
/// change back onto it too, so code that reads <c>instrument.Tags</c> sees the live values.
/// </para>
/// </remarks>
public sealed class DecentSamplerTagState
{
    internal DecentSamplerTagState(string name, DecentSamplerTag source)
    {
        Name = name;
        Source = source;
        Enabled = source?.Enabled ?? true;
        Volume = source?.Volume ?? 1.0;
        Pan = source?.Pan ?? 0.0;
        Polyphony = source?.Polyphony ?? -1;
    }

    /// <summary>The tag's name, as written on the samples and groups that carry it.</summary>
    public string Name { get; }

    /// <summary>The declared <c>&lt;tag&gt;</c> element, or null when the preset declares none.</summary>
    public DecentSamplerTag Source { get; }

    /// <summary>Whether zones carrying this tag play. Default true.</summary>
    public bool Enabled { get; internal set; }

    /// <summary>The tag's volume as a linear multiplier. Default 1.</summary>
    public double Volume { get; internal set; }

    /// <summary>The tag's stereo position, -100 to 100. Default 0.</summary>
    public double Pan { get; internal set; }

    /// <summary>How many voices the tag may hold at once. Default -1, meaning no limit.</summary>
    public int Polyphony { get; internal set; }

    /// <inheritdoc/>
    public override string ToString() =>
        $"tag \"{Name}\": enabled {Enabled}, volume {Volume}, polyphony {Polyphony}";
}
