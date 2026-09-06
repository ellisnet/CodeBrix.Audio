using System;
using CodeBrix.Audio.Codecs;

namespace CodeBrix.Audio.Playback.Internal;

/// <summary>
/// Renders a track's recording: a chunked managed decode, converted to stereo at the mix's sample
/// rate, with silence past the end of the file.
/// </summary>
internal sealed class AudioSourceRenderer : SourceRenderer
{
    private readonly WaveStreamSoundDecoder decoder;

    private readonly long lengthFrames;

    private long position;

    /// <summary>Opens a decoder over the given recording.</summary>
    /// <param name="definition">Where the recording lives.</param>
    /// <param name="sampleRate">The rate to render at.</param>
    internal AudioSourceRenderer(AudioSourceDefinition definition, int sampleRate)
    {
        decoder = definition.OpenDecoder(sampleRate);

        // ManagedSoundDecoder.Length counts SAMPLES at the target format; the mixer thinks in
        // frames. Fall back to the probed duration when the decoder cannot say (a stream whose
        // length the reader could not work out).
        lengthFrames = decoder.Length > 0
            ? decoder.Length / 2
            : (long)(definition.Duration.TotalSeconds * sampleRate);
    }

    /// <inheritdoc/>
    internal override long LengthFrames => lengthFrames;

    /// <inheritdoc/>
    internal override void Seek(long frame)
    {
        if (frame < 0)
        {
            frame = 0;
        }

        // The decoder takes an offset in samples, not frames.
        decoder.Seek((int)Math.Min(int.MaxValue, frame * 2));
        position = frame;
    }

    /// <inheritdoc/>
    internal override void Render(Span<float> buffer)
    {
        var written = 0;
        while (written < buffer.Length)
        {
            var read = decoder.Decode(buffer.Slice(written));
            if (read <= 0)
            {
                break;
            }

            written += read;
        }

        if (written < buffer.Length)
        {
            buffer.Slice(written).Clear();
        }

        position += buffer.Length / 2;
    }

    /// <inheritdoc/>
    public override void Dispose() => decoder.Dispose();
}
