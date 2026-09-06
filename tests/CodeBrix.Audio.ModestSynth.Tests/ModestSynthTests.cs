using System.Collections.Generic;
using CodeBrix.Audio.ModestSynth.Internal;
using CodeBrix.Audio.ModestSynth.Oscillators;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// Tests for <see cref="ModestSynth" /> and the registration list behind it.
/// </summary>
/// <remarks>
/// Registration is process-wide and one-way by design, so these tests never assert that it has NOT
/// happened - another test class may have registered first. What they assert is that registering is
/// idempotent, that it leaves <see cref="ModestSynth.IsRegistered" /> true, and that the offer it
/// makes is the list of waveforms this package can actually generate.
/// </remarks>
public class ModestSynthTests
{
    [Fact]
    public void Register_leaves_the_package_registered()
    {
        //Act
        ModestSynth.Register();

        //Assert
        ModestSynth.IsRegistered.Should().BeTrue();
    }

    [Fact]
    public void Register_called_twice_changes_nothing_the_second_time()
    {
        //Arrange
        ModestSynth.Register();
        IReadOnlyList<string> afterFirst = ModestSynth.RegisteredOscillatorWaveforms;

        //Act
        ModestSynth.Register();

        //Assert
        ModestSynth.IsRegistered.Should().BeTrue();
        ModestSynth.RegisteredOscillatorWaveforms.Should().BeSameAs(afterFirst);
    }

    [Fact]
    public void Register_offers_every_waveform_this_release_generates()
    {
        //Act
        ModestSynth.Register();

        //Assert
        // "formant" is offered too, and served with a sine: the reference player accepts the name and
        // nothing documents its parameters, so a 1.30 preset still sounds rather than going silent.
        ModestSynth.RegisteredOscillatorWaveforms.Should().Equal(
            "sine", "saw", "square", "triangle", "noise", "white_noise", "pluck1", "fm6op",
            "wavetable", "harmonic", "formant");
    }

    [Fact]
    public void The_registration_list_builds_a_working_oscillator_for_every_entry()
    {
        //Act, Assert
        foreach (ModestOscillatorRegistration entry in ModestSynthRegistry.OscillatorRegistrations)
        {
            IModestOscillator oscillator = entry.Factory();
            oscillator.Should().NotBeNull();
            ModestWaveforms.TryParse(entry.WaveformName, out ModestWaveform parsed).Should().BeTrue();
            parsed.Should().Be(entry.Waveform);
        }
    }

    [Fact]
    public void The_formant_entry_is_served_by_its_own_fixed_tone()
    {
        //Arrange
        ModestOscillatorRegistration formant = null;

        //Act
        foreach (ModestOscillatorRegistration entry in ModestSynthRegistry.OscillatorRegistrations)
        {
            if (entry.Waveform == ModestWaveform.Formant) { formant = entry; }
        }

        //Assert
        formant.Should().NotBeNull();
        formant.WaveformName.Should().Be("formant");
        formant.Factory().Waveform.Should().Be(ModestWaveforms.Formant);
    }

    [Fact]
    public void The_registration_list_covers_every_waveform_the_factory_supports()
    {
        //Arrange
        HashSet<ModestWaveform> registered = new HashSet<ModestWaveform>();

        //Act
        foreach (ModestOscillatorRegistration entry in ModestSynthRegistry.OscillatorRegistrations)
        {
            registered.Add(entry.Waveform);
        }

        //Assert
        registered.Should().HaveCount(ModestOscillatorFactory.SupportedWaveforms.Count);
        foreach (ModestWaveform waveform in ModestOscillatorFactory.SupportedWaveforms)
        {
            registered.Contains(waveform).Should().BeTrue();
        }

        registered.Contains(ModestWaveform.Formant).Should().BeTrue();
    }
}
