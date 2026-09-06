using System;
using CodeBrix.Audio.ModestSynth.Oscillators;
using CodeBrix.Audio.ModestSynth.Patch;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// Tests for <see cref="ModestPatch" />: every default is the format's documented default, every
/// out-of-range value is clamped rather than thrown at, and the patch builds a voice that carries
/// its parameters.
/// </summary>
public class ModestPatchTests
{
    [Fact]
    public void A_new_patch_carries_every_documented_default()
    {
        //Arrange
        ModestPatch patch = new ModestPatch();

        //Assert
        patch.Waveform.Should().Be(ModestWaveform.Sine);
        patch.Damping.Should().Be(0.5);
        patch.PluckType.Should().Be(0.5);
        patch.RandomPhase.Should().BeFalse();
        patch.Seed.Should().Be(0u);
        patch.WavetableFile.Should().BeNull();
        patch.WavetableFrameSize.Should().Be(2048);
        patch.WavetablePosition.Should().Be(0.0);
        patch.WavetableFrameInterpolation.Should().BeTrue();
        patch.NumPartials.Should().Be(8);
        patch.HarmonicTilt.Should().Be(0.0);
        patch.HarmonicOddEvenBalance.Should().Be(0.5);
        patch.HarmonicNormalization.Should().Be(0.0);
        patch.FmAlgorithm.Should().Be(1);
    }

    [Fact]
    public void Every_harmonic_partial_starts_silent()
    {
        //Arrange
        ModestPatch patch = new ModestPatch();

        //Act, Assert
        for (int partial = 1; partial <= ModestPatch.MaximumPartials; partial++)
        {
            patch.GetPartialLevel(partial).Should().Be(0.0);
        }
    }

    [Fact]
    public void Every_fm_operator_carries_its_own_documented_defaults()
    {
        //Arrange
        ModestPatch patch = new ModestPatch();

        //Assert
        patch.FmOperators.Should().HaveCount(6);
        foreach (ModestFmOperator op in patch.FmOperators)
        {
            // MEASURED (round 2, item 27): fmOpNRatio defaults to N, not to 1.
            op.Ratio.Should().Be(op.Number);
            op.Detune.Should().Be(0);
            op.Mode.Should().Be(ModestFmOperatorMode.Ratio);
            op.FixedFrequency.Should().Be(440.0);
            op.VelocitySensitivity.Should().Be(0);
            op.Feedback.Should().Be(0.0);
            op.Attack.Should().Be(0.0);
            op.Decay.Should().Be(0.0);
            op.Sustain.Should().Be(1.0);
            op.Release.Should().Be(-1.0);
            op.EnvelopeType.Should().Be(ModestFmEnvelopeType.Adsr);
            op.EgRate1.Should().Be(99);
            op.EgRate2.Should().Be(99);
            op.EgRate3.Should().Be(0);
            op.EgRate4.Should().Be(99);
        }

        // Only operator 1 starts at full level; see the ModestFmOperator remarks for why that
        // differs from the format's own attribute table.
        patch.GetFmOperator(1).Level.Should().Be(1.0);
        for (int number = 2; number <= 6; number++)
        {
            patch.GetFmOperator(number).Level.Should().Be(0.0);
        }
    }

