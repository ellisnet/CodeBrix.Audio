using System;
using System.Collections.Generic;
using CodeBrix.Audio.ModestSynth.Effects;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Effects;

/// <summary>
/// What <c>ModestSynth.Register()</c> hands the Decent Sampler engine, and what the factories it
/// registers build out of a parsed <c>&lt;effect&gt;</c> element.
/// </summary>
/// <remarks>
/// The effect elements come from a preset parsed out of a string, because the model's setters are
/// internal to CodeBrix.Audio: parsing is the only way for a consumer to hold a real
/// <see cref="DecentSamplerEffect" />, and it is also what the engine will be doing.
/// </remarks>
public class EffectRegistrationTests
{
    private const string PresetXml = @"<DecentSampler>
  <groups><group><sample path=""a.wav"" /></group></groups>
  <effects>
    <effect type=""phaser"" mix=""0.8"" modDepth=""0.4"" modRate=""1.5"" centerFrequency=""900"" feedback=""-0.3"" />
    <effect type=""pitch_shift"" pitchShift=""-5"" mix=""0.9"" />
    <effect type=""wave_folder"" drive=""12"" threshold=""0.4"" />
    <effect type=""wave_shaper"" drive=""25"" driveBoost=""0.4"" outputLevel=""0.7"" highQuality=""true"" />
    <effect type=""stereo_simulator"" algorithm=""lauridsen"" width=""0.8"" delayTime=""0.012"" modRate=""2"" modDepth=""0.6"" />
    <effect type=""bit_crusher"" bitDepth=""6"" sampleRateReduction=""5"" mix=""0.7"" />
    <effect type=""gate"" amount=""0.35"" mix=""0.6"" />
  </effects>
</DecentSampler>";

    [Fact]
    public void Register_offers_the_seven_creative_effects()
    {
        //Act
        ModestSynth.Register();

        //Assert
        ModestSynth.RegisteredEffectTypes.Should().Equal(
            "phaser", "pitch_shift", "wave_folder", "wave_shaper", "stereo_simulator", "bit_crusher",
            "gate");
    }

    [Fact]
    public void Register_puts_every_one_of_them_in_the_shared_registry()
    {
        //Act
        ModestSynth.Register();

        //Assert
        foreach (string type in ModestEffectTypes.All)
        {
            DecentSamplerExtensions.IsEffectRegistered(type).Should().BeTrue();
        }
    }

    [Fact]
    public void Register_fills_a_registry_of_your_own()
    {
        //Arrange
        DecentSamplerExtensionRegistry registry = new DecentSamplerExtensionRegistry();

        //Act
        ModestSynth.Register(registry);

        //Assert
        registry.RegisteredEffects.Should().HaveCount(ModestEffectTypes.All.Count);
        foreach (string type in ModestEffectTypes.All)
        {
            registry.IsEffectRegistered(type).Should().BeTrue();
        }
    }

    [Fact]
    public void Register_into_a_registry_twice_registers_nothing_twice()
    {
        //Arrange
        DecentSamplerExtensionRegistry registry = new DecentSamplerExtensionRegistry();
        ModestSynth.Register(registry);

        //Act
        ModestSynth.Register(registry);

        //Assert
        registry.RegisteredEffects.Should().HaveCount(ModestEffectTypes.All.Count);
    }

    [Fact]
    public void Register_refuses_a_null_registry()
    {
        //Act, Assert
        Assert.Throws<ArgumentNullException>(() => ModestSynth.Register(null));
    }

    [Fact]
    public void Register_leaves_the_cores_own_effect_types_alone()
    {
        //Arrange
        DecentSamplerExtensionRegistry registry = new DecentSamplerExtensionRegistry();

        //Act
        ModestSynth.Register(registry);

        //Assert
        registry.IsEffectRegistered("reverb").Should().BeFalse();
        registry.IsEffectRegistered("chorus").Should().BeFalse();
    }

