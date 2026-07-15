using System;
using System.Threading;

namespace CodeBrix.Audio.Wave;

/// <summary>
/// Plays audio to the default output device, implementing the familiar NAudio-style
/// <see cref="IWavePlayer"/> contract: <c>Init</c> a source, then <c>Play</c>/<c>Pause</c>/<c>Stop</c>.
/// Unlike NAudio's Windows-only <c>WaveOutEvent</c>, this is cross-platform (Windows, macOS, Linux)
/// and every instance is a <em>voice</em> mixed into one shared output device rather than a separate
/// hardware device of its own — see <see cref="SharedAudioOutput"/>.
/// </summary>
/// <remarks>
/// <para>
/// Simple use is unchanged from NAudio: <c>var p = new WaveOutEvent(); p.Init(reader); p.Play();</c>.
/// The shared device starts on the first play and adopts that sound's sample rate. Because many
/// instances share one device, overlapping dozens of short sounds is cheap mixing, not dozens of
/// device opens.
/// </para>
/// <para>
/// There is no built-in resampler, so a source must be at the shared output's sample rate. When the
/// output is not yet running it adopts the first source's rate, so a lone sound always plays; when it
/// is already running (or was pinned with <see cref="SharedAudioOutput.Configure"/>) a source at a
/// different rate is rejected by <see cref="Init"/> rather than played at the wrong pitch. Mono and
/// stereo sources are matched to the output automatically. Applications that overlap many sounds
/// should pin the rate once at start-up with <see cref="SharedAudioOutput.Configure"/>.
/// </para>
/// <para>
/// <see cref="PlaybackStopped"/> is raised (on the <see cref="SynchronizationContext"/> captured when
/// <see cref="Play"/> was called, if any) when the source reaches its end or when <see cref="Stop"/>
/// is called. To replay a source, reposition it to the start and call <see cref="Play"/> again.
/// </para>
/// </remarks>
public sealed class WaveOutEvent : IWavePlayer
{
    private readonly object _lock = new object();

    private ISampleProvider _matchedSource;
    private WaveFormat _outputWaveFormat;
    private SampleSourceVoice _voice;
    private float _volume = 1.0f;
    private PlaybackState _playbackState = PlaybackState.Stopped;
    private SynchronizationContext _syncContext;
    private bool _stoppedRaised;
    private bool _disposed;

    /// <inheritdoc />
    public event EventHandler<StoppedEventArgs> PlaybackStopped;

    /// <inheritdoc />
    public PlaybackState PlaybackState
    {
        get { lock (_lock) { return _playbackState; } }
    }

    /// <inheritdoc />
    public WaveFormat OutputWaveFormat
    {
        get { lock (_lock) { return _outputWaveFormat; } }
    }

    /// <inheritdoc />
    public float Volume
    {
        get { lock (_lock) { return _volume; } }
        set
        {
            lock (_lock)
            {
                _volume = value;
                if (_voice != null)
                {
                    _voice.Volume = value;
                }
            }
        }
    }

    /// <inheritdoc />
    public void Init(IWaveProvider waveProvider)
    {
        if (waveProvider == null)
        {
            throw new ArgumentNullException(nameof(waveProvider));
        }

        lock (_lock)
        {
            ThrowIfDisposed();
            if (_playbackState != PlaybackState.Stopped)
            {
                throw new InvalidOperationException(
                    "Cannot re-initialise a WaveOutEvent while it is playing or paused; call Stop() first.");
            }

            var sampleSource = waveProvider.ToSampleProvider();
            int outputRate = SharedAudioOutput.EffectiveSampleRate(sampleSource.WaveFormat.SampleRate);
            int outputChannels = SharedAudioOutput.EffectiveChannels();

            if (sampleSource.WaveFormat.SampleRate != outputRate)
            {
                throw new InvalidOperationException(
                    $"This source is {sampleSource.WaveFormat.SampleRate} Hz but the shared audio output runs at "
                    + $"{outputRate} Hz, and CodeBrix.Audio has no resampler. Convert the source to {outputRate} Hz "
                    + "first, or call SharedAudioOutput.Configure(...) / play this sound first so the output adopts its rate.");
            }

            _matchedSource = MatchChannels(sampleSource, outputChannels);
            _outputWaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(outputRate, outputChannels);
            _stoppedRaised = false;
            DiscardVoice();
        }
    }

    /// <inheritdoc />
    public void Play()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            if (_matchedSource == null)
            {
                throw new InvalidOperationException("Call Init(...) before Play().");
            }

            if (_playbackState == PlaybackState.Playing)
            {
                return;
            }

            if (_playbackState == PlaybackState.Paused && _voice != null && !_voice.HasEnded)
            {
                _voice.ResumePlaying();
                _playbackState = PlaybackState.Playing;
                return;
            }

