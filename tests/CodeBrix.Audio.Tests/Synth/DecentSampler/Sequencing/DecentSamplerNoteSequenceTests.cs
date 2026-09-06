using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Sequencing;

/// <summary>
/// The note sequencer: when its notes land, which note each loop mode chooses, and what the binding's
/// SEQ_* attributes do to them.
/// </summary>
/// <remarks>
/// Timing is measured to the FRAME. A generated note carries a sub-block offset into the voice
/// runtime, so a sequence lands on its beat rather than on the block boundary that follows it; every
/// assertion here allows one frame for the rounding of a fractional beat position.
/// </remarks>
public class DecentSamplerNoteSequenceTests
{
    [Fact]
    public void a_sequence_plays_its_notes_on_the_beat()
    {
        //Arrange - four quarter notes; one beat at 120 BPM is half a second.
        using var world = SequencingWorld.Build(Preset());

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var onsets = SequencingWorld.Onsets(world.RenderSeconds(1.9));

        //Assert
        onsets.Should().HaveCount(4);
        Expect(onsets, 0, 22050, 44100, 66150);
    }

    [Fact]
    public void the_tempo_source_moves_the_beats()
    {
        //Arrange - at 60 BPM a beat is a whole second.
        using var world = SequencingWorld.Build(Preset());
        world.Synthesizer.TempoSource.BeatsPerMinute = 60.0;

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var onsets = SequencingWorld.Onsets(world.RenderSeconds(2.9));

        //Assert
        onsets.Should().HaveCount(3);
        Expect(onsets, 0, 44100, 88200);
    }

    [Fact]
    public void seq_follow_global_tempo_false_holds_the_sequence_at_120()
    {
        //Arrange
        using var world = SequencingWorld.Build(Preset(binding: """seqFollowGlobalTempo="false" """));
        world.Synthesizer.TempoSource.BeatsPerMinute = 60.0;

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var onsets = SequencingWorld.Onsets(world.RenderSeconds(1.9));

        //Assert - still half a second a beat, whatever the transport says.
        onsets.Should().HaveCount(4);
        Expect(onsets, 0, 22050, 44100, 66150);
    }

    [Fact]
    public void seq_playback_rate_scales_the_speed()
    {
        //Arrange
        using var world = SequencingWorld.Build(Preset(binding: """seqPlaybackRate="2" """));

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var onsets = SequencingWorld.Onsets(world.RenderSeconds(0.95));

        //Assert - twice the speed, so a quarter note every 11 025 frames.
        onsets.Should().HaveCount(4);
        Expect(onsets, 0, 11025, 22050, 33075);
    }

    [Fact]
    public void the_sequences_own_rate_scales_the_speed()
    {
        //Arrange
        using var world = SequencingWorld.Build(Preset(rate: "4"));

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var onsets = SequencingWorld.Onsets(world.RenderSeconds(0.48));

        //Assert
        onsets.Should().HaveCount(4);
        Expect(onsets, 0, 5512, 11025, 16537);
    }

    [Fact]
    public void forward_walks_the_notes_in_order_and_loops()
    {
        //Arrange
        using var world = SequencingWorld.Build(Preset());

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var render = world.RenderSeconds(3.9);

        //Assert - four notes, then the same four again.
        Keys(render).Should().Equal(0, 1, 2, 3, 0, 1, 2, 3);
    }

    [Fact]
    public void reverse_plays_the_first_note_and_then_walks_backwards_from_the_end()
    {
        //Arrange
        // MEASURED (round 3, item 40): reverse gave 60 72 67 64 | 60 72 67 64 on a sequence written
        // 60 64 67 72 - note 0 first, then backwards from the end. It is not a plain reversal.
        using var world = SequencingWorld.Build(Preset(binding: """seqLoopMode="reverse" """));

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var render = world.RenderSeconds(3.9);

        //Assert
        Keys(render).Should().Equal(0, 3, 2, 1, 0, 3, 2, 1);
    }

    [Fact]
    public void no_loop_plays_the_sequence_once()
    {
        //Arrange
        using var world = SequencingWorld.Build(Preset(binding: """seqLoopMode="no_loop" """));

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var render = world.RenderSeconds(3.9);

        //Assert
        Keys(render).Should().Equal(0, 1, 2, 3);
    }

