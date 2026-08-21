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
