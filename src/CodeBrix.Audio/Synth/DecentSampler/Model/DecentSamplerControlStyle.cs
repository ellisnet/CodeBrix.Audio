namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// The visual form of a user-interface control.
/// </summary>
public enum DecentSamplerControlStyle
{
    /// <summary>A horizontal filled bar (<c>linear_bar</c>).</summary>
    LinearBar,

    /// <summary>A vertical filled bar (<c>linear_bar_vertical</c>).</summary>
    LinearBarVertical,

    /// <summary>A horizontal slider (<c>linear_horizontal</c>).</summary>
    LinearHorizontal,

    /// <summary>A vertical slider (<c>linear_vertical</c>).</summary>
    LinearVertical,

    /// <summary>A rotary dial (<c>rotary</c>).</summary>
    Rotary,

    /// <summary>A rotary dial dragged horizontally (<c>rotary_horizontal_drag</c>).</summary>
    RotaryHorizontalDrag,

    /// <summary>A rotary dial dragged in either direction (<c>rotary_horizontal_vertical_drag</c>).</summary>
    RotaryHorizontalVerticalDrag,

    /// <summary>A rotary dial dragged vertically (<c>rotary_vertical_drag</c>). The default.</summary>
    RotaryVerticalDrag,

    /// <summary>A skinned control dragged vertically (<c>custom_skin_vertical_drag</c>).</summary>
    CustomSkinVerticalDrag,

    /// <summary>A skinned control dragged horizontally (<c>custom_skin_horizontal_drag</c>).</summary>
    CustomSkinHorizontalDrag,

    /// <summary>A skinned control dragged in either direction (<c>custom_skin_horizontal_vertical_drag</c>).</summary>
    CustomSkinHorizontalVerticalDrag,
}
