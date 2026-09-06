using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth.Mpe;
using CodeBrix.Audio.Synth.Sfz;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.Mpe;

/// <summary>
/// The SFZ engine playing a MIDI file that carries an expressive performance, measured against the
/// same expectations as the SoundFont and Decent Sampler engines.
/// </summary>
/// <remarks>
/// The SFZ format states a region's own bend range in cents, with <c>bend_up</c> and
/// <c>bend_down</c>, and routes expression through its <c>_onccN</c> modulation matrix. So the zone
/// rules arrive here as a SCALE on the region's own range and as the values the modulation sources
/// read - a library keeps whatever it asked for, and a performance still reaches its whole range.
/// </remarks>
public class MpeSfzEngineTests
{
    private const int Key = MpeEngineFixtures.SfzKey;

    /// <summary>Frames between the samples pinned in <see cref="LegacyRenderLeft"/> and its twin.</summary>
    private const int LegacyStride = 1536;

    // The performance that the byte-identity fence renders, sampled from the engine BEFORE the zone
    // rules existed. MpeMode.Off must still produce it. These were a single digest until they had to
    // pass on Windows, Linux and macOS at once, which no digest of a render can - see PinnedRender.
    private static readonly double[] LegacyRenderLeft =
    [
        0.000000000, 0.005057688, 0.010112597, 0.015162038,
        0.020203318, 0.025233762, 0.030250689, 0.035251427,
        0.040233318, 0.045193713, 0.050129972, 0.055039462,
        0.000000000, 0.000000000, 0.000000000, 0.000000000,
        0.000000000, 0.000000000, 0.192594424, 0.087585002,
        -0.054491621, -0.173508406, -0.219096661, -0.171962082,
        -0.052050531, 0.089892246, 0.193795592, 0.215685576,
        0.146295458, 0.000000000, 0.000000000, 0.000000000,
        0.000000000, 0.000000000, 0.000000000, 0.103312097,
        -0.041114416, -0.024779918, 0.088443838, -0.144057572,
        0.186683133, -0.212383747, 0.218788326, -0.205473065,
        0.173498958, -0.125787064, 0.004585469, 0.000000000,
        0.000000000, 0.000000000, 0.000000000, 0.000000000,
        0.041289367, -0.210495397, 0.150237173, 0.073796898,
        -0.217383832, 0.123997323, 0.104560569, -0.219135374,
        0.094827361, 0.132853419, -0.215708658, 0.063416585,
    ];

    // The right channel is pinned separately rather than asserted equal to the left. It happens to be
    // identical today, because nothing in this performance moves the SFZ engine off centre, but that
    // is an observation about the fixture and not a rule the engine promises.
    private static readonly double[] LegacyRenderRight =
    [
        0.000000000, 0.005057688, 0.010112597, 0.015162038,
        0.020203318, 0.025233762, 0.030250689, 0.035251427,
        0.040233318, 0.045193713, 0.050129972, 0.055039462,
        0.000000000, 0.000000000, 0.000000000, 0.000000000,
        0.000000000, 0.000000000, 0.192594424, 0.087585002,
        -0.054491621, -0.173508406, -0.219096661, -0.171962082,
        -0.052050531, 0.089892246, 0.193795592, 0.215685576,
        0.146295458, 0.000000000, 0.000000000, 0.000000000,
        0.000000000, 0.000000000, 0.000000000, 0.103312097,
        -0.041114416, -0.024779918, 0.088443838, -0.144057572,
        0.186683133, -0.212383747, 0.218788326, -0.205473065,
        0.173498958, -0.125787064, 0.004585469, 0.000000000,
        0.000000000, 0.000000000, 0.000000000, 0.000000000,
        0.041289367, -0.210495397, 0.150237173, 0.073796898,
        -0.217383832, 0.123997323, 0.104560569, -0.219135374,
        0.094827361, 0.132853419, -0.215708658, 0.063416585,
    ];

    [Fact]
    public void an_export_with_no_configuration_message_gives_each_note_its_own_bend()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create();
        var sequence = MpeSequences.Sequence(MpeEngineFixtures.AbletonStyleExport(Key));