            var device = SharedAudioOutput.EnsureStarted(_outputWaveFormat.SampleRate);
            if (_matchedSource.WaveFormat.SampleRate != device.Format.SampleRate
                || _outputWaveFormat.Channels != device.Format.Channels)
            {
                throw new InvalidOperationException(
                    $"This source is {_matchedSource.WaveFormat.SampleRate} Hz / {_outputWaveFormat.Channels}ch but the shared "
                    + $"audio output started at {device.Format.SampleRate} Hz / {device.Format.Channels}ch. CodeBrix.Audio has no "
                    + "resampler; reconfigure the output (SharedAudioOutput.Shutdown then Configure) or convert the source.");
            }

            // Build a fresh voice for this play (covers first play and replay after stop/end).
            DiscardVoice();
            _voice = new SampleSourceVoice(device.Engine, device.Format, _matchedSource) { Volume = _volume };
            _stoppedRaised = false;
            if (_syncContext == null)
            {
                _syncContext = SynchronizationContext.Current;
            }

            _voice.ResumePlaying();
            SharedAudioOutput.AddPlayer(this, _voice);
            _playbackState = PlaybackState.Playing;
        }
    }

    /// <inheritdoc />
    public void Pause()
    {
        lock (_lock)
        {
            if (_disposed || _playbackState != PlaybackState.Playing)
            {
                return;
            }
            if (_voice != null)
            {
                _voice.PausePlaying();
            }
            _playbackState = PlaybackState.Paused;
        }
    }

    /// <inheritdoc />
    public void Stop()
    {
        bool raise = false;
        lock (_lock)
        {
            if (_disposed || _playbackState == PlaybackState.Stopped)
            {
                return;
            }
            if (_voice != null)
            {
                _voice.PausePlaying();
                SharedAudioOutput.RemovePlayer(this, _voice);
            }
            _playbackState = PlaybackState.Stopped;
            raise = true;
        }

        if (raise)
        {
            RaiseStopped(null);
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        SampleSourceVoice voice;
        bool wasActive;
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            voice = _voice;
            wasActive = _playbackState != PlaybackState.Stopped;
            _voice = null;
            _matchedSource = null;
            _playbackState = PlaybackState.Stopped;
        }

        if (voice != null)
        {
            SharedAudioOutput.RemovePlayer(this, voice);
            SafeDisposeVoice(voice);
        }
        if (wasActive)
        {
            RaiseStopped(null);
        }
    }

    // Called by the shared output's sweep thread (never the real-time audio thread) to reclaim a
    // voice whose source has ended and raise PlaybackStopped for it.
    internal void PollEndOfStream()
    {
        bool raise = false;
        lock (_lock)
        {
            if (_disposed || _playbackState != PlaybackState.Playing)
            {
                return;
            }
            if (_voice == null || !_voice.HasEnded)
            {
                return;
            }
            SharedAudioOutput.RemovePlayer(this, _voice);
            _playbackState = PlaybackState.Stopped;
            raise = true;
        }

        if (raise)
        {
            RaiseStopped(null);
        }
    }

    private static ISampleProvider MatchChannels(ISampleProvider source, int outputChannels)
    {
        int sourceChannels = source.WaveFormat.Channels;
        if (sourceChannels == outputChannels)
        {
            return source;
        }
        if (outputChannels == 2 && sourceChannels == 1)
        {
            return source.ToStereo();
        }
        if (outputChannels == 1 && sourceChannels == 2)
        {
            return source.ToMono();
        }
        throw new InvalidOperationException(
            $"The shared audio output is {outputChannels}-channel, but this source has {sourceChannels} channels. "
            + "Only mono<->stereo conversion is automatic; convert the source to the output channel count first.");
    }

    private void RaiseStopped(Exception exception)
    {
        lock (_lock)
        {
            if (_stoppedRaised)
            {
                return;
            }
            _stoppedRaised = true;
        }

        var handler = PlaybackStopped;
        if (handler == null)
        {
            return;
        }

        var args = new StoppedEventArgs(exception);
        var context = _syncContext;
        if (context != null)
        {
            context.Post(_ => handler(this, args), null);
        }
        else
        {
            handler(this, args);
        }
    }

    // Removes and disposes the current voice, if any. Callers hold _lock.
    private void DiscardVoice()
    {
        if (_voice != null)
        {
            SharedAudioOutput.RemovePlayer(this, _voice);
            SafeDisposeVoice(_voice);
            _voice = null;
        }
    }

    private static void SafeDisposeVoice(SampleSourceVoice voice)
    {
        try
        {
            voice.Dispose();
        }
        catch (Exception)
        {
            // The mixer/engine may already be torn down (e.g. after SharedAudioOutput.Shutdown()).
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(WaveOutEvent));
        }
    }
}
