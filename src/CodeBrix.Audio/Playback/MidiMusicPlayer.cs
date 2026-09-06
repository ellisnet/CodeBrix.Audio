using System;
using System.IO;
using System.Threading;
using CodeBrix.Audio.Engine.Components;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.Mpe;
using CodeBrix.Audio.Synth.Sfz;
using CodeBrix.Audio.Wave;
using EnginePlaybackState = CodeBrix.Audio.Engine.Enums.PlaybackState;

namespace CodeBrix.Audio.Playback;

/// <summary>
/// A long-running player for MIDI music rendered through a SoundFont or an SFZ instrument, with the
/// same transport controls as <see cref="AudioFilePlayer"/>: play/pause/stop, volume, looping, seek to
/// a timecode, and a readable position and duration.
/// </summary>
/// <remarks>
/// <para>
/// This is the counterpart to <see cref="AudioFilePlayer"/> for music that is synthesized rather than
/// decoded. Load an instrument - a <c>.sf2</c> SoundFont or a <c>.sfz</c> instrument - and a MIDI
/// sequence, then drive it exactly as you would a file player. The format decides which synthesizer
/// renders; the transport is identical either way. The music mixes into the process-wide
/// <see cref="SharedAudioOutput"/> alongside everything else.
/// </para>
/// <para>
/// Instruments are large. Load them through a <see cref="SoundFontCache"/> or
/// <see cref="SfzInstrumentCache"/> and share one instance across every player rather than reloading
/// per track - see the <c>Load</c> overloads that take an instrument instance.
/// </para>
/// <para>
/// The synthesizer is created at the output device's sample rate, so nothing is resampled. Rendering
/// happens on the engine's real-time audio thread while transport calls arrive from yours; the two are
/// serialized internally, because the underlying synthesizer is not thread-safe.
/// <see cref="PlaybackEnded"/> is raised when a non-looping sequence reaches its end and its final
/// voices finish sounding, on the <see cref="SynchronizationContext"/> captured at load if there is
/// one.
/// </para>
/// <para>Dispose when finished.</para>
/// </remarks>
public sealed class MidiMusicPlayer : IDisposable
{
    private readonly object _lock = new object();
    private readonly TempoSource _tempoSource = new TempoSource();

    private IMidiSynthesizer _synthesizer;
    private IMpeSynthesizer _mpe;
    private MidiSynthDataProvider _provider;
    private SoundPlayer _player;
    private SynchronizationContext _syncContext;
    private MidiSequence _sequence;
    private MidiSequencer.MessageHook _messageFilter;
    private MidiMessageObserver _messageObserver;
    private float _volume = 1.0f;
    private bool _dropAuxiliaryOutputs;
    private float _speed = 1.0f;
    private bool _isLooping;
    private bool _disposed;
    private MpeMode _mpeMode = MpeMode.Off;
    private double _mpeMemberBendRange = MpeChannelState.DefaultMemberBendRange;
    private int _mpeLowerZoneMemberCount;
    private int _mpeUpperZoneMemberCount;

    /// <summary>
    /// Raised when a non-looping sequence reaches its end and its final voices finish sounding, so
    /// release tails ring out before the event. Not raised for <see cref="Stop"/>, and not raised
    /// while <see cref="IsLooping"/> is set. Raised on the <see cref="SynchronizationContext"/>
    /// captured when the sequence was loaded, if there is one; otherwise on a background thread.
    /// </summary>
    public event EventHandler PlaybackEnded;

    /// <summary>Whether a SoundFont and sequence are loaded and ready to play.</summary>
    public bool IsLoaded
    {
        get { lock (_lock) { return _player != null; } }
    }

    /// <summary>The current playback position. <see cref="TimeSpan.Zero"/> if nothing is loaded.</summary>
    public TimeSpan Position
    {
        get { lock (_lock) { return _provider == null ? TimeSpan.Zero : _provider.CurrentTime; } }
    }