    [Fact]
    public void random_draws_from_the_sequence_and_is_reproducible()
    {
        //Arrange
        using var first = SequencingWorld.Build(Preset(binding: """seqLoopMode="random" """));
        using var second = SequencingWorld.Build(Preset(binding: """seqLoopMode="random" """));

        //Act
        first.Synthesizer.NoteOn(0, 24, 100);
        second.Synthesizer.NoteOn(0, 24, 100);

        var one = Keys(first.RenderSeconds(7.9));
        var other = Keys(second.RenderSeconds(7.9));

        //Assert - the same seed gives the same performance, and every draw is a note of the sequence.
        one.Should().HaveCount(16);
        one.Should().Equal(other);
        one.Should().AllSatisfy(key => key.Should().BeInRange(0, 3));
    }

    [Fact]
    public void random_no_repeat_never_plays_the_same_note_twice_running()
    {
        //Arrange
        using var world = SequencingWorld.Build(
            Preset(binding: """seqLoopMode="random_no_repeat" """));

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var keys = Keys(world.RenderSeconds(7.9));

        //Assert
        keys.Should().HaveCount(16);

        for (var index = 1; index < keys.Count; index++)
        {
            keys[index].Should().NotBe(keys[index - 1]);
        }
    }

    [Fact]
    public void seq_transpose_shifts_every_note()
    {
        //Arrange - the sequence writes notes 60 to 63; a transposition of 2 makes them 62 to 65.
        using var world = SequencingWorld.Build(Preset(binding: """seqTranspose="2" """));

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var keys = Keys(world.RenderSeconds(1.9));

        //Assert
        keys.Should().Equal(2, 3, 4, 5);
    }

    [Fact]
    public void seq_transpose_with_root_note_shifts_by_the_triggering_key()
    {
        //Arrange - the guide's own arithmetic: root 24, key 27, so up three semitones.
        using var world = SequencingWorld.Build(
            Preset(note: "24-27", binding: """seqTransposeWithRootNote="24" """));

        //Act
        world.Synthesizer.NoteOn(0, 27, 100);
        var keys = Keys(world.RenderSeconds(1.9));

        //Assert
        keys.Should().Equal(3, 4, 5, 6);
    }

    [Fact]
    public void seq_track_midi_input_velocity_scales_the_note_velocities()
    {
        //Arrange - one group whose level follows velocity, so the emitted velocity is measurable.
        var preset =
            $$"""
            <DecentSampler>
            {{SequencingWorld.BlipGroup().Replace("ampVelTrack=\"0\"", "ampVelTrack=\"1\"")}}
              <midi>
                <note note="24" swallowNotes="true">
                  <binding level="instrument" type="note_sequence" seqIndex="0"
                           seqTriggerBehavior="midi_key" seqTrackMidiInputVelocity="1" />
                </note>
                <note note="25" swallowNotes="true">
                  <binding level="instrument" type="note_sequence" seqIndex="0"
                           seqTriggerBehavior="midi_key" seqTrackMidiInputVelocity="0" />
                </note>
              </midi>
              <noteSequences>
                <sequence name="quarters" length="4" rate="1">
                  <note position="0" velocity="1" note="60" length="0.2" />
                </sequence>
              </noteSequences>
            </DecentSampler>
            """;

        using var world = SequencingWorld.Build(preset);

        //Act
        world.Synthesizer.NoteOn(0, 24, 64);
        var tracked = world.RenderSeconds(0.025);

        world.Synthesizer.NoteOff(0, 24);
        world.Synthesizer.NoteOn(0, 25, 64);
        var untracked = world.RenderSeconds(0.025);

        //Assert - tracking on scales the sequence note's full velocity by 64/127.
        SequencingWorld.LevelAt(tracked, 0)
            .Should().BeApproximately(SequencingWorld.BlipLevel * (64.0 / 127.0), 0.01);
        SequencingWorld.LevelAt(untracked, 0)
            .Should().BeApproximately(SequencingWorld.BlipLevel, 0.01);
    }

