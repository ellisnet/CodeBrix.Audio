using System;
using System.Collections.Generic;
using CodeBrix.Audio.ModestSynth.Oscillators;
using CodeBrix.Audio.ModestSynth.Patch;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Integration;

/// <summary>The worked example patches, and the promise that every one of them plays.</summary>
public class ModestSynthPresetsTests
{
    private const int SampleRate = Spectrum.SampleRate;

    public static IEnumerable<object[]> EveryPreset()
    {
        foreach (string name in ModestSynthPresets.Names)
        {
            yield return [name];
        }
    }

    [Fact]
    public void the_six_documented_presets_are_offered() =>
        ModestSynthPresets.Names.Should().Equal(
            "sub sine", "saw lead", "plucked string", "electric piano", "wavetable pad",
            "additive organ");

    [Theory]
    [MemberData(nameof(EveryPreset))]
    public void every_preset_plays_a_chord_and_then_lets_go(string name)
    {
        //Arrange
        ModestSynthesizer synthesizer = Build(name);

        //Act
        synthesizer.NoteOn(0, 57, 100);
        synthesizer.NoteOn(0, 64, 100);
        var (held, right) = RenderProbe.RenderSeconds(synthesizer, 1.0);

        synthesizer.NoteOff(0, 57);
        synthesizer.NoteOff(0, 64);
        var (tail, _) = RenderProbe.RenderSeconds(synthesizer, 3.0);

        //Assert
        RenderProbe.Rms(held, Spectrum.BlockLength, Spectrum.BlockLength).Should().BeGreaterThan(0.01);
        RenderProbe.Peak(held).Should().BeLessThan(1.0);
        held.Should().Equal(right);
        RenderProbe.Rms(tail, tail.Length - 4096, 4096).Should().BeApproximately(0.0, 1e-6);
        synthesizer.ActiveVoiceCount.Should().Be(0);
    }

    [Theory]
    [MemberData(nameof(EveryPreset))]
    public void every_preset_renders_the_same_bytes_from_the_same_events(string name)
    {
        //Arrange
        ModestSynthesizer first = Build(name);
        ModestSynthesizer second = Build(name);

        //Act
        first.NoteOn(0, 60, 96);
        second.NoteOn(0, 60, 96);
        var (a, _) = RenderProbe.RenderSeconds(first, 0.5);
        var (b, _) = RenderProbe.RenderSeconds(second, 0.5);

        //Assert
        a.Should().Equal(b);
    }

    [Theory]
    [MemberData(nameof(EveryPreset))]
    public void every_preset_renders_a_block_without_allocating(string name)
    {
        //Arrange
        ModestSynthesizer synthesizer = Build(name);
        synthesizer.NoteOn(0, 60, 100);

        float[] left = new float[synthesizer.BlockSize];
        float[] right = new float[synthesizer.BlockSize];
        Action work = () => synthesizer.Render(left, right);
        work();

        //Act
        long bytes = AllocationProbe.LowestBytes(work);

        //Assert
        bytes.Should().Be(0L);
    }

    [Theory]
    [MemberData(nameof(EveryPreset))]
    public void every_preset_comes_back_new_each_time(string name)
    {
        //Arrange
        ModestPatch first = ModestSynthPresets.Create(name);

        //Act
        ModestPatch second = ModestSynthPresets.Create(name);

        //Assert
        ReferenceEquals(first, second).Should().BeFalse();
        second.Waveform.Should().Be(first.Waveform);
        second.CanCreateOscillator.Should().BeTrue();
    }

    [Fact]
    public void the_wavetable_pad_carries_a_table_built_in_code_and_touches_no_file()
    {
        //Arrange
        //Act
        ModestPatch patch = ModestSynthPresets.WavetablePad();

        //Assert
        patch.Waveform.Should().Be(ModestWaveform.Wavetable);
        patch.WavetableFile.Should().BeNull();
        patch.WavetableTable.Should().NotBeNull();
        patch.WavetableTable.FrameCount.Should().Be(4);
        patch.WavetableTable.FrameSize.Should().Be(1024);
    }

    [Fact]
    public void the_electric_piano_is_two_of_the_three_pairs_of_algorithm_five()
    {
        //Arrange
        //Act
        ModestPatch patch = ModestSynthPresets.ElectricPiano();

        //Assert
        patch.FmAlgorithm.Should().Be(5);
        patch.GetFmOperator(2).Ratio.Should().Be(14.0);
        patch.GetFmOperator(2).VelocitySensitivity.Should().Be(4);
        patch.GetFmOperator(5).Level.Should().Be(0.0);
        patch.GetFmOperator(6).Level.Should().Be(0.0);
    }

    [Fact]
    public void the_additive_organ_pulls_the_drawbar_partials_only()
    {
        //Arrange
        //Act
        ModestPatch patch = ModestSynthPresets.AdditiveOrgan();

        //Assert
        patch.GetPartialLevel(1).Should().Be(1.0);
        patch.GetPartialLevel(5).Should().Be(0.0);
        patch.GetPartialLevel(8).Should().Be(0.35);
        patch.HarmonicNormalization.Should().Be(1.0);
    }

    [Fact]
    public void a_name_that_is_not_a_preset_is_rejected()
    {
        //Arrange
        //Act
        Action create = () => ModestSynthPresets.Create("space harp");
        Action settings = () => ModestSynthPresets.SettingsFor("space harp", SampleRate);

        //Assert
        create.Should().Throw<ArgumentException>();
        settings.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void the_names_are_matched_without_regard_to_case() =>
        ModestSynthPresets.Create("SAW LEAD").Waveform.Should().Be(ModestWaveform.Saw);

    private static ModestSynthesizer Build(string name) =>
        new ModestSynthesizer(
            ModestSynthPresets.Create(name), ModestSynthPresets.SettingsFor(name, SampleRate));
}
