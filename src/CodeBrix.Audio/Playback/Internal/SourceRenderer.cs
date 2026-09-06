using System;

namespace CodeBrix.Audio.Playback.Internal;

/// <summary>
/// One of a track's two sources, rendered as interleaved stereo at the mix's sample rate.
/// </summary>
/// <remarks>
/// The two implementations - a decoded file and a synthesized MIDI performance - look identical
/// from here, which is what lets the mixer treat them as interchangeable and crossfade between
/// them.
/// </remarks>
internal abstract class SourceRenderer : IDisposable
{
    /// <summary>How long the source is, in frames at the mix's sample rate.</summary>
    internal abstract long LengthFrames { get; }

    /// <summary>Moves to a position in the source's own timeline.</summary>
    /// <param name="frame">The frame to move to; never negative.</param>
    internal abstract void Seek(long frame);

    /// <summary>
    /// Renders the next frames, advancing the source's position by <c>buffer.Length / 2</c>.
    /// </summary>
    /// <param name="buffer">Interleaved stereo destination; filled completely, with silence past the end of the source.</param>
    internal abstract void Render(Span<float> buffer);

    /// <summary>Releases what the renderer holds open.</summary>
    public abstract void Dispose();
}
