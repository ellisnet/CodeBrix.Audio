using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Midi;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests.Midi;

/// <summary>
/// Holds the General MIDI Level 1 name tables to the published sound set. The expected names below
/// are the fixture: they are typed out here so that a change to the library's own table has to be
/// made twice, deliberately, before it can pass.
/// </summary>
public class GeneralMidiTests
{
    private static readonly string[] ExpectedProgramNames =
    [
        "Acoustic Grand Piano",
        "Bright Acoustic Piano",
        "Electric Grand Piano",
        "Honky-tonk Piano",
        "Electric Piano 1",
        "Electric Piano 2",
        "Harpsichord",
        "Clavi",
        "Celesta",
        "Glockenspiel",
        "Music Box",
        "Vibraphone",
        "Marimba",
        "Xylophone",
        "Tubular Bells",
        "Dulcimer",
        "Drawbar Organ",
        "Percussive Organ",
        "Rock Organ",
        "Church Organ",
        "Reed Organ",
        "Accordion",
        "Harmonica",
        "Tango Accordion",
        "Acoustic Guitar (nylon)",
        "Acoustic Guitar (steel)",
        "Electric Guitar (jazz)",
        "Electric Guitar (clean)",
        "Electric Guitar (muted)",
        "Overdriven Guitar",
        "Distortion Guitar",
        "Guitar harmonics",
        "Acoustic Bass",
        "Electric Bass (finger)",
        "Electric Bass (pick)",
        "Fretless Bass",
        "Slap Bass 1",
        "Slap Bass 2",
        "Synth Bass 1",
        "Synth Bass 2",
        "Violin",
        "Viola",
        "Cello",
        "Contrabass",
        "Tremolo Strings",
        "Pizzicato Strings",
        "Orchestral Harp",
        "Timpani",
        "String Ensemble 1",
        "String Ensemble 2",
        "SynthStrings 1",
        "SynthStrings 2",
        "Choir Aahs",
        "Voice Oohs",
        "Synth Voice",
        "Orchestra Hit",
        "Trumpet",
        "Trombone",
        "Tuba",
        "Muted Trumpet",
        "French Horn",
        "Brass Section",
        "SynthBrass 1",
        "SynthBrass 2",
        "Soprano Sax",
        "Alto Sax",
        "Tenor Sax",
        "Baritone Sax",
        "Oboe",
        "English Horn",
        "Bassoon",
        "Clarinet",
        "Piccolo",
        "Flute",
        "Recorder",
        "Pan Flute",
        "Blown Bottle",
        "Shakuhachi",
        "Whistle",
        "Ocarina",
        "Lead 1 (square)",
        "Lead 2 (sawtooth)",
        "Lead 3 (calliope)",
        "Lead 4 (chiff)",
        "Lead 5 (charang)",
        "Lead 6 (voice)",
        "Lead 7 (fifths)",
        "Lead 8 (bass + lead)",
        "Pad 1 (new age)",
        "Pad 2 (warm)",
        "Pad 3 (polysynth)",
        "Pad 4 (choir)",
        "Pad 5 (bowed)",
        "Pad 6 (metallic)",
        "Pad 7 (halo)",
        "Pad 8 (sweep)",
        "FX 1 (rain)",
        "FX 2 (soundtrack)",
        "FX 3 (crystal)",
        "FX 4 (atmosphere)",
        "FX 5 (brightness)",
        "FX 6 (goblins)",
        "FX 7 (echoes)",
        "FX 8 (sci-fi)",
        "Sitar",
        "Banjo",
        "Shamisen",
        "Koto",
        "Kalimba",
        "Bag pipe",
        "Fiddle",
        "Shanai",
        "Tinkle Bell",
        "Agogo",
        "Steel Drums",
        "Woodblock",
        "Taiko Drum",
        "Melodic Tom",
        "Synth Drum",
        "Reverse Cymbal",
        "Guitar Fret Noise",
        "Breath Noise",
        "Seashore",
        "Bird Tweet",
        "Telephone Ring",
        "Helicopter",
        "Applause",
        "Gunshot",
    ];

    private static readonly string[] ExpectedFamilyNames =
    [
        "Piano",
        "Chromatic Percussion",
        "Organ",
        "Guitar",
        "Bass",
        "Strings",
        "Ensemble",
        "Brass",
        "Reed",
        "Pipe",
        "Synth Lead",
        "Synth Pad",
        "Synth Effects",
        "Ethnic",
        "Percussive",
        "Sound Effects",
    ];

    private static readonly string[] ExpectedPercussionNames =
    [
        "Acoustic Bass Drum",
        "Bass Drum 1",
        "Side Stick",
        "Acoustic Snare",
        "Hand Clap",
        "Electric Snare",
        "Low Floor Tom",
        "Closed Hi Hat",
        "High Floor Tom",
        "Pedal Hi-Hat",
        "Low Tom",
        "Open Hi-Hat",
        "Low-Mid Tom",
        "Hi-Mid Tom",
        "Crash Cymbal 1",
        "High Tom",
        "Ride Cymbal 1",
        "Chinese Cymbal",
        "Ride Bell",
        "Tambourine",
        "Splash Cymbal",
        "Cowbell",
        "Crash Cymbal 2",
        "Vibraslap",
        "Ride Cymbal 2",
        "Hi Bongo",
        "Low Bongo",
        "Mute Hi Conga",
        "Open Hi Conga",
        "Low Conga",
        "High Timbale",
        "Low Timbale",
        "High Agogo",
        "Low Agogo",
        "Cabasa",
        "Maracas",
        "Short Whistle",
        "Long Whistle",
        "Short Guiro",
        "Long Guiro",
        "Claves",
        "Hi Wood Block",
        "Low Wood Block",
        "Mute Cuica",
        "Open Cuica",
        "Mute Triangle",
        "Open Triangle",
    ];

