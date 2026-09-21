using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

/// <summary>
/// Covers <see cref="MidiStreamSequencer"/>: the equivalence with playing the same content as a
/// sequence, starvation and pre-roll, late events, seeking, and the transport.
/// </summary>
/// <remarks>
/// Nothing here opens an audio device, uses a timer or sleeps: every test renders into float
/// arrays and appends from its own thread, exactly as the file sequencer's tests do.
/// </remarks>
public class MidiStreamSequencerTests
{
    private const int SampleRate = 44100;

    [Fact]
    public void a_stream_completed_before_playback_renders_exactly_as_its_sequence()
    {
        //Arrange - THE FENCE. A stream finished before anyone played it is the same music as the
        // sequence it converts to, so the two must render the same samples, bit for bit: the same
        // synthesizer, the same block sizes, the same tick-to-time arithmetic in the same order. A
        // failure here by a few ULPs means the walk has drifted from MidiSequence.MergeTracks.
        var stream = BuildFenceContent();
        stream.Complete();
        var sequence = stream.ToSequence();

        // 1000 frames is deliberately odd against the synthesizer's own 64-frame block, so events
        // land inside blocks rather than on their boundaries.
        const int blockFrames = 1000;
        const int blockCount = 160;
        var frames = blockFrames * blockCount;

        var streamSequencer = new MidiStreamSequencer(NewSynthesizer());
        streamSequencer.Play(stream);

        var fileSequencer = new MidiSequencer(NewSynthesizer());
        fileSequencer.Play(sequence, loop: false);

        var streamLeft = new float[frames];
        var streamRight = new float[frames];
        var fileLeft = new float[frames];
        var fileRight = new float[frames];

        //Act
        for (var block = 0; block < blockCount; block++)
        {
            var at = block * blockFrames;
            streamSequencer.Render(streamLeft.AsSpan(at, blockFrames), streamRight.AsSpan(at, blockFrames));
            fileSequencer.Render(fileLeft.AsSpan(at, blockFrames), fileRight.AsSpan(at, blockFrames));
        }

        //Assert
        FirstDifference(streamLeft, fileLeft).Should().Be(-1);
        FirstDifference(streamRight, fileRight).Should().Be(-1);

        // ... and the two agree on something rather than on silence.
        Peak(fileLeft).Should().BeGreaterThan(1e-3f);

        // Rendering compares the two at the resolution of a synthesizer block; the walk itself is
        // held to the exact TimeSpan tick here, which is what catches a truncation that has gone
        // astray long before it is big enough to move an event into another block.
        stream.HorizonTime.Should().Be(sequence.Length);
    }

    [Fact]
    public void the_event_at_the_horizon_fires_before_the_stream_starves()
    {
        //Arrange - a lone note-on at tick 0 is the whole stream, so the horizon is tick 0 too: if
        // the head had to stay behind the horizon to play anything, this note would never sound.
        var lone = new MidiStream(480);
        lone.Append(new NoteEvent(0, 1, MidiCommandCode.NoteOn, 79, 100));

        var loneSequencer = new MidiStreamSequencer(NewSynthesizer());
        loneSequencer.Play(lone);

        // ... and one bar of a note, whose note-off IS the horizon.
        var bar = new MidiStream(480);
        bar.AppendNote(0, 1, 79, 100, 1920);

        var delivered = new List<int>();
        var barSequencer = new MidiStreamSequencer(NewSynthesizer());
        barSequencer.OnSendMessage = (synthesizer, channel, command, data1, data2) => delivered.Add(command);
        barSequencer.Play(bar);

        //Act
        var (loneLeft, _) = Render(loneSequencer, 0.25);
        Render(barSequencer, 2.5);

        //Assert - the note at the horizon sounded at once, and the head is held exactly at it.
        Peak(loneLeft).Should().BeGreaterThan(1e-3f);
        loneSequencer.Position.Should().Be(lone.HorizonTime);
        loneSequencer.IsStarved.Should().BeTrue();

        // The bar's last note-off fired on time - 1920 ticks at 480 per quarter and 120 BPM is two
        // seconds - and the head holds exactly at the bar's end rather than short of it.
        delivered.Should().Equal(0x90, 0x80);
        barSequencer.Position.Should().Be(bar.HorizonTime);
        barSequencer.Position.TotalSeconds.Should().BeApproximately(2.0, 1e-6);
        barSequencer.IsStarved.Should().BeTrue();
    }

