using System;
using System.Collections.Generic;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Sequencing;

/// <summary>
/// What happens when a sequence that is already running is triggered again: the same key, a different
/// key carrying the same <c>seqPlayerIdentifier</c>, an <c>on</c> binding fired twice, and a user
/// interface button re-selecting the state it is already in.
/// </summary>
/// <remarks>
/// Every number here comes from round 4, item 52 of the reference measurements. The sequence is four
/// quarter notes on keys 60 to 63, which the keyed blip group turns into four distinguishable levels,
/// so a decoded key says which note of the sequence sounded and an onset frame says when.
/// </remarks>
public class DecentSamplerSequenceRetriggerTests
{
    private const double SecondsBeforeRetrigger = 1.25;
    private const double SecondsAfterRetrigger = 1.9;

    [Fact]
    public void the_same_key_again_restarts_the_one_player_on_a_new_grid()
    {
        //Arrange
        // MEASURED (round 4, item 52): a second note-on on the SAME key restarts the one player at its
        // first note and re-bases the grid on the new note-on, and only ONE stream is heard - every
        // emitted note came out at the level of a single voice.
        using var world = SequencingWorld.Build(KeyPreset());

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var before = world.RenderSeconds(SecondsBeforeRetrigger);

        world.Synthesizer.NoteOn(0, 24, 100);
        var after = world.RenderSeconds(SecondsAfterRetrigger);

        //Assert
        var render = Join(before, after);
        var onsets = SequencingWorld.Onsets(render);

        Keys(render).Should().Equal(0, 1, 2, 0, 1, 2, 3);
        onsets[3].Should().BeInRange(before.Length - 1, before.Length + 1);
        onsets[4].Should().BeInRange(before.Length + 22049, before.Length + 22051);
    }

    [Fact]
    public void a_different_key_starts_a_second_player_even_under_the_same_identifier()
    {
        //Arrange
        // MEASURED (round 4, item 52): two keys naming the SAME seqPlayerIdentifier ran two fully
        // independent players on their own grids - a midi_key trigger is keyed by the KEY.
        using var world = SequencingWorld.Build(KeyPreset(identifier: """ seqPlayerIdentifier="p1" """));

        //Act
        world.Synthesizer.NoteOn(0, 24, 100);
        var before = world.RenderSeconds(SecondsBeforeRetrigger);

        world.Synthesizer.NoteOn(0, 25, 100);
        var after = world.RenderSeconds(1.4);

        //Assert - the first player keeps ITS OWN grid (0, 0.5, 1.0, 1.5, 2.0, 2.5 s) while the second
        //runs on a grid based at the second key-down, so the two interleave instead of one restarting
        //the other. The keys decode as A0 A1 A2 B0 A3 B1 A0 B2 A1.
        var render = Join(before, after);
        var onsets = SequencingWorld.Onsets(render);
        var second = before.Length;

        Keys(render).Should().Equal(0, 1, 2, 0, 3, 1, 0, 2, 1);
        Expect(
            onsets, 0, 22050, 44100, second, 66150, second + 22050, 88200, second + 44100, 110250);
    }

    [Fact]
    public void firing_on_again_through_a_binding_restarts_that_identifiers_player()
    {
        //Arrange
        // MEASURED (round 4, item 52): a <cc> binding fires on every controller CHANGE, and each
        // firing of seqTriggerBehavior="on" restarts the one player the identifier names - one stream,
        // not two. The sequence runs with no key held.
        using var world = SequencingWorld.Build(ControllerPreset());

        //Act
        world.Synthesizer.ProcessMidiMessage(0, 0xB0, 31, 127);
        var before = world.RenderSeconds(SecondsBeforeRetrigger);

        world.Synthesizer.ProcessMidiMessage(0, 0xB0, 31, 100);
        var after = world.RenderSeconds(SecondsAfterRetrigger);

        //Assert
        var render = Join(before, after);
        var onsets = SequencingWorld.Onsets(render);

        Keys(render).Should().Equal(0, 1, 2, 0, 1, 2, 3);
        onsets[3].Should().BeInRange(before.Length - 1, before.Length + 1);
    }

    [Fact]
    public void an_off_binding_stops_the_player_the_identifier_names()
    {
        //Arrange
        using var world = SequencingWorld.Build(ControllerPreset());

        //Act
        world.Synthesizer.ProcessMidiMessage(0, 0xB0, 31, 127);
        var before = world.RenderSeconds(SecondsBeforeRetrigger);

        world.Synthesizer.ProcessMidiMessage(0, 0xB0, 32, 127);
        var after = world.RenderSeconds(SecondsAfterRetrigger);

        //Assert
        Keys(before).Should().Equal(0, 1, 2);
        SequencingWorld.Onsets(after).Should().BeEmpty();
    }