    [Fact]
    public void the_program_values_are_exactly_zero_to_one_hundred_and_twenty_seven()
    {
        //Arrange
        var expected = Enumerable.Range(0, 128).ToArray();

        //Act
        var values = Enum.GetValues<GeneralMidiProgram>().Select(p => (int)p).ToArray();

        //Assert
        values.Should().HaveCount(128);
        values.Distinct().Should().HaveCount(128);
        values.OrderBy(v => v).Should().Equal(expected);
    }

    [Fact]
    public void every_program_display_name_matches_the_published_sound_set()
    {
        //Arrange
        var actual = new List<string>();

        //Act
        for (int value = 0; value < 128; value++)
        {
            actual.Add(GeneralMidi.DisplayName((GeneralMidiProgram)value));
        }

        //Assert
        actual.Should().Equal(ExpectedProgramNames);
    }

    [Fact]
    public void the_first_and_last_programs_are_the_ones_the_specification_names()
    {
        //Arrange
        //Act
        string first = GeneralMidi.DisplayName(GeneralMidiProgram.AcousticGrandPiano);
        string last = GeneralMidi.DisplayName(GeneralMidiProgram.Gunshot);

        //Assert
        ((int)GeneralMidiProgram.AcousticGrandPiano).Should().Be(0);
        ((int)GeneralMidiProgram.Gunshot).Should().Be(127);
        first.Should().Be("Acoustic Grand Piano");
        last.Should().Be("Gunshot");
    }

    [Fact]
    public void each_family_holds_exactly_its_eight_programs()
    {
        //Arrange
        var families = Enum.GetValues<GeneralMidiProgramFamily>();

        //Act
        var held = families.ToDictionary(
            f => f,
            f => Enumerable.Range(0, 128)
                .Where(v => GeneralMidi.FamilyOf((GeneralMidiProgram)v) == f)
                .ToArray());

        //Assert
        families.Should().HaveCount(16);
        foreach (var family in families)
        {
            int start = (int)family * 8;
            held[family].Should().Equal(Enumerable.Range(start, 8).ToArray());
        }
    }

    [Fact]
    public void every_family_display_name_matches_the_published_sound_set()
    {
        //Arrange
        var actual = new List<string>();

        //Act
        foreach (var family in Enum.GetValues<GeneralMidiProgramFamily>().OrderBy(f => (int)f))
        {
            actual.Add(GeneralMidi.DisplayName(family));
        }

        //Assert
        actual.Should().Equal(ExpectedFamilyNames);
    }

    [Fact]
    public void the_percussion_values_are_exactly_thirty_five_to_eighty_one()
    {
        //Arrange
        var expected = Enumerable.Range(35, 81 - 35 + 1).ToArray();

        //Act
        var values = Enum.GetValues<GeneralMidiPercussion>().Select(p => (int)p).ToArray();

        //Assert
        values.Should().HaveCount(47);
        values.Distinct().Should().HaveCount(47);
        values.OrderBy(v => v).Should().Equal(expected);
    }

    [Fact]
    public void every_percussion_display_name_matches_the_published_key_map()
    {
        //Arrange
        var actual = new List<string>();

        //Act
        for (int note = 35; note <= 81; note++)
        {
            actual.Add(GeneralMidi.DisplayName((GeneralMidiPercussion)note));
        }

        //Assert
        actual.Should().Equal(ExpectedPercussionNames);
    }

    [Fact]
    public void the_percussion_channel_is_ten()
    {
        //Arrange
        //Act
        int channel = GeneralMidi.PercussionChannel;

        //Assert
        channel.Should().Be(10);
    }

    [Fact]
    public void a_program_round_trips_through_a_patch_change_event()
    {
        //Arrange
        var program = GeneralMidiProgram.Lead1Square;

        //Act
        var patch = new PatchChangeEvent(0, 1, (int)program);
        var read = (GeneralMidiProgram)patch.Patch;

        //Assert
        patch.Patch.Should().Be(80);
        read.Should().Be(GeneralMidiProgram.Lead1Square);
        GeneralMidi.DisplayName(read).Should().Be("Lead 1 (square)");
    }

    [Fact]
    public void every_program_round_trips_through_a_patch_change_event()
    {
        //Arrange
        var all = Enum.GetValues<GeneralMidiProgram>();

        //Act
        var read = all.Select(p => (GeneralMidiProgram)new PatchChangeEvent(0, 1, (int)p).Patch).ToArray();

        //Assert
        read.Should().Equal(all);
    }

    [Fact]
    public void an_undefined_program_is_rejected()
    {
        //Arrange
        var program = (GeneralMidiProgram)128;

        //Act
        Action act = () => GeneralMidi.DisplayName(program);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void an_undefined_percussion_note_is_rejected()
    {
        //Arrange
        var percussion = (GeneralMidiPercussion)34;

        //Act
        Action act = () => GeneralMidi.DisplayName(percussion);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void an_undefined_family_is_rejected()
    {
        //Arrange
        var family = (GeneralMidiProgramFamily)16;

        //Act
        Action act = () => GeneralMidi.DisplayName(family);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void an_undefined_program_has_no_family()
    {
        //Arrange
        var program = (GeneralMidiProgram)(-1);

        //Act
        Action act = () => GeneralMidi.FamilyOf(program);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
