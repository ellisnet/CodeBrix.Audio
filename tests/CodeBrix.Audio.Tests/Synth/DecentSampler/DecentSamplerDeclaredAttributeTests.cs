using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using CodeBrix.Audio.Synth.DecentSampler;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler;

/// <summary>
/// Holds the parser and <see cref="DecentSamplerSupportedFeatures"/> to the same story: every attribute
/// the feature list declares for an element must actually be read when that element is parsed.
/// </summary>
/// <remarks>
/// Without this, an attribute could be listed as known and still be reported as unrecognised - or worse,
/// silently dropped - and nothing would notice. The test builds one preset carrying every declared
/// attribute on every element and asserts that nothing comes back unrecognised.
/// </remarks>
public class DecentSamplerDeclaredAttributeTests
{
    // The guide writes <note> for two different elements - a MIDI handler and a note inside a
    // sequence - so one owner name carries both attribute sets and neither element reads all of them.
    // Both are covered by their own tests instead.
    private static readonly string[] SharedOwnerElements = ["note"];

    // Only ever found in DSLibraryInfo.xml, which has its own tests.
    private static readonly string[] SidecarElements =
        ["DecentSamplerLibraryInfo", "presetMenu", "presetMenu/menu", "presetMenu/preset"];

    [Fact]
    public void every_declared_attribute_is_read_by_the_parser()
    {
        //Arrange
        var xml = BuildPresetWithEveryDeclaredAttribute();

        //Act
        var preset = DecentSamplerParser.ParseText(xml);
        var unrecognised = preset.AllUnknownAttributes()
            .Select(attribute => attribute.ElementName + "@" + attribute.Name)
            .Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal)
            .ToArray();

