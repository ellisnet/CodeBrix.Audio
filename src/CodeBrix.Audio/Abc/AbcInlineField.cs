namespace CodeBrix.Audio.Abc;

/// <summary>
/// A field that changes the tune's state from this point on, written inside the body.
/// </summary>
/// <remarks>
/// <para>
/// Abc allows a field inline - <c>[K:G]</c>, <c>[M:3/4]</c>, <c>[L:1/16]</c>, <c>[Q:1/4=90]</c> -
/// and on a line of its own inside the body; both mean the same thing and both arrive here. The
/// change takes effect where it stands and holds until the next one.
/// </para>
/// <para>
/// A <c>V:</c> field switches voice rather than changing state, so it never appears as an element:
/// the material after it simply belongs to the other voice.
/// </para>
/// </remarks>
public sealed class AbcInlineField : AbcElement
{
    /// <summary>
    /// Creates an inline field change.
    /// </summary>
    /// <param name="field">The field letter, such as <c>K</c>.</param>
    /// <param name="text">The field value exactly as it was written.</param>
    /// <param name="key">The new key, when this is a <c>K:</c> field.</param>
    /// <param name="meter">The new meter, when this is an <c>M:</c> field.</param>
    /// <param name="unitNoteLength">The new unit note length, when this is an <c>L:</c> field.</param>
    /// <param name="tempo">The new tempo, when this is a <c>Q:</c> field.</param>
    public AbcInlineField(
        char field,
        string text,
        AbcKey key,
        AbcMeter meter,
        AbcUnitNoteLength unitNoteLength,
        AbcTempo tempo)
    {
        Field = field;
        Text = text ?? string.Empty;
        Key = key;
        Meter = meter;
        UnitNoteLength = unitNoteLength;
        Tempo = tempo;
    }

    /// <summary>The field letter: <c>K</c>, <c>M</c>, <c>L</c> or <c>Q</c>.</summary>
    public char Field { get; }

    /// <summary>The field value exactly as it was written.</summary>
    public string Text { get; }

    /// <summary>The new key, or <see langword="null"/> when this is not a <c>K:</c> field.</summary>
    public AbcKey Key { get; }

    /// <summary>The new meter, or <see langword="null"/> when this is not an <c>M:</c> field.</summary>
    public AbcMeter Meter { get; }

    /// <summary>
    /// The new unit note length, or <see langword="null"/> when this is not an <c>L:</c> field.
    /// </summary>
    public AbcUnitNoteLength UnitNoteLength { get; }

    /// <summary>The new tempo, or <see langword="null"/> when this is not a <c>Q:</c> field.</summary>
    public AbcTempo Tempo { get; }

    /// <summary>Describes the field change.</summary>
    /// <returns>For example <c>"[K:G]"</c>.</returns>
    public override string ToString() => $"[{Field}:{Text}]";
}
