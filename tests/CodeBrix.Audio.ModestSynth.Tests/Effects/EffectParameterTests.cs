using System;
using CodeBrix.Audio.ModestSynth.Effects;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Effects;

/// <summary>
/// The parameter plumbing every creative effect shares: the binding names from the guide's
/// appendix, the attribute names from the effect's own page, and the rules about case, punctuation
/// and values that are not numbers.
/// </summary>
public class EffectParameterTests
{
    [Theory]
    [InlineData(ModestEffectTypes.Phaser, "FX_MIX", 0.75)]
    [InlineData(ModestEffectTypes.Phaser, "FX_MOD_DEPTH", 0.6)]
    [InlineData(ModestEffectTypes.Phaser, "FX_MOD_RATE", 3.5)]
    [InlineData(ModestEffectTypes.Phaser, "FX_CENTER_FREQUENCY", 1200.0)]
    [InlineData(ModestEffectTypes.Phaser, "FX_FEEDBACK", -0.4)]
    [InlineData(ModestEffectTypes.PitchShift, "FX_PITCH_SHIFT", -5.0)]
    [InlineData(ModestEffectTypes.PitchShift, "FX_MIX", 0.25)]
    [InlineData(ModestEffectTypes.WaveFolder, "FX_DRIVE", 42.0)]
    [InlineData(ModestEffectTypes.WaveFolder, "FX_THRESHOLD", 0.8)]
    [InlineData(ModestEffectTypes.WaveFolder, "FX_MIX", 0.3)]
    [InlineData(ModestEffectTypes.WaveShaper, "FX_DRIVE", 250.0)]
    [InlineData(ModestEffectTypes.WaveShaper, "FX_DRIVE_BOOST", 0.2)]
    [InlineData(ModestEffectTypes.WaveShaper, "FX_OUTPUT_LEVEL", 0.9)]
    [InlineData(ModestEffectTypes.WaveShaper, "FX_MIX", 0.4)]
    [InlineData(ModestEffectTypes.StereoSimulator, "FX_WIDTH", 0.85)]
    [InlineData(ModestEffectTypes.StereoSimulator, "FX_DELAY_TIME", 0.02)]
    [InlineData(ModestEffectTypes.StereoSimulator, "FX_MOD_RATE", 4.0)]
    [InlineData(ModestEffectTypes.StereoSimulator, "FX_MOD_DEPTH", 0.7)]
    [InlineData(ModestEffectTypes.BitCrusher, "FX_BIT_DEPTH", 6.0)]
    [InlineData(ModestEffectTypes.BitCrusher, "FX_SAMPLE_RATE_REDUCTION", 12.0)]
    [InlineData(ModestEffectTypes.BitCrusher, "FX_MIX", 0.65)]
    [InlineData(ModestEffectTypes.Gate, "FX_GATE_AMOUNT", 0.2)]
    [InlineData(ModestEffectTypes.Gate, "FX_MIX", 0.55)]
    public void TrySetParameter_takes_every_binding_name_the_guide_lists(
        string type, string parameter, double value)
    {
        //Arrange
        IInstrumentEffect effect = ModestEffectFactory.Create(type);

        //Act
        bool set = effect.TrySetParameter(parameter, value);

        //Assert
        double read;
        set.Should().BeTrue();
        effect.TryGetParameter(parameter, out read).Should().BeTrue();
        read.Should().BeApproximately(value, 1.0e-9);
    }

