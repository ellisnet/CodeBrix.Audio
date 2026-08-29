using System;
using System.Threading;
using CodeBrix.Audio.Codecs;
using CodeBrix.Audio.Engine.Components;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Engine.Metadata.Models;
using CodeBrix.Audio.Wave;
using EnginePlaybackState = CodeBrix.Audio.Engine.Enums.PlaybackState;
using EngineSampleFormat = CodeBrix.Audio.Engine.Enums.SampleFormat;

namespace CodeBrix.Audio.Playback;

/// <summary>
/// Plays audio that arrives as compressed CONTAINER PACKETS - the shape a demultiplexer produces -
/// through the process-wide shared output, decoding each packet on the audio thread as the mixer
/// asks for samples.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="AudioFilePlayer"/> plays a file; this plays a packet feed that some other component is
/// pulling out of a container. It is the supported route for that, because the shared output has no
/// resampler on its raw-PCM entry point (<see cref="WaveOutEvent"/> refuses a source whose rate does
/// not match the running device), while this player decodes through the engine's own conversion and
/// therefore plays at whatever rate the packets carry.
/// </para>
/// <para>
/// THE PACKET FEED IS PULLED, NOT PUSHED. The player asks <see cref="IAudioPacketSource"/> for the
/// next packet on the audio thread, exactly when it needs one, and never blocks: a source that has
/// nothing ready yet says so and the player plays silence for that moment, keeping the voice alive.
/// Playback ends only when the source reports <see cref="IAudioPacketSource.EndOfStream"/> and the
/// last decoded samples have been handed to the mixer, at which point
/// <see cref="PlaybackEnded"/> is raised away from the audio thread.
/// </para>
/// <para>
/// <see cref="Position"/> is the clock: it counts the audio actually delivered since the last
/// <see cref="Seek"/>, at the codec's own sample rate, and is readable from any thread. An
/// application synchronising something else to the audio - pictures, say - should read it rather
/// than keeping a clock of its own.
/// </para>
/// <para>
/// RATE ADVICE: the only rate conversion in this package is linear interpolation, so an application
/// built around packet audio should call <c>SharedAudioOutput.Configure(48000)</c> at start-up. 48
/// kHz is what media containers carry, and when the device runs at the media's rate no conversion
/// runs at all.
/// </para>
/// <para>
/// TWO THINGS THE CONTAINER KNOWS AND THE CODEC DOES NOT, both handled here.
/// <see cref="SetTrailingTrim(TimeSpan)"/> says how much of the END of the track is encoder padding
/// that must never be heard, and <see cref="AudioPacket.Loss(System.TimeSpan, System.Nullable{System.TimeSpan})"/>
/// says that packets went missing and how much audio they held, so the gap comes out the length it
/// really was instead of the audio after it sliding earlier.
/// </para>
/// <para>Dispose when finished; the voice is removed from the mixer and the decoder released.</para>
/// </remarks>
public sealed class PacketAudioPlayer : IDisposable
{
    private readonly object _lock = new object();

    private PacketDecoderAdapter _adapter;
    private PacketDataProvider _provider;
    private SoundPlayer _player;
    private SynchronizationContext _syncContext;
    private float _volume = 1.0f;
    private TimeSpan _trailingTrim;
    private int _trailingTrimFrames;
    private bool _trailingTrimInFrames;
    private bool _endedRaised;
    private bool _disposed;

    /// <summary>
    /// Raised once, after the source has reported the end of the stream and the last decoded samples
    /// have been delivered. Not raised for <see cref="Stop"/>, and not raised for an underrun.
    /// </summary>
    /// <remarks>
    /// Raised on the <see cref="SynchronizationContext"/> captured when the audio was opened, if
    /// there was one (a UI thread, typically); otherwise on a thread-pool thread. Never on the audio
    /// thread, so a handler is free to take locks or tear the player down.
    /// </remarks>
    public event EventHandler PlaybackEnded;

    /// <summary>Whether a packet feed is currently open and ready to play.</summary>
    public bool IsOpen
    {
        get { lock (_lock) { return _player != null; } }
    }

    /// <summary>The current playback state (Stopped / Playing / Paused).</summary>
    public PlaybackState PlaybackState
    {
        get { lock (_lock) { return _player == null ? PlaybackState.Stopped : Map(_player.State); } }
    }

    /// <summary>
    /// How far into the audio playback has reached: the position established by the last
    /// <see cref="Seek"/> (or zero) plus every sample handed to the mixer since, measured at the
    /// codec's own sample rate.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Silence played through an underrun does NOT advance it - only real decoded audio does - so it
    /// stays honest about how much of the media has actually been heard. Samples discarded as
    /// codec priming or seek pre-roll DO advance it, because they are media time: once the pre-roll
    /// after a <see cref="Seek"/> has been discarded, the clock reads the position that was sought
    /// to.
    /// </para>
    /// <para>
    /// It counts samples handed to the mixer, so it runs a fraction of a buffer ahead of what a
    /// listener is hearing - the same small lead every clock of this kind has.
    /// </para>
    /// </remarks>
    public TimeSpan Position
    {
        get
        {
            var adapter = _adapter;
            return adapter == null ? TimeSpan.Zero : adapter.Position;
        }
    }

