using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// The <c>&lt;tags&gt;</c> element: extra detail about the tags used elsewhere in the preset.
/// </summary>
/// <remarks>
/// A tag works without being declared here. This element exists so a preset can give one an initial
/// volume, a polyphony limit or an enabled state, all of which bindings can then change.
/// </remarks>
public sealed class DecentSamplerTagsElement : DecentSamplerElement
{
    private readonly List<DecentSamplerTag> _tags = [];

    /// <summary>The declared tags, in document order.</summary>
    public IReadOnlyList<DecentSamplerTag> Tags => _tags;

    internal void Add(DecentSamplerTag tag)
    {
        _tags.Add(tag);
        AddChild(tag);
    }
}
