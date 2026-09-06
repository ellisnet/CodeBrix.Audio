using System;

namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>Says which property of a control changed and what it changed to.</summary>
public sealed class DecentSamplerControlChangedEventArgs : EventArgs
{
    /// <summary>Builds the arguments.</summary>
    /// <param name="control">The control that changed.</param>
    /// <param name="propertyName">The name of the property that changed, such as <c>Value</c>.</param>
    public DecentSamplerControlChangedEventArgs(DecentSamplerControl control, string propertyName)
    {
        Control = control;
        PropertyName = propertyName;
    }

    /// <summary>The control that changed.</summary>
    public DecentSamplerControl Control { get; }

    /// <summary>
    /// The name of the property that changed, matching the property on
    /// <see cref="DecentSamplerControl"/>, so a renderer can bind to it by name.
    /// </summary>
    public string PropertyName { get; }
}