    /// <summary>Playback volume, where 1.0 is unity gain. Persists across opens.</summary>
    public float Volume
    {
        get { lock (_lock) { return _volume; } }
        set
        {
            lock (_lock)
            {
                _volume = value;
                if (_player != null)
                {
                    _player.Volume = value;
                }
            }
        }
    }

    /// <summary>The sample rate of the open audio, in Hz; 0 when nothing is open.</summary>
    public int SampleRate
    {
        get { lock (_lock) { return _adapter == null ? 0 : _adapter.NativeSampleRate; } }
    }

    /// <summary>The channel count of the open audio; 0 when nothing is open.</summary>
    public int Channels
    {
        get { lock (_lock) { return _adapter == null ? 0 : _adapter.NativeChannels; } }
    }

    /// <summary>
    /// How much of the very END of the audio is never played: the encoder padding a container
    /// records for the last of a track's packets. <see cref="TimeSpan.Zero"/> - no trim - unless
    /// <see cref="SetTrailingTrim(TimeSpan)"/> or <see cref="SetTrailingTrimFrames"/> said otherwise.
    /// </summary>
    /// <remarks>
    /// A trim set in frames reads back as a duration once audio is open, because that is when the
    /// sample rate turning frames into time is known; before then it reads
    /// <see cref="TimeSpan.Zero"/>.
    /// </remarks>
    public TimeSpan TrailingTrim
    {
        get
        {
            lock (_lock)
            {
                if (!_trailingTrimInFrames)
                {
                    return _trailingTrim;
                }
                return _adapter == null
                    ? TimeSpan.Zero
                    : TimeSpan.FromSeconds(_trailingTrimFrames / (double)_adapter.NativeSampleRate);
            }
        }
    }

    /// <summary>
    /// Says how much of the very END of the audio must never be heard, because it is encoder padding
    /// rather than content. Settable before or after <see cref="Open(string, ReadOnlyMemory{byte}, IAudioPacketSource)"/>,
    /// and at any time before the source reaches its end.
    /// </summary>
    /// <param name="trim">
    /// How much of the tail to swallow. <see cref="TimeSpan.Zero"/> - the default - plays everything
    /// the packets contain.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="trim"/> is negative.</exception>
    /// <exception cref="ObjectDisposedException">The player has been disposed.</exception>
    /// <remarks>
    /// <para>
    /// WHY IT EXISTS. An encoder pads the end of what it encodes, and the container - not the codec -
    /// is what records how much: Matroska writes a DiscardPadding on the last block, other containers
    /// state a trailing sample count. Without this the padding plays, which is tens of milliseconds
    /// of encoder tail at the end of every track.
    /// </para>
    /// <para>
    /// WHAT IT DOES. The last <paramref name="trim"/> of everything the source will ever deliver is
    /// held back and then thrown away. The player cannot know which packet is the last one until the
    /// source says so, so it keeps the most recent <paramref name="trim"/> worth of decoded audio in
    /// hand and releases a sample to the mixer only once more than that much has been decoded behind
    /// it. When the source reports the end of the stream, what is still in hand is discarded. The
    /// cost is latency of exactly <paramref name="trim"/> - normally less than one packet - and no
    /// allocation while playing.
    /// </para>
    /// <para>
    /// <see cref="Position"/> never counts trimmed audio, because it counts what reached the mixer.
    /// <see cref="Seek"/> clears what is in hand (the audio around a jump is not the end of the
    /// track) but keeps the trim itself, which belongs to the track rather than to the moment. So
    /// does the trim survive <see cref="Open(string, ReadOnlyMemory{byte}, IAudioPacketSource)"/>:
    /// set it again - or to <see cref="TimeSpan.Zero"/> - when opening a different track.
    /// </para>
    /// <para>
    /// A packet may also carry its own <see cref="AudioPacket.DiscardPadding"/>, and the larger of
    /// the two wins, so a container may pass its per-block value through instead of calling this.
    /// A trim longer than the whole track leaves nothing to hear and still ends cleanly, raising
    /// <see cref="PlaybackEnded"/>.
    /// </para>
    /// </remarks>
    public void SetTrailingTrim(TimeSpan trim)
    {
        if (trim < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(trim), trim, "A trailing trim cannot be negative.");
        }

