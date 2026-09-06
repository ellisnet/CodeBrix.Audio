using System;
using SilverAssertions;

namespace CodeBrix.Audio.Tests.Synth;

/// <summary>
/// Holding a render to values pinned in a test, in the one way that holds on every operating system.
/// </summary>
/// <remarks>
/// <para>
/// A render cannot be pinned bit for bit across platforms, and a test that tries to is a test that
/// fails on whichever machine did not record it. The synthesizers reach the platform's maths library -
/// <c>MathF.Pow</c> in the gain and envelope paths compiles to <c>powf</c>, which is UCRT on Windows,
/// glibc on Linux and Apple's libm on macOS - and none of the three is correctly rounded. They
/// disagree by one ulp on a good fraction of arguments, and a resonant biquad can sustain that
/// difference through its recursion rather than damping it away.
/// </para>
/// <para>
/// So the fence is a tolerance instead, and the gap it has to live in is enormous. The largest
/// cross-platform difference measured on these renders is 5.4e-7; the smallest defect worth catching -
/// one cent of detune - moves samples by 1.6e-2, and one frame of loop drift moves them by 1.1e-1.
/// <see cref="SampleTolerance"/> sits between the two with four orders of margin on each side. See
/// MAINTAINER-README, "PINNED RENDERS AND THE PLATFORM MATHS LIBRARY", for how those numbers were
/// taken and what to do if a platform ever lands outside them.
/// </para>
/// <para>
/// The samples are compared one at a time, spread across the render, rather than as an aggregate. An
/// aggregate is the tempting shape and the wrong one: the RMS of a sine does not depend on its phase,
/// so a block of them would say nothing about the loop drift these tests exist to catch. Individual
/// samples carry the phase, and a drift of one frame moves every one of them.
/// </para>
/// </remarks>
internal static class PinnedRender
{
    /// <summary>How far a sample may sit from its pinned value before the render has changed.</summary>
    public const double SampleTolerance = 1e-4;

    /// <summary>How far a whole render's sum may drift. Aggregate cover over the samples not spot-checked.</summary>
    public const double SumTolerance = 1e-3;

    /// <summary>
    /// Asserts that the render still holds its pinned values, reading one sample every
    /// <paramref name="stride"/> frames.
    /// </summary>
    /// <param name="render">The channel to check.</param>
    /// <param name="stride">Frames between the samples that were pinned.</param>
    /// <param name="reference">The pinned values, in order from frame zero.</param>
    public static void ShouldStillRender(float[] render, int stride, double[] reference)
    {
        render.Length.Should().BeGreaterThanOrEqualTo(
            (reference.Length - 1) * stride + 1,
            "the pinned values must all fall inside the render");

        for (var index = 0; index < reference.Length; index++)
        {
            var frame = index * stride;

            ((double)render[frame]).Should().BeApproximately(
                reference[index],
                SampleTolerance,
                "frame {0} must still render what it always did",
                frame);
        }
    }

    /// <summary>Sums a render, as one number standing for every sample rather than the pinned few.</summary>
    /// <param name="samples">The channel to sum.</param>
    /// <returns>The total.</returns>
    public static double Sum(float[] samples)
    {
        var total = 0.0;

        foreach (var sample in samples)
        {
            total += sample;
        }

        return total;
    }
}
