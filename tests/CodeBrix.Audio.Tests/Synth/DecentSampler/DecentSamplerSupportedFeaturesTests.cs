using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Effects;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler;

/// <summary>
/// Cross-checks the engine's feature list against the names taken from the Decent Sampler developer
/// guide, so the claim "the engine knows the format" is measured rather than asserted.
/// </summary>
/// <remarks>
/// <c>decent-sampler-guide-names.txt</c> is a checked-in list derived once from the guide: element and
/// attribute names come from every XML example in it, binding parameters from the parameter column of
/// its tables. Names are facts about a published file format, not prose. If a later version of the
/// guide adds a name, the list is regenerated and this test says what the engine is missing.
/// </remarks>
public class DecentSamplerSupportedFeaturesTests
{
    private const string GuideNamesFile = "decent-sampler-guide-names.txt";

    [Fact]
    public void the_engine_knows_every_name_the_developer_guide_uses()
    {
        //Arrange
        var missing = new List<string>();

        //Act
        foreach (var (kind, name) in GuideNames())
        {
            var known = kind switch
            {
                "element" => DecentSamplerSupportedFeatures.IsElement(name),
                "attribute" => IsAttribute(name),
                "bindingParameter" => DecentSamplerSupportedFeatures.IsBindingParameter(name),
                "bindingType" => DecentSamplerSupportedFeatures.IsBindingType(name),
                "bindingLevel" => DecentSamplerSupportedFeatures.IsBindingLevel(name),
                "effectType" => DecentSamplerSupportedFeatures.IsEffectType(name),
                "waveform" => DecentSamplerSupportedFeatures.IsWaveform(name),
                "translation" => DecentSamplerSupportedFeatures.IsTranslationMode(name),
                _ => throw new InvalidDataException($"unknown line kind '{kind}' in {GuideNamesFile}"),
            };

            if (!known)
            {
                missing.Add(kind + " " + name);
            }
        }

        //Assert
        missing.Should().BeEmpty();
    }

    [Fact]
    public void the_checked_in_guide_list_is_substantial() =>
        GuideNames().Count.Should().BeGreaterThan(500);

    [Fact]
    public void every_listed_feature_has_a_status()
    {
        //Arrange
        //Act
        var features = DecentSamplerSupportedFeatures.Features;

        //Assert
        // Asked with NO registry, so the answer is what the CORE implements - which is what the listed
        // status means. The registry-aware overloads are covered separately below.
        features.Should().NotBeEmpty();
        features.Should().AllSatisfy(feature =>
            DecentSamplerSupportedFeatures
                .StatusOf(feature.Category, feature.Owner, feature.Name, null)
                .Should().Be(feature.Status));
    }

    // The parameter and binding engine is live, so its four categories now carry Implemented entries.
    // Every other category waits for the phase that makes it audible.
    private static readonly DecentSamplerFeatureCategory[] BindingEngineCategories =
    [
        DecentSamplerFeatureCategory.BindingType,
        DecentSamplerFeatureCategory.BindingLevel,
        DecentSamplerFeatureCategory.BindingParameter,
        DecentSamplerFeatureCategory.TranslationMode,
    ];

    // The seven effect types that live in the CodeBrix.Audio.ModestSynth add-on. The core parses them,
    // reports them and bypasses them; only the add-on's Register() makes them sound, so they stay
    // Parsed in a core-only feature list.
    private static readonly string[] AddOnEffectTypes =
    [
        "phaser", "pitch_shift", "wave_folder", "wave_shaper", "stereo_simulator", "bit_crusher", "gate",
    ];

    // EVERY CORE ENGINE PHASE HAS LANDED. The list started out with nothing marked Implemented; the
    // binding engine flipped its four categories (asserted positively below), the sampler engine flipped
    // the group, sample and tag features it makes audible, the effects phase flipped the thirteen effect
    // types the core implements along with the buses, the modulator phase flipped the seven modulator
    // types, and the streaming phase flipped playbackMode. The oscillator waveforms and the seven creative
    // effect types are not the core's to flip at all - they are supplied by an ADD-ON, so their status is
    // a question about the running application, answered by the registry-aware StatusOf overload and
    // asserted in the next test. What stays Parsed by design (the <ui> visual attributes, the store's
    // productId, minVersion) is the residual table the integration phase documents and tests.
    [Fact]
    public void the_modulator_types_are_marked_implemented() =>
        DecentSamplerSupportedFeatures.Features
            .Where(feature => feature.Category == DecentSamplerFeatureCategory.ModulatorType)
            .Should().AllSatisfy(feature =>
                feature.Status.Should().Be(DecentSamplerFeatureStatus.Implemented));

