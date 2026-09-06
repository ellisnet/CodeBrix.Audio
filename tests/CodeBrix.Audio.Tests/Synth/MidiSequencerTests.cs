using System;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

/// <summary>
/// Covers <see cref="MidiSequencer.Seek"/> at the boundary the rest of the suite never exercised:
/// what happens to the events written EXACTLY at the position being seeked to.
/// </summary>
public class MidiSequencerTests
{
    private const int SampleRate = 44100;

    [Fact]
    public void seeking_to_the_start_still_plays_the_notes_written_at_tick_zero()
    {
        //Arrange - the bug this fences: Seek consumed every event at or before the position, and a
        // note-on is deliberately not replayed when it is consumed, so seeking to zero swallowed
        // the whole downbeat and the sequence rendered as silence.
        var sequence = MultiTrackTestSong.BuildSingleNoteSequence(72, noteTicks: 1000, totalTicks: 1400);
        var sequencer = new MidiSequencer(new SoundFontSynthesizer(MultiTrackTestSong.SoundFont, SampleRate));
        sequencer.Play(sequence, loop: false);

        //Act
        sequencer.Seek(TimeSpan.Zero);
        var left = new float[SampleRate / 4];
        var right = new float[SampleRate / 4];
        sequencer.Render(left, right);

        //Assert
        Peak(left).Should().BeGreaterThan(1e-3f);
    }

    [Fact]
    public void seeking_onto_a_notes_own_start_still_plays_that_note()
    {
        //Arrange - the second note starts half a second in; seeking exactly there must sound it.
        var sequence = MultiTrackTestSong.BuildNoteSequence([72, 74], noteTicks: 900, stepTicks: 1000);
        var sequencer = new MidiSequencer(new SoundFontSynthesizer(MultiTrackTestSong.SoundFont, SampleRate));
        sequencer.Play(sequence, loop: false);

        //Act
        sequencer.Seek(TimeSpan.FromSeconds(0.5));
        var left = new float[SampleRate / 4];
        var right = new float[SampleRate / 4];
        sequencer.Render(left, right);

        //Assert
        Peak(left).Should().BeGreaterThan(1e-3f);
    }

    [Fact]
    public void seeking_past_a_note_does_not_re_trigger_it()
    {
        //Arrange - one note, over by 0.45 s; seeking to 0.6 s lands in the silence after it.
        var sequence = MultiTrackTestSong.BuildSingleNoteSequence(72, noteTicks: 900, totalTicks: 2400);
        var sequencer = new MidiSequencer(new SoundFontSynthesizer(MultiTrackTestSong.SoundFont, SampleRate));
        sequencer.Play(sequence, loop: false);

        //Act
        sequencer.Seek(TimeSpan.FromSeconds(0.6));
        var left = new float[SampleRate / 4];
        var right = new float[SampleRate / 4];
        sequencer.Render(left, right);

        //Assert
        Peak(left).Should().BeLessThan(1e-4f);
    }

    private static float Peak(float[] samples)
    {
        var peak = 0f;
        foreach (var sample in samples)
        {
            var magnitude = Math.Abs(sample);
            if (magnitude > peak)
            {
                peak = magnitude;
            }
        }

        return peak;
    }
}
