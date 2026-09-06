using System;
using System.Collections.Generic;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Playback.Internal;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Covers the machinery under <see cref="MultiTrackPlayer"/>: the lockstep mix itself and the data
/// provider that feeds it to the engine. Testing them directly is what lets the TRANSPORT be
/// exercised - position, seek, end of stream, looping - with no audio device anywhere.
/// </summary>
public class MultiTrackMixTests
{
    private const int Rate = MultiTrackTestSong.SampleRate;

    [Fact]
    public void the_mix_is_as_long_as_the_last_frame_any_track_reaches()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        var early = new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.5f, 4410));
        var late = new AudioTrack(song.WriteConstantWav("b.wav", 0.5f, 0.5f, 4410))
        {
            Offset = TimeSpan.FromSeconds(0.2),
        };

        //Act
        using var mix = new MultiTrackMix([early, late], Rate, null);

        //Assert
        mix.LengthFrames.Should().Be(8820 + 4410);
        mix.Position.Should().Be(0);
        mix.VoiceCount.Should().Be(2);
    }

    [Fact]
    public void rendering_advances_the_position_by_exactly_what_was_asked_for()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        using var mix = BuildMix(song, 44100);
        var buffer = new float[1000 * 2];

        //Act
        mix.Render(buffer);
        mix.Render(buffer);

        //Assert
        mix.Position.Should().Be(2000);
    }

    [Fact]
    public void seeking_moves_every_track_to_the_same_place()
    {
        //Arrange - two tones, so a track left behind would show up as a phase error.
        using var song = MultiTrackTestSong.Create();
        var first = new AudioTrack(song.WriteSineWav("one.wav", 220, 44100));
        var second = new AudioTrack(song.WriteSineWav("two.wav", 331, 44100));

        using var whole = new MultiTrackMix([first, second], Rate, null);
        var full = new float[44100 * 2];
        whole.Render(full);

        var third = new AudioTrack(song.WriteSineWav("three.wav", 220, 44100));
        var fourth = new AudioTrack(song.WriteSineWav("four.wav", 331, 44100));
        using var seeked = new MultiTrackMix([third, fourth], Rate, null);

        //Act
        seeked.Seek(10000);
        var tail = new float[5000 * 2];
        seeked.Render(tail);

        //Assert
        seeked.Position.Should().Be(15000);
        for (var i = 0; i < tail.Length; i++)
        {
            tail[i].Should().BeApproximately(full[10000 * 2 + i], 1e-6f);
        }
    }

    [Fact]
    public void the_provider_ends_the_stream_once_the_song_and_its_tail_are_done()
    {
        //Arrange - a tenth of a second of song and no tail.
        using var song = MultiTrackTestSong.Create();
        var provider = new MultiTrackDataProvider(BuildMix(song, 4410), tailFrames: 0, looping: false);
        using (provider)
        {
            var buffer = new float[4410 * 2];

            //Act
            var first = provider.ReadBytes(buffer);
            var second = provider.ReadBytes(buffer);

            //Assert
            first.Should().Be(4410 * 2);
            second.Should().Be(0);
        }
    }

    [Fact]
    public void the_provider_raises_end_of_stream_exactly_once()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        var provider = new MultiTrackDataProvider(BuildMix(song, 4410), tailFrames: 0, looping: false);
        using (provider)
        {
            var raised = 0;
            provider.EndOfStreamReached += (_, _) => raised++;
            var buffer = new float[4410 * 2];

            //Act
            provider.ReadBytes(buffer);
            provider.ReadBytes(buffer);
            provider.ReadBytes(buffer);

            //Assert
            raised.Should().Be(1);
        }
    }

    [Fact]
    public void the_tail_keeps_the_stream_alive_past_the_end_of_the_song()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        var provider = new MultiTrackDataProvider(BuildMix(song, 4410), tailFrames: 2205, looping: false);
        using (provider)
        {
            var buffer = new float[4410 * 2];

            //Act
            var first = provider.ReadBytes(buffer);
            var second = provider.ReadBytes(buffer);
            var third = provider.ReadBytes(buffer);

            //Assert
            first.Should().Be(4410 * 2);
            second.Should().Be(4410 * 2);
            third.Should().Be(0);
            provider.TotalFrames.Should().Be(4410 + 2205);
            provider.LengthFrames.Should().Be(4410);
        }
    }

    [Fact]
    public void a_looping_provider_wraps_back_to_the_start_instead_of_ending()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        var provider = new MultiTrackDataProvider(BuildMix(song, 1000), tailFrames: 0, looping: true);
        using (provider)
        {
            var buffer = new float[2500 * 2];

            //Act
            var read = provider.ReadBytes(buffer);

            //Assert - two and a half passes of a constant stem, all of it filled.
            read.Should().Be(2500 * 2);
            buffer.Should().OnlyContain(sample => sample == 0.5f);
            provider.PositionFrames.Should().Be(500);
        }
    }

    [Fact]
    public void the_provider_reports_position_and_length_in_samples()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        var provider = new MultiTrackDataProvider(BuildMix(song, 4410), tailFrames: 0, looping: false);
        using (provider)
        {
            //Act
            provider.SeekFrames(1000);

            //Assert
            provider.Position.Should().Be(2000);
            provider.Length.Should().Be(8820);
            provider.SampleRate.Should().Be(Rate);
            provider.CanSeek.Should().BeTrue();
        }
    }

    [Fact]
    public void switching_the_active_source_crossfades_and_leaves_the_midi_in_time()
    {
        //Arrange - a constant recording against a run of notes, so a MIDI source that had restarted
        // at the switch would be playing an audibly different note from the reference.
        using var song = MultiTrackTestSong.Create();
        var sequence = MultiTrackTestSong.BuildNoteSequence([72, 74, 76, 77, 79], noteTicks: 400, stepTicks: 500);

        var track = new AudioTrack(song.WriteConstantWav("a.wav", 0.8f, 0.8f, 44100 * 2));
        track.SetMidiSource(sequence, MultiTrackTestSong.SoundFontFactory());

        var referenceTrack = new MidiTrack(sequence, MultiTrackTestSong.SoundFontFactory());

        using var mix = new MultiTrackMix([track], Rate, null);
        using var reference = new MultiTrackMix([referenceTrack], Rate, null);

        const int switchFrame = 44100;   // one second in
        var buffer = new float[switchFrame * 2];
        var referenceBuffer = new float[switchFrame * 2];

        mix.Render(buffer);
        reference.Render(referenceBuffer);

        //Act
        track.ActiveSource = TrackSource.Midi;

        const int afterFrames = 8192;
        var after = new float[afterFrames * 2];
        var referenceAfter = new float[afterFrames * 2];
        mix.Render(after);
        reference.Render(referenceAfter);

        //Assert - the first frame after the switch is still the recording (the crossfade has only
        // just started), and once the 20 ms fade is over the output is the MIDI reference frame for
        // frame, which it could not be if the synthesizer had restarted.
        after[0].Should().BeApproximately(0.8f, 0.05f);

        var settled = (int)(Rate * 0.04);
        for (var i = settled * 2; i < after.Length; i++)
        {
            after[i].Should().BeApproximately(referenceAfter[i], 1e-5f);
        }
    }

    [Fact]
    public void the_tempo_source_follows_the_tempo_map_as_the_mix_renders()
    {
        //Arrange - 480 ppq, 100 BPM for two beats (1.2 s) then 150 BPM.
        var sequence = MultiTrackTestSong.BuildTempoSequence([(0, 100.0), (960, 150.0)], lengthTicks: 4800);
        var track = new MidiTrack(sequence, MultiTrackTestSong.SoundFontFactory());
        var tempo = new TempoSource();

        using var mix = new MultiTrackMix([track], Rate, tempo);
        var oneSecond = new float[Rate * 2];

        //Act
        mix.Render(oneSecond);
        var early = (tempo.BeatsPerMinute, tempo.BeatPosition);
        mix.Render(oneSecond);
        var late = (tempo.BeatsPerMinute, tempo.BeatPosition);

        //Assert
        early.BeatsPerMinute.Should().BeApproximately(100.0, 0.05);
        early.BeatPosition.Should().BeApproximately(100.0 / 60.0, 1e-3);

        // Two seconds in: two beats at 100 BPM took 1.2 s, and the remaining 0.8 s at 150 BPM is
        // another two beats.
        late.BeatsPerMinute.Should().BeApproximately(150.0, 0.05);
        late.BeatPosition.Should().BeApproximately(4.0, 1e-2);
        tempo.IsPlaying.Should().BeTrue();
    }

    [Fact]
    public void the_tempo_source_reads_the_default_map_when_the_song_has_no_midi()
    {
        //Arrange
        using var song = MultiTrackTestSong.Create();
        var tempo = new TempoSource();

        //Act
        using var mix = new MultiTrackMix([new AudioTrack(song.WriteConstantWav("a.wav", 0.5f, 0.5f, 4410))],
            Rate, tempo);

        //Assert
        mix.TempoMap.IsConstant.Should().BeTrue();
        mix.TempoMap.InitialBeatsPerMinute.Should().Be(120.0);
    }

    private static MultiTrackMix BuildMix(MultiTrackTestSong song, int frames)
    {
        var name = "mix-" + Guid.NewGuid().ToString("N") + ".wav";
        IReadOnlyList<PlayerTrack> tracks = [new AudioTrack(song.WriteConstantWav(name, 0.5f, 0.5f, frames))];
        return new MultiTrackMix(tracks, Rate, null);
    }
}
