using System;

namespace CodeBrix.Audio.Playback.Internal;

/// <summary>
/// One source of a track, shifted in time relative to the track itself. This is what makes
/// <see cref="PlayerTrack.MidiSourceOffset"/> work: the recording stays where it is and the MIDI
/// rendition moves against it.
/// </summary>
/// <remarks>
/// A positive delay means the wrapped source starts later: the first frames the mixer asks for
/// come back as silence, and the source's own timeline runs behind the track's by that much. A
/// negative delay runs it ahead, and whatever falls before the start of the track is not heard.
/// The delay is re-read from the track whenever the transport seeks, in step with the way a
/// track's own offset is re-read.
/// </remarks>
internal sealed class DelayedSourceRenderer : SourceRenderer
{
    private readonly SourceRenderer inner;

    private long delayFrames;
    private long position;

    /// <summary>Wraps a renderer so that it can be shifted in time.</summary>
    /// <param name="inner">The renderer to shift. This renderer owns it and disposes it.</param>
    internal DelayedSourceRenderer(SourceRenderer inner) => this.inner = inner;

    /// <inheritdoc/>
    internal override long LengthFrames
    {
        get
        {
            var end = inner.LengthFrames + delayFrames;
            return end > 0 ? end : 0;
        }
    }

    /// <summary>How far the wrapped source is shifted, in frames. Positive delays it.</summary>
    internal long DelayFrames
    {
        get => delayFrames;
        set => delayFrames = value;
    }

    /// <inheritdoc/>
    internal override void Seek(long frame)
    {
        position = frame;
        var innerFrame = frame - delayFrames;
        inner.Seek(innerFrame > 0 ? innerFrame : 0);
    }

    /// <inheritdoc/>
    internal override void Render(Span<float> buffer)
    {
        var frames = buffer.Length / 2;
        if (frames == 0)
        {
            return;
        }

        var lead = 0L;
        var innerPosition = position - delayFrames;
        if (innerPosition < 0)
        {
            lead = Math.Min(frames, -innerPosition);
            buffer.Slice(0, (int)lead * 2).Clear();
        }

        if (lead < frames)
        {
            inner.Render(buffer.Slice((int)lead * 2));
        }

        position += frames;
    }

    /// <inheritdoc/>
    public override void Dispose() => inner.Dispose();
}
