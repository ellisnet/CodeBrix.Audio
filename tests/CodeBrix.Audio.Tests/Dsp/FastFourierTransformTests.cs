using System;
using CodeBrix.Audio.Dsp;
using Xunit;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
namespace CodeBrix.Audio.Tests.Dsp;
public class FastFourierTransformTests
{
    private const double Tolerance = 1e-5;

    [Fact]
    public void ForwardFft_ConstantSignal_HasOnlyDcComponent()
    {
        const int m = 3;
        const int n = 1 << m;
        var data = new Complex[n];
        for (int i = 0; i < n; i++)
        {
            data[i].X = 1.0f;
        }

        FastFourierTransform.FFT(true, m, data);

        Assert.Multiple(() =>
        {
            Assert.Equal(1.0f, data[0].X, Tolerance);
            Assert.Equal(0.0f, data[0].Y, Tolerance);

            for (int i = 1; i < n; i++)
            {
                Assert.Equal(0.0f, data[i].X, Tolerance);
                Assert.Equal(0.0f, data[i].Y, Tolerance);
            }
        });
    }

    [Fact]
    public void ForwardFft_ImpulseSignal_HasFlatSpectrum()
    {
        const int m = 3;
        const int n = 1 << m;
        var data = new Complex[n];
        data[0].X = 1.0f;

        FastFourierTransform.FFT(true, m, data);

        var expected = 1.0 / n;
        Assert.Multiple(() =>
        {
            for (int i = 0; i < n; i++)
            {
                Assert.Equal(expected, data[i].X, Tolerance);
                Assert.Equal(0.0f, data[i].Y, Tolerance);
            }
        });
    }

    [Fact]
    public void ForwardFft_CosineSignal_HasEnergyAtPositiveAndNegativeBin()
    {
        const int m = 4;
        const int n = 1 << m;
        const int frequencyBin = 3;
        var data = new Complex[n];

        for (int i = 0; i < n; i++)
        {
            data[i].X = (float)Math.Cos((2.0 * Math.PI * frequencyBin * i) / n);
        }

        FastFourierTransform.FFT(true, m, data);

        Assert.Multiple(() =>
        {
            Assert.Equal(0.5f, data[frequencyBin].X, 1e-4);
            Assert.Equal(0.0f, data[frequencyBin].Y, 1e-4);

            int mirroredBin = n - frequencyBin;
            Assert.Equal(0.5f, data[mirroredBin].X, 1e-4);
            Assert.Equal(0.0f, data[mirroredBin].Y, 1e-4);

            for (int i = 0; i < n; i++)
            {
                if (i == frequencyBin || i == mirroredBin)
                {
                    continue;
                }

                Assert.Equal(0.0f, data[i].X, 1e-4);
                Assert.Equal(0.0f, data[i].Y, 1e-4);
            }
        });
    }

    [Fact]
    public void ForwardThenInverseFft_RoundTripsComplexData()
    {
        const int m = 4;
        const int n = 1 << m;
        var random = new Random(12345);
        var data = new Complex[n];
        var original = new Complex[n];

        for (int i = 0; i < n; i++)
        {
            data[i].X = (float)(random.NextDouble() * 2.0 - 1.0);
            data[i].Y = (float)(random.NextDouble() * 2.0 - 1.0);
            original[i] = data[i];
        }

        FastFourierTransform.FFT(true, m, data);
        FastFourierTransform.FFT(false, m, data);

        Assert.Multiple(() =>
        {
            for (int i = 0; i < n; i++)
            {
                Assert.Equal(original[i].X, data[i].X, 1e-4);
                Assert.Equal(original[i].Y, data[i].Y, 1e-4);
            }
        });
    }

    [Fact]
    public void Fft_SizeOne_DoesNotChangeValue()
    {
        var data = new[] { new Complex { X = 0.125f, Y = -0.75f } };

        FastFourierTransform.FFT(true, 0, data);
        FastFourierTransform.FFT(false, 0, data);

        Assert.Multiple(() =>
        {
            Assert.Equal(0.125f, data[0].X, Tolerance);
            Assert.Equal(-0.75f, data[0].Y, Tolerance);
        });
    }

    [Fact]
    public void HannWindow_HasExpectedEndpointsAndSymmetry()
    {
        const int frameSize = 1024;

        Assert.Multiple(() =>
        {
            Assert.Equal(0.0, FastFourierTransform.HannWindow(0, frameSize), 1e-12);
            Assert.Equal(0.0, FastFourierTransform.HannWindow(frameSize - 1, frameSize), 1e-12);

            for (int i = 0; i < 50; i++)
            {
                Assert.Equal(FastFourierTransform.HannWindow(frameSize - 1 - i, frameSize), FastFourierTransform.HannWindow(i, frameSize), 1e-12);
            }
        });
    }

    [Fact]
    public void HammingWindow_HasExpectedEndpointsAndSymmetry()
    {
        const int frameSize = 1024;

        Assert.Multiple(() =>
        {
            Assert.Equal(0.08, FastFourierTransform.HammingWindow(0, frameSize), 1e-12);
            Assert.Equal(0.08, FastFourierTransform.HammingWindow(frameSize - 1, frameSize), 1e-12);

            for (int i = 0; i < 50; i++)
            {
                Assert.Equal(FastFourierTransform.HammingWindow(frameSize - 1 - i, frameSize), FastFourierTransform.HammingWindow(i, frameSize), 1e-12);
            }
        });
    }

    [Fact]
    public void BlackmanHarrisWindow_HasExpectedEndpointsAndSymmetry()
    {
        const int frameSize = 1024;

        Assert.Multiple(() =>
        {
            Assert.Equal(0.00006, FastFourierTransform.BlackmanHarrisWindow(0, frameSize), 1e-8);
            Assert.Equal(0.00006, FastFourierTransform.BlackmanHarrisWindow(frameSize - 1, frameSize), 1e-8);

            for (int i = 0; i < 50; i++)
            {
                Assert.Equal(FastFourierTransform.BlackmanHarrisWindow(frameSize - 1 - i, frameSize), FastFourierTransform.BlackmanHarrisWindow(i, frameSize), 1e-12);
            }
        });
    }

    [Fact]
    public void WindowFunctions_AreOneAtCenterForOddSizedFrame()
    {
        const int frameSize = 5;
        const int center = 2;

        Assert.Multiple(() =>
        {
            Assert.Equal(1.0, FastFourierTransform.HannWindow(center, frameSize), 1e-12);
            Assert.Equal(1.0, FastFourierTransform.HammingWindow(center, frameSize), 1e-12);
            Assert.Equal(1.0, FastFourierTransform.BlackmanHarrisWindow(center, frameSize), 1e-12);
        });
    }
}
