using System.Buffers;
using System.Runtime.InteropServices;
using CodeBrix.Audio.Engine.Backends.MiniAudio.Enums;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Engine.Utils;

namespace CodeBrix.Audio.Engine.Backends.MiniAudio;  //was previously: SoundFlow.Backends.MiniAudio

/// <summary>
///     An object to assist with converting audio formats into raw PCM frames.
/// </summary>
internal sealed unsafe class MiniAudioDecoder : ISoundDecoder
{
    /// <summary>
    ///     Ogg streams up to this size are handed to the decoder as one block of memory; anything
    ///     larger streams through the read callbacks instead. 256 MB of Vorbis is on the order of
    ///     seven hours of audio, so in practice this only rules out pathological inputs.
    /// </summary>
    private const long MaxInMemoryOggBytes = 256L * 1024 * 1024;

    private readonly nint _decoder;
    private readonly Stream _stream;

    // Keep references to delegates to prevent GC collection while native code uses them
    private readonly Native.BufferProcessingCallback _readCallback;
    private readonly Native.SeekCallback _seekCallbackCallback;

    // Non-null when the decoder was initialized from a block of memory we own (see
    // TryInitializeFromMemory). miniaudio does not copy the data, so it must outlive the decoder.
    private byte* _ownedData;

    private bool _endOfStreamReached;
    private byte[]? _rentedReadBuffer;
    private readonly object _syncLock = new();

    /// <summary>
    ///     Constructs a new decoder from the given stream in one of the supported formats.
    /// </summary>
    /// <param name="stream">A stream to a file or streaming audio source in one of the supported formats.</param>
    /// <param name="sampleFormat">The format of the audio samples.</param>
    /// <param name="channels">The number of audio channels.</param>
    /// <param name="sampleRate">The sample rate of the audio.</param>
    public MiniAudioDecoder(Stream stream, SampleFormat sampleFormat, int channels, int sampleRate)
    {
        _stream = stream ?? throw new ArgumentNullException(nameof(stream));
        SampleFormat = sampleFormat;
        Channels = channels;
        SampleRate = sampleRate;

        var configPtr = Native.AllocateDecoderConfig(SampleFormat, (uint)Channels, (uint)SampleRate);

        _decoder = Native.AllocateDecoder();

        // Store delegates in fields to prevent GC collection
        _readCallback = ReadCallback;
        _seekCallbackCallback = SeekCallback;

        // Ogg gets first refusal on the in-memory path: given the whole file, miniaudio drives
        // stb_vorbis in pull mode, which knows the stream length and seeks directly. Through the
        // read callbacks it uses push mode instead, where the length reads back as zero and a
        // backward seek re-decodes from the beginning. If the in-memory attempt does not apply or
        // does not work, the callback path below still runs, so nothing is lost by trying.
        var result = TryInitializeFromMemory(configPtr);

        if (result != MiniAudioResult.Success)
        {
            ReleaseOwnedData();
            if (_stream.CanSeek) _stream.Position = 0;
            result = Native.DecoderInit(_readCallback, _seekCallbackCallback, nint.Zero, configPtr, _decoder);
        }

        Native.Free(configPtr);

        if (result != MiniAudioResult.Success)
            throw new MiniaudioException("MiniAudio", result, "Unable to initialize decoder.");

        result = Native.DecoderGetLengthInPcmFrames(_decoder, out var length);
        if (result != MiniAudioResult.Success) 
            throw new MiniaudioException("MiniAudio", result, "Unable to get decoder length.");
        
        Length = (int)length * Channels;
        _endOfStreamReached = false;
    }
    
    /// <summary>
    ///     Attempts to open the stream as one block of memory rather than through read callbacks.
    /// </summary>
    /// <returns>
    ///     <see cref="MiniAudioResult.Success" /> when the decoder was initialized this way;
    ///     any other value means the caller should fall back to the callback path.
    /// </returns>
    /// <remarks>
    ///     Only Ogg streams take this route. Every other format miniaudio decodes reports its
    ///     length and seeks correctly from callbacks, and streaming them keeps memory flat for
    ///     long files - which is the entire point of <c>ChunkedDataProvider</c>. Ogg is the
    ///     exception that has to be bought out of push mode.
    /// </remarks>
    private MiniAudioResult TryInitializeFromMemory(nint configPtr)
    {
        if (!_stream.CanSeek) return MiniAudioResult.InvalidOperation;

        long length;
        try
        {
            length = _stream.Length;
        }
        catch (NotSupportedException)
        {
            // Some streams claim CanSeek but refuse Length.
            return MiniAudioResult.InvalidOperation;
        }

        if (length is <= 0 or > MaxInMemoryOggBytes) return MiniAudioResult.InvalidOperation;

        _stream.Position = 0;
        Span<byte> header = stackalloc byte[4];
        if (_stream.ReadAtLeast(header, 4, throwOnEndOfStream: false) < 4) return MiniAudioResult.InvalidOperation;
        _stream.Position = 0;

        // "OggS" - the Ogg page capture pattern.
        if (header[0] != (byte)'O' || header[1] != (byte)'g' || header[2] != (byte)'g' || header[3] != (byte)'S')
            return MiniAudioResult.InvalidOperation;

        if (!Native.HasVorbis)
        {
            // An older native library without stb_vorbis cannot decode this at all. Say so once,
            // clearly, rather than letting it surface as a bare initialization failure - a
            // managed Vorbis decoder registered with the engine takes over from here.
            Log.Info("The loaded codebrix_miniaudio has no Ogg Vorbis decoder; " +
                     "Ogg playback will use the managed decoder instead.");
            return MiniAudioResult.InvalidOperation;
        }

        var buffer = (byte*)NativeMemory.Alloc((nuint)length);
        try
        {
            _stream.ReadExactly(new Span<byte>(buffer, (int)length));
        }
        catch (Exception)
        {
            NativeMemory.Free(buffer);
            return MiniAudioResult.IoError;
        }

        var result = Native.DecoderInitMemory((nint)buffer, (nuint)length, configPtr, _decoder);
        if (result != MiniAudioResult.Success)
        {
            NativeMemory.Free(buffer);
            return result;
        }

        // The decoder now holds a pointer into this block, so we own it until Dispose.
        _ownedData = buffer;
        return MiniAudioResult.Success;
    }

