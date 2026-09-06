using System;
using CodeBrix.Audio.ModestSynth.Effects;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Effects;

/// <summary>
/// Tests for <see cref="ModestEffectFactory" /> and <see cref="ModestEffectTypes" />.
/// </summary>
public class ModestEffectFactoryTests
{
    [Fact]
    public void SupportedTypes_is_the_seven_creative_effects()
    {
        //Act, Assert
        ModestEffectFactory.SupportedTypes.Should().Equal(
            "phaser", "pitch_shift", "wave_folder", "wave_shaper", "stereo_simulator", "bit_crusher",
            "gate");
    }

    [Theory]
    [InlineData("phaser", typeof(PhaserEffect))]
    [InlineData("pitch_shift", typeof(PitchShiftEffect))]
    [InlineData("wave_folder", typeof(WaveFolderEffect))]
    [InlineData("wave_shaper", typeof(WaveShaperEffect))]
    [InlineData("stereo_simulator", typeof(StereoSimulatorEffect))]
    [InlineData("bit_crusher", typeof(BitCrusherEffect))]
    [InlineData("gate", typeof(GateEffect))]
    public void Create_builds_the_effect_the_name_asks_for(string type, Type expected)
    {
        //Act
        IInstrumentEffect effect = ModestEffectFactory.Create(type);

        //Assert
        effect.Should().BeOfType(expected);
    }

    [Theory]
    [InlineData("PITCH_SHIFT")]
    [InlineData("pitchShift")]
    [InlineData("pitch-shift")]
    public void A_type_name_is_matched_without_case_or_punctuation(string spelling)
    {
        //Act
        IInstrumentEffect effect;
        bool created = ModestEffectFactory.TryCreate(spelling, out effect);

        //Assert
        created.Should().BeTrue();
        effect.Should().BeOfType(typeof(PitchShiftEffect));
    }

    [Theory]
    [InlineData("reverb")]
    [InlineData("chorus")]
    [InlineData("lowpass")]
    [InlineData("compressor")]
    public void The_cores_own_effects_are_not_this_packages(string type)
    {
        //Act, Assert
        ModestEffectFactory.IsSupported(type).Should().BeFalse();
    }

    [Fact]
    public void Create_says_so_when_the_effect_belongs_to_the_core()
    {
        //Act, Assert
        Assert.Throws<NotSupportedException>(() => ModestEffectFactory.Create("reverb"));
    }

    [Fact]
    public void TryCreate_refuses_a_name_nothing_recognises()
    {
        //Act
        IInstrumentEffect effect;
        bool created = ModestEffectFactory.TryCreate("not_an_effect", out effect);

        //Assert
        created.Should().BeFalse();
        effect.Should().BeNull();
    }

    [Fact]
    public void Create_refuses_a_null_name()
    {
        //Act, Assert
        Assert.Throws<ArgumentNullException>(() => ModestEffectFactory.Create(null));
    }
}
