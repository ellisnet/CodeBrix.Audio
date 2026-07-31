using System;
using System.Linq;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

public class ArrayMathTests
{
    [Theory]
    [InlineData(64)]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(10)]
    [InlineData(100)]
    [InlineData(123)]
    [InlineData(127)]
    [InlineData(128)]
    [InlineData(129)]
    [InlineData(130)]
    [InlineData(41)]
    [InlineData(57)]
    [InlineData(278)]
    [InlineData(314)]
    public void multiply_add_test(int length)
    {
        var random = new Random(31415);

        var x1 = Enumerable.Range(0, length).Select(i => (float)(2 * (random.NextDouble() - 0.5))).ToArray();
        var x2 = Enumerable.Range(0, length).Select(i => (float)(2 * (random.NextDouble() - 0.5))).ToArray();
        var a = (float)(1 + random.NextDouble());

        var expected = new float[length];
        for (var i = 0; i < length; i++)
        {
            expected[i] = x1[i] + a * x2[i];
        }

        var actual = x1.ToArray();
        ArrayMath.MultiplyAdd(a, x2, actual);

        for (var i = 0; i < length; i++)
        {
            expected[i].ShouldBeApproximately(actual[i], 1.0E-3F);
        }
    }
}
