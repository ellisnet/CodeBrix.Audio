using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// A <c>&lt;labeled-knob&gt;</c> or <c>&lt;control&gt;</c>: the knobs and sliders that drive an
/// instrument's parameters.
/// </summary>
/// <remarks>
/// The two elements differ only in whether a built-in label is shown, so they share one class;
/// <see cref="DecentSamplerElement.ElementName"/> says which was written.
/// </remarks>
public sealed class DecentSamplerUiControl : DecentSamplerUiElement
{
    private readonly List<DecentSamplerUiState> _states = [];

    /// <summary>The text above the control when its label is shown (<c>label</c>).</summary>
    public string Label { get; internal set; }

    /// <summary>
    /// Whether the built-in label is shown (<c>showLabel</c>). Default true for a labelled knob and
    /// false for a control.
    /// </summary>
    public bool? ShowLabel { get; internal set; }

    /// <summary>The name a host shows for this parameter (<c>parameterName</c>).</summary>
    public string ParameterName { get; internal set; }

    /// <summary>The visual form of the control (<c>style</c>). Default a vertically dragged rotary.</summary>
    public DecentSamplerControlStyle? Style { get; internal set; }

    /// <summary>The <c>style</c> attribute exactly as written.</summary>
    public string StyleName { get; internal set; }

    /// <summary>The lowest value (<c>minValue</c>). Default 0.</summary>
    public double? MinValue { get; internal set; }

    /// <summary>The highest value (<c>maxValue</c>). Default 1.</summary>
    public double? MaxValue { get; internal set; }

    /// <summary>
    /// The value the control starts at (<c>value</c>). Default 0. This is what fires the control's
    /// bindings when the preset loads, and so what sets the instrument's initial state.
    /// </summary>
    public double? Value { get; internal set; }

    /// <summary>The value a double-click restores (<c>defaultValue</c>).</summary>
    public double? DefaultValue { get; internal set; }

    /// <summary>
    /// How the value is interpreted (<c>valueType</c>, or the older <c>type</c>). Default float.
    /// </summary>
    public DecentSamplerValueType? ValueType { get; internal set; }

    /// <summary>The <c>valueType</c> or <c>type</c> attribute exactly as written.</summary>
    public string ValueTypeName { get; internal set; }

    /// <summary>The label colour, eight hexadecimal ARGB digits (<c>textColor</c>).</summary>
    public string TextColor { get; internal set; }

    /// <summary>The label font size (<c>textSize</c>). Default 12.</summary>
    public double? TextSize { get; internal set; }

    /// <summary>The filled part of the track, eight hexadecimal ARGB digits (<c>trackForegroundColor</c>).</summary>
    public string TrackForegroundColor { get; internal set; }

    /// <summary>The empty part of the track, eight hexadecimal ARGB digits (<c>trackBackgroundColor</c>).</summary>
    public string TrackBackgroundColor { get; internal set; }

    /// <summary>How opaque the control is while disabled (<c>disabledOpacity</c>). Default 0.5.</summary>
    public double? DisabledOpacity { get; internal set; }

    /// <summary>How the value snaps as the user drags (<c>snapMode</c>). Default none.</summary>
    public DecentSamplerSnapMode? SnapMode { get; internal set; }

    /// <summary>The values a stop-point snap uses (<c>snapStopPoints</c>).</summary>
    public IReadOnlyList<double> SnapStopPoints { get; internal set; } = [];

    /// <summary>Whether holding shift defeats snapping (<c>defeatSnapWithShift</c>). Default false.</summary>
    public bool? DefeatSnapWithShift { get; internal set; }

    /// <summary>A KnobMan-format skin image (<c>customSkinImage</c>).</summary>
    public string CustomSkinImage { get; internal set; }

    /// <summary>A KnobMan-format skin image used while hovering (<c>customSkinHoverImage</c>).</summary>
    public string CustomSkinHoverImage { get; internal set; }

    /// <summary>How many frames the skin image holds (<c>customSkinNumFrames</c>).</summary>
    public int? CustomSkinNumFrames { get; internal set; }

    /// <summary>How the skin image's frames are laid out (<c>customSkinImageOrientation</c>). Default vertical.</summary>
    public DecentSamplerImageOrientation? CustomSkinImageOrientation { get; internal set; }

    /// <summary>How far the mouse must move to change the value (<c>mouseDragSensitivity</c>).</summary>
    public int? MouseDragSensitivity { get; internal set; }

    /// <summary>
    /// The <c>&lt;state&gt;</c> elements of a multi-state control, in document order. Empty for any
    /// other value type.
    /// </summary>
    public IReadOnlyList<DecentSamplerUiState> States => _states;

    internal void Add(DecentSamplerUiState state)
    {
        state.Index = _states.Count;
        _states.Add(state);
        AddChild(state);
    }
}
