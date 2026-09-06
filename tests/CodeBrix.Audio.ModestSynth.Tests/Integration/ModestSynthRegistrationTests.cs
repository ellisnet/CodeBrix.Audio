using System;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Effects;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Integration;

/// <summary>
/// What registration does and what happens without it, end to end through
/// <c>DecentSamplerSynthesizer</c>.
/// </summary>
/// <remarks>
/// Registration is process-wide and cannot be undone, so the "without it" cases build an ISOLATED
/// <c>DecentSamplerExtensionRegistry</c> instead: a hand-built registry starts empty, and filling it
/// with the CORE effect set alone reproduces exactly what a consumer who never referenced this package
/// hears.
/// </remarks>
public class ModestSynthRegistrationTests
{
    private const int Window = Spectrum.BlockLength;

    private const string SawGroup =
        "    <group attack=\"0\" decay=\"0\" sustain=\"1\" release=\"0.05\">\n" +
        "      <oscillator waveform=\"saw\" />\n    </group>";

    private const string PhaserChain =
        "  <effects>\n" +
        "    <effect type=\"phaser\" mix=\"1.0\" modDepth=\"0.0\" modRate=\"0.0\"" +
        " centerFrequency=\"800\" feedback=\"0.7\" />\n" +
        "  </effects>\n";

    public ModestSynthRegistrationTests() => ModestSynth.Register();

    [Fact]
    public void without_registration_an_oscillator_group_is_silent_and_problems_names_the_package()
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument instrument =
            fixtures.LoadPreset(PresetXml.OneOscillator("waveform=\"fm6op\""));
        DecentSamplerSynthesizer synthesizer =
            RenderProbe.Synthesizer(instrument, CoreOnlyRegistry());

        //Act
        synthesizer.NoteOn(0, 60, 127);
        var (left, right) = RenderProbe.RenderSeconds(synthesizer, 0.3);

