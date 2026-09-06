using System.Collections.Generic;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Sequencing;

/// <summary>
/// The arpeggiator running inside the synthesizer: it consumes what is held, emits its own notes at
/// the rate its attributes ask for, and answers to every one of them live.
/// </summary>
public class DecentSamplerArpeggiatorTests
{
    [Fact]
    public void an_armed_arpeggiator_consumes_the_note_and_plays_its_own()
    {
        //Arrange - a quarter-note arpeggio, one note held, so every step is the same note.
        using var world = SequencingWorld.Build(Preset());

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        var onsets = SequencingWorld.Onsets(world.RenderSeconds(1.9));

        //Assert - a quarter note at 120 BPM is 22 050 frames.
        onsets.Should().HaveCount(4);
        Expect(onsets, 0, 22050, 44100, 66150);
    }

    [Fact]
    public void the_sync_division_sets_the_step()
    {
        //Arrange
        using var world = SequencingWorld.Build(Preset(division: "noteOneEighth"));

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        var onsets = SequencingWorld.Onsets(world.RenderSeconds(0.95));

        //Assert - an eighth note at 120 BPM is 11 025 frames.
        onsets.Should().HaveCount(4);
        Expect(onsets, 0, 11025, 22050, 33075);
    }

    [Fact]
    public void the_tempo_source_moves_the_steps()
    {
        //Arrange
        using var world = SequencingWorld.Build(Preset());
        world.Synthesizer.TempoSource.BeatsPerMinute = 60.0;

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        var onsets = SequencingWorld.Onsets(world.RenderSeconds(2.9));

        //Assert - a quarter note at 60 BPM is a whole second.
        onsets.Should().HaveCount(3);
        Expect(onsets, 0, 44100, 88200);
    }

    [Fact]
    public void arp_override_bpm_replaces_the_transport_tempo()
    {
        //Arrange
        using var world = SequencingWorld.Build(
            Preset(extra: """arpFollowGlobalTempo="false" arpOverrideBpm="60" """));
        world.Synthesizer.TempoSource.BeatsPerMinute = 240.0;

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        var onsets = SequencingWorld.Onsets(world.RenderSeconds(2.9));

        //Assert - the transport is ignored: a quarter note at 60 BPM is a whole second.
        onsets.Should().HaveCount(3);
        Expect(onsets, 0, 44100, 88200);
    }

    [Fact]
    public void arp_rate_multiplier_plays_faster()
    {
        //Arrange
        using var world = SequencingWorld.Build(Preset(extra: """arpRateMultiplier="2" """));

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        var onsets = SequencingWorld.Onsets(world.RenderSeconds(0.95));

        //Assert - twice the speed, so a quarter note every 11 025 frames.
        onsets.Should().HaveCount(4);
        Expect(onsets, 0, 11025, 22050, 33075);
    }

    [Fact]
    public void arp_gate_length_is_a_fraction_of_the_step()
    {
        //Arrange
        using var half = SequencingWorld.Build(Preset(group: SequencingWorld.SustainGroup(), gate: "0.5"));
        using var quarter = SequencingWorld.Build(
            Preset(group: SequencingWorld.SustainGroup(), gate: "0.25"));

        //Act
        half.Synthesizer.NoteOn(0, 60, 100);
        quarter.Synthesizer.NoteOn(0, 60, 100);

        var halfEnds = SequencingWorld.Offsets(half.RenderSeconds(0.9));
        var quarterEnds = SequencingWorld.Offsets(quarter.RenderSeconds(0.9));

        //Assert - a note-off lands at the start of the block it is due in, so a block of tolerance.
        halfEnds[0].Should().BeInRange(11025 - 128, 11025 + 128);
        quarterEnds[0].Should().BeInRange(5512 - 128, 5512 + 128);
    }

