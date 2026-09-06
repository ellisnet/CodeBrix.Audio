using System;
using CodeBrix.Audio.Dsp;
using CodeBrix.Audio.ModestSynth.Internal;
using CodeBrix.Audio.ModestSynth.Oscillators;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// The spectrum measurements the oscillator tests are written against, built on CodeBrix.Audio's
/// own FFT.
/// </summary>
/// <remarks>
/// <para>
/// Every test that measures a spectrum picks its fundamental with <see cref="BinFrequency" /> so
/// that the fundamental and all of its harmonics land exactly on FFT bins. A block of a waveform
/// that repeats a whole number of times inside the block is exactly periodic, so a rectangular
/// window leaks nothing and every bin that is NOT a multiple of the fundamental's bin holds
/// aliasing and nothing else. That is what makes <see cref="AliasFloorDb" /> a real measurement
/// rather than a measurement of the window.
/// </para>
/// <para>
/// Aliases fold to bins that are not multiples of the fundamental's: the fundamental sits on bin
/// 171 of 8192 and 171 shares no factor with 8192, so harmonic k folding to |171k - 8192| can
/// never land back on a harmonic.
/// </para>
/// </remarks>
public static class Spectrum
{
    /// <summary>The sample rate every spectrum test renders at.</summary>
    public const int SampleRate = 48000;

    /// <summary>The power of two that gives the analysis block length.</summary>
    public const int BlockPower = 13;

    /// <summary>The analysis block length in samples.</summary>
    public const int BlockLength = 1 << BlockPower;

    private static readonly FourierPlan Plan = new FourierPlan(BlockLength);

    /// <summary>
    /// The frequency whose period fits a whole number of times into one analysis block.
    /// </summary>
    /// <param name="bin">Which bin the fundamental should land on.</param>
    /// <returns>The frequency in Hz.</returns>
    public static double BinFrequency(int bin) => bin * (double)SampleRate / BlockLength;

    /// <summary>
    /// Renders one analysis block from an oscillator, after resetting it.
    /// </summary>
    /// <param name="oscillator">The oscillator, already given its sample rate and pitch.</param>
    /// <returns>The rendered block.</returns>
    public static float[] RenderBlock(IModestOscillator oscillator)
    {
        float[] block = new float[BlockLength];
        oscillator.Reset(0.0);
        oscillator.Render(block);
        return block;
    }

    /// <summary>
    /// The magnitude spectrum of one block, with no window.
    /// </summary>
    /// <param name="samples">A block of exactly <see cref="BlockLength" /> samples.</param>
    /// <returns>The magnitude of each bin from DC to just below Nyquist.</returns>
    public static double[] Magnitudes(float[] samples)
    {
        Complex[] data = new Complex[BlockLength];
        for (int i = 0; i < BlockLength; i++)
        {
            data[i].X = samples[i];
        }

        FastFourierTransform.FFT(true, BlockPower, data);

        double[] magnitudes = new double[BlockLength / 2];
        for (int i = 0; i < magnitudes.Length; i++)
        {
            magnitudes[i] = Math.Sqrt(((double)data[i].X * data[i].X) + ((double)data[i].Y * data[i].Y));
        }

        return magnitudes;
    }

    /// <summary>
    /// The magnitude spectrum of one block, with no window, computed in DOUBLE precision.
    /// </summary>
    /// <param name="samples">A block of exactly <see cref="BlockLength" /> samples.</param>
    /// <returns>The magnitude of each bin from DC to just below Nyquist.</returns>
    /// <remarks>
    /// CodeBrix.Audio's own FFT is single precision, which puts a noise floor around -73 dB over an
    /// 8,192-point block and around -40 dB once the whole band up to Nyquist is searched for the
    /// worst bin. That is fine for the polyBLEP shapes, whose aliases sit well above it, and useless
    /// for the mip-mapped wavetable and the additive oscillator, whose whole point is that there is
    /// nothing there. This runs the same analysis through the package's own double-precision
    /// transform - the one that builds the mip maps - so a measurement of "nothing" reads as
    /// nothing.
    /// </remarks>
    public static double[] PreciseMagnitudes(float[] samples)
    {
        double[] real = new double[BlockLength];
        double[] imaginary = new double[BlockLength];

        for (int i = 0; i < BlockLength; i++) { real[i] = samples[i]; }

        Plan.Transform(real, imaginary, BlockLength, false);

        double[] magnitudes = new double[BlockLength / 2];
        for (int i = 0; i < magnitudes.Length; i++)
        {
            magnitudes[i] = Math.Sqrt((real[i] * real[i]) + (imaginary[i] * imaginary[i]));
        }

        return magnitudes;
    }

