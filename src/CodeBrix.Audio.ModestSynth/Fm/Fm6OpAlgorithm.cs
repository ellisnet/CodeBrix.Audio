using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.ModestSynth.Fm;

/// <summary>
/// One of the 32 operator routings the <c>fmAlgorithm</c> attribute selects: which operators are
/// heard, which modulates which, and where the feedback loop is.
/// </summary>
/// <remarks>
/// <para>
/// Operators are numbered 1 to 6, the way the <c>fmOpN...</c> attributes number them. An operator
/// is a CARRIER when nothing takes its output as modulation - its sound is added to what you hear
/// - and a MODULATOR when it feeds another operator's phase. Every operator is exactly one of the
/// two, in every algorithm.
/// </para>
/// <para>
/// The topologies are the published charts of the six-operator hardware the format names; the
/// algorithm numbers are the same numbers, so a patch bank's algorithm number can be used as it
/// stands.
/// </para>
/// </remarks>
public sealed class Fm6OpAlgorithm
{
    private readonly int[] carriers;
    private readonly int[] modulators;
    private readonly int[] renderOrder;
    private readonly int[][] modulatorsOf;

    internal Fm6OpAlgorithm(int number, int[] carriers, int[] modulators, int[] renderOrder,
        int[][] modulatorsOf, int feedbackSource, int feedbackDestination)
    {
        Number = number;
        this.carriers = carriers;
        this.modulators = modulators;
        this.renderOrder = renderOrder;
        this.modulatorsOf = modulatorsOf;
        FeedbackSource = feedbackSource;
        FeedbackDestination = feedbackDestination;
    }

    /// <summary>The algorithm number, 1 to 32, as <c>fmAlgorithm</c> spells it.</summary>
    public int Number { get; }

    /// <summary>
    /// The operators whose output is heard, in ascending order. Their sum, scaled by
    /// <see cref="Fm6OpOscillator.OutputGain" />, is what the oscillator renders.
    /// </summary>
    public IReadOnlyList<int> Carriers => carriers;

    /// <summary>
    /// The operators that are not heard directly because they modulate another operator, in
    /// ascending order.
    /// </summary>
    public IReadOnlyList<int> Modulators => modulators;

    /// <summary>
    /// All six operators in an order that computes every modulator before whatever it feeds, which
    /// is the order the oscillator evaluates them in.
    /// </summary>
    public IReadOnlyList<int> RenderOrder => renderOrder;

    /// <summary>
    /// The operator whose output travels back round the feedback loop - operator 6 in most
    /// algorithms, and the top of the loop in the two where the loop spans several operators.
    /// </summary>
    public int FeedbackSource { get; }

    /// <summary>
    /// The operator whose phase the feedback loop arrives at, and therefore the operator whose
    /// <c>fmOpNFeedback</c> attribute the algorithm listens to.
    /// </summary>
    /// <remarks>
    /// It is operator 6 in 20 of the 32 algorithms, which is why the format calls
    /// <c>fmOp6Feedback</c> the feedback control. See
    /// <see cref="Fm6OpOscillator.UseOperator6FeedbackFallback" /> for how the other twelve are
    /// handled.
    /// </remarks>
    public int FeedbackDestination { get; }

    /// <summary>Whether an operator's output is heard directly.</summary>
    /// <param name="operatorNumber">The operator, 1 to 6.</param>
    /// <returns><see langword="true" /> when it is a carrier.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="operatorNumber" /> is outside 1 to 6.</exception>
    public bool IsCarrier(int operatorNumber)
    {
        RequireOperator(operatorNumber);

        for (int i = 0; i < carriers.Length; i++)
        {
            if (carriers[i] == operatorNumber) { return true; }
        }

        return false;
    }

    /// <summary>The operators that modulate a given operator's phase, in ascending order.</summary>
    /// <param name="operatorNumber">The operator, 1 to 6.</param>
    /// <returns>Their numbers; an empty list when nothing modulates it.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="operatorNumber" /> is outside 1 to 6.</exception>
    public IReadOnlyList<int> GetModulatorsOf(int operatorNumber)
    {
        RequireOperator(operatorNumber);
        return modulatorsOf[operatorNumber - 1];
    }

    /// <summary>Whether one operator feeds another's phase in this algorithm.</summary>
    /// <param name="modulator">The operator that would be doing the modulating, 1 to 6.</param>
    /// <param name="target">The operator that would be modulated, 1 to 6.</param>
    /// <returns><see langword="true" /> when the routing exists.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Either operator number is outside 1 to 6.</exception>
    public bool Modulates(int modulator, int target)
    {
        RequireOperator(modulator);
        RequireOperator(target);

        int[] sources = modulatorsOf[target - 1];
        for (int i = 0; i < sources.Length; i++)
        {
            if (sources[i] == modulator) { return true; }
        }

        return false;
    }

    /// <summary>The algorithm number and its carriers, for diagnostics.</summary>
    /// <returns>Something like <c>"algorithm 5: carriers 1, 3, 5; feedback 6"</c>.</returns>
    public override string ToString()
        => "algorithm " + Number + ": carriers " + string.Join(", ", carriers) +
           "; feedback " + (FeedbackSource == FeedbackDestination
               ? FeedbackDestination.ToString()
               : FeedbackSource + "->" + FeedbackDestination);

    internal int[] CarrierNumbers => carriers;

    internal int[] RenderOrderNumbers => renderOrder;

    internal int[][] ModulatorTable => modulatorsOf;

    private static void RequireOperator(int operatorNumber)
    {
        if (operatorNumber < 1 || operatorNumber > Fm6OpAlgorithms.OperatorCount)
        {
            throw new ArgumentOutOfRangeException(nameof(operatorNumber), operatorNumber,
                "An fm6op oscillator has operators 1 to " + Fm6OpAlgorithms.OperatorCount + ".");
        }
    }
}
