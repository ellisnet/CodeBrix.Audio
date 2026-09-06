using System;
using System.Threading;

namespace CodeBrix.Audio.Synth.DecentSampler.Streaming;

// One voice's window onto a streamed sample: a ring buffer the reader fills and the audio thread
// plays, plus the small run-in buffer a loop crossfade needs.
//
// THE ONE IDEA THAT MAKES THIS WORK. The ring is addressed in STREAM SPACE - frame 0 is the first
// frame the voice will play, and the count never goes backwards - while the file position wanders,
// wrapping at the loop end. The reader does the wrapping; the audio thread reads a straight line. That
// is what makes a streamed voice produce the same numbers as an in-memory one: the in-memory
// oscillator's wrap (position -= loopLength, index2 folded back to loopStart) hands out exactly the
// sequence of file frames the reader writes into the ring.
//
// The exception is a loop CROSSFADE, whose fading-in audio comes from the frames immediately BEFORE
// loopStart - a fixed little region, far behind the play head, every time round the loop. That region
// is fetched once at prime time into Fade and read from there.
//
// Threading: Written is written by the reader and read by the audio thread; Consumed the other way
// round. Both are Interlocked/Volatile longs; nothing else crosses. The audio thread never allocates,
// never locks and never touches a file.
internal sealed class StreamingVoiceBuffer
{
    /// <summary>Nothing is using this buffer.</summary>
    public const int StateFree = 0;

    /// <summary>Configured by a note-on; the reader has not filled it yet.</summary>
    public const int StatePriming = 1;

    /// <summary>Playing; the reader keeps it topped up.</summary>
    public const int StateRunning = 2;

    /// <summary>
    /// The note has ended and the buffer is on its way back to the pool. Only the thread that services
    /// it may return it to <see cref="StateFree"/>, which is what keeps a reader that is mid-fill from
    /// writing into a buffer another note has already claimed.
    /// </summary>
    public const int StateReleasing = 3;

    /// <summary>The largest crossfade run-in this holds, in frames: 1 M, about 8 MB in stereo.</summary>
    public const int MaximumFadeFrames = 1 << 20;

    private readonly float[][] _ring;
    private readonly int _capacity;
    private readonly int _mask;
    private readonly int _channelCount;

    private float[][] _fade;
    private int _fadeCapacity;
    private bool _fadeFilled;

    private long _written;
    private long _consumed;
    private int _state;
    private int _readerDone;
    private int _underrun;
    private int _primed;
    private string _underrunFileName;

    public StreamingVoiceBuffer(int channelCount, int capacityFrames)
    {
        _channelCount = Math.Clamp(channelCount, 1, 2);
        _capacity = RoundUpToPowerOfTwo(Math.Max(1024, capacityFrames));
        _mask = _capacity - 1;
        _ring = new float[_channelCount][];

        for (var c = 0; c < _channelCount; c++)
        {
            _ring[c] = new float[_capacity];
        }
    }

    /// <summary>The file this buffer streams from. Set by the note-on, read by the reader.</summary>
    public StreamingSampleSource Source { get; private set; }

    /// <summary>How many frames the ring holds.</summary>
    public int Capacity => _capacity;

    /// <summary>How many channels the ring carries.</summary>
    public int ChannelCount => _channelCount;

    /// <summary>The first file frame the voice plays.</summary>
    public long StartFrame { get; private set; }

    /// <summary>One past the last file frame a non-looping voice plays.</summary>
    public long EndFrameExclusive { get; private set; }

    /// <summary>Whether the voice loops.</summary>
    public bool Looping { get; private set; }

    /// <summary>The first file frame of the loop.</summary>
    public long LoopStartFrame { get; private set; }

    /// <summary>One past the last file frame of the loop.</summary>
    public long LoopEndFrameExclusive { get; private set; }

    /// <summary>How many frames one turn of the loop takes.</summary>
    public long LoopLength { get; private set; }

    /// <summary>How many stream frames play before the loop wraps for the first time.</summary>
    public long RunInFrames { get; private set; }

    /// <summary>How many stream frames a non-looping voice has in total.</summary>
    public long StreamEndFrames { get; private set; }

    /// <summary>How many frames of crossfade run-in the fade buffer holds.</summary>
    public int FadeFrameCount { get; private set; }

    /// <summary>The first file frame the fade buffer holds.</summary>
    public long FadeStartFrame { get; private set; }

    /// <summary>Free, priming or running.</summary>
    public int State => Volatile.Read(ref _state);

    /// <summary>Whether the reader has filled the ring at least once since the note started.</summary>
    public bool IsPrimed => Volatile.Read(ref _primed) != 0;

    /// <summary>Whether the reader has reached the end of the voice's audio.</summary>
    public bool ReaderFinished => Volatile.Read(ref _readerDone) != 0;

    /// <summary>Whether the audio thread ever found the ring short. Cleared when the buffer is rented.</summary>
    public bool HadUnderrun => Volatile.Read(ref _underrun) != 0;