    [Theory]
    [InlineData(ModestEffectTypes.Phaser, "modDepth", "FX_MOD_DEPTH", 0.4)]
    [InlineData(ModestEffectTypes.Phaser, "centerFrequency", "FX_CENTER_FREQUENCY", 640.0)]
    [InlineData(ModestEffectTypes.Phaser, "feedback", "FX_FEEDBACK", 0.4)]
    [InlineData(ModestEffectTypes.PitchShift, "pitchShift", "FX_PITCH_SHIFT", 0.4)]
    [InlineData(ModestEffectTypes.WaveFolder, "threshold", "FX_THRESHOLD", 0.4)]
    [InlineData(ModestEffectTypes.WaveFolder, "drive", "FX_DRIVE", 4.0)]
    [InlineData(ModestEffectTypes.WaveShaper, "driveBoost", "FX_DRIVE_BOOST", 0.4)]
    [InlineData(ModestEffectTypes.WaveShaper, "outputLevel", "FX_OUTPUT_LEVEL", 0.4)]
    [InlineData(ModestEffectTypes.StereoSimulator, "width", "FX_WIDTH", 0.4)]
    [InlineData(ModestEffectTypes.BitCrusher, "bitDepth", "FX_BIT_DEPTH", 8.0)]
    [InlineData(ModestEffectTypes.BitCrusher, "sampleRateReduction", "FX_SAMPLE_RATE_REDUCTION", 4.0)]
    [InlineData(ModestEffectTypes.Gate, "amount", "FX_GATE_AMOUNT", 0.4)]
    public void An_attribute_name_and_its_binding_name_are_one_parameter(
        string type, string attribute, string binding, double value)
    {
        //Arrange
        IInstrumentEffect effect = ModestEffectFactory.Create(type);

        //Act
        effect.TrySetParameter(attribute, value).Should().BeTrue();

        //Assert
        double read;
        effect.TryGetParameter(binding, out read).Should().BeTrue();
        read.Should().BeApproximately(value, 1.0e-9);
    }

    [Theory]
    [InlineData("fx_mix")]
    [InlineData("FX MIX")]
    [InlineData("fxMix")]
    [InlineData("mix")]
    [InlineData("MIX")]
    public void A_parameter_name_is_matched_without_case_or_punctuation(string spelling)
    {
        //Arrange
        IInstrumentEffect effect = ModestEffectFactory.Create(ModestEffectTypes.Phaser);

        //Act
        bool set = effect.TrySetParameter(spelling, 0.125);

        //Assert
        double read;
        set.Should().BeTrue();
        effect.TryGetParameter("FX_MIX", out read).Should().BeTrue();
        read.Should().Be(0.125);
    }

    [Theory]
    [InlineData(ModestEffectTypes.Phaser)]
    [InlineData(ModestEffectTypes.PitchShift)]
    [InlineData(ModestEffectTypes.WaveFolder)]
    [InlineData(ModestEffectTypes.WaveShaper)]
    [InlineData(ModestEffectTypes.StereoSimulator)]
    [InlineData(ModestEffectTypes.BitCrusher)]
    [InlineData(ModestEffectTypes.Gate)]
    public void ENABLED_is_a_parameter_on_every_effect(string type)
    {
        //Arrange
        IInstrumentEffect effect = ModestEffectFactory.Create(type);

        //Act
        effect.TrySetParameter("ENABLED", 0.0).Should().BeTrue();

        //Assert
        double read;
        effect.Enabled.Should().BeFalse();
        effect.TryGetParameter("ENABLED", out read).Should().BeTrue();
        read.Should().Be(0.0);
    }

    [Theory]
    [InlineData(ModestEffectTypes.Phaser)]
    [InlineData(ModestEffectTypes.PitchShift)]
    [InlineData(ModestEffectTypes.WaveFolder)]
    [InlineData(ModestEffectTypes.WaveShaper)]
    [InlineData(ModestEffectTypes.StereoSimulator)]
    [InlineData(ModestEffectTypes.BitCrusher)]
    [InlineData(ModestEffectTypes.Gate)]
    public void A_disabled_effect_passes_the_block_through(string type)
    {
        //Arrange
        IInstrumentEffect effect = ModestEffectFactory.Create(type);
        effect.TrySetParameter("ENABLED", "false");
        float[] source = EffectSignals.Noise(2048, 61u, 0.5);

        //Act
        float[] left;
        float[] right;
        EffectSignals.RenderMono(effect, source, out left, out right);

        //Assert
        left.Should().Equal(source);
    }

