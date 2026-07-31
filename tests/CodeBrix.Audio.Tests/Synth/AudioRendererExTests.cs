using System;
using System.Linq;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

public class AudioRendererExTests
{
    [Theory]
    [InlineData(64)]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(41)]
    [InlineData(57)]
    [InlineData(278)]
    [InlineData(314)]
    public void render_interleaved_test(int length)
    {
        var random = new Random(31415);

        var srcLeft = Enumerable.Range(0, length).Select(i => (float)(2 * (random.NextDouble() - 0.5))).ToArray();
        var srcRight = Enumerable.Range(0, length).Select(i => (float)(2 * (random.NextDouble() - 0.5))).ToArray();
        var renderer = new DummyRenderer(srcLeft, srcRight);

        var expected = srcLeft.Zip(srcRight, (x, y) => new[] { x, y }).SelectMany(x => x).ToArray();

        var actual = new float[2 * length];
        renderer.RenderInterleaved(actual);

        for (var i = 0; i < length; i++)
        {
            expected[i].ShouldBeApproximately(actual[i], 1.0E-6F);
        }
    }

    [Theory]
    [InlineData(64)]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(41)]
    [InlineData(57)]
    [InlineData(278)]
    [InlineData(314)]
    public void render_mono_test(int length)
    {
        var random = new Random(31415);

        var srcLeft = Enumerable.Range(0, length).Select(i => (float)(2 * (random.NextDouble() - 0.5))).ToArray();
        var srcRight = Enumerable.Range(0, length).Select(i => (float)(2 * (random.NextDouble() - 0.5))).ToArray();
        var renderer = new DummyRenderer(srcLeft, srcRight);

        var expected = srcLeft.Zip(srcRight, (x, y) => (x + y) / 2).ToArray();

        var actual = new float[length];
        renderer.RenderMono(actual);

        for (var i = 0; i < length; i++)
        {
            expected[i].ShouldBeApproximately(actual[i], 1.0E-6F);
        }
    }

    [Theory]
    [InlineData(64)]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(41)]
    [InlineData(57)]
    [InlineData(278)]
    [InlineData(314)]
    public void render_int_16_test(int length)
    {
        var random = new Random(31415);

        var srcLeft = Enumerable.Range(0, length).Select(i => (float)(4 * (random.NextDouble() - 0.5))).ToArray();
        var srcRight = Enumerable.Range(0, length).Select(i => (float)(4 * (random.NextDouble() - 0.5))).ToArray();
        var renderer = new DummyRenderer(srcLeft, srcRight);

        var expectedLeft = srcLeft.Select(x => ToShort(x)).ToArray();
        var expectedRight = srcRight.Select(x => ToShort(x)).ToArray();

        var actualLeft = new short[length];
        var actualRight = new short[length];
        renderer.RenderInt16(actualLeft, actualRight);

        for (var i = 0; i < length; i++)
        {
            expectedLeft[i].Should().Be(actualLeft[i]);
            expectedRight[i].Should().Be(actualRight[i]);
        }
    }

    [Theory]
    [InlineData(64)]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(41)]
    [InlineData(57)]
    [InlineData(278)]
    [InlineData(314)]
    public void render_interleaved_int_16_test(int length)
    {
        var random = new Random(31415);

        var srcLeft = Enumerable.Range(0, length).Select(i => (float)(4 * (random.NextDouble() - 0.5))).ToArray();
        var srcRight = Enumerable.Range(0, length).Select(i => (float)(4 * (random.NextDouble() - 0.5))).ToArray();
        var renderer = new DummyRenderer(srcLeft, srcRight);

        var expected = srcLeft.Zip(srcRight, (x, y) => new[] { ToShort(x), ToShort(y) }).SelectMany(x => x).ToArray();

        var actual = new short[2 * length];
        renderer.RenderInterleavedInt16(actual);

        for (var i = 0; i < length; i++)
        {
            expected[i].Should().Be(actual[i]);
        }
    }

    [Theory]
    [InlineData(64)]
    [InlineData(63)]
    [InlineData(65)]
    [InlineData(41)]
    [InlineData(57)]
    [InlineData(278)]
    [InlineData(314)]
    public void render_mono_int_16_test(int length)
    {
        var random = new Random(31415);

        var srcLeft = Enumerable.Range(0, length).Select(i => (float)(4 * (random.NextDouble() - 0.5))).ToArray();
        var srcRight = Enumerable.Range(0, length).Select(i => (float)(4 * (random.NextDouble() - 0.5))).ToArray();
        var renderer = new DummyRenderer(srcLeft, srcRight);

        var expected = srcLeft.Zip(srcRight, (x, y) => ToShort((x + y) / 2)).ToArray();

        var actual = new short[length];
        renderer.RenderMonoInt16(actual);

        for (var i = 0; i < length; i++)
        {
            expected[i].Should().Be(actual[i]);
        }
    }

    private static short ToShort(float value)
    {
        return (short)Math.Clamp((int)(32768 * value), short.MinValue, short.MaxValue);
    }



    private class DummyRenderer : IAudioRenderer
    {
        private float[] srcLeft;
        private float[] srcRight;

        public DummyRenderer(float[] srcLeft, float[] srcRight)
        {
            this.srcLeft = srcLeft;
            this.srcRight = srcRight;
        }

        public void Render(Span<float> left, Span<float> right)
        {
            srcLeft.AsSpan().CopyTo(left);
            srcRight.AsSpan().CopyTo(right);
        }
    }
}