    // The add-on's own waveforms and effect types: Parsed while nothing supplies them, Implemented as
    // soon as a registry has a factory for the name - which is what CodeBrix.Audio.ModestSynth's
    // Register() does. An INSTANCE registry is used so that this says nothing about, and does nothing
    // to, the process-wide one.
    [Fact]
    public void an_add_ons_waveforms_and_effect_types_are_parsed_until_a_registry_offers_them()
    {
        //Arrange
        var registry = new DecentSamplerExtensionRegistry();

        //Act
        var waveformBefore = DecentSamplerSupportedFeatures.StatusOf(
            DecentSamplerFeatureCategory.Waveform, string.Empty, "fm6op", registry);
        var effectBefore = DecentSamplerSupportedFeatures.StatusOf(
            DecentSamplerFeatureCategory.EffectType, string.Empty, "phaser", registry);

        registry.RegisterOscillator("fm6op", _ => null);
        registry.RegisterEffect("phaser", _ => null);

        var waveformAfter = DecentSamplerSupportedFeatures.StatusOf(
            DecentSamplerFeatureCategory.Waveform, string.Empty, "fm6op", registry);
        var effectAfter = DecentSamplerSupportedFeatures.StatusOf(
            DecentSamplerFeatureCategory.EffectType, string.Empty, "phaser", registry);

        //Assert
        waveformBefore.Should().Be(DecentSamplerFeatureStatus.Parsed);
        effectBefore.Should().Be(DecentSamplerFeatureStatus.Parsed);
        waveformAfter.Should().Be(DecentSamplerFeatureStatus.Implemented);
        effectAfter.Should().Be(DecentSamplerFeatureStatus.Implemented);
    }

    [Fact]
    public void registration_never_demotes_what_the_core_implements_itself()
    {
        //Arrange
        var empty = new DecentSamplerExtensionRegistry();

        //Act
        var reverb = DecentSamplerSupportedFeatures.StatusOf(
            DecentSamplerFeatureCategory.EffectType, string.Empty, "reverb", empty);
        var trigger = DecentSamplerSupportedFeatures.StatusOf(
            DecentSamplerFeatureCategory.EnumerationValue, "trigger", "legato", empty);

        //Assert
        reverb.Should().Be(DecentSamplerFeatureStatus.Implemented);
        trigger.Should().Be(DecentSamplerFeatureStatus.Implemented);
    }

    [Fact]
    public void the_static_feature_list_describes_the_core_alone()
    {
        //Arrange
        var registry = new DecentSamplerExtensionRegistry();
        registry.RegisterOscillator("saw", _ => null);

        //Act
        var listed = DecentSamplerSupportedFeatures.Features
            .First(feature =>
                feature.Category == DecentSamplerFeatureCategory.Waveform && feature.Name == "saw");

        //Assert
        listed.Status.Should().Be(DecentSamplerFeatureStatus.Parsed);
        DecentSamplerSupportedFeatures
            .StatusOf(DecentSamplerFeatureCategory.Waveform, string.Empty, "saw", registry)
            .Should().Be(DecentSamplerFeatureStatus.Implemented);
        DecentSamplerSupportedFeatures
            .StatusOf(DecentSamplerFeatureCategory.Waveform, string.Empty, "saw", null)
            .Should().Be(DecentSamplerFeatureStatus.Parsed);
    }

    [Fact]
    public void the_core_effect_types_are_implemented_and_the_add_on_ones_are_not()
    {
        //Arrange
        var effectTypes = DecentSamplerSupportedFeatures.Features
            .Where(feature => feature.Category == DecentSamplerFeatureCategory.EffectType)
            .ToArray();

        //Act
        var implemented = effectTypes
            .Where(feature => feature.Status == DecentSamplerFeatureStatus.Implemented)
            .Select(feature => feature.Name)
            .ToArray();

        //Assert
        implemented.Should().BeEquivalentTo(DecentSamplerCoreEffects.TypeNames);
        effectTypes.Length.Should().Be(implemented.Length + AddOnEffectTypes.Length);
    }

    [Fact]
    public void the_binding_engines_own_categories_are_marked_implemented() =>
        DecentSamplerSupportedFeatures.Features
            .Where(feature => BindingEngineCategories.Contains(feature.Category))
            .Should().AllSatisfy(feature =>
                feature.Status.Should().Be(DecentSamplerFeatureStatus.Implemented));