        lock (_lock)
        {
            ThrowIfDisposed();
            _trailingTrim = trim;
            _trailingTrimFrames = 0;
            _trailingTrimInFrames = false;
            if (_adapter != null)
            {
                _adapter.SetTrailingTrimFrames(FramesFromDuration(trim, _adapter.NativeSampleRate));
            }
        }
    }

    /// <summary>
    /// The exact form of <see cref="SetTrailingTrim(TimeSpan)"/>, for a container that states its
    /// trailing padding as a sample count rather than as a duration.
    /// </summary>
    /// <param name="frames">
    /// How much of the tail to swallow, counted in FRAMES PER CHANNEL at the decoder's own sample
    /// rate - the unit <c>IPacketSoundDecoder.PreSkipSamples</c> uses at the other end of the track.
    /// Zero plays everything.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="frames"/> is negative.</exception>
    /// <exception cref="ObjectDisposedException">The player has been disposed.</exception>
    /// <remarks>
    /// Everything <see cref="SetTrailingTrim(TimeSpan)"/> says applies here; only the unit differs.
    /// Frames go through no rounding, so a container that knows its padding to the sample should say
    /// it this way.
    /// </remarks>
    public void SetTrailingTrimFrames(int frames)
    {
        if (frames < 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frames), frames, "A trailing trim cannot be negative.");
        }

        lock (_lock)
        {
            ThrowIfDisposed();
            _trailingTrimFrames = frames;
            _trailingTrim = TimeSpan.Zero;
            _trailingTrimInFrames = true;
            if (_adapter != null)
            {
                _adapter.SetTrailingTrimFrames(frames);
            }
        }
    }

    /// <summary>
    /// Opens a packet feed, resolving the decoder for <paramref name="codecId"/> through the codecs
    /// registered with the shared output. Replaces anything previously open.
    /// </summary>
    /// <param name="codecId">The codec identifier, lowercase - "vorbis", "opus".</param>
    /// <param name="codecPrivate">
    /// The codec's initialisation data exactly as the container carried it: the three Xiph-laced
    /// setup headers for Vorbis, the identification-header bytes for Opus.
    /// </param>
    /// <param name="source">Where the compressed packets come from.</param>
    /// <exception cref="ArgumentException"><paramref name="codecId"/> is null or empty.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="source"/> is null.</exception>
    /// <exception cref="NotSupportedException">
    /// No packet decoder is registered for that codec. Codecs this package does not carry are
    /// registered from an add-on package with
    /// <see cref="SharedAudioOutput.RegisterPacketCodecFactory"/>.
    /// </exception>
    /// <remarks>
    /// Opening starts the shared output if it is not already running - at 48 kHz unless
    /// <see cref="SharedAudioOutput.Configure"/> pinned a rate - and the voice is added to the mixer
    /// straight away, stopped. Call <see cref="Play"/> to start it.
    /// </remarks>
    public void Open(string codecId, ReadOnlyMemory<byte> codecPrivate, IAudioPacketSource source)
    {
        if (string.IsNullOrEmpty(codecId))
        {
            throw new ArgumentException("A codec identifier is required.", nameof(codecId));
        }
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        var decoder = SharedAudioOutput.CreatePacketDecoder(codecId, codecPrivate);
        try
        {
            OpenCore(decoder, source, ownsDecoder: true);
        }
        catch (Exception)
        {
            decoder.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Opens a packet feed with a decoder the caller already has. Replaces anything previously open.
    /// </summary>
    /// <param name="decoder">The decoder for the packets <paramref name="source"/> will deliver.</param>
    /// <param name="source">Where the compressed packets come from.</param>
    /// <param name="leaveOpen">
    /// When <see langword="false"/> (the default) the decoder is disposed with this player; when
    /// <see langword="true"/> the caller keeps ownership of it.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="decoder"/> or <paramref name="source"/> is null.</exception>
    public void Open(IPacketSoundDecoder decoder, IAudioPacketSource source, bool leaveOpen = false)
    {
        if (decoder == null)
        {
            throw new ArgumentNullException(nameof(decoder));
        }
        if (source == null)
        {
            throw new ArgumentNullException(nameof(source));
        }

        OpenCore(decoder, source, ownsDecoder: !leaveOpen);
    }

    /// <summary>Starts or resumes playback.</summary>
    /// <exception cref="InvalidOperationException">Nothing is open.</exception>
    /// <exception cref="ObjectDisposedException">The player has been disposed.</exception>
    public void Play()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            RequireOpen();
            _player.Play();
        }
    }

    /// <summary>Pauses playback, keeping the position. The source is not asked for packets while paused.</summary>
    public void Pause()
    {
        lock (_lock)
        {
            if (_disposed || _player == null)
            {
                return;
            }
            _player.Pause();
        }
    }

    /// <summary>
    /// Stops playback. The position is left where it is and no packets are consumed until
    /// <see cref="Play"/> is called again; a caller that wants to start somewhere else calls
    /// <see cref="Seek"/> first.
    /// </summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (_disposed || _player == null)
            {
                return;
            }
            _player.Stop();
        }
    }

    /// <summary>
    /// Tells the player that the packet feed has jumped: the decoder is reset, the samples decoded
    /// from the old position are dropped, and the clock is re-based.
    /// </summary>
    /// <param name="firstPacketTimestamp">
    /// The timestamp of the first packet the source will hand over after this call. That is where
    /// <see cref="Position"/> starts counting again.
    /// </param>
    /// <param name="preRoll">
    /// How much audio to decode and throw away before any is heard, starting from
    /// <paramref name="firstPacketTimestamp"/>. Leave it at zero to hear everything from the first
    /// packet on.
    /// </param>
    /// <exception cref="InvalidOperationException">Nothing is open.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="preRoll"/> is negative.</exception>
    /// <remarks>
    /// <para>
    /// THE CONTRACT, because this player does not do the seeking - it cannot, having no container to
    /// seek in. The caller repositions its own source FIRST and then calls this, so that the very
    /// next <see cref="IAudioPacketSource.TryReadPacket"/> returns a packet from the new position.
    /// The order matters: calling this while the old packets are still queued would date the clock
    /// to the new position and then play the old audio against it.
    /// </para>
    /// <para>
    /// PRE-ROLL, because a codec that carries state between packets cannot decode correctly at a
    /// jump. The caller starts a little BEFORE its real target - one packet for Vorbis, about 80 ms
    /// for Opus - passes the timestamp of that earlier packet as
    /// <paramref name="firstPacketTimestamp"/>, and passes the gap as <paramref name="preRoll"/>.
    /// The player decodes that much and discards it, so the first audio heard is the target, and
    /// <see cref="Position"/> reads the target once the discard is done. Any codec priming the
    /// decoder declares of its own accord (an encoder delay) is discarded on top of that
    /// automatically, and only at the start of the stream.
    /// </para>
    /// <para>
    /// A source that had reached its end is expected to report
    /// <see cref="IAudioPacketSource.EndOfStream"/> as false again after being repositioned;
    /// <see cref="PlaybackEnded"/> can then be raised again for the new stretch of audio.
    /// </para>
    /// </remarks>
    public void Seek(TimeSpan firstPacketTimestamp, TimeSpan preRoll = default)
    {
        if (preRoll < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(preRoll), preRoll, "Pre-roll cannot be negative.");
        }

        lock (_lock)
        {
            ThrowIfDisposed();
            RequireOpen();

            var discardFrames = (int)Math.Round(preRoll.TotalSeconds * _adapter.NativeSampleRate);
            _adapter.Reposition(firstPacketTimestamp, discardFrames);
            _endedRaised = false;
        }
    }

    /// <summary>Stops playback, removes the voice from the mixer and releases the decoder (unless the caller kept it).</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            TearDown();
        }
    }

    private void OpenCore(IPacketSoundDecoder decoder, IAudioPacketSource source, bool ownsDecoder)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            TearDown();

            // The device is what the decoder has to convert to, so start it before building the
            // adapter. Packet audio is normally 48 kHz; the shared output adopts that when nothing
            // else has been configured or started.
            var device = SharedAudioOutput.EnsureStarted(
                decoder.SampleRate > 0 ? decoder.SampleRate : 48000);

            PacketDecoderAdapter adapter = null;
            PacketDataProvider provider = null;
            SoundPlayer player = null;
            try
            {
                adapter = new PacketDecoderAdapter(decoder, source, ownsDecoder,
                    device.Format.Channels, device.Format.SampleRate);
                provider = new PacketDataProvider(adapter);
                player = new SoundPlayer(device.Engine, device.Format, provider)
                {
                    Volume = _volume,
                };
                provider.EndOfStreamReached += OnProviderEndOfStream;
                SharedAudioOutput.AddComponentToMixer(player);
            }
            catch (Exception)
            {
                if (player != null)
                {
                    try { player.Dispose(); } catch (Exception) { /* best effort */ }
                }
                else if (provider != null)
                {
                    try { provider.Dispose(); } catch (Exception) { /* best effort */ }
                }
                else if (adapter != null)
                {
                    try { adapter.Dispose(); } catch (Exception) { /* best effort */ }
                }
                throw;
            }

            // The trailing trim belongs to the track and is set before or after opening, so whatever
            // is on the player now is handed to the adapter that will apply it.
            var trimFrames = _trailingTrimInFrames
                ? _trailingTrimFrames
                : FramesFromDuration(_trailingTrim, adapter.NativeSampleRate);
            if (trimFrames > 0)
            {
                adapter.SetTrailingTrimFrames(trimFrames);
            }

            _adapter = adapter;
            _provider = provider;
            _player = player;
            _endedRaised = false;
            if (_syncContext == null)
            {
                _syncContext = SynchronizationContext.Current;
            }
        }
    }

    // Fires on the engine's real-time audio thread the moment the decoder runs out for good; get off
    // that thread before doing anything, including stopping the voice.
    private void OnProviderEndOfStream(object sender, EventArgs e)
    {
        lock (_lock)
        {
            if (_disposed || _endedRaised)
            {
                return;
            }
            _endedRaised = true;
        }

        var context = _syncContext;
        if (context != null)
        {
            context.Post(_ => RaisePlaybackEnded(), null);
        }
        else
        {
            ThreadPool.QueueUserWorkItem(_ => RaisePlaybackEnded());
        }
    }

    private void RaisePlaybackEnded()
    {
        lock (_lock)
        {
            if (_disposed || _player == null)
            {
                return;
            }
            _player.Stop();
        }

        var handler = PlaybackEnded;
        if (handler != null)
        {
            handler(this, EventArgs.Empty);
        }
    }

    // Removes and disposes the current voice, provider and adapter. Callers hold _lock.
    private void TearDown()
    {
        if (_provider != null)
        {
            _provider.EndOfStreamReached -= OnProviderEndOfStream;
        }
        if (_player != null)
        {
            try { SharedAudioOutput.RemoveComponentFromMixer(_player); } catch (Exception) { /* output may be torn down */ }
            try { _player.Dispose(); } catch (Exception) { /* also disposes the data provider */ }
            _player = null;
        }
        if (_provider != null)
        {
            try { _provider.Dispose(); } catch (Exception) { /* best effort */ }
            _provider = null;
        }
        _adapter = null;
    }

    private static PlaybackState Map(EnginePlaybackState state)
    {
        switch (state)
        {
            case EnginePlaybackState.Playing:
                return PlaybackState.Playing;
            case EnginePlaybackState.Paused:
                return PlaybackState.Paused;
            default:
                return PlaybackState.Stopped;
        }
    }

    private void RequireOpen()
    {
        if (_player == null)
        {
            throw new InvalidOperationException("Open a packet source before controlling playback.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(PacketAudioPlayer));
        }
    }

    // A duration in frames at a given rate, rounded to the nearest frame and never negative.
    internal static int FramesFromDuration(TimeSpan duration, int sampleRate)
    {
        if (duration <= TimeSpan.Zero || sampleRate <= 0)
        {
            return 0;
        }

        var frames = Math.Round(duration.TotalSeconds * sampleRate);
        return frames >= int.MaxValue ? int.MaxValue : (int)frames;
    }

    /// <summary>
    /// Turns the packet feed into the stream of samples the engine wants: pulls packets, decodes
    /// them, and lets <see cref="ManagedSoundDecoder"/> do the channel and rate conversion to the
    /// device's format.
    /// </summary>
    internal sealed class PacketDecoderAdapter : ManagedSoundDecoder
    {
        private readonly object clockLock = new object();
        private readonly IPacketSoundDecoder packetDecoder;
        private readonly IAudioPacketSource source;
        private readonly bool ownsDecoder;
        private readonly float[] packetSamples;

        private readonly object trimLock = new object();

        private int pendingOffset;
        private int pendingCount;
        private int discardFrames;
        private int pendingLossFrames;
        private bool sourceEnded;

        private long decodedFrames;
        private long baseTicks;

        private long pendingBaseTicks;
        private int pendingDiscardFrames;

        // THE TRAILING-TRIM HOLD-BACK. A ring of the most recently decoded samples that have NOT been
        // handed to the mixer yet, because they might turn out to be the encoder padding at the end
        // of the track. A sample leaves the ring only once holdCapacity samples have been decoded
        // behind it; whatever is still in the ring when the source ends is thrown away.
        //
        // holdBuffer only ever grows, so lowering the trim needs no allocation and raising it needs
        // one only when the new trim is longer than any previous one. holdCount may briefly exceed
        // holdCapacity after the trim is lowered; the surplus is released before anything new goes in.
        private float[] holdBuffer = new float[0];
        private int holdStart;
        private int holdCount;
        private int trimSamples;
        private int packetTrimSamples;
        private int holdCapacity;

        // Set by SetTrailingTrimFrames on whatever thread the application calls it from, and picked
        // up by the audio thread at the top of the next read. The replacement buffer is allocated by
        // the setting thread so the audio thread does not have to.
        private volatile bool trimChangePending;
        private float[] pendingHoldBuffer;
        private int pendingTrimSamples;

        public PacketDecoderAdapter(IPacketSoundDecoder packetDecoder, IAudioPacketSource source,
            bool ownsDecoder, int channels, int sampleRate)
            : base(channels, sampleRate)
        {
            this.packetDecoder = packetDecoder;
            this.source = source;
            this.ownsDecoder = ownsDecoder;

            NativeChannels = packetDecoder.Channels;
            NativeSampleRate = packetDecoder.SampleRate;

            if (NativeChannels <= 0 || NativeSampleRate <= 0)
            {
                throw new ArgumentException(
                    "The packet decoder reported no channel count or sample rate, so its output cannot be played.",
                    nameof(packetDecoder));
            }

            var packetCapacity = Math.Max(packetDecoder.MaxSamplesPerPacket, NativeChannels);
            packetSamples = new float[packetCapacity];

            // The codec's own priming (an encoder delay) is discarded at the start of the stream.
            discardFrames = Math.Max(0, packetDecoder.PreSkipSamples);

            // Total frames unknown: a packet feed has no length until its container says so.
            Initialize(NativeChannels, NativeSampleRate, 0);
        }

        /// <summary>The channel count the packets decode to.</summary>
        public int NativeChannels { get; }

        /// <summary>The sample rate the packets decode at, in Hz.</summary>
        public int NativeSampleRate { get; }

        /// <summary>Where playback has reached: the last re-based position plus everything handed over since.</summary>
        public TimeSpan Position
        {
            get
            {
                lock (clockLock)
                {
                    return new TimeSpan(baseTicks) +
                           TimeSpan.FromSeconds(decodedFrames / (double)NativeSampleRate);
                }
            }
        }

        /// <summary>
        /// Re-bases the clock and drops everything decoded from the old position. The source must
        /// already be repositioned - see <see cref="PacketAudioPlayer.Seek"/>.
        /// </summary>
        /// <param name="basePosition">The timestamp of the first packet the source will now deliver.</param>
        /// <param name="discard">How many frames to decode and throw away before delivering any.</param>
        public void Reposition(TimeSpan basePosition, int discard)
        {
            pendingBaseTicks = basePosition.Ticks;
            pendingDiscardFrames = Math.Max(0, discard);

            // Seek(0) is how the base class's interpolation state gets cleared; the packet-side
            // state is cleared in SeekSource, which it calls under the same lock the decode path
            // holds, so nothing here races the audio thread.
            Seek(0);
        }

        /// <summary>
        /// Sets how much of the end of the track must never be heard, in frames per channel at the
        /// decoder's own rate. Safe to call from any thread, at any time.
        /// </summary>
        /// <param name="frames">The trailing trim in frames per channel; 0 plays everything.</param>
        public void SetTrailingTrimFrames(int frames)
        {
            var samples = (long)Math.Max(0, frames) * NativeChannels;
            var capped = samples > int.MaxValue ? int.MaxValue : (int)samples;

            lock (trimLock)
            {
                pendingTrimSamples = capped;

                // Allocate here rather than on the audio thread. A buffer that is already big enough
                // is kept, which is what makes lowering the trim - and setting it back to zero -
                // allocation-free.
                pendingHoldBuffer = capped > holdBuffer.Length ? new float[capped] : null;
                trimChangePending = true;
            }
        }

        /// <inheritdoc />
        protected override int ReadSourceSamples(Span<float> destination)
        {
            if (trimChangePending)
            {
                ApplyTrimChange();
            }

            var written = 0;
            long framesAdvanced = 0;

            while (written < destination.Length)
            {
                if (holdCount > holdCapacity)
                {
                    // The trim was lowered while audio was in flight; what is no longer inside the
                    // window is owed to the mixer, so it goes out before anything new comes in.
                    var flushed = PushThroughHoldBack(ReadOnlySpan<float>.Empty,
                        destination.Slice(written), out _);
                    written += flushed;
                    framesAdvanced += flushed / NativeChannels;
                    continue;
                }

                if (pendingCount > 0)
                {
                    int take;
                    int handedOver;

                    if (holdCapacity == 0)
                    {
                        // No trim: the samples go straight out, exactly as they did before the
                        // hold-back existed.
                        take = Math.Min(pendingCount, destination.Length - written);
                        new ReadOnlySpan<float>(packetSamples, pendingOffset, take)
                            .CopyTo(destination.Slice(written, take));
                        handedOver = take;
                    }
                    else
                    {
                        handedOver = PushThroughHoldBack(
                            new ReadOnlySpan<float>(packetSamples, pendingOffset, pendingCount),
                            destination.Slice(written), out take);
                    }

                    pendingOffset += take;
                    pendingCount -= take;
                    written += handedOver;

                    // The clock counts audio handed over, not audio decoded: a packet decoded but
                    // still waiting in the buffer - or held back as possible end-of-track padding -
                    // has not been heard yet.
                    framesAdvanced += handedOver / NativeChannels;
                    continue;
                }

                if (pendingLossFrames > 0)
                {
                    // A gap the source reported: as much concealment as the decoder will give for it,
                    // silence for the rest, in helpings of at most one packet.
                    framesAdvanced += ProduceConcealment();
                    continue;
                }

                if (sourceEnded)
                {
                    break;
                }

                AudioPacket packet;
                if (!source.TryReadPacket(out packet))
                {
                    if (source.EndOfStream)
                    {
                        // Everything the source will ever deliver has been decoded and handed over.
                        // What is still held back IS the end-of-track padding: drop it.
                        sourceEnded = true;
                        holdStart = 0;
                        holdCount = 0;
                        break;
                    }

                    // UNDERRUN. The reader has not kept up, which is a hiccup, not an ending: fill
                    // the rest of this buffer with silence and leave the voice running so playback
                    // continues the moment packets arrive again.
                    destination.Slice(written).Clear();
                    written = destination.Length;
                    break;
                }

                if (packet.IsLoss)
                {
                    var lostFrames = packet.LossFrames > 0
                        ? packet.LossFrames
                        : FramesFromDuration(packet.LossDuration, NativeSampleRate);
                    pendingLossFrames = lostFrames;
                    continue;
                }

                SetPacketTrim(packet.DiscardPadding);

                // An empty packet is the lengthless way of saying one packet was lost; the decoder
                // is the one that knows what to do about it, so it is passed straight through.
                var produced = packetDecoder.DecodePacket(packet.Data.Span, packetSamples);

                if (produced <= 0)
                {
                    // Normal: a lapped-transform codec finalises nothing for the first packet after
                    // a reset. Ask for the next one.
                    continue;
                }

                pendingOffset = 0;
                pendingCount = produced;
                framesAdvanced += TakeStartDiscard();
            }

            if (framesAdvanced > 0)
            {
                lock (clockLock)
                {
                    decodedFrames += framesAdvanced;
                }
            }

            return written;
        }

        // Drops as much of what was just decoded as the codec's priming or a seek pre-roll still
        // calls for, and returns how many frames that was. Discarded audio is media time even though
        // nobody hears it, so the clock reads the sought-to position once a pre-roll is worked
        // through.
        private int TakeStartDiscard()
        {
            if (discardFrames <= 0)
            {
                return 0;
            }

            var dropFrames = Math.Min(discardFrames, pendingCount / NativeChannels);
            pendingOffset += dropFrames * NativeChannels;
            pendingCount -= dropFrames * NativeChannels;
            discardFrames -= dropFrames;
            return dropFrames;
        }

        // Fills the packet buffer with one helping of concealment for the gap still outstanding, and
        // returns the frames of it that a start discard swallowed.
        private int ProduceConcealment()
        {
            // Whole frames only: the buffer is sized to the decoder's own MaxSamplesPerPacket.
            var roomFrames = packetSamples.Length / NativeChannels;
            if (pendingLossFrames <= 0 || roomFrames <= 0)
            {
                pendingLossFrames = 0;
                return 0;
            }

            // The decoder is told how much is STILL missing, not how much will fit, so a codec that
            // conceals in its own fixed steps can choose the step. What comes back is capped by both.
            var produced = packetDecoder.ConcealLoss(pendingLossFrames, packetSamples);
            var cap = (int)Math.Min((long)pendingLossFrames * NativeChannels, roomFrames * (long)NativeChannels);

            if (produced <= 0)
            {
                // The decoder has no concealment to offer. Silence of exactly the right length keeps
                // the timeline honest: what follows the gap stays where it belongs instead of
                // sliding earlier by the length of what was lost.
                new Span<float>(packetSamples, 0, cap).Clear();
                produced = cap;
            }
            else if (produced > cap)
            {
                // A decoder that hands back more than the gap - or more than the buffer holds - is
                // trimmed to it rather than trusted.
                produced = cap;
            }

            pendingOffset = 0;
            pendingCount = produced;

            var coveredFrames = produced / NativeChannels;
            pendingLossFrames = Math.Max(0, pendingLossFrames - coveredFrames);

            return TakeStartDiscard();
        }

        // Remembers the padding the most recent packet declared. It raises the hold-back only while
        // that packet is the most recent one, so a value on the LAST packet trims the track and a
        // value anywhere else merely delays audio that the next packet lets through.
        private void SetPacketTrim(TimeSpan discardPadding)
        {
            var frames = FramesFromDuration(discardPadding, NativeSampleRate);
            var samples = (long)frames * NativeChannels;
            var capped = samples > int.MaxValue ? int.MaxValue : (int)samples;
            if (capped == packetTrimSamples)
            {
                return;
            }

            packetTrimSamples = capped;
            UpdateHoldCapacity(null);
        }

        // Picks up a trim change published by SetTrailingTrimFrames.
        private void ApplyTrimChange()
        {
            float[] supplied;
            int samples;

            lock (trimLock)
            {
                trimChangePending = false;
                supplied = pendingHoldBuffer;
                pendingHoldBuffer = null;
                samples = pendingTrimSamples;
            }

            trimSamples = samples;
            UpdateHoldCapacity(supplied);
        }

        // Recomputes the hold-back window from the track-level trim and the most recent packet's
        // padding, growing the ring if the window no longer fits in it.
        private void UpdateHoldCapacity(float[] supplied)
        {
            var wanted = Math.Max(trimSamples, packetTrimSamples);
            if (wanted > holdBuffer.Length)
            {
                GrowHoldBuffer(wanted, supplied);
            }
            holdCapacity = wanted;
        }

        // Moves whatever is held into a bigger ring, oldest sample first, so nothing is lost when the
        // window grows.
        private void GrowHoldBuffer(int samples, float[] supplied)
        {
            var replacement = supplied != null && supplied.Length >= samples ? supplied : new float[samples];
            var held = holdCount;
            if (held > 0)
            {
                CopyOutOfRing(new Span<float>(replacement, 0, held));
            }

            holdBuffer = replacement;
            holdStart = 0;
            holdCount = held;
        }

        // Pushes as much of input through the hold-back as destination has room for, releasing the
        // samples that fall out of the back of the window. Returns the samples written to
        // destination; consumed receives the samples taken from input.
        private int PushThroughHoldBack(ReadOnlySpan<float> input, Span<float> destination, out int consumed)
        {
            var room = holdCapacity - holdCount;                  // negative after the trim is lowered
            var maxConsume = destination.Length + room;
            if (maxConsume < 0)
            {
                maxConsume = 0;
            }
            consumed = Math.Min(input.Length, maxConsume);

            var release = holdCount + consumed - holdCapacity;
            if (release < 0)
            {
                release = 0;
            }
            if (release > destination.Length)
            {
                release = destination.Length;
            }

            var written = 0;

            var fromRing = Math.Min(release, holdCount);
            if (fromRing > 0)
            {
                CopyOutOfRing(destination.Slice(0, fromRing));
                written = fromRing;
            }

            // Once the ring is empty the rest of what is released comes straight from the input; only
            // what is left after that goes into the ring.
            var fromInput = release - fromRing;
            if (fromInput > 0)
            {
                input.Slice(0, fromInput).CopyTo(destination.Slice(written, fromInput));
                written += fromInput;
            }

            var intoRing = consumed - fromInput;
            if (intoRing > 0)
            {
                CopyIntoRing(input.Slice(fromInput, intoRing));
            }

            return written;
        }

        // Takes the oldest samples out of the ring, wrapping if they straddle the end of the buffer.
        private void CopyOutOfRing(Span<float> destination)
        {
            var count = destination.Length;
            var first = Math.Min(count, holdBuffer.Length - holdStart);
            new ReadOnlySpan<float>(holdBuffer, holdStart, first).CopyTo(destination);
            if (first < count)
            {
                new ReadOnlySpan<float>(holdBuffer, 0, count - first).CopyTo(destination.Slice(first));
            }

            holdStart += count;
            if (holdStart >= holdBuffer.Length)
            {
                holdStart -= holdBuffer.Length;
            }
            holdCount -= count;
        }

        // Appends samples to the ring, wrapping if they straddle the end of the buffer.
        private void CopyIntoRing(ReadOnlySpan<float> source)
        {
            var count = source.Length;
            var end = holdStart + holdCount;
            if (end >= holdBuffer.Length)
            {
                end -= holdBuffer.Length;
            }

            var first = Math.Min(count, holdBuffer.Length - end);
            source.Slice(0, first).CopyTo(new Span<float>(holdBuffer, end, first));
            if (first < count)
            {
                source.Slice(first).CopyTo(new Span<float>(holdBuffer, 0, count - first));
            }

            holdCount += count;
        }

        /// <inheritdoc />
        /// <remarks>
        /// There is nothing here to seek IN - the container does that, on the far side of the packet
        /// source - so this is the reset half of a seek: drop the codec's inter-packet state, drop
        /// the samples decoded from the old position, and start the clock again from the new base.
        /// </remarks>
        protected override bool SeekSource(long frameIndex)
        {
            packetDecoder.Reset();
            pendingOffset = 0;
            pendingCount = 0;
            pendingLossFrames = 0;
            sourceEnded = false;
            discardFrames = pendingDiscardFrames;

            // What is held back describes the audio just before the jump, and a jump is not the end
            // of the track, so it is dropped. The trim itself belongs to the track and stays; so does
            // the ring it lives in, which is why a seek allocates nothing. A per-packet padding is a
            // property of a packet that is no longer the current one.
            holdStart = 0;
            holdCount = 0;
            if (packetTrimSamples != 0)
            {
                packetTrimSamples = 0;
                UpdateHoldCapacity(null);
            }

            lock (clockLock)
            {
                baseTicks = pendingBaseTicks;
                decodedFrames = 0;
            }

            return true;
        }

        /// <inheritdoc />
        protected override void DisposeCore()
        {
            if (ownsDecoder)
            {
                packetDecoder.Dispose();
            }
        }
    }

    /// <summary>
    /// Presents the adapter to the engine's sound player as a data provider of unknown length.
    /// </summary>
    internal sealed class PacketDataProvider : ISoundDataProvider
    {
        private readonly PacketDecoderAdapter decoder;
        private int position;

        public PacketDataProvider(PacketDecoderAdapter decoder)
        {
            this.decoder = decoder;
        }

        /// <inheritdoc />
        public int Position => position;

        /// <inheritdoc />
        /// <remarks>
        /// Zero, meaning unknown: a packet feed has no length of its own, and the engine reads zero
        /// as "live stream" - which is what keeps it from ending playback of its own accord when a
        /// read comes back empty. This player raises <see cref="PacketAudioPlayer.PlaybackEnded"/>
        /// itself instead, because only the packet source knows the difference between an underrun
        /// and an ending.
        /// </remarks>
        public int Length => 0;

        /// <inheritdoc />
        public bool CanSeek => false;

        /// <inheritdoc />
        public EngineSampleFormat SampleFormat => decoder.SampleFormat;

        /// <inheritdoc />
        public int SampleRate => decoder.SampleRate;

        /// <inheritdoc />
        public bool IsDisposed { get; private set; }

        /// <inheritdoc />
        public SoundFormatInfo FormatInfo => null;

        /// <inheritdoc />
        public event EventHandler<EventArgs> EndOfStreamReached;

        /// <inheritdoc />
        public event EventHandler<PositionChangedEventArgs> PositionChanged;

        /// <inheritdoc />
        public int ReadBytes(Span<float> buffer)
        {
            if (IsDisposed) return 0;

            var read = decoder.Decode(buffer);
            if (read <= 0)
            {
                var ended = EndOfStreamReached;
                if (ended != null)
                {
                    ended(this, EventArgs.Empty);
                }
                return 0;
            }

            position += read;

            var moved = PositionChanged;
            if (moved != null)
            {
                moved(this, new PositionChangedEventArgs(position));
            }

            return read;
        }

        /// <inheritdoc />
        /// <exception cref="NotSupportedException">Always: seeking belongs to the container, not to the packet feed.</exception>
        public void Seek(int offset) =>
            throw new NotSupportedException(
                "A packet feed cannot be seeked here; reposition the packet source and call PacketAudioPlayer.Seek.");

        /// <inheritdoc />
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true;
            decoder.Dispose();
        }
    }
}
