using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.Mpe;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.Mpe;

/// <summary>
/// The SoundFont engine playing a MIDI file that carries an expressive performance: each note bends,
/// brightens and swells on its own channel, plus its zone master, and the pitches come out equal to
/// the bend arithmetic within one cent.
/// </summary>
/// <remarks>
/// The same two fixtures the Decent Sampler engine is measured against - a sequencer-style export
/// with no configuration message, and a file that says everything out loud - played through the
/// committed synthetic SoundFont. Behaving alike with an expressive controller is the point of the
/// exercise, so the expectations here are the ones in <see cref="MpeMidiFileTests"/>.
/// </remarks>
public class MpeSoundFontEngineTests
{
    private const int Key = MpeEngineFixtures.SoundFontKey;

    /// <summary>Frames between the samples pinned in <see cref="LegacyRenderLeft"/> and its twin.</summary>
    private const int LegacyStride = 1536;

    // The performance that the byte-identity fence renders, sampled from the engine BEFORE any of
    // this existed. MpeMode.Off must still produce it. This was a single digest until the SFZ pair
    // had to pass on Windows, Linux and macOS at once, which no digest of a render can - the SoundFont
    // voice path reaches the platform's powf, sinf and exp the same way, and only luck kept this one
    // agreeing across two of the three. See PinnedRender. Regenerate only when a deliberate change to
    // SoundFont voice arithmetic is being made, and say so in the commit.
    private static readonly double[] LegacyRenderLeft =
    [
        0.000000000, 0.002114212, 0.005147552, 0.008180997,
        0.011206800, 0.014232610, 0.017242410, 0.024681600,
        0.027659370, 0.030621220, 0.033558660, 0.036487710,
        0.000000000, 0.000000000, 0.000000000, 0.000000000,
        0.000000000, 0.000000000, 0.116041200, 0.053386830,
        -0.031848260, -0.103617600, -0.131536400, -0.103783800,
        -0.032105420, 0.057235050, 0.117985400, 0.128799700,
        0.085105880, 0.000000000, 0.000000000, 0.000000000,
        0.000000000, 0.000000000, 0.000000000, 0.062809120,
        -0.025574700, -0.013965040, 0.052265690, -0.085800560,
        0.111597000, -0.127266400, 0.131106200, -0.122064900,
        0.101929600, -0.072545970, 0.009599230, 0.000000000,
        0.000000000, 0.000000000, 0.000000000, 0.000000000,
        0.025664720, -0.126609700, 0.089532980, 0.045138530,
        -0.130611700, 0.073695970, 0.063548100, -0.131589400,
        0.060155050, 0.076856840, -0.130086700, 0.041506710,
    ];

    // The right channel is pinned separately: the SoundFont engine pans its voices with a sine law,
    // so the two channels differ by a fraction of a percent throughout this performance.
    private static readonly double[] LegacyRenderRight =
    [
        0.000000000, 0.002114414, 0.005148045, 0.008181782,
        0.011207880, 0.014233970, 0.017244070, 0.024683970,
        0.027662020, 0.030624160, 0.033561870, 0.036491210,
        0.000000000, 0.000000000, 0.000000000, 0.000000000,
        0.000000000, 0.000000000, 0.116052300, 0.053391950,
        -0.031851320, -0.103627500, -0.131549100, -0.103793800,
        -0.032108500, 0.057240540, 0.117996700, 0.128812100,
        0.085114040, 0.000000000, 0.000000000, 0.000000000,
        0.000000000, 0.000000000, 0.000000000, 0.062815150,
        -0.025577150, -0.013966380, 0.052270700, -0.085808790,
        0.111607700, -0.127278700, 0.131118800, -0.122076600,
        0.101939400, -0.072552930, 0.009600151, 0.000000000,
        0.000000000, 0.000000000, 0.000000000, 0.000000000,
        0.025667180, -0.126621900, 0.089541570, 0.045142860,
        -0.130624200, 0.073703040, 0.063554200, -0.131602100,
        0.060160820, 0.076864220, -0.130099100, 0.041510690,
    ];

