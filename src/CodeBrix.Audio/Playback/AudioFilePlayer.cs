using System;
using System.IO;
using System.Threading;
using CodeBrix.Audio.Engine.Components;
using CodeBrix.Audio.Engine.Providers;
using CodeBrix.Audio.Engine.Structs;
using CodeBrix.Audio.Wave;
using EnginePlaybackState = CodeBrix.Audio.Engine.Enums.PlaybackState;

namespace CodeBrix.Audio.Playback;

/// <summary>
/// A simple, long‑running player for a single WAV or MP3 file, with transport controls suitable for a
/// media‑player UI: play/pause/stop, volume, seek to a timecode, and a readable current position and
/// total duration. It hides the CodeBrix.Audio.Engine plumbing (a streaming decoder feeding an engine
/// sound player mixed into the shared output device) behind a small, UI‑friendly surface.
/// </summary>
/// <remarks>
/// <para>
/// Typical use for a transport / scrubber control: <c>Load</c> a file, read <see cref="Duration"/> to
/// size the timeline, <c>Play</c>, poll <see cref="Position"/> (for example each UI frame) to move the
/// playback marker, and call <see cref="Seek"/> when the user drags the marker. The file streams from
/// disk in chunks, so a multi‑minute track does not sit fully decoded in memory.
/// </para>
/// <para>
/// The file plays through the process‑wide <see cref="SharedAudioOutput"/> (the same device that
/// <see cref="WaveOutEvent"/> uses), so it mixes cleanly alongside any sound effects. Unlike the
/// sample‑effect path, the file is decoded through the engine, which resamples it to the output rate,
/// so a file at any sample rate plays correctly. <see cref="PlaybackEnded"/> is raised when the file
/// reaches its natural end (on the <see cref="SynchronizationContext"/> captured at load, if any).
/// </para>
/// <para>Not thread‑safe against itself for concurrent transport calls from many threads, but safe to
/// drive from a single UI thread while playback runs on the audio thread. Dispose when finished.</para>
/// </remarks>
public sealed class AudioFilePlayer : IDisposable
{
    private readonly object _lock = new object();

    private Stream _stream;
    private bool _ownsStream;
    private SoundPlayer _player;
    private SynchronizationContext _syncContext;
    private float _volume = 1.0f;
    private bool _isLooping;
    private bool _disposed;

    /// <summary>
    /// Raised when the file reaches its natural end (not raised for <see cref="Stop"/> or when looping).
    /// Raised on the <see cref="SynchronizationContext"/> captured when the file was loaded, if there is
    /// one (for example the UI thread); otherwise on a background thread.
    /// </summary>
    public event EventHandler PlaybackEnded;

    /// <summary>Whether a file is currently loaded and ready to play.</summary>
    public bool IsLoaded
    {
        get { lock (_lock) { return _player != null; } }
    }

    /// <summary>The current playback position as a timecode. <see cref="TimeSpan.Zero"/> if no file is loaded.</summary>
    public TimeSpan Position
    {
        get { lock (_lock) { return _player == null ? TimeSpan.Zero : TimeSpan.FromSeconds(_player.Time); } }
    }

    /// <summary>The total duration of the loaded file. <see cref="TimeSpan.Zero"/> if no file is loaded or unknown.</summary>
    public TimeSpan Duration
    {
        get { lock (_lock) { return _player == null ? TimeSpan.Zero : TimeSpan.FromSeconds(_player.Duration); } }
    }

    /// <summary>The current playback state (Stopped / Playing / Paused).</summary>
    public PlaybackState PlaybackState
    {
        get { lock (_lock) { return _player == null ? PlaybackState.Stopped : Map(_player.State); } }
    }

    /// <summary>Playback volume, where 1.0 is unity gain. Persists across loads.</summary>
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

    /// <summary>Whether the loaded file loops back to the start when it reaches the end. Persists across loads.</summary>
    public bool IsLooping
    {
        get { lock (_lock) { return _isLooping; } }
        set
        {
            lock (_lock)
            {
                _isLooping = value;
                if (_player != null)
                {
                    _player.IsLooping = value;
                }
            }
        }
    }