    [Fact]
    public void the_octave_range_reaches_the_higher_keys()
    {
        //Arrange - the keyed group tells the octaves apart by level.
        using var world = SequencingWorld.Build(
            $$"""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" attack="0" decay="0" sustain="1" release="0.001">
                  <sample path="Samples/blip.wav" rootNote="60" loNote="60" hiNote="60"
                          pitchKeyTrack="0" volume="0.2" />
                  <sample path="Samples/blip.wav" rootNote="72" loNote="72" hiNote="72"
                          pitchKeyTrack="0" volume="0.6" />
                </group>
              </groups>
              <arpeggiator enabled="true" arpOrder="up" arpOctaveRange="2"
                           arpSyncDivision="noteOneFourth" arpGateLength="0.5" />
            </DecentSampler>
            """);

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        var render = world.RenderSeconds(1.9);
        var onsets = SequencingWorld.Onsets(render);

        //Assert - the held note, then its octave, then round again.
        onsets.Should().HaveCount(4);
        SequencingWorld.LevelAt(render, onsets[0])
            .Should().BeApproximately(SequencingWorld.BlipLevel * 0.2, 0.01);
        SequencingWorld.LevelAt(render, onsets[1])
            .Should().BeApproximately(SequencingWorld.BlipLevel * 0.6, 0.01);
        SequencingWorld.LevelAt(render, onsets[2])
            .Should().BeApproximately(SequencingWorld.BlipLevel * 0.2, 0.01);
    }

    [Fact]
    public void releasing_the_chord_stops_the_stream()
    {
        //Arrange
        using var world = SequencingWorld.Build(Preset());

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        var held = SequencingWorld.Onsets(world.RenderSeconds(1.2));

        world.Synthesizer.NoteOff(0, 60);
        var released = SequencingWorld.Onsets(world.RenderSeconds(1.2));

        //Assert
        held.Should().HaveCount(3);
        released.Should().BeEmpty();
        world.Synthesizer.Sequencing.Arpeggiator.HeldNoteCount.Should().Be(0);
    }

    [Fact]
    public void enabling_it_in_the_middle_of_a_held_chord_takes_the_chord_over()
    {
        //Arrange - a key switch arms the arpeggiator.
        using var world = SequencingWorld.Build(
            $$"""
            <DecentSampler>
            {{SequencingWorld.SustainGroup()}}
              <midi>
                <note note="24" swallowNotes="true">
                  <binding type="arpeggiator" level="instrument" parameter="ARP_ENABLED"
                           translation="fixed_value" translationValue="true" />
                </note>
              </midi>
              <arpeggiator enabled="false" arpOrder="up" arpOctaveRange="1"
                           arpSyncDivision="noteOneFourth" arpGateLength="0.5" />
            </DecentSampler>
            """);

        //Act - the note sounds straight through while the arpeggiator is off.
        world.Synthesizer.NoteOn(0, 60, 100);
        var plain = SequencingWorld.Onsets(world.RenderSeconds(0.5));

        world.Synthesizer.NoteOn(0, 24, 100);
        var arpeggiated = SequencingWorld.Onsets(world.RenderSeconds(1.2));

        //Assert
        plain.Should().HaveCount(1);
        world.Synthesizer.Sequencing.Arpeggiator.HeldNoteCount.Should().Be(1);
        arpeggiated.Should().HaveCount(3);
    }

    [Fact]
    public void disabling_it_releases_the_notes_it_was_playing()
    {
        //Arrange
        using var world = SequencingWorld.Build(
            $$"""
            <DecentSampler>
            {{SequencingWorld.SustainGroup()}}
              <midi>
                <note note="24" swallowNotes="true">
                  <binding type="arpeggiator" level="instrument" parameter="ARP_ENABLED"
                           translation="fixed_value" translationValue="false" />
                </note>
              </midi>
              <arpeggiator enabled="true" arpOrder="up" arpOctaveRange="1"
                           arpSyncDivision="noteOneFourth" arpGateLength="4" />
            </DecentSampler>
            """);

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        world.RenderSeconds(0.3);
        var sounding = world.Synthesizer.ActiveVoiceCount;

        world.Synthesizer.NoteOn(0, 24, 100);
        world.RenderSeconds(0.3);

        //Assert
        sounding.Should().BeGreaterThan(0);
        world.Synthesizer.ActiveVoiceCount.Should().Be(0);
        world.Synthesizer.Sequencing.Arpeggiator.HeldNoteCount.Should().Be(0);
    }

