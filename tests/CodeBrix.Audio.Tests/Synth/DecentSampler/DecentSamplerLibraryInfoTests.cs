using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Containers;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler;

/// <summary>
/// Covers the <c>DSLibraryInfo.xml</c> sidecar: its four attributes, the preset browsing menu, and the
/// tolerance rules the guide describes.
/// </summary>
public class DecentSamplerLibraryInfoTests
{
    [Fact]
    public void every_attribute_is_read()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        var path = fixture.WriteLibraryInfo("""
            <?xml version="1.0" encoding="UTF-8"?>
            <DecentSamplerLibraryInfo name="My Sample Library" productId="com.example.mylibrary"
                                      version="1.2.0" coverArt="CoverArt.png" />
            """);

        //Act
        var info = DecentSamplerLibraryInfo.Load(path);

        //Assert
        info.Name.Should().Be("My Sample Library");
        info.ProductId.Should().Be("com.example.mylibrary");
        info.Version.Should().Be("1.2.0");
        info.CoverArt.Should().Be("CoverArt.png");
        info.IsStoreTied.Should().BeTrue();
    }

    [Fact]
    public void the_preset_menu_keeps_its_nesting_and_order()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        var path = fixture.WriteLibraryInfo("""
            <DecentSamplerLibraryInfo name="My Sample Library">
              <presetMenu>
                <menu name="Pads">
                  <preset file="Presets/Pads/WarmPad.dspreset" />
                  <preset file="Presets/Pads/GlassPad.dspreset" />
                  <menu name="Analog">
                    <preset file="Presets/Pads/Analog/Drift.dspreset" />
                  </menu>
                </menu>
                <menu name="Leads">
                  <preset file="Presets/Leads/Saw.dspreset" />
                </menu>
              </presetMenu>
            </DecentSamplerLibraryInfo>
            """);

        //Act
        var info = DecentSamplerLibraryInfo.Load(path);

        //Assert
        info.Problems.Should().BeEmpty();
        info.PresetMenu.Should().HaveCount(2);
        info.PresetMenu[0].IsMenu.Should().BeTrue();
        info.PresetMenu[0].Name.Should().Be("Pads");
        info.PresetMenu[0].Children.Should().HaveCount(3);
        info.PresetMenu[0].Children[0].File.Should().Be("Presets/Pads/WarmPad.dspreset");
        info.PresetMenu[0].Children[2].Name.Should().Be("Analog");
        info.PresetMenu[0].Children[2].Children.Should().ContainSingle();
        info.PresetMenu[1].Name.Should().Be("Leads");
    }

    [Fact]
    public void a_preset_entry_with_no_file_is_skipped_and_reported()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        var path = fixture.WriteLibraryInfo(
            "<DecentSamplerLibraryInfo><presetMenu><preset /></presetMenu></DecentSamplerLibraryInfo>");

        //Act
        var info = DecentSamplerLibraryInfo.Load(path);

        //Assert
        info.PresetMenu.Should().BeEmpty();
        info.Problems.Should().ContainSingle();
    }

    [Fact]
    public void an_unknown_attribute_is_reported()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        var path = fixture.WriteLibraryInfo("<DecentSamplerLibraryInfo futureThing=\"1\" />");

        //Act
        var info = DecentSamplerLibraryInfo.Load(path);

        //Assert
        info.Problems.Should().ContainSingle();
        info.Problems[0].Should().Contain("futureThing");
    }

    [Fact]
    public void malformed_xml_throws()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        var path = fixture.WriteLibraryInfo("<DecentSamplerLibraryInfo>");

        //Act
        var act = () => DecentSamplerLibraryInfo.Load(path);

        //Assert
        act.Should().Throw<DecentSamplerParseException>();
    }
}
