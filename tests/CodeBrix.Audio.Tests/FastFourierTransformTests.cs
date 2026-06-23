using System;
using CodeBrix.Audio.Dsp;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests for <see cref="FastFourierTransform"/>.
/// </summary>
public class FastFourierTransformTests
{
    [Fact]
    public void Fft_of_a_pure_tone_peaks_at_the_expected_bin()
    {
        //Arrange
        const int m = 10;            // 2^10 = 1024 point FFT
        const int size = 1 << m;
        const int bin = 8;           // tone completes 8 cycles across the window
        var data = new Complex[size];
        for (int n = 0; n < size; n++)
        {
            data[n].X = (float)Math.Sin(2.0 * Math.PI * bin * n / size);
            data[n].Y = 0f;
        }

        //Act
        FastFourierTransform.FFT(true, m, data);

        //Assert
        int peakBin = 0;
        double peakMag = 0;
        for (int i = 1; i < size / 2; i++)
        {
            double mag = data[i].X * data[i].X + data[i].Y * data[i].Y;
            if (mag > peakMag) { peakMag = mag; peakBin = i; }
        }
        peakBin.Should().Be(bin);
    }

    [Fact]
    public void Forward_then_inverse_fft_restores_the_original_signal()
    {
        //Arrange
        const int m = 8;
        const int size = 1 << m;
        var original = new Complex[size];
        var data = new Complex[size];
        for (int n = 0; n < size; n++)
        {
            float v = (float)Math.Sin(2.0 * Math.PI * 5 * n / size);
            original[n].X = v;
            data[n].X = v;
        }

        //Act
        FastFourierTransform.FFT(true, m, data);
        FastFourierTransform.FFT(false, m, data);

        //Assert
        for (int n = 0; n < size; n++)
        {
            Math.Abs(data[n].X - original[n].X).Should().BeLessThan(0.001f);
        }
    }
}
