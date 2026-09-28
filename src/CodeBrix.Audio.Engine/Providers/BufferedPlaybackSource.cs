using System.Runtime.ExceptionServices;
using CodeBrix.Audio.Engine.Interfaces;

namespace CodeBrix.Audio.Engine.Providers;

// Single producer/consumer. The player's transport gate excludes reads during Reset/Dispose.
// After construction only the worker touches the source. Read never decodes, waits or signals.
internal sealed class BufferedPlaybackSource : IDisposable
{
    private readonly ISoundDataProvider _source;
    private readonly float[] _samples;
    private readonly int[] _positions;
    private readonly float[] _decodeBuffer;
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _worker;
    private long _read;
    private long _written;
    private volatile bool _ended;
    private volatile bool _stopping;
    private volatile bool _active;
    private ExceptionDispatchInfo? _fault;
    private ExceptionDispatchInfo? _cleanupError;
    private Request? _request;
    private int _loopStart;
    private int _loopEnd;
    private bool _looping;
    private int _position;

    private sealed class Request(int position, bool seek, bool looping, int loopStart, int loopEnd)
    {
        internal readonly int Position = position;
        internal readonly bool Seek = seek;
        internal readonly bool Looping = looping;
        internal readonly int LoopStart = loopStart;
        internal readonly int LoopEnd = loopEnd;
        internal readonly ManualResetEventSlim Completed = new(false);
        internal ExceptionDispatchInfo? Error;
    }

    internal BufferedPlaybackSource(ISoundDataProvider source, int channels)
    {
        _source = source;
        // One second of PCM, independent of file length. Position tags preserve transport
        // reporting even when a read crosses several short loops or file length is estimated.
        int capacity = checked(Math.Max(4096, source.SampleRate) * channels);
        _samples = new float[capacity];
        _positions = new int[capacity];
        _decodeBuffer = new float[Math.Min(capacity, checked(4096 * channels))];
        _position = source.Position;
        _worker = new Thread(Run) { IsBackground = true, Name = "CodeBrix audio decoder" };
        _worker.Start();
        try { Reset(_position, false, false, 0, -1); }
        catch { Dispose(); throw; }
    }

    internal int Position => _position;
    internal bool IsWorkerThread => Environment.CurrentManagedThreadId == _worker.ManagedThreadId;
    internal bool IsExhausted => _ended && Volatile.Read(ref _read) == Volatile.Read(ref _written);

    internal void SetActive(bool active)
    {
        ObjectDisposedException.ThrowIf(_stopping, this);
        _active = active;
        _wake.Set(); // Only control threads call this; never the render callback.
    }

    internal int Read(Span<float> destination)
    {
        long read = _read;
        int count = (int)Math.Min(destination.Length, Volatile.Read(ref _written) - read);
        if (count == 0)
        {
            Volatile.Read(ref _fault)?.Throw();
            return 0;
        }
        int offset = (int)(read % _samples.Length);
        int first = Math.Min(count, _samples.Length - offset);
        _samples.AsSpan(offset, first).CopyTo(destination);
        _samples.AsSpan(0, count - first).CopyTo(destination[first..]);
        _position = _positions[(int)((read + count - 1) % _samples.Length)];
        Volatile.Write(ref _read, read + count);
        return count;
    }

    // Control thread waits for seek and prefill. Rendering skips this player while its gate
    // is held, so even a decoder that takes a long time to seek cannot stall the device.
    internal void Reset(int position, bool seek, bool looping, int loopStart, int loopEnd)
    {
        ObjectDisposedException.ThrowIf(_stopping, this);
        if (IsWorkerThread)
            throw new InvalidOperationException("Do not control playback from decoder/provider callbacks.");
        var request = new Request(position, seek, looping, loopStart, loopEnd);
        Volatile.Write(ref _request, request);
        _wake.Set();
        request.Completed.Wait();
        request.Completed.Dispose();
        request.Error?.Throw();
        _position = position;
    }

    private void Run()
    {
        try { Pump(); }
        finally
        {
            try { _source.Dispose(); }
            catch (Exception error) { _cleanupError = ExceptionDispatchInfo.Capture(error); }
            _wake.Dispose();
        }
    }

    private void Pump()
    {
        _wake.WaitOne(); // The constructor publishes the initial prefill request first.
        Request? preparing = null;
        while (!_stopping)
        {
            try
            {
                var request = Interlocked.Exchange(ref _request, null);
                if (request != null)
                {
                    preparing = request;
                    if (request.Seek) _source.Seek(request.Position);
                    _loopStart = request.LoopStart;
                    _loopEnd = request.LoopEnd;
                    _looping = request.Looping && _source.CanSeek &&
                        (_loopEnd < 0 || _loopStart < _loopEnd);
                    Volatile.Write(ref _read, 0);
                    Volatile.Write(ref _written, 0);
                    _ended = false;
                    Volatile.Write(ref _fault, null);
                }
                long written = _written;
                int free = _samples.Length - (int)(written - Volatile.Read(ref _read));
                if (_ended || _fault != null || free == 0 || (!_active && preparing == null))
                {
                    if (preparing != null)
                    {
                        preparing.Completed.Set();
                        preparing = null;
                    }
                    _wake.WaitOne(_ended || _fault != null || !_active ? Timeout.Infinite : 5);
                    continue;
                }
                if (_looping && _loopEnd >= 0 && _source.Position >= _loopEnd)
                    _source.Seek(_loopStart);
                int wanted = Math.Min(free, _decodeBuffer.Length);
                if (_looping && _loopEnd >= 0)
                    wanted = Math.Min(wanted, _loopEnd - _source.Position);
                int start = _source.Position;
                int count = _source.ReadBytes(_decodeBuffer.AsSpan(0, wanted));
                if (count == 0 && _looping)
                {
                    // Try once: an empty file or invalid loop start must not spin.
                    _source.Seek(_loopStart);
                    start = _source.Position;
                    count = _source.ReadBytes(_decodeBuffer.AsSpan(0, wanted));
                }
                if (count == 0)
                {
                    _ended = true;
                    continue;
                }
                int offset = (int)(written % _samples.Length);
                int first = Math.Min(count, _samples.Length - offset);
                _decodeBuffer.AsSpan(0, first).CopyTo(_samples.AsSpan(offset));
                _decodeBuffer.AsSpan(first, count - first).CopyTo(_samples);
                for (int i = 0; i < count; i++)
                    _positions[(int)((written + i) % _samples.Length)] = start + i + 1;
                Volatile.Write(ref _written, written + count);
            }
            catch (Exception error)
            {
                var captured = ExceptionDispatchInfo.Capture(error);
                Volatile.Write(ref _fault, captured);
                _ended = true;
                if (preparing != null)
                {
                    preparing.Error = captured;
                    preparing.Completed.Set();
                    preparing = null;
                }
            }
        }
    }

    public void Dispose()
    {
        if (_stopping) return;
        RequestStop();
        _worker.Join();
        _cleanupError?.Throw();
    }

    // Finalization requests cleanup without waiting for a potentially blocked decoder.
    internal void RequestStop()
    {
        if (_stopping) return;
        _stopping = true;
        _wake.Set();
    }
}
