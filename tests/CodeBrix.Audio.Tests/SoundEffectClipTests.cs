using System;
using System.IO;
using System.Threading;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests for <see cref="SoundEffectClip"/>, the decode-once one-shot player.
/// </summary>
/// <remarks>
/// Loading a clip starts the shared output device, so these run in the same non-parallel
/// collection as the other device-touching tests. Loading and decoding does not emit sound;
/// the tests that actually play are opt-in behind
/// <c>CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1</c>, like the rest of the suite.
/// </remarks>
[Collection("SharedAudioOutput")]
public class SoundEffectClipTests
{
    private const string PlaybackEnvVar = "CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS";

    private static bool PlaybackTestsEnabled =>
        Environment.GetEnvironmentVariable(PlaybackEnvVar) == "1";

    private static string TempWavPath() =>
        Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");

    [Theory]
    [InlineData("vorbis-tone-stereo-44100.ogg")]
    [InlineData("vorbis-tone-mono-22050.ogg")]
    [InlineData("flac-tone-stereo-16bit-44100-midside.flac")]
    [InlineData("flac-tone-stereo-24bit-48000.flac")]
    public void Loads_every_supported_format_regardless_of_its_sample_rate(string fixture)
    {
        //Arrange / Act
        // The point of the type: an asset pack's files load whatever they are and whatever rate
        // they were recorded at, because the decode step converts to the output's format.
        using var clip = SoundEffectClip.Load(TestAssets.Path(fixture));

        //Assert
        clip.Duration.TotalSeconds.Should().BeApproximately(0.25, 0.02);
        clip.SampleRate.Should().Be(SharedAudioOutput.SampleRate);
        clip.Channels.Should().BeGreaterThan(0);
        clip.ActiveVoiceCount.Should().Be(0);
    }

    [Fact]
    public void Loads_from_a_byte_array_and_from_a_stream()
    {
        //Arrange
        var bytes = File.ReadAllBytes(TestAssets.Path(TestAssets.VorbisToneStereo));

        //Act
        using var fromBytes = SoundEffectClip.Load(bytes);
        using var stream = new MemoryStream(bytes);
        using var fromStream = SoundEffectClip.Load(stream);

        //Assert
        fromBytes.Duration.Should().Be(fromStream.Duration);
        stream.CanRead.Should().BeTrue(); // the stream is read but not taken over
    }

    [Fact]
    public void A_wav_clip_matches_the_duration_of_the_file_it_came_from()
    {
        //Arrange
        var path = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".wav");
        TestAudio.WriteSineWaveFile(path, seconds: 0.4);

        try
        {
            //Act
            using var clip = SoundEffectClip.Load(path);

            //Assert
            clip.Duration.TotalSeconds.Should().BeApproximately(0.4, 0.02);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Playing_a_disposed_clip_throws()
    {
        //Arrange
        var clip = SoundEffectClip.Load(TestAssets.Path(TestAssets.VorbisToneStereo));
        clip.Dispose();

        //Act
        var play = () => clip.Play();

        //Assert
        play.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Disposing_twice_is_harmless()
    {
        //Arrange
        var clip = SoundEffectClip.Load(TestAssets.Path(TestAssets.VorbisToneStereo));

        //Act
        var disposeTwice = () => { clip.Dispose(); clip.Dispose(); };

        //Assert
        disposeTwice.Should().NotThrow();
    }

    [Fact]
    public void Overlapping_plays_each_get_their_own_voice()
    {
        Assert.SkipUnless(PlaybackTestsEnabled,
            $"Set {PlaybackEnvVar}=1 to run the tests that open a real audio device and make sound.");

        //Arrange
        using var audible = new AudibleTestScope();
        var path = TestAudio.WriteCloseEncountersWaveFile(TempWavPath());

        try
        {
            using var clip = SoundEffectClip.Load(path);

            //Act
            // Three voices started together, so they sound in UNISON - one clean statement of the
            // motif, just louder. (Staggering them proves the same thing about the voice count and
            // sounds like a round, which makes the tune unrecognisable.)
            clip.Play(0.3f);
            clip.Play(0.3f);
            clip.Play(0.3f);
            var duringPlayback = clip.ActiveVoiceCount;

            Thread.Sleep(TestAudio.CloseEncountersDuration + TimeSpan.FromMilliseconds(500));

            //Assert
            duringPlayback.Should().Be(3);
            clip.ActiveVoiceCount.Should().Be(0); // each voice retires itself when it finishes
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Plays_a_clip_whose_rate_differs_from_the_output_device()
    {
        Assert.SkipUnless(PlaybackTestsEnabled,
            $"Set {PlaybackEnvVar}=1 to run the tests that open a real audio device and make sound.");

        //Arrange
        using var audible = new AudibleTestScope();
        // The motif rendered at 22.05 kHz mono, played on a device running at something else:
        // this is the case that throws on a path with no rate conversion. It should sound
        // exactly like the other renderings - same five tones, same pitches.
        var path = TestAudio.WriteCloseEncountersWaveFile(TempWavPath(), sampleRate: 22050, channels: 1);

        try
        {
            using var clip = SoundEffectClip.Load(path);

            //Act
            var play = () => clip.Play();

            //Assert
            play.Should().NotThrow();
            Thread.Sleep(TestAudio.CloseEncountersDuration + TimeSpan.FromMilliseconds(250));
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void StopAll_silences_everything_immediately()
    {
        Assert.SkipUnless(PlaybackTestsEnabled,
            $"Set {PlaybackEnvVar}=1 to run the tests that open a real audio device and make sound.");

        //Arrange
        using var audible = new AudibleTestScope();
        // Deliberately cut off mid-motif: you should hear it start and stop dead, not run on.
        var path = TestAudio.WriteCloseEncountersWaveFile(TempWavPath());

        try
        {
            using var clip = SoundEffectClip.Load(path);
            clip.Play();
            clip.Play();
            Thread.Sleep(TimeSpan.FromSeconds(TestAudio.CloseEncountersNoteSeconds * 2));

            //Act
            clip.StopAll();

            //Assert
            clip.ActiveVoiceCount.Should().Be(0);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
