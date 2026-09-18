using System;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

/// <summary>
/// Covers <see cref="MidiSynthDataProvider"/>'s end-of-stream contract, with no audio device: the
/// provider is pulled directly, the way the engine's player pulls it. A non-looping sequence must
/// end the stream - a zero-length read - once its last message has been dispatched and the final
/// voices have rung out, because the engine treats that zero-length read as its ONLY end-of-stream
/// signal; without it the player is stuck Playing forever and PlaybackEnded never fires.
/// </summary>
public sealed class MidiSynthDataProviderTests
{
    private const int SampleRate = 44100;

    [Fact]
    public void a_non_looping_sequence_ends_the_stream_after_the_voices_ring_out()
    {
        //Arrange
        using var provider = CreateProvider();
        var endRaised = 0;
        provider.EndOfStreamReached += (_, _) => endRaised++;
        provider.Start(BuildShortSequence(), loop: false);

        //Act - budget: the 0.2s sequence, the 10s stuck-voice tail cap, and margin.
        var framesToEnd = PumpUntilEnd(provider, maxSeconds: 12);

        //Assert
        // The end must come from the voices ringing out - well under the stuck-voice tail cap. A
        // stream that only ends via the cap would make every sequence "play" ten extra seconds.
        framesToEnd.Should().NotBeNull();
        (framesToEnd.Value / (double)SampleRate).Should().BeLessThan(5);
        endRaised.Should().Be(1);
    }

    [Fact]
    public void a_looping_sequence_never_ends_the_stream()
    {
        //Arrange
        using var provider = CreateProvider();
        var endRaised = 0;
        provider.EndOfStreamReached += (_, _) => endRaised++;
        provider.Start(BuildShortSequence(), loop: true);

        //Act - some fifteen times around the 0.2s sequence.
        var framesToEnd = PumpUntilEnd(provider, maxSeconds: 3);

        //Assert
        framesToEnd.Should().BeNull();
        endRaised.Should().Be(0);
    }

    [Fact]
    public void the_stream_stays_ended_and_the_position_freezes_once_it_ends()
    {
        //Arrange
        using var provider = CreateProvider();
        provider.Start(BuildShortSequence(), loop: false);
        PumpUntilEnd(provider, maxSeconds: 12).Should().NotBeNull();
        var positionAtEnd = provider.Position;

        //Act
        var read = provider.ReadBytes(new float[4096]);

        //Assert
        read.Should().Be(0);
        provider.Position.Should().Be(positionAtEnd);
    }

    [Fact]
    public void seeking_back_after_the_end_resumes_the_stream()
    {
        //Arrange
        using var provider = CreateProvider();
        var endRaised = 0;
        provider.EndOfStreamReached += (_, _) => endRaised++;
        provider.Start(BuildShortSequence(), loop: false);
        PumpUntilEnd(provider, maxSeconds: 12).Should().NotBeNull();

        //Act
        provider.Seek(0);
        var read = provider.ReadBytes(new float[4096]);
        var endedAgain = PumpUntilEnd(provider, maxSeconds: 12);

        //Assert
        read.Should().Be(4096);
        endedAgain.Should().NotBeNull();
        endRaised.Should().Be(2);
    }

    [Fact]
    public void the_tempo_source_follows_the_sequences_tempo_map_as_it_renders()
    {
        //Arrange - 480 ppq: 100 BPM for the first two beats (1.2 s), then 150 BPM.
        using var provider = CreateProvider();
        var tempo = new TempoSource();
        provider.Tempo = tempo;
        provider.Start(MultiTrackTestSong.BuildTempoSequence([(0, 100.0), (960, 150.0)], lengthTicks: 4800), loop: false);

        //Act
        var buffer = new float[SampleRate * 2];
        provider.ReadBytes(buffer);
        var early = (tempo.BeatsPerMinute, tempo.BeatPosition);
        provider.ReadBytes(buffer);
        var late = (tempo.BeatsPerMinute, tempo.BeatPosition);

        //Assert
        early.BeatsPerMinute.Should().BeApproximately(100.0, 0.05);
        early.BeatPosition.Should().BeApproximately(100.0 / 60.0, 1e-2);
        late.BeatsPerMinute.Should().BeApproximately(150.0, 0.05);
        late.BeatPosition.Should().BeApproximately(4.0, 1e-2);
    }

    [Fact]
    public void a_stream_provider_reports_a_growing_length()
    {
        //Arrange - a stream's length is its horizon, so it follows the producer rather than
        // standing still the way a finished sequence's does.
        using var provider = CreateProvider();
        var stream = new MidiStream(480);
        stream.AppendNote(0, 1, 72, 100, 960);

        //Act
        provider.Start(stream);
        var early = provider.Length;

        stream.AppendNote(960, 1, 74, 100, 960);
        var later = provider.Length;

        //Assert - 960 ticks at 480 per quarter and 120 BPM is a second, and then two.
        (early / (double)SampleRate).Should().BeApproximately(1.0, 0.01);
        (later / (double)SampleRate).Should().BeApproximately(2.0, 0.01);
        later.Should().BeGreaterThan(early);
    }

