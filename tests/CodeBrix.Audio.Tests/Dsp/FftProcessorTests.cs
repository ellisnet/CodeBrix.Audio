using System;
using CodeBrix.Audio.Dsp;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.Dsp;

/// <summary>
/// Verifies that <see cref="FftProcessor"/> produces correct and consistent output against
/// both known analytical signals and the existing static <see cref="FastFourierTransform"/>
/// implementation.
/// </summary>
public class FftProcessorTests
{
    private const double Tolerance = 1e-5;

    [Theory]
    [InlineData(4)]
    [InlineData(8)]
    [InlineData(16)]
    [InlineData(64)]
    [InlineData(1024)]
    public void RealForwardMatchesFullComplexFftOnRealInput(int n)
    {
        // Pack real samples (Y=0) into a full Complex[] and run the static full-size FFT;
        // compare the first N/2+1 bins to RealForward's output — they should match exactly
        // (both use the same 1/N forward scaling convention).
        var rng = new Random(1337);
        var samples = new float[n];
        for (int i = 0; i < n; i++) samples[i] = (float)(rng.NextDouble() * 2 - 1);

        var fullBuffer = new Complex[n];
        for (int i = 0; i < n; i++) { fullBuffer[i].X = samples[i]; fullBuffer[i].Y = 0f; }
        int m = Log2(n);
        FastFourierTransform.FFT(true, m, fullBuffer);

        var processor = new FftProcessor(n);
        var half = new Complex[n / 2 + 1];
        processor.RealForward(samples, half);

        // Accumulated float32 rounding differs slightly between the full-complex FFT path
        // (N-point with twiddle recurrence) and the real-FFT path (N/2-point + unpack). Allow
        // a proportional tolerance that scales with FFT size.
        double tol = Tolerance * Math.Max(1, n / 64);
        for (int k = 0; k <= n / 2; k++)
        {
            Assert.Equal(fullBuffer[k].X, half[k].X, tol);
            Assert.Equal(fullBuffer[k].Y, half[k].Y, tol);
        }
    }

    [Fact]
    public void RealForwardOnConstantSignalProducesDcOnly()
    {
        const int n = 16;
        var samples = new float[n];
        for (int i = 0; i < n; i++) samples[i] = 1.0f;

        var processor = new FftProcessor(n);
        var spectrum = new Complex[n / 2 + 1];
        processor.RealForward(samples, spectrum);

        Assert.Equal(1.0f, spectrum[0].Real, Tolerance);
        for (int k = 1; k <= n / 2; k++)
        {
            Assert.Equal(0.0f, spectrum[k].Real, Tolerance);
            Assert.Equal(0.0f, spectrum[k].Imaginary, Tolerance);
        }
    }

    [Fact]
    public void RealForwardOnImpulseProducesFlatSpectrum()
    {
        const int n = 16;
        var samples = new float[n];
        samples[0] = 1.0f;

        var processor = new FftProcessor(n);
        var spectrum = new Complex[n / 2 + 1];
        processor.RealForward(samples, spectrum);

        float expected = 1.0f / n;
        for (int k = 0; k <= n / 2; k++)
        {
            Assert.Equal(expected, spectrum[k].Real, Tolerance);
            Assert.Equal(0.0f, spectrum[k].Imaginary, Tolerance);
        }
    }

    [Theory]
    [InlineData(3, 16)]
    [InlineData(5, 32)]
    [InlineData(7, 64)]
    public void RealForwardOnCosineHasEnergyAtBin(int bin, int n)
    {
        var samples = new float[n];
        for (int i = 0; i < n; i++)
            samples[i] = (float)Math.Cos(2 * Math.PI * bin * i / n);

        var processor = new FftProcessor(n);
        var spectrum = new Complex[n / 2 + 1];
        processor.RealForward(samples, spectrum);

        // Real cosine has energy split evenly between +bin and -bin; the real-half-spectrum
        // sums both halves (conjugate symmetry) into magnitude 0.5 at the target bin.
        for (int k = 0; k <= n / 2; k++)
        {
            float magnitude = (float)Math.Sqrt(spectrum[k].Real * spectrum[k].Real + spectrum[k].Imaginary * spectrum[k].Imaginary);
            if (k == bin)
                Assert.Equal(0.5f, magnitude, Tolerance);
            else
                Assert.Equal(0.0f, magnitude, Tolerance);
        }
    }

    [Theory]
    [InlineData(8)]
    [InlineData(64)]
    [InlineData(1024)]
    public void RealForwardInverseRoundTrips(int n)
    {
        var rng = new Random(7);
        var input = new float[n];
        for (int i = 0; i < n; i++) input[i] = (float)(rng.NextDouble() * 2 - 1);

        var processor = new FftProcessor(n);
        var spectrum = new Complex[n / 2 + 1];
        processor.RealForward(input, spectrum);

        var recovered = new float[n];
        processor.RealInverse(spectrum, recovered);

        for (int i = 0; i < n; i++)
            Assert.Equal(input[i], recovered[i], 1e-4f);
    }