        //Act
        var audio = fixtures.RenderSfz(sequence, 3.0, synthesizer => synthesizer.MpeMode = MpeMode.Auto);

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
        var audio = fixtures.RenderSfz(sequence, 3.0, synthesizer => synthesizer.MpeMode = MpeMode.Off);

        //Assert
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
        fixtures.RenderSfz(sequence, 3.0, synthesizer => synthesizer.MpeMode = MpeMode.Auto);

        //Assert
        fixtures.LastSfzSynthesizer.MpeLowerZone.IsActive.Should().BeTrue();
        fixtures.LastSfzSynthesizer.MpeLowerZone.MemberCount.Should().Be(15);
        fixtures.LastSfzSynthesizer.MpeUpperZone.IsActive.Should().BeFalse();
    }

    [Fact]
    public void the_member_bend_range_override_changes_how_far_a_bend_reaches()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create();
        var sequence = MpeSequences.Sequence(MpeEngineFixtures.AbletonStyleExport(Key));

        //Act
        var audio = fixtures.RenderSfz(sequence, 3.0, synthesizer =>
        {
            synthesizer.MpeMemberBendRange = 24.0;
            synthesizer.MpeMode = MpeMode.LowerZone;
        });

        //Assert
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
        var audio = fixtures.RenderSfz(sequence, 3.0, synthesizer => synthesizer.MpeMode = MpeMode.Auto);

        //Assert
        // A lower-zone member over twelve semitones bent a quarter (+3) plus its master's two bent
        // halfway (+1); an upper-zone member over twenty-four bent halfway (+12) plus its master's
        // two bent halfway down (-1); and a channel in no zone with MIDI's own two bent halfway.
        MpeEngineFixtures.Cents(audio, 0, 4.0).Should().BeLessThan(1.0);
        MpeEngineFixtures.Cents(audio, 1, 11.0).Should().BeLessThan(1.0);
        MpeEngineFixtures.Cents(audio, 2, 1.0).Should().BeLessThan(1.0);
    }

    [Fact]
    public void the_zones_a_configuration_message_asks_for_are_readable_afterwards()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create();
        var sequence = MpeSequences.Sequence(MpeEngineFixtures.FullySpecified(Key));

        //Act
        fixtures.RenderSfz(sequence, 3.0, synthesizer => synthesizer.MpeMode = MpeMode.Auto);

        var lower = fixtures.LastSfzSynthesizer.MpeLowerZone;
        var upper = fixtures.LastSfzSynthesizer.MpeUpperZone;

        //Assert
        lower.IsActive.Should().BeTrue();
        lower.MemberCount.Should().Be(4);
        lower.MemberBendRange.Should().Be(12.0);
        upper.IsActive.Should().BeTrue();
        upper.MemberCount.Should().Be(4);
        upper.MemberBendRange.Should().Be(24.0);
    }

    [Fact]
    public void a_regions_own_bend_range_still_scales_a_performances_bend()
    {
        //Arrange
        // The region asks for an octave each way, six times MIDI's own two semitones. A member
        // bending three semitones of its zone range therefore reaches eighteen.
        using var fixtures = MpeEngineFixtures.Create("bend_up=1200 bend_down=-1200");
        var sequence = MpeSequences.Sequence(MpeEngineFixtures.AbletonStyleExport(Key));

        //Act
        var audio = fixtures.RenderSfz(
            sequence, 3.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);

        //Assert
        MpeEngineFixtures.Cents(audio, 0, 18.0).Should().BeLessThan(1.0);
        MpeEngineFixtures.Cents(audio, 1, -36.0).Should().BeLessThan(1.0);
    }

    [Theory]
    [InlineData(false, 6.0)]
    [InlineData(true, 3.0)]
    public void the_newest_note_on_a_member_channel_takes_the_channels_expression(
        bool supersede, double expectedSemitones)
    {
        //Arrange
        // The superseding note is played at velocity 1, far below the first, so it claims the
        // channel without disturbing the measurement of the note that lost it.
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
        var audio = fixtures.RenderSfz(
            MpeSequences.Sequence(events), 2.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);

        //Assert
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
        var withPedal = fixtures.RenderSfz(
            MpeSequences.Sequence(held), 1.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);
        var withoutPedal = fixtures.RenderSfz(
            MpeSequences.Sequence(free), 1.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);

        //Assert
        var late = (int)(0.6 * MpeEngineFixtures.SampleRate);
        MpeEngineFixtures.Rms(withPedal, late, 4096).Should().BeGreaterThan(0.05);
        MpeEngineFixtures.Rms(withoutPedal, late, 4096).Should().BeLessThan(0.001);
    }

    [Fact]
    public void a_zone_masters_controller_reaches_a_member_that_has_not_sent_its_own()
    {
        //Arrange
        // The region's gain follows CC 7, which is how an SFZ library wires a volume fader.
        using var fixtures = MpeEngineFixtures.Create("amplitude_oncc7=100");

        var events = new List<MidiEvent>
        {
            MpeSequences.Controller(0.0, 1, 7, 10),
            MpeSequences.Controller(0.0, 3, 7, 127),
            MpeSequences.NoteOn(0.0, 2, Key),
            MpeSequences.NoteOff(MpeEngineFixtures.NoteSeconds, 2, Key, 64),
            MpeSequences.NoteOn(MpeEngineFixtures.NoteSpacing, 3, Key),
            MpeSequences.NoteOff(MpeEngineFixtures.NoteSpacing + MpeEngineFixtures.NoteSeconds, 3, Key, 64),
        };

        //Act
        var audio = fixtures.RenderSfz(
            MpeSequences.Sequence(events), 2.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);

        //Assert
        // The first note never sent a fader position of its own, so the master's applies; the second
        // sent its own and keeps it.
        MpeEngineFixtures.Level(audio, 0).Should().BeLessThan(0.02);
        MpeEngineFixtures.Level(audio, 1).Should().BeGreaterThan(0.1);
    }

    [Fact]
    public void a_notes_timbre_follows_only_its_own_channel()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create("amplitude_oncc74=100");

        var events = new List<MidiEvent>(MpeEngineFixtures.AbletonStyleExport(Key))
        {
            MpeSequences.Controller(0.0, 2, 74, 127),
            MpeSequences.Controller(0.0, 3, 74, 4),
        };

        //Act
        var audio = fixtures.RenderSfz(
            MpeSequences.Sequence(events), 3.0, synthesizer => synthesizer.MpeMode = MpeMode.Auto);

        //Assert
        MpeEngineFixtures.Level(audio, 0).Should().BeGreaterThan(0.1);
        MpeEngineFixtures.Level(audio, 1).Should().BeLessThan(0.02);
    }

    [Fact]
    public void a_notes_pressure_reaches_the_aftertouch_modulation_source()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create("amplitude_oncc129=100");

        var events = new List<MidiEvent>(MpeEngineFixtures.AbletonStyleExport(Key))
        {
            MpeSequences.Pressure(0.0, 2, 127),
            MpeSequences.Pressure(0.0, 3, 4),
        };

        //Act
        var audio = fixtures.RenderSfz(
            MpeSequences.Sequence(events), 3.0, synthesizer => synthesizer.MpeMode = MpeMode.Auto);

        //Assert
        MpeEngineFixtures.Level(audio, 0).Should().BeGreaterThan(0.1);
        MpeEngineFixtures.Level(audio, 1).Should().BeLessThan(0.02);
    }

    [Fact]
    public void a_zone_masters_pressure_reaches_every_note_in_the_zone()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create("amplitude_oncc129=100");

        var events = new List<MidiEvent>(MpeEngineFixtures.AbletonStyleExport(Key))
        {
            MpeSequences.Pressure(0.0, 1, 127),
        };

        //Act
        var audio = fixtures.RenderSfz(
            MpeSequences.Sequence(events), 3.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);

        //Assert
        MpeEngineFixtures.Level(audio, 0).Should().BeGreaterThan(0.1);
        MpeEngineFixtures.Level(audio, 1).Should().BeGreaterThan(0.1);
    }

    [Fact]
    public void a_release_sample_follows_the_lift_velocity_of_the_note_it_releases()
    {
        //Arrange
        // The ARIA extended source 132 is the note-off velocity. This is ordinary SFZ, not MPE: a
        // release sample that follows how quickly the finger left the key works in every mode.
        using var fixtures = MpeEngineFixtures.CreateWithSfzBody(
            "<region> sample=tone.wav pitch_keycenter=" + Key + " lokey=0 hikey=127 ampeg_release=0.005\n" +
            "<region> sample=tone.wav pitch_keycenter=" + Key + " lokey=0 hikey=127 trigger=release " +
            "ampeg_release=0.4 amplitude_oncc132=100");

        var sequence = MpeSequences.Sequence(
        [
            MpeSequences.NoteOn(0.0, 1, Key),
            MpeSequences.NoteOff(0.2, 1, Key, 127),
            MpeSequences.NoteOn(0.6, 1, Key),
            MpeSequences.NoteOff(0.8, 1, Key, 4),
        ]);

        //Act
        var audio = fixtures.RenderSfz(sequence, 1.5);

        //Assert
        var loud = MpeEngineFixtures.Rms(audio, (int)(0.25 * MpeEngineFixtures.SampleRate), 4096);
        var quiet = MpeEngineFixtures.Rms(audio, (int)(0.85 * MpeEngineFixtures.SampleRate), 4096);

        loud.Should().BeGreaterThan(0.1);
        quiet.Should().BeLessThan(0.02);
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
        fixtures.RenderSfz(sequence, 1.0, synthesizer => synthesizer.MpeMode = MpeMode.LowerZone);

        //Assert
        // Channels are 0-based on the synthesizer's own surface, as MIDI messages carry them.
        fixtures.LastSfzSynthesizer.ReleaseVelocity(1, Key).Should().Be(96);
        fixtures.LastSfzSynthesizer.ReleaseVelocity(2, Key).Should().Be(11);
        fixtures.LastSfzSynthesizer.ReleaseVelocity(3, Key).Should().Be(0);
    }

    [Fact]
    public void the_synthesizer_takes_its_mpe_configuration_from_its_settings()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create();

        var settings = new SfzSynthesizerSettings(MpeEngineFixtures.SampleRate)
        {
            MpeMode = MpeMode.Both,
            MpeMemberBendRange = 36.0,
            MpeLowerZoneMemberCount = 5,
            MpeUpperZoneMemberCount = 4,
        };

        //Act
        var synthesizer = new SfzSynthesizer(fixtures.SfzInstrument, settings);

        //Assert
        synthesizer.MpeMode.Should().Be(MpeMode.Both);
        synthesizer.MpeMemberBendRange.Should().Be(36.0);
        synthesizer.MpeLowerZone.MemberCount.Should().Be(5);
        synthesizer.MpeUpperZone.MemberCount.Should().Be(4);
        synthesizer.MpeUpperZone.MemberBendRange.Should().Be(36.0);
    }

    [Fact]
    public void mpe_mode_off_renders_an_expressive_performance_exactly_as_the_engine_always_has()
    {
        //Arrange
        using var fixtures = MpeEngineFixtures.Create();
        var sequence = MpeSequences.Sequence(MpeEngineFixtures.ExpressivePerformance(Key));

        //Act
        var audio = fixtures.RenderSfzStereo(sequence, 3.0);

        //Assert
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
        var configured = fixtures.RenderSfzStereo(
            MpeSequences.Sequence(MpeEngineFixtures.ExpressivePerformance(Key)), 3.0, synthesizer =>
            {
                synthesizer.MpeMemberBendRange = 96.0;
                synthesizer.MpeLowerZoneMemberCount = 15;
                synthesizer.MpeUpperZoneMemberCount = 15;
                synthesizer.MpeMode = MpeMode.Auto;
                synthesizer.MpeMode = MpeMode.Off;
            });

        var untouched = fixtures.RenderSfzStereo(
            MpeSequences.Sequence(MpeEngineFixtures.ExpressivePerformance(Key)), 3.0);

        //Assert
        // This one needs no pinned numbers and no tolerance. The claim is that the settings do not
        // reach the render, and two renders on the SAME machine settle that bit for bit - which is
        // both a stricter test than a pinned digest and one that cannot care what platform it is on.
        MpeEngineFixtures.Digest(configured.Left, configured.Right)
            .Should().Be(MpeEngineFixtures.Digest(untouched.Left, untouched.Right));
    }
}