    /// <summary>
    /// Loads a WAV or MP3 file for playback, positioned at the start and stopped. Replaces any
    /// previously loaded file. <see cref="Duration"/> is available once this returns.
    /// </summary>
    /// <param name="filePath">Path to a .wav or .mp3 file.</param>
    /// <exception cref="ArgumentNullException"><paramref name="filePath"/> is null.</exception>
    public void Load(string filePath)
    {
        if (filePath == null)
        {
            throw new ArgumentNullException(nameof(filePath));
        }

        Stream stream = File.OpenRead(filePath);
        try
        {
            LoadStream(stream, ownsStream: true);
        }
        catch (Exception)
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Loads a WAV or MP3 file from a stream. The stream should be seekable so the player can report
    /// duration and seek. Replaces any previously loaded file.
    /// </summary>
    /// <param name="stream">A readable (ideally seekable) stream containing a WAV or MP3 file.</param>
    /// <param name="leaveOpen">When <see langword="false"/> (the default), the stream is disposed when a
    /// new file is loaded or this player is disposed; when <see langword="true"/>, the caller keeps ownership.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    public void Load(Stream stream, bool leaveOpen = false)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }
        LoadStream(stream, ownsStream: !leaveOpen);
    }

    /// <summary>Starts or resumes playback from the current position.</summary>
    /// <exception cref="InvalidOperationException">No file is loaded.</exception>
    public void Play()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            RequireLoaded();
            _player.Play();
        }
    }

    /// <summary>Pauses playback, keeping the current position.</summary>
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

    /// <summary>Stops playback and rewinds to the start.</summary>
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
    /// Seeks to a timecode. May be called while playing or stopped (for example when the user drags the
    /// scrubber, or to begin playback at a specific point via <c>Seek(...)</c> then <see cref="Play"/>).
    /// </summary>
    /// <param name="position">The position to seek to, from the start of the file.</param>
    /// <exception cref="InvalidOperationException">No file is loaded.</exception>
    public void Seek(TimeSpan position)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            RequireLoaded();
            _player.Seek(position, SeekOrigin.Begin);
        }
    }

    /// <summary>Stops playback and releases the loaded file and its stream (if owned).</summary>
    public void Dispose()
    {
        lock (_lock)
        {
            if (_disposed)
            {
                return;
            }
            _disposed = true;
            TearDownPlayer();
        }
    }

    private void LoadStream(Stream stream, bool ownsStream)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            TearDownPlayer();

            if (stream.CanSeek)
            {
                stream.Position = 0;
            }

            // Start the shared output at the file's native rate when possible (the engine resamples to
            // the running output rate otherwise, so any rate still plays correctly).
            int desiredRate = 48000;
            if (stream.CanSeek)
            {
                try
                {
                    AudioFormat? native = AudioFormat.GetFormatFromStream(stream);
                    if (native.HasValue && native.Value.SampleRate > 0)
                    {
                        desiredRate = native.Value.SampleRate;
                    }
                }
                catch (Exception)
                {
                    // Fall back to the default rate; the engine will resample.
                }
                finally
                {
                    try { stream.Position = 0; } catch (Exception) { /* non-seekable after all */ }
                }
            }

            var device = SharedAudioOutput.EnsureStarted(desiredRate);

            SoundPlayer player = null;
            ChunkedDataProvider provider = null;
            try
            {
                provider = new ChunkedDataProvider(device.Engine, device.Format, stream);
                player = new SoundPlayer(device.Engine, device.Format, provider)
                {
                    Volume = _volume,
                    IsLooping = _isLooping,
                };
                player.PlaybackEnded += OnEnginePlaybackEnded;
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
                throw;
            }

            _stream = stream;
            _ownsStream = ownsStream;
            _player = player;
            if (_syncContext == null)
            {
                _syncContext = SynchronizationContext.Current;
            }
        }
    }

    // Fires on the engine's real-time audio thread; marshal off it before raising the public event.
    private void OnEnginePlaybackEnded(object sender, EventArgs e)
    {
        var handler = PlaybackEnded;
        if (handler == null)
        {
            return;
        }

        var context = _syncContext;
        if (context != null)
        {
            context.Post(_ => handler(this, EventArgs.Empty), null);
        }
        else
        {
            handler(this, EventArgs.Empty);
        }
    }

    // Removes and disposes the current player + stream. Callers hold _lock.
    private void TearDownPlayer()
    {
        if (_player != null)
        {
            _player.PlaybackEnded -= OnEnginePlaybackEnded;
            try { SharedAudioOutput.RemoveComponentFromMixer(_player); } catch (Exception) { /* output may be torn down */ }
            try { _player.Dispose(); } catch (Exception) { /* also disposes the data provider */ }
            _player = null;
        }
        if (_stream != null)
        {
            if (_ownsStream)
            {
                try { _stream.Dispose(); } catch (Exception) { /* best effort */ }
            }
            _stream = null;
            _ownsStream = false;
        }
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

    private void RequireLoaded()
    {
        if (_player == null)
        {
            throw new InvalidOperationException("Load a file before controlling playback.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(AudioFilePlayer));
        }
    }
}
