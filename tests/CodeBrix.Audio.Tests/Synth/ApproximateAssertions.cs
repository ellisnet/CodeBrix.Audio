using System;
using SilverAssertions;

namespace CodeBrix.Audio.Tests.Synth;

// Delta-tolerance equality for the translated MeltySynth tests (upstream used
// NUnit's Is.EqualTo(expected).Within(delta) and Math.Abs(error) < delta checks).
// When the tolerance is exceeded, the exact Be() assertion runs so the failure
// message shows the expected and actual values. Mirrors the same helper in
// Doom.Brix.GameEngine.Tests.
internal static class ApproximateAssertions
{
    public static void ShouldBeApproximately(this double actual, double expected, double delta)
    {
        if (double.IsNaN(actual) || Math.Abs(actual - expected) > delta)
        {
            actual.Should().Be(expected);
        }
    }

    public static void ShouldBeApproximately(this float actual, double expected, double delta) =>
        ((double)actual).ShouldBeApproximately(expected, delta);

    public static void ShouldBeApproximately(this int actual, double expected, double delta) =>
        ((double)actual).ShouldBeApproximately(expected, delta);
}
