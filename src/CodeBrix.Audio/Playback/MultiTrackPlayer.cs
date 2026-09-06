using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Engine.Components;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Playback.Internal;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Wave;
using EnginePlaybackState = CodeBrix.Audio.Engine.Enums.PlaybackState;

namespace CodeBrix.Audio.Playback;

/// <summary>
/// Plays a song made of separate tracks - the stems of a mix, the parts of an arrangement - on one
/// transport, with every track rendered in lockstep and mixed sample-accurately.
/// </summary>
/// <remarks>
/// <para>
/// Each track is either a recording (<see cref="AudioTrack"/>) or a MIDI performance
/// (<see cref="MidiTrack"/>) played through a synthesizer the consumer chooses, and a track can
/// hold BOTH: give a stem its WAV and its MIDI file, and <see cref="PlayerTrack.ActiveSource"/>
/// chooses which one is heard, switching between them mid-song as a short crossfade. Per track
/// there is level, mute, solo, pan and a signed timing offset; over the whole song there is one
/// position, one duration, one play/pause/stop.
/// </para>
/// <para>
/// The same mix serves the speakers and the disk. <see cref="Play"/> renders it through the shared
/// audio output; <see cref="Render(int, TimeSpan?)"/> and <see cref="RenderToWav(string, int, TimeSpan?)"/>
/// render it faster than real time with no device open at all, and produce the same audio.
/// <see cref="ExportMergedMidi(string)"/> writes the MIDI tracks back out as one General MIDI file.
/// </para>
/// <para>
/// LOCKSTEP IS THE POINT. Every track is pulled for exactly the same frames on every block, from
/// one position, inside one engine component. Tracks cannot drift apart, and a seek moves all of
/// them at once. That is not something a player per track can promise.
/// </para>
/// <para>
/// Transport calls are safe from one thread while playback runs on the audio thread; they are
/// serialized internally. Dispose when finished - it stops playback and releases every decoder and
/// synthesizer the tracks opened.
/// </para>
/// </remarks>
public sealed partial class MultiTrackPlayer : IDisposable
{
    /// <summary>The sample rate <see cref="Render(int, TimeSpan?)"/> uses when none is given.</summary>
    public const int DefaultRenderSampleRate = 44100;

    /// <summary>
    /// The rate <see cref="MeasureRelativeTrackLevels()"/> renders at. Loudness does not depend on
    /// the sample rate, so the measurement runs at a low one and costs about half of what a
    /// full-rate render would.
    /// </summary>
    public const int LevelMeasurementSampleRate = 22050;

    /// <summary>How long the mix keeps rendering past the end of the song, by default.</summary>
    /// <remarks>
    /// Release tails and reverb do not stop when the last note is written; without this the last
    /// chord of a synthesized track is chopped off. It does not change <see cref="Duration"/>.
    /// </remarks>
    public static readonly TimeSpan DefaultTail = TimeSpan.FromMilliseconds(400);

    private readonly object gate = new object();
    private readonly List<PlayerTrack> tracks = new List<PlayerTrack>();
    private readonly TempoSource tempoSource = new TempoSource();

    private MultiTrackDataProvider provider;
    private SoundPlayer player;
    private SynchronizationContext syncContext;
    private Task levelMeasurement = Task.CompletedTask;
    private bool autoMeasurementStarted;
    private float volume = 1.0f;
    private bool isLooping;
    private TimeSpan tail = DefaultTail;
    private bool disposed;

    /// <summary>
    /// Raised when a non-looping song reaches its end (including its tail). Not raised for
    /// <see cref="Stop"/>, and not raised while <see cref="IsLooping"/> is set. Raised on the
    /// <see cref="SynchronizationContext"/> captured when the player was first prepared, if there is
    /// one; otherwise on a background thread.
    /// </summary>
    public event EventHandler PlaybackEnded;

    /// <summary>
    /// The tracks of the song, in the order they were added. Use <see cref="this[string]"/>,
    /// <see cref="FindTrack(string)"/> or <see cref="TryGetTrack(string, out PlayerTrack)"/> to
    /// reach one by name.
    /// </summary>
    public IReadOnlyList<PlayerTrack> Tracks
    {
        get { lock (gate) { return tracks.ToArray(); } }
    }