    /// <summary>The total length of the loaded sequence. <see cref="TimeSpan.Zero"/> if nothing is loaded.</summary>
    public TimeSpan Duration
    {
        get { lock (_lock) { return _sequence == null ? TimeSpan.Zero : _sequence.Length; } }
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

    /// <summary>
    /// Whether an instrument's AUXILIARY STEREO OUTPUTS are thrown away instead of being folded into
    /// the stereo mix. False by default, so nothing an instrument makes is silently lost.
    /// </summary>
    /// <remarks>
    /// A Decent Sampler preset can route a group, a zone or a bus to one of sixteen auxiliary stereo
    /// outputs. A plug-in host would give those their own outputs; this player has one stereo pair, so
    /// it adds them into the mix. Set this when a preset uses auxiliary outputs for something a
    /// listener should not hear through the main pair - a cue feed, or a layer meant for an external
    /// processor. Formats without auxiliary outputs are unaffected. Persists across loads.
    /// </remarks>
    public bool DropAuxiliaryOutputs
    {
        get { lock (_lock) { return _dropAuxiliaryOutputs; } }
        set
        {
            lock (_lock)
            {
                _dropAuxiliaryOutputs = value;

                if (_synthesizer is IMultiOutputRenderer renderer)
                {
                    renderer.FoldAuxiliaryOutputs = !value;
                }
            }
        }
    }

    /// <summary>
    /// Whether the sequence repeats. Loop points come from the sequence itself (see
    /// <see cref="MidiSequenceLoopType"/>); a sequence with no loop point repeats from the start.
    /// Persists across loads.
    /// </summary>
    public bool IsLooping
    {
        get { lock (_lock) { return _isLooping; } }
        set
        {
            lock (_lock)
            {
                _isLooping = value;
                _provider?.SetLooping(value);
            }
        }
    }

    /// <summary>
    /// The live musical clock of the sequence being played: its tempo at the current position, and
    /// how far into it the transport has travelled in beats.
    /// </summary>
    /// <remarks>
    /// Fed from the loaded sequence's own tempo map as the music renders, so a sequence with tempo
    /// changes reports the tempo in force right now rather than the one it started at. Before
    /// anything is loaded it reports the MIDI default of 120 BPM at beat zero. Reads are lock-free
    /// and safe from any thread, including the audio thread.
    /// </remarks>
    public TempoSource TempoSource => _tempoSource;

    /// <summary>
    /// How the player reads the MIDI Polyphonic Expression zones of the music it plays.
    /// <see cref="CodeBrix.Audio.Synth.Mpe.MpeMode.Off"/> by default. Persists across loads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A performance recorded from an expressive controller spreads each note onto its own MIDI
    /// channel so that it can bend, brighten and swell alone. Exporters normally leave out the
    /// configuration message that says so, which is what <see cref="CodeBrix.Audio.Synth.Mpe.MpeMode.Auto"/> is for:
    /// notes spread over channels 2 to 16 with per-channel bends, and nothing on channel 1, are read
    /// as a lower zone. <see cref="CodeBrix.Audio.Synth.Mpe.MpeMode.LowerZone"/> pins the same layout for a file the
    /// detector is not sure about.
    /// </para>
    /// <para>
    /// All three sampled instrument formats - SoundFont, SFZ and Decent Sampler - read a performance
    /// the same way, so the same recording plays alike through any of them. A synthesizer of your
    /// own follows too when it implements <see cref="IMpeSynthesizer"/>.
    /// </para>
    /// </remarks>
    public MpeMode MpeMode
    {
        get { lock (_lock) { return _mpeMode; } }
        set
        {
            lock (_lock)
            {
                _mpeMode = value;

                var mpe = MpeSynthesizer();
                if (mpe != null)
                {
                    mpe.MpeMode = value;
                }
            }
        }
    }

    /// <summary>
    /// How far a member channel's pitch bend reaches when the music never says, in semitones.
    /// Forty-eight by default, the value expressive controllers ship with. Persists across loads.
    /// </summary>
    /// <remarks>RPN 0 in the music overrides this, per channel and per zone.</remarks>
    public double MpeMemberBendRange
    {
        get { lock (_lock) { return _mpeMemberBendRange; } }
        set
        {
            lock (_lock)
            {
                _mpeMemberBendRange = value;

                var mpe = MpeSynthesizer();
                if (mpe != null)
                {
                    mpe.MpeMemberBendRange = value;
                }
            }
        }
    }

    /// <summary>
    /// How many member channels the lower zone holds in an explicit <see cref="MpeMode"/>. Zero, the
    /// default, means fifteen when only the lower zone is on and seven when both zones are. Persists
    /// across loads.
    /// </summary>
    /// <remarks>Use this to pin a file the automatic detector reads differently from how it was played.</remarks>
    public int MpeLowerZoneMemberCount
    {
        get { lock (_lock) { return _mpeLowerZoneMemberCount; } }
        set
        {
            lock (_lock)
            {
                _mpeLowerZoneMemberCount = value;

                var mpe = MpeSynthesizer();
                if (mpe != null)
                {
                    mpe.MpeLowerZoneMemberCount = value;
                }
            }
        }
    }

    /// <summary>The upper zone's equivalent of <see cref="MpeLowerZoneMemberCount"/>.</summary>
    public int MpeUpperZoneMemberCount
    {
        get { lock (_lock) { return _mpeUpperZoneMemberCount; } }
        set
        {
            lock (_lock)
            {
                _mpeUpperZoneMemberCount = value;

                var mpe = MpeSynthesizer();
                if (mpe != null)
                {
                    mpe.MpeUpperZoneMemberCount = value;
                }
            }
        }
    }

    /// <summary>
    /// The lower MPE zone as the loaded instrument currently reads it - master channel 1, its
    /// members, and the bend ranges in force.
    /// </summary>
    /// <remarks>
    /// Reads back what a configuration message in the music, or automatic detection, decided. An
    /// inactive zone is reported while nothing is loaded, or while the loaded instrument is played
    /// by a synthesizer that does not implement <see cref="IMpeSynthesizer"/>.
    /// </remarks>
    public MpeZoneInfo MpeLowerZone
    {
        get
        {
            lock (_lock)
            {
                var mpe = MpeSynthesizer();
                return mpe == null ? default(MpeZoneInfo) : mpe.MpeLowerZone;
            }
        }
    }

    /// <summary>The upper MPE zone as the loaded instrument currently reads it, with master channel 16.</summary>
    public MpeZoneInfo MpeUpperZone
    {
        get
        {
            lock (_lock)
            {
                var mpe = MpeSynthesizer();
                return mpe == null ? default(MpeZoneInfo) : mpe.MpeUpperZone;
            }
        }
    }

    /// <summary>
    /// The note-off ("lift") velocity of the last note-off for a key on a channel, 0 to 127, or -1
    /// when the loaded instrument format does not capture it.
    /// </summary>
    /// <param name="channel">The MIDI channel, 0 to 15.</param>
    /// <param name="key">The MIDI note number, 0 to 127.</param>
    /// <returns>The release velocity, 0 when that key has not been released, or -1 when unavailable.</returns>
    /// <remarks>
    /// <para>
    /// An expressive controller sends how quickly a finger left the key, and a MIDI file records it.
    /// No sample format in this package defines what it should DO, so it changes nothing about how a
    /// preset sounds; it is here so a host can react to it.
    /// </para>
    /// <para>
    /// To watch lifts as they happen rather than ask afterwards, use
    /// <see cref="MidiMessageProcessed"/>: a note-off carries the lift as its second data byte.
    /// Every instrument format in this package answers this; a synthesizer of your own answers when
    /// it implements <see cref="IMpeSynthesizer"/>.
    /// </para>
    /// </remarks>
    public int GetReleaseVelocity(int channel, int key)
    {
        RequireChannel(channel);

        lock (_lock)
        {
            var mpe = MpeSynthesizer();
            return mpe == null ? -1 : mpe.ReleaseVelocity(channel, key);
        }
    }

    // The loaded synthesizer's MPE surface, or null when nothing is loaded or the loaded
    // synthesizer has none. Called with the lock held.
    private IMpeSynthesizer MpeSynthesizer() => _mpe;

    // Resolved once per load rather than on every read: every synthesizer in the package that reads
    // MPE declares the interface, and one that does not simply has no MPE surface.
    private static IMpeSynthesizer ResolveMpe(IMidiSynthesizer synthesizer) =>
        synthesizer as IMpeSynthesizer;

    /// <summary>The number of voices currently sounding. Useful for diagnostics and polyphony tuning.</summary>
    public int ActiveVoiceCount
    {
        get { lock (_lock) { return _synthesizer == null ? 0 : _synthesizer.ActiveVoiceCount; } }
    }

    /// <summary>
    /// The sequence currently loaded, or <see langword="null"/> if nothing is loaded.
    /// </summary>
    /// <remarks>
    /// Chiefly useful after <see cref="Load(string, string)"/>, which builds the sequence itself and
    /// would otherwise leave the caller without a reference to it.
    /// </remarks>
    public MidiSequence Sequence
    {
        get { lock (_lock) { return _sequence; } }
    }

    /// <summary>
    /// The playback speed multiplier: 1.0 is the sequence's own tempo, 0.5 is half speed, 2.0 is
    /// double speed. Must not be negative. Persists across loads.
    /// </summary>
    /// <remarks>
    /// This scales the tempo without changing pitch - the synthesizer still renders every note at its
    /// written frequency, the sequence just advances more slowly or quickly. A value of 0 freezes the
    /// transport while leaving sounding voices to ring out.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public float Speed
    {
        get { lock (_lock) { return _speed; } }
        set
        {
            if (value < 0.0f)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "The playback speed must not be negative.");
            }

            lock (_lock)
            {
                _speed = value;
                if (_provider != null)
                {
                    _provider.Speed = value;
                }
            }
        }
    }

    /// <summary>
    /// An observe-only callback raised after each MIDI message is delivered to the synthesizer -
    /// the hook for making something outside the audio react to the music. Persists across loads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is almost always the one you want. It cannot change what is played, so there is no way to
    /// silence the music with it. See <see cref="MidiMessageFilter"/> for the hook that CAN.
    /// </para>
    /// <para>
    /// It runs on the real-time AUDIO THREAD - see <see cref="MidiMessageObserver"/> for the rules
    /// that come with that. In particular, do not call back into this player from it.
    /// </para>
    /// </remarks>
    public MidiMessageObserver MidiMessageProcessed
    {
        get { lock (_lock) { return _messageObserver; } }
        set
        {
            lock (_lock)
            {
                _messageObserver = value;
                if (_provider != null)
                {
                    _provider.MessageObserver = value;
                }
            }
        }
    }

    /// <summary>
    /// A hook that REPLACES delivery of each MIDI message to the synthesizer, for transposing,
    /// re-channelling or suppressing messages as they play. Persists across loads.
    /// </summary>
    /// <remarks>
    /// <para>
    /// READ THIS BEFORE SETTING IT. While this is non-null the player does NOT deliver messages
    /// itself - your hook owns delivery. A hook that inspects a message and returns without calling
    /// <see cref="IMidiSynthesizer.ProcessMidiMessage"/> on the synthesizer it was handed silences
    /// the music completely, which looks like a bug in the player rather than in the hook. To merely
    /// WATCH messages, use <see cref="MidiMessageProcessed"/>, which cannot do this.
    /// </para>
    /// <para>
    /// The synthesizer passed to the hook is safe to use FROM INSIDE THE HOOK ONLY - the lock that
    /// serializes it against rendering is held for the duration of the call. Never store it and use
    /// it later; use <see cref="SendMidiMessage"/> for that, which takes the lock properly.
    /// </para>
    /// <para>It runs on the real-time audio thread; the same speed and allocation rules apply.</para>
    /// </remarks>
    public MidiSequencer.MessageHook MidiMessageFilter
    {
        get { lock (_lock) { return _messageFilter; } }
        set
        {
            lock (_lock)
            {
                _messageFilter = value;
                if (_provider != null)
                {
                    _provider.MessageFilter = value;
                }
            }
        }
    }

    /// <summary>
    /// Sends a MIDI message to the synthesizer alongside the sequence that is playing - the general
    /// form of the per-channel helpers below.
    /// </summary>
    /// <param name="channel">The channel to send to, 0-15.</param>
    /// <param name="command">The command nibble: 0x80 note-off, 0x90 note-on, 0xB0 control change, 0xC0 program change, 0xE0 pitch bend.</param>
    /// <param name="data1">The first data byte, 0-127.</param>
    /// <param name="data2">The second data byte, 0-127. Ignored by commands that take one byte.</param>
    /// <remarks>
    /// Safe to call from any thread at any time: the call is serialized against the rendering that
    /// happens on the audio thread, which is why this exists rather than a property handing back the
    /// synthesizer itself (an <see cref="IMidiSynthesizer"/> is not thread-safe, and the lock that
    /// makes it safe here is not reachable from outside). Does nothing when nothing is loaded.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 0-15.</exception>
    public void SendMidiMessage(int channel, int command, int data1, int data2)
    {
        RequireChannel(channel);

        lock (_lock)
        {
            if (_disposed || _provider == null)
            {
                return;
            }

            _provider.SendMidiMessage(channel, command, data1, data2);
        }
    }

    /// <summary>
    /// Sets one channel's volume, as MIDI control change 7. This is how a layered arrangement is
    /// mixed live - fade a channel in or out and the rest of the sequence plays on unchanged.
    /// </summary>
    /// <param name="channel">The channel to set, 0-15.</param>
    /// <param name="volume">The volume, 0.0 (silent) to 1.0 (full). Clamped.</param>
    /// <remarks>
    /// The sequence's own control-change 7 messages still apply: a track that automates its volume
    /// will overwrite what is set here the next time it does so. For a layer the game controls, use a
    /// channel the sequence does not automate.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 0-15.</exception>
    public void SetChannelVolume(int channel, float volume)
    {
        var clamped = volume < 0.0f ? 0.0f : volume > 1.0f ? 1.0f : volume;
        SendMidiMessage(channel, 0xB0, 7, (int)(clamped * 127.0f + 0.5f));
    }

    /// <summary>
    /// Sets one channel's stereo position, as MIDI control change 10.
    /// </summary>
    /// <param name="channel">The channel to set, 0-15.</param>
    /// <param name="pan">The position, -1.0 (full left) through 0.0 (centre) to 1.0 (full right). Clamped.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 0-15.</exception>
    public void SetChannelPan(int channel, float pan)
    {
        var clamped = pan < -1.0f ? -1.0f : pan > 1.0f ? 1.0f : pan;
        SendMidiMessage(channel, 0xB0, 10, (int)((clamped + 1.0f) * 0.5f * 127.0f + 0.5f));
    }

    /// <summary>
    /// Changes the instrument one channel plays, as a MIDI program change.
    /// </summary>
    /// <param name="channel">The channel to set, 0-15.</param>
    /// <param name="program">The program (patch) number, 0-127.</param>
    /// <remarks>
    /// Which instrument a program number selects is the loaded SoundFont's or SFZ instrument's
    /// business, not this player's. As with volume, the sequence's own program changes still apply.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 0-15, or <paramref name="program"/> is outside 0-127.</exception>
    public void SetChannelProgram(int channel, int program)
    {
        if (program < 0 || program > 127)
        {
            throw new ArgumentOutOfRangeException(nameof(program), program, "A MIDI program number must be 0-127.");
        }

        SendMidiMessage(channel, 0xC0, program, 0);
    }

    private static void RequireChannel(int channel)
    {
        if (channel < 0 || channel > 15)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel, "A MIDI channel must be 0-15.");
        }
    }

    /// <summary>
    /// Loads an instrument and a MIDI file by path, positioned at the start and stopped. The
    /// instrument's extension decides the synthesizer: <c>.sfz</c> loads an SFZ instrument,
    /// <c>.dspreset</c>, <c>.dslibrary</c> and <c>.dsbundle</c> load a Decent Sampler instrument, and
    /// anything else a SoundFont. A FOLDER holding a Decent Sampler preset works too, which is how a
    /// <c>.dsbundle</c> appears on macOS.
    /// </summary>
    /// <remarks>
    /// A Decent Sampler instrument loaded this way is held in a process-wide cache
    /// (<see cref="SharedDecentSamplerCache"/>), because its decoded samples run to hundreds of
    /// megabytes and every player that names the same path should share one copy. The other two formats
    /// keep their existing behaviour of loading a fresh instance; use the overloads that take an
    /// instrument to share those.
    /// </remarks>
    /// <param name="instrumentPath">
    /// Path to a <c>.sf2</c>, <c>.sfz</c>, <c>.dspreset</c>, <c>.dslibrary</c> or <c>.dsbundle</c>, or
    /// to a folder holding a Decent Sampler preset.
    /// </param>
    /// <param name="midiFilePath">Path to a Standard MIDI File.</param>
    /// <exception cref="ArgumentNullException">Either path is null.</exception>
    public void Load(string instrumentPath, string midiFilePath)
    {
        if (instrumentPath == null)
        {
            throw new ArgumentNullException(nameof(instrumentPath));
        }

        if (midiFilePath == null)
        {
            throw new ArgumentNullException(nameof(midiFilePath));
        }

        var extension = Path.GetExtension(instrumentPath);

        if (string.Equals(extension, ".sfz", StringComparison.OrdinalIgnoreCase))
        {
            Load(new SfzInstrument(instrumentPath), new MidiSequence(midiFilePath));
        }
        else if (IsDecentSamplerPath(instrumentPath, extension))
        {
            Load(SharedDecentSamplerCache.Get(instrumentPath), new MidiSequence(midiFilePath));
        }
        else
        {
            Load(new SoundFont(instrumentPath), new MidiSequence(midiFilePath));
        }
    }

    /// <summary>
    /// The process-wide cache behind the path form of <see cref="Load(string, string)"/> for Decent
    /// Sampler instruments. Exposed so an application can pre-load a library, count what is held, or
    /// clear it when nothing is playing.
    /// </summary>
    /// <remarks>
    /// Never disposed by the player. Clearing it disposes the instruments it holds, so do that only when
    /// no player is rendering from one.
    /// </remarks>
    public static DecentSamplerInstrumentCache SharedDecentSamplerCache { get; } =
        new DecentSamplerInstrumentCache();

    private static bool IsDecentSamplerPath(string path, string extension)
    {
        if (string.Equals(extension, ".dspreset", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".dslibrary", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".dsbundle", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        // A folder is a Decent Sampler instrument when it holds a preset; a .dsbundle on macOS is a
        // folder, and so is an unpacked library.
        if (!Directory.Exists(path))
        {
            return false;
        }

        return Directory.EnumerateFiles(path, "*.dspreset", SearchOption.AllDirectories).GetEnumerator().MoveNext();
    }

    /// <summary>
    /// Loads a shared SoundFont and a MIDI sequence. This is the overload to prefer: the SoundFont can
    /// come from a <see cref="SoundFontCache"/> and be shared across every player in the process.
    /// </summary>
    /// <param name="soundFont">The SoundFont to render with.</param>
    /// <param name="sequence">The sequence to play.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public void Load(SoundFont soundFont, MidiSequence sequence)
    {
        if (soundFont == null)
        {
            throw new ArgumentNullException(nameof(soundFont));
        }

        LoadCore(rate => new SoundFontSynthesizer(soundFont, rate), sequence);
    }

    /// <summary>
    /// Loads a shared SFZ instrument and a MIDI sequence. This is the overload to prefer for SFZ: the
    /// instrument can come from an <see cref="SfzInstrumentCache"/> and be shared across every player
    /// in the process.
    /// </summary>
    /// <param name="instrument">The SFZ instrument to render with.</param>
    /// <param name="sequence">The sequence to play.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public void Load(SfzInstrument instrument, MidiSequence sequence)
    {
        if (instrument == null)
        {
            throw new ArgumentNullException(nameof(instrument));
        }

        LoadCore(rate => new SfzSynthesizer(instrument, rate), sequence);
    }

    /// <summary>
    /// Loads a shared Decent Sampler instrument and a MIDI sequence. This is the overload to prefer for
    /// the format: the instrument can come from a <see cref="DecentSamplerInstrumentCache"/> and be
    /// shared across every player in the process.
    /// </summary>
    /// <remarks>
    /// The synthesizer reads this player's <see cref="TempoSource"/>, so a note delay or a retrigger
    /// interval written in beats follows the MIDI file's own tempo map.
    /// </remarks>
    /// <param name="instrument">The Decent Sampler instrument to render with.</param>
    /// <param name="sequence">The sequence to play.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public void Load(DecentSamplerInstrument instrument, MidiSequence sequence)
    {
        if (instrument == null)
        {
            throw new ArgumentNullException(nameof(instrument));
        }

        LoadCore(
            rate => new DecentSamplerSynthesizer(instrument, rate)
            {
                TempoSource = _tempoSource,
                MpeMemberBendRange = _mpeMemberBendRange,
                MpeMode = _mpeMode,
            },
            sequence);
    }

    /// <summary>
    /// Loads a synthesizer of your own and a MIDI sequence - a synthesizer this package knows nothing
    /// about, such as the standalone one in CodeBrix.Audio.ModestSynth.
    /// </summary>
    /// <param name="synthesizer">
    /// The synthesizer to play. The player takes it as it is; see the remarks about its sample rate.
    /// </param>
    /// <param name="sequence">The sequence to play.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <remarks>
    /// <para>
    /// The player renders through the shared audio device, and an already-built synthesizer cannot
    /// change the rate it synthesizes at. Its <see cref="IMidiSynthesizer.SampleRate"/> should
    /// therefore be the rate the device settled on, or everything it plays is transposed and
    /// time-stretched by the ratio between the two. Prefer
    /// <see cref="Load(Func{int, IMidiSynthesizer}, MidiSequence)"/>, which is handed the device's own
    /// rate and builds the synthesizer at it.
    /// </para>
    /// <para>
    /// The player does not dispose the synthesizer, and a synthesizer must not be shared between
    /// players: one belongs to one rendering thread.
    /// </para>
    /// </remarks>
    public void Load(IMidiSynthesizer synthesizer, MidiSequence sequence)
    {
        if (synthesizer == null)
        {
            throw new ArgumentNullException(nameof(synthesizer));
        }

        LoadCore(_ => synthesizer, sequence);
    }

    /// <summary>
    /// Loads a MIDI sequence and a factory that builds the synthesizer to play it with, at whatever
    /// rate the shared audio device settled on.
    /// </summary>
    /// <param name="synthesizerFactory">
    /// Builds the synthesizer. Its argument is the device's sample rate in Hz, and the synthesizer it
    /// returns must render at that rate. Called once per load, on the calling thread.
    /// </param>
    /// <param name="sequence">The sequence to play.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <remarks>
    /// This is the general form the format-specific overloads are built on, and the one to prefer for
    /// any synthesizer this package does not have an overload for.
    /// </remarks>
    public void Load(Func<int, IMidiSynthesizer> synthesizerFactory, MidiSequence sequence)
    {
        if (synthesizerFactory == null)
        {
            throw new ArgumentNullException(nameof(synthesizerFactory));
        }

        LoadCore(synthesizerFactory, sequence);
    }

    private void LoadCore(Func<int, IMidiSynthesizer> createSynthesizer, MidiSequence sequence)
    {
        if (sequence == null)
        {
            throw new ArgumentNullException(nameof(sequence));
        }

        lock (_lock)
        {
            ThrowIfDisposed();
            TearDown();

            // Start the shared device first, then build the synthesizer at whatever rate it settled on.
            // Matching the device rate exactly is what keeps this path resampler-free.
            var device = SharedAudioOutput.EnsureStarted(48000);
            var deviceRate = SharedAudioOutput.SampleRate;

            IMidiSynthesizer synthesizer = null;
            MidiSynthDataProvider provider = null;
            SoundPlayer player = null;
            try
            {
                synthesizer = createSynthesizer(deviceRate);

                if (synthesizer is IMultiOutputRenderer renderer)
                {
                    renderer.FoldAuxiliaryOutputs = !_dropAuxiliaryOutputs;
                }

                provider = new MidiSynthDataProvider(synthesizer);

                // Speed and the two message hooks are properties of the PLAYER, not of any one
                // sequence, so they survive a load - the provider (and the sequencer inside it) is
                // rebuilt here and would otherwise come back at its defaults with the hooks lost.
                provider.Speed = _speed;
                provider.MessageFilter = _messageFilter;
                provider.MessageObserver = _messageObserver;
                provider.Tempo = _tempoSource;

                provider.Start(sequence, _isLooping);

                player = new SoundPlayer(device.Engine, device.Format, provider)
                {
                    Volume = _volume,

                    // Looping is the sequencer's job, not the engine's: a MIDI sequence loops at its own
                    // loop point, which is rarely the end of the stream.
                    IsLooping = false,
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
                else
                {
                    try { provider?.Dispose(); } catch (Exception) { /* best effort */ }
                }
                throw;
            }

            _synthesizer = synthesizer;
            _mpe = ResolveMpe(synthesizer);
            _provider = provider;
            _player = player;
            _sequence = sequence;

            // The MPE settings belong to the PLAYER, not to any one instrument, so they are applied
            // to whatever was just loaded - the same rule Speed and the message hooks follow. A
            // format-specific overload may already have passed them to its own settings object;
            // setting them again lands on the same values.
            if (_mpe != null)
            {
                _mpe.MpeMemberBendRange = _mpeMemberBendRange;
                _mpe.MpeLowerZoneMemberCount = _mpeLowerZoneMemberCount;
                _mpe.MpeUpperZoneMemberCount = _mpeUpperZoneMemberCount;
                _mpe.MpeMode = _mpeMode;
            }

            if (_syncContext == null)
            {
                _syncContext = SynchronizationContext.Current;
            }
        }
    }

    /// <summary>Starts or resumes playback from the current position.</summary>
    /// <exception cref="InvalidOperationException">Nothing is loaded.</exception>
    public void Play()
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            RequireLoaded();
            _tempoSource.IsPlaying = true;
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
            _tempoSource.IsPlaying = false;
        }
    }

    /// <summary>Stops playback, silences all voices, and rewinds to the start.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (_disposed || _player == null)
            {
                return;
            }

            _player.Stop();
            _tempoSource.IsPlaying = false;

            // Rewind by restarting the sequence: a stopped MIDI player should be at bar one with no
            // note, controller or pitch-bend state left over from where it was interrupted.
            if (_sequence != null)
            {
                _provider.Start(_sequence, _isLooping);
            }
        }
    }

    /// <summary>
    /// Seeks to a timecode. May be called while playing or stopped.
    /// </summary>
    /// <param name="position">The position to seek to, from the start of the sequence.</param>
    /// <remarks>
    /// Controller state up to <paramref name="position"/> is replayed so the instruments sound correct,
    /// but notes already sounding at that point do not resume - see <see cref="MidiSequencer.Seek"/>.
    /// </remarks>
    /// <exception cref="InvalidOperationException">Nothing is loaded.</exception>
    public void Seek(TimeSpan position)
    {
        lock (_lock)
        {
            ThrowIfDisposed();
            RequireLoaded();

            if (position < TimeSpan.Zero)
            {
                position = TimeSpan.Zero;
            }

            _provider.Seek((int)(position.TotalSeconds * _synthesizer.SampleRate));
        }
    }

    /// <summary>Stops playback and releases the synthesizer. The SoundFont itself is not disposed.</summary>
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

    // Fires on the engine's real-time audio thread; marshal off it before raising the public event.
    private void OnEnginePlaybackEnded(object sender, EventArgs e)
    {
        _tempoSource.IsPlaying = false;

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

    // Removes and disposes the current player chain. Callers hold _lock.
    private void TearDown()
    {
        if (_player != null)
        {
            _player.PlaybackEnded -= OnEnginePlaybackEnded;
            try { SharedAudioOutput.RemoveComponentFromMixer(_player); } catch (Exception) { /* output may be torn down */ }
            try { _player.Dispose(); } catch (Exception) { /* also disposes the data provider */ }
            _player = null;
        }

        if (_provider != null)
        {
            try { _provider.Dispose(); } catch (Exception) { /* best effort */ }
            _provider = null;
        }

        _synthesizer = null;
        _mpe = null;
        _sequence = null;
        _tempoSource.IsPlaying = false;
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
            throw new InvalidOperationException(
                "No MIDI sequence is loaded. Call Load(...) before using the transport.");
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(MidiMusicPlayer));
        }
    }
}