        //Assert
        RenderProbe.Peak(left).Should().Be(0.0);
        RenderProbe.Peak(right).Should().Be(0.0);
        synthesizer.Problems.Should().Contain(DecentSamplerExtensions.MissingOscillatorMessage("fm6op"));
        synthesizer.Problems.Should().Contain(problem =>
            problem.Contains("CodeBrix.Audio.ModestSynth") && problem.Contains("ModestSynth.Register()"));
        synthesizer.UnsupportedFeatures.Should().Contain("oscillator waveform 'fm6op'");
    }

    [Fact]
    public void without_registration_an_add_on_effect_is_bypassed_and_problems_names_the_package()
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument bare =
            fixtures.LoadPreset(PresetXml.Wrap(null, SawGroup), "bare.dspreset");
        using DecentSamplerInstrument phased =
            fixtures.LoadPreset(PresetXml.Wrap(null, SawGroup, PhaserChain), "phased.dspreset");

        //Act
        float[] dry = RenderNote(RenderProbe.Synthesizer(bare, CoreOnlyRegistry()));
        DecentSamplerSynthesizer bypassed = RenderProbe.Synthesizer(phased, CoreOnlyRegistry());
        float[] wet = RenderNote(bypassed);

        //Assert
        wet.Should().Equal(dry);
        bypassed.Problems.Should().Contain(DecentSamplerExtensions.MissingEffectMessage("phaser"));
    }

    [Fact]
    public void a_registered_phaser_reshapes_what_the_preset_plays()
    {
        //Arrange
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument bare =
            fixtures.LoadPreset(PresetXml.Wrap(null, SawGroup), "bare.dspreset");
        using DecentSamplerInstrument phased =
            fixtures.LoadPreset(PresetXml.Wrap(null, SawGroup, PhaserChain), "phased.dspreset");

        DecentSamplerSynthesizer synthesizer = RenderProbe.Synthesizer(phased);

        //Act
        float[] dry = RenderNote(RenderProbe.Synthesizer(bare));
        float[] wet = RenderNote(synthesizer);

        double[] dryBins = Spectrum.WindowedMagnitudes(dry, Window);
        double[] wetBins = Spectrum.WindowedMagnitudes(wet, Window);

        double deepest = 0.0;
        double highest = 0.0;

        for (int harmonic = 1; harmonic <= 12; harmonic++)
        {
            int bin = 19 * harmonic;
            double change = RenderProbe.Decibels(wetBins[bin], dryBins[bin]);
            deepest = Math.Min(deepest, change);
            highest = Math.Max(highest, change);
        }

        //Assert
        synthesizer.Problems.Should().BeEmpty();
        deepest.Should().BeLessThan(-3.0);
        highest.Should().BeGreaterThan(3.0);

        // A phaser with feedback is not level-neutral: MEASURED, feedback="0.7" makes the cancelling
        // frequencies resonate (+1.5 dB at mix="0.5") and costs about 2 dB elsewhere, and at mix="1.0"
        // the resonances are all that is left. The check here is that the level stays in the same
        // neighbourhood, not that it is unchanged.
        RenderProbe.Decibels(
                RenderProbe.Rms(wet, Window, Window), RenderProbe.Rms(dry, Window, Window))
            .Should().BeInRange(-6.0, 6.0);
    }

    [Fact]
    public void formant_sounds_its_own_fixed_tone_and_is_still_reported_as_undocumented()
    {
        //Arrange
        // MEASURED (round 2, item 26): the reference accepts "formant" and makes ONE FIXED TONE with
        // it, ignoring every attribute. The add-on now renders that tone, so it no longer falls back
        // to a sine - but the core parser still reports the name, because the guide this engine's
        // feature list is built from does not document the waveform at all.
        using DecentSamplerFixtures fixtures = DecentSamplerFixtures.Create();
        using DecentSamplerInstrument formant =
            fixtures.LoadPreset(PresetXml.OneOscillator("waveform=\"formant\""), "formant.dspreset");
        using DecentSamplerInstrument sine =
            fixtures.LoadPreset(PresetXml.OneOscillator("waveform=\"sine\""), "sine.dspreset");

        //Act
        float[] played = RenderNote(RenderProbe.Synthesizer(formant));
        float[] plain = RenderNote(RenderProbe.Synthesizer(sine));

        //Assert
        played.Should().NotEqual(plain);
        RenderProbe.Rms(played, 0, played.Length)
            .Should().BeGreaterThan(RenderProbe.Rms(plain, 0, plain.Length));
        formant.Problems.Should().Contain(problem =>
            problem.Contains("formant") && problem.Contains("not a waveform this engine recognises"));
        formant.UnsupportedFeatures.Should().Contain("oscillator waveform 'formant'");
        ModestSynth.RegisteredOscillatorWaveforms.Should().Contain("formant");
    }

    [Fact]
    public void registering_into_a_registry_of_your_own_leaves_the_shared_one_alone()
    {
        //Arrange
        DecentSamplerExtensionRegistry registry = new DecentSamplerExtensionRegistry();

        //Act
        ModestSynth.Register(registry);

        //Assert
        registry.IsOscillatorRegistered("fm6op").Should().BeTrue();
        registry.IsEffectRegistered("phaser").Should().BeTrue();
        registry.IsEffectRegistered("reverb").Should().BeFalse();
        registry.RegisteredOscillators.Should().HaveCount(ModestSynth.RegisteredOscillatorWaveforms.Count);
    }

    [Fact]
    public void a_null_registry_is_rejected()
    {
        //Arrange
        //Act
        Action act = () => ModestSynth.Register(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void the_engines_feature_list_calls_the_add_ons_waveforms_implemented_once_it_is_registered()
    {
        //Arrange
        DecentSamplerExtensionRegistry registry = new DecentSamplerExtensionRegistry();

        //Act
        DecentSamplerFeatureStatus? waveformBefore = DecentSamplerSupportedFeatures.StatusOf(
            DecentSamplerFeatureCategory.Waveform, string.Empty, "wavetable", registry);
        DecentSamplerFeatureStatus? effectBefore = DecentSamplerSupportedFeatures.StatusOf(
            DecentSamplerFeatureCategory.EffectType, string.Empty, "bit_crusher", registry);

        ModestSynth.Register(registry);

        DecentSamplerFeatureStatus? waveformAfter = DecentSamplerSupportedFeatures.StatusOf(
            DecentSamplerFeatureCategory.Waveform, string.Empty, "wavetable", registry);
        DecentSamplerFeatureStatus? effectAfter = DecentSamplerSupportedFeatures.StatusOf(
            DecentSamplerFeatureCategory.EffectType, string.Empty, "bit_crusher", registry);

        //Assert
        waveformBefore.Should().Be(DecentSamplerFeatureStatus.Parsed);
        effectBefore.Should().Be(DecentSamplerFeatureStatus.Parsed);
        waveformAfter.Should().Be(DecentSamplerFeatureStatus.Implemented);
        effectAfter.Should().Be(DecentSamplerFeatureStatus.Implemented);
    }

    [Fact]
    public void the_process_wide_registration_makes_every_waveform_and_effect_implemented()
    {
        //Arrange
        //Act
        //Assert
        foreach (string waveform in DecentSamplerSupportedFeatures.WaveformNames)
        {
            DecentSamplerSupportedFeatures
                .StatusOf(DecentSamplerFeatureCategory.Waveform, string.Empty, waveform)
                .Should().Be(DecentSamplerFeatureStatus.Implemented);
        }

        foreach (string type in ModestSynth.RegisteredEffectTypes)
        {
            DecentSamplerSupportedFeatures
                .StatusOf(DecentSamplerFeatureCategory.EffectType, string.Empty, type)
                .Should().Be(DecentSamplerFeatureStatus.Implemented);
        }
    }

    // What a consumer who never referenced this package has: the core's own effects and nothing else.
    private static DecentSamplerExtensionRegistry CoreOnlyRegistry()
    {
        DecentSamplerExtensionRegistry registry = new DecentSamplerExtensionRegistry();
        DecentSamplerCoreEffects.RegisterInto(registry);
        return registry;
    }

    private static float[] RenderNote(DecentSamplerSynthesizer synthesizer)
    {
        synthesizer.NoteOn(0, 45, 127);
        var (left, _) = RenderProbe.RenderSeconds(synthesizer, 0.4);
        return left;
    }
}