    [Fact]
    public void midi_key_stops_the_sequence_when_the_key_comes_up()
    {
        //Arrange
        using var world = SequencingWorld.Build(Preset());

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var running = world.RenderSeconds(1.2);

        world.Synthesizer.NoteOff(0, 24);
        var stopped = world.RenderSeconds(1.2);

        //Assert
        SequencingWorld.Onsets(running).Should().HaveCount(3);
        SequencingWorld.Onsets(stopped).Should().BeEmpty();
    }

    [Fact]
    public void two_keys_with_no_identifier_run_two_players_at_once()
    {
        //Arrange - the handler covers a range, and each key gets its own player.
        using var world = SequencingWorld.Build(
            Preset(note: "24-27", binding: """seqTransposeWithRootNote="24" """));

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        world.Synthesizer.NoteOn(0, 26, 100);
        world.RenderSeconds(0.02);

        //Assert - two players, and both are sounding a note.
        world.Synthesizer.Sequencing.Sequences.RunningPlayerCount.Should().Be(2);
        world.Synthesizer.ActiveVoiceCount.Should().Be(2);
    }

    [Fact]
    public void a_player_identifier_lets_one_binding_stop_another()
    {
        //Arrange - the guide's button example: an "on" state and an "off" state sharing an identifier.
        using var world = SequencingWorld.Build(
            $$"""
            <DecentSampler>
            {{SequencingWorld.KeyedBlipGroup(60, 4)}}
              <ui>
                <tab>
                  <button x="0" y="0" width="50" height="20" value="0">
                    <state name="Off">
                      <binding level="instrument" type="note_sequence" seqIndex="0"
                               seqTriggerBehavior="off" seqPlayerIdentifier="button_1" />
                    </state>
                    <state name="On">
                      <binding level="instrument" type="note_sequence" seqIndex="0"
                               seqTriggerBehavior="on" seqPlayerIdentifier="button_1" />
                    </state>
                  </button>
                </tab>
              </ui>
              <noteSequences>
                <sequence name="quarters" length="4" rate="1">
                  <note position="0" velocity="1" note="60" length="0.2" />
                  <note position="1" velocity="1" note="61" length="0.2" />
                  <note position="2" velocity="1" note="62" length="0.2" />
                  <note position="3" velocity="1" note="63" length="0.2" />
                </sequence>
              </noteSequences>
            </DecentSampler>
            """);

        //Act
        var silent = world.RenderSeconds(1.2);

        world.Instrument.Controls[0].Select(1);
        var playing = world.RenderSeconds(1.2);

        world.Instrument.Controls[0].Select(0);
        var stopped = world.RenderSeconds(1.2);

        //Assert
        SequencingWorld.Onsets(silent).Should().BeEmpty();
        SequencingWorld.Onsets(playing).Should().HaveCount(3);
        SequencingWorld.Onsets(stopped).Should().BeEmpty();
    }

    [Fact]
    public void a_sequence_note_goes_through_the_midi_handlers()
    {
        //Arrange - the sequence plays note 60, and a handler on 60 silences the group.
        using var world = SequencingWorld.Build(
            $$"""
            <DecentSampler>
            {{SequencingWorld.BlipGroup()}}
              <midi>
                <note note="24" swallowNotes="true">
                  <binding level="instrument" type="note_sequence" seqIndex="0"
                           seqTriggerBehavior="midi_key" />
                </note>
                <note note="60" eventType="note_on">
                  <binding type="general" level="group" groupIndex="0" parameter="ENABLED"
                           translation="fixed_value" translationValue="false" />
                </note>
              </midi>
              <noteSequences>
                <sequence name="one" length="4" rate="1">
                  <note position="0" velocity="1" note="60" length="0.2" />
                </sequence>
              </noteSequences>
            </DecentSampler>
            """);

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        world.RenderSeconds(0.02);

        //Assert
        world.Instrument.Groups[0].Enabled.Should().BeFalse();
    }

