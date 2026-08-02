using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

/// <summary>
/// Covers the message-hook and per-channel plumbing <see cref="MidiSynthDataProvider"/> owns, driven
/// through a stub synthesizer so no audio device is involved. These are the semantics
/// <see cref="CodeBrix.Audio.Playback.MidiMusicPlayer"/> exposes, tested where the logic actually
/// lives rather than through a device-gated player.
/// </summary>
public sealed class MidiSynthDataProviderTests
{
    private const int SampleRate = 44100;

    [Fact]
    public void messages_reach_the_synthesizer_when_no_hook_is_installed()
    {
        //Arrange
        var synthesizer = new RecordingSynthesizer();
        using var provider = new MidiSynthDataProvider(synthesizer);

        //Act
        provider.Start(BuildThreeNoteSequence(), loop: false);
        RenderOneSecond(provider);

        //Assert
        NoteOnsIn(synthesizer.Received).Should().Equal(60, 62, 64);
    }

    [Fact]
    public void the_observer_sees_messages_that_are_delivered()
    {
        //Arrange
        var synthesizer = new RecordingSynthesizer();
        using var provider = new MidiSynthDataProvider(synthesizer);
        var observed = new List<(int channel, int command, int data1, int data2)>();
        provider.MessageObserver = (channel, command, data1, data2) => observed.Add((channel, command, data1, data2));

        //Act
        provider.Start(BuildThreeNoteSequence(), loop: false);
        RenderOneSecond(provider);

        //Assert
        // The observer must not change what is played: the synthesizer still got everything.
        NoteOnsIn(observed).Should().Equal(60, 62, 64);
        NoteOnsIn(synthesizer.Received).Should().Equal(60, 62, 64);
    }

    [Fact]
    public void a_filter_replaces_delivery_so_a_no_op_filter_silences_the_synthesizer()
    {
        //Arrange
        // This is the sharp edge the two-hook split exists to make impossible to hit by accident:
        // MidiSequencer's hook REPLACES delivery, so a filter that only looks at a message and
        // returns stops the music dead.
        var synthesizer = new RecordingSynthesizer();
        using var provider = new MidiSynthDataProvider(synthesizer);
        var seen = new List<(int channel, int command, int data1, int data2)>();
        provider.MessageFilter = (_, channel, command, data1, data2) => seen.Add((channel, command, data1, data2));

        //Act
        provider.Start(BuildThreeNoteSequence(), loop: false);
        RenderOneSecond(provider);

        //Assert
        NoteOnsIn(seen).Should().Equal(60, 62, 64);
        synthesizer.Received.Should().BeEmpty();
    }

    [Fact]
    public void a_filter_that_forwards_reaches_the_synthesizer()
    {
        //Arrange
        var synthesizer = new RecordingSynthesizer();
        using var provider = new MidiSynthDataProvider(synthesizer);

        // Transpose every note up an octave on the way through - the point of the filter.
        provider.MessageFilter = (synth, channel, command, data1, data2) =>
            synth.ProcessMidiMessage(channel, command, command is 0x90 or 0x80 ? data1 + 12 : data1, data2);

        //Act
        provider.Start(BuildThreeNoteSequence(), loop: false);
        RenderOneSecond(provider);

        //Assert
        NoteOnsIn(synthesizer.Received).Should().Equal(72, 74, 76);
    }

    [Fact]
    public void the_observer_still_runs_when_a_filter_is_installed()
    {
        //Arrange
        var synthesizer = new RecordingSynthesizer();
        using var provider = new MidiSynthDataProvider(synthesizer);
        var observed = new List<(int channel, int command, int data1, int data2)>();

        provider.MessageFilter = (synth, channel, command, data1, data2) =>
            synth.ProcessMidiMessage(channel, command, data1, data2);
        provider.MessageObserver = (channel, command, data1, data2) => observed.Add((channel, command, data1, data2));

        //Act
        provider.Start(BuildThreeNoteSequence(), loop: false);
        RenderOneSecond(provider);

        //Assert
        NoteOnsIn(observed).Should().Equal(60, 62, 64);
        NoteOnsIn(synthesizer.Received).Should().Equal(60, 62, 64);
    }

    [Fact]
    public void clearing_both_hooks_restores_direct_delivery()
    {
        //Arrange
        var synthesizer = new RecordingSynthesizer();
        using var provider = new MidiSynthDataProvider(synthesizer);
        provider.MessageFilter = (_, _, _, _, _) => { };
        provider.MessageObserver = (_, _, _, _) => { };

        //Act
        provider.MessageFilter = null;
        provider.MessageObserver = null;
        provider.Start(BuildThreeNoteSequence(), loop: false);
        RenderOneSecond(provider);

        //Assert
        NoteOnsIn(synthesizer.Received).Should().Equal(60, 62, 64);
    }

