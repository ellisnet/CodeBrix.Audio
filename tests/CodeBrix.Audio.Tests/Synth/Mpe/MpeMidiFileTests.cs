using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth.Mpe;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.Mpe;

/// <summary>
/// The acceptance the plan asks for: a MIDI file carrying an expressive performance plays with each
/// note bent, brightened and swelled by its own channel alone, plus its zone master, and the pitches
/// come out equal to the bend arithmetic within one cent.
/// </summary>
/// <remarks>
/// Two fixtures, both built in code. The first is shaped like a sequencer export: no configuration
/// message at all, notes spread over channels 2 to 5, each with its own bend. The second says
/// everything out loud - a configuration message for each zone, a bend range per zone, and master
/// bends that combine with the member bends.
/// </remarks>
public class MpeMidiFileTests
{
    // Bends chosen so the 14-bit value is exact: a bend of k/8192 over a 48-semitone range is
    // 3k/512 semitones, which is a whole number when k is a multiple of 512.
    private const double ThreeSemitonesOf48 = 512.0 / 8192.0;
    private const double SixSemitonesOf48 = 1024.0 / 8192.0;

    private const double NoteSeconds = 0.4;
    private const double NoteSpacing = 0.6;

    [Fact]
    public void an_export_with_no_configuration_message_gives_each_note_its_own_bend()
    {
        //Arrange
        using var fixtures = MpeSequences.Create();
        var sequence = MpeSequences.Sequence(AbletonStyleExport());

        //Act
        var audio = fixtures.Render(
            sequence, 3.0, synthesizer => synthesizer.MpeMode = MpeMode.Auto);

        //Assert
        // Channels 2 to 5 bend by +3, -6, +6 and 0 semitones over the 48-semitone member default.
        double[] expected = [3.0, -6.0, 6.0, 0.0];

        for (var note = 0; note < expected.Length; note++)
        {
            Cents(audio, note, expected[note]).Should().BeLessThan(1.0);
        }
    }

    [Fact]
    public void the_same_export_played_without_mpe_bends_only_two_semitones()
    {
        //Arrange
        using var fixtures = MpeSequences.Create();
        var sequence = MpeSequences.Sequence(AbletonStyleExport());

        //Act
        var audio = fixtures.Render(
            sequence, 3.0, synthesizer => synthesizer.MpeMode = MpeMode.Off);

        //Assert
        // The same wheel positions over MIDI's own two-semitone range: an eighth of the MPE reading.
        double[] expected = [0.125, -0.25, 0.25, 0.0];

        for (var note = 0; note < expected.Length; note++)
        {
            Cents(audio, note, expected[note]).Should().BeLessThan(1.0);
        }
    }

    [Fact]
    public void an_explicit_lower_zone_reads_the_export_the_same_way_as_automatic_detection()
    {
        //Arrange
        using var fixtures = MpeSequences.Create();
        var sequence = MpeSequences.Sequence(AbletonStyleExport());

        //Act
        var audio = fixtures.Render(
            sequence, 3.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);

        //Assert
        Cents(audio, 0, 3.0).Should().BeLessThan(1.0);
        Cents(audio, 2, 6.0).Should().BeLessThan(1.0);
    }

    [Fact]
    public void the_member_bend_range_override_changes_how_far_a_bend_reaches()
    {
        //Arrange
        using var fixtures = MpeSequences.Create();
        var sequence = MpeSequences.Sequence(AbletonStyleExport());

        //Act
        var audio = fixtures.Render(sequence, 3.0, synthesizer =>
        {
            synthesizer.MpeMemberBendRange = 24.0;
            synthesizer.MpeMode = MpeMode.LowerZone;
        });

        //Assert
        // Half the range, so half the bend.
        Cents(audio, 0, 1.5).Should().BeLessThan(1.0);
        Cents(audio, 1, -3.0).Should().BeLessThan(1.0);
    }