    [Fact]
    public void the_sequence_rate_is_bindable_while_it_plays()
    {
        //Arrange - a controller drives the sequence's RATE from 1 to 2.
        using var world = SequencingWorld.Build(
            $$"""
            <DecentSampler>
            {{SequencingWorld.KeyedBlipGroup(60, 4)}}
              <midi>
                <note note="24" swallowNotes="true">
                  <binding level="instrument" type="note_sequence" seqIndex="0"
                           seqTriggerBehavior="midi_key" />
                </note>
                <cc number="20">
                  <binding level="instrument" type="note_sequence" seqIndex="0" parameter="RATE"
                           translation="linear" translationOutputMin="1" translationOutputMax="2" />
                </cc>
              </midi>
              <noteSequences>
                <sequence name="quarters" length="4" rate="1">
                  <note position="0" velocity="1" note="60" length="0.2" />
                  <note position="1" velocity="1" note="61" length="0.2" />
                  <note position="2" velocity="1" note="62" length="0.2" />
                  <note position="3" velocity="1" note="63" length="0.2" />
                </sequence>
              </noteSequences>
            </DecentSampler>
            """);

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var slow = SequencingWorld.Onsets(world.RenderSeconds(1.2));

        world.Synthesizer.ProcessMidiMessage(0, 0xB0, 20, 127);
        var fast = SequencingWorld.Onsets(world.RenderSeconds(1.2));

        //Assert - three quarter notes in 1.2 s at rate 1, five at rate 2.
        slow.Should().HaveCount(3);
        fast.Should().HaveCount(5);
    }

    [Fact]
    public void a_key_switch_can_swap_the_sequence_a_note_binding_plays()
    {
        //Arrange - key 20 rewrites the seqIndex of the binding under the key switch on 24.
        using var world = SequencingWorld.Build(
            $$"""
            <DecentSampler>
            {{SequencingWorld.KeyedBlipGroup(60, 6)}}
              <midi>
                <note note="20" swallowNotes="true">
                  <binding type="note_binding" level="midi" midiElementIndex="1" bindingIndex="0"
                           parameter="SEQ_INDEX" translation="fixed_value" translationValue="1" />
                </note>
                <note note="24" swallowNotes="true">
                  <binding level="instrument" type="note_sequence" seqIndex="0"
                           seqTriggerBehavior="midi_key" />
                </note>
              </midi>
              <noteSequences>
                <sequence name="low" length="4" rate="1">
                  <note position="0" velocity="1" note="60" length="0.2" />
                </sequence>
                <sequence name="high" length="4" rate="1">
                  <note position="0" velocity="1" note="63" length="0.2" />
                </sequence>
              </noteSequences>
            </DecentSampler>
            """);

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var before = Keys(world.RenderSeconds(0.03));

        world.Synthesizer.NoteOff(0, 24);
        world.Synthesizer.NoteOn(0, 20, 100);
        world.Synthesizer.NoteOn(0, 24, 100);
        var after = Keys(world.RenderSeconds(0.03));

        //Assert
        before.Should().Equal(0);
        after.Should().Equal(3);
    }

    [Fact]
    public void a_sequence_with_no_running_player_costs_nothing()
    {
        //Arrange
        using var world = SequencingWorld.Build(Preset());

        //Act
        var render = world.RenderSeconds(0.5);

        //Assert
        SequencingWorld.Onsets(render).Should().BeEmpty();
        world.Synthesizer.Sequencing.Sequences.RunningPlayerCount.Should().Be(0);
    }

    // Which key of the keyed group each onset played, as an offset above the group's first key.
    private static IReadOnlyList<int> Keys(float[] render)
    {
        var onsets = SequencingWorld.Onsets(render);
        var keys = new List<int>(onsets.Count);

        foreach (var onset in onsets)
        {
            var level = SequencingWorld.LevelAt(render, onset);
            keys.Add((int)Math.Round(level / SequencingWorld.BlipLevel * 10.0) - 1);
        }

        return keys;
    }

    private static void Expect(IReadOnlyList<int> onsets, params int[] expected)
    {
        for (var index = 0; index < expected.Length; index++)
        {
            onsets[index].Should().BeInRange(expected[index] - 1, expected[index] + 1);
        }
    }

