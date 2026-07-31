using System;
using System.Linq;
using System.Numerics;
using MathNet.Numerics;
using MathNet.Numerics.IntegralTransforms;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

public class BiQuadFilterTests
{
    [Theory]
    [InlineData(44100, 1000)]
    [InlineData(44100, 500)]
    [InlineData(44100, 5000)]
    [InlineData(22050, 3000)]
    [InlineData(44100, 22050)]
    [InlineData(44100, 50000)]
    [InlineData(48000, 10000)]
    [InlineData(48000, 24000)]
    public void low_pass_filter_test(int sampleRate, int cutoffFrequency)
    {
        var synthesizer = new SoundFontSynthesizer(SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName), sampleRate);

        var lpf = new BiQuadFilter(synthesizer);
        lpf.SetLowPassFilter(cutoffFrequency, 1);

        var block = new float[4096];
        block[0] = 1;

        lpf.Process(block);

        var fft = block.Select(x => (Complex)x).ToArray();
        Fourier.Forward(fft, FourierOptions.AsymmetricScaling);

        var spectrum = fft.Select(x => x.Magnitude).ToArray();

        for (var i = 0; i < spectrum.Length / 2; i++)
        {
            var frequency = (double)i / spectrum.Length * sampleRate;

            if (frequency < cutoffFrequency - 1)
            {
                (spectrum[i] > 1 / Math.Sqrt(2)).Should().BeTrue();
            }

            if (frequency > cutoffFrequency + 1)
            {
                (spectrum[i] < 1 / Math.Sqrt(2)).Should().BeTrue();
            }

            if (frequency < cutoffFrequency / 10)
            {
                spectrum[i].ShouldBeApproximately(1.0, 0.1);
            }
        }
    }

    [Theory]
    [InlineData(44100, 1000, 2.0F)]
    [InlineData(44100, 500, 3.14F)]
    [InlineData(44100, 5000, 5.7F)]
    [InlineData(22050, 3000, 12.3F)]
    [InlineData(48000, 500, 2.7F)]
    public void resonance_test(int sampleRate, int cutoffFrequency, float resonance)
    {
        var synthesizer = new SoundFontSynthesizer(SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName), sampleRate);

        var lpf = new BiQuadFilter(synthesizer);
        lpf.SetLowPassFilter(cutoffFrequency, resonance);

        var block = new float[4096];
        block[0] = 1;

        lpf.Process(block);

        var fft = block.Select(x => (Complex)x).ToArray();
        Fourier.Forward(fft, FourierOptions.AsymmetricScaling);

        var spectrum = fft.Select(x => x.Magnitude).ToArray();

        for (var i = 0; i < spectrum.Length / 2; i++)
        {
            var frequency = (double)i / spectrum.Length * sampleRate;

            if (frequency < cutoffFrequency / 10)
            {
                spectrum[i].ShouldBeApproximately(1.0, 0.1);
            }
        }

        resonance.ShouldBeApproximately(spectrum.Max(), 0.03);
    }
}
