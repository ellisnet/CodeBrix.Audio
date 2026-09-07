using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler.Bindings;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>
/// One user-interface element as a live parameter: its current value, range, states and every piece of
/// state a binding can change. Changing a control fires its bindings, which is how a host moves an
/// instrument's knobs.
/// </summary>
/// <remarks>
/// <para>
/// This library does not draw anything. It keeps the interface as a parameter model because the
/// initial control values decide how the instrument sounds the moment it loads, and because a host
/// needs a list of knobs to expose. Everything a renderer would need - position, size, colours, image
/// paths, animation frames - is here and observable, so a future panel can watch it.
/// </para>
/// <para>
/// Every element under every tab gets a control, labels and images included, and
/// <see cref="Index"/> matches the position a binding's <c>controlIndex</c> or <c>position</c>
/// addresses. Do not filter this list down to the interactive elements.
/// </para>
/// </remarks>
public sealed class DecentSamplerControl
{
    private readonly DecentSamplerBindingEngine _engine;
    private readonly List<string> _states = [];
    private readonly List<string> _options = [];

    // Which state last fired its bindings. -1 is a real selection (nothing selected), so "never
    // fired" needs a value of its own.
    private int _firedStateIndex = int.MinValue;

    internal DecentSamplerControl(
        DecentSamplerBindingEngine engine, DecentSamplerUiElement source, int index)
    {
        _engine = engine;
        Source = source;
        Index = index;

        switch (source)
        {
            case DecentSamplerUiControl control:
                Kind = control.ElementName == "labeled-knob" || control.ElementName == "labeled_knob"
                    ? DecentSamplerControlKind.LabeledKnob
                    : DecentSamplerControlKind.Control;
                Label = control.Label;
                Name = control.ParameterName ?? control.Label;
                MinValue = control.MinValue ?? 0.0;
                MaxValue = control.MaxValue ?? 1.0;
                Value = control.Value ?? 0.0;
                DefaultValue = control.DefaultValue;
                ValueType = control.ValueType ?? DecentSamplerValueType.Float;
                TextColor = control.TextColor;
                foreach (var state in control.States)
                {
                    _states.Add(state.Name);
                }

                break;

            case DecentSamplerUiButton button:
                Kind = DecentSamplerControlKind.Button;
                Name = button.ParameterName ?? button.Name;
                Label = button.Name;
                MinValue = 0.0;
                MaxValue = Math.Max(0, button.States.Count - 1);
                Value = button.Value ?? 0.0;
                DefaultValue = button.DefaultValue;
                ValueType = DecentSamplerValueType.MultiState;
                Path = button.MainImage;
                foreach (var state in button.States)
                {
                    _states.Add(state.Name);
                }

                break;

            case DecentSamplerUiMenu menu:
                Kind = DecentSamplerControlKind.Menu;
                MinValue = 0.0;
                MaxValue = menu.Options.Count;
                Value = menu.Value ?? 0.0;
                ValueType = DecentSamplerValueType.Integer;
                TextColor = menu.TextColor;
                BackgroundColor = menu.BackgroundColor;
                HighlightedTextColor = menu.HighlightedTextColor;
                HighlightedBackgroundColor = menu.HighlightedBackgroundColor;
                Text = menu.PlaceholderText;
                foreach (var option in menu.Options)
                {
                    _options.Add(option.Name);
                }

                break;

            case DecentSamplerUiXyPad pad:
                Kind = DecentSamplerControlKind.XyPad;
                Name = pad.ParameterName;
                MinValue = 0.0;
                MaxValue = 1.0;
                XValue = pad.XValue ?? 0.0;
                YValue = pad.YValue ?? 0.0;
                BackgroundColor = pad.BgColor;
                break;

            case DecentSamplerUiLabel label:
                Kind = DecentSamplerControlKind.Label;
                Text = label.Text;
                TextColor = label.TextColor;
                break;

            case DecentSamplerUiMultiFrameImage animation:
                Kind = DecentSamplerControlKind.MultiFrameImage;
                Path = animation.Path;
                Opacity = animation.Opacity ?? 1.0;
                FrameRate = animation.FrameRate ?? 0.0;
                PlaybackMode = animation.PlaybackMode ?? DecentSamplerAnimationPlaybackMode.ForwardLoop;
                break;

            case DecentSamplerUiImage image:
                Kind = DecentSamplerControlKind.Image;
                Path = image.Path;
                Opacity = image.Opacity ?? 1.0;
                break;

            case DecentSamplerUiRectangle rectangle:
                Kind = DecentSamplerControlKind.Rectangle;
                BackgroundColor = rectangle.FillColor;
                break;

            case DecentSamplerUiLine line:
                Kind = DecentSamplerControlKind.Line;
                X1 = line.X1 ?? 0.0;
                Y1 = line.Y1 ?? 0.0;
                X2 = line.X2 ?? 0.0;
                Y2 = line.Y2 ?? 0.0;
                break;

            case DecentSamplerUiOscilloscope oscilloscope:
                Kind = DecentSamplerControlKind.Oscilloscope;
                BackgroundColor = oscilloscope.BackgroundColor;
                break;

            default:
                Kind = DecentSamplerControlKind.Unknown;
                break;
        }

        X = source.X ?? 0.0;
        Y = source.Y ?? 0.0;
        Width = source.Width ?? 0.0;
        Height = source.Height ?? 0.0;
        Visible = source.Visible ?? true;
        Enabled = source.Enabled ?? true;
        Tooltip = source.Tooltip;
        Tags = source.Tags;
        Name ??= Label ?? Text ?? Kind.ToString();
    }