    [Fact]
    public void GetFmOperator_numbers_them_the_way_the_attributes_do()
    {
        //Arrange
        ModestPatch patch = new ModestPatch();

        //Act
        ModestFmOperator third = patch.GetFmOperator(3);

        //Assert
        third.Number.Should().Be(3);
        third.Should().BeSameAs(patch.FmOperators[2]);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void GetFmOperator_rejects_an_operator_that_does_not_exist(int number)
    {
        //Arrange
        ModestPatch patch = new ModestPatch();

        //Act
        Action act = () => patch.GetFmOperator(number);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(65)]
    public void GetPartialLevel_rejects_a_partial_that_does_not_exist(int partial)
    {
        //Arrange
        ModestPatch patch = new ModestPatch();

        //Act
        Action act = () => patch.GetPartialLevel(partial);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Out_of_range_values_are_clamped_rather_than_rejected()
    {
        //Arrange
        ModestPatch patch = new ModestPatch();

        //Act
        patch.Damping = 4.0;
        patch.PluckType = -4.0;
        patch.WavetablePosition = 9.0;
        patch.NumPartials = 500;
        patch.HarmonicTilt = -9.0;
        patch.FmAlgorithm = 99;

        //Assert
        patch.Damping.Should().Be(1.0);
        patch.PluckType.Should().Be(0.0);
        patch.WavetablePosition.Should().Be(1.0);
        patch.NumPartials.Should().Be(64);
        patch.HarmonicTilt.Should().Be(-1.0);
        patch.FmAlgorithm.Should().Be(32);
    }

    [Fact]
    public void A_value_that_is_not_a_number_leaves_the_previous_one_in_place()
    {
        //Arrange
        ModestPatch patch = new ModestPatch();
        patch.Damping = 0.75;

        //Act
        patch.Damping = double.NaN;

        //Assert
        patch.Damping.Should().Be(0.75);
    }

    [Fact]
    public void SetPartialLevel_and_GetPartialLevel_agree()
    {
        //Arrange
        ModestPatch patch = new ModestPatch();

        //Act
        patch.SetPartialLevel(7, 0.4);

        //Assert
        patch.GetPartialLevel(7).Should().Be(0.4);
        patch.GetPartialLevel(6).Should().Be(0.0);
    }

    [Fact]
    public void CreateOscillator_hands_the_pluck_parameters_to_the_string()
    {
        //Arrange
        ModestPatch patch = new ModestPatch
        {
            Waveform = ModestWaveform.Pluck1,
            Damping = 0.8,
            PluckType = 0.2,
            Seed = 777u,
        };

        //Act
        Pluck1Oscillator oscillator = (Pluck1Oscillator)patch.CreateOscillator(44100);

        //Assert
        oscillator.SampleRate.Should().Be(44100);
        oscillator.Damping.Should().Be(0.8);
        oscillator.PluckType.Should().Be(0.2);
        oscillator.Seed.Should().Be(777u);
    }

    [Fact]
    public void CreateOscillator_hands_the_seed_to_the_noise_source()
    {
        //Arrange
        ModestPatch patch = new ModestPatch { Waveform = ModestWaveform.Noise, Seed = 555u };

        //Act
        NoiseOscillator oscillator = (NoiseOscillator)patch.CreateOscillator();

        //Assert
        oscillator.Seed.Should().Be(555u);
    }

    [Fact]
    public void CreateOscillator_leaves_the_oscillators_own_seed_alone_when_the_patch_has_none()
    {
        //Arrange
        ModestPatch patch = new ModestPatch { Waveform = ModestWaveform.Noise };

        //Act
        NoiseOscillator oscillator = (NoiseOscillator)patch.CreateOscillator();

        //Assert
        oscillator.Seed.Should().Be(NoiseOscillator.DefaultSeed);
    }

    [Fact]
    public void CreateOscillator_builds_the_formant_tone_now_that_it_has_a_generator()
    {
        //Arrange
        ModestPatch patch = new ModestPatch { Waveform = ModestWaveform.Formant };

        //Act
        IModestOscillator oscillator = patch.CreateOscillator();

        //Assert
        patch.CanCreateOscillator.Should().BeTrue();
        oscillator.Should().BeOfType(typeof(FormantOscillator));
    }

    [Fact]
    public void GetStartPhase_is_zero_when_random_phase_is_off()
    {
        //Arrange
        ModestPatch patch = new ModestPatch();

        //Act, Assert
        patch.GetStartPhase(0u).Should().Be(0.0);
        patch.GetStartPhase(12345u).Should().Be(0.0);
    }

    [Fact]
    public void GetStartPhase_scatters_voices_when_random_phase_is_on()
    {
        //Arrange
        ModestPatch patch = new ModestPatch { RandomPhase = true };

        //Act
        double first = patch.GetStartPhase(0u);
        double second = patch.GetStartPhase(1u);
        double third = patch.GetStartPhase(2u);

        //Assert
        first.Should().NotBe(second);
        second.Should().NotBe(third);
        first.Should().BeInRange(0.0, 1.0);
        second.Should().BeInRange(0.0, 1.0);
        third.Should().BeInRange(0.0, 1.0);
    }

    [Fact]
    public void GetStartPhase_gives_the_same_answer_for_the_same_voice_every_time()
    {
        //Arrange
        ModestPatch patch = new ModestPatch { RandomPhase = true, Seed = 3u };

        //Act
        double first = patch.GetStartPhase(9u);
        double again = patch.GetStartPhase(9u);

        //Assert
        again.Should().Be(first);
    }
}