    private void ReleaseOwnedData()
    {
        if (_ownedData == null) return;
        NativeMemory.Free(_ownedData);
        _ownedData = null;
    }

    /// <inheritdoc />
    public int Channels { get; }
    
    /// <inheritdoc />
    public int SampleRate { get; }

    /// <inheritdoc />
    public bool IsDisposed { get; private set; }

    /// <inheritdoc />
    public int Length { get; private set; }

    /// <inheritdoc />
    public SampleFormat SampleFormat { get; }

    public event EventHandler<EventArgs>? EndOfStreamReached;

    /// <summary>
    ///     Decodes the next several samples.
    /// </summary>
    public int Decode(Span<float> samples)
    {
        lock (_syncLock)
        {
            if (IsDisposed || _endOfStreamReached)
                return 0;

            var framesToRead = (uint)(samples.Length / Channels);
            if (framesToRead == 0)
                return 0;
            
            var buffer = GetBufferIfNeeded(samples.Length);
            var span = buffer ?? MemoryMarshal.AsBytes(samples);

            fixed (byte* nativeBuffer = span)
            {
                var result = Native.DecoderReadPcmFrames(_decoder, (nint)nativeBuffer, framesToRead, out var framesRead);

                // If we reached the end of the stream, set the flag and return 0
                if (result == MiniAudioResult.AtEnd)
                {
                    _endOfStreamReached = true;
                }
                // Check for actual errors, ignoring the clean AtEnd result.
                else if (result != MiniAudioResult.Success)
                {
                    _endOfStreamReached = true;
                    return 0;
                }

                if (framesRead == 0 && _endOfStreamReached)
                {
                    EndOfStreamReached?.Invoke(this, EventArgs.Empty);
                }

                if (framesRead == 0)
                {
                    // If we got here, it means no frames were read, and it wasn't an error, so we're done.
                    if (buffer is not null) ArrayPool<byte>.Shared.Return(buffer);
                    return 0;
                }

                if (SampleFormat is not SampleFormat.F32) ConvertToFloat(samples, framesRead, span);
                if (buffer is not null) ArrayPool<byte>.Shared.Return(buffer);

                return (int)framesRead * Channels;
            }
        }
    }

    private byte[]? GetBufferIfNeeded(int sampleLength)
    {
        // U32 can be done in-place with the passed in float span
        if (SampleFormat is SampleFormat.F32 or SampleFormat.S32)
        {
            return null;
        }
        var byteSize = SampleFormat switch
        {
            SampleFormat.S16 => sampleLength * 2,
            SampleFormat.S24 => sampleLength * 3,
            SampleFormat.U8 => sampleLength,
            _ => throw new NotSupportedException($"Sample format {SampleFormat} is not supported.")
        };
        return ArrayPool<byte>.Shared.Rent(byteSize);
    }

    private void ConvertToFloat(Span<float> samples, ulong framesRead, Span<byte> nativeBuffer)
    {
        var sampleCount = checked((int)framesRead * Channels);
        switch (SampleFormat)
        {
            case SampleFormat.S16:
                var shortSpan = MemoryMarshal.Cast<byte, short>(nativeBuffer);
                for (var i = 0; i < sampleCount; i++)
                    samples[i] = shortSpan[i] / (float)short.MaxValue;
                break;
            case SampleFormat.S24:
                for (var i = 0; i < sampleCount; i++)
                {
                    // Read 3 bytes as a 24-bit integer
                    var sample24 = (nativeBuffer[i * 3] << 0) | (nativeBuffer[i * 3 + 1] << 8) | (nativeBuffer[i * 3 + 2] << 16);
                    if ((sample24 & 0x800000) != 0) // Sign extension for negative values
                        sample24 |= unchecked((int)0xFF000000);
                    samples[i] = sample24 / 8388608f;
                }
                break;
            case SampleFormat.S32:
                var int32Span = MemoryMarshal.Cast<byte, int>(nativeBuffer);
                for (var i = 0; i < sampleCount; i++)
                    samples[i] = int32Span[i] / (float)int.MaxValue;
                break;
            case SampleFormat.U8:
                for (var i = 0; i < sampleCount; i++)
                    samples[i] = (nativeBuffer[i] - 128) / 128f; // Scale U8 to -1.0 to 1.0
                break;
        }
    }

