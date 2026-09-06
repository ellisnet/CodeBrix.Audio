using System;
using CodeBrix.Audio.ModestSynth.Oscillators;
using SilverAssertions;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// Tests for <see cref="ModestWaveforms" />: the names round-trip, the synonym is accepted, and an
/// unknown name is answered rather than thrown at.
/// </summary>
public class ModestWaveformsTests
{
    [Theory]
    [InlineData("sine", ModestWaveform.Sine)]
    [InlineData("saw", ModestWaveform.Saw)]
    [InlineData("square", ModestWaveform.Square)]
    [InlineData("triangle", ModestWaveform.Triangle)]
    [InlineData("noise", ModestWaveform.Noise)]
    [InlineData("white_noise", ModestWaveform.Noise)]
    [InlineData("pluck1", ModestWaveform.Pluck1)]
    [InlineData("wavetable", ModestWaveform.Wavetable)]
    [InlineData("harmonic", ModestWaveform.Harmonic)]
    [InlineData("fm6op", ModestWaveform.Fm6Op)]
    [InlineData("formant", ModestWaveform.Formant)]
    public void TryParse_recognises_every_name_the_format_uses(string name, ModestWaveform expected)
    {
        //Act
        bool parsed = ModestWaveforms.TryParse(name, out ModestWaveform waveform);

        //Assert
        parsed.Should().BeTrue();
        waveform.Should().Be(expected);
    }

    [Theory]
    [InlineData("SQUARE")]
    [InlineData("  square  ")]
    [InlineData("Square")]
    public void TryParse_ignores_case_and_surrounding_whitespace(string name)
    {
        //Act
        bool parsed = ModestWaveforms.TryParse(name, out ModestWaveform waveform);

        //Assert
        parsed.Should().BeTrue();
        waveform.Should().Be(ModestWaveform.Square);
    }

    [Theory]
    [InlineData("zzz_not_a_waveform")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public void TryParse_answers_false_for_a_name_it_does_not_know(string name)
    {
        //Act
        bool parsed = ModestWaveforms.TryParse(name, out ModestWaveform waveform);

        //Assert
        parsed.Should().BeFalse();
        waveform.Should().Be(ModestWaveform.Sine);
    }

    [Theory]
    [InlineData(ModestWaveform.Sine, "sine")]
    [InlineData(ModestWaveform.Saw, "saw")]
    [InlineData(ModestWaveform.Square, "square")]
    [InlineData(ModestWaveform.Triangle, "triangle")]
    [InlineData(ModestWaveform.Noise, "noise")]
    [InlineData(ModestWaveform.Pluck1, "pluck1")]
    [InlineData(ModestWaveform.Wavetable, "wavetable")]
    [InlineData(ModestWaveform.Harmonic, "harmonic")]
    [InlineData(ModestWaveform.Fm6Op, "fm6op")]
    [InlineData(ModestWaveform.Formant, "formant")]
    public void ToName_gives_the_name_a_preset_spells(ModestWaveform waveform, string expected) =>
        ModestWaveforms.ToName(waveform).Should().Be(expected);

    [Fact]
    public void ToName_rejects_a_value_that_is_not_a_waveform()
    {
        //Act
        Action act = () => ModestWaveforms.ToName((ModestWaveform)99);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToName_round_trips_through_TryParse_for_every_waveform()
    {
        //Arrange
        Array values = Enum.GetValues(typeof(ModestWaveform));

        //Act, Assert
        foreach (ModestWaveform waveform in values)
        {
            ModestWaveforms.TryParse(ModestWaveforms.ToName(waveform), out ModestWaveform parsed)
                .Should().BeTrue();
            parsed.Should().Be(waveform);
        }
    }
}
