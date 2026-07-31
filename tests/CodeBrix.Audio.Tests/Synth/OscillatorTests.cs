using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

public class OscillatorTests
{
    [Theory]
    [MemberData(nameof(SynthTestAssets.SoundFontNames), MemberType = typeof(SynthTestAssets))]
    public void no_loop_pitch_ratio_100(string soundFontName)
    {
        var soundFont = SynthTestAssets.LoadSoundFont(soundFontName);

        foreach (var instrument in soundFont.Instruments)
        {
            foreach (var region in instrument.Regions.Take(3))
            {
                if (region.SampleModes == LoopMode.NoLoop)
                {
                    NoLoop_PitchRatio100_Run(soundFont, instrument, region);
                }
            }
        }
    }

    private void NoLoop_PitchRatio100_Run(SoundFont soundFont, SoundFontInstrument instrument, InstrumentRegion region)
    {
        Console.WriteLine(instrument.Name + ", " + region.Sample.Name);

        var synthesizer = new SoundFontSynthesizer(soundFont, 44100);
        var oscillator = new Oscillator(synthesizer);

        var block = new float[synthesizer.BlockSize];

        oscillator.Start(synthesizer.SoundFont.WaveDataArray, region);

        var actual = new List<float>();

        while (true)
        {
            if (oscillator.FillBlock(block, 1))
            {
                actual.AddRange(block);
            }
            else
            {
                break;
            }
        }

        var start = region.SampleStart;
        var end = region.SampleEnd;
        var length = end - start;

        var raw = soundFont.WaveDataArray.AsSpan(start, length);
        var expected = new float[length];
        for (var i = 0; i < length; i++)
        {
            var x = (float)raw[i] / 32768;
            expected[i] = x;
        }

        (actual.Count - length >= 0).Should().BeTrue();
        (actual.Count - length <= synthesizer.BlockSize).Should().BeTrue();

        for (var t = 0; t < expected.Length; t++)
        {
            actual[t].ShouldBeApproximately(expected[t], 1.0E-6);
        }

        for (var t = expected.Length; t < actual.Count; t++)
        {
            if (actual[t] != 0)
            {
                Assert.Fail("Trailing sample should be zero.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(SynthTestAssets.SoundFontNames), MemberType = typeof(SynthTestAssets))]
    public void no_loop_pitch_ratio_050(string soundFontName)
    {
        var soundFont = SynthTestAssets.LoadSoundFont(soundFontName);

        foreach (var instrument in soundFont.Instruments)
        {
            foreach (var region in instrument.Regions.Take(3))
            {
                if (region.SampleModes == LoopMode.NoLoop)
                {
                    NoLoop_PitchRatio050_Run(soundFont, instrument, region);
                }
            }
        }
    }

    private void NoLoop_PitchRatio050_Run(SoundFont soundFont, SoundFontInstrument instrument, InstrumentRegion region)
    {
        Console.WriteLine(instrument.Name + ", " + region.Sample.Name);

        var synthesizer = new SoundFontSynthesizer(soundFont, 44100);
        var oscillator = new Oscillator(synthesizer);

        var block = new float[synthesizer.BlockSize];

        oscillator.Start(synthesizer.SoundFont.WaveDataArray, region);

        var actual = new List<float>();

        while (true)
        {
            if (oscillator.FillBlock(block, 0.5))
            {
                actual.AddRange(block);
            }
            else
            {
                break;
            }
        }

        var start = region.SampleStart;
        var end = region.SampleEnd;
        var length = end - start;

        var raw = soundFont.WaveDataArray.AsSpan(start, length + 1);
        var expected = new float[2 * length];
        for (var i = 0; i < length; i++)
        {
            var x1 = (float)raw[i] / 32768;
            var x2 = (float)raw[i + 1] / 32768;
            expected[2 * i] = x1;
            expected[2 * i + 1] = (x1 + x2) / 2;
        }

        (actual.Count - 2 * length >= 0).Should().BeTrue();
        (actual.Count - 2 * length <= synthesizer.BlockSize).Should().BeTrue();

        for (var t = 0; t < expected.Length; t++)
        {
            actual[t].ShouldBeApproximately(expected[t], 1.0E-6);
        }

        for (var t = expected.Length; t < actual.Count; t++)
        {
            if (actual[t] != 0)
            {
                Assert.Fail("Trailing sample should be zero.");
            }
        }
    }

    [Theory]
    [MemberData(nameof(SynthTestAssets.SoundFontNames), MemberType = typeof(SynthTestAssets))]
    public void continuous_pitch_ratio_100(string soundFontName)
    {
        var soundFont = SynthTestAssets.LoadSoundFont(soundFontName);

        foreach (var instrument in soundFont.Instruments.Take(30))
        {
            foreach (var region in instrument.Regions.Take(3))
            {
                if (region.SampleModes == LoopMode.Continuous)
                {
                    Continuous_PitchRatio100_Run(soundFont, instrument, region);
                }
            }
        }
    }

    private void Continuous_PitchRatio100_Run(SoundFont soundFont, SoundFontInstrument instrument, InstrumentRegion region)
    {
        Console.WriteLine(instrument.Name + ", " + region.Sample.Name);

        var synthesizer = new SoundFontSynthesizer(soundFont, 44100);
        var oscillator = new Oscillator(synthesizer);

        var block = new float[synthesizer.BlockSize];

        oscillator.Start(synthesizer.SoundFont.WaveDataArray, region);

        var actual = new List<float>();

        for (var i = 0; i < 500; i++)
        {
            var result = oscillator.FillBlock(block, 1);

            result.Should().BeTrue();

            actual.AddRange(block);
        }

        var start = region.SampleStart;
        var startLoop = region.SampleStartLoop;
        var endLoop = region.SampleEndLoop;

        var expected = new float[actual.Count];

        var pos = start;
        for (var t = 0; t < expected.Length; t++)
        {
            expected[t] = (float)soundFont.WaveDataArray[pos] / 32768;
            pos++;
            if (pos == endLoop)
            {
                pos = startLoop;
            }
        }

        for (var t = 0; t < expected.Length; t++)
        {
            actual[t].ShouldBeApproximately(expected[t], 1.0E-6);
        }
    }

    [Theory]
    [MemberData(nameof(SynthTestAssets.SoundFontNames), MemberType = typeof(SynthTestAssets))]
    public void continuous_pitch_ratio_050(string soundFontName)
    {
        var soundFont = SynthTestAssets.LoadSoundFont(soundFontName);

        foreach (var instrument in soundFont.Instruments.Take(30))
        {
            foreach (var region in instrument.Regions.Take(3))
            {
                if (region.SampleModes == LoopMode.Continuous)
                {
                    Continuous_PitchRatio050_Run(soundFont, instrument, region);
                }
            }
        }
    }

    private void Continuous_PitchRatio050_Run(SoundFont soundFont, SoundFontInstrument instrument, InstrumentRegion region)
    {
        Console.WriteLine(instrument.Name + ", " + region.Sample.Name);

        var synthesizer = new SoundFontSynthesizer(soundFont, 44100);
        var oscillator = new Oscillator(synthesizer);

        var block = new float[synthesizer.BlockSize];

        oscillator.Start(synthesizer.SoundFont.WaveDataArray, region);

        var actual = new List<float>();

        for (var i = 0; i < 500; i++)
        {
            var result = oscillator.FillBlock(block, 0.5);

            result.Should().BeTrue();

            actual.AddRange(block);
        }

        var start = region.SampleStart;
        var startLoop = region.SampleStartLoop;
        var endLoop = region.SampleEndLoop;

        var raw = new float[actual.Count / 2 + 1];

        var pos = start;
        for (var t = 0; t < raw.Length; t++)
        {
            raw[t] = (float)soundFont.WaveDataArray[pos] / 32768;
            pos++;
            if (pos == endLoop)
            {
                pos = startLoop;
            }
        }

        var expected = new float[actual.Count];
        for (var t = 0; t < expected.Length; t += 2)
        {
            expected[t] = raw[t / 2];
            expected[t + 1] = (raw[t / 2] + raw[t / 2 + 1]) / 2;
        }

        for (var t = 0; t < expected.Length; t++)
        {
            actual[t].ShouldBeApproximately(expected[t], 1.0E-6);
        }
    }
}
