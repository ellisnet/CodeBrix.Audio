using System;
using CodeBrix.Audio.Synth.DecentSampler.Bindings;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Bindings;

/// <summary>
/// Covers the modulation slot arithmetic: the four <c>modBehavior</c> values, the depth, and the two
/// scopes. This is the machinery the modulator runtime plugs into.
/// </summary>
public class DecentSamplerParameterTests
{
    [Fact]
    public void a_target_publishes_its_base_value_when_it_is_written()
    {
        //Arrange
        var published = 0.0;
        var target = Target(1.0, value => published = value);

        //Act
        target.SetBaseValue(DecentSamplerParameterValue.FromNumber(0.25));

        //Assert
        published.Should().Be(0.25);
        target.BaseValue.AsNumber.Should().Be(0.25);
    }

    [Fact]
    public void set_replaces_the_base_value_at_full_depth()
    {
        //Arrange
        var target = Target(0.2);

        //Act
        target.SetModulation(1, -1, Contribution(DecentSamplerModBehavior.Set, 1.0, 0.9, 0.5));

        //Assert
        target.GetEffectiveValue().AsNumber.Should().BeApproximately(0.9, 1e-12);
    }

    [Fact]
    public void set_at_half_depth_is_half_the_translated_value()
    {
        //Arrange
        var target = Target(0.2);

        //Act
        // MEASURED: `set` discards the base entirely and modAmount scales the value, so half depth on
        // a translated 1.0 lands on 0.5 rather than half way between 0.2 and 1.0.
        target.SetModulation(1, -1, Contribution(DecentSamplerModBehavior.Set, 0.5, 1.0, 0.5));

        //Assert
        target.GetEffectiveValue().AsNumber.Should().BeApproximately(0.5, 1e-12);
    }

    [Fact]
    public void add_sums_the_translated_value_scaled_by_the_depth()
    {
        //Arrange
        var target = Target(0.2);

        //Act
        target.SetModulation(1, -1, Contribution(DecentSamplerModBehavior.Add, 0.5, 0.4, 0.0));

        //Assert
        target.GetEffectiveValue().AsNumber.Should().BeApproximately(0.4, 1e-12);
    }

    [Fact]
    public void modulate_adds_a_zero_centred_delta_so_a_neutral_value_changes_nothing()
    {
        //Arrange
        var target = Target(0.3);

        //Act
        target.SetModulation(1, -1, Contribution(DecentSamplerModBehavior.Modulate, 1.0, 0.5, 0.5));

        //Assert
        target.GetEffectiveValue().AsNumber.Should().BeApproximately(0.3, 1e-12);
    }

    [Fact]
    public void modulate_offsets_by_the_distance_from_neutral()
    {
        //Arrange
        var target = Target(0.3);

        //Act
        target.SetModulation(1, -1, Contribution(DecentSamplerModBehavior.Modulate, 1.0, 0.9, 0.5));

        //Assert
        target.GetEffectiveValue().AsNumber.Should().BeApproximately(0.7, 1e-12);
    }

    [Fact]
    public void multiply_scales_the_base_value()
    {
        //Arrange
        var target = Target(0.4);

        //Act
        target.SetModulation(1, -1, Contribution(DecentSamplerModBehavior.Multiply, 1.0, 0.5, 0.5));

        //Assert
        target.GetEffectiveValue().AsNumber.Should().BeApproximately(0.2, 1e-12);
    }

    [Fact]
    public void multiply_at_zero_depth_scales_the_base_value_away()
    {
        //Arrange
        var target = Target(0.4);

        //Act
        // MEASURED: modAmount scales the translated value rather than blending toward the base, so a
        // depth of zero makes a multiply contribute a factor of zero.
        target.SetModulation(1, -1, Contribution(DecentSamplerModBehavior.Multiply, 0.0, 0.5, 0.5));

        //Assert
        target.GetEffectiveValue().AsNumber.Should().BeApproximately(0.0, 1e-12);
    }

    [Fact]
    public void contributions_from_several_sources_stack_in_the_order_they_arrived()
    {
        //Arrange
        var target = Target(0.1);

        //Act
        target.SetModulation(1, -1, Contribution(DecentSamplerModBehavior.Add, 1.0, 0.2, 0.0));
        target.SetModulation(2, -1, Contribution(DecentSamplerModBehavior.Multiply, 1.0, 2.0, 1.0));

        //Assert
        target.GetEffectiveValue().AsNumber.Should().BeApproximately(0.6, 1e-12);
    }

