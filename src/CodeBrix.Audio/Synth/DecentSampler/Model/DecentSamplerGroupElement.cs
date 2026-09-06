using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// One <c>&lt;group&gt;</c>: a set of samples and oscillators that share settings, an optional
/// per-voice effect chain, and a position that bindings address it by.
/// </summary>
public sealed class DecentSamplerGroupElement : DecentSamplerSoundElement
{
    private readonly List<DecentSamplerSampleElement> _samples = [];
    private readonly List<DecentSamplerOscillatorElement> _oscillators = [];

    /// <summary>The group's 0-based position in the preset, which bindings use as their index.</summary>
    public int Index { get; internal set; }

    /// <summary>The group's name (<c>name</c>). Display only; bindings address groups by index or tag.</summary>
    public string Name { get; internal set; }

    /// <summary>Whether the group plays (<c>enabled</c>). Default true.</summary>
    public bool? Enabled { get; internal set; }

    /// <summary>The samples in the group, in document order.</summary>
    public IReadOnlyList<DecentSamplerSampleElement> Samples => _samples;

    /// <summary>The oscillators in the group, in document order.</summary>
    public IReadOnlyList<DecentSamplerOscillatorElement> Oscillators => _oscillators;

    /// <summary>
    /// The group's own effect chain, instantiated per voice by the engine, or null when the group
    /// declares none.
    /// </summary>
    public DecentSamplerEffectsElement Effects { get; internal set; }

    internal void Add(DecentSamplerSampleElement sample)
    {
        sample.Index = _samples.Count;
        _samples.Add(sample);
        AddChild(sample);
    }

    internal void Add(DecentSamplerOscillatorElement oscillator)
    {
        oscillator.Index = _oscillators.Count;
        _oscillators.Add(oscillator);
        AddChild(oscillator);
    }
}
