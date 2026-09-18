using System.Collections.Generic;

namespace CodeBrix.Audio.Abc;

/// <summary>
/// One bar of one voice: what it holds, the bar lines around it, and the repeat endings it belongs
/// to.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="OpeningBarLine"/> and <see cref="ClosingBarLine"/> are the lines on either side, and
/// the line closing one bar is the SAME instance as the one opening the next, so a <c>|:</c> or a
/// <c>:|</c> is never counted twice. The first bar of a voice opens with
/// <see cref="AbcBarLine.None"/> unless the music began with a bar line.
/// </para>
/// <para>
/// <see cref="Endings"/> holds the numbers from a <c>[1</c> or <c>|2</c> mark, including the lists
/// and ranges the standard allows - <c>[1,3</c> and <c>[1-3</c>. It is empty for an ordinary bar,
/// which is played on every pass.
/// </para>
/// </remarks>
public sealed class AbcBar
{
    private readonly List<AbcElement> _elements;
    private readonly List<int> _endings;

    /// <summary>
    /// Creates a bar.
    /// </summary>
    /// <param name="elements">What the bar holds, in the order written.</param>
    /// <param name="openingBarLine">The bar line before this bar.</param>
    /// <param name="closingBarLine">The bar line after this bar.</param>
    /// <param name="endings">The repeat endings this bar belongs to; empty for an ordinary bar.</param>
    public AbcBar(
        IEnumerable<AbcElement> elements,
        AbcBarLine openingBarLine,
        AbcBarLine closingBarLine,
        IEnumerable<int> endings)
    {
        _elements = elements == null ? new List<AbcElement>() : new List<AbcElement>(elements);
        OpeningBarLine = openingBarLine ?? AbcBarLine.None;
        ClosingBarLine = closingBarLine ?? AbcBarLine.None;
        _endings = endings == null ? new List<int>() : new List<int>(endings);
    }

    /// <summary>What the bar holds, in the order written.</summary>
    public IReadOnlyList<AbcElement> Elements => _elements;

    /// <summary>The bar line before this bar.</summary>
    public AbcBarLine OpeningBarLine { get; }

    /// <summary>The bar line after this bar.</summary>
    public AbcBarLine ClosingBarLine { get; }

    /// <summary>
    /// The repeat endings this bar belongs to, in ascending order. Empty for an ordinary bar, which
    /// is played on every pass.
    /// </summary>
    public IReadOnlyList<int> Endings => _endings;

    /// <summary>Describes the bar.</summary>
    /// <returns>For example <c>"bar of 8 elements"</c>.</returns>
    public override string ToString() => $"bar of {_elements.Count} element(s)";
}
