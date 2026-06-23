using System;
using CodeBrix.Audio.Dsp;
using CodeBrix.Audio.Wave;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.Dsp;
public class DenormalGuardTests
{
    [Fact]
    public void FlushZeroesSubnormalRiskValuesOnly()
    {
        // Unchanged: zero, audible values, and values above the 1e-15 threshold.
        Assert.Equal(0f, DenormalGuard.Flush(0f));
        Assert.Equal(1f, DenormalGuard.Flush(1f));
        Assert.Equal(-0.5f, DenormalGuard.Flush(-0.5f));
        Assert.Equal(1e-10f, DenormalGuard.Flush(1e-10f));
        Assert.Equal(1e-15f, DenormalGuard.Flush(1e-15f)); // threshold is exclusive

        // Flushed: tiny magnitudes that would decay into the subnormal range.
        Assert.Equal(0f, DenormalGuard.Flush(5e-16f));
        Assert.Equal(0f, DenormalGuard.Flush(-5e-16f));
        Assert.Equal(0f, DenormalGuard.Flush(float.Epsilon));
        Assert.Equal(0f, DenormalGuard.Flush(-float.Epsilon));
    }
}