    // Four quarter notes on keys 60 to 63, triggered by a swallowed key switch.
    [Fact]
    public void fractional_positions_truncate_onto_the_same_beat()
    {
        //Arrange
        // MEASURED (round 3, item 40): a <note>'s position is TRUNCATED TO A WHOLE BEAT, so two notes
        // written at "0" and "0.5" sound SIMULTANEOUSLY - 3 dB louder than one note - rather than as a
        // rhythm. This is the single most surprising thing the item found.
        using var world = SequencingWorld.Build(CustomSequence(
            "4",
            """<note position="0" velocity="1" note="60" length="1" />""" +
            """<note position="0.5" velocity="1" note="60" length="1" />""" +
            """<note position="2" velocity="1" note="60" length="1" />"""));

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var render = world.RenderSeconds(1.9);
        var onsets = SequencingWorld.Onsets(render);

        //Assert
        onsets.Should().HaveCount(2);
        Expect(onsets, 0, 44100);

        // Two copies of the same note on the same beat sum to twice the level.
        var together = SequencingWorld.LevelAt(render, onsets[0]);
        var alone = SequencingWorld.LevelAt(render, onsets[1]);

        (together / alone).Should().BeApproximately(2.0, 0.15);
    }

    [Fact]
    public void a_note_written_at_or_beyond_the_declared_length_never_plays()
    {
        //Arrange
        // MEASURED (round 3, item 40): the declared length TRUNCATES the sequence.
        using var world = SequencingWorld.Build(CustomSequence(
            "2",
            """<note position="0" velocity="1" note="60" length="1" />""" +
            """<note position="1" velocity="1" note="61" length="1" />""" +
            """<note position="2" velocity="1" note="62" length="1" />""" +
            """<note position="3" velocity="1" note="63" length="1" />"""));

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var render = world.RenderSeconds(1.9);

        //Assert - two notes, looping over the declared two beats, and never the third or fourth.
        Keys(render).Should().Equal(0, 1, 0, 1);
    }

    [Fact]
    public void seq_transpose_with_root_note_overrides_seq_transpose()
    {
        //Arrange
        // MEASURED (round 3, item 40): a binding carrying both moved the sequence by
        // key-minus-root ONLY. They do not add.
        using var both = SequencingWorld.Build(
            Preset(binding: """seqTranspose="12" seqTransposeWithRootNote="22" """));
        using var rootOnly = SequencingWorld.Build(
            Preset(binding: """seqTransposeWithRootNote="22" """));
        using var transposeOnly = SequencingWorld.Build(
            Preset(binding: """seqTranspose="2" """));

        //Act - the trigger key is 24, two above the root, so both readings shift by two.
        both.Synthesizer.NoteOn(0, 24, 100);
        var withBoth = Keys(both.RenderSeconds(0.4));

        rootOnly.Synthesizer.NoteOn(0, 24, 100);
        var withRoot = Keys(rootOnly.RenderSeconds(0.4));

        transposeOnly.Synthesizer.NoteOn(0, 24, 100);
        var withTranspose = Keys(transposeOnly.RenderSeconds(0.4));

        //Assert - the two shift alike, and neither shifts by fourteen.
        withBoth[0].Should().Be(withRoot[0]);
        withBoth[0].Should().Be(withTranspose[0]);
    }

    [Fact]
    public void the_first_note_fires_on_the_key_down_however_late_it_is_written()
    {
        //Arrange
        // MEASURED (round 3, item 40): the first note of a midi_key sequence fires IMMEDIATELY on the
        // key-down - eight trigger phases spread over the 0.500 s beat all gave a latency of 3 to
        // 12 ms with no correlation to the phase, where a grid-locked start would have given 500, 363,
        // 229 and 87 ms - and the grid is sample-accurate from there. So a sequence whose first note
        // is written at beat 2 sounds AT ONCE and the rest of its grid moves with it.
        using var world = SequencingWorld.Build(CustomSequence(
            "4",
            """<note position="2" velocity="1" note="60" length="1" />""" +
            """<note position="3" velocity="1" note="61" length="1" />"""));

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var render = world.RenderSeconds(2.6);
        var onsets = SequencingWorld.Onsets(render);

        //Assert
        // Note 0 at the key-down, note 1 one beat later, then the wrap gap of
        // length - lastPosition + firstPosition = 4 - 3 + 2 = 3 beats back to note 0.
        Keys(render).Should().Equal(0, 1, 0, 1);
        Expect(onsets, 0, 22050, 88200, 110250);
    }

