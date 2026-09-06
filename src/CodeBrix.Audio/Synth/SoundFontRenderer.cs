using System;
using System.IO;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.Sfz;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Synth;

/// <summary>
/// Renders MIDI music through a SoundFont or an SFZ instrument without an audio device - to a buffer,
/// or straight to a WAV file. Runs as fast as the machine allows rather than in real time.
/// </summary>
/// <remarks>
/// <para>
/// Use this to bounce a track to disk, to render music in a build step, or to test synthesis on a
/// machine with no sound card. For actually playing music to the speakers, use
/// <c>CodeBrix.Audio.Playback.MidiMusicPlayer</c> instead - this class deliberately has no transport.
/// </para>
/// <para>
/// Output is always 32-bit float stereo at the sample rate you ask for. Synthesis is not bit-exact
/// across refactors of the voice engine, so treat renders as audibly equivalent rather than
/// byte-identical; any regression test over this output wants a tolerance.
/// </para>
/// <para>This file is NOT part of the MeltySynth port; it is CodeBrix code added alongside it.</para>
/// </remarks>
public static class SoundFontRenderer
{
    /// <summary>The sample rate used when none is given.</summary>
    public const int DefaultSampleRate = 44100;

    /// <summary>
    /// Renders a whole sequence to interleaved stereo float samples.
    /// </summary>
    /// <param name="soundFont">The SoundFont to render with.</param>
    /// <param name="sequence">The sequence to render.</param>
    /// <param name="sampleRate">Output sample rate in Hz.</param>
    /// <param name="tail">
    /// Extra time rendered after the sequence ends, so release tails and reverb decay away instead of
    /// being cut off. Pass <see cref="TimeSpan.Zero"/> to stop exactly at the end.
    /// </param>
    /// <returns>Interleaved stereo samples: left, right, left, right, ...</returns>
    /// <exception cref="ArgumentNullException"><paramref name="soundFont"/> or <paramref name="sequence"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive, or <paramref name="tail"/> is negative.</exception>
    public static float[] Render(
        SoundFont soundFont,
        MidiSequence sequence,
        int sampleRate = DefaultSampleRate,
        TimeSpan tail = default)
    {
        if (soundFont == null)
        {
            throw new ArgumentNullException(nameof(soundFont));
        }

        if (sequence == null)
        {
            throw new ArgumentNullException(nameof(sequence));
        }

        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "The sample rate must be positive.");
        }

        if (tail < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(tail), tail, "The tail length cannot be negative.");
        }

        return RenderCore(new SoundFontSynthesizer(soundFont, sampleRate), sequence, sampleRate, tail);
    }

    /// <summary>
    /// Renders a whole sequence through an SFZ instrument to interleaved stereo float samples.
    /// </summary>
    /// <param name="instrument">The SFZ instrument to render with.</param>
    /// <param name="sequence">The sequence to render.</param>
    /// <param name="sampleRate">Output sample rate in Hz.</param>
    /// <param name="tail">
    /// Extra time rendered after the sequence ends, so release tails decay away instead of being cut
    /// off. Pass <see cref="TimeSpan.Zero"/> to stop exactly at the end.
    /// </param>
    /// <returns>Interleaved stereo samples: left, right, left, right, ...</returns>
    /// <exception cref="ArgumentNullException"><paramref name="instrument"/> or <paramref name="sequence"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive, or <paramref name="tail"/> is negative.</exception>
    public static float[] Render(
        SfzInstrument instrument,
        MidiSequence sequence,
        int sampleRate = DefaultSampleRate,
        TimeSpan tail = default)
    {
        if (instrument == null)
        {
            throw new ArgumentNullException(nameof(instrument));
        }

        if (sequence == null)
        {
            throw new ArgumentNullException(nameof(sequence));
        }

        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "The sample rate must be positive.");
        }

        if (tail < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(tail), tail, "The tail length cannot be negative.");
        }

        return RenderCore(new SfzSynthesizer(instrument, sampleRate), sequence, sampleRate, tail);
    }

    /// <summary>
    /// Renders a whole sequence through a Decent Sampler instrument to interleaved stereo float samples.
    /// </summary>
    /// <param name="instrument">The Decent Sampler instrument to render with.</param>
    /// <param name="sequence">The sequence to render.</param>
    /// <param name="sampleRate">Output sample rate in Hz.</param>
    /// <param name="tail">
    /// Extra time rendered after the sequence ends, so release tails decay away instead of being cut
    /// off. Pass <see cref="TimeSpan.Zero"/> to stop exactly at the end. A Decent Sampler zone that
    /// declares no release still rings for half a second, so leave room for it.
    /// </param>
    /// <returns>Interleaved stereo samples: left, right, left, right, ...</returns>
    /// <exception cref="ArgumentNullException"><paramref name="instrument"/> or <paramref name="sequence"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive, or <paramref name="tail"/> is negative.</exception>
    public static float[] Render(
        DecentSamplerInstrument instrument,
        MidiSequence sequence,
        int sampleRate = DefaultSampleRate,
        TimeSpan tail = default)
    {
        if (instrument == null)
        {
            throw new ArgumentNullException(nameof(instrument));
        }

        if (sequence == null)
        {
            throw new ArgumentNullException(nameof(sequence));
        }

        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "The sample rate must be positive.");
        }

        if (tail < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(tail), tail, "The tail length cannot be negative.");
        }

        // Offline streaming: this call is not an audio callback, so it fetches its own streamed frames
        // rather than racing a background reader. An offline render can then never underrun, however
        // fast it runs, and it renders the same samples every time.
        var synthesizer = new DecentSamplerSynthesizer(
            instrument,
            new DecentSamplerSynthesizerSettings(sampleRate)
            {
                StreamingMode = DecentSampler.Streaming.DecentSamplerStreamingMode.Offline,
            });

        if (sequence.TempoMap != null)
        {
            synthesizer.TempoSource.BeatsPerMinute = sequence.TempoMap.InitialBeatsPerMinute;
        }

        return RenderCore(synthesizer, sequence, sampleRate, tail);
    }

    /// <summary>
    /// Renders a whole sequence through a synthesizer of your own - the standalone one in
    /// CodeBrix.Audio.ModestSynth, for instance - to interleaved stereo float samples.
    /// </summary>
    /// <param name="synthesizer">
    /// The synthesizer to render with. The output is produced at its own
    /// <see cref="IMidiSynthesizer.SampleRate"/>, so there is no rate argument.
    /// </param>
    /// <param name="sequence">The sequence to render.</param>
    /// <param name="tail">
    /// Extra time rendered after the sequence ends, so release tails decay away instead of being cut
    /// off. Pass <see cref="TimeSpan.Zero"/> to stop exactly at the end.
    /// </param>
    /// <returns>Interleaved stereo samples: left, right, left, right, ...</returns>
    /// <exception cref="ArgumentNullException"><paramref name="synthesizer"/> or <paramref name="sequence"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The synthesizer's sample rate is not positive, or <paramref name="tail"/> is negative.</exception>
    /// <remarks>
    /// The synthesizer is played from its current state and is not reset first, so render a fresh one
    /// - or call <see cref="IMidiSynthesizer.Reset"/> - when you need the same bytes every time.
    /// </remarks>
    public static float[] Render(IMidiSynthesizer synthesizer, MidiSequence sequence, TimeSpan tail = default)
    {
        if (synthesizer == null)
        {
            throw new ArgumentNullException(nameof(synthesizer));
        }

        if (sequence == null)
        {
            throw new ArgumentNullException(nameof(sequence));
        }

        if (synthesizer.SampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(synthesizer), synthesizer.SampleRate, "The sample rate must be positive.");
        }

        if (tail < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(tail), tail, "The tail length cannot be negative.");
        }

        return RenderCore(synthesizer, sequence, synthesizer.SampleRate, tail);
    }

    private static float[] RenderCore(IMidiSynthesizer synthesizer, MidiSequence sequence, int sampleRate, TimeSpan tail)
    {
        // Every render this class does is OFFLINE, and it runs as fast as the machine allows. A Decent
        // Sampler synthesizer streaming its samples in the real-time mode would outrun its background
        // reader and write starved blocks as silence, so it is switched over for the render and put
        // back afterwards. The instrument-taking overloads build their synthesizer offline already;
        // this covers one a consumer built and handed in.
        var decentSampler = synthesizer as DecentSampler.DecentSamplerSynthesizer;
        var previousStreamingMode = decentSampler == null
            ? DecentSampler.Streaming.DecentSamplerStreamingMode.Offline
            : decentSampler.StreamingMode;

        if (decentSampler != null)
        {
            decentSampler.StreamingMode = DecentSampler.Streaming.DecentSamplerStreamingMode.Offline;
        }

        try
        {
            return RenderFrames(synthesizer, sequence, sampleRate, tail);
        }
        finally
        {
            if (decentSampler != null)
            {
                decentSampler.StreamingMode = previousStreamingMode;
            }
        }
    }

    private static float[] RenderFrames(
        IMidiSynthesizer synthesizer, MidiSequence sequence, int sampleRate, TimeSpan tail)
    {
        var sequencer = new MidiSequencer(synthesizer);
        sequencer.Play(sequence, loop: false);

        var totalSeconds = (sequence.Length + tail).TotalSeconds;
        var frames = (int)Math.Ceiling(totalSeconds * sampleRate);
        if (frames <= 0)
        {
            return [];
        }

        var left = new float[frames];
        var right = new float[frames];
        sequencer.Render(left, right);

        var interleaved = new float[frames * 2];
        for (var i = 0; i < frames; i++)
        {
            interleaved[i * 2] = left[i];
            interleaved[i * 2 + 1] = right[i];
        }

        return interleaved;
    }

    /// <summary>
    /// Renders a whole sequence straight to a 32-bit float stereo WAV file.
    /// </summary>
    /// <param name="soundFont">The SoundFont to render with.</param>
    /// <param name="sequence">The sequence to render.</param>
    /// <param name="outputPath">Path of the <c>.wav</c> file to write. Overwritten if it exists.</param>
    /// <param name="sampleRate">Output sample rate in Hz.</param>
    /// <param name="tail">Extra time rendered after the sequence ends, so tails are not cut off.</param>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive, or <paramref name="tail"/> is negative.</exception>
    public static void RenderToWavFile(
        SoundFont soundFont,
        MidiSequence sequence,
        string outputPath,
        int sampleRate = DefaultSampleRate,
        TimeSpan tail = default)
    {
        if (outputPath == null)
        {
            throw new ArgumentNullException(nameof(outputPath));
        }

        using (var stream = File.Create(outputPath))
        {
            RenderToWavStream(soundFont, sequence, stream, sampleRate, tail, leaveOpen: true);
        }
    }

    /// <summary>
    /// Renders a whole sequence through an SFZ instrument straight to a 32-bit float stereo WAV file.
    /// </summary>
    /// <param name="instrument">The SFZ instrument to render with.</param>
    /// <param name="sequence">The sequence to render.</param>
    /// <param name="outputPath">Path of the <c>.wav</c> file to write. Overwritten if it exists.</param>
    /// <param name="sampleRate">Output sample rate in Hz.</param>
    /// <param name="tail">Extra time rendered after the sequence ends, so tails are not cut off.</param>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive, or <paramref name="tail"/> is negative.</exception>
    public static void RenderToWavFile(
        SfzInstrument instrument,
        MidiSequence sequence,
        string outputPath,
        int sampleRate = DefaultSampleRate,
        TimeSpan tail = default)
    {
        if (outputPath == null)
        {
            throw new ArgumentNullException(nameof(outputPath));
        }

        using (var stream = File.Create(outputPath))
        {
            RenderToWavStream(instrument, sequence, stream, sampleRate, tail, leaveOpen: true);
        }
    }

    /// <summary>
    /// Renders a whole sequence through a Decent Sampler instrument straight to a 32-bit float stereo
    /// WAV file.
    /// </summary>
    /// <param name="instrument">The Decent Sampler instrument to render with.</param>
    /// <param name="sequence">The sequence to render.</param>
    /// <param name="outputPath">Path of the <c>.wav</c> file to write. Overwritten if it exists.</param>
    /// <param name="sampleRate">Output sample rate in Hz.</param>
    /// <param name="tail">Extra time rendered after the sequence ends, so tails are not cut off.</param>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive, or <paramref name="tail"/> is negative.</exception>
    public static void RenderToWavFile(
        DecentSamplerInstrument instrument,
        MidiSequence sequence,
        string outputPath,
        int sampleRate = DefaultSampleRate,
        TimeSpan tail = default)
    {
        if (outputPath == null)
        {
            throw new ArgumentNullException(nameof(outputPath));
        }

        using (var stream = File.Create(outputPath))
        {
            RenderToWavStream(instrument, sequence, stream, sampleRate, tail, leaveOpen: true);
        }
    }

    /// <summary>
    /// Renders a whole sequence to a 32-bit float stereo WAV stream.
    /// </summary>
    /// <param name="soundFont">The SoundFont to render with.</param>
    /// <param name="sequence">The sequence to render.</param>
    /// <param name="output">The stream to write the WAV file to. Must be writable and seekable.</param>
    /// <param name="sampleRate">Output sample rate in Hz.</param>
    /// <param name="tail">Extra time rendered after the sequence ends, so tails are not cut off.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the stream is left open once writing finishes.</param>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive, or <paramref name="tail"/> is negative.</exception>
    public static void RenderToWavStream(
        SoundFont soundFont,
        MidiSequence sequence,
        Stream output,
        int sampleRate = DefaultSampleRate,
        TimeSpan tail = default,
        bool leaveOpen = false)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        WriteWav(Render(soundFont, sequence, sampleRate, tail), output, sampleRate, leaveOpen);
    }

    /// <summary>
    /// Renders a whole sequence through an SFZ instrument to a 32-bit float stereo WAV stream.
    /// </summary>
    /// <param name="instrument">The SFZ instrument to render with.</param>
    /// <param name="sequence">The sequence to render.</param>
    /// <param name="output">The stream to write the WAV file to. Must be writable and seekable.</param>
    /// <param name="sampleRate">Output sample rate in Hz.</param>
    /// <param name="tail">Extra time rendered after the sequence ends, so tails are not cut off.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the stream is left open once writing finishes.</param>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive, or <paramref name="tail"/> is negative.</exception>
    public static void RenderToWavStream(
        SfzInstrument instrument,
        MidiSequence sequence,
        Stream output,
        int sampleRate = DefaultSampleRate,
        TimeSpan tail = default,
        bool leaveOpen = false)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        WriteWav(Render(instrument, sequence, sampleRate, tail), output, sampleRate, leaveOpen);
    }

    /// <summary>
    /// Renders a whole sequence through a Decent Sampler instrument to a 32-bit float stereo WAV stream.
    /// </summary>
    /// <param name="instrument">The Decent Sampler instrument to render with.</param>
    /// <param name="sequence">The sequence to render.</param>
    /// <param name="output">The stream to write the WAV file to. Must be writable and seekable.</param>
    /// <param name="sampleRate">Output sample rate in Hz.</param>
    /// <param name="tail">Extra time rendered after the sequence ends, so tails are not cut off.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the stream is left open once writing finishes.</param>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive, or <paramref name="tail"/> is negative.</exception>
    public static void RenderToWavStream(
        DecentSamplerInstrument instrument,
        MidiSequence sequence,
        Stream output,
        int sampleRate = DefaultSampleRate,
        TimeSpan tail = default,
        bool leaveOpen = false)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        WriteWav(Render(instrument, sequence, sampleRate, tail), output, sampleRate, leaveOpen);
    }

    /// <summary>
    /// Renders a whole sequence through a synthesizer of your own straight to a 32-bit float stereo
    /// WAV file.
    /// </summary>
    /// <param name="synthesizer">The synthesizer to render with, at its own sample rate.</param>
    /// <param name="sequence">The sequence to render.</param>
    /// <param name="outputPath">Path of the <c>.wav</c> file to write. Overwritten if it exists.</param>
    /// <param name="tail">Extra time rendered after the sequence ends, so tails are not cut off.</param>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The synthesizer's sample rate is not positive, or <paramref name="tail"/> is negative.</exception>
    public static void RenderToWavFile(
        IMidiSynthesizer synthesizer,
        MidiSequence sequence,
        string outputPath,
        TimeSpan tail = default)
    {
        if (outputPath == null)
        {
            throw new ArgumentNullException(nameof(outputPath));
        }

        using (var stream = File.Create(outputPath))
        {
            RenderToWavStream(synthesizer, sequence, stream, tail, leaveOpen: true);
        }
    }

    /// <summary>
    /// Renders a whole sequence through a synthesizer of your own to a 32-bit float stereo WAV stream.
    /// </summary>
    /// <param name="synthesizer">The synthesizer to render with, at its own sample rate.</param>
    /// <param name="sequence">The sequence to render.</param>
    /// <param name="output">The stream to write the WAV file to. Must be writable and seekable.</param>
    /// <param name="tail">Extra time rendered after the sequence ends, so tails are not cut off.</param>
    /// <param name="leaveOpen">When <see langword="true"/>, the stream is left open once writing finishes.</param>
    /// <exception cref="ArgumentNullException">Any reference argument is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The synthesizer's sample rate is not positive, or <paramref name="tail"/> is negative.</exception>
    public static void RenderToWavStream(
        IMidiSynthesizer synthesizer,
        MidiSequence sequence,
        Stream output,
        TimeSpan tail = default,
        bool leaveOpen = false)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        if (synthesizer == null)
        {
            throw new ArgumentNullException(nameof(synthesizer));
        }

        WriteWav(Render(synthesizer, sequence, tail), output, synthesizer.SampleRate, leaveOpen);
    }

    private static void WriteWav(float[] samples, Stream output, int sampleRate, bool leaveOpen)
    {
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);

        var writer = new WaveFileWriter(new IgnoreDisposeStream(output, leaveOpen), format);
        try
        {
            writer.WriteSamples(samples, 0, samples.Length);
        }
        finally
        {
            writer.Dispose();
        }
    }

    // WaveFileWriter always disposes the stream it was handed; this keeps leaveOpen honest without
    // changing that behaviour for every other caller.
    private sealed class IgnoreDisposeStream : Stream
    {
        private readonly Stream _inner;
        private readonly bool _leaveOpen;

        internal IgnoreDisposeStream(Stream inner, bool leaveOpen)
        {
            _inner = inner;
            _leaveOpen = leaveOpen;
        }

        public override bool CanRead => _inner.CanRead;
        public override bool CanSeek => _inner.CanSeek;
        public override bool CanWrite => _inner.CanWrite;
        public override long Length => _inner.Length;

        public override long Position
        {
            get => _inner.Position;
            set => _inner.Position = value;
        }

        public override void Flush() => _inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => _inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => _inner.Seek(offset, origin);
        public override void SetLength(long value) => _inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => _inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_leaveOpen)
            {
                _inner.Dispose();
            }
        }
    }
}