    /// <summary>
    /// The song's live musical clock - tempo and beat position - fed from the tempo map of the
    /// first track that has a MIDI source.
    /// </summary>
    /// <remarks>
    /// A song with no MIDI in it reports the MIDI default of 120 BPM, because nothing in a set of
    /// recordings says otherwise. The values are updated on the audio thread as the mix renders and
    /// are safe to read from anywhere.
    /// </remarks>
    public TempoSource TempoSource => tempoSource;

    /// <summary>
    /// Whether each track's MIDI rendition is level-matched to that track's own recording, so the
    /// relative levels of the song survive switching a track from its audio to its MIDI. Defaults
    /// to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// <para>
    /// When set before <see cref="Prepare"/> or <see cref="Play"/>, the measurement runs once on a
    /// worker thread: each track that holds both sources is rendered twice, and
    /// <see cref="PlayerTrack.MidiSourceGain"/> is set to the ratio of the two RMS levels. Await
    /// <see cref="LevelMeasurement"/> if you need the result to be in before you read a gain.
    /// </para>
    /// <para>
    /// It writes <see cref="PlayerTrack.MidiSourceGain"/> and nothing else. With it off, NO gain the
    /// consumer did not set is ever touched.
    /// </para>
    /// </remarks>
    public bool AutoSetRelativeTrackLevels { get; set; }

    /// <summary>
    /// The most recent level measurement, as a task that completes when the gains have been
    /// written. NEVER <see langword="null"/>: it is an already-completed task until a measurement
    /// starts, so <c>await player.LevelMeasurement;</c> is safe at any moment and returns
    /// immediately when there is nothing to wait for.
    /// </summary>
    /// <remarks>
    /// A measurement is started by <see cref="Prepare"/> when
    /// <see cref="AutoSetRelativeTrackLevels"/> is on, and by
    /// <see cref="MeasureRelativeTrackLevelsAsync"/>. The synchronous
    /// <see cref="MeasureRelativeTrackLevels()"/> has finished by the time it returns, so it leaves
    /// a completed task here as well.
    /// </remarks>
    public Task LevelMeasurement
    {
        get { lock (gate) { return levelMeasurement; } }
    }

    /// <summary>
    /// How long the mix keeps rendering past the end of the song, so release tails ring out instead
    /// of being cut. Defaults to <see cref="DefaultTail"/>; set it to <see cref="TimeSpan.Zero"/> to
    /// stop exactly at <see cref="Duration"/>. Read when the player is prepared.
    /// </summary>
    /// <remarks>
    /// It does not change <see cref="Duration"/>, which is the song's own length. It does mean
    /// <see cref="Position"/> can run slightly past it.
    /// </remarks>
    public TimeSpan Tail
    {
        get { lock (gate) { return tail; } }
        set { lock (gate) { tail = value > TimeSpan.Zero ? value : TimeSpan.Zero; } }
    }

    /// <summary>
    /// The length of the song: the last moment any track reaches, its
    /// <see cref="PlayerTrack.Offset"/> included. Zero when there are no tracks.
    /// </summary>
    /// <remarks>
    /// A track's contribution is the longer of its two sources, so switching a track's source does
    /// not change the song's length.
    /// </remarks>
    public TimeSpan Duration
    {
        get
        {
            lock (gate)
            {
                if (provider != null)
                {
                    return TimeSpan.FromSeconds((double)provider.LengthFrames / provider.SampleRate);
                }

                var longest = TimeSpan.Zero;
                foreach (var track in tracks)
                {
                    var end = track.Offset + track.Duration;
                    if (end > longest)
                    {
                        longest = end;
                    }
                }

                return longest;
            }
        }
    }

