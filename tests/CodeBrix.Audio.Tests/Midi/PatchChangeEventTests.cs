using System;
using CodeBrix.Audio.Midi;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests.Midi;

/// <summary>
/// <see cref="PatchChangeEvent"/>, and in particular the names it reports. The class used to carry
/// its own abbreviated copy of the General MIDI sound set, typos included; it now reads the one
/// table in <see cref="GeneralMidi"/>, and these tests are what keeps the two from drifting apart
/// again.
/// </summary>
public class PatchChangeEventTests
{
    [Theory]
    [InlineData(0, "Acoustic Grand Piano")]
    [InlineData(7, "Clavi")]
    [InlineData(21, "Accordion")]
    [InlineData(23, "Tango Accordion")]
    [InlineData(24, "Acoustic Guitar (nylon)")]
    [InlineData(33, "Electric Bass (finger)")]
    [InlineData(46, "Orchestral Harp")]
    [InlineData(77, "Shakuhachi")]
    [InlineData(80, "Lead 1 (square)")]
    [InlineData(96, "FX 1 (rain)")]
    [InlineData(127, "Gunshot")]
    public void patch_names_are_the_official_general_midi_names(int patchNumber, string expected)
    {
        //Arrange
        //Act
        string name = PatchChangeEvent.GetPatchName(patchNumber);

        //Assert
        name.Should().Be(expected);
    }

    [Fact]
    public void get_patch_name_agrees_with_general_midi_display_name_for_every_program()
    {
        //Arrange
        //Act
        //Assert
        for (int patchNumber = 0; patchNumber < 128; patchNumber++)
        {
            PatchChangeEvent.GetPatchName(patchNumber)
                .Should().Be(GeneralMidi.DisplayName((GeneralMidiProgram)patchNumber));
        }
    }

    [Theory]
    [InlineData(-1)]
    [InlineData(128)]
    public void get_patch_name_rejects_a_number_outside_the_sound_set(int patchNumber)
    {
        //Arrange
        //Act
        Action act = () => PatchChangeEvent.GetPatchName(patchNumber);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void to_string_names_the_program_the_way_the_specification_does()
    {
        //Arrange
        var patchChange = new PatchChangeEvent(0, 1, 21);

        //Act
        string text = patchChange.ToString();

        //Assert
        text.Should().Contain("Accordion");
        text.Should().NotContain("Accoridan");
    }
}
