using System;
using System.IO;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Synth;

/// <summary>
/// Renders MIDI music through a SoundFont without an audio device - to a buffer, or straight to a WAV
/// file. Runs as fast as the machine allows rather than in real time.
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

        var synthesizer = new SoundFontSynthesizer(soundFont, sampleRate);
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

        var samples = Render(soundFont, sequence, sampleRate, tail);
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