    [Fact]
    public void is_starved_is_false_right_after_play_on_a_buffered_stream()
    {
        //Arrange - two bars written before anyone plays it, so there is plenty to be getting on
        // with. Starvation is about whether the head can move, not about whether a block has been
        // rendered yet.
        var stream = new MidiStream(StreamingTestProducer.TicksPerQuarterNote);
        var producer = new StreamingTestProducer(stream);
        producer.AppendNextBar();
        producer.AppendNextBar();

        var sequencer = new MidiStreamSequencer(NewSynthesizer());

        //Act
        sequencer.Play(stream);
        var beforeAnyRender = sequencer.IsStarved;
        var positionBeforeAnyRender = sequencer.Position;

        Render(sequencer, 5.0);

        //Assert
        beforeAnyRender.Should().BeFalse();
        positionBeforeAnyRender.Should().Be(TimeSpan.Zero);

        // ... and once the head has run out of music, it says so.
        sequencer.IsStarved.Should().BeTrue();
        sequencer.Position.Should().Be(stream.HorizonTime);
    }

    [Fact]
    public void rendering_up_to_the_horizon_then_holds_the_position_and_is_starved()
    {
        //Arrange - one bar written, so the horizon is the first note's note-off.
        var stream = new MidiStream(StreamingTestProducer.TicksPerQuarterNote);
        var producer = new StreamingTestProducer(stream);
        producer.AppendNextBar();

        var sequencer = new MidiStreamSequencer(NewSynthesizer());
        sequencer.Play(stream);

        //Act
        Render(sequencer, 3.0);
        var held = sequencer.Position;
        Render(sequencer, 1.0);

        //Assert - the head stops AT the horizon, not a tick short of it, and stays there.
        sequencer.IsStarved.Should().BeTrue();
        sequencer.Position.Should().Be(held);
        sequencer.Position.Should().Be(stream.HorizonTime);
        sequencer.PositionTicks.Should().Be(stream.HorizonTicks);
        sequencer.EndOfStream.Should().BeFalse();
    }

    [Fact]
    public void the_ring_out_of_a_sounding_note_continues_while_starved()
    {
        //Arrange - a note whose note-off has not been written yet, and a later event to give the
        // head somewhere to run to.
        var stream = new MidiStream(480);
        stream.Append(new NoteEvent(0, 1, MidiCommandCode.NoteOn, 79, 100));
        stream.Append(new ControlChangeEvent(960, 1, MidiController.Expression, 127));

        var sequencer = new MidiStreamSequencer(NewSynthesizer());
        sequencer.Play(stream);
        Render(sequencer, 1.5);

        //Act
        var (left, _) = Render(sequencer, 0.5);

        //Assert - the head is held at the horizon, and the note is still sounding: honest, and the
        // producer's next append is what ends it.
        sequencer.IsStarved.Should().BeTrue();
        sequencer.Position.Should().Be(stream.HorizonTime);
        sequencer.Synthesizer.ActiveVoiceCount.Should().BeGreaterThan(0);
        Peak(left).Should().BeGreaterThan(1e-3f);
    }

