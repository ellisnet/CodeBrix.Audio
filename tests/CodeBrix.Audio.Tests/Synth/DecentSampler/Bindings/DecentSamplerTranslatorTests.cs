using System.Collections.Generic;
using System.Globalization;
using CodeBrix.Audio.Synth.DecentSampler.Bindings;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Bindings;

/// <summary>Covers the three translation modes and the coercions a fixed value goes through.</summary>
public class DecentSamplerTranslatorTests
{
    [Fact]
    public void a_linear_translation_maps_the_source_range_onto_the_output_range()
    {
        //Arrange
        var binding = Linear(0.0, 2000.0);

        //Act
        var translated = DecentSamplerTranslator.Translate(binding, Knob(0.25, 0.0, 1.0));

        //Assert
        translated.AsNumber.Should().BeApproximately(500.0, 1e-9);
    }

    [Fact]
    public void a_linear_translation_of_a_controller_uses_nought_to_a_hundred_and_twenty_seven()
    {
        //Arrange
        var binding = Linear(0.0, 1.0);

        //Act
        var translated = DecentSamplerTranslator.Translate(binding, DecentSamplerBindingInput.Controller(127));

        //Assert
        translated.AsNumber.Should().BeApproximately(1.0, 1e-9);
    }

    [Fact]
    public void a_reversed_linear_translation_runs_the_other_way()
    {
        //Arrange
        var binding = Linear(0.0, 1.0);
        binding.TranslationReversed = true;

        //Act
        var translated = DecentSamplerTranslator.Translate(binding, Knob(0.25, 0.0, 1.0));

        //Assert
        translated.AsNumber.Should().BeApproximately(0.75, 1e-9);
    }

    [Fact]
    public void a_linear_translation_with_no_output_range_passes_the_value_through()
    {
        //Arrange
        var binding = new DecentSamplerBinding { Parameter = "AMP_VOLUME" };

        //Act
        var translated = DecentSamplerTranslator.Translate(binding, Knob(0.35, 0.0, 1.0));

        //Assert
        translated.AsNumber.Should().BeApproximately(0.35, 1e-12);
    }

    [Fact]
    public void a_reversed_pass_through_mirrors_within_the_source_range()
    {
        //Arrange
        var binding = new DecentSamplerBinding { Parameter = "AMP_VOLUME", TranslationReversed = true };

        //Act
        var translated = DecentSamplerTranslator.Translate(binding, Knob(0.25, 0.0, 1.0));

        //Assert
        translated.AsNumber.Should().BeApproximately(0.75, 1e-12);
    }

    [Theory]
    [InlineData(0.0, 0.0)]
    [InlineData(0.4, 0.0)]
    [InlineData(0.7, 0.5)]
    [InlineData(1.0, 1.0)]
    public void a_table_translation_interpolates_between_its_points(double input, double expected)
    {
        //Arrange
        var binding = Table("0,0;0.4,0;1,1");

        //Act
        var translated = DecentSamplerTranslator.Translate(binding, Knob(input, 0.0, 1.0));

        //Assert
        translated.AsNumber.Should().BeApproximately(expected, 1e-9);
    }

    [Theory]
    [InlineData(-5.0, 0.0)]
    [InlineData(4.0, 1.0)]
    public void a_table_translation_clamps_at_both_ends(double input, double expected)
    {
        //Arrange
        var binding = Table("0,0;1,1");

        //Act
        var translated = DecentSamplerTranslator.Translate(binding, Knob(input, 0.0, 1.0));

        //Assert
        translated.AsNumber.Should().Be(expected);
    }

    [Fact]
    public void a_table_translation_reads_the_raw_source_value_not_a_normalised_one()
    {
        //Arrange - a corpus preset writes exactly this table against a MIDI controller
        var binding = Table("0,200;30,355;60,850;90,4950;128,22000");

        //Act
        var translated = DecentSamplerTranslator.Translate(binding, DecentSamplerBindingInput.Controller(60));

        //Assert
        translated.AsNumber.Should().BeApproximately(850.0, 1e-9);
    }

    [Fact]
    public void an_empty_table_is_the_identity() =>
        DecentSamplerTranslator
            .Translate(
                new DecentSamplerBinding
                {
                    Parameter = "VALUE", Translation = DecentSamplerTranslation.Table,
                },
                Knob(0.42, 0.0, 1.0))
            .AsNumber.Should().BeApproximately(0.42, 1e-12);

    [Theory]
    [InlineData("true", true)]
    [InlineData("false", false)]
    [InlineData("1", true)]
    [InlineData("0", false)]
    public void a_fixed_value_coerces_to_a_switch(string text, bool expected) =>
        DecentSamplerTranslator
            .Translate(Fixed(text), Knob(0.5, 0.0, 1.0))
            .AsBoolean.Should().Be(expected);

    [Fact]
    public void a_fixed_value_coerces_to_a_number() =>
        DecentSamplerTranslator
            .Translate(Fixed("12"), Knob(0.5, 0.0, 1.0))
            .AsNumber.Should().Be(12.0);

    [Fact]
    public void a_fixed_value_keeps_an_enumeration_name_as_text() =>
        DecentSamplerTranslator
            .Translate(Fixed("forward_loop"), Knob(0.5, 0.0, 1.0))
            .AsText.Should().Be("forward_loop");

    [Fact]
    public void a_fixed_value_keeps_a_colour_as_text() =>
        DecentSamplerTranslator
            .Translate(Fixed("FF2C365E"), Knob(0.5, 0.0, 1.0))
            .AsText.Should().Be("FF2C365E");

    [Fact]
    public void a_fixed_value_ignores_the_source_entirely() =>
        DecentSamplerTranslator
            .Translate(Fixed("0.25"), Knob(1.0, 0.0, 1.0))
            .AsNumber.Should().Be(0.25);

    [Theory]
    [InlineData(0.0, 100.0)]
    [InlineData(0.5, 200.0)]
    [InlineData(1.0, 300.0)]
    public void the_neutral_output_is_the_translation_of_the_modulators_resting_raw_value(
        double resting, double expected) =>
        DecentSamplerTranslator
            .NeutralOutput(Linear(100.0, 300.0), DecentSamplerBindingInput.Normalised(0.0), resting)
            .Should().BeApproximately(expected, 1e-9);

    private static DecentSamplerBindingInput Knob(double value, double minimum, double maximum) =>
        new DecentSamplerBindingInput(value, minimum, maximum);

    private static DecentSamplerBinding Linear(double outputMin, double outputMax) =>
        new DecentSamplerBinding
        {
            Parameter = "VALUE",
            Translation = DecentSamplerTranslation.Linear,
            TranslationOutputMin = outputMin,
            TranslationOutputMax = outputMax,
        };

    private static DecentSamplerBinding Fixed(string value) =>
        new DecentSamplerBinding
        {
            Parameter = "VALUE",
            Translation = DecentSamplerTranslation.FixedValue,
            TranslationValue = value,
        };

    private static DecentSamplerBinding Table(string text)
    {
        var points = new List<DecentSamplerTranslationPoint>();

        foreach (var pair in text.Split(';'))
        {
            var parts = pair.Split(',');
            points.Add(new DecentSamplerTranslationPoint(
                double.Parse(parts[0], CultureInfo.InvariantCulture),
                double.Parse(parts[1], CultureInfo.InvariantCulture)));
        }

        return new DecentSamplerBinding
        {
            Parameter = "VALUE",
            Translation = DecentSamplerTranslation.Table,
            TranslationTable = points,
            TranslationTableText = text,
        };
    }
}
