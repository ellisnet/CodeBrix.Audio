namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>Which kind of user-interface element a <see cref="DecentSamplerControl"/> stands for.</summary>
public enum DecentSamplerControlKind
{
    /// <summary>A <c>&lt;labeled-knob&gt;</c>: a knob that draws its own label.</summary>
    LabeledKnob,

    /// <summary>A <c>&lt;control&gt;</c>: a knob, slider or bar with no built-in label.</summary>
    Control,

    /// <summary>A <c>&lt;button&gt;</c>, which steps through its states.</summary>
    Button,

    /// <summary>A <c>&lt;menu&gt;</c>: a drop-down list of options.</summary>
    Menu,

    /// <summary>An <c>&lt;xyPad&gt;</c>: two axes, each 0 to 1.</summary>
    XyPad,

    /// <summary>A <c>&lt;label&gt;</c>: text with no input.</summary>
    Label,

    /// <summary>An <c>&lt;image&gt;</c>.</summary>
    Image,

    /// <summary>A <c>&lt;multiFrameImage&gt;</c>: an animation strip.</summary>
    MultiFrameImage,

    /// <summary>A <c>&lt;rectangle&gt;</c>.</summary>
    Rectangle,

    /// <summary>A <c>&lt;line&gt;</c>.</summary>
    Line,

    /// <summary>An <c>&lt;oscilloscope&gt;</c>.</summary>
    Oscilloscope,

    /// <summary>An element this engine has no kind for. It still counts towards the control index.</summary>
    Unknown,
}