    /// <summary>
    /// The magnitude spectrum of one block through a Hann window, for signals that are NOT
    /// periodic in the block - a decaying pluck, for instance.
    /// </summary>
    /// <param name="samples">The samples to analyse.</param>
    /// <param name="offset">Where in <paramref name="samples" /> the block starts.</param>
    /// <returns>The magnitude of each bin from DC to just below Nyquist.</returns>
    public static double[] WindowedMagnitudes(float[] samples, int offset)
    {
        Complex[] data = new Complex[BlockLength];
        for (int i = 0; i < BlockLength; i++)
        {
            double window = 0.5 - (0.5 * Math.Cos(2.0 * Math.PI * i / BlockLength));
            data[i].X = (float)(samples[offset + i] * window);
        }

        FastFourierTransform.FFT(true, BlockPower, data);

        double[] magnitudes = new double[BlockLength / 2];
        for (int i = 0; i < magnitudes.Length; i++)
        {
            magnitudes[i] = Math.Sqrt(((double)data[i].X * data[i].X) + ((double)data[i].Y * data[i].Y));
        }

        return magnitudes;
    }

    /// <summary>
    /// The loudest thing in the spectrum that is not a harmonic of the fundamental, relative to the
    /// fundamental, in dB.
    /// </summary>
    /// <param name="magnitudes">A spectrum from <see cref="Magnitudes" />.</param>
    /// <param name="fundamentalBin">The bin the fundamental was placed on.</param>
    /// <param name="upperFrequencyHz">
    /// How far up to look. Aliases that fold to just below Nyquist are inaudible; the ones that
    /// matter fold down into the band a listener can hear.
    /// </param>
    /// <returns>The alias floor in dB, negative and further from zero being better.</returns>
    public static double AliasFloorDb(double[] magnitudes, int fundamentalBin, double upperFrequencyHz)
    {
        int limit = (int)(upperFrequencyHz * BlockLength / SampleRate);
        if (limit >= magnitudes.Length) { limit = magnitudes.Length - 1; }

        double worst = 0.0;
        for (int i = 1; i <= limit; i++)
        {
            if (i % fundamentalBin == 0) { continue; }
            if (magnitudes[i] > worst) { worst = magnitudes[i]; }
        }

        return 20.0 * Math.Log10(worst / magnitudes[fundamentalBin]);
    }

    /// <summary>
    /// The bin holding the most energy, ignoring DC.
    /// </summary>
    /// <param name="magnitudes">A spectrum.</param>
    /// <returns>The index of the loudest bin.</returns>
    public static int PeakBin(double[] magnitudes)
    {
        int peak = 1;
        for (int i = 2; i < magnitudes.Length; i++)
        {
            if (magnitudes[i] > magnitudes[peak]) { peak = i; }
        }

        return peak;
    }

    /// <summary>
    /// The power-weighted spectral centroid of a signal, in Hz - the measure the reference-player
    /// recordings were analysed with.
    /// </summary>
    /// <param name="samples">The samples to analyse.</param>
    /// <param name="count">How many of them to use; whole blocks only are analysed.</param>
    /// <returns>The centroid in Hz.</returns>
    public static double PowerCentroid(float[] samples, int count)
    {
        double weighted = 0.0;
        double total = 0.0;

        for (int offset = 0; offset + BlockLength <= count; offset += BlockLength)
        {
            double[] magnitudes = WindowedMagnitudes(samples, offset);
            for (int i = 1; i < magnitudes.Length; i++)
            {
                double power = magnitudes[i] * magnitudes[i];
                weighted += power * i;
                total += power;
            }
        }

        return weighted / total * SampleRate / BlockLength;
    }

    /// <summary>
    /// The root-mean-square level of a stretch of samples.
    /// </summary>
    /// <param name="samples">The samples.</param>
    /// <param name="offset">Where to start.</param>
    /// <param name="count">How many to include.</param>
    /// <returns>The RMS level.</returns>
    public static double Rms(float[] samples, int offset, int count)
    {
        double sum = 0.0;
        for (int i = 0; i < count; i++)
        {
            double value = samples[offset + i];
            sum += value * value;
        }

        return Math.Sqrt(sum / count);
    }
}