    /// <summary>How many stream frames the reader has made available.</summary>
    public long Available => Volatile.Read(ref _written);

    /// <summary>
    /// Sets the buffer up for one note. Called on the note-on path before the reader is told about it,
    /// so nothing here needs to be thread-safe against the reader.
    /// </summary>
    /// <param name="source">The file to stream.</param>
    /// <param name="startFrame">The first file frame to play.</param>
    /// <param name="endFrameExclusive">One past the last file frame a non-looping voice plays.</param>
    /// <param name="looping">Whether the voice loops.</param>
    /// <param name="loopStartFrame">The first file frame of the loop.</param>
    /// <param name="loopEndFrameExclusive">One past the last file frame of the loop.</param>
    /// <param name="crossfadeFrames">How many frames of loop crossfade the zone asks for.</param>
    public void Configure(
        StreamingSampleSource source,
        long startFrame,
        long endFrameExclusive,
        bool looping,
        long loopStartFrame,
        long loopEndFrameExclusive,
        long crossfadeFrames)
    {
        Source = source;
        StartFrame = startFrame;
        EndFrameExclusive = endFrameExclusive;
        Looping = looping;
        LoopStartFrame = loopStartFrame;
        LoopEndFrameExclusive = loopEndFrameExclusive;
        LoopLength = Math.Max(1, loopEndFrameExclusive - loopStartFrame);
        RunInFrames = looping ? Math.Max(0, loopEndFrameExclusive - startFrame) : long.MaxValue;
        StreamEndFrames = looping ? long.MaxValue : Math.Max(0, endFrameExclusive - startFrame);

        // The fading-in audio is the run of frames immediately before loopStart, and the interpolation
        // reaches one frame past the last of them, so the window is crossfade + 1 frames wide.
        var fade = (int)Math.Min(MaximumFadeFrames, Math.Max(0, crossfadeFrames));
        FadeFrameCount = fade > 0 ? fade + 1 : 0;
        FadeStartFrame = loopStartFrame - fade;

        _written = 0;
        _consumed = 0;
        _state = StatePriming;
        _readerDone = 0;
        _underrun = 0;
        _primed = 0;
        _fadeFilled = false;
    }

    /// <summary>
    /// Claims a free buffer for one voice. A compare-and-swap, so the audio thread may call it.
    /// </summary>
    /// <returns><see langword="true"/> when the buffer is now this caller's.</returns>
    public bool TryClaim() =>
        Interlocked.CompareExchange(ref _state, StatePriming, StateFree) == StateFree;

    /// <summary>Publishes the buffer to the reader.</summary>
    public void Start() => Volatile.Write(ref _state, StateRunning);

    /// <summary>
    /// Ends the buffer's note. In the offline mode the caller is also the one who services the buffer,
    /// so it goes straight back to the pool; in the real-time mode it is handed to the reader, which
    /// frees it on its next pass once it is certainly not mid-fill.
    /// </summary>
    /// <param name="immediate">Whether the buffer may return to the pool without the reader's help.</param>
    public void Release(bool immediate)
    {
        if (immediate)
        {
            Source = null;
            Volatile.Write(ref _state, StateFree);
            return;
        }

        Volatile.Write(ref _state, StateReleasing);
    }

    /// <summary>
    /// Returns a released buffer to the pool. Called only by the thread that services buffers.
    /// </summary>
    public void MarkFree()
    {
        Source = null;
        Volatile.Write(ref _state, StateFree);
    }

    /// <summary>Tells the reader how far the audio thread has got, so it knows what it may overwrite.</summary>
    /// <param name="streamFrame">The lowest stream frame the voice still needs.</param>
    public void SetConsumed(long streamFrame) => Volatile.Write(ref _consumed, streamFrame);

    /// <summary>
    /// Records that the audio thread found the ring short. Only a flag and a reference copy, so it
    /// costs nothing on the audio thread; the reader turns it into a problem line later.
    /// </summary>
    public void ReportUnderrun()
    {
        _underrunFileName = Source?.FileName;
        Volatile.Write(ref _underrun, 1);
    }

    /// <summary>
    /// Takes the underrun flag, clearing it, and says which sample starved.
    /// </summary>
    /// <param name="fileName">The sample the voice was playing, or null.</param>
    /// <returns><see langword="true"/> when there was an underrun to report.</returns>
    public bool TakeUnderrun(out string fileName)
    {
        fileName = _underrunFileName;
        return Interlocked.Exchange(ref _underrun, 0) != 0;
    }

    /// <summary>One frame of the ring, in stream space. The caller has already checked availability.</summary>
    /// <param name="channel">The channel.</param>
    /// <param name="streamFrame">The stream frame.</param>
    /// <returns>The sample.</returns>
    public float Sample(int channel, long streamFrame) => _ring[channel][(int)(streamFrame & _mask)];