    [Fact]
    public void a_second_contribution_from_the_same_source_replaces_the_first()
    {
        //Arrange
        var target = Target(0.1);

        //Act
        target.SetModulation(1, -1, Contribution(DecentSamplerModBehavior.Add, 1.0, 0.2, 0.0));
        target.SetModulation(1, -1, Contribution(DecentSamplerModBehavior.Add, 1.0, 0.5, 0.0));

        //Assert
        target.ModulationCount.Should().Be(1);
        target.GetEffectiveValue().AsNumber.Should().BeApproximately(0.6, 1e-12);
    }

    [Fact]
    public void a_voice_scope_contribution_is_invisible_to_the_global_value()
    {
        //Arrange
        var target = Target(0.1);

        //Act
        target.SetModulation(1, 7, Contribution(DecentSamplerModBehavior.Add, 1.0, 0.4, 0.0));

        //Assert
        target.GetEffectiveValue().AsNumber.Should().BeApproximately(0.1, 1e-12);
        target.GetEffectiveValue(7).AsNumber.Should().BeApproximately(0.5, 1e-12);
    }

    [Fact]
    public void one_voice_does_not_see_another_voices_contribution()
    {
        //Arrange
        var target = Target(0.1);

        //Act
        target.SetModulation(1, 7, Contribution(DecentSamplerModBehavior.Add, 1.0, 0.4, 0.0));
        target.SetModulation(1, 8, Contribution(DecentSamplerModBehavior.Add, 1.0, 0.8, 0.0));

        //Assert
        target.GetEffectiveValue(7).AsNumber.Should().BeApproximately(0.5, 1e-12);
        target.GetEffectiveValue(8).AsNumber.Should().BeApproximately(0.9, 1e-12);
    }

    [Fact]
    public void a_voice_sees_the_global_contributions_as_well_as_its_own()
    {
        //Arrange
        var target = Target(0.1);

        //Act
        target.SetModulation(1, -1, Contribution(DecentSamplerModBehavior.Add, 1.0, 0.2, 0.0));
        target.SetModulation(2, 7, Contribution(DecentSamplerModBehavior.Add, 1.0, 0.3, 0.0));

        //Assert
        target.GetEffectiveValue(7).AsNumber.Should().BeApproximately(0.6, 1e-12);
    }

    [Fact]
    public void clearing_a_voice_removes_only_that_voices_contributions()
    {
        //Arrange
        var target = Target(0.1);
        target.SetModulation(1, -1, Contribution(DecentSamplerModBehavior.Add, 1.0, 0.2, 0.0));
        target.SetModulation(2, 7, Contribution(DecentSamplerModBehavior.Add, 1.0, 0.3, 0.0));

        //Act
        target.ClearVoice(7);

        //Assert
        target.ModulationCount.Should().Be(1);
        target.GetEffectiveValue(7).AsNumber.Should().BeApproximately(0.3, 1e-12);
    }

    [Fact]
    public void clearing_a_source_removes_its_contribution()
    {
        //Arrange
        var target = Target(0.1);
        target.SetModulation(1, -1, Contribution(DecentSamplerModBehavior.Add, 1.0, 0.2, 0.0));

        //Act
        target.ClearModulation(1, -1);

        //Assert
        target.ModulationCount.Should().Be(0);
        target.GetEffectiveValue().AsNumber.Should().BeApproximately(0.1, 1e-12);
    }

    [Fact]
    public void a_textual_target_ignores_modulation()
    {
        //Arrange
        var published = string.Empty;
        var target = new DecentSamplerParameter(
            "control[0].TEXT", "TEXT", DecentSamplerParameterValueKind.Text,
            DecentSamplerParameterValue.FromText("hello"), value => published = value.AsText);

        //Act
        target.SetModulation(1, -1, Contribution(DecentSamplerModBehavior.Add, 1.0, 5.0, 0.0));

        //Assert
        target.GetEffectiveValue().AsText.Should().Be("hello");
        published.Should().Be("hello");
    }

    private static DecentSamplerModulationContribution Contribution(
        DecentSamplerModBehavior behavior, double amount, double value, double neutral) =>
        new DecentSamplerModulationContribution(behavior, amount, value, neutral);

    private static DecentSamplerParameter Target(double initial, Action<double> publish = null) =>
        new DecentSamplerParameter(
            "test.PARAMETER", "PARAMETER", DecentSamplerParameterValueKind.Number,
            DecentSamplerParameterValue.FromNumber(initial),
            value => publish?.Invoke(value.AsNumber));
}
