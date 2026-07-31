using System;
using System.Globalization;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Wave;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

public class ReverbFreeverbTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(16)]
    [InlineData(23)]
    [InlineData(50)]
    [InlineData(100)]
    public void comb_filter_buffer_size_16_feedback_08_damp_01(int delay)
    {
        var path = Path.Combine(SynthTestAssets.ReferenceDataDirectory, "Freeverb", "cf_bs16_fb08_da01.csv");

        var data = File.ReadLines(path).Select(line => float.Parse(line, CultureInfo.InvariantCulture)).ToArray();
        data.Length.Should().Be(500);

        var expected = new float[delay].Concat(data).ToArray();

        var cf = new Reverb.CombFilter(16);
        cf.Feedback = 0.8F;
        cf.Damp = 0.1F;

        var input = new float[expected.Length];
        input[delay] = 1F;

        var actual = new float[expected.Length];
        cf.Process(input, actual);

        for (var t = 0; t < expected.Length; t++)
        {
            actual[t].ShouldBeApproximately(expected[t], 1.0E-3);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(16)]
    [InlineData(23)]
    [InlineData(50)]
    [InlineData(100)]
    public void comb_filter_buffer_size_23_feedback_07_damp_03(int delay)
    {
        var path = Path.Combine(SynthTestAssets.ReferenceDataDirectory, "Freeverb", "cf_bs23_fb07_da03.csv");

        var data = File.ReadLines(path).Select(line => float.Parse(line, CultureInfo.InvariantCulture)).ToArray();
        data.Length.Should().Be(500);

        var expected = new float[delay].Concat(data).ToArray();

        var cf = new Reverb.CombFilter(23);
        cf.Feedback = 0.7F;
        cf.Damp = 0.3F;

        var input = new float[expected.Length];
        input[delay] = 1F;

        var actual = new float[expected.Length];
        cf.Process(input, actual);

        for (var t = 0; t < expected.Length; t++)
        {
            actual[t].ShouldBeApproximately(expected[t], 1.0E-3);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(16)]
    [InlineData(23)]
    [InlineData(50)]
    [InlineData(100)]
    public void all_pass_filter_buffer_size_16_feedback_05(int delay)
    {
        var path = Path.Combine(SynthTestAssets.ReferenceDataDirectory, "Freeverb", "apf_bs16_fb05.csv");

        var data = File.ReadLines(path).Select(line => float.Parse(line, CultureInfo.InvariantCulture)).ToArray();
        data.Length.Should().Be(500);

        var expected = new float[delay].Concat(data).ToArray();

        var apf = new Reverb.AllPassFilter(16);
        apf.Feedback = 0.5F;

        var actual = new float[expected.Length];
        actual[delay] = 1F;
        apf.Process(actual);

        for (var t = 0; t < expected.Length; t++)
        {
            actual[t].ShouldBeApproximately(expected[t], 1.0E-3);
        }
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(3)]
    [InlineData(16)]
    [InlineData(23)]
    [InlineData(50)]
    [InlineData(100)]
    public void all_pass_filter_buffer_size_23_feedback_07(int delay)
    {
        var path = Path.Combine(SynthTestAssets.ReferenceDataDirectory, "Freeverb", "apf_bs23_fb07.csv");

        var data = File.ReadLines(path).Select(line => float.Parse(line, CultureInfo.InvariantCulture)).ToArray();
        data.Length.Should().Be(500);

        var expected = new float[delay].Concat(data).ToArray();

        var apf = new Reverb.AllPassFilter(23);
        apf.Feedback = 0.7F;

        var actual = new float[expected.Length];
        actual[delay] = 1F;
        apf.Process(actual);

        for (var t = 0; t < expected.Length; t++)
        {
            actual[t].ShouldBeApproximately(expected[t], 1.0E-3);
        }
    }

    [Fact]
    public void impulse_response()
    {
        var length = 44100;

        var expectedLeft = new float[length];
        var expectedRight = new float[length];

        using (var reader = new WaveFileReader(Path.Combine(SynthTestAssets.ReferenceDataDirectory, "Freeverb", "freeverb_default_ir.wav")))
        {
            for (var t = 0; t < length; t++)
            {
                var frame = reader.ReadNextSampleFrame();
                expectedLeft[t] = frame[0];
                expectedRight[t] = frame[1];
            }
        }

        var reverb = new Reverb(44100);

        var input = new float[length];
        input[0] = reverb.InputGain;

        var actualLeft = new float[length];
        var actualRight = new float[length];

        reverb.Process(input, actualLeft, actualRight);

        for (var t = 0; t < length; t++)
        {
            actualLeft[t].ShouldBeApproximately(expectedLeft[t], 1.0E-3);
            actualRight[t].ShouldBeApproximately(expectedRight[t], 1.0E-3);
        }
    }
}