    [Fact]
    public void send_midi_message_reaches_the_synthesizer()
    {
        //Arrange
        var synthesizer = new RecordingSynthesizer();
        using var provider = new MidiSynthDataProvider(synthesizer);

        //Act
        provider.SendMidiMessage(3, 0xB0, 7, 64);

        //Assert
        synthesizer.Received.Should().ContainSingle();
        synthesizer.Received[0].Should().Be((3, 0xB0, 7, 64));
    }

    [Fact]
    public void send_midi_message_does_not_pass_the_observer()
    {
        //Arrange
        // The observer reports what the SEQUENCE played. A message the caller sent itself is not
        // something it needs telling about, and reporting it would make note counting wrong.
        var synthesizer = new RecordingSynthesizer();
        using var provider = new MidiSynthDataProvider(synthesizer);
        var observed = 0;
        provider.MessageObserver = (_, _, _, _) => observed++;

        //Act
        provider.SendMidiMessage(0, 0xB0, 7, 100);

        //Assert
        observed.Should().Be(0);
        synthesizer.Received.Should().ContainSingle();
    }

    [Fact]
    public void send_midi_message_is_ignored_after_disposal()
    {
        //Arrange
        var synthesizer = new RecordingSynthesizer();
        var provider = new MidiSynthDataProvider(synthesizer);
        provider.Dispose();

        //Act
        var act = () => provider.SendMidiMessage(0, 0x90, 60, 100);

        //Assert
        act.Should().NotThrow();
        synthesizer.Received.Should().BeEmpty();
    }

    [Fact]
    public void speed_round_trips_and_defaults_to_one()
    {
        //Arrange
        var synthesizer = new RecordingSynthesizer();
        using var provider = new MidiSynthDataProvider(synthesizer);

        //Act
        var initial = provider.Speed;
        provider.Speed = 2.5f;

        //Assert
        initial.Should().Be(1.0f);
        provider.Speed.Should().Be(2.5f);
    }

    [Fact]
    public void speed_scales_how_far_the_sequence_advances_per_rendered_second()
    {
        //Arrange
        var synthesizer = new RecordingSynthesizer();
        using var provider = new MidiSynthDataProvider(synthesizer);
        provider.Speed = 2.0f;

        //Act
        // One second of audio at double speed should carry the transport two seconds into the
        // sequence - which is measured here in rendered samples, not wall-clock time.
        provider.Start(BuildThreeNoteSequence(), loop: false);
        RenderOneSecond(provider);

        //Assert
        provider.CurrentTime.TotalSeconds.Should().BeApproximately(2.0, 0.05);
    }

    // ----- helpers -----

    /// <summary>Renders one second of audio through the provider, which is what advances the sequence.</summary>
    private static void RenderOneSecond(MidiSynthDataProvider provider)
        => provider.ReadBytes(new float[SampleRate * 2]);

    /// <summary>The note numbers of the note-on messages in the order they arrived.</summary>
    private static List<int> NoteOnsIn(List<(int channel, int command, int data1, int data2)> messages)
    {
        var notes = new List<int>();
        foreach (var message in messages)
        {
            if (message.command == 0x90 && message.data2 > 0)
            {
                notes.Add(message.data1);
            }
        }

        return notes;
    }

    /// <summary>Three notes 0.1s apart - short enough that one rendered second covers them all.</summary>
    private static MidiSequence BuildThreeNoteSequence()
    {
        const int ticksPerQuarter = 1000;   // at 120 BPM: 1 tick = 0.5 ms
        const int stepTicks = 200;          // 0.10 s
        const int channel = 1;

        var events = new MidiEventCollection(1, ticksPerQuarter);

        var tick = 0L;
        foreach (var note in new[] { 60, 62, 64 })
        {
            events.AddEvent(new NoteOnEvent(tick, channel, note, 100, stepTicks / 2), 1);
            events.AddEvent(new NoteEvent(tick + (stepTicks / 2), channel, MidiCommandCode.NoteOff, note, 0), 1);
            tick += stepTicks;
        }

        events.PrepareForExport();
        return MidiSequence.FromEvents(events);
    }

    /// <summary>
    /// A synthesizer that records the messages it is handed and renders silence, so a test can
    /// assert on delivery without an audio device or a real SoundFont.
    /// </summary>
    private sealed class RecordingSynthesizer : IMidiSynthesizer
    {
        public List<(int channel, int command, int data1, int data2)> Received { get; } = new();

        public int SampleRate => MidiSynthDataProviderTests.SampleRate;

        public int BlockSize => 64;

        public int ActiveVoiceCount => 0;

        public float MasterVolume { get; set; } = 1.0f;

        public void ProcessMidiMessage(int channel, int command, int data1, int data2)
            => Received.Add((channel, command, data1, data2));

        public void NoteOffAll(bool immediate) { }

        public void Reset() { }

        public void Render(Span<float> left, Span<float> right)
        {
            left.Clear();
            right.Clear();
        }
    }
}
