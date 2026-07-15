using System.Text.Json;

namespace CodeBrix.Audio.Engine.Editing.Persistence;  //was previously: SoundFlow.Editing.Persistence

/// <summary>
/// Represents the serializable data for a single SoundModifier or AudioAnalyzer instance,
/// including its type and parameter values.
/// </summary>
public class ProjectEffectData
{
    /// <summary>
    /// Gets or sets the fully qualified assembly name of the SoundModifier or AudioAnalyzer type.
    /// Example: "CodeBrix.Audio.Engine.Modifiers.ParametricEqualizer, CodeBrix.Audio.Engine, Version=1.0.0.0, Culture=neutral, PublicKeyToken=null"
    /// </summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this effect/analyzer is currently enabled.
    /// </summary>
    public bool IsEnabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the JSON representation of the effect's/analyzer's parameters.
    /// This allows storing arbitrary parameter sets for different effect types.
    /// </summary>
    public JsonDocument? Parameters { get; set; }
}
