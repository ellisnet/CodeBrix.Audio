using System;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
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
        var act = () => SoundFontRenderer.Render((SoundFont)null, sequence);

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

    // ----- rendering through the writer registry -----

    [Fact]
    public void render_to_file_picks_the_writer_by_extension()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var sequence = BuildSingleNoteSequence();
        var path = Path.Combine(Path.GetTempPath(), $"codebrix-render-{Guid.NewGuid():N}.aiff");

        try
        {
            //Act
            SoundFontRenderer.RenderToFile(soundFont, sequence, path, 22050);

            //Assert
            // Nothing named AIFF anywhere in the call: the extension found the writer.
            using var reader = new AiffFileReader(path);
            reader.WaveFormat.SampleRate.Should().Be(22050);
            reader.WaveFormat.Channels.Should().Be(2);
            reader.WaveFormat.BitsPerSample.Should().Be(16);
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
    public void render_to_file_writes_the_same_float_wav_the_wav_specific_method_does()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var sequence = BuildSingleNoteSequence();
        var throughTheRegistry = Path.Combine(Path.GetTempPath(), $"codebrix-render-{Guid.NewGuid():N}.wav");
        var throughTheOldWay = Path.Combine(Path.GetTempPath(), $"codebrix-render-{Guid.NewGuid():N}.wav");

        try
        {
            //Act
            SoundFontRenderer.RenderToFile(soundFont, sequence, throughTheRegistry, 22050);
            SoundFontRenderer.RenderToWavFile(soundFont, sequence, throughTheOldWay, 22050);

            //Assert
            // The existing RenderToWav* family keeps working and keeps writing exactly this, so
            // the new road is additive rather than a change of behaviour.
            File.ReadAllBytes(throughTheRegistry).Should().Equal(File.ReadAllBytes(throughTheOldWay));
        }
        finally
        {
            foreach (var path in new[] { throughTheRegistry, throughTheOldWay })
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
        }
    }

    [Fact]
    public void render_to_file_writes_sixteen_bit_pcm_when_the_format_asks_for_it()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var sequence = BuildSingleNoteSequence();
        var path = Path.Combine(Path.GetTempPath(), $"codebrix-render-{Guid.NewGuid():N}.wav");

        try
        {
            //Act
            SoundFontRenderer.RenderToFile(
                soundFont, sequence, path, new WaveFormat(22050, 16, 2));

            //Assert
            using var reader = new WaveFileReader(path);
            reader.WaveFormat.Encoding.Should().Be(WaveFormatEncoding.Pcm);
            reader.WaveFormat.BitsPerSample.Should().Be(16);
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
    public void render_to_file_through_a_synthesizer_picks_the_writer_by_extension()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var synthesizer = new SoundFontSynthesizer(soundFont, 22050);
        var sequence = BuildSingleNoteSequence();
        var path = Path.Combine(Path.GetTempPath(), $"codebrix-render-{Guid.NewGuid():N}.wav");

        try
        {
            //Act
            SoundFontRenderer.RenderToFile(synthesizer, sequence, path);

            //Assert
            using var reader = new WaveFileReader(path);
            reader.WaveFormat.SampleRate.Should().Be(22050);
            reader.WaveFormat.Encoding.Should().Be(WaveFormatEncoding.IeeeFloat);
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
    public void render_to_file_rejects_an_extension_nothing_is_registered_for()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var sequence = BuildSingleNoteSequence();
        var path = Path.Combine(Path.GetTempPath(), $"codebrix-render-{Guid.NewGuid():N}.xyz");

        //Act
        var act = () => SoundFontRenderer.RenderToFile(soundFont, sequence, path, 22050);

        //Assert
        act.Should().Throw<NotSupportedException>().WithMessage("*'.xyz'*");

        // And it says so before creating anything, so there is no empty file to clean up.
        File.Exists(path).Should().BeFalse();
    }

    [Fact]
    public void render_to_stream_writes_through_the_registry_and_leaves_the_stream_open()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var sequence = BuildSingleNoteSequence();
        using var stream = new MemoryStream();

        //Act
        SoundFontRenderer.RenderToStream(soundFont, sequence, stream, ".wav", 22050);

        //Assert
        stream.CanRead.Should().BeTrue();
        stream.Position = 0;
        using var reader = new WaveFileReader(stream);
        reader.WaveFormat.SampleRate.Should().Be(22050);
        reader.SampleCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void render_to_stream_through_a_synthesizer_takes_the_format_from_an_extension()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var synthesizer = new SoundFontSynthesizer(soundFont, 22050);
        var sequence = BuildSingleNoteSequence();
        using var stream = new MemoryStream();

        //Act
        SoundFontRenderer.RenderToStream(synthesizer, sequence, stream, "anything.aif");

        //Assert
        stream.Position = 0;
        using var reader = new AiffFileReader(stream);
        reader.WaveFormat.SampleRate.Should().Be(22050);
        reader.SampleCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void rendering_to_a_file_refuses_a_format_that_is_not_stereo()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var sequence = BuildSingleNoteSequence();
        var path = Path.Combine(Path.GetTempPath(), $"codebrix-render-{Guid.NewGuid():N}.wav");

        //Act
        var act = () => SoundFontRenderer.RenderToFile(
            soundFont, sequence, path, WaveFormat.CreateIeeeFloatWaveFormat(22050, 1));

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*2 channels*");
    }

    [Fact]
    public void rendering_a_synthesizer_refuses_a_format_at_another_sample_rate()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var synthesizer = new SoundFontSynthesizer(soundFont, 22050);
        var sequence = BuildSingleNoteSequence();
        using var stream = new MemoryStream();

        //Act
        var act = () => SoundFontRenderer.RenderToStream(
            synthesizer, sequence, stream, ".wav", WaveFormat.CreateIeeeFloatWaveFormat(44100, 2));

        //Assert
        // Rendering does not resample, and a file whose header says one rate while its samples
        // were made at another plays at the wrong pitch.
        act.Should().Throw<ArgumentException>().WithMessage("*does not resample*");
    }

    [Fact]
    public void rendering_to_a_file_rejects_the_usual_null_arguments()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var sequence = BuildSingleNoteSequence();

        //Act
        var nullPath = () => SoundFontRenderer.RenderToFile(soundFont, sequence, (string)null);
        var nullSynthesizer = () => SoundFontRenderer.RenderToFile(
            (IMidiSynthesizer)null, sequence, "tune.wav");
        var nullStream = () => SoundFontRenderer.RenderToStream(soundFont, sequence, null, ".wav");

        //Assert
        nullPath.Should().Throw<ArgumentNullException>();
        nullSynthesizer.Should().Throw<ArgumentNullException>();
        nullStream.Should().Throw<ArgumentNullException>();
    }
}
