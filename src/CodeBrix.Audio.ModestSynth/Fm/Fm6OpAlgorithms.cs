using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.ModestSynth.Fm;

/// <summary>
/// The 32 operator routings the <c>fmAlgorithm</c> attribute chooses between, built once and
/// shared.
/// </summary>
/// <remarks>
/// <para>
/// The table is the published algorithm chart of the six-operator hardware the format names, and
/// the numbering matches, so <c>fmAlgorithm="19"</c> here is algorithm 19 there: one modulator on
/// operator 6 feeding both operator 4 and operator 5, with operators 1, 4 and 5 heard.
/// </para>
/// <para>
/// Only two things are written down for each algorithm - which operator modulates which, and where
/// the feedback loop runs. The carriers are DERIVED (an operator nothing takes as modulation is
/// heard) and the evaluation order is derived too (a topological sort, so every modulator is
/// computed before whatever it feeds). Deriving them is why the table cannot disagree with itself.
/// </para>
/// </remarks>
public static class Fm6OpAlgorithms
{
    /// <summary>How many operators an <c>fm6op</c> oscillator has.</summary>
    public const int OperatorCount = 6;

    /// <summary>How many algorithms there are; <c>fmAlgorithm</c> runs from 1 to this.</summary>
    public const int Count = 32;

    /// <summary>The algorithm <c>fmAlgorithm</c> defaults to.</summary>
    public const int DefaultAlgorithm = 1;

    // One row per algorithm, each a flat list of modulator/target pairs. The feedback path is NOT
    // in here: it is a loop rather than a routing, it is applied a sample late, and counting it
    // would wrongly demote a carrier (algorithm 4's operator 4 feeds the loop and is still heard).
    private static readonly int[][] Routings =
    {
        /*  1 */ new[] { 2, 1, 6, 5, 5, 4, 4, 3 },
        /*  2 */ new[] { 2, 1, 6, 5, 5, 4, 4, 3 },
        /*  3 */ new[] { 3, 2, 2, 1, 6, 5, 5, 4 },
        /*  4 */ new[] { 3, 2, 2, 1, 6, 5, 5, 4 },
        /*  5 */ new[] { 2, 1, 4, 3, 6, 5 },
        /*  6 */ new[] { 2, 1, 4, 3, 6, 5 },
        /*  7 */ new[] { 2, 1, 4, 3, 5, 3, 6, 5 },
        /*  8 */ new[] { 2, 1, 4, 3, 5, 3, 6, 5 },
        /*  9 */ new[] { 2, 1, 4, 3, 5, 3, 6, 5 },
        /* 10 */ new[] { 3, 2, 2, 1, 5, 4, 6, 4 },
        /* 11 */ new[] { 3, 2, 2, 1, 5, 4, 6, 4 },
        /* 12 */ new[] { 2, 1, 4, 3, 5, 3, 6, 3 },
        /* 13 */ new[] { 2, 1, 4, 3, 5, 3, 6, 3 },
        /* 14 */ new[] { 2, 1, 4, 3, 5, 4, 6, 4 },
        /* 15 */ new[] { 2, 1, 4, 3, 5, 4, 6, 4 },
        /* 16 */ new[] { 2, 1, 3, 1, 4, 3, 5, 1, 6, 5 },
        /* 17 */ new[] { 2, 1, 3, 1, 4, 3, 5, 1, 6, 5 },
        /* 18 */ new[] { 2, 1, 3, 1, 4, 1, 5, 4, 6, 5 },
        /* 19 */ new[] { 3, 2, 2, 1, 6, 4, 6, 5 },
        /* 20 */ new[] { 3, 1, 3, 2, 5, 4, 6, 4 },
        /* 21 */ new[] { 3, 1, 3, 2, 6, 4, 6, 5 },
        /* 22 */ new[] { 2, 1, 6, 3, 6, 4, 6, 5 },
        /* 23 */ new[] { 3, 2, 6, 4, 6, 5 },
        /* 24 */ new[] { 6, 3, 6, 4, 6, 5 },
        /* 25 */ new[] { 6, 4, 6, 5 },
        /* 26 */ new[] { 3, 2, 5, 4, 6, 4 },
        /* 27 */ new[] { 3, 2, 5, 4, 6, 4 },
        /* 28 */ new[] { 2, 1, 4, 3, 5, 4 },
        /* 29 */ new[] { 4, 3, 6, 5 },
        /* 30 */ new[] { 4, 3, 5, 4 },
        /* 31 */ new[] { 6, 5 },
        /* 32 */ new int[0],
    };