    [Fact]
    public void a_fully_specified_file_combines_each_zones_master_bend_with_its_members()
    {
        //Arrange
        using var fixtures = MpeSequences.Create();
        var sequence = MpeSequences.Sequence(FullySpecified());

        //Act
        var audio = fixtures.Render(
            sequence, 3.0, synthesizer => synthesizer.MpeMode = MpeMode.Auto);

        //Assert
        // Note 0, channel 3: a lower-zone member with a 12-semitone range bent a quarter of the way
        // (+3), plus its master's two-semitone range bent halfway (+1).
        Cents(audio, 0, 4.0).Should().BeLessThan(1.0);

        // Note 1, channel 13: an upper-zone member with a 24-semitone range bent halfway (+12), plus
        // its master's two-semitone range bent halfway down (-1).
        Cents(audio, 1, 11.0).Should().BeLessThan(1.0);

        // Note 2, channel 8: in no zone at all, so MIDI's own two semitones bent halfway (+1) and no
        // master to combine with.
        Cents(audio, 2, 1.0).Should().BeLessThan(1.0);
    }

    [Fact]
    public void the_zones_a_configuration_message_asks_for_are_readable_afterwards()
    {
        //Arrange
        using var fixtures = MpeSequences.Create();
        var sequence = MpeSequences.Sequence(FullySpecified());

        //Act
        fixtures.Render(sequence, 3.0, synthesizer => synthesizer.MpeMode = MpeMode.Auto);

        var lower = fixtures.LastSynthesizer.MpeLowerZone;
        var upper = fixtures.LastSynthesizer.MpeUpperZone;

        //Assert
        lower.IsActive.Should().BeTrue();
        lower.MemberCount.Should().Be(4);
        lower.MemberBendRange.Should().Be(12.0);
        upper.IsActive.Should().BeTrue();
        upper.MemberCount.Should().Be(4);
        upper.MemberBendRange.Should().Be(24.0);
    }

    [Fact]
    public void an_export_with_no_configuration_message_is_detected_as_a_lower_zone()
    {
        //Arrange
        using var fixtures = MpeSequences.Create();
        var sequence = MpeSequences.Sequence(AbletonStyleExport());

        //Act
        fixtures.Render(sequence, 3.0, synthesizer => synthesizer.MpeMode = MpeMode.Auto);

        //Assert
        fixtures.LastSynthesizer.MpeLowerZone.IsActive.Should().BeTrue();
        fixtures.LastSynthesizer.MpeLowerZone.MemberCount.Should().Be(15);
        fixtures.LastSynthesizer.MpeUpperZone.IsActive.Should().BeFalse();
    }

    [Fact]
    public void an_ordinary_single_channel_file_is_left_alone_by_automatic_detection()
    {
        //Arrange
        using var fixtures = MpeSequences.Create();
        var sequence = MpeSequences.Sequence(
        [
            MpeSequences.Bend(0.0, 1, 0.5),
            MpeSequences.NoteOn(0.0, 1, 60),
            MpeSequences.NoteOff(0.4, 1, 60, 64),
            MpeSequences.NoteOn(0.6, 1, 64),
            MpeSequences.NoteOff(1.0, 1, 64, 64),
        ]);

        //Act
        var audio = fixtures.Render(
            sequence, 2.0, synthesizer => synthesizer.MpeMode = MpeMode.Auto);

        //Assert
        fixtures.LastSynthesizer.MpeLowerZone.IsActive.Should().BeFalse();
        Cents(audio, 0, 1.0).Should().BeLessThan(1.0);
    }