    [Fact]
    public void appending_after_starvation_resumes_from_the_held_position()
    {
        //Arrange
        var stream = new MidiStream(StreamingTestProducer.TicksPerQuarterNote);
        var producer = new StreamingTestProducer(stream);
        producer.AppendNextBar();

        var sequencer = new MidiStreamSequencer(NewSynthesizer());
        sequencer.Play(stream);
        Render(sequencer, 3.0);
        var held = sequencer.Position;
        sequencer.IsStarved.Should().BeTrue();
        held.Should().Be(stream.HorizonTime);

        //Act
        producer.AppendNextBar();
        Render(sequencer, 1.0);

        //Assert - it carries on from where it held, rather than from where the next bar starts.
        sequencer.IsStarved.Should().BeFalse();
        sequencer.Position.Should().BeGreaterThan(held);
        (sequencer.Position - held).TotalSeconds.Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void preroll_delays_the_start_until_enough_is_buffered()
    {
        //Arrange - one second of pre-roll, and a quarter of a second written.
        var stream = new MidiStream(480);
        stream.Preroll = TimeSpan.FromSeconds(1);
        stream.AppendNote(0, 1, 79, 100, 240);

        var sequencer = new MidiStreamSequencer(NewSynthesizer());
        sequencer.Play(stream);

        //Act
        var (before, _) = Render(sequencer, 0.5);
        var heldAtStart = sequencer.Position;
        var starvedAtStart = sequencer.IsStarved;

        // The horizon moves to 1440 ticks - a second and a half, comfortably past the pre-roll.
        stream.AppendNote(480, 1, 81, 100, 960);
        Render(sequencer, 0.5);

        //Assert - the pre-roll holds the CLOCK, not the music: what the head has already reached
        // still plays, so the note at tick zero sounds while the head waits at zero.
        starvedAtStart.Should().BeTrue();
        heldAtStart.Should().Be(TimeSpan.Zero);
        Peak(before).Should().BeGreaterThan(1e-3f);

        sequencer.IsStarved.Should().BeFalse();
        sequencer.Position.TotalSeconds.Should().BeApproximately(0.5, 0.01);
    }

    [Fact]
    public void preroll_applies_again_after_an_underrun()
    {
        //Arrange - half a second of pre-roll; two seconds written.
        var stream = new MidiStream(480);
        stream.Preroll = TimeSpan.FromSeconds(0.5);
        stream.AppendNote(0, 1, 79, 100, 1920);

        var sequencer = new MidiStreamSequencer(NewSynthesizer());
        sequencer.Play(stream);
        Render(sequencer, 3.0);
        var held = sequencer.Position;
        sequencer.IsStarved.Should().BeTrue();

        //Act - a trickle is not a pre-roll: an eighth of a second more does not restart the clock.
        stream.Append(new ControlChangeEvent(1980, 1, MidiController.Expression, 127));
        Render(sequencer, 0.5);
        var afterTrickle = sequencer.Position;
        var starvedAgain = sequencer.IsStarved;

        stream.AppendNote(2400, 1, 81, 100, 960);
        Render(sequencer, 0.5);

        //Assert
        starvedAgain.Should().BeTrue();
        afterTrickle.Should().Be(held);
        sequencer.IsStarved.Should().BeFalse();
        sequencer.Position.Should().BeGreaterThan(held + TimeSpan.FromSeconds(0.4));
    }

    [Fact]
    public void preroll_is_a_resume_threshold_not_a_sliding_gate()
    {
        //Arrange - one second of pre-roll and three seconds buffered: 2880 ticks at 480 per quarter
        // and 120 BPM.
        var stream = new MidiStream(480);
        stream.Preroll = TimeSpan.FromSeconds(1);
        stream.AppendNote(0, 1, 79, 100, 2880);

        var sequencer = new MidiStreamSequencer(NewSynthesizer());
        sequencer.Play(stream);

        //Act - two and a half seconds in, less than the pre-roll is left, and playback runs on
        // anyway: the threshold is what it takes to RESUME, not a gap the head has to keep.
        Render(sequencer, 2.5);
        var running = sequencer.Position;
        var starvedBelowThePreroll = sequencer.IsStarved;

        Render(sequencer, 1.0);
        var atTheHorizon = sequencer.Position;
        var starvedAtTheHorizon = sequencer.IsStarved;
        var horizonWhenItStarved = stream.HorizonTime;

        // A quarter of a second more is not enough to start again.
        stream.Append(new ControlChangeEvent(3120, 1, MidiController.Expression, 127));
        Render(sequencer, 0.5);
        var afterTheTrickle = sequencer.Position;

        // Two more seconds is.
        stream.AppendNote(3840, 1, 81, 100, 960);
        Render(sequencer, 0.5);

        //Assert
        starvedBelowThePreroll.Should().BeFalse();
        running.TotalSeconds.Should().BeApproximately(2.5, 0.01);

        starvedAtTheHorizon.Should().BeTrue();
        atTheHorizon.Should().Be(horizonWhenItStarved);
        atTheHorizon.TotalSeconds.Should().BeApproximately(3.0, 1e-6);

        afterTheTrickle.Should().Be(atTheHorizon);
        sequencer.IsStarved.Should().BeFalse();
        sequencer.Position.Should().BeGreaterThan(atTheHorizon + TimeSpan.FromSeconds(0.4));
    }

    [Fact]
    public void complete_lets_the_stream_drain_and_end_of_stream_becomes_true()
    {
        //Arrange
        var stream = new MidiStream(StreamingTestProducer.TicksPerQuarterNote);
        var producer = new StreamingTestProducer(stream);
        producer.AppendAllBars();

        var sequencer = new MidiStreamSequencer(NewSynthesizer());
        sequencer.Play(stream);
        Render(sequencer, 12.0);

        //Act
        sequencer.EndOfStream.Should().BeFalse();
        sequencer.IsStarved.Should().BeTrue();

        stream.Complete();
        Render(sequencer, 0.5);

        //Assert
        sequencer.IsStarved.Should().BeFalse();
        sequencer.EndOfStream.Should().BeTrue();
    }

    [Fact]
    public void a_late_event_plays_at_the_next_block_is_counted_and_is_recorded_at_its_own_tick()
    {
        //Arrange - a long note keeps the horizon far ahead, so the stream is not starved when the
        // straggler arrives.
        var stream = new MidiStream(480);
        stream.AppendTempo(0, 120);
        stream.AppendNote(0, 1, 79, 100, 9600);

        var sequencer = new MidiStreamSequencer(NewSynthesizer());
        sequencer.Play(stream);
        Render(sequencer, 1.0);
        sequencer.PositionTicks.Should().BeGreaterThan(100);

        var delivered = new List<int>();
        sequencer.OnSendMessage = (synthesizer, channel, command, data1, data2) =>
            delivered.Add((command << 16) | (data1 << 8) | data2);

        //Act - written at tick 100, which the head passed long ago.
        stream.Append(new ControlChangeEvent(100, 1, MidiController.Expression, 77));
        Render(sequencer, 0.05);

        //Assert - delivered, counted, and recorded where the producer meant it.
        stream.LateEventCount.Should().Be(1);
        delivered.Should().Equal((0xB0 << 16) | (11 << 8) | 77);

        var channelTrack = stream.ToMidiEventCollection().GetTrackEvents(1);
        var recorded = FindFirst(channelTrack, MidiCommandCode.ControlChange);
        recorded.AbsoluteTime.Should().Be(100);
    }

    [Fact]
    public void a_tempo_change_appended_ahead_of_the_head_retimes_what_follows()
    {
        //Arrange - two notes a bar apart; the second is due one second in at 120 BPM.
        var stream = new MidiStream(480);
        stream.AppendNote(0, 1, 79, 100, 240);
        stream.AppendNote(960, 1, 81, 100, 240);

        var sequencer = new MidiStreamSequencer(NewSynthesizer());
        sequencer.Play(stream);
        Render(sequencer, 0.25);

        var secondNoteAt = TimeSpan.MinValue;
        sequencer.OnSendMessage = (synthesizer, channel, command, data1, data2) =>
        {
            if (command == 0x90 && data1 == 81)
            {
                secondNoteAt = sequencer.Position;
            }
        };

        //Act - halving the tempo at tick 480, which the head has not reached, doubles what is left.
        stream.AppendTempo(480, 60);
        Render(sequencer, 2.5);

        //Assert - half a second at 120 BPM, then a whole one at 60.
        stream.LateEventCount.Should().Be(0);
        secondNoteAt.TotalSeconds.Should().BeApproximately(1.5, 0.02);
    }

    [Fact]
    public void a_time_signature_updates_beats_per_bar()
    {
        //Arrange
        var stream = new MidiStream(480);
        stream.Append(new TimeSignatureEvent(960, 3, 2, 24, 8));
        stream.AppendNote(1920, 1, 79, 100, 240);

        var sequencer = new MidiStreamSequencer(NewSynthesizer());
        sequencer.Play(stream);

        //Act
        sequencer.BeatsPerBar.Should().Be(4);
        Render(sequencer, 1.5);

        //Assert
        sequencer.BeatsPerBar.Should().Be(3);
        sequencer.CurrentBeatsPerMinute.Should().Be(120.0);
        sequencer.CurrentBeatPosition.Should().BeGreaterThan(2.0);
    }

    [Fact]
    public void seeking_backwards_replays_controller_state_and_not_notes()
    {
        //Arrange - the file sequencer's own seek cases, on a stream.
        var passed = BuildSeekContent(volume: -1);
        var silenced = BuildSeekContent(volume: 0);
        var sounding = BuildSeekContent(volume: 100);

        var passedSequencer = new MidiStreamSequencer(NewSynthesizer());
        passedSequencer.Play(passed);
        var silencedSequencer = new MidiStreamSequencer(NewSynthesizer());
        silencedSequencer.Play(silenced);
        var soundingSequencer = new MidiStreamSequencer(NewSynthesizer());
        soundingSequencer.Play(sounding);

        //Act - 0.55 s is tick 1100 at 1000 ticks per quarter and 120 BPM: after the first note has
        // finished, before the second one starts.
        passedSequencer.Seek(TimeSpan.FromSeconds(0.55));
        silencedSequencer.Seek(TimeSpan.FromSeconds(0.55));
        soundingSequencer.Seek(TimeSpan.FromSeconds(0.55));

        var (passedLeft, _) = Render(passedSequencer, 0.04);
        var (silencedLeft, _) = Render(silencedSequencer, 0.25);
        var (soundingLeft, _) = Render(soundingSequencer, 0.25);

        //Assert - the note that was already over is not struck again; the volume written at tick
        // zero is replayed, so the note after the seek point obeys it.
        Peak(passedLeft).Should().BeLessThan(1e-4f);
        Peak(silencedLeft).Should().BeLessThan(1e-4f);
        Peak(soundingLeft).Should().BeGreaterThan(1e-3f);
    }

    [Fact]
    public void seeking_to_the_start_still_plays_the_notes_written_at_tick_zero()
    {
        //Arrange - the bug this fences, on a stream: seeking to zero must not swallow the downbeat.
        var stream = new MidiStream(1000);
        stream.AppendNote(0, 1, 72, 100, 1000);
        stream.Append(new ControlChangeEvent(1400, 1, MidiController.Expression, 127));
        stream.Complete();

        var sequencer = new MidiStreamSequencer(NewSynthesizer());
        sequencer.Play(stream);

        //Act
        sequencer.Seek(TimeSpan.Zero);
        var (left, _) = Render(sequencer, 0.25);

        //Assert
        Peak(left).Should().BeGreaterThan(1e-3f);
    }

    [Fact]
    public void seeking_beyond_the_horizon_clamps_to_it()
    {
        //Arrange
        var stream = new MidiStream(480);
        stream.AppendNote(0, 1, 79, 100, 1920);

        var sequencer = new MidiStreamSequencer(NewSynthesizer());
        sequencer.Play(stream);

        //Act
        sequencer.Seek(TimeSpan.FromMinutes(5));

        //Assert - 1920 ticks at 480 per quarter and 120 BPM is two seconds.
        sequencer.Position.Should().Be(stream.HorizonTime);
        sequencer.Position.TotalSeconds.Should().BeApproximately(2.0, 1e-6);
    }

    [Fact]
    public void stop_detaches_and_a_second_play_starts_from_the_beginning()
    {
        //Arrange
        var stream = new MidiStream(480);
        stream.AppendNote(0, 1, 79, 100, 1920);

        var sequencer = new MidiStreamSequencer(NewSynthesizer());
        sequencer.Play(stream);
        Render(sequencer, 0.5);
        sequencer.Position.Should().BeGreaterThan(TimeSpan.Zero);

        //Act
        sequencer.Stop();

        //Assert
        sequencer.Stream.Should().BeNull();
        sequencer.EndOfStream.Should().BeTrue();
        sequencer.IsStarved.Should().BeFalse();

        // Detached, so another sequencer may take it - and so may this one, from the top.
        var other = new MidiStreamSequencer(NewSynthesizer());
        other.Play(stream);
        other.Position.Should().Be(TimeSpan.Zero);
        other.Stop();

        sequencer.Play(stream);
        sequencer.Position.Should().Be(TimeSpan.Zero);
        sequencer.Stream.Should().BeSameAs(stream);
    }

    [Fact]
    public void a_stream_cannot_be_played_by_two_sequencers_at_once()
    {
        //Arrange
        var stream = new MidiStream(480);
        stream.AppendNote(0, 1, 79, 100, 480);

        var first = new MidiStreamSequencer(NewSynthesizer());
        var second = new MidiStreamSequencer(NewSynthesizer());
        first.Play(stream);

        //Act
        Action act = () => second.Play(stream);

        //Assert
        act.Should().Throw<InvalidOperationException>();
        first.Stream.Should().BeSameAs(stream);
        second.Stream.Should().BeNull();
    }

    [Fact]
    public void play_on_the_held_stream_rewinds_without_throwing()
    {
        //Arrange - a host's rewind is one call, not a stop and a play.
        var stream = new MidiStream(480);
        stream.AppendNote(0, 1, 79, 100, 1920);

        var sequencer = new MidiStreamSequencer(NewSynthesizer());
        sequencer.Play(stream);
        Render(sequencer, 1.0);
        sequencer.Position.Should().BeGreaterThan(TimeSpan.Zero);

        //Act
        Action act = () => sequencer.Play(stream);

        //Assert
        act.Should().NotThrow();
        sequencer.Position.Should().Be(TimeSpan.Zero);
        sequencer.PositionTicks.Should().Be(0);
        sequencer.Stream.Should().BeSameAs(stream);

        // Still claimed, and still only by this one.
        var other = new MidiStreamSequencer(NewSynthesizer());
        Action stolen = () => other.Play(stream);
        stolen.Should().Throw<InvalidOperationException>();

        // ... and it plays again from the top.
        var (left, _) = Render(sequencer, 0.5);
        Peak(left).Should().BeGreaterThan(1e-3f);
    }

    [Fact]
    public void speed_scales_the_clock_as_it_does_for_a_file()
    {
        //Arrange
        var stream = new MidiStream(480);
        stream.AppendNote(0, 1, 79, 100, 1920);
        stream.Complete();

        var streamSequencer = new MidiStreamSequencer(NewSynthesizer());
        streamSequencer.Play(stream);
        var fileSequencer = new MidiSequencer(NewSynthesizer());
        fileSequencer.Play(stream.ToSequence(), loop: false);

        //Act
        streamSequencer.Speed = 2F;
        fileSequencer.Speed = 2F;
        Render(streamSequencer, 0.5);
        Render(fileSequencer, 0.5);

        //Assert - half a second of audio moved the clock a whole one, exactly as it does for a file.
        streamSequencer.Position.Should().Be(fileSequencer.Position);
        streamSequencer.Position.TotalSeconds.Should().BeApproximately(1.0, 0.01);

        Action negative = () => streamSequencer.Speed = -1F;
        negative.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void the_message_hook_replaces_delivery_as_it_does_for_a_file()
    {
        //Arrange
        var stream = new MidiStream(480);
        stream.Append(new PatchChangeEvent(0, 1, 0));
        stream.AppendNote(0, 1, 79, 100, 480);
        stream.Complete();

        var sequencer = new MidiStreamSequencer(NewSynthesizer());
        var channels = new List<int>();
        var commands = new List<int>();
        sequencer.OnSendMessage = (synthesizer, channel, command, data1, data2) =>
        {
            channels.Add(channel);
            commands.Add(command);
        };
        sequencer.Play(stream);

        //Act
        var (left, _) = Render(sequencer, 1.0);

        //Assert - the hook owns delivery, so nothing reached the synthesizer and nothing sounded.
        // The channel it is handed is 0-based, as it is everywhere the synthesizer is spoken to.
        commands.Should().Equal(0xC0, 0x90, 0x80);
        channels.Should().Equal(0, 0, 0);
        Peak(left).Should().Be(0f);
    }

    /// <summary>
    /// The fence's content: three tempo changes with one of them inside a note, a time signature, a
    /// key signature after the last tempo change, notes on three channels including channel 10,
    /// two program changes, a controller and a pitch bend - and, at two ticks, several channels
    /// writing at once, appended in an order that is deliberately NOT the order of the tracks they
    /// land on.
    /// </summary>
    // ----- a settled rest carried by the horizon (the producer's "nothing here") -----

    [Fact]
    public void the_head_walks_through_a_settled_rest_instead_of_starving_at_the_last_event()
    {
        //Arrange - the music ends at tick 240, a quarter of a second in, and the head holds there.
        var stream = new MidiStream(480);
        stream.AppendNote(0, 1, 60, 100, 240);

        var sequencer = new MidiStreamSequencer(NewSynthesizer());
        sequencer.Play(stream);
        Render(sequencer, 0.5);

        var heldAt = sequencer.Position;
        var starvedAtTheLastEvent = sequencer.IsStarved;

        //Act - the producer says the next two seconds are settled and empty.
        stream.AdvanceHorizon(1920);
        Render(sequencer, 1.0);

        //Assert
        starvedAtTheLastEvent.Should().BeTrue();
        heldAt.TotalSeconds.Should().BeApproximately(0.25, 0.01);
        sequencer.IsStarved.Should().BeFalse();
        sequencer.Position.TotalSeconds.Should().BeApproximately(1.25, 0.01);
        sequencer.Length.Should().Be(stream.HorizonTime);
    }

    [Fact]
    public void a_settled_rest_satisfies_the_pre_roll()
    {
        //Arrange - half a second has to be buffered and only a quarter of a second is written.
        var stream = new MidiStream(480);
        stream.Preroll = TimeSpan.FromSeconds(0.5);
        stream.AppendNote(0, 1, 60, 100, 240);

        var sequencer = new MidiStreamSequencer(NewSynthesizer());
        sequencer.Play(stream);

        var beforeTheRest = sequencer.IsStarved;

        //Act
        stream.AdvanceHorizon(1920);

        //Assert
        // A declared rest is as good as written music for the pre-roll: the producer has settled it.
        beforeTheRest.Should().BeTrue();
        sequencer.IsStarved.Should().BeFalse();
    }

    [Fact]
    public void a_completed_stream_plays_a_trailing_settled_rest_out()
    {
        //Arrange
        var stream = new MidiStream(480);
        stream.AppendNote(0, 1, 60, 100, 240);
        stream.AdvanceHorizon(1920);
        stream.Complete();

        var sequencer = new MidiStreamSequencer(NewSynthesizer());
        sequencer.Play(stream);

        //Act
        Render(sequencer, 0.5);
        var whileTheRestRuns = sequencer.EndOfStream;

        Render(sequencer, 2.0);

        //Assert
        // A piece that ends in silence ends when the silence does, not at its last note.
        whileTheRestRuns.Should().BeFalse();
        sequencer.EndOfStream.Should().BeTrue();
    }

    [Fact]
    public void a_completed_stream_with_no_settled_rest_still_ends_at_its_last_event()
    {
        //Arrange
        var stream = new MidiStream(480);
        stream.AppendNote(0, 1, 60, 100, 240);
        stream.Complete();

        var sequencer = new MidiStreamSequencer(NewSynthesizer());
        sequencer.Play(stream);

        //Act
        Render(sequencer, 0.5);

        //Assert
        // PINNED: a stream AdvanceHorizon was never called on behaves exactly as it always did.
        sequencer.EndOfStream.Should().BeTrue();
    }

    [Fact]
    public void TimeAtTick_agrees_with_the_head_the_sequencer_reports()
    {
        //Arrange - content with three tempo changes and a conductor track that ends after them.
        var stream = BuildFenceContent();
        stream.Complete();

        var sequencer = new MidiStreamSequencer(NewSynthesizer());
        sequencer.Play(stream);

        //Act
        Render(sequencer, 1.5);

        var head = sequencer.PositionTicks;

        //Assert
        // The head's tick is interpolated and truncated, so the two agree to within the tick the
        // head was rounded down to - not to the hundred-nanosecond tick.
        (stream.TimeAtTick(head) - sequencer.Position).Duration()
            .Should().BeLessThan(TimeSpan.FromMilliseconds(5));

        (stream.TickAtTime(sequencer.Position) - head).Should().BeInRange(-1, 1);
    }

    private static MidiStream BuildFenceContent()
    {
        var stream = new MidiStream(480);

        stream.AppendTempo(0, 120);
        stream.Append(new TimeSignatureEvent(0, 3, 2, 24, 8));
        stream.Append(new PatchChangeEvent(0, 1, 0));
        stream.Append(new ControlChangeEvent(0, 1, MidiController.Expression, 100));
        stream.Append(new PatchChangeEvent(10, 2, 1));
        stream.Append(new PitchWheelChangeEvent(250, 2, 9000));

        // TICK 480: three channels at one tick, written back to front. Channel 1 took track 1 and
        // channel 2 took track 2 above, and channel 10 takes track 3 here - so arrival order is
        // 3, 2, 1 while the merged file's order is 1, 2, 3. Equal ticks are where the two paths can
        // hand the synthesizer its voices in different orders, and where the first form of this
        // fence quietly declined to look.
        stream.AppendNote(480, 10, 38, 110, 100);
        stream.AppendNote(480, 2, 60, 90, 300);
        stream.AppendNote(480, 1, 79, 100, 960);

        stream.AppendTempo(700, 90);

        // TICK 1000: a tempo change at the same tick as notes on two other channels, appended LAST
        // although the conductor track is the first track in the file.
        stream.AppendNote(1000, 2, 64, 90, 240);
        stream.AppendNote(1000, 10, 42, 100, 100);
        stream.AppendTempo(1000, 105);

        stream.AppendTempo(1600, 140);

        // The conductor track now ends AFTER its last tempo change, so the end-of-track event the
        // exported file carries falls at a tick that holds no message. The walk has to step through
        // it, or the note below is timed from the wrong place by a tick of truncation.
        stream.Append(new KeySignatureEvent(2, 0, 1701));
        stream.AppendNote(1930, 1, 81, 100, 480);

        return stream;
    }

    /// <summary>
    /// A completed stream for the seek cases: a note that is over before the seek point, a note
    /// that starts after it, and optionally a channel volume written at tick zero.
    /// </summary>
    /// <param name="volume">The volume to write at tick zero, or -1 to write none.</param>
    private static MidiStream BuildSeekContent(int volume)
    {
        var stream = new MidiStream(1000);

        if (volume >= 0)
        {
            stream.Append(new ControlChangeEvent(0, 1, MidiController.MainVolume, volume));
        }

        stream.AppendNote(0, 1, 72, 100, 900);
        stream.AppendNote(1200, 1, 74, 100, 800);
        stream.Complete();
        return stream;
    }

    private static MidiEvent FindFirst(IList<MidiEvent> events, MidiCommandCode commandCode)
    {
        foreach (var midiEvent in events)
        {
            if (midiEvent.CommandCode == commandCode)
            {
                return midiEvent;
            }
        }

        throw new InvalidOperationException($"The track holds no {commandCode} event.");
    }

    private static SoundFontSynthesizer NewSynthesizer() =>
        new SoundFontSynthesizer(MultiTrackTestSong.SoundFont, SampleRate);

    private static (float[] Left, float[] Right) Render(IAudioRenderer renderer, double seconds)
    {
        var frames = (int)(SampleRate * seconds);
        var left = new float[frames];
        var right = new float[frames];
        renderer.Render(left, right);
        return (left, right);
    }

    private static int FirstDifference(float[] first, float[] second)
    {
        if (first.Length != second.Length)
        {
            return 0;
        }

        for (var i = 0; i < first.Length; i++)
        {
            if (first[i] != second[i])
            {
                return i;
            }
        }

        return -1;
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