    [Fact]
    public void controller_indexed_attributes_fold_onto_one_feature()
    {
        //Arrange
        //Act
        //Assert
        DecentSamplerSupportedFeatures.CanonicalAttributeName("loCC64").Should().Be("loCCN");
        DecentSamplerSupportedFeatures.CanonicalAttributeName("hiCC7").Should().Be("hiCCN");
        DecentSamplerSupportedFeatures.CanonicalAttributeName("onLoCC11").Should().Be("onLoCCN");
        DecentSamplerSupportedFeatures.CanonicalAttributeName("onHiCC11").Should().Be("onHiCCN");
        DecentSamplerSupportedFeatures.CanonicalAttributeName("volume").Should().Be("volume");
    }

    [Fact]
    public void a_controller_attribute_gives_up_its_family_and_number()
    {
        //Arrange
        //Act
        var read = DecentSamplerSupportedFeatures
            .TryReadControllerAttribute("onHiCC64", out var family, out var controller);

        //Assert
        read.Should().BeTrue();
        family.Should().Be("onHiCC");
        controller.Should().Be(64);
    }

    [Fact]
    public void a_controller_number_above_the_midi_range_is_not_a_controller_attribute() =>
        DecentSamplerSupportedFeatures.TryReadControllerAttribute("loCC300", out _, out _)
            .Should().BeFalse();

    [Fact]
    public void every_controller_number_is_known_on_a_sample()
    {
        //Arrange
        //Act
        //Assert
        for (var controller = 0; controller <= 127; controller++)
        {
            DecentSamplerSupportedFeatures.IsAttribute("sample", "loCC" + controller).Should().BeTrue();
            DecentSamplerSupportedFeatures.IsAttribute("sample", "onHiCC" + controller).Should().BeTrue();
        }
    }

    [Fact]
    public void enumerated_values_are_listed_for_the_attributes_that_have_them()
    {
        //Arrange
        //Act
        //Assert
        DecentSamplerSupportedFeatures.IsEnumerationValue("seqMode", "round_robin").Should().BeTrue();
        DecentSamplerSupportedFeatures.IsEnumerationValue("seqMode", "sideways").Should().BeFalse();
        DecentSamplerSupportedFeatures.IsEnumerationValue("outputNTarget", "AUX_STEREO_OUTPUT_16")
            .Should().BeTrue();
        DecentSamplerSupportedFeatures.EnumerationValueNames("arpSyncDivision").Should().HaveCount(21);
        DecentSamplerSupportedFeatures.EnumerationValueNames("nothing").Should().BeEmpty();
    }

    [Fact]
    public void the_seven_modulator_elements_are_listed() =>
        DecentSamplerSupportedFeatures.ModulatorTypeNames.Should().HaveCount(7);

    [Fact]
    public void an_unknown_name_is_reported_as_unknown()
    {
        //Arrange
        //Act
        //Assert
        DecentSamplerSupportedFeatures.IsElement("formantOscillator").Should().BeFalse();
        DecentSamplerSupportedFeatures.IsAttribute("sample", "futureThing").Should().BeFalse();
        DecentSamplerSupportedFeatures.IsBindingParameter("FUTURE_PARAMETER").Should().BeFalse();
        DecentSamplerSupportedFeatures.IsEffectType("time_machine").Should().BeFalse();
        DecentSamplerSupportedFeatures.IsWaveform("formant").Should().BeFalse();
    }

    [Fact]
    public void a_null_attribute_name_is_rejected()
    {
        //Arrange
        //Act
        var act = () => DecentSamplerSupportedFeatures.CanonicalAttributeName(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    private static bool IsAttribute(string qualifiedName)
    {
        var at = qualifiedName.IndexOf('@');
        return at > 0 && DecentSamplerSupportedFeatures.IsAttribute(
            qualifiedName.Substring(0, at), qualifiedName.Substring(at + 1));
    }

    private static IReadOnlyList<(string Kind, string Name)> GuideNames()
    {
        var path = Path.Combine(
            AppContext.BaseDirectory, "Assets", "decent-sampler", GuideNamesFile);

        if (!File.Exists(path))
        {
            throw new FileNotFoundException(
                $"The guide name list was not copied to the test output. It lives beside the tests at " +
                $"tests/CodeBrix.Audio.Tests/Synth/DecentSampler/{GuideNamesFile}.", path);
        }

        return File.ReadAllLines(path)
            .Select(line => line.Trim())
            .Where(line => line.Length > 0 && !line.StartsWith("#", StringComparison.Ordinal))
            .Select(line =>
            {
                var space = line.IndexOf(' ');
                return (line.Substring(0, space), line.Substring(space + 1));
            })
            .ToArray();
    }
}