    // Where the feedback loop starts and where it arrives. They differ in exactly two algorithms:
    // in 4 the loop runs 6 -> 5 -> 4 and back into 6, and in 6 it runs 6 -> 5 and back into 6.
    private static readonly int[] FeedbackSources =
    {
        6, 2, 6, 4, 6, 5, 6, 4, 2, 3, 6, 2, 6, 6, 2, 6,
        2, 3, 6, 3, 3, 6, 6, 6, 6, 6, 3, 5, 6, 5, 6, 6,
    };

    private static readonly int[] FeedbackDestinations =
    {
        6, 2, 6, 6, 6, 6, 6, 4, 2, 3, 6, 2, 6, 6, 2, 6,
        2, 3, 6, 3, 3, 6, 6, 6, 6, 6, 3, 5, 6, 5, 6, 6,
    };

    private static readonly Fm6OpAlgorithm[] All = Build();

    /// <summary>Every algorithm, in order, so <c>Algorithms[0]</c> is algorithm 1.</summary>
    public static IReadOnlyList<Fm6OpAlgorithm> Algorithms => All;

    /// <summary>The algorithm a number selects.</summary>
    /// <param name="number">The algorithm number, 1 to <see cref="Count" />.</param>
    /// <returns>The routing; never null.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="number" /> is outside 1 to 32.</exception>
    public static Fm6OpAlgorithm Get(int number)
    {
        if (number < 1 || number > Count)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number,
                "fmAlgorithm runs from 1 to " + Count + ".");
        }

        return All[number - 1];
    }

    /// <summary>
    /// Brings an algorithm number into range the way a preset attribute is treated everywhere in
    /// this package: by clamping rather than by complaining.
    /// </summary>
    /// <param name="number">Any number.</param>
    /// <returns>The same number clamped to 1 to <see cref="Count" />.</returns>
    public static int Clamp(int number) => number < 1 ? 1 : number > Count ? Count : number;

    private static Fm6OpAlgorithm[] Build()
    {
        Fm6OpAlgorithm[] built = new Fm6OpAlgorithm[Count];

        for (int index = 0; index < Count; index++)
        {
            int[] routing = Routings[index];

            List<int>[] sources = new List<int>[OperatorCount];
            bool[] isModulator = new bool[OperatorCount];
            for (int i = 0; i < OperatorCount; i++) { sources[i] = new List<int>(); }

            for (int i = 0; i < routing.Length; i += 2)
            {
                int modulator = routing[i];
                int target = routing[i + 1];
                sources[target - 1].Add(modulator);
                isModulator[modulator - 1] = true;
            }

            int[][] modulatorsOf = new int[OperatorCount][];
            for (int i = 0; i < OperatorCount; i++)
            {
                sources[i].Sort();
                modulatorsOf[i] = sources[i].ToArray();
            }

            List<int> carriers = new List<int>();
            List<int> modulators = new List<int>();
            for (int i = 0; i < OperatorCount; i++)
            {
                if (isModulator[i]) { modulators.Add(i + 1); } else { carriers.Add(i + 1); }
            }

            built[index] = new Fm6OpAlgorithm(index + 1, carriers.ToArray(), modulators.ToArray(),
                TopologicalOrder(modulatorsOf), modulatorsOf,
                FeedbackSources[index], FeedbackDestinations[index]);
        }

        return built;
    }

    // A modulator is placed before whatever it feeds, and among the operators whose own
    // modulators are all placed the lowest-numbered goes first, so the order is the same on every
    // machine and every run.
    private static int[] TopologicalOrder(int[][] modulatorsOf)
    {
        int[] order = new int[OperatorCount];
        bool[] placed = new bool[OperatorCount];

        for (int count = 0; count < OperatorCount; count++)
        {
            int chosen = 0;

            for (int candidate = 1; candidate <= OperatorCount && chosen == 0; candidate++)
            {
                if (placed[candidate - 1]) { continue; }

                bool ready = true;
                int[] sources = modulatorsOf[candidate - 1];
                for (int i = 0; i < sources.Length; i++)
                {
                    if (!placed[sources[i] - 1]) { ready = false; break; }
                }

                if (ready) { chosen = candidate; }
            }

            order[count] = chosen;
            placed[chosen - 1] = true;
        }

        return order;
    }
}