    [Fact]
    public void a_button_state_binding_fires_only_when_the_state_changes()
    {
        //Arrange
        // MEASURED (round 4, item 52): the same schedule driven through a button's state pair ran
        // UNBROKEN on its original grid, because setting the button to the state it is already in is
        // not a state change and the "On" binding did not fire a second time.
        using var world = SequencingWorld.Build(ButtonPreset());
        var button = world.Instrument.Controls[0];

        //Act
        button.SetValue(1);
        var before = world.RenderSeconds(SecondsBeforeRetrigger);

        button.SetValue(1);
        var after = world.RenderSeconds(0.6);

        //Assert - the grid never moved, so the fourth note lands on beat three of the ORIGINAL grid.
        var render = Join(before, after);
        var onsets = SequencingWorld.Onsets(render);

        Keys(render).Should().Equal(0, 1, 2, 3);
        onsets[3].Should().BeInRange(66149, 66151);
    }

    [Fact]
    public void a_button_stops_the_sequence_when_it_enters_the_off_state()
    {
        //Arrange
        using var world = SequencingWorld.Build(ButtonPreset());
        var button = world.Instrument.Controls[0];

        //Act
        button.SetValue(1);
        var before = world.RenderSeconds(SecondsBeforeRetrigger);

        button.SetValue(0);
        var after = world.RenderSeconds(SecondsAfterRetrigger);

        //Assert
        Keys(before).Should().Equal(0, 1, 2);
        SequencingWorld.Onsets(after).Should().BeEmpty();
    }

    // Two key switches, 24 and 25, each starting the same sequence.
    private static string KeyPreset(string identifier = "") =>
        $$"""
        <DecentSampler>
        {{SequencingWorld.KeyedBlipGroup(60, 10)}}
          <midi>
            <note note="24" enabled="true" swallowNotes="true">
              <binding enabled="true" level="instrument" type="note_sequence" seqIndex="0"
                       seqTriggerBehavior="midi_key"{{identifier}}/>
            </note>
            <note note="25" enabled="true" swallowNotes="true">
              <binding enabled="true" level="instrument" type="note_sequence" seqIndex="0"
                       seqTriggerBehavior="midi_key"{{identifier}}/>
            </note>
          </midi>
        {{Quarters()}}
        </DecentSampler>
        """;

    // An on/off pair on two controllers, both naming one player.
    private static string ControllerPreset() =>
        $$"""
        <DecentSampler>
        {{SequencingWorld.KeyedBlipGroup(60, 10)}}
          <midi>
            <cc number="31">
              <binding level="instrument" type="note_sequence" seqIndex="0"
                       seqTriggerBehavior="on" seqPlayerIdentifier="c1" seqLoopMode="forward" />
            </cc>
            <cc number="32">
              <binding level="instrument" type="note_sequence" seqIndex="0"
                       seqTriggerBehavior="off" seqPlayerIdentifier="c1" />
            </cc>
          </midi>
        {{Quarters()}}
        </DecentSampler>
        """;

    // The same pair as the two states of one button.
    private static string ButtonPreset() =>
        $$"""
        <DecentSampler>
          <ui width="812" height="375">
            <tab>
              <button x="10" y="10" width="80" height="30" value="0">
                <state name="Off">
                  <binding level="instrument" type="note_sequence" seqIndex="0"
                           seqTriggerBehavior="off" seqPlayerIdentifier="b1" />
                </state>
                <state name="On">
                  <binding level="instrument" type="note_sequence" seqIndex="0"
                           seqTriggerBehavior="on" seqPlayerIdentifier="b1" seqLoopMode="forward" />
                </state>
              </button>
            </tab>
          </ui>
        {{SequencingWorld.KeyedBlipGroup(60, 10)}}
        {{Quarters()}}
        </DecentSampler>
        """;

    private static string Quarters() =>
        """
          <noteSequences>
            <sequence name="quarters" length="4" rate="1">
              <note position="0" velocity="1" note="60" length="0.2" />
              <note position="1" velocity="1" note="61" length="0.2" />
              <note position="2" velocity="1" note="62" length="0.2" />
              <note position="3" velocity="1" note="63" length="0.2" />
            </sequence>
          </noteSequences>
        """;

    private static void Expect(IReadOnlyList<int> onsets, params int[] expected)
    {
        onsets.Should().HaveCount(expected.Length);

        for (var index = 0; index < expected.Length; index++)
        {
            onsets[index].Should().BeInRange(expected[index] - 1, expected[index] + 1);
        }
    }

    private static float[] Join(float[] first, float[] second)
    {
        var joined = new float[first.Length + second.Length];
        first.CopyTo(joined, 0);
        second.CopyTo(joined, first.Length);
        return joined;
    }

    // Which note of the sequence each onset was, read off the keyed blip group's levels.
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
}