    [Theory]
    [InlineData(false, 6.0)]
    [InlineData(true, 3.0)]
    public void the_newest_note_on_a_member_channel_takes_the_channels_expression(
        bool supersede, double expectedSemitones)
    {
        //Arrange
        // The zone answers only from key 55 up, so the superseding note on key 40 claims the channel
        // without sounding: what is measured is the FIRST note's pitch, alone.
        using var fixtures = MpeSequences.Create(lowestNote: 55);

        var events = new List<MidiEvent>
        {
            MpeSequences.Bend(0.0, 2, ThreeSemitonesOf48),
            MpeSequences.NoteOn(0.0, 2, 60),
            MpeSequences.Bend(0.25, 2, SixSemitonesOf48),
            MpeSequences.NoteOff(1.2, 2, 60, 64),
        };

        if (supersede)
        {
            events.Add(MpeSequences.NoteOn(0.2, 2, 40));
            events.Add(MpeSequences.NoteOff(1.2, 2, 40, 64));
        }

        //Act
        var audio = fixtures.Render(
            MpeSequences.Sequence(events), 2.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);

        //Assert
        // Without the newer note the first one follows the channel to six semitones; with it, the
        // first note keeps the three it had when it lost the channel.
        var offset = (int)(0.4 * MpeSequences.SampleRate);
        var measured = PitchProbe.Hertz(audio, offset, MpeSequences.SampleRate);
        var expected = MpeSequences.RootHertz * Math.Pow(2.0, expectedSemitones / 12.0);

        Math.Abs(PitchProbe.Cents(measured, expected)).Should().BeLessThan(1.0);
    }

    [Fact]
    public void a_notes_timbre_follows_only_its_own_channel()
    {
        //Arrange
        // CC 74 drives the group's volume, so the note played on a bright channel is loud and the one
        // on a dark channel is quiet - a per-note reading rendered as a level.
        using var fixtures = MpeSequences.Create(
            "<mpeTimbre scope=\"voice\">" +
            "<binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"GROUP_VOLUME\" " +
            "modBehavior=\"set\" translation=\"linear\" translationOutputMin=\"0\" " +
            "translationOutputMax=\"1\" /></mpeTimbre>");

        var events = new List<MidiEvent>(AbletonStyleExport())
        {
            MpeSequences.Controller(0.0, 2, 74, 127),
            MpeSequences.Controller(0.0, 3, 74, 8),
        };

        //Act
        var audio = fixtures.Render(
            MpeSequences.Sequence(events), 3.0, synthesizer => synthesizer.MpeMode = MpeMode.Auto);

        //Assert
        Level(audio, 0).Should().BeGreaterThan(0.2);
        Level(audio, 1).Should().BeLessThan(0.05);
    }

    [Fact]
    public void a_notes_pressure_follows_only_its_own_channel()
    {
        //Arrange
        using var fixtures = MpeSequences.Create(
            "<mpePressure scope=\"voice\">" +
            "<binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"GROUP_VOLUME\" " +
            "modBehavior=\"set\" translation=\"linear\" translationOutputMin=\"0\" " +
            "translationOutputMax=\"1\" /></mpePressure>");

        var events = new List<MidiEvent>(AbletonStyleExport())
        {
            MpeSequences.Pressure(0.0, 2, 127),
            MpeSequences.Pressure(0.0, 3, 4),
        };

        //Act
        var audio = fixtures.Render(
            MpeSequences.Sequence(events), 3.0, synthesizer => synthesizer.MpeMode = MpeMode.Auto);

        //Assert
        Level(audio, 0).Should().BeGreaterThan(0.2);
        Level(audio, 1).Should().BeLessThan(0.05);
    }

    [Fact]
    public void a_zone_masters_pressure_reaches_every_note_in_the_zone()
    {
        //Arrange
        using var fixtures = MpeSequences.Create(
            "<mpePressure scope=\"voice\">" +
            "<binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"GROUP_VOLUME\" " +
            "modBehavior=\"set\" translation=\"linear\" translationOutputMin=\"0\" " +
            "translationOutputMax=\"1\" /></mpePressure>");

        var events = new List<MidiEvent>(AbletonStyleExport())
        {
            MpeSequences.Pressure(0.0, 1, 127),
        };

        //Act
        var audio = fixtures.Render(
            MpeSequences.Sequence(events), 3.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);

        //Assert
        Level(audio, 0).Should().BeGreaterThan(0.2);
        Level(audio, 1).Should().BeGreaterThan(0.2);
    }

