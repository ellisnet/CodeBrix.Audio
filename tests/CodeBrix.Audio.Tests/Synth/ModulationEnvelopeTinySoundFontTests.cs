using System;
using System.Globalization;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

public class ModulationEnvelopeTinySoundFontTests
{
    [Fact]
    public void d_03_a_05_h_07_d_11_s_02_r_13_continuous()
    {
        var dir = Path.Combine(SynthTestAssets.ReferenceDataDirectory, "TinySoundFont", "ModulationEnvelope", "D03_A05_H07_D11_S02_R13");
        var expected = File.ReadLines(Path.Combine(dir, "Continuous.csv")).Select(line => float.Parse(line.Split(',')[1], CultureInfo.InvariantCulture)).ToArray();
        expected.Length.Should().Be(3000);

        var synthesizer = new SoundFontSynthesizer(SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName), 44100);
        var envelope = new ModulationEnvelope(synthesizer);

        envelope.Start(0.3F, 0.5F, 0.7F, 1.1F, 0.2F, 1.3F);

        for (var i = 0; i < expected.Length; i++)
        {
            envelope.Process();

            envelope.Value.ShouldBeApproximately(expected[i], 2.0E-2);
        }
    }

    [Fact]
    public void d_03_a_05_h_07_d_11_s_02_r_13_note_off()
    {
        var dir = Path.Combine(SynthTestAssets.ReferenceDataDirectory, "TinySoundFont", "ModulationEnvelope", "D03_A05_H07_D11_S02_R13");

        var synthesizer = new SoundFontSynthesizer(SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName), 44100);
        var envelope = new ModulationEnvelope(synthesizer);

        for (var noteOffBlock = 0; noteOffBlock <= 2000; noteOffBlock += 50)
        {
            var name = "NoteOff" + noteOffBlock.ToString("0000") + ".csv";
            var expected = File.ReadLines(Path.Combine(dir, name)).Select(line => float.Parse(line.Split(',')[1], CultureInfo.InvariantCulture)).ToArray();
            expected.Length.Should().Be(3000);

            envelope.Start(0.3F, 0.5F, 0.7F, 1.1F, 0.2F, 1.3F);

            for (var i = 0; i < expected.Length; i++)
            {
                if (i == noteOffBlock)
                {
                    envelope.Release();
                }

                envelope.Process();

                envelope.Value.ShouldBeApproximately(expected[i], 2.0E-2);
            }
        }
    }

    [Fact]
    public void d_00_a_00_h_00_d_23_s_01_r_57_continuous()
    {
        var dir = Path.Combine(SynthTestAssets.ReferenceDataDirectory, "TinySoundFont", "ModulationEnvelope", "D00_A00_H00_D23_S01_R57");
        var expected = File.ReadLines(Path.Combine(dir, "Continuous.csv")).Select(line => float.Parse(line.Split(',')[1], CultureInfo.InvariantCulture)).ToArray();
        expected.Length.Should().Be(3000);

        var synthesizer = new SoundFontSynthesizer(SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName), 44100);
        var envelope = new ModulationEnvelope(synthesizer);

        envelope.Start(0.0F, 0.0F, 0.0F, 2.3F, 0.1F, 5.7F);

        for (var i = 0; i < expected.Length; i++)
        {
            envelope.Process();

            envelope.Value.ShouldBeApproximately(expected[i], 2.0E-2);
        }
    }

    [Fact]
    public void d_00_a_00_h_00_d_23_s_01_r_57_note_off()
    {
        var dir = Path.Combine(SynthTestAssets.ReferenceDataDirectory, "TinySoundFont", "ModulationEnvelope", "D00_A00_H00_D23_S01_R57");

        var synthesizer = new SoundFontSynthesizer(SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName), 44100);
        var envelope = new ModulationEnvelope(synthesizer);

        for (var noteOffBlock = 0; noteOffBlock <= 2000; noteOffBlock += 50)
        {
            var name = "NoteOff" + noteOffBlock.ToString("0000") + ".csv";
            var expected = File.ReadLines(Path.Combine(dir, name)).Select(line => float.Parse(line.Split(',')[1], CultureInfo.InvariantCulture)).ToArray();
            expected.Length.Should().Be(3000);

            envelope.Start(0.0F, 0.0F, 0.0F, 2.3F, 0.1F, 5.7F);

            for (var i = 0; i < expected.Length; i++)
            {
                if (i == noteOffBlock)
                {
                    envelope.Release();
                }

                envelope.Process();

                envelope.Value.ShouldBeApproximately(expected[i], 2.0E-2);
            }
        }
    }
}