        //Assert
        preset.AllUnknownElements().Should().BeEmpty();
        unrecognised.Should().BeEmpty();
    }

    private static string BuildPresetWithEveryDeclaredAttribute()
    {
        var text = new StringBuilder();

        text.Append("<DecentSampler ").Append(AttributesFor("DecentSampler")).AppendLine(">");
        text.Append("  <ui ").Append(AttributesFor("ui")).AppendLine(">");
        text.Append("    <tab ").Append(AttributesFor("tab")).AppendLine(">");

        foreach (var element in new[]
                 {
                     "labeled-knob", "labeled_knob", "control", "button", "menu", "xyPad", "label",
                     "image", "multiFrameImage", "rectangle", "line", "oscilloscope",
                 })
        {
            text.Append("      <").Append(element).Append(' ').Append(AttributesFor(element)).AppendLine(">");

            switch (element)
            {
                case "labeled-knob":
                case "labeled_knob":
                case "control":
                case "button":
                    text.Append("        <state ").Append(AttributesFor("state")).AppendLine(">");
                    text.Append("          <binding ").Append(AttributesFor("binding")).AppendLine(" />");
                    text.AppendLine("        </state>");
                    break;

                case "menu":
                    text.Append("        <option ").Append(AttributesFor("option")).AppendLine(" />");
                    break;

                case "xyPad":
                    text.AppendLine("        <x><binding /></x>");
                    text.AppendLine("        <y><binding /></y>");
                    break;
            }

            text.Append("      </").Append(element).AppendLine(">");
        }

        text.AppendLine("    </tab>");
        text.Append("    <keyboard ").Append(AttributesFor("keyboard")).AppendLine(">");
        text.Append("      <color ").Append(AttributesFor("color")).AppendLine(" />");
        text.AppendLine("    </keyboard>");
        text.AppendLine("  </ui>");

        text.Append("  <groups ").Append(AttributesFor("groups")).AppendLine(">");
        text.Append("    <group ").Append(AttributesFor("group")).AppendLine(">");
        text.Append("      <sample ").Append(AttributesFor("sample")).AppendLine(" />");
        text.Append("      <oscillator ").Append(AttributesFor("oscillator")).AppendLine(" />");
        text.Append("      <effects ").Append(AttributesFor("effects")).AppendLine(">");
        text.Append("        <effect ").Append(AttributesFor("effect")).AppendLine(" />");
        text.AppendLine("      </effects>");
        text.AppendLine("    </group>");
        text.AppendLine("  </groups>");

        text.Append("  <effects ").Append(AttributesFor("effects")).AppendLine(">");
        text.Append("    <effect ").Append(AttributesFor("effect")).AppendLine(" />");
        text.AppendLine("  </effects>");

        text.Append("  <buses ").Append(AttributesFor("buses")).AppendLine(">");
        text.Append("    <bus ").Append(AttributesFor("bus")).AppendLine(">");
        text.AppendLine("      <effects><effect type=\"reverb\" /></effects>");
        text.AppendLine("    </bus>");
        text.AppendLine("  </buses>");

        text.Append("  <midi ").Append(AttributesFor("midi")).AppendLine(">");
        text.Append("    <cc ").Append(AttributesFor("cc")).AppendLine(">");
        text.Append("      <binding ").Append(AttributesFor("binding")).AppendLine(" />");
        text.AppendLine("    </cc>");
        text.AppendLine("    <note note=\"11\" eventType=\"note_on\" enabled=\"true\" swallowNotes=\"true\" />");
        text.Append("    <velocity ").Append(AttributesFor("velocity")).AppendLine(" />");
        text.AppendLine("  </midi>");

        text.Append("  <modulators ").Append(AttributesFor("modulators")).AppendLine(">");

        foreach (var element in DecentSamplerSupportedFeatures.ModulatorTypeNames
                     .OrderBy(name => name, StringComparer.Ordinal))
        {
            text.Append("    <").Append(element).Append(' ').Append(AttributesFor(element)).AppendLine(" />");
        }

        text.AppendLine("  </modulators>");

        text.Append("  <noteSequences ").Append(AttributesFor("noteSequences")).AppendLine(">");
        text.Append("    <sequence ").Append(AttributesFor("sequence")).AppendLine(">");
        text.AppendLine("      <note position=\"0\" velocity=\"1\" note=\"48\" length=\"4\" />");
        text.AppendLine("    </sequence>");
        text.AppendLine("  </noteSequences>");

        text.Append("  <arpeggiator ").Append(AttributesFor("arpeggiator")).AppendLine(" />");

        text.Append("  <tags ").Append(AttributesFor("tags")).AppendLine(">");
        text.Append("    <tag ").Append(AttributesFor("tag")).AppendLine(" />");
        text.AppendLine("  </tags>");

        text.AppendLine("</DecentSampler>");
        return text.ToString();
    }

    private static string AttributesFor(string elementName)
    {
        if (SharedOwnerElements.Contains(elementName) || SidecarElements.Contains(elementName))
        {
            return string.Empty;
        }

        var names = DecentSamplerSupportedFeatures.AttributeNames(elementName)
            .Select(Concrete)
            .OrderBy(name => name, StringComparer.Ordinal);

        return string.Join(" ", names.Select(name => name + "=\"1\""));
    }

    // The feature list folds controller-indexed attributes onto one name; a document needs a real
    // controller number.
    private static string Concrete(string attributeName) =>
        attributeName switch
        {
            "loCCN" => "loCC64",
            "hiCCN" => "hiCC64",
            "onLoCCN" => "onLoCC11",
            "onHiCCN" => "onHiCC11",
            _ => attributeName,
        };

    [Fact]
    public void the_shared_note_owner_carries_both_elements_attribute_sets()
    {
        //Arrange
        var names = new HashSet<string>(
            DecentSamplerSupportedFeatures.AttributeNames("note"), StringComparer.Ordinal);

        //Act
        //Assert
        names.Should().Contain("eventType");
        names.Should().Contain("swallowNotes");
        names.Should().Contain("position");
        names.Should().Contain("velocity");
    }
}
