using System;
using CodeBrix.Audio.ModestSynth.Fm;
using CodeBrix.Audio.ModestSynth.Harmonic;
using CodeBrix.Audio.ModestSynth.Oscillators;
using CodeBrix.Audio.ModestSynth.Wavetable;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// Tests for <see cref="ModestOscillatorFactory" />: it builds what it says it can build and says
/// so plainly about what it cannot.
/// </summary>
public class ModestOscillatorFactoryTests
{
    [Theory]
    [InlineData(ModestWaveform.Sine, typeof(SineOscillator))]
    [InlineData(ModestWaveform.Saw, typeof(SawOscillator))]
    [InlineData(ModestWaveform.Square, typeof(SquareOscillator))]
    [InlineData(ModestWaveform.Triangle, typeof(TriangleOscillator))]
    [InlineData(ModestWaveform.Noise, typeof(NoiseOscillator))]
    [InlineData(ModestWaveform.Pluck1, typeof(Pluck1Oscillator))]
    [InlineData(ModestWaveform.Fm6Op, typeof(Fm6OpOscillator))]
    [InlineData(ModestWaveform.Wavetable, typeof(WavetableOscillator))]
    [InlineData(ModestWaveform.Harmonic, typeof(HarmonicOscillator))]
    [InlineData(ModestWaveform.Formant, typeof(FormantOscillator))]
    public void Create_builds_the_oscillator_for_every_supported_waveform(ModestWaveform waveform, Type expected) =>
        ModestOscillatorFactory.Create(waveform).Should().BeOfType(expected);

    [Fact]
    public void Create_rejects_a_value_that_is_not_a_waveform()
    {
        //Act
        Action act = () => ModestOscillatorFactory.Create((ModestWaveform)99);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void SupportedWaveforms_lists_the_ones_this_release_generates() =>
        ModestOscillatorFactory.SupportedWaveforms.Should().Equal(
            ModestWaveform.Sine,
            ModestWaveform.Saw,
            ModestWaveform.Square,
            ModestWaveform.Triangle,
            ModestWaveform.Noise,
            ModestWaveform.Pluck1,
            ModestWaveform.Fm6Op,
            ModestWaveform.Wavetable,
            ModestWaveform.Harmonic,
            ModestWaveform.Formant);

    [Theory]
    [InlineData("pluck1", true)]
    [InlineData("white_noise", true)]
    [InlineData("fm6op", true)]
    [InlineData("formant", true)]
    [InlineData("zzz_not_a_waveform", false)]
    public void IsSupported_answers_for_a_name_without_throwing(string name, bool expected) =>
        ModestOscillatorFactory.IsSupported(name).Should().Be(expected);

    [Fact]
    public void TryCreate_builds_the_oscillator_a_synonym_names()
    {
        //Act
        bool created = ModestOscillatorFactory.TryCreate("white_noise", out IModestOscillator oscillator);

        //Assert
        created.Should().BeTrue();
        oscillator.Should().BeOfType(typeof(NoiseOscillator));
    }

    [Theory]
    [InlineData("zzz_not_a_waveform")]
    public void TryCreate_answers_false_rather_than_throwing(string name)
    {
        //Act
        bool created = ModestOscillatorFactory.TryCreate(name, out IModestOscillator oscillator);

        //Assert
        created.Should().BeFalse();
        oscillator.Should().BeNull();
    }
}
