using System;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Engine.Metadata.Models;

namespace CodeBrix.Audio.Playback.Internal;

/// <summary>
/// Feeds a <see cref="MultiTrackMix"/> into the audio engine as a stereo float source, and owns the
/// thread-safety contract the mix does not provide.
/// </summary>
/// <remarks>
/// <para>
/// Rendering happens on the engine's real-time audio thread while transport calls arrive from the
/// application's; the mix (and the synthesizers inside it) must never see the two at once, so every
/// entry point here takes the same lock. The same arrangement as
/// <c>MidiSynthDataProvider</c>, for the same reason.
/// </para>
/// <para>
/// A non-looping song ends the STREAM: once the last frame and its tail have gone out,
/// <see cref="ReadBytes"/> returns 0, which is the engine's one end-of-stream signal - it is what
/// moves the engine player to Stopped and ultimately raises
/// <see cref="MultiTrackPlayer.PlaybackEnded"/>.
/// </para>
/// </remarks>
internal sealed class MultiTrackDataProvider : ISoundDataProvider
{
    private readonly object gate = new object();
    private readonly MultiTrackMix mix;
    private readonly long tailFrames;

    private bool looping;
    private bool endRaised;
    private bool disposed;

    /// <summary>Wraps a mix for the engine.</summary>
    /// <param name="mix">The mix to render. This provider takes ownership of it.</param>
    /// <param name="tailFrames">How many frames to keep rendering past the song's end.</param>
    /// <param name="looping">Whether the song repeats from the start when it reaches the end.</param>
    internal MultiTrackDataProvider(MultiTrackMix mix, long tailFrames, bool looping)
    {
        this.mix = mix;
        this.tailFrames = tailFrames > 0 ? tailFrames : 0;
        this.looping = looping;
    }

    /// <inheritdoc/>
    public event EventHandler<EventArgs> EndOfStreamReached;

    /// <inheritdoc/>
    public event EventHandler<PositionChangedEventArgs> PositionChanged;

    /// <inheritdoc/>
    public int Position
    {
        get { lock (gate) { return (int)Math.Min(int.MaxValue, mix.Position * 2); } }
    }

    /// <inheritdoc/>
    public int Length
    {
        get { lock (gate) { return (int)Math.Min(int.MaxValue, TotalFrames * 2); } }
    }

    /// <inheritdoc/>
    public bool CanSeek => true;

    /// <inheritdoc/>
    public SampleFormat SampleFormat => SampleFormat.F32;

    /// <inheritdoc/>
    public int SampleRate => mix.SampleRate;

    /// <inheritdoc/>
    public bool IsDisposed
    {
        get { lock (gate) { return disposed; } }
    }

    /// <inheritdoc/>
    public SoundFormatInfo FormatInfo => null;

    /// <summary>The mix this provider renders. Only touch it under <see cref="Gate"/>.</summary>
    internal MultiTrackMix Mix => mix;

    /// <summary>The lock that serializes the mix against the audio thread.</summary>
    internal object Gate => gate;

    /// <summary>The song's own length in frames, without the tail.</summary>
    internal long LengthFrames => mix.LengthFrames;

    /// <summary>The song's length plus the tail, in frames - how long the stream actually runs.</summary>
    internal long TotalFrames => mix.LengthFrames + tailFrames;

    /// <summary>The current position in frames.</summary>
    internal long PositionFrames
    {
        get { lock (gate) { return mix.Position; } }
    }

    /// <summary>Whether the song repeats when it reaches the end.</summary>
    internal bool IsLooping
    {
        get { lock (gate) { return looping; } }
        set
        {
            lock (gate)
            {
                looping = value;
                if (value)
                {
                    endRaised = false;
                }
            }
        }
    }

    /// <summary>Publishes musical time with the transport reported as stopped or running.</summary>
    /// <param name="isPlaying">What to report.</param>
    internal void SetTransportRunning(bool isPlaying)
    {
        lock (gate)
        {
            mix.PublishTempo(isPlaying);
        }
    }

    /// <inheritdoc/>
    public int ReadBytes(Span<float> buffer)
    {
        lock (gate)
        {
            if (disposed)
            {
                return 0;
            }

            var total = TotalFrames;

            if (!looping && total > 0 && mix.Position >= total)
            {
                if (!endRaised)
                {
                    endRaised = true;
                    EndOfStreamReached?.Invoke(this, EventArgs.Empty);
                }

                return 0;
            }

            var frames = buffer.Length / 2;
            var written = 0;

            while (written < frames)
            {
                var remaining = total - mix.Position;
                if (remaining <= 0)
                {
                    if (!looping)
                    {
                        break;
                    }

                    mix.Seek(0);
                    remaining = total;
                    if (remaining <= 0)
                    {
                        break;
                    }
                }

                var take = (int)Math.Min(frames - written, remaining);
                mix.Render(buffer.Slice(written * 2, take * 2));
                written += take;
            }

            if (written == 0)
            {
                if (!endRaised)
                {
                    endRaised = true;
                    EndOfStreamReached?.Invoke(this, EventArgs.Empty);
                }

                return 0;
            }

            if (written < frames)
            {
                buffer.Slice(written * 2).Clear();
            }

            PositionChanged?.Invoke(this, new PositionChangedEventArgs(Position));

            // The engine treats a SHORT read as "that is all there was" and stops asking, so the
            // buffer is always returned full: the end of the song is signalled by the next call
            // returning zero, which is the one signal the engine acts on.
            return frames * 2;
        }
    }

    /// <inheritdoc/>
    public void Seek(int offset)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            mix.Seek(offset <= 0 ? 0 : offset / 2);
            endRaised = false;
        }
    }

    /// <summary>Moves the song to a frame position.</summary>
    /// <param name="frame">The frame to move to.</param>
    internal void SeekFrames(long frame)
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            mix.Seek(frame);
            endRaised = false;
        }
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            mix.Dispose();
        }
    }
}