    [Theory]
    [InlineData(ModestEffectTypes.Phaser)]
    [InlineData(ModestEffectTypes.PitchShift)]
    [InlineData(ModestEffectTypes.WaveFolder)]
    [InlineData(ModestEffectTypes.WaveShaper)]
    [InlineData(ModestEffectTypes.StereoSimulator)]
    [InlineData(ModestEffectTypes.BitCrusher)]
    [InlineData(ModestEffectTypes.Gate)]
    public void An_unknown_parameter_is_refused_rather_than_swallowed(string type)
    {
        //Arrange
        IInstrumentEffect effect = ModestEffectFactory.Create(type);

        //Act
        bool set = effect.TrySetParameter("FX_NOT_A_PARAMETER", 1.0);

        //Assert
        double read;
        set.Should().BeFalse();
        effect.TryGetParameter("FX_NOT_A_PARAMETER", out read).Should().BeFalse();
        read.Should().Be(0.0);
    }

    [Fact]
    public void A_value_that_is_not_a_number_leaves_the_parameter_alone()
    {
        //Arrange
        IInstrumentEffect effect = ModestEffectFactory.Create(ModestEffectTypes.Phaser);
        effect.TrySetParameter("FX_MOD_RATE", 4.0);

        //Act
        bool set = effect.TrySetParameter("FX_MOD_RATE", double.NaN);

        //Assert
        double read;
        set.Should().BeTrue();
        effect.TryGetParameter("FX_MOD_RATE", out read);
        read.Should().Be(4.0);
    }

    [Fact]
    public void An_out_of_range_value_is_clamped_rather_than_refused()
    {
        //Arrange
        IInstrumentEffect effect = ModestEffectFactory.Create(ModestEffectTypes.BitCrusher);

        //Act
        bool set = effect.TrySetParameter("FX_BIT_DEPTH", 400.0);

        //Assert
        double read;
        set.Should().BeTrue();
        effect.TryGetParameter("FX_BIT_DEPTH", out read);
        read.Should().Be(BitCrusherEffect.MaximumBitDepth);
    }

    [Fact]
    public void HighQuality_is_settable_as_a_number_and_as_a_word()
    {
        //Arrange
        WaveShaperEffect effect = new WaveShaperEffect();

        //Act
        effect.TrySetParameter("highQuality", 1.0).Should().BeTrue();
        bool afterNumber = effect.HighQuality;
        effect.TrySetParameter("highQuality", "false").Should().BeTrue();

        //Assert
        afterNumber.Should().BeTrue();
        effect.HighQuality.Should().BeFalse();
    }

    [Fact]
    public void Tags_default_to_an_empty_list_and_never_become_null()
    {
        //Arrange
        PhaserEffect effect = new PhaserEffect();

        //Act
        effect.Tags = null;

        //Assert
        effect.Tags.Should().NotBeNull();
        effect.Tags.Count.Should().Be(0);
    }

    [Fact]
    public void Process_refuses_one_buffer_used_as_both_channels()
    {
        //Arrange
        PhaserEffect effect = new PhaserEffect();
        effect.Prepare(EffectSignals.SampleRate);
        float[] shared = new float[64];

        //Act, Assert
        Assert.Throws<ArgumentException>(() => effect.Process(shared, shared, 64));
    }

    [Fact]
    public void Prepare_refuses_a_sample_rate_nothing_renders_at()
    {
        //Arrange
        PhaserEffect effect = new PhaserEffect();

        //Act, Assert
        Assert.Throws<ArgumentOutOfRangeException>(() => effect.Prepare(10));
    }

    [Fact]
    public void An_effect_that_was_never_prepared_prepares_itself_at_the_default_rate()
    {
        //Arrange
        PhaserEffect effect = new PhaserEffect();
        float[] left = new float[64];
        float[] right = new float[64];

        //Act
        effect.Process(left, right, 64);

        //Assert
        effect.IsPrepared.Should().BeTrue();
        effect.SampleRate.Should().Be(ModestEffectBase.DefaultSampleRate);
    }
}