    /// <summary>Raised after any property of the control has changed.</summary>
    public event EventHandler<DecentSamplerControlChangedEventArgs> Changed;

    /// <summary>The parsed element this control was built from.</summary>
    public DecentSamplerUiElement Source { get; }

    /// <summary>
    /// The control's 0-based position in <see cref="DecentSamplerInstrument.Controls"/>, which is what
    /// a binding's <c>controlIndex</c> or <c>position</c> addresses.
    /// </summary>
    public int Index { get; }

    /// <summary>Which kind of element this is.</summary>
    public DecentSamplerControlKind Kind { get; }

    /// <summary>
    /// A name for the control: its <c>parameterName</c>, or its label, or its kind when it has neither.
    /// </summary>
    public string Name { get; }

    /// <summary>The control's label, or null when it has none.</summary>
    public string Label { get; }

    /// <summary>The tags the control carries, which a binding can address it by.</summary>
    public IReadOnlyList<string> Tags { get; }

    /// <summary>The current value. For a menu this is the 1-based option number, 0 meaning none.</summary>
    public double Value { get; internal set; }

    /// <summary>The lowest value the control takes. Default 0.</summary>
    public double MinValue { get; internal set; }

    /// <summary>The highest value the control takes. Default 1.</summary>
    public double MaxValue { get; internal set; }

    /// <summary>The value a double-click restores, or null when the preset names none.</summary>
    public double? DefaultValue { get; }

    /// <summary>
    /// How the value is read: a float, a whole number, a state, or a musical-time subdivision. When it
    /// is <see cref="DecentSamplerValueType.MusicalTime"/>, <see cref="Value"/> is an index into the
    /// format's subdivision list - pass it to <see cref="DecentSamplerTempo"/> to turn it into beats or
    /// seconds.
    /// </summary>
    public DecentSamplerValueType ValueType { get; internal set; }

    /// <summary>The state names of a button or a multi-state control, in order. Empty for other kinds.</summary>
    public IReadOnlyList<string> States => _states;

    /// <summary>The option names of a menu, in order. Empty for other kinds.</summary>
    public IReadOnlyList<string> Options => _options;

    /// <summary>
    /// The selected state or menu option, 0-based, or -1 when nothing is selected. A menu's own
    /// <see cref="Value"/> is 1-based; this is the 0-based form of it.
    /// </summary>
    public int SelectedIndex
    {
        get
        {
            if (Kind == DecentSamplerControlKind.Menu)
            {
                var selected = (int)Math.Round(Value, MidpointRounding.AwayFromZero) - 1;
                return selected >= 0 && selected < _options.Count ? selected : -1;
            }

            if (_states.Count == 0)
            {
                return -1;
            }

            var index = (int)Math.Round(Value, MidpointRounding.AwayFromZero);
            return index < 0 ? 0 : index >= _states.Count ? _states.Count - 1 : index;
        }
    }

    /// <summary>Whether the control is drawn. Default true.</summary>
    public bool Visible { get; internal set; }

    /// <summary>Whether the control responds to input. Default true.</summary>
    public bool Enabled { get; internal set; }

    /// <summary>A label's text, or a menu's placeholder. Bindable through the <c>TEXT</c> parameter.</summary>
    public string Text { get; internal set; }

    /// <summary>An X-Y pad's horizontal value, 0 to 1.</summary>
    public double XValue { get; internal set; }

    /// <summary>An X-Y pad's vertical value, 0 to 1.</summary>
    public double YValue { get; internal set; }

