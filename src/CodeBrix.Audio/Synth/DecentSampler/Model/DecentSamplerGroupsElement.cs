using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// The <c>&lt;groups&gt;</c> element: the instrument-wide defaults, and the groups themselves.
/// </summary>
/// <remarks>
/// A preset has exactly one <c>&lt;groups&gt;</c> element. Besides its own <c>volume</c>,
/// <c>globalTuning</c>, <c>glideTime</c> and <c>glideMode</c>, it accepts any inheritable sound
/// attribute as an instrument-wide default - the boilerplate preset in the developer guide, for
/// instance, writes the amplitude envelope here.
/// </remarks>
public sealed class DecentSamplerGroupsElement : DecentSamplerSoundElement
{
    private readonly List<DecentSamplerGroupElement> _groups = [];

    /// <summary>The groups, in document order. The first is group 0 for binding indexes.</summary>
    public IReadOnlyList<DecentSamplerGroupElement> Groups => _groups;

    internal void Add(DecentSamplerGroupElement group)
    {
        group.Index = _groups.Count;
        _groups.Add(group);
        AddChild(group);
    }
}