    [Fact]
    public void a_key_lift_cuts_the_sounding_sequence_note()
    {
        //Arrange
        // MEASURED (round 3, item 40): a sequence whose single note is four beats (2.0 s) long,
        // triggered by a key held for 1.0 s, produced ONE note sounding 0.9955 s. The note is cut at
        // the key release rather than running out its length, and no further step is scheduled.
        using var world = SequencingWorld.Build(
            $$"""
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" attack="0" decay="0" sustain="1" release="0.005">
                  <sample path="Samples/long.wav" rootNote="60" loNote="0" hiNote="127"
                          pitchKeyTrack="0" />
                </group>
              </groups>
              <midi>
                <note note="24" enabled="true" swallowNotes="true">
                  <binding enabled="true" level="instrument" type="note_sequence" seqIndex="0"
                           seqTriggerBehavior="midi_key" />
                </note>
              </midi>
              <noteSequences>
                <sequence name="one_long_note" length="4" rate="1">
                  <note position="0" velocity="1" note="60" length="4" />
                </sequence>
              </noteSequences>
            </DecentSampler>
            """);

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var held = world.RenderSeconds(1.0);

        world.Synthesizer.NoteOff(0, 24);
        var after = world.RenderSeconds(2.0);

        var render = new float[held.Length + after.Length];
        held.CopyTo(render, 0);
        after.CopyTo(render, held.Length);

        //Assert - one note, and it stops at the key lift rather than at its declared two seconds.
        SequencingWorld.Onsets(render).Should().HaveCount(1);

        var offsets = SequencingWorld.Offsets(render);
        offsets.Should().HaveCount(1);
        (offsets[0] / 44100.0).Should().BeApproximately(0.9955, 0.02);
    }

    [Theory]
    // MEASURED (round 3, item 40): the level of the emitted note, in dB relative to the same case at
    // trigger velocity 127, over three track values and three trigger velocities. The law is
    // emitted = 127 * noteVelocity * ((1 - seqTrackMidiInputVelocity) + track * triggerVelocity/127).
    [InlineData("1", 127, 0.00)]
    [InlineData("1", 64, -6.02)]
    [InlineData("1", 32, -12.04)]
    [InlineData("0.5", 127, 0.00)]
    [InlineData("0.5", 64, -2.45)]
    [InlineData("0.5", 32, -4.05)]
    [InlineData("0", 127, 0.00)]
    [InlineData("0", 64, 0.00)]
    [InlineData("0", 32, 0.00)]
    public void the_emitted_velocity_follows_the_measured_tracking_law(
        string track, int triggerVelocity, double expectedDecibels)
    {
        //Arrange - the zone follows velocity exactly, so its level reports the emitted velocity.
        using var world = SequencingWorld.Build(VelocityPreset(track));

        //Act
        world.Synthesizer.NoteOn(0, 24, triggerVelocity);
        var measured = SequencingWorld.LevelAt(world.RenderSeconds(0.025), 0);

        world.Synthesizer.NoteOffAll(true);
        world.Synthesizer.NoteOn(0, 24, 127);
        var reference = SequencingWorld.LevelAt(world.RenderSeconds(0.025), 0);

        //Assert
        var decibels = 20.0 * Math.Log10(measured / reference);
        decibels.Should().BeApproximately(expectedDecibels, 0.15);
    }

    [Fact]
    public void a_sequence_note_velocity_of_one_half_is_six_decibels_down()
    {
        //Arrange
        // MEASURED (round 3, item 40): a <note velocity="0.5"> measured exactly 6.02 dB below the same
        // note at velocity="1" at every trigger velocity, so the attribute is a plain linear scale on
        // the MIDI velocity rather than a curve.
        using var full = SequencingWorld.Build(VelocityPreset("0", noteVelocity: "1"));
        using var half = SequencingWorld.Build(VelocityPreset("0", noteVelocity: "0.5"));

        //Act
        full.Synthesizer.NoteOn(0, 24, 100);
        var loud = SequencingWorld.LevelAt(full.RenderSeconds(0.025), 0);

        half.Synthesizer.NoteOn(0, 24, 100);
        var soft = SequencingWorld.LevelAt(half.RenderSeconds(0.025), 0);

        //Assert
        (20.0 * Math.Log10(soft / loud)).Should().BeApproximately(-6.02, 0.15);
    }