    /// <summary>
    /// One frame of the crossfade run-in, in FILE space. Out-of-range reads give silence, exactly as
    /// the in-memory oscillator's bounds-checked interpolation does.
    /// </summary>
    /// <param name="channel">The channel.</param>
    /// <param name="fileFrame">The file frame.</param>
    /// <returns>The sample, or zero.</returns>
    public float FadeSample(int channel, long fileFrame)
    {
        var index = fileFrame - FadeStartFrame;

        if (_fade == null || index < 0 || index >= FadeFrameCount)
        {
            return 0f;
        }

        return _fade[channel][index];
    }

    /// <summary>The file frame a stream frame plays.</summary>
    /// <param name="streamFrame">The stream frame.</param>
    /// <returns>The file frame.</returns>
    public long FileFrameAt(long streamFrame) =>
        streamFrame < RunInFrames
            ? StartFrame + streamFrame
            : LoopStartFrame + (streamFrame - RunInFrames) % LoopLength;

    /// <summary>
    /// Fills the ring from the source's preload head only - RAM, no lock, no file - so a note that
    /// starts inside the head has audio in its very first block without waiting for the reader.
    /// Called from the note-on path, before <see cref="Start"/> publishes the buffer to the reader.
    /// </summary>
    public void PrimeFromHead()
    {
        var source = Source;

        if (source == null)
        {
            return;
        }

        var written = _written;
        var limit = _capacity;

        while (written < limit)
        {
            var fileFrame = FileFrameAt(written);

            var untilBoundary = Looping
                ? LoopEndFrameExclusive - fileFrame
                : EndFrameExclusive - fileFrame;

            var ringOffset = (int)(written & _mask);
            var untilRingEnd = _capacity - ringOffset;

            var want = (int)Math.Min(Math.Min(limit - written, untilBoundary), untilRingEnd);

            if (want <= 0)
            {
                break;
            }

            var read = source.ReadFromHead(fileFrame, _ring, ringOffset, want);

            if (read <= 0)
            {
                break;
            }

            written += read;

            if (!Looping && written >= StreamEndFrames)
            {
                Volatile.Write(ref _readerDone, 1);
                break;
            }
        }

        Volatile.Write(ref _written, written);
    }

    /// <summary>
    /// Fills whatever the ring has room for. Called by the reader thread, or by the render thread in
    /// the offline streaming mode. Never called from a real-time audio thread.
    /// </summary>
    /// <returns><see langword="true"/> when there is still work to do later.</returns>
    public bool Service()
    {
        var source = Source;

        if (source == null || Volatile.Read(ref _state) == StateFree)
        {
            return false;
        }

        FillFade(source);

        var consumed = Volatile.Read(ref _consumed);
        var written = Volatile.Read(ref _written);

        // A drop-out let the audio thread run past what the reader had produced. Resynchronise rather
        // than replay: the missing frames were heard as silence and the timeline must not shift.
        if (consumed > written)
        {
            written = consumed;
        }

        var limit = consumed + _capacity;

        while (written < limit && Volatile.Read(ref _readerDone) == 0)
        {
            var fileFrame = FileFrameAt(written);

            var untilBoundary = Looping
                ? LoopEndFrameExclusive - fileFrame
                : EndFrameExclusive - fileFrame;

            var ringOffset = (int)(written & _mask);
            var untilRingEnd = _capacity - ringOffset;

            var want = (int)Math.Min(Math.Min(limit - written, untilBoundary), untilRingEnd);

            if (want <= 0)
            {
                break;
            }

            var read = source.ReadFrames(fileFrame, _ring, ringOffset, want);

            if (read <= 0)
            {
                Volatile.Write(ref _readerDone, 1);
                break;
            }

            written += read;

            if (!Looping && written >= StreamEndFrames)
            {
                Volatile.Write(ref _readerDone, 1);
            }
        }

        Volatile.Write(ref _written, written);
        Volatile.Write(ref _primed, 1);

        return Volatile.Read(ref _readerDone) == 0;
    }

    private void FillFade(StreamingSampleSource source)
    {
        if (FadeFrameCount <= 0 || _fadeFilled)
        {
            return;
        }

        if (_fade == null || _fadeCapacity < FadeFrameCount)
        {
            _fade = new float[_channelCount][];
            for (var c = 0; c < _channelCount; c++)
            {
                _fade[c] = new float[FadeFrameCount];
            }

            _fadeCapacity = FadeFrameCount;
        }

        for (var c = 0; c < _channelCount; c++)
        {
            Array.Clear(_fade[c], 0, FadeFrameCount);
        }

        if (FadeStartFrame >= 0)
        {
            source.ReadFrames(FadeStartFrame, _fade, 0, FadeFrameCount);
        }

        _fadeFilled = true;
    }

    private static int RoundUpToPowerOfTwo(int value)
    {
        var result = 1;
        while (result < value)
        {
            result <<= 1;
        }

        return result;
    }
}
