using System.Linq;
using CodeBrix.Audio.Synth.DecentSampler;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler;

/// <summary>
/// The residual table is the published answer to "what is missing", and this is what keeps it honest:
/// every feature the engine has not implemented is accounted for by exactly one entry, and no entry
/// claims a feature the engine does implement.
/// </summary>
/// <remarks>
/// Adding a name to <see cref="DecentSamplerSupportedFeatures"/> without either implementing it or
/// giving it a home in <see cref="DecentSamplerResidualTable"/> fails here, which is the whole point:
/// the claim in the documentation cannot drift from the code.
/// </remarks>
public class DecentSamplerResidualTableTests
{
    [Fact]
    public void every_feature_the_engine_has_not_implemented_is_named_in_the_residual_table()
    {
        //Arrange
        var parsed = DecentSamplerSupportedFeatures.Features
            .Where(feature => feature.Status == DecentSamplerFeatureStatus.Parsed)
            .ToList();

        //Act
        var unexplained = parsed
            .Where(feature => !DecentSamplerResidualTable.TryExplain(feature, out _))
            .Select(feature => feature.QualifiedName)
            .ToList();

        //Assert
        unexplained.Should().BeEmpty();
        parsed.Should().NotBeEmpty();
    }

    [Fact]
    public void the_residual_table_claims_nothing_the_engine_implements()
    {
        //Arrange
        var implemented = DecentSamplerSupportedFeatures.Features
            .Where(feature => feature.Status == DecentSamplerFeatureStatus.Implemented)
            .ToList();

        //Act
        var claimed = implemented
            .Where(feature => DecentSamplerResidualTable.TryExplain(feature, out _))
            .Select(feature => feature.QualifiedName)
            .ToList();

        //Assert
        claimed.Should().BeEmpty();
        implemented.Should().NotBeEmpty();
    }

    [Fact]
    public void every_parsed_feature_belongs_to_exactly_one_entry()
    {
        //Arrange
        var parsed = DecentSamplerSupportedFeatures.Features
            .Count(feature => feature.Status == DecentSamplerFeatureStatus.Parsed);

        //Act
        var covered = DecentSamplerResidualTable.Entries.Sum(entry => entry.Features.Count);
        var distinct = DecentSamplerResidualTable.Entries
            .SelectMany(entry => entry.Features)
            .Distinct()
            .Count();

        //Assert
        covered.Should().Be(parsed);
        distinct.Should().Be(parsed);
    }

    [Fact]
    public void every_entry_accounts_for_something_and_says_why()
    {
        //Assert
        DecentSamplerResidualTable.Entries.Should().NotBeEmpty();
        DecentSamplerResidualTable.Entries.Should().OnlyContain(entry => entry.Features.Count > 0);
        DecentSamplerResidualTable.Entries.Should().OnlyContain(entry => entry.Name.Length > 0);
        DecentSamplerResidualTable.Entries.Should().OnlyContain(entry => entry.Reason.Length > 40);
    }

    [Fact]
    public void exactly_one_entry_is_answered_by_the_add_on_package()
    {
        //Act
        var addOn = DecentSamplerResidualTable.Entries.Where(entry => entry.IsSuppliedByAddOn).ToList();

        //Assert - every waveform and every add-on effect type sits in it.
        addOn.Should().HaveCount(1);
        addOn[0].Features.Select(feature => feature.Name).Should()
            .Contain(["fm6op", "wavetable", "pluck1", "phaser", "bit_crusher"]);

        // The 1.30 formant oscillator is not in the feature list, because the 1.29 guide it is built
        // from does not document it. The entry's own text is where a consumer reads about it.
        addOn[0].Reason.Should().Contain("formant");
    }

    [Fact]
    public void the_interface_entry_covers_the_ui_subtree_and_nothing_that_makes_a_sound()
    {
        //Act
        var entry = DecentSamplerResidualTable.Entries
            .Single(candidate => candidate.Name == "user interface appearance");

        //Assert
        entry.Features.Select(feature => feature.Name).Should().Contain(["bgImage", "bgColor", "style"]);
        entry.Features.Should().NotContain(feature => feature.Owner == "group");
        entry.Features.Should().NotContain(feature => feature.Owner == "sample");
    }

    [Fact]
    public void the_store_entry_names_the_product_identifier()
    {
        //Act
        var entry = DecentSamplerResidualTable.Entries
            .Single(candidate => candidate.Name == "library catalogue and store distribution");

        //Assert
        entry.Features.Should().Contain(feature =>
            feature.Owner == "DecentSamplerLibraryInfo" && feature.Name == "productId");
        entry.Reason.Should().Contain("productId");
    }

    [Fact]
    public void the_version_entry_names_the_minimum_version()
    {
        //Act
        var entry = DecentSamplerResidualTable.Entries
            .Single(candidate => candidate.Name == "format version negotiation");

        //Assert
        entry.Features.Should().Contain(feature =>
            feature.Owner == "DecentSampler" && feature.Name == "minVersion");
    }

    [Fact]
    public void the_table_describes_itself_for_a_host_to_show()
    {
        //Act
        var text = DecentSamplerResidualTable.Describe();

        //Assert
        text.Should().Contain("user interface appearance");
        text.Should().Contain("ModestSynth.Register()");
        text.Should().Contain("productId");
    }

    [Fact]
    public void explaining_a_null_feature_is_rejected()
    {
        //Act
        var act = () => DecentSamplerResidualTable.Explain(null);

        //Assert
        act.Should().Throw<System.ArgumentNullException>();
    }
}