    [Fact]
    public void a_modulated_rate_multiplier_changes_the_step()
    {
        //Arrange - a MIDI CC modulator drives ARP_RATE_MULTIPLIER from 1 to 2.
        using var world = SequencingWorld.Build(
            $$"""
            <DecentSampler>
            {{SequencingWorld.BlipGroup()}}
              <modulators>
                <midiCC number="21" scope="global" modBehavior="set" modAmount="1">
                  <binding type="arpeggiator" level="instrument" parameter="ARP_RATE_MULTIPLIER"
                           translation="linear" translationOutputMin="1" translationOutputMax="2" />
                </midiCC>
              </modulators>
              <arpeggiator enabled="true" arpOrder="up" arpOctaveRange="1"
                           arpSyncDivision="noteOneFourth" arpGateLength="0.5" />
            </DecentSampler>
            """);

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        var slow = SequencingWorld.Onsets(world.RenderSeconds(1.2));

        world.Synthesizer.ProcessMidiMessage(0, 0xB0, 21, 127);
        var fast = SequencingWorld.Onsets(world.RenderSeconds(1.2));

        //Assert - quarter notes become eighths. The step ALREADY BOOKED keeps its old length, because
        // the arpeggiator computes each interval when its step fires; every one after it is halved.
        slow.Should().HaveCount(3);
        (slow[1] - slow[0]).Should().BeInRange(22050 - 1, 22050 + 1);
        (fast[2] - fast[1]).Should().BeInRange(11025 - 1, 11025 + 1);
    }

    [Fact]
    public void the_channel_of_the_originating_note_travels_with_the_arpeggios()
    {
        //Arrange
        using var world = SequencingWorld.Build(
            Preset(group: SequencingWorld.SustainGroup(), gate: "4"));

        //Act - the chord is held on channel 3, so its arpeggios sound there too.
        world.Synthesizer.NoteOn(3, 60, 100);
        world.RenderSeconds(0.3);
        var sounding = world.Synthesizer.ActiveVoiceCount;

        world.Synthesizer.NoteOffAll(0, true);
        world.Render(2);
        var afterWrongChannel = world.Synthesizer.ActiveVoiceCount;

        world.Synthesizer.NoteOffAll(3, true);
        world.Render(2);
        var afterRightChannel = world.Synthesizer.ActiveVoiceCount;

        //Assert
        sounding.Should().BeGreaterThan(0);
        afterWrongChannel.Should().Be(sounding);
        afterRightChannel.Should().Be(0);
    }

    [Fact]
    public void an_arpeggiated_note_does_not_run_the_midi_handlers_again()
    {
        //Arrange - the arpeggiator's octave copy is note 72; a handler on 72 must not fire.
        using var world = SequencingWorld.Build(
            $$"""
            <DecentSampler>
            {{SequencingWorld.BlipGroup()}}
              <midi>
                <note note="72" eventType="note_on">
                  <binding type="general" level="group" groupIndex="0" parameter="ENABLED"
                           translation="fixed_value" translationValue="false" />
                </note>
              </midi>
              <arpeggiator enabled="true" arpOrder="up" arpOctaveRange="2"
                           arpSyncDivision="noteOneFourth" arpGateLength="0.5" />
            </DecentSampler>
            """);

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        world.RenderSeconds(1.2);

        //Assert
        world.Instrument.Groups[0].Enabled.Should().BeTrue();
    }

    [Fact]
    public void a_disarmed_arpeggiator_leaves_the_notes_alone()
    {
        //Arrange
        using var world = SequencingWorld.Build(Preset(enabled: "false"));

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        var render = world.RenderSeconds(1.2);

        //Assert - one plain note, and nothing generated.
        SequencingWorld.Onsets(render).Should().HaveCount(1);
        world.Synthesizer.Sequencing.Arpeggiator.HeldNoteCount.Should().Be(0);
    }

