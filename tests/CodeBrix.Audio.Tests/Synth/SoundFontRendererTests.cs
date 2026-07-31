using System;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

/// <summary>
/// Covers <see cref="SoundFontRenderer"/> - offline rendering with no audio device involved. This is
/// also the harness the reference-render comparison uses, which is why it is public API rather than a
/// test helper.
/// </summary>
public class SoundFontRendererTests
{
    // The synthetic fixture's first preset is a looping tone; play a note from it long enough
    // to be unambiguously audible in the output.
    private static MidiSequence BuildSingleNoteSequence()
    {
        var collection = new MidiEventCollection(1, 120);
        collection.AddEvent(new NoteOnEvent(0, 1, 60, 127, 240), 1);
        collection.AddEvent(new NoteEvent(240, 1, MidiCommandCode.NoteOff, 60, 0), 1);
        collection.PrepareForExport();
        return MidiSequence.FromEvents(collection);
    }

    [Fact]
    public void render_produces_interleaved_stereo_of_the_expected_length()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var sequence = BuildSingleNoteSequence();

        //Act
        var samples = SoundFontRenderer.Render(soundFont, sequence, 44100);

        //Assert
        var expectedFrames = (int)Math.Ceiling(sequence.Length.TotalSeconds * 44100);
        samples.Length.Should().Be(expectedFrames * 2);
    }

    [Fact]
    public void render_produces_audible_output()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var sequence = BuildSingleNoteSequence();

        //Act
        var samples = SoundFontRenderer.Render(soundFont, sequence, 44100);

        //Assert
        // A rendered note must actually make sound. Silence here means the SoundFont parsed but
        // nothing reached the voice engine - the failure mode that looks like success.
        samples.Max(Math.Abs).Should().BeGreaterThan(0.0001f);
    }

    [Fact]
    public void render_honours_the_requested_tail()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var sequence = BuildSingleNoteSequence();

        //Act
        var withoutTail = SoundFontRenderer.Render(soundFont, sequence, 22050);
        var withTail = SoundFontRenderer.Render(soundFont, sequence, 22050, TimeSpan.FromSeconds(1));

        //Assert
        withTail.Length.Should().Be(withoutTail.Length + 22050 * 2);
    }

    [Theory]
    [InlineData(22050)]
    [InlineData(44100)]
    [InlineData(48000)]
    public void render_scales_with_the_sample_rate(int sampleRate)
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var sequence = BuildSingleNoteSequence();

        //Act
        var samples = SoundFontRenderer.Render(soundFont, sequence, sampleRate);

        //Assert
        var expectedFrames = (int)Math.Ceiling(sequence.Length.TotalSeconds * sampleRate);
        samples.Length.Should().Be(expectedFrames * 2);
    }

    [Fact]
    public void render_to_wav_file_writes_a_readable_stereo_float_wav()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var sequence = BuildSingleNoteSequence();
        var path = Path.Combine(Path.GetTempPath(), $"codebrix-render-{Guid.NewGuid():N}.wav");

        try
        {
            //Act
            SoundFontRenderer.RenderToWavFile(soundFont, sequence, path, 44100);

            //Assert
            using var reader = new WaveFileReader(path);
            reader.WaveFormat.SampleRate.Should().Be(44100);
            reader.WaveFormat.Channels.Should().Be(2);
            reader.WaveFormat.Encoding.Should().Be(WaveFormatEncoding.IeeeFloat);
            reader.SampleCount.Should().BeGreaterThan(0);
        }
        finally
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }
    }

    [Fact]
    public void render_to_wav_stream_can_leave_the_stream_open()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var sequence = BuildSingleNoteSequence();
        using var stream = new MemoryStream();

        //Act
        SoundFontRenderer.RenderToWavStream(soundFont, sequence, stream, 22050, leaveOpen: true);

        //Assert
        stream.CanRead.Should().BeTrue();
        stream.Length.Should().BeGreaterThan(44);
    }

    [Fact]
    public void render_rejects_a_null_soundfont()
    {
        //Arrange
        var sequence = BuildSingleNoteSequence();

        //Act
        var act = () => SoundFontRenderer.Render(null, sequence);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void render_rejects_a_non_positive_sample_rate()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var sequence = BuildSingleNoteSequence();

        //Act
        var act = () => SoundFontRenderer.Render(soundFont, sequence, 0);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void render_rejects_a_negative_tail()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var sequence = BuildSingleNoteSequence();

        //Act
        var act = () => SoundFontRenderer.Render(soundFont, sequence, 44100, TimeSpan.FromSeconds(-1));

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