    [Fact]
    public void A_registered_factory_reads_the_phasers_attributes()
    {
        //Act
        PhaserEffect effect = (PhaserEffect)Build(0);

        //Assert
        effect.Mix.Should().Be(0.8);
        effect.ModDepth.Should().Be(0.4);
        effect.ModRate.Should().Be(1.5);
        effect.CenterFrequency.Should().Be(900.0);
        effect.Feedback.Should().Be(-0.3);
        effect.SampleRate.Should().Be(EffectSignals.SampleRate);
        effect.IsPrepared.Should().BeTrue();
    }

    [Fact]
    public void A_registered_factory_reads_the_pitch_shifters_attributes()
    {
        //Act
        PitchShiftEffect effect = (PitchShiftEffect)Build(1);

        //Assert
        effect.PitchShift.Should().Be(-5.0);
        effect.Mix.Should().Be(0.9);
    }

    [Fact]
    public void A_registered_factory_reads_the_wave_folders_attributes()
    {
        //Act
        WaveFolderEffect effect = (WaveFolderEffect)Build(2);

        //Assert
        effect.Drive.Should().Be(12.0);
        effect.Threshold.Should().Be(0.4);
        effect.Mix.Should().Be(1.0);
    }

    [Fact]
    public void A_registered_factory_reads_the_wave_shapers_attributes()
    {
        //Act
        WaveShaperEffect effect = (WaveShaperEffect)Build(3);

        //Assert
        effect.Drive.Should().Be(25.0);
        effect.DriveBoost.Should().Be(0.4);
        effect.OutputLevel.Should().Be(0.7);
        effect.HighQuality.Should().BeTrue();
    }

    [Fact]
    public void A_registered_factory_reads_the_stereo_simulators_attributes()
    {
        //Act
        StereoSimulatorEffect effect = (StereoSimulatorEffect)Build(4);

        //Assert
        effect.Algorithm.Should().Be(ModestStereoAlgorithm.Lauridsen);
        effect.Width.Should().Be(0.8);
        effect.DelayTime.Should().Be(0.012);
        effect.ModRate.Should().Be(2.0);
        effect.ModDepth.Should().Be(0.6);
    }

    [Fact]
    public void A_registered_factory_reads_the_bit_crushers_attributes()
    {
        //Act
        BitCrusherEffect effect = (BitCrusherEffect)Build(5);

        //Assert
        effect.BitDepth.Should().Be(6.0);
        effect.SampleRateReduction.Should().Be(5.0);
        effect.Mix.Should().Be(0.7);
    }

    [Fact]
    public void A_registered_factory_reads_the_gates_attributes()
    {
        //Act
        GateEffect effect = (GateEffect)Build(6);

        //Assert
        effect.Amount.Should().Be(0.35);
        effect.Mix.Should().Be(0.6);
    }

    [Fact]
    public void Two_gates_in_one_chain_do_not_stutter_in_lock_step()
    {
        //Arrange
        IReadOnlyList<DecentSamplerEffect> parsed = ParsedEffects();

        //Act
        GateEffect first = (GateEffect)Build(6, 0);
        GateEffect second = (GateEffect)Build(6, 3);

        //Assert
        parsed.Count.Should().Be(7);
        first.Seed.Should().NotBe(second.Seed);
    }

    private static IInstrumentEffect Build(int index) => Build(index, -1);

    private static IInstrumentEffect Build(int index, int chainIndex)
    {
        ModestSynth.Register();

        DecentSamplerEffect source = ParsedEffects()[index];

        Func<EffectContext, IInstrumentEffect> factory;
        DecentSamplerExtensions.Shared.TryGetEffectFactory(source.TypeName, out factory)
            .Should().BeTrue();

        return factory(new EffectContext(
            null, source, EffectSignals.SampleRate, DecentSamplerEffectPlacement.Instrument, chainIndex));
    }

    private static IReadOnlyList<DecentSamplerEffect> ParsedEffects() =>
        DecentSamplerParser.ParseText(PresetXml).Effects.Effects;
}