    [Fact]
    public void an_all_notes_off_binding_stops_the_arpeggiator()
    {
        //Arrange
        using var world = SequencingWorld.Build(
            $$"""
            <DecentSampler>
            {{SequencingWorld.SustainGroup()}}
              <midi>
                <note note="24" swallowNotes="true">
                  <binding type="general" level="instrument" parameter="ALL_NOTES_OFF"
                           translation="fixed_value" translationValue="true" />
                </note>
              </midi>
              <arpeggiator enabled="true" arpOrder="up" arpOctaveRange="1"
                           arpSyncDivision="noteOneFourth" arpGateLength="0.5" />
            </DecentSampler>
            """);

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        world.RenderSeconds(0.3);

        world.Synthesizer.NoteOn(0, 24, 100);
        var after = SequencingWorld.Onsets(world.RenderSeconds(1.2));

        //Assert
        after.Should().BeEmpty();
        world.Synthesizer.Sequencing.Arpeggiator.HeldNoteCount.Should().Be(0);
    }

    [Fact]
    public void the_random_orders_are_reproducible_from_the_seed()
    {
        //Arrange
        using var first = SequencingWorld.Build(Preset(order: "random"));
        using var second = SequencingWorld.Build(Preset(order: "random"));

        //Act
        first.Synthesizer.NoteOn(0, 60, 100);
        first.Synthesizer.NoteOn(0, 64, 100);
        second.Synthesizer.NoteOn(0, 60, 100);
        second.Synthesizer.NoteOn(0, 64, 100);

        var one = first.RenderSeconds(3.9);
        var other = second.RenderSeconds(3.9);

        //Assert
        SequencingWorld.Onsets(one).Should().Equal(SequencingWorld.Onsets(other));
        DecentSamplerRenderProbe.Rms(one).Should().BeApproximately(DecentSamplerRenderProbe.Rms(other), 1e-9);
    }

    [Fact]
    public void the_first_step_fires_immediately_on_the_key_down()
    {
        //Arrange
        // MEASURED (round 2, item 30): the first step arrives 2 to 12 ms after the key-down at every
        // phase of the grid, not on the next grid boundary, and the grid runs from there.
        using var world = SequencingWorld.Build(Preset(group: SequencingWorld.SustainGroup()));

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        var onsets = SequencingWorld.Onsets(world.RenderSeconds(1.2));

        //Assert
        var rate = DecentSamplerEngineFixtures.SampleRate;
        onsets[0].Should().BeLessThan(rate / 64);
        Expect(onsets, onsets[0], onsets[0] + (rate / 2), onsets[0] + rate);
    }

    [Fact]
    public void the_step_already_started_runs_its_whole_gate_after_the_last_key_lifts()
    {
        //Arrange
        // MEASURED (round 2, item 30): a chord released mid-step still let the note that had already
        // begun ring its full 0.261 s gate; only the NEXT step was never scheduled.
        using var world = SequencingWorld.Build(
            Preset(gate: "0.5", group: SequencingWorld.SustainGroup()));

        var rate = DecentSamplerEngineFixtures.SampleRate;

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        var before = world.RenderSeconds(0.1);

        world.Synthesizer.NoteOff(0, 60);
        var after = world.RenderSeconds(1.2);

        //Assert
        // The step began at frame 0 and its gate is half of a half-second step, so it is still
        // sounding a twentieth of a second after the key came up, silent by three tenths, and the
        // step that would have followed at half a second never arrives.
        SequencingWorld.Onsets(before).Should().HaveCount(1);
        SequencingWorld.LevelAt(after, rate / 20).Should().BeGreaterThan(0.02);
        SequencingWorld.LevelAt(after, (int)(rate * 0.3)).Should().BeLessThan(0.01);
        SequencingWorld.LevelAt(after, (int)(rate * 0.55)).Should().BeLessThan(0.01);
        SequencingWorld.LevelAt(after, rate).Should().BeLessThan(0.01);
    }