    [Fact]
    public void a_stream_provider_renders_silence_not_zero_while_starved()
    {
        //Arrange - a quarter of a second written and nothing more coming. A zero-length read is the
        // engine's ONE end-of-stream signal, so a starved stream that returned zero would end the
        // music for good instead of waiting for the producer.
        using var provider = CreateProvider();
        var endRaised = 0;
        provider.EndOfStreamReached += (_, _) => endRaised++;

        var stream = new MidiStream(480);
        stream.AppendNote(0, 1, 72, 100, 240);
        provider.Start(stream);

        //Act - some three seconds of pulling, far past the quarter second that was written.
        var buffer = new float[4096];
        var reads = 0;
        var shortRead = false;
        for (var i = 0; i < 64; i++)
        {
            if (provider.ReadBytes(buffer) != buffer.Length)
            {
                shortRead = true;
                break;
            }

            reads++;
        }

        //Assert
        shortRead.Should().BeFalse();
        reads.Should().Be(64);
        endRaised.Should().Be(0);
        provider.IsStarved.Should().BeTrue();

        // ... and what it handed back at the end was silence: the note is long over.
        Peak(buffer).Should().BeLessThan(1e-4f);
    }

    [Fact]
    public void a_stream_provider_ends_only_after_complete_and_the_last_voice()
    {
        //Arrange
        using var provider = CreateProvider();
        var endRaised = 0;
        provider.EndOfStreamReached += (_, _) => endRaised++;

        var stream = new MidiStream(1000);
        stream.AppendNote(0, 1, 72, 100, 400);
        provider.Start(stream);

        //Act
        var whileWriting = PumpUntilEnd(provider, maxSeconds: 3);
        var raisedWhileWriting = endRaised;
        stream.Complete();
        var afterComplete = PumpUntilEnd(provider, maxSeconds: 12);

        //Assert - waiting for a producer that has stopped without completing is by design; the
        // producer's Complete is what ends the music.
        whileWriting.Should().BeNull();
        raisedWhileWriting.Should().Be(0);

        afterComplete.Should().NotBeNull();
        endRaised.Should().Be(1);
    }

    [Fact]
    public void set_looping_is_ignored_for_a_stream()
    {
        //Arrange - a growing timeline has no end to loop at.
        using var provider = CreateProvider();
        var stream = new MidiStream(1000);
        stream.AppendNote(0, 1, 72, 100, 400);
        stream.Complete();
        provider.Start(stream);

        //Act
        provider.SetLooping(true);

        //Assert - had looping taken, the stream would never end and this would run out of budget.
        PumpUntilEnd(provider, maxSeconds: 12).Should().NotBeNull();
    }

    [Fact]
    public void tempo_and_beats_per_bar_are_published_from_a_stream()
    {
        //Arrange - 480 ppq: 100 BPM for the first two beats (1.2 s), then 150, in three-four. The
        // long note keeps the horizon well ahead of the head, so nothing starves.
        using var provider = CreateProvider();
        var tempo = new TempoSource();
        provider.Tempo = tempo;

        var stream = new MidiStream(480);
        stream.AppendTempo(0, 100);
        stream.Append(new TimeSignatureEvent(0, 3, 2, 24, 8));
        stream.AppendNote(0, 1, 72, 100, 4800);
        stream.AppendTempo(960, 150);
        provider.Start(stream);

        //Act
        var buffer = new float[SampleRate * 2];
        provider.ReadBytes(buffer);
        var early = (tempo.BeatsPerMinute, tempo.BeatPosition, tempo.BeatsPerBar);
        provider.ReadBytes(buffer);
        var late = (tempo.BeatsPerMinute, tempo.BeatPosition, tempo.BeatsPerBar);

        //Assert - the same readings the sequence path publishes, plus the beats per bar a
        // MidiSequence has never been able to fill.
        early.BeatsPerMinute.Should().BeApproximately(100.0, 0.05);
        early.BeatPosition.Should().BeApproximately(100.0 / 60.0, 1e-2);
        early.BeatsPerBar.Should().Be(3);

        late.BeatsPerMinute.Should().BeApproximately(150.0, 0.05);
        late.BeatPosition.Should().BeApproximately(4.0, 1e-2);
        late.BeatsPerBar.Should().Be(3);
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

    private static MidiSynthDataProvider CreateProvider()
    {
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        return new MidiSynthDataProvider(new SoundFontSynthesizer(soundFont, SampleRate));
    }

    /// <summary>A single 0.2s note; the whole sequence is 0.2s long.</summary>
    private static MidiSequence BuildShortSequence()
    {
        const int ticksPerQuarter = 1000;   // at the default 120 BPM: 1 tick = 0.5 ms
        var events = new MidiEventCollection(1, ticksPerQuarter);
        events.AddEvent(new NoteOnEvent(0, 1, 72, 100, 400), 1);
        events.AddEvent(new NoteEvent(400, 1, MidiCommandCode.NoteOff, 72, 0), 1);
        events.PrepareForExport();
        return MidiSequence.FromEvents(events);
    }

    // Pulls the provider the way the engine does - interleaved stereo, a buffer at a time - until
    // it signals end-of-stream. Returns the frames rendered before the end, or null if the frame
    // budget ran out with the stream still going.
    private static long? PumpUntilEnd(MidiSynthDataProvider provider, double maxSeconds)
    {
        var buffer = new float[4096];
        var budget = (long)(maxSeconds * SampleRate);
        var frames = 0L;
        while (frames < budget)
        {
            var read = provider.ReadBytes(buffer);
            if (read == 0)
            {
                return frames;
            }

            frames += read / 2;
        }

        return null;
    }
}
