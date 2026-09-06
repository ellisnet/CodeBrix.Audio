using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Bindings;

/// <summary>
/// The user-interface parameter targets: everything a binding can change on a control, on the
/// on-screen keyboard's colour ranges, and on the interface background.
/// </summary>
/// <remarks>
/// <para>
/// Writing <c>VALUE</c> to a control does what turning the knob does: the control's own bindings fire
/// afterwards. That is the documented way to wire a MIDI controller to a knob, and it is why a preset
/// can put its whole mapping on one control and drive it from three places.
/// </para>
/// <para>
/// The purely visual parameters - position, size, colours, image paths, animation frames - are kept as
/// observable state on the control rather than dropped, so a renderer can follow them without this
/// library drawing anything.
/// </para>
/// </remarks>
internal sealed partial class DecentSamplerParameterFactory
{
    /// <summary>The target of a user-interface parameter, or null when the parameter is not one.</summary>
    /// <param name="control">The control.</param>
    /// <param name="parameter">The parameter token.</param>
    /// <returns>The target, or null.</returns>
    internal DecentSamplerParameter ForControl(DecentSamplerControl control, string parameter)
    {
        var name = "control[" + Ordinal(control.Index) + "]." + parameter;

        switch (Fold(parameter))
        {
            case "value":
                return Number(control, name, parameter, control.Value, value =>
                {
                    control.Value = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.Value));
                    _engine.FireControlBindings(control);
                });

            case "xvalue":
                return Number(control, name, parameter, control.XValue, value =>
                {
                    control.XValue = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.XValue));
                    _engine.FireControlAxis(control, horizontal: true);
                });

            case "yvalue":
                return Number(control, name, parameter, control.YValue, value =>
                {
                    control.YValue = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.YValue));
                    _engine.FireControlAxis(control, horizontal: false);
                });

            case "enabled":
                return Switch(control, name, parameter, control.Enabled, value =>
                {
                    control.Enabled = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.Enabled));
                });

            case "visible":
                return Switch(control, name, parameter, control.Visible, value =>
                {
                    control.Visible = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.Visible));
                });

            case "text":
                return Words(control, name, parameter, control.Text, value =>
                {
                    control.Text = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.Text));
                });

            case "minvalue":
                return Number(control, name, parameter, control.MinValue, value =>
                {
                    control.MinValue = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.MinValue));
                });

            case "maxvalue":
                return Number(control, name, parameter, control.MaxValue, value =>
                {
                    control.MaxValue = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.MaxValue));
                });

            case "valuetype":
                return Choice<DecentSamplerValueType>(control, name, parameter, control.ValueType, value =>
                {
                    control.ValueType = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.ValueType));
                });

            case "path":
                return Words(control, name, parameter, control.Path, value =>
                {
                    control.Path = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.Path));
                });

            case "opacity":
                return Number(control, name, parameter, control.Opacity, value =>
                {
                    control.Opacity = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.Opacity));
                });

            case "framerate":
                return Number(control, name, parameter, control.FrameRate, value =>
                {
                    control.FrameRate = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.FrameRate));
                });

            case "currentframe":
                return Integer(control, name, parameter, control.CurrentFrame, value =>
                {
                    control.CurrentFrame = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.CurrentFrame));
                });

            case "playbackmode":
                return Choice<DecentSamplerAnimationPlaybackMode>(
                    control, name, parameter, control.PlaybackMode, value =>
                    {
                        control.PlaybackMode = value;
                        control.RaiseChanged(nameof(DecentSamplerControl.PlaybackMode));
                    });

            case "x":
                return Number(control, name, parameter, control.X, value =>
                {
                    control.X = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.X));
                });

            case "y":
                return Number(control, name, parameter, control.Y, value =>
                {
                    control.Y = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.Y));
                });

            case "width":
                return Number(control, name, parameter, control.Width, value =>
                {
                    control.Width = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.Width));
                });

            case "height":
                return Number(control, name, parameter, control.Height, value =>
                {
                    control.Height = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.Height));
                });

            case "x1":
                return Number(control, name, parameter, control.X1, value =>
                {
                    control.X1 = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.X1));
                });

            case "y1":
                return Number(control, name, parameter, control.Y1, value =>
                {
                    control.Y1 = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.Y1));
                });

            case "x2":
                return Number(control, name, parameter, control.X2, value =>
                {
                    control.X2 = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.X2));
                });

            case "y2":
                return Number(control, name, parameter, control.Y2, value =>
                {
                    control.Y2 = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.Y2));
                });

            case "textcolor":
                return Words(control, name, parameter, control.TextColor, value =>
                {
                    control.TextColor = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.TextColor));
                });

            case "backgroundcolor":
                return Words(control, name, parameter, control.BackgroundColor, value =>
                {
                    control.BackgroundColor = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.BackgroundColor));
                });

            case "highlightedtextcolor":
                return Words(control, name, parameter, control.HighlightedTextColor, value =>
                {
                    control.HighlightedTextColor = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.HighlightedTextColor));
                });

            case "highlightedbackgroundcolor":
                return Words(control, name, parameter, control.HighlightedBackgroundColor, value =>
                {
                    control.HighlightedBackgroundColor = value;
                    control.RaiseChanged(nameof(DecentSamplerControl.HighlightedBackgroundColor));
                });

            default:
                return null;
        }
    }

    /// <summary>The target of a keyboard colour parameter, or null when the parameter is not one.</summary>
    /// <param name="color">The colour range.</param>
    /// <param name="parameter">The parameter token.</param>
    /// <returns>The target, or null.</returns>
    internal DecentSamplerParameter ForKeyboardColor(
        DecentSamplerUiKeyboardColor color, string parameter)
    {
        var name = "keyboardColor[" + Ordinal(color.Index) + "]." + parameter;

        return Fold(parameter) switch
        {
            "enabled" => Switch(
                color, name, parameter, color.Enabled ?? true, value => color.Enabled = value),
            "lonote" => Integer(
                color, name, parameter, color.LoNote ?? 0, value => color.LoNote = value),
            "hinote" => Integer(
                color, name, parameter, color.HiNote ?? 127, value => color.HiNote = value),
            "color" => Words(color, name, parameter, color.Color, value => color.Color = value),
            _ => null,
        };
    }

    /// <summary>The target of an interface-wide parameter, or null when the parameter is not one.</summary>
    /// <param name="parameter">The parameter token.</param>
    /// <returns>The target, or null.</returns>
    internal DecentSamplerParameter ForUi(string parameter)
    {
        var owner = _engine;
        var name = "ui." + parameter;

        return Fold(parameter) switch
        {
            "bgimage" => Words(
                owner, name, parameter, _engine.BackgroundImage,
                value => _engine.BackgroundImage = value),
            _ => null,
        };
    }
}