    /// <summary>
    ///     Seek to start decoding at the given offset.
    /// </summary>
    public bool Seek(int offset)
    {
        lock (_syncLock)
        {
            if (IsDisposed || offset < 0) return false;

            // A zero Length is NOT a reason to refuse: miniaudio reports no length for some
            // formats (notably Vorbis when it is driven in push mode) yet still seeks fine.
            // Re-query opportunistically - it costs nothing and keeps Length useful - then
            // let the native decoder decide whether the seek is possible.
            if (Length == 0 &&
                Native.DecoderGetLengthInPcmFrames(_decoder, out var length) == MiniAudioResult.Success)
            {
                Length = (int)length * Channels;
            }

            var miniAudioResult = Native.DecoderSeekToPcmFrame(_decoder, (ulong)(offset / Channels));
            if (miniAudioResult != MiniAudioResult.Success) return false;

            _endOfStreamReached = false;
            return true;
        }
    }

    /// <summary>
    /// Disposes of the decoder resources.
    /// </summary>
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~MiniAudioDecoder()
    {
        Dispose(false);
    }

    private MiniAudioResult ReadCallback(nint pDecoder, nint pBufferOut, ulong bytesToRead, out ulong pBytesRead)
    {
        try
        {
            lock (_syncLock)
            {
                if (!_stream.CanRead)
                {
                    pBytesRead = 0;
                    return MiniAudioResult.NoDataAvailable;
                }

                var size = (int)bytesToRead;
                if (size <= 0) 
                {
                     pBytesRead = 0;
                     return MiniAudioResult.Success;
                }

                // Use ArrayPool to avoid allocating a new buffer on every read
                if (_rentedReadBuffer == null || _rentedReadBuffer.Length < size)
                {
                    if (_rentedReadBuffer != null)
                        ArrayPool<byte>.Shared.Return(_rentedReadBuffer);
                    
                    _rentedReadBuffer = ArrayPool<byte>.Shared.Rent(size);
                }

                var read = _stream.Read(_rentedReadBuffer, 0, size);
                
                if (read > 0)
                {
                    fixed (byte* pReadBuffer = _rentedReadBuffer)
                    {
                        Buffer.MemoryCopy(pReadBuffer, (void*)pBufferOut, size, read);
                    }
                }

                pBytesRead = (ulong)read;
                return MiniAudioResult.Success;
            }
        }
        catch (Exception)
        {
            // Swallow exception to prevent runtime crash, signal I/O error to miniaudio
            pBytesRead = 0;
            Log.Critical("Failed to read PCM frames from decoder.");
            return MiniAudioResult.IoError;
        }
    }

    private MiniAudioResult SeekCallback(nint _, long byteOffset, SeekPoint point)
    {
        try
        {
            lock (_syncLock)
            {
                if (!_stream.CanSeek)
                    return MiniAudioResult.NoDataAvailable;

                // Basic bounds check to prevent seeking past EOF if stream supports Length
                try 
                {
                    if (byteOffset >= 0 && byteOffset < _stream.Length - 1)
                        _stream.Seek(byteOffset, point == SeekPoint.FromCurrent ? SeekOrigin.Current : SeekOrigin.Begin);
                }
                catch (NotSupportedException)
                {
                    // Some streams claim CanSeek but throw on Length or Position
                    Log.Critical("Stream does not support seeking.");
                    return MiniAudioResult.InvalidOperation;
                }

                return MiniAudioResult.Success;
            }
        }
        catch (Exception)
        {
            Log.Critical("Failed to seek stream.");
            return MiniAudioResult.IoError;
        }
    }

    private void Dispose(bool _)
    {
        lock (_syncLock)
        {
            if (IsDisposed) return;

            // Return rented buffer if it exists
            if (_rentedReadBuffer != null)
            {
                ArrayPool<byte>.Shared.Return(_rentedReadBuffer);
                _rentedReadBuffer = null;
            }

            Native.DecoderUninit(_decoder);
            Native.Free(_decoder);

            // Only safe after DecoderUninit: while the decoder lives it holds pointers into this
            // block (miniaudio never copies the data it is initialized from).
            ReleaseOwnedData();

            // Keep delegates alive until after Uninit to prevent GC during callback (defensive)
            GC.KeepAlive(_readCallback);
            GC.KeepAlive(_seekCallbackCallback);

            IsDisposed = true;
        }
    }
}