    [Fact]
    public void the_position_register_survives_between_chords()
    {
        //Arrange
        // MEASURED (round 2, item 30): the pattern's starting position is NOT always the lowest held
        // note - two of three chords began on the middle one - so a position register survives the
        // gap between them. Here three keys each play at their own level, so the level of the first
        // step says which position the second chord started on.
        using var world = SequencingWorld.Build(
            Preset(group: SequencingWorld.KeyedBlipGroup(60, 3)));

        //Act
        world.Synthesizer.NoteOn(0, 60, 100);
        world.Synthesizer.NoteOn(0, 61, 100);
        world.Synthesizer.NoteOn(0, 62, 100);
        var first = world.RenderSeconds(1.2);

        world.Synthesizer.NoteOffAll(immediate: true);
        world.RenderSeconds(0.2);

        world.Synthesizer.NoteOn(0, 60, 100);
        world.Synthesizer.NoteOn(0, 61, 100);
        world.Synthesizer.NoteOn(0, 62, 100);
        var second = world.RenderSeconds(1.2);

        //Assert
        var firstOnsets = SequencingWorld.Onsets(first);
        var secondOnsets = SequencingWorld.Onsets(second);

        firstOnsets.Should().HaveCount(3);
        secondOnsets.Should().HaveCount(3);

        // Three steps ran, so the register is back where it started only if it were reset; it is not,
        // so the second chord opens on the position the first one left it at - which after three
        // steps of a three-note pattern is the same one. The discriminator is a chord released after
        // TWO steps, below.
        SequencingWorld.LevelAt(second, secondOnsets[0])
            .Should().BeApproximately(SequencingWorld.LevelAt(first, firstOnsets[0]), 0.01);
    }

    [Fact]
    public void a_chord_released_mid_pattern_leaves_the_register_where_it_was()
    {
        //Arrange
        using var world = SequencingWorld.Build(
            Preset(group: SequencingWorld.KeyedBlipGroup(60, 3)));

        //Act - two steps, then a new chord, which must open on the THIRD position.
        world.Synthesizer.NoteOn(0, 60, 100);
        world.Synthesizer.NoteOn(0, 61, 100);
        world.Synthesizer.NoteOn(0, 62, 100);
        var first = world.RenderSeconds(0.7);

        world.Synthesizer.NoteOffAll(immediate: true);
        world.RenderSeconds(0.2);

        world.Synthesizer.NoteOn(0, 60, 100);
        world.Synthesizer.NoteOn(0, 61, 100);
        world.Synthesizer.NoteOn(0, 62, 100);
        var second = world.RenderSeconds(0.4);

        //Assert
        var firstOnsets = SequencingWorld.Onsets(first);
        var secondOnsets = SequencingWorld.Onsets(second);

        firstOnsets.Should().HaveCount(2);
        secondOnsets.Should().HaveCount(1);

        // The first chord played positions 0 and 1; the second opens on position 2, whose key plays
        // at three tenths of the blip against the first step's one tenth.
        SequencingWorld.LevelAt(second, secondOnsets[0])
            .Should().BeGreaterThan(SequencingWorld.LevelAt(first, firstOnsets[0]) * 1.5);
    }

    private static void Expect(IReadOnlyList<int> onsets, params int[] expected)
    {
        for (var index = 0; index < expected.Length; index++)
        {
            onsets[index].Should().BeInRange(expected[index] - 1, expected[index] + 1);
        }
    }

    // One blip zone across the keyboard, arpeggiated in quarter notes so a step is easy to read.
    private static string Preset(
        string enabled = "true",
        string order = "up",
        string division = "noteOneFourth",
        string gate = "0.5",
        string extra = "",
        string group = null) =>
        $$"""
        <DecentSampler>
        {{group ?? SequencingWorld.BlipGroup()}}
          <arpeggiator enabled="{{enabled}}" arpOrder="{{order}}" arpOctaveRange="1"
                       arpSyncDivision="{{division}}" arpGateLength="{{gate}}" {{extra}}/>
        </DecentSampler>
        """;
}