    /// <summary>The current position in the song. <see cref="TimeSpan.Zero"/> before preparing.</summary>
    public TimeSpan Position
    {
        get
        {
            lock (gate)
            {
                return provider == null
                    ? TimeSpan.Zero
                    : TimeSpan.FromSeconds((double)provider.PositionFrames / provider.SampleRate);
            }
        }
    }

    /// <summary>The current playback state (Stopped / Playing / Paused).</summary>
    public PlaybackState PlaybackState
    {
        get { lock (gate) { return player == null ? PlaybackState.Stopped : Map(player.State); } }
    }

    /// <summary>Whether the audio device is open and the mix is built.</summary>
    public bool IsPrepared
    {
        get { lock (gate) { return player != null; } }
    }

    /// <summary>The whole song's output volume, where 1.0 is unity gain. Persists across preparations.</summary>
    public float Volume
    {
        get { lock (gate) { return volume; } }
        set
        {
            lock (gate)
            {
                volume = value < 0.0f ? 0.0f : value;
                if (player != null)
                {
                    player.Volume = volume;
                }
            }
        }
    }

    /// <summary>
    /// Whether the song repeats from the start when it reaches the end. Persists across
    /// preparations.
    /// </summary>
    public bool IsLooping
    {
        get { lock (gate) { return isLooping; } }
        set
        {
            lock (gate)
            {
                isLooping = value;
                if (provider != null)
                {
                    provider.IsLooping = value;
                }
            }
        }
    }

    /// <summary>Adds a track to the song.</summary>
    /// <typeparam name="T">The track type, so the call reads as a fluent one.</typeparam>
    /// <param name="track">The track to add.</param>
    /// <returns>The same track, for chaining.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="track"/> is null.</exception>
    /// <exception cref="InvalidOperationException">The song is playing or paused; stop it first.</exception>
    /// <remarks>
    /// Adding or removing a track discards anything already prepared, so the next <see cref="Play"/>
    /// rebuilds the mix. Everything ELSE about a track - its gain, mute, solo, pan, active source -
    /// can be changed while the music runs.
    /// </remarks>
    public T Add<T>(T track) where T : PlayerTrack
    {
        if (track == null)
        {
            throw new ArgumentNullException(nameof(track));
        }

        lock (gate)
        {
            ThrowIfDisposed();
            RequireStopped("add a track");
            tracks.Add(track);
            TearDown();
        }

        return track;
    }

    /// <summary>Removes a track from the song.</summary>
    /// <param name="track">The track to remove.</param>
    /// <returns><see langword="true"/> when the track was in the song.</returns>
    /// <exception cref="InvalidOperationException">The song is playing or paused; stop it first.</exception>
    public bool Remove(PlayerTrack track)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            RequireStopped("remove a track");
            var removed = tracks.Remove(track);
            if (removed)
            {
                TearDown();
            }

