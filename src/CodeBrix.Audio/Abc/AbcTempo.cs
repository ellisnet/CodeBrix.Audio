using System.Collections.Generic;
using System.Globalization;

namespace CodeBrix.Audio.Abc;

/// <summary>
/// The tempo from a <c>Q:</c> field.
/// </summary>
/// <remarks>
/// <para>
/// The standard allows a beat and a rate - <c>Q:1/4=120</c> - up to four beats that are summed -
/// <c>Q:1/4 3/8 1/4 3/8=40</c>, which means the same as <c>Q:5/4=40</c> - a text label before or
/// after the value, a label on its own with no value at all, and the deprecated bare number, which
/// counts UNIT NOTE LENGTHS per minute rather than quarter notes.
/// </para>
/// <para>
/// <see cref="QuarterNotesPerMinute"/> is the one number playback needs, and it is what becomes the
/// <c>TempoEvent</c>. A tempo with no value at all - <c>Q:"Allegro"</c> - carries none, and the
/// conversion falls back to <see cref="AbcToMidiOptions.DefaultBeatsPerMinute"/>.
/// </para>
/// </remarks>
public sealed class AbcTempo
{
    private readonly List<AbcDuration> _beats;

    /// <summary>
    /// Creates a tempo.
    /// </summary>
    /// <param name="beats">The beat lengths written, in order. Empty when the field carried no value.</param>
    /// <param name="beatsPerMinute">How many of those beats, summed, are played per minute.</param>
    /// <param name="hasValue">Whether the field carried a tempo value at all.</param>
    /// <param name="label">The quoted text, or an empty string.</param>
    /// <param name="text">The field value exactly as it was written.</param>
    public AbcTempo(
        IEnumerable<AbcDuration> beats,
        double beatsPerMinute,
        bool hasValue,
        string label,
        string text)
    {
        _beats = beats == null ? new List<AbcDuration>() : new List<AbcDuration>(beats);
        BeatsPerMinute = beatsPerMinute;
        HasValue = hasValue;
        Label = label ?? string.Empty;
        Text = text ?? string.Empty;
    }

    /// <summary>The beat lengths written, in order. Empty when the field carried no value.</summary>
    public IReadOnlyList<AbcDuration> Beats => _beats;

    /// <summary>
    /// The sum of <see cref="Beats"/> - the beat the rate actually counts.
    /// </summary>
    public AbcDuration BeatLength
    {
        get
        {
            var total = AbcDuration.Zero;
            for (int i = 0; i < _beats.Count; i++)
            {
                total += _beats[i];
            }

            return total;
        }
    }

    /// <summary>How many beats of <see cref="BeatLength"/> are played per minute.</summary>
    public double BeatsPerMinute { get; }

    /// <summary>Whether the field carried a tempo value, as opposed to a label on its own.</summary>
    public bool HasValue { get; }

    /// <summary>The quoted text, such as <c>Allegro</c>, or an empty string.</summary>
    public string Label { get; }

    /// <summary>The field value exactly as it was written.</summary>
    public string Text { get; }

    /// <summary>
    /// The tempo in quarter notes per minute, which is what a MIDI tempo event holds. Zero when the
    /// field carried no value.
    /// </summary>
    public double QuarterNotesPerMinute => HasValue ? BeatsPerMinute * BeatLength.Value * 4.0 : 0.0;

    /// <summary>Describes the tempo.</summary>
    /// <returns>For example <c>"1/4=120"</c>.</returns>
    public override string ToString() =>
        HasValue
            ? string.Create(CultureInfo.InvariantCulture, $"{BeatLength}={BeatsPerMinute}")
            : Label;
}