    [Fact]
    public void the_lift_velocity_of_every_note_is_captured()
    {
        //Arrange
        using var fixtures = MpeSequences.Create();
        var sequence = MpeSequences.Sequence(
        [
            MpeSequences.NoteOn(0.0, 2, 60),
            MpeSequences.NoteOff(0.2, 2, 60, 96),
            MpeSequences.NoteOn(0.4, 3, 64),
            MpeSequences.NoteOff(0.6, 3, 64, 11),
        ]);

        //Act
        fixtures.Render(sequence, 1.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);

        //Assert
        // Channels are 0-based on the synthesizer's own surface, as MIDI messages carry them.
        fixtures.LastSynthesizer.ReleaseVelocity(1, 60).Should().Be(96);
        fixtures.LastSynthesizer.ReleaseVelocity(2, 64).Should().Be(11);
        fixtures.LastSynthesizer.ReleaseVelocity(2, 60).Should().Be(0);
    }

    // An export shaped like a sequencer's: bends written for every channel before the first note,
    // then one note per channel from 2 to 5. Nothing plays on channel 1, which is the shape the
    // automatic detector looks for.
    private static IEnumerable<MidiEvent> AbletonStyleExport()
    {
        double[] bends = [ThreeSemitonesOf48, -SixSemitonesOf48, SixSemitonesOf48, 0.0];

        for (var note = 0; note < bends.Length; note++)
        {
            yield return MpeSequences.Bend(0.0, note + 2, bends[note]);
        }

        for (var note = 0; note < bends.Length; note++)
        {
            var start = note * NoteSpacing;
            yield return MpeSequences.NoteOn(start, note + 2, 60);
            yield return MpeSequences.NoteOff(start + NoteSeconds, note + 2, 60, 64);
        }
    }

    // A file that says everything: a four-member lower zone, a four-member upper zone, a bend range
    // per zone, master bends on both, and one note in each zone plus one outside both.
    private static IEnumerable<MidiEvent> FullySpecified()
    {
        foreach (var midiEvent in MpeSequences.Rpn(0.0, 1, 6, 4))
        {
            yield return midiEvent;
        }

        foreach (var midiEvent in MpeSequences.Rpn(0.0, 16, 6, 4))
        {
            yield return midiEvent;
        }

        // RPN 0 on a member configures every member of that zone.
        foreach (var midiEvent in MpeSequences.Rpn(0.01, 2, 0, 12))
        {
            yield return midiEvent;
        }

        foreach (var midiEvent in MpeSequences.Rpn(0.01, 13, 0, 24))
        {
            yield return midiEvent;
        }

        yield return MpeSequences.Bend(0.02, 1, 0.5);
        yield return MpeSequences.Bend(0.02, 16, -0.5);
        yield return MpeSequences.Bend(0.02, 3, 0.25);
        yield return MpeSequences.Bend(0.02, 13, 0.5);
        yield return MpeSequences.Bend(0.02, 8, 0.5);

        int[] channels = [3, 13, 8];

        for (var note = 0; note < channels.Length; note++)
        {
            var start = note * NoteSpacing;
            yield return MpeSequences.NoteOn(start, channels[note], 60);
            yield return MpeSequences.NoteOff(start + NoteSeconds, channels[note], 60, 64);
        }
    }

    // How far the note's measured pitch is from the pitch the bend arithmetic asks for, in cents.
    private static double Cents(float[] audio, int note, double semitones)
    {
        var offset = (int)((note * NoteSpacing * MpeSequences.SampleRate) + 4096);
        var measured = PitchProbe.Hertz(audio, offset, MpeSequences.SampleRate);
        var expected = MpeSequences.RootHertz * Math.Pow(2.0, semitones / 12.0);

        return Math.Abs(PitchProbe.Cents(measured, expected));
    }

    // The level of one note's window.
    private static double Level(float[] audio, int note)
    {
        var offset = (int)((note * NoteSpacing * MpeSequences.SampleRate) + 4096);
        return DecentSamplerRenderProbe.Rms(audio, offset, 4096);
    }
}