            return removed;
        }
    }

    /// <summary>Removes every track.</summary>
    /// <exception cref="InvalidOperationException">The song is playing or paused; stop it first.</exception>
    public void ClearTracks()
    {
        lock (gate)
        {
            ThrowIfDisposed();
            RequireStopped("clear the tracks");
            tracks.Clear();
            TearDown();
        }
    }

    /// <summary>
    /// The track of a given name. Names are matched case-insensitively and with surrounding
    /// whitespace ignored, the same way <c>SunoSong</c> matches a stem name.
    /// </summary>
    /// <param name="name">The track name, for example "Drums".</param>
    /// <returns>The track. When two tracks share a name the FIRST one added is returned.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    /// <exception cref="KeyNotFoundException">
    /// The song has no track of that name. The message lists the names it does have.
    /// </exception>
    /// <remarks>
    /// This throws where <c>SunoSong</c>'s indexer returns null, because a track name that is not
    /// in the song is a mistake in the calling code and a message naming the tracks that ARE there
    /// says so immediately. Use <see cref="FindTrack(string)"/> or
    /// <see cref="TryGetTrack(string, out PlayerTrack)"/> when the name might legitimately be
    /// absent.
    /// </remarks>
    public PlayerTrack this[string name]
    {
        get
        {
            if (name == null)
            {
                throw new ArgumentNullException(nameof(name));
            }

            var found = FindTrack(name);
            if (found == null)
            {
                throw new KeyNotFoundException(
                    $"This song has no track named '{name}'. {DescribeTrackNames()}");
            }

            return found;
        }
    }

    /// <summary>
    /// Finds a track by name, case-insensitively, or returns <see langword="null"/> when the song
    /// has no such track.
    /// </summary>
    /// <param name="name">The track name, for example "Backing Vocals". Null returns null.</param>
    /// <returns>The track, or null. When two tracks share a name the FIRST one added is returned.</returns>
    public PlayerTrack FindTrack(string name)
    {
        if (name == null)
        {
            return null;
        }

        var wanted = name.Trim();
        lock (gate)
        {
            foreach (var track in tracks)
            {
                if (string.Equals(track.Name, wanted, StringComparison.OrdinalIgnoreCase))
                {
                    return track;
                }
            }
        }

        return null;
    }

    /// <summary>Tries to find a track by name, case-insensitively.</summary>
    /// <param name="name">The track name, for example "Drums". Null returns false.</param>
    /// <param name="track">
    /// Receives the track, or null when there is none. When two tracks share a name the FIRST one
    /// added is returned.
    /// </param>
    /// <returns><see langword="true"/> when the song has a track of that name.</returns>
    public bool TryGetTrack(string name, out PlayerTrack track)
    {
        track = FindTrack(name);
        return track != null;
    }

    /// <summary>
    /// Opens the audio device, builds every track's decoder and synthesizer at the device's rate,
    /// and leaves the song at its start, stopped.
    /// </summary>
    /// <remarks>
    /// <see cref="Play"/> does this for you. Call it directly when you want the cost paid up front -
    /// building a SoundFont synthesizer per track is not instant - or when
    /// <see cref="AutoSetRelativeTrackLevels"/> is on and you would rather the measurement started
    /// now. Calling it again when already prepared does nothing.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The player has been disposed.</exception>
    /// <exception cref="InvalidOperationException">There are no tracks, or a synthesizer factory returned null.</exception>
    public void Prepare()
    {
        lock (gate)
        {
            ThrowIfDisposed();

            if (player != null)
            {
                return;
            }

            if (tracks.Count == 0)
            {
                throw new InvalidOperationException(
                    "Add at least one track before preparing or playing a MultiTrackPlayer.");
            }

            // Start the shared device first, then build everything at whatever rate it settled on.
            // Matching the device rate exactly is what keeps the synthesizers resampler-free.
            var device = SharedAudioOutput.EnsureStarted(48000);
            var rate = SharedAudioOutput.SampleRate;

            var snapshot = tracks.ToArray();

            MultiTrackMix mix = null;
            MultiTrackDataProvider newProvider = null;
            SoundPlayer newPlayer = null;
            try
            {
                mix = new MultiTrackMix(snapshot, rate, tempoSource);
                var tailFrames = (long)(tail.TotalSeconds * rate);
                newProvider = new MultiTrackDataProvider(mix, tailFrames, isLooping);

                newPlayer = new SoundPlayer(device.Engine, device.Format, newProvider)
                {
                    Volume = volume,

                    // Looping is the mix's job: the engine would loop the raw stream, which is the
                    // song plus its tail, and would not move the tracks together.
                    IsLooping = false,
                };
                newPlayer.PlaybackEnded += OnEnginePlaybackEnded;
                SharedAudioOutput.AddComponentToMixer(newPlayer);
            }
            catch (Exception)
            {
                if (newPlayer != null)
                {
                    try { newPlayer.Dispose(); } catch (Exception) { /* best effort */ }
                }
                else if (newProvider != null)
                {
                    try { newProvider.Dispose(); } catch (Exception) { /* also disposes the mix */ }
                }
                else
                {
                    try { mix?.Dispose(); } catch (Exception) { /* best effort */ }
                }

                throw;
            }

            provider = newProvider;
            player = newPlayer;
            syncContext ??= SynchronizationContext.Current;

            StartLevelMeasurement(snapshot);
        }
    }

    /// <summary>Starts or resumes playback from the current position, preparing the song if needed.</summary>
    /// <exception cref="ObjectDisposedException">The player has been disposed.</exception>
    /// <exception cref="InvalidOperationException">There are no tracks.</exception>
    public void Play()
    {
        lock (gate)
        {
            ThrowIfDisposed();
            Prepare();
            provider.SetTransportRunning(true);
            player.Play();
        }
    }

    /// <summary>Pauses playback, keeping the current position.</summary>
    public void Pause()
    {
        lock (gate)
        {
            if (disposed || player == null)
            {
                return;
            }

            player.Pause();
            provider.SetTransportRunning(false);
        }
    }

    /// <summary>Stops playback and rewinds every track to the start of the song.</summary>
    public void Stop()
    {
        lock (gate)
        {
            if (disposed || player == null)
            {
                return;
            }

            player.Stop();
            provider.SeekFrames(0);
            provider.SetTransportRunning(false);
        }
    }

    /// <summary>
    /// Seeks the whole song to a timecode, taking every track with it. May be called while playing
    /// or stopped.
    /// </summary>
    /// <param name="position">The position to seek to, from the start of the song.</param>
    /// <remarks>
    /// Each track's <see cref="PlayerTrack.Offset"/> is re-read here, so an offset changed while the
    /// music was running takes effect now. Notes already sounding in a MIDI track do not resume -
    /// see <see cref="MidiSequencer.Seek"/> for why.
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The player has been disposed.</exception>
    /// <exception cref="InvalidOperationException">The song has not been prepared.</exception>
    public void Seek(TimeSpan position)
    {
        lock (gate)
        {
            ThrowIfDisposed();
            if (provider == null)
            {
                throw new InvalidOperationException(
                    "Prepare or play the song before seeking it.");
            }

            var frames = position <= TimeSpan.Zero
                ? 0L
                : (long)(position.TotalSeconds * provider.SampleRate);

            provider.SeekFrames(frames);
        }
    }

    /// <summary>
    /// Renders the whole song to interleaved stereo float samples, faster than real time and
    /// without opening an audio device.
    /// </summary>
    /// <param name="sampleRate">The rate to render at. Defaults to <see cref="DefaultRenderSampleRate"/>.</param>
    /// <param name="tailLength">
    /// How long to keep rendering past the end of the song. Defaults to <see cref="Tail"/>.
    /// </param>
    /// <returns>Interleaved stereo samples: left, right, left, right, ...</returns>
    /// <exception cref="ObjectDisposedException">The player has been disposed.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive.</exception>
    /// <remarks>
    /// This does NOT touch anything the live transport has prepared: it builds its own decoders and
    /// synthesizers at the requested rate, renders, and releases them. Rendering while the same song
    /// is playing is therefore legitimate - though a track built from a Stream pays for it in
    /// memory, which is why file paths are the better source for anything long.
    /// <para>
    /// Every per-track control and <see cref="Volume"/> apply, so what comes out is what the
    /// speakers would have produced. Nothing is limited or normalised: a mix that adds up past 1.0
    /// comes back past 1.0, and turning it down is what <see cref="Volume"/> is for.
    /// </para>
    /// </remarks>
    public float[] Render(int sampleRate = DefaultRenderSampleRate, TimeSpan? tailLength = null)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "The sample rate must be positive.");
        }

        PlayerTrack[] snapshot;
        TimeSpan effectiveTail;
        float songVolume;
        lock (gate)
        {
            ThrowIfDisposed();
            snapshot = tracks.ToArray();
            effectiveTail = tailLength ?? tail;
            songVolume = volume;
        }

        if (effectiveTail < TimeSpan.Zero)
        {
            effectiveTail = TimeSpan.Zero;
        }

        if (snapshot.Length == 0)
        {
            return [];
        }

        using (var mix = new MultiTrackMix(snapshot, sampleRate, null))
        {
            var frames = mix.LengthFrames + (long)(effectiveTail.TotalSeconds * sampleRate);
            if (frames <= 0)
            {
                return [];
            }

            var samples = new float[checked(frames * 2)];
            mix.Render(samples);

            // The live path applies the song volume inside the output component; an offline render
            // has no output component, so it applies it here. Without this the same song came out
            // at a different level depending on where it was going.
            if (songVolume != 1.0f)
            {
                for (var i = 0; i < samples.Length; i++)
                {
                    samples[i] *= songVolume;
                }
            }

            return samples;
        }
    }

    /// <summary>Renders the whole song to a 32-bit float stereo WAV file.</summary>
    /// <param name="path">The file to write. Overwritten if it exists.</param>
    /// <param name="sampleRate">The rate to render at.</param>
    /// <param name="tailLength">How long to keep rendering past the end of the song. Defaults to <see cref="Tail"/>.</param>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public void RenderToWav(string path, int sampleRate = DefaultRenderSampleRate, TimeSpan? tailLength = null)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        var samples = Render(sampleRate, tailLength);
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);
        using (var writer = new WaveFileWriter(path, format))
        {
            writer.WriteSamples(samples, 0, samples.Length);
        }
    }

    /// <summary>Renders the whole song to a 32-bit float stereo WAV in a stream.</summary>
    /// <param name="output">The stream to write to. Must be writable and seekable.</param>
    /// <param name="sampleRate">The rate to render at.</param>
    /// <param name="tailLength">How long to keep rendering past the end of the song. Defaults to <see cref="Tail"/>.</param>
    /// <param name="leaveOpen">When true, the stream is left open once writing finishes.</param>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is null.</exception>
    public void RenderToWav(Stream output, int sampleRate = DefaultRenderSampleRate, TimeSpan? tailLength = null,
        bool leaveOpen = false)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        var samples = Render(sampleRate, tailLength);
        var format = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2);

        // WaveFileWriter always disposes the stream it is handed; honour leaveOpen without changing
        // that for everyone else.
        var target = leaveOpen ? new LeaveOpenStream(output) : output;
        using (var writer = new WaveFileWriter(target, format))
        {
            writer.WriteSamples(samples, 0, samples.Length);
        }
    }

    /// <summary>
    /// Measures each track's recording against its own MIDI rendition and sets
    /// <see cref="PlayerTrack.MidiSourceGain"/> so the two are the same loudness.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This BLOCKS the calling thread until the measurement is done - it is what
    /// <see cref="AutoSetRelativeTrackLevels"/> runs on a worker thread, run here on yours. Call it
    /// when you want it done before the next line, or want to redo it after changing an instrument;
    /// call <see cref="MeasureRelativeTrackLevelsAsync"/> instead when you would rather wait for it
    /// without holding a thread. Only tracks holding BOTH a recording and a MIDI performance are
    /// touched - there is nothing to match against otherwise.
    /// </para>
    /// <para>
    /// The measure is RMS over the whole track, at <see cref="LevelMeasurementSampleRate"/>. It
    /// decodes and synthesizes the whole song to do it, so it is seconds of work, not milliseconds.
    /// A track whose recording or rendition is effectively silent is left alone.
    /// </para>
    /// <para>
    /// It has finished by the time it returns, so <see cref="LevelMeasurement"/> is a completed
    /// task afterwards and the two members read as the pair they look like.
    /// </para>
    /// </remarks>
    /// <exception cref="ObjectDisposedException">The player has been disposed.</exception>
    public void MeasureRelativeTrackLevels()
    {
        PlayerTrack[] snapshot;
        lock (gate)
        {
            ThrowIfDisposed();
            snapshot = tracks.ToArray();
        }

        MeasureLevels(snapshot);

        lock (gate)
        {
            // Leave a completed task behind, so this method and LevelMeasurement read as the pair
            // they look like. A measurement still running on a worker is NOT masked: it is the more
            // recent one and it completes on its own.
            if (levelMeasurement.IsCompleted)
            {
                levelMeasurement = Task.CompletedTask;
            }
        }
    }

    /// <summary>
    /// Starts <see cref="MeasureRelativeTrackLevels()"/> on a worker thread and returns the task it
    /// runs on, which is also what <see cref="LevelMeasurement"/> reports until another measurement
    /// starts.
    /// </summary>
    /// <returns>The running measurement. Await it to know the gains have been written.</returns>
    /// <exception cref="ObjectDisposedException">The player has been disposed.</exception>
    /// <remarks>
    /// The tracks are snapshotted before the worker starts, so adding or removing a track
    /// afterwards does not change what this measurement covers. Calling it again while one is still
    /// running starts a second one rather than joining the first; the last one started is the one
    /// <see cref="LevelMeasurement"/> reports.
    /// </remarks>
    public Task MeasureRelativeTrackLevelsAsync()
    {
        lock (gate)
        {
            ThrowIfDisposed();
            var snapshot = tracks.ToArray();
            levelMeasurement = Task.Run(() => MeasureLevels(snapshot));
            return levelMeasurement;
        }
    }

    /// <summary>
    /// Builds the merged General MIDI event collection: every MIDI track on its own channel and its
    /// own SMF track, percussion on channel 10, with the shared tempo map and each track's offset
    /// already applied.
    /// </summary>
    /// <param name="problems">
    /// Receives one human-readable line per thing that could not be honoured - a song with more
    /// melodic tracks than General MIDI has channels, for instance. Never thrown.
    /// </param>
    /// <returns>A type 1 collection, sorted and terminated, ready for <see cref="MidiFile.Export(string, MidiEventCollection)"/>.</returns>
    public MidiEventCollection BuildMergedMidi(out IReadOnlyList<string> problems)
    {
        PlayerTrack[] snapshot;
        lock (gate)
        {
            ThrowIfDisposed();
            snapshot = tracks.ToArray();
        }

        var found = new List<string>();
        var events = MergedMidiBuilder.Build(snapshot, found);
        problems = found;
        return events;
    }

    /// <summary>Writes the merged General MIDI file to disk.</summary>
    /// <param name="path">The file to write. Overwritten if it exists.</param>
    /// <returns>One line per thing that could not be honoured; empty when everything was.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    public IReadOnlyList<string> ExportMergedMidi(string path)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        var events = BuildMergedMidi(out var problems);
        MidiFile.Export(path, events);
        return problems;
    }

    /// <summary>Writes the merged General MIDI file to a stream.</summary>
    /// <param name="output">The stream to write to. Must be writable and seekable.</param>
    /// <param name="leaveOpen">When true, the stream is left open once writing finishes.</param>
    /// <returns>One line per thing that could not be honoured; empty when everything was.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is null.</exception>
    public IReadOnlyList<string> ExportMergedMidi(Stream output, bool leaveOpen = false)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        var events = BuildMergedMidi(out var problems);
        MidiFile.Export(output, events, leaveOpen);
        return problems;
    }

    /// <summary>Stops playback and releases every decoder and synthesizer the song opened.</summary>
    public void Dispose()
    {
        lock (gate)
        {
            if (disposed)
            {
                return;
            }

            disposed = true;
            TearDown();
            tracks.Clear();
        }
    }

    // Fires on the engine's real-time audio thread; marshal off it before raising the public event.
    private void OnEnginePlaybackEnded(object sender, EventArgs e)
    {
        tempoSource.IsPlaying = false;

        var handler = PlaybackEnded;
        if (handler == null)
        {
            return;
        }

        var context = syncContext;
        if (context != null)
        {
            context.Post(_ => handler(this, EventArgs.Empty), null);
        }
        else
        {
            handler(this, EventArgs.Empty);
        }
    }

    // Removes and disposes the prepared chain. Callers hold the gate.
    private void TearDown()
    {
        if (player != null)
        {
            player.PlaybackEnded -= OnEnginePlaybackEnded;
            try { SharedAudioOutput.RemoveComponentFromMixer(player); } catch (Exception) { /* output may be torn down */ }
            try { player.Dispose(); } catch (Exception) { /* also disposes the data provider and the mix */ }
            player = null;
        }

        if (provider != null)
        {
            try { provider.Dispose(); } catch (Exception) { /* best effort */ }
            provider = null;
        }

        tempoSource.IsPlaying = false;
    }

    private string DescribeTrackNames()
    {
        var names = new List<string>();
        lock (gate)
        {
            foreach (var track in tracks)
            {
                names.Add(track.Name.Length == 0 ? "(unnamed)" : $"'{track.Name}'");
            }
        }

        return names.Count == 0
            ? "This song has no tracks."
            : $"It has: {string.Join(", ", names)}.";
    }

    // Callers hold the gate.
    private void StartLevelMeasurement(PlayerTrack[] snapshot)
    {
        if (!AutoSetRelativeTrackLevels || autoMeasurementStarted)
        {
            return;
        }

        autoMeasurementStarted = true;
        levelMeasurement = Task.Run(() => MeasureLevels(snapshot));
    }

    private static void MeasureLevels(PlayerTrack[] snapshot)
    {
        foreach (var track in snapshot)
        {
            if (!track.HasAudioSource || !track.HasMidiSource)
            {
                continue;
            }

            var audioRms = MeasureRms(track, TrackSource.Audio);
            var midiRms = MeasureRms(track, TrackSource.Midi);

            // Nothing to match: a silent recording or a rendition that produced no sound would only
            // give an absurd ratio, so the gain the consumer set stands.
            if (audioRms <= 1e-6 || midiRms <= 1e-6)
            {
                continue;
            }

            var ratio = audioRms / midiRms;
            track.MidiSourceGain = (float)Math.Clamp(ratio, 1.0 / 32.0, 32.0);
        }
    }

    private static double MeasureRms(PlayerTrack track, TrackSource source)
    {
        const int chunkFrames = 4096;

        using (SourceRenderer renderer = source == TrackSource.Audio
                   ? new AudioSourceRenderer(track.AudioSource, LevelMeasurementSampleRate)
                   : new MidiSourceRenderer(track, LevelMeasurementSampleRate))
        {
            var total = renderer.LengthFrames;
            if (total <= 0)
            {
                return 0.0;
            }

            var buffer = new float[chunkFrames * 2];
            var sumOfSquares = 0.0;
            var counted = 0L;
            var rendered = 0L;

            while (rendered < total)
            {
                var take = (int)Math.Min(chunkFrames, total - rendered);
                var span = buffer.AsSpan(0, take * 2);
                renderer.Render(span);

                for (var i = 0; i < span.Length; i++)
                {
                    sumOfSquares += (double)span[i] * span[i];
                }

                counted += span.Length;
                rendered += take;
            }

            return counted == 0 ? 0.0 : Math.Sqrt(sumOfSquares / counted);
        }
    }

    private void RequireStopped(string action)
    {
        if (player != null && player.State != EnginePlaybackState.Stopped)
        {
            throw new InvalidOperationException(
                $"Stop the song before trying to {action}: the mix is being rendered on the audio thread.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (disposed)
        {
            throw new ObjectDisposedException(nameof(MultiTrackPlayer));
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

    // WaveFileWriter always disposes the stream it was handed; this keeps leaveOpen honest without
    // changing that behaviour for every other caller.
    private sealed class LeaveOpenStream : Stream
    {
        private readonly Stream inner;

        internal LeaveOpenStream(Stream inner) => this.inner = inner;

        public override bool CanRead => inner.CanRead;
        public override bool CanSeek => inner.CanSeek;
        public override bool CanWrite => inner.CanWrite;
        public override long Length => inner.Length;

        public override long Position
        {
            get => inner.Position;
            set => inner.Position = value;
        }

        public override void Flush() => inner.Flush();
        public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
        public override long Seek(long offset, SeekOrigin origin) => inner.Seek(offset, origin);
        public override void SetLength(long value) => inner.SetLength(value);
        public override void Write(byte[] buffer, int offset, int count) => inner.Write(buffer, offset, count);

        protected override void Dispose(bool disposing)
        {
            // Deliberately does not touch the inner stream.
            base.Dispose(disposing);
        }
    }
}
