using System.Collections.Generic;

namespace CodeBrix.Audio.ModestSynth.Tests.Fm;

/// <summary>
/// The published six-operator algorithm chart, written out here a SECOND time and by hand, so that
/// the table the library ships can be checked against something that was not derived from it.
/// </summary>
/// <remarks>
/// Each row lists the carriers (the operators that are heard) and the modulation paths as
/// "modulator to target" pairs, exactly as the chart draws them. The feedback loop is listed
/// separately because it is a loop, not a routing: in algorithms 4 and 6 it spans several
/// operators, and in the other thirty it is one operator feeding itself.
/// </remarks>
public static class Dx7AlgorithmChart
{
    /// <summary>How many algorithms the chart has.</summary>
    public const int Count = 32;

    private static readonly int[][] CarrierRows =
    {
        /*  1 */ new[] { 1, 3 },
        /*  2 */ new[] { 1, 3 },
        /*  3 */ new[] { 1, 4 },
        /*  4 */ new[] { 1, 4 },
        /*  5 */ new[] { 1, 3, 5 },
        /*  6 */ new[] { 1, 3, 5 },
        /*  7 */ new[] { 1, 3 },
        /*  8 */ new[] { 1, 3 },
        /*  9 */ new[] { 1, 3 },
        /* 10 */ new[] { 1, 4 },
        /* 11 */ new[] { 1, 4 },
        /* 12 */ new[] { 1, 3 },
        /* 13 */ new[] { 1, 3 },
        /* 14 */ new[] { 1, 3 },
        /* 15 */ new[] { 1, 3 },
        /* 16 */ new[] { 1 },
        /* 17 */ new[] { 1 },
        /* 18 */ new[] { 1 },
        /* 19 */ new[] { 1, 4, 5 },
        /* 20 */ new[] { 1, 2, 4 },
        /* 21 */ new[] { 1, 2, 4, 5 },
        /* 22 */ new[] { 1, 3, 4, 5 },
        /* 23 */ new[] { 1, 2, 4, 5 },
        /* 24 */ new[] { 1, 2, 3, 4, 5 },
        /* 25 */ new[] { 1, 2, 3, 4, 5 },
        /* 26 */ new[] { 1, 2, 4 },
        /* 27 */ new[] { 1, 2, 4 },
        /* 28 */ new[] { 1, 3, 6 },
        /* 29 */ new[] { 1, 2, 3, 5 },
        /* 30 */ new[] { 1, 2, 3, 6 },
        /* 31 */ new[] { 1, 2, 3, 4, 5 },
        /* 32 */ new[] { 1, 2, 3, 4, 5, 6 },
    };

    private static readonly int[][] RoutingRows =
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

    // Where the loop starts, then where it arrives. Only algorithms 4 and 6 differ from each other.
    private static readonly int[][] FeedbackRows =
    {
        /*  1 */ new[] { 6, 6 }, /*  2 */ new[] { 2, 2 }, /*  3 */ new[] { 6, 6 }, /*  4 */ new[] { 4, 6 },
        /*  5 */ new[] { 6, 6 }, /*  6 */ new[] { 5, 6 }, /*  7 */ new[] { 6, 6 }, /*  8 */ new[] { 4, 4 },
        /*  9 */ new[] { 2, 2 }, /* 10 */ new[] { 3, 3 }, /* 11 */ new[] { 6, 6 }, /* 12 */ new[] { 2, 2 },
        /* 13 */ new[] { 6, 6 }, /* 14 */ new[] { 6, 6 }, /* 15 */ new[] { 2, 2 }, /* 16 */ new[] { 6, 6 },
        /* 17 */ new[] { 2, 2 }, /* 18 */ new[] { 3, 3 }, /* 19 */ new[] { 6, 6 }, /* 20 */ new[] { 3, 3 },
        /* 21 */ new[] { 3, 3 }, /* 22 */ new[] { 6, 6 }, /* 23 */ new[] { 6, 6 }, /* 24 */ new[] { 6, 6 },
        /* 25 */ new[] { 6, 6 }, /* 26 */ new[] { 6, 6 }, /* 27 */ new[] { 3, 3 }, /* 28 */ new[] { 5, 5 },
        /* 29 */ new[] { 6, 6 }, /* 30 */ new[] { 5, 5 }, /* 31 */ new[] { 6, 6 }, /* 32 */ new[] { 6, 6 },
    };

    /// <summary>The operators the chart shows as carriers for an algorithm.</summary>
    /// <param name="algorithm">The algorithm number, 1 to 32.</param>
    /// <returns>The carrier numbers in ascending order.</returns>
    public static IReadOnlyList<int> Carriers(int algorithm) => CarrierRows[algorithm - 1];

    /// <summary>The modulation paths the chart shows for an algorithm, as modulator/target pairs.</summary>
    /// <param name="algorithm">The algorithm number, 1 to 32.</param>
    /// <returns>A flat list of pairs.</returns>
    public static IReadOnlyList<int> Routing(int algorithm) => RoutingRows[algorithm - 1];

    /// <summary>The operator whose output travels round the feedback loop.</summary>
    /// <param name="algorithm">The algorithm number, 1 to 32.</param>
    /// <returns>The operator number.</returns>
    public static int FeedbackSource(int algorithm) => FeedbackRows[algorithm - 1][0];

    /// <summary>The operator the feedback loop arrives at.</summary>
    /// <param name="algorithm">The algorithm number, 1 to 32.</param>
    /// <returns>The operator number.</returns>
    public static int FeedbackDestination(int algorithm) => FeedbackRows[algorithm - 1][1];

    /// <summary>Every algorithm number, for a theory that walks all 32.</summary>
    /// <returns>One single-element row per algorithm.</returns>
    public static IEnumerable<object[]> AllAlgorithms()
    {
        for (int algorithm = 1; algorithm <= Count; algorithm++)
        {
            yield return new object[] { algorithm };
        }
    }
}