    [Fact]
    public void ComplexForwardMatchesStaticFftAtSameSize()
    {
        const int n = 64;
        int m = Log2(n);
        var rng = new Random(42);
        var a = new Complex[n];
        var b = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            a[i].X = (float)rng.NextDouble();
            a[i].Y = (float)rng.NextDouble();
            b[i] = a[i];
        }

        FastFourierTransform.FFT(true, m, a);
        new FftProcessor(n).ComplexForward(b);

        for (int i = 0; i < n; i++)
        {
            Assert.Equal(a[i].X, b[i].X, Tolerance);
            Assert.Equal(a[i].Y, b[i].Y, Tolerance);
        }
    }

    [Fact]
    public void HammingWindowTableMatchesStaticFunction()
    {
        // When a window is configured, RealForward should apply it identically to how a caller
        // would using the static window function on each sample. Verified indirectly: compare
        // the windowed output to a manually-windowed unwindowed call.
        const int n = 64;
        var rng = new Random(99);
        var samples = new float[n];
        for (int i = 0; i < n; i++) samples[i] = (float)(rng.NextDouble() * 2 - 1);

        var windowedSamples = new float[n];
        for (int i = 0; i < n; i++)
            windowedSamples[i] = samples[i] * (float)FastFourierTransform.HammingWindow(i, n);

        var noWindow = new FftProcessor(n);
        var withWindow = new FftProcessor(n, FftWindowType.Hamming);

        var expectedSpectrum = new Complex[n / 2 + 1];
        noWindow.RealForward(windowedSamples, expectedSpectrum);

        var actualSpectrum = new Complex[n / 2 + 1];
        withWindow.RealForward(samples, actualSpectrum);

        for (int k = 0; k <= n / 2; k++)
        {
            Assert.Equal(expectedSpectrum[k].X, actualSpectrum[k].X, Tolerance);
            Assert.Equal(expectedSpectrum[k].Y, actualSpectrum[k].Y, Tolerance);
        }
    }

    [Fact]
    public void NonPowerOfTwoSizeThrows()
    {
        Assert.Throws<ArgumentException>(() => new FftProcessor(7));
        Assert.Throws<ArgumentException>(() => new FftProcessor(1000));
    }

    [Fact]
    public void SizeLessThanTwoThrows()
    {
        Assert.Throws<ArgumentException>(() => new FftProcessor(1));
        Assert.Throws<ArgumentException>(() => new FftProcessor(0));
    }

    [Fact]
    public void WrongSizedSpansThrow()
    {
        var processor = new FftProcessor(16);
        var shortSamples = new float[8];
        var rightSamples = new float[16];
        var shortSpectrum = new Complex[5];
        var rightSpectrum = new Complex[9];

        Assert.Throws<ArgumentException>(() => processor.RealForward(shortSamples, rightSpectrum));
        Assert.Throws<ArgumentException>(() => processor.RealForward(rightSamples, shortSpectrum));
        Assert.Throws<ArgumentException>(() => processor.RealInverse(shortSpectrum, rightSamples));
        Assert.Throws<ArgumentException>(() => processor.RealInverse(rightSpectrum, shortSamples));
    }

    [Fact]
    public void RealForwardDoesNotAllocateInSteadyState()
    {
        const int n = 1024;
        var processor = new FftProcessor(n, FftWindowType.Hamming);
        var samples = new float[n];
        var spectrum = new Complex[n / 2 + 1];
        var rng = new Random(0);
        for (int i = 0; i < n; i++) samples[i] = (float)rng.NextDouble();

        // Warm up hard so the hot path is fully JIT-tiered before measuring; a single
        // call leaves it at Tier-0 and the re-JIT lands inside the measured window,
        // which is attributed to this thread's allocation counter (flaky across runs
        // and OSes — passes on Linux, intermittently fails on Windows).
        for (int i = 0; i < 10_000; i++) processor.RealForward(samples, spectrum);

        // Steady state allocates nothing. Allow a few attempts so a stray background
        // tiering/GC event in one window doesn't fail an otherwise allocation-free path.
        // A genuine per-call allocation would show up in every window, not just one.
        long allocated = -1;
        for (int attempt = 0; attempt < 5 && allocated != 0; attempt++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 100; i++) processor.RealForward(samples, spectrum);
            long after = GC.GetAllocatedBytesForCurrentThread();
            allocated = after - before;
        }

        Assert.Equal(0, allocated);
    }

    private static int Log2(int value)
    {
        int log = 0;
        while (value > 1) { value >>= 1; log++; }
        return log;
    }
}