    [Fact]
    public void no_loop_stops_a_sequence_whose_declared_length_is_two()
    {
        //Arrange
        // MEASURED (round 3, item 40) AND DELIBERATELY NOT COPIED: in the reference, no_loop fails to
        // stop a sequence whose DECLARED LENGTH is 2 - two independent length-2 sequences looped for
        // ever while lengths 3 and 4 stopped, whatever the note count. That is a defect; here no_loop
        // always stops after one pass.
        using var world = SequencingWorld.Build(
            Preset(binding: """seqLoopMode="no_loop" """, length: "2"));

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var render = world.RenderSeconds(3.9);

        //Assert - the two notes inside the declared length, once, and then silence.
        Keys(render).Should().Equal(0, 1);
    }

    [Fact]
    public void both_random_modes_draw_every_note_of_the_sequence()
    {
        //Arrange
        // MEASURED (round 3, item 40) AND DELIBERATELY NOT COPIED: both of the reference's random
        // modes have a hole - `random` never drew note index 3 of four in 26 draws and
        // `random_no_repeat` never drew index 2 in 30 draws, odds of 0.05 % and about 0.1 % for a fair
        // draw. Here every note is reachable in either mode.
        using var random = SequencingWorld.Build(Preset(binding: """seqLoopMode="random" """));
        using var noRepeat = SequencingWorld.Build(
            Preset(binding: """seqLoopMode="random_no_repeat" """));

        //Act
        random.Synthesizer.NoteOn(0, 24, 100);
        noRepeat.Synthesizer.NoteOn(0, 24, 100);

        var drawn = Keys(random.RenderSeconds(15.9));
        var drawnNoRepeat = Keys(noRepeat.RenderSeconds(15.9));

        //Assert - all four notes appear in a run of 32 draws, in both modes.
        drawn.Should().HaveCount(32);
        drawnNoRepeat.Should().HaveCount(32);
        drawn.Distinct().Order().Should().Equal(0, 1, 2, 3);
        drawnNoRepeat.Distinct().Order().Should().Equal(0, 1, 2, 3);
    }

    // One velocity-following zone and one handler, for the emitted-velocity cases.
    private static string VelocityPreset(string track, string noteVelocity = "1") =>
        $$"""
        <DecentSampler>
        {{SequencingWorld.BlipGroup().Replace("ampVelTrack=\"0\"", "ampVelTrack=\"1\"")}}
          <midi>
            <note note="24" swallowNotes="true">
              <binding level="instrument" type="note_sequence" seqIndex="0"
                       seqTriggerBehavior="midi_key" seqTrackMidiInputVelocity="{{track}}" />
            </note>
          </midi>
          <noteSequences>
            <sequence name="one" length="4" rate="1">
              <note position="0" velocity="{{noteVelocity}}" note="60" length="1" />
            </sequence>
          </noteSequences>
        </DecentSampler>
        """;

    // A preset whose one sequence is written out in full, for the truncation cases.
    private static string CustomSequence(string length, string notes) =>
        $$"""
        <DecentSampler>
        {{SequencingWorld.KeyedBlipGroup(60, 10)}}
          <midi>
            <note note="24" enabled="true" swallowNotes="true">
              <binding enabled="true" level="instrument" type="note_sequence" seqIndex="0"
                       seqTriggerBehavior="midi_key" />
            </note>
          </midi>
          <noteSequences>
            <sequence name="custom" length="{{length}}" rate="1">
              {{notes}}
            </sequence>
          </noteSequences>
        </DecentSampler>
        """;

    private static string Preset(
        string note = "24", string binding = "", string rate = "1", string length = "4") =>
        $$"""
        <DecentSampler>
        {{SequencingWorld.KeyedBlipGroup(60, 10)}}
          <midi>
            <note note="{{note}}" enabled="true" swallowNotes="true">
              <binding enabled="true" level="instrument" type="note_sequence" seqIndex="0"
                       seqTriggerBehavior="midi_key" {{binding}}/>
            </note>
          </midi>
          <noteSequences>
            <sequence name="quarters" length="{{length}}" rate="{{rate}}">
              <note position="0" velocity="1" note="60" length="0.2" />
              <note position="1" velocity="1" note="61" length="0.2" />
              <note position="2" velocity="1" note="62" length="0.2" />
              <note position="3" velocity="1" note="63" length="0.2" />
            </sequence>
          </noteSequences>
        </DecentSampler>
        """;
}
