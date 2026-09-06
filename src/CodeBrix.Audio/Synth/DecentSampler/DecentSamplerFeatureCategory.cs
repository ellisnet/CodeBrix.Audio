namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>
/// What kind of thing a listed feature of the Decent Sampler format is.
/// </summary>
public enum DecentSamplerFeatureCategory
{
    /// <summary>An XML element, such as <c>group</c> or <c>labeled-knob</c>.</summary>
    Element,

    /// <summary>An attribute of one element, named <c>element@attribute</c>.</summary>
    Attribute,

    /// <summary>A value of a <c>&lt;binding&gt;</c>'s <c>type</c> attribute.</summary>
    BindingType,

    /// <summary>A value of a <c>&lt;binding&gt;</c>'s <c>level</c> attribute.</summary>
    BindingLevel,

    /// <summary>A value of a <c>&lt;binding&gt;</c>'s <c>parameter</c> attribute.</summary>
    BindingParameter,

    /// <summary>A value of an <c>&lt;effect&gt;</c>'s <c>type</c> attribute.</summary>
    EffectType,

    /// <summary>An element name beneath <c>&lt;modulators&gt;</c>.</summary>
    ModulatorType,

    /// <summary>A value of an <c>&lt;oscillator&gt;</c>'s <c>waveform</c> attribute.</summary>
    Waveform,

    /// <summary>A value of a <c>&lt;binding&gt;</c>'s <c>translation</c> attribute.</summary>
    TranslationMode,

    /// <summary>A value of some other enumerated attribute, named <c>attribute=value</c>.</summary>
    EnumerationValue,
}
