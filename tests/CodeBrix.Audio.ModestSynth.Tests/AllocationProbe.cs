using System;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// Measures how much a piece of work allocates on the calling thread, robustly enough to assert
/// that the answer is zero.
/// </summary>
/// <remarks>
/// <para>
/// <c>GC.GetAllocatedBytesForCurrentThread</c> reads the thread's allocation context, and a
/// collection triggered by ANOTHER thread in the middle of a measurement retires that context and
/// leaves the difference overstated by whatever was unused. Under a parallel test run that happens
/// often enough to make a single measurement flaky - it showed up here as a render that
/// "allocated" 1,320 bytes once in every few full runs and nothing at all when run alone.
/// </para>
/// <para>
/// Repeating the measurement and keeping the LOWEST answer removes that noise without weakening
/// the assertion: work that really does allocate allocates on every attempt, so its lowest
/// measurement is still positive.
/// </para>
/// </remarks>
public static class AllocationProbe
{
    /// <summary>How many times a measurement is repeated before the lowest answer is taken.</summary>
    public const int DefaultAttempts = 5;

    /// <summary>
    /// Runs the work several times and returns the smallest number of bytes any one run allocated.
    /// </summary>
    /// <param name="work">
    /// The work to measure. Create the delegate BEFORE calling this - a closure allocates when it
    /// is built, not when it is invoked.
    /// </param>
    /// <returns>The lowest measured allocation in bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="work" /> is null.</exception>
    public static long LowestBytes(Action work) => LowestBytes(work, DefaultAttempts);

    /// <summary>
    /// Runs the work a given number of times and returns the smallest number of bytes any one run
    /// allocated.
    /// </summary>
    /// <param name="work">The work to measure.</param>
    /// <param name="attempts">How many times to repeat it. At least one.</param>
    /// <returns>The lowest measured allocation in bytes.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="work" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="attempts" /> is not positive.</exception>
    public static long LowestBytes(Action work, int attempts)
    {
        if (work == null) { throw new ArgumentNullException(nameof(work)); }

        if (attempts < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(attempts), attempts, "At least one attempt is needed.");
        }

        long lowest = long.MaxValue;

        for (int attempt = 0; attempt < attempts; attempt++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            work();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            if (allocated < lowest) { lowest = allocated; }
            if (lowest <= 0L) { break; }
        }

        return lowest;
    }
}
