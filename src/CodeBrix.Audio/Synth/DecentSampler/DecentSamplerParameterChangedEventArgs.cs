using System;

namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>
/// Says which engine parameter a binding has just changed. This is the seam a synthesizer listens on
/// to learn that a group's volume, a zone's loop point or an effect's cut-off has moved.
/// </summary>
public sealed class DecentSamplerParameterChangedEventArgs : EventArgs
{
    /// <summary>Builds the arguments.</summary>
    /// <param name="targetName">The target's name, such as <c>group[0].AMP_VOLUME</c>.</param>
    /// <param name="parameter">The parameter token, such as <c>AMP_VOLUME</c>.</param>
    /// <param name="numberValue">The new value as a number.</param>
    /// <param name="textValue">The new value as text.</param>
    public DecentSamplerParameterChangedEventArgs(
        string targetName, string parameter, double numberValue, string textValue)
    {
        TargetName = targetName;
        Parameter = parameter;
        NumberValue = numberValue;
        TextValue = textValue;
    }

    /// <summary>
    /// The target's name: the thing that changed and the parameter that changed on it, such as
    /// <c>group[0].AMP_VOLUME</c> or <c>effect[1].FX_REVERB_WET_LEVEL</c>.
    /// </summary>
    public string TargetName { get; }

    /// <summary>The parameter token, such as <c>AMP_VOLUME</c>.</summary>
    public string Parameter { get; }

    /// <summary>The new effective value as a number. Switches are 1 and 0.</summary>
    public double NumberValue { get; }

    /// <summary>The new effective value as text, which is the useful form for names and paths.</summary>
    public string TextValue { get; }
}