    [Fact]
    public void an_export_with_no_configuration_message_gives_each_note_its_own_bend()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create();
        var sequence = MpeSequences.Sequence(MpeEngineFixtures.AbletonStyleExport(Key));

        //Act
        var audio = fixtures.RenderSoundFont(
            sequence, 3.0, synthesizer => synthesizer.MpeMode = MpeMode.Auto);

        //Assert
        // Channels 2 to 5 bend by +3, -6, +6 and 0 semitones over the 48-semitone member default.
        double[] expected = [3.0, -6.0, 6.0, 0.0];

        for (var note = 0; note < expected.Length; note++)
        {
            MpeEngineFixtures.Cents(audio, note, expected[note]).Should().BeLessThan(1.0);
        }
    }

    [Fact]
    public void the_same_export_played_without_mpe_bends_only_two_semitones()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create();
        var sequence = MpeSequences.Sequence(MpeEngineFixtures.AbletonStyleExport(Key));

        //Act
        var audio = fixtures.RenderSoundFont(
            sequence, 3.0, synthesizer => synthesizer.MpeMode = MpeMode.Off);

        //Assert
        // The same wheel positions over MIDI's own two-semitone range: an eighth of the MPE reading.
        double[] expected = [0.125, -0.25, 0.25, 0.0];

        for (var note = 0; note < expected.Length; note++)
        {
            MpeEngineFixtures.Cents(audio, note, expected[note]).Should().BeLessThan(1.0);
        }
    }

    [Fact]
    public void an_export_with_no_configuration_message_is_detected_as_a_lower_zone()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create();
        var sequence = MpeSequences.Sequence(MpeEngineFixtures.AbletonStyleExport(Key));

        //Act
        fixtures.RenderSoundFont(sequence, 3.0, synthesizer => synthesizer.MpeMode = MpeMode.Auto);

        //Assert
        fixtures.LastSoundFontSynthesizer.MpeLowerZone.IsActive.Should().BeTrue();
        fixtures.LastSoundFontSynthesizer.MpeLowerZone.MemberCount.Should().Be(15);
        fixtures.LastSoundFontSynthesizer.MpeUpperZone.IsActive.Should().BeFalse();
    }

    [Fact]
    public void the_member_bend_range_override_changes_how_far_a_bend_reaches()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create();
        var sequence = MpeSequences.Sequence(MpeEngineFixtures.AbletonStyleExport(Key));

        //Act
        var audio = fixtures.RenderSoundFont(sequence, 3.0, synthesizer =>
        {
            synthesizer.MpeMemberBendRange = 24.0;
            synthesizer.MpeMode = MpeMode.LowerZone;
        });

        //Assert
        // Half the range, so half the bend.
        MpeEngineFixtures.Cents(audio, 0, 1.5).Should().BeLessThan(1.0);
        MpeEngineFixtures.Cents(audio, 1, -3.0).Should().BeLessThan(1.0);
    }

    [Fact]
    public void a_fully_specified_file_combines_each_zones_master_bend_with_its_members()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create();
        var sequence = MpeSequences.Sequence(MpeEngineFixtures.FullySpecified(Key));

        //Act
        var audio = fixtures.RenderSoundFont(
            sequence, 3.0, synthesizer => synthesizer.MpeMode = MpeMode.Auto);

        //Assert
        // Note 0, channel 3: a lower-zone member with a 12-semitone range bent a quarter of the way
        // (+3), plus its master's two-semitone range bent halfway (+1).
        MpeEngineFixtures.Cents(audio, 0, 4.0).Should().BeLessThan(1.0);

        // Note 1, channel 13: an upper-zone member with a 24-semitone range bent halfway (+12), plus
        // its master's two-semitone range bent halfway down (-1).
        MpeEngineFixtures.Cents(audio, 1, 11.0).Should().BeLessThan(1.0);

        // Note 2, channel 8: in no zone at all, so MIDI's own two semitones bent halfway (+1) and no
        // master to combine with.
        MpeEngineFixtures.Cents(audio, 2, 1.0).Should().BeLessThan(1.0);
    }

    [Fact]
    public void the_zones_a_configuration_message_asks_for_are_readable_afterwards()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create();
        var sequence = MpeSequences.Sequence(MpeEngineFixtures.FullySpecified(Key));

        //Act
        fixtures.RenderSoundFont(sequence, 3.0, synthesizer => synthesizer.MpeMode = MpeMode.Auto);

        var lower = fixtures.LastSoundFontSynthesizer.MpeLowerZone;
        var upper = fixtures.LastSoundFontSynthesizer.MpeUpperZone;

        //Assert
        lower.IsActive.Should().BeTrue();
        lower.MemberCount.Should().Be(4);
        lower.MemberBendRange.Should().Be(12.0);
        upper.IsActive.Should().BeTrue();
        upper.MemberCount.Should().Be(4);
        upper.MemberBendRange.Should().Be(24.0);
    }

    [Theory]
    [InlineData(false, 6.0)]
    [InlineData(true, 3.0)]
    public void the_newest_note_on_a_member_channel_takes_the_channels_expression(
        bool supersede, double expectedSemitones)
    {
        //Arrange
        // The superseding note is played at velocity 1, some eighty decibels below the first, so it
        // claims the channel without disturbing the measurement of the note that lost it.
        using var fixtures = MpeEngineFixtures.Create();

        var events = new List<MidiEvent>
        {
            MpeSequences.Bend(0.0, 2, MpeEngineFixtures.ThreeSemitonesOf48),
            MpeSequences.NoteOn(0.0, 2, Key),
            MpeSequences.Bend(0.25, 2, MpeEngineFixtures.SixSemitonesOf48),
            MpeSequences.NoteOff(1.2, 2, Key, 64),
        };

        if (supersede)
        {
            events.Add(MpeSequences.NoteOn(0.2, 2, Key + 1, 1));
            events.Add(MpeSequences.NoteOff(1.2, 2, Key + 1, 64));
        }

        //Act
        var audio = fixtures.RenderSoundFont(
            MpeSequences.Sequence(events), 2.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);

        //Assert
        // Without the newer note the first one follows the channel to six semitones; with it, the
        // first note keeps the three it had when it lost the channel.
        var offset = (int)(0.4 * MpeEngineFixtures.SampleRate);
        var measured = PitchProbe.Hertz(audio, offset, MpeEngineFixtures.SampleRate);
        var expected = MpeEngineFixtures.RootHertz * Math.Pow(2.0, expectedSemitones / 12.0);

        Math.Abs(PitchProbe.Cents(measured, expected)).Should().BeLessThan(1.0);
    }

    [Fact]
    public void a_zone_masters_sustain_pedal_holds_a_member_channels_note()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create();

        var held = new List<MidiEvent>
        {
            MpeSequences.Controller(0.0, 1, 64, 127),
            MpeSequences.NoteOn(0.0, 2, Key),
            MpeSequences.NoteOff(0.2, 2, Key, 64),
        };

        var free = new List<MidiEvent>
        {
            MpeSequences.NoteOn(0.0, 2, Key),
            MpeSequences.NoteOff(0.2, 2, Key, 64),
        };

        //Act
        var withPedal = fixtures.RenderSoundFont(
            MpeSequences.Sequence(held), 1.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);
        var withoutPedal = fixtures.RenderSoundFont(
            MpeSequences.Sequence(free), 1.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);

        //Assert
        var late = (int)(0.6 * MpeEngineFixtures.SampleRate);
        MpeEngineFixtures.Rms(withPedal, late, 4096).Should().BeGreaterThan(0.05);
        MpeEngineFixtures.Rms(withoutPedal, late, 4096).Should().BeLessThan(0.001);
    }

    [Fact]
    public void a_zone_masters_volume_reaches_a_member_that_has_not_sent_its_own()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create();

        var events = new List<MidiEvent>
        {
            MpeSequences.Controller(0.0, 1, 7, 20),
            MpeSequences.Controller(0.0, 3, 7, 127),
            MpeSequences.NoteOn(0.0, 2, Key),
            MpeSequences.NoteOff(MpeEngineFixtures.NoteSeconds, 2, Key, 64),
            MpeSequences.NoteOn(MpeEngineFixtures.NoteSpacing, 3, Key),
            MpeSequences.NoteOff(MpeEngineFixtures.NoteSpacing + MpeEngineFixtures.NoteSeconds, 3, Key, 64),
        };

        //Act
        var audio = fixtures.RenderSoundFont(
            MpeSequences.Sequence(events), 2.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);

        //Assert
        // The first note never sent a volume of its own, so the master's quiet one applies to it; the
        // second sent its own and keeps it.
        MpeEngineFixtures.Level(audio, 0).Should().BeLessThan(0.02);
        MpeEngineFixtures.Level(audio, 1).Should().BeGreaterThan(0.1);
    }

    [Fact]
    public void a_notes_pressure_deepens_its_vibrato_while_a_zone_is_active()
    {
        //Arrange
        // Channel pressure is a DEFAULT SoundFont modulator, routed to vibrato depth beside the
        // modulation wheel. There is nothing else in a SoundFont for it to reach.
        using var fixtures = MpeEngineFixtures.Create();

        var pressed = MpeSequences.Sequence(
        [
            MpeSequences.Pressure(0.0, 2, 127),
            MpeSequences.NoteOn(0.0, 2, Key),
            MpeSequences.NoteOff(0.8, 2, Key, 64),
        ]);

        var relaxed = MpeSequences.Sequence(
        [
            MpeSequences.NoteOn(0.0, 2, Key),
            MpeSequences.NoteOff(0.8, 2, Key, 64),
        ]);

        //Act
        var pressedAudio = fixtures.RenderSoundFontStereo(
            pressed, 1.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);
        var relaxedAudio = fixtures.RenderSoundFontStereo(
            relaxed, 1.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);
        var pressedWithoutMpe = fixtures.RenderSoundFontStereo(pressed, 1.0);
        var relaxedWithoutMpe = fixtures.RenderSoundFontStereo(relaxed, 1.0);

        //Assert
        MpeEngineFixtures.Digest(pressedAudio.Left, pressedAudio.Right)
            .Should().NotBe(MpeEngineFixtures.Digest(relaxedAudio.Left, relaxedAudio.Right));

        // With MPE switched off the pressure message is stored and nothing reads it, so the two
        // performances are the same audio down to the last sample.
        MpeEngineFixtures.Digest(pressedWithoutMpe.Left, pressedWithoutMpe.Right)
            .Should().Be(MpeEngineFixtures.Digest(relaxedWithoutMpe.Left, relaxedWithoutMpe.Right));
    }

    [Fact]
    public void a_registered_parameter_null_stops_a_later_data_entry_reaching_the_bend_range()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create();

        var events = new List<MidiEvent>(MpeSequences.Rpn(0.0, 2, 0, 12))
        {
            // RPN null: data entry now points at nothing, so the 48 that follows is ignored.
            MpeSequences.Controller(0.01, 2, 101, 127),
            MpeSequences.Controller(0.01, 2, 100, 127),
            MpeSequences.Controller(0.01, 2, 6, 48),
            MpeSequences.Bend(0.02, 2, 1.0),
            MpeSequences.NoteOn(0.05, 2, Key),
            MpeSequences.NoteOff(0.8, 2, Key, 64),
        };

        //Act
        var audio = fixtures.RenderSoundFont(
            MpeSequences.Sequence(events), 1.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);

        //Assert
        var measured = PitchProbe.Hertz(audio, (int)(0.2 * MpeEngineFixtures.SampleRate), MpeEngineFixtures.SampleRate);
        var expected = MpeEngineFixtures.RootHertz * Math.Pow(2.0, 12.0 / 12.0);

        Math.Abs(PitchProbe.Cents(measured, expected)).Should().BeLessThan(1.0);
    }

    [Fact]
    public void data_increment_and_decrement_nudge_the_registered_bend_range()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create();

        var events = new List<MidiEvent>(MpeSequences.Rpn(0.0, 2, 0, 12))
        {
            MpeSequences.Controller(0.01, 2, 96, 0),
            MpeSequences.Controller(0.01, 2, 96, 0),
            MpeSequences.Controller(0.01, 2, 97, 0),
            MpeSequences.Bend(0.02, 2, 1.0),
            MpeSequences.NoteOn(0.05, 2, Key),
            MpeSequences.NoteOff(0.8, 2, Key, 64),
        };

        //Act
        var audio = fixtures.RenderSoundFont(
            MpeSequences.Sequence(events), 1.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);

        //Assert
        // Twelve, up twice and down once: thirteen semitones at full bend.
        var measured = PitchProbe.Hertz(audio, (int)(0.2 * MpeEngineFixtures.SampleRate), MpeEngineFixtures.SampleRate);
        var expected = MpeEngineFixtures.RootHertz * Math.Pow(2.0, 13.0 / 12.0);

        Math.Abs(PitchProbe.Cents(measured, expected)).Should().BeLessThan(1.0);
    }

    [Fact]
    public void a_registered_bend_range_on_a_member_keeps_its_cents_when_the_semitones_arrive()
    {
        //Arrange
        // RPN 0 sent on a member configures every member of the zone, and its two data bytes are
        // semitones and cents. Both halves have to land on the ZONE's range, not on the channel's
        // own, or the second byte reads the wrong number to build on.
        using var fixtures = MpeEngineFixtures.Create();

        var events = new List<MidiEvent>(MpeSequences.Rpn(0.0, 2, 0, 12, 50))
        {
            MpeSequences.Bend(0.02, 2, 1.0),
            MpeSequences.NoteOn(0.05, 2, Key),
            MpeSequences.NoteOff(0.8, 2, Key, 64),
        };

        //Act
        var audio = fixtures.RenderSoundFont(
            MpeSequences.Sequence(events), 1.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);

        //Assert
        var measured = PitchProbe.Hertz(
            audio, (int)(0.2 * MpeEngineFixtures.SampleRate), MpeEngineFixtures.SampleRate);
        var expected = MpeEngineFixtures.RootHertz * Math.Pow(2.0, 12.5 / 12.0);

        Math.Abs(PitchProbe.Cents(measured, expected)).Should().BeLessThan(1.0);
        fixtures.LastSoundFontSynthesizer.MpeLowerZone.MemberBendRange.Should().Be(12.5);
    }

    [Fact]
    public void the_lift_velocity_of_every_note_is_captured()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create();
        var sequence = MpeSequences.Sequence(
        [
            MpeSequences.NoteOn(0.0, 2, Key),
            MpeSequences.NoteOff(0.2, 2, Key, 96),
            MpeSequences.NoteOn(0.4, 3, Key),
            MpeSequences.NoteOff(0.6, 3, Key, 11),
        ]);

        //Act
        fixtures.RenderSoundFont(sequence, 1.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);

        //Assert
        // Channels are 0-based on the synthesizer's own surface, as MIDI messages carry them.
        fixtures.LastSoundFontSynthesizer.ReleaseVelocity(1, Key).Should().Be(96);
        fixtures.LastSoundFontSynthesizer.ReleaseVelocity(2, Key).Should().Be(11);
        fixtures.LastSoundFontSynthesizer.ReleaseVelocity(3, Key).Should().Be(0);
    }

    [Fact]
    public void a_notes_timbre_and_pressure_are_readable_with_the_zone_master_folded_in()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create();
        var sequence = MpeSequences.Sequence(
        [
            MpeSequences.Controller(0.0, 1, 74, 84),
            MpeSequences.Controller(0.0, 2, 74, 64),
            MpeSequences.Pressure(0.0, 1, 20),
            MpeSequences.Pressure(0.0, 2, 40),
            MpeSequences.NoteOn(0.0, 2, Key),
            MpeSequences.NoteOff(0.2, 2, Key, 64),
        ]);

        //Act
        fixtures.RenderSoundFont(sequence, 0.5, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);

        //Assert
        // Timbre rests at the centre of its range, so the master's 84 is a bipolar offset of
        // (84 - 64) / 127 on top of the member's own 64 / 127. Pressure rests at zero and adds.
        fixtures.LastSoundFontSynthesizer.MpeTimbre(1)
            .Should().BeApproximately((64.0 + (84.0 - 64.0)) / 127.0, 1e-9);
        fixtures.LastSoundFontSynthesizer.MpePressure(1, Key)
            .Should().BeApproximately(60.0 / 127.0, 1e-9);
    }

    [Fact]
    public void the_synthesizer_takes_its_mpe_configuration_from_its_settings()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create();

        var settings = new SoundFontSynthesizerSettings(MpeEngineFixtures.SampleRate)
        {
            MpeMode = MpeMode.Both,
            MpeMemberBendRange = 36.0,
            MpeLowerZoneMemberCount = 5,
            MpeUpperZoneMemberCount = 4,
        };

        //Act
        var synthesizer = new SoundFontSynthesizer(fixtures.SoundFont, settings);

        //Assert
        synthesizer.MpeMode.Should().Be(MpeMode.Both);
        synthesizer.MpeMemberBendRange.Should().Be(36.0);
        synthesizer.MpeLowerZone.MemberCount.Should().Be(5);
        synthesizer.MpeUpperZone.MemberCount.Should().Be(4);
        synthesizer.MpeLowerZone.MemberBendRange.Should().Be(36.0);
    }

    [Fact]
    public void mpe_mode_off_renders_an_expressive_performance_exactly_as_the_engine_always_has()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create();
        var sequence = MpeSequences.Sequence(MpeEngineFixtures.ExpressivePerformance(Key));

        //Act
        var audio = fixtures.RenderSoundFontStereo(sequence, 3.0);

        //Assert
        // Measured from the engine before it knew anything about MPE. Every bend, controller,
        // pressure and registered-parameter message in the performance is delivered; with the zones
        // switched off the render still holds these values, on every platform, within the one
        // tolerance PinnedRender explains.
        PinnedRender.ShouldStillRender(audio.Left, LegacyStride, LegacyRenderLeft);
        PinnedRender.ShouldStillRender(audio.Right, LegacyStride, LegacyRenderRight);
    }

    [Fact]
    public void the_mpe_settings_do_not_reach_a_render_with_the_zones_switched_off()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create();

        //Act - the same performance twice: once through a synthesizer that was given the zone settings
        //and then switched off, once through one that never heard of them.
        var configured = fixtures.RenderSoundFontStereo(
            MpeSequences.Sequence(MpeEngineFixtures.ExpressivePerformance(Key)), 3.0, synthesizer =>
            {
                synthesizer.MpeMemberBendRange = 96.0;
                synthesizer.MpeLowerZoneMemberCount = 15;
                synthesizer.MpeUpperZoneMemberCount = 15;
                synthesizer.MpeMode = MpeMode.Auto;
                synthesizer.MpeMode = MpeMode.Off;
            });

        var untouched = fixtures.RenderSoundFontStereo(
            MpeSequences.Sequence(MpeEngineFixtures.ExpressivePerformance(Key)), 3.0);

        //Assert
        // No pinned numbers and no tolerance: the claim is that the settings do not reach the render,
        // and two renders on the SAME machine settle that bit for bit, whatever platform it is.
        MpeEngineFixtures.Digest(configured.Left, configured.Right)
            .Should().Be(MpeEngineFixtures.Digest(untouched.Left, untouched.Right));
    }
}
