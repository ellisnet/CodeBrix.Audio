using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// A <c>&lt;button&gt;</c>: a control that steps through its states, firing each state's bindings.
/// </summary>
public sealed class DecentSamplerUiButton : DecentSamplerUiElement
{
    private readonly List<DecentSamplerUiState> _states = [];

    /// <summary>The 0-based index of the state that starts selected (<c>value</c>). Default 0.</summary>
    public double? Value { get; internal set; }

    /// <summary>A name for the button (<c>name</c>). Display only; the state names carry the text.</summary>
    public string Name { get; internal set; }

    /// <summary>The name a host shows for this parameter (<c>parameterName</c>).</summary>
    public string ParameterName { get; internal set; }

    /// <summary>The state a double-click restores (<c>defaultValue</c>).</summary>
    public double? DefaultValue { get; internal set; }

    /// <summary>Whether the button draws text or images (<c>style</c>). Default text.</summary>
    public DecentSamplerButtonStyle? Style { get; internal set; }

    /// <summary>The <c>style</c> attribute exactly as written.</summary>
    public string StyleName { get; internal set; }

    /// <summary>The image shown in every state that does not override it (<c>mainImage</c>).</summary>
    public string MainImage { get; internal set; }

    /// <summary>The hover image shown in every state that does not override it (<c>hoverImage</c>).</summary>
    public string HoverImage { get; internal set; }

    /// <summary>The click image shown in every state that does not override it (<c>clickImage</c>).</summary>
    public string ClickImage { get; internal set; }

    /// <summary>How opaque the button is while disabled (<c>disabledOpacity</c>). Default 0.5.</summary>
    public double? DisabledOpacity { get; internal set; }

    /// <summary>The button's states, in document order.</summary>
    public IReadOnlyList<DecentSamplerUiState> States => _states;

    internal void Add(DecentSamplerUiState state)
    {
        state.Index = _states.Count;
        _states.Add(state);
        AddChild(state);
    }
}