    /// <summary>The control's left edge in pixels.</summary>
    public double X { get; internal set; }

    /// <summary>The control's top edge in pixels.</summary>
    public double Y { get; internal set; }

    /// <summary>The control's width in pixels.</summary>
    public double Width { get; internal set; }

    /// <summary>The control's height in pixels.</summary>
    public double Height { get; internal set; }

    /// <summary>A line's first x coordinate.</summary>
    public double X1 { get; internal set; }

    /// <summary>A line's first y coordinate.</summary>
    public double Y1 { get; internal set; }

    /// <summary>A line's second x coordinate.</summary>
    public double X2 { get; internal set; }

    /// <summary>A line's second y coordinate.</summary>
    public double Y2 { get; internal set; }

    /// <summary>The text colour, eight hexadecimal ARGB digits, or null when none was written.</summary>
    public string TextColor { get; internal set; }

    /// <summary>The background colour, eight hexadecimal ARGB digits, or null when none was written.</summary>
    public string BackgroundColor { get; internal set; }

    /// <summary>A menu's highlighted text colour, or null when none was written.</summary>
    public string HighlightedTextColor { get; internal set; }

    /// <summary>A menu's highlighted background colour, or null when none was written.</summary>
    public string HighlightedBackgroundColor { get; internal set; }

    /// <summary>An image or animation file, relative to the preset, or null when none was written.</summary>
    public string Path { get; internal set; }

    /// <summary>An image's opacity, 0 to 1. Default 1.</summary>
    public double Opacity { get; internal set; } = 1.0;

    /// <summary>An animation's frame rate in frames per second. The format's maximum is 24.</summary>
    public double FrameRate { get; internal set; }

    /// <summary>An animation's current frame, 0-based.</summary>
    public int CurrentFrame { get; internal set; }

    /// <summary>How an animation plays. Default forward loop.</summary>
    public DecentSamplerAnimationPlaybackMode PlaybackMode { get; internal set; } =
        DecentSamplerAnimationPlaybackMode.ForwardLoop;

    /// <summary>The tool tip text, or null when none was written.</summary>
    public string Tooltip { get; }

    /// <summary>
    /// Sets the control's value and fires its bindings, exactly as turning the knob in the reference
    /// player would.
    /// </summary>
    /// <param name="value">The new value. It is not clamped, matching the format's own behaviour.</param>
    public void SetValue(double value) => _engine.SetControlValue(this, value);

    /// <summary>Sets an X-Y pad's horizontal value, 0 to 1, and fires the pad's x-axis bindings.</summary>
    /// <param name="value">The new value.</param>
    public void SetXValue(double value) => _engine.SetControlXValue(this, value);

    /// <summary>Sets an X-Y pad's vertical value, 0 to 1, and fires the pad's y-axis bindings.</summary>
    /// <param name="value">The new value.</param>
    public void SetYValue(double value) => _engine.SetControlYValue(this, value);

    /// <summary>
    /// Selects one state of a button or multi-state control, or one option of a menu, and fires that
    /// state's or option's bindings.
    /// </summary>
    /// <param name="index">The 0-based state or option number.</param>
    public void Select(int index) =>
        SetValue(Kind == DecentSamplerControlKind.Menu ? index + 1 : index);

    /// <summary>Selects a state or menu option by name, case-insensitively.</summary>
    /// <param name="name">The state or option name.</param>
    /// <returns><see langword="true"/> when a state or option of that name exists.</returns>
    public bool Select(string name)
    {
        var names = Kind == DecentSamplerControlKind.Menu ? _options : _states;

        for (var index = 0; index < names.Count; index++)
        {
            if (!string.Equals(names[index], name, StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            Select(index);
            return true;
        }

        return false;
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Kind} {Index} \"{Name}\" = {Value}";

    internal void RaiseChanged(string propertyName) =>
        Changed?.Invoke(this, new DecentSamplerControlChangedEventArgs(this, propertyName));

    // Whether the selected state has actually moved since the last time its bindings fired.
    //
    // MEASURED (round 4, item 52): a button's state binding fires only on a STATE CHANGE. Setting a
    // button to the state it is already in does nothing at all, which is the practical difference
    // between latching a sequence from a button and latching it from a <cc> - a <cc> binding fires on
    // every controller CHANGE and so restarts the sequence each time.
    internal bool StateSelectionChanged(int selected)
    {
        if (_firedStateIndex == selected)
        {
            return false;
        }

        _firedStateIndex = selected;
        return true;
    }
}
