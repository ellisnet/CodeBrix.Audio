using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler.Effects;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using CodeBrix.Audio.Synth.DecentSampler.Modulation;
using CodeBrix.Audio.Synth.DecentSampler.Samples;
using CodeBrix.Audio.Synth.DecentSampler.Sequencing;
using CodeBrix.Audio.Synth.DecentSampler.Streaming;
using CodeBrix.Audio.Synth.Mpe;
using CodeBrix.Audio.Synth.Sfz;

namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>
/// Plays a <see cref="DecentSamplerInstrument"/> from MIDI events, as <see cref="SoundFontSynthesizer"/>
/// plays a SoundFont and <see cref="SfzSynthesizer"/> plays an SFZ instrument.
/// </summary>
/// <remarks>
/// <para>
/// The third implementation of <see cref="IMidiSynthesizer"/> in this package, so
/// <see cref="MidiSequencer"/>, <see cref="SoundFontRenderer"/> and
/// <c>CodeBrix.Audio.Playback.MidiMusicPlayer</c> drive it without caring which format is loaded. Like
/// its two peers it is NOT thread-safe: sending events and rendering must not overlap.
/// </para>
/// <para>
/// The zone runtime implements the sampler half of the Decent Sampler format: enabled groups and tags,
/// key, velocity and controller ranges, the five trigger modes, round robins, previous-note and
/// legato-interval matching, tag polyphony and voice muting, glide, amplitude envelopes with the
/// format's curve law, loops with crossfades, sample start and end offsets, release triggers with both
/// decay forms, controller-triggered zones, continuous zones, note delays and tempo-driven retriggers.
/// </para>
/// <para>
/// The <c>&lt;midi&gt;</c> element runs on the note path: every note-on, note-off and controller
/// message passes through the instrument's handlers BEFORE the voice runtime sees it, so a
/// <c>&lt;cc&gt;</c> binding moves a knob, a <c>&lt;velocity&gt;</c> binding scales a parameter, and a
/// <c>&lt;note&gt;</c> handler with <c>swallowNotes="true"</c> is a key switch that never sounds. The
/// note sequencer and the arpeggiator sit in the same path: both consume notes and emit their own,
/// sample-accurately inside the render block.
/// </para>
/// <para>
/// Random choices are seeded, so the same instrument and the same events render the same audio on every
/// run. Vary <see cref="DecentSamplerSynthesizerSettings.RandomSeed"/> for a different performance.
/// </para>
/// </remarks>
public sealed class DecentSamplerSynthesizer : IMidiSynthesizer, IMpeSynthesizer, IMultiOutputRenderer,
    IDecentSamplerNoteHost
{
    private const int MidiChannelCount = 16;
    private const int SustainController = 64;

    // The volume ceiling the reference player's parser applies: 16.0 linear, +24.08 dB, the same
    // ceiling the guide gives for the gain effect's level attribute. Measured, plan section 7 item 3.
    private const double MaximumVolume = 16.0;

    private readonly DecentSamplerInstrument _instrument;
    private readonly DecentSamplerExtensionRegistry _extensions;
    private readonly IDecentSamplerTagState _tagState;

    private readonly int _sampleRate;
    private readonly int _blockSize;
    private readonly int _maximumPolyphony;
    private readonly int _randomSeed;
    private readonly float _inverseBlockSize;

    private readonly List<string> _problems = [];
    private readonly SortedSet<string> _unsupported = new(StringComparer.Ordinal);

    private readonly DecentSamplerChannelState[] _channels;
    private readonly MpeChannelState _mpe;
    private readonly DecentSamplerVoiceCollection _voices;

    private DecentSamplerModulationRuntime _modulation;
    private DecentSamplerSequencingRuntime _sequencing;

    private readonly List<DecentSamplerZoneRuntime> _attackZones = [];
    private readonly List<DecentSamplerZoneRuntime> _releaseZones = [];
    private readonly List<DecentSamplerZoneRuntime> _controllerZones = [];
    private readonly List<DecentSamplerZoneRuntime> _continuousZones = [];
    private readonly List<DecentSamplerZoneRuntime> _candidates = [];

    private readonly DecentSamplerSequenceRegister[] _registers;
    private readonly long[] _registerStamps;

    private readonly List<RetriggerSchedule> _retriggers = [];

    private readonly DecentSamplerMixer _mixer;

    private DecentSamplerStreamingMode _streamingMode;
    private readonly int _streamingVoiceCount;
    private readonly int _streamingRingFrames;

    private StreamingVoicePool _streamingPool;
    private DecentSamplerStreamingContext _streamingContext;

    private readonly float[] _blockLeft;
    private readonly float[] _blockRight;
    private readonly float[][] _auxLeft;
    private readonly float[][] _auxRight;

    private Random _random;
    private TempoSource _tempoSource;
    private int _blockRead;
    private long _renderedFrames;
    private long _noteStamp;
    private int _lastTriggeredNote = -1;
    private bool _continuousStarted;
    private bool _foldAuxiliaryOutputs = true;
    private float _masterVolume;

    /// <summary>
    /// Creates a synthesizer for an instrument at a sample rate.
    /// </summary>
    /// <param name="instrument">The instrument to play. Shared instruments are fine; the synthesizer
    /// never disposes one.</param>
    /// <param name="sampleRate">The synthesis sample rate in Hz.</param>
    /// <exception cref="ArgumentNullException"><paramref name="instrument"/> is null.</exception>
    public DecentSamplerSynthesizer(DecentSamplerInstrument instrument, int sampleRate)
        : this(instrument, new DecentSamplerSynthesizerSettings(sampleRate))
    {
    }

    /// <summary>
    /// Creates a synthesizer for an instrument with explicit settings.
    /// </summary>
    /// <param name="instrument">The instrument to play.</param>
    /// <param name="settings">The synthesis settings.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public DecentSamplerSynthesizer(DecentSamplerInstrument instrument, DecentSamplerSynthesizerSettings settings)
    {
        if (instrument == null)
        {
            throw new ArgumentNullException(nameof(instrument));
        }

        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        _instrument = instrument;
        _sampleRate = settings.SampleRate;
        _blockSize = settings.BlockSize;
        _maximumPolyphony = settings.MaximumPolyphony;
        _randomSeed = settings.RandomSeed;
        _extensions = settings.Extensions ?? DecentSamplerExtensions.Shared;
        _masterVolume = settings.MasterVolume;
        _inverseBlockSize = 1f / _blockSize;
        _streamingMode = settings.StreamingMode;
        _streamingVoiceCount = settings.ResolveStreamingVoiceCount();
        _streamingRingFrames = settings.StreamingRingFrames;

        _problems.AddRange(instrument.Problems);
        foreach (var feature in instrument.UnsupportedFeatures)
        {
            _unsupported.Add(feature);
        }

        // The LIVE tag state when the instrument has a binding engine, so a TAG_ENABLED or TAG_VOLUME
        // binding reaches the voices; the parsed <tag> elements otherwise.
        var live = instrument.BindingEngine == null
            ? null
            : new DecentSamplerLiveTagState(instrument.TagStates);

        _tagState = live != null && live.Count > 0
            ? live
            : new DecentSamplerStaticTagState(instrument.Tags);

        _channels = new DecentSamplerChannelState[MidiChannelCount];
        for (var i = 0; i < _channels.Length; i++)
        {
            _channels[i] = new DecentSamplerChannelState();
        }

        _mpe = new MpeChannelState
        {
            MemberBendRange = settings.MpeMemberBendRange,
            LowerZoneMemberCount = settings.MpeLowerZoneMemberCount,
            UpperZoneMemberCount = settings.MpeUpperZoneMemberCount,
            Mode = settings.MpeMode,
        };

        // The instrument-wide round-robin length: the highest seqPosition declared ANYWHERE in the
        // preset. Measured, plan section 7 item 9 - it is not per group, which is why a preset that
        // mixes round-robin sets of different lengths gets silent notes in the reference player too.
        var sharedLength = 1;
        foreach (var zone in instrument.Zones)
        {
            sharedLength = Math.Max(sharedLength, zone.SeqPosition);
        }

        BuildZones(sharedLength);
        BuildStreaming();

        _registers = new DecentSamplerSequenceRegister[instrument.Groups.Count + 1];
        _registerStamps = new long[_registers.Length];

        _registers[0] = new DecentSamplerSequenceRegister(sharedLength);
        foreach (var group in instrument.Groups)
        {
            var explicitLength = 0;
            foreach (var zone in group.Zones)
            {
                if (zone.SeqLength > 0)
                {
                    explicitLength = Math.Max(explicitLength, zone.SeqLength);
                }
            }

            _registers[group.Index + 1] = new DecentSamplerSequenceRegister(
                explicitLength > 0 ? explicitLength : sharedLength);
        }

        _voices = new DecentSamplerVoiceCollection(this, _sampleRate, _blockSize, _maximumPolyphony);

        _blockLeft = new float[_blockSize];
        _blockRight = new float[_blockSize];
        _blockRead = _blockSize;

        _random = new Random(_randomSeed);

        _tempoSource = new TempoSource { BeatsPerMinute = settings.DefaultBeatsPerMinute };

        // The chains and the routing. Built last, because the bus chains and the instrument chain read
        // the tempo source a musical delay follows.
        _mixer = DecentSamplerMixer.Build(
            _instrument, _extensions, _sampleRate, _blockSize, _tempoSource, _problems, _unsupported);

        BuildGroupChains();

        var auxiliary = _mixer.AuxiliaryOutputCount;
        _auxLeft = new float[auxiliary][];
        _auxRight = new float[auxiliary][];
        for (var i = 0; i < auxiliary; i++)
        {
            _auxLeft[i] = new float[_blockSize];
            _auxRight[i] = new float[_blockSize];
        }

        if (settings.EnableModulators)
        {
            _modulation = new DecentSamplerModulationRuntime(
                instrument, _mpe, _sampleRate, _blockSize, _maximumPolyphony, _randomSeed, _problems)
            {
                Tempo = _tempoSource,
            };
        }

        // The <midi> element, the note sequencer and the arpeggiator. Built last, because attaching it
        // hands the binding engine somewhere to send a note-sequence trigger, and the initial state may
        // already have fired one.
        _sequencing = new DecentSamplerSequencingRuntime(this, instrument, _randomSeed);

        // An ALL_NOTES_OFF binding stops the voices, the sequences and the arpeggiator together.
        //
        // The subscription is WEAK. An instrument outlives the synthesizers playing it - the shared
        // cache behind MidiMusicPlayer holds one for the life of the process, and a multi-track
        // player builds a fresh synthesizer per render - so a strong handler would pin every
        // synthesizer ever made for that instrument. The handler drops itself the first time it fires
        // after its synthesizer has gone.
        var weak = new WeakReference<DecentSamplerSynthesizer>(this);
        EventHandler handler = null;

        handler = (sender, arguments) =>
        {
            if (weak.TryGetTarget(out var synthesizer))
            {
                synthesizer.NoteOffAll(false);
            }
            else if (sender is DecentSamplerInstrument owner)
            {
                owner.AllNotesOffRequested -= handler;
            }
        };

        instrument.AllNotesOffRequested += handler;
    }

    /// <summary>The instrument being played.</summary>
    public DecentSamplerInstrument Instrument => _instrument;

    /// <summary>
    /// Everything about this instrument that could not be honoured, one line each: the instrument's own
    /// load problems, plus anything the engine found while building the zone runtime.
    /// </summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>
    /// The names of features the preset uses that this engine does not implement, sorted. A superset of
    /// the instrument's own list.
    /// </summary>
    public IReadOnlyCollection<string> UnsupportedFeatures => _unsupported;

    /// <summary>
    /// The musical clock that drives tempo-based note delays and pattern retriggers. Never null;
    /// setting it to null restores a fresh source at the settings' default tempo.
    /// </summary>
    /// <remarks>
    /// A transport writes this - <c>MidiMusicPlayer</c> and <c>MultiTrackPlayer</c> both publish one
    /// from the MIDI tempo map - and the synthesizer reads it. Left alone it reports 120 BPM, which is
    /// what the reference player uses when no host is present.
    /// </remarks>
    public TempoSource TempoSource
    {
        get => _tempoSource;
        set => _tempoSource = value ?? new TempoSource();
    }

    /// <summary>
    /// How the synthesizer reads the MIDI Polyphonic Expression zones of the music it is given.
    /// <see cref="MpeMode.Off"/> by default, which plays every channel as ordinary MIDI.
    /// </summary>
    /// <remarks>
    /// Changing this reconfigures the zones at once, and forgets any configuration message the music
    /// has already delivered. An MPE Configuration Message arriving later is honoured in every mode
    /// but <see cref="MpeMode.Off"/>, which is the way to insist a file is played as plain MIDI.
    /// </remarks>
    public MpeMode MpeMode
    {
        get => _mpe.Mode;
        set => _mpe.Mode = value;
    }

    /// <summary>
    /// How far a member channel's pitch bend reaches when the music never says, in semitones.
    /// Forty-eight by default, which is what expressive controllers ship with.
    /// </summary>
    /// <remarks>
    /// RPN 0 in the music overrides this. Master channels and channels outside every zone keep
    /// MIDI's own two semitones unless RPN 0 says otherwise.
    /// </remarks>
    public double MpeMemberBendRange
    {
        get => _mpe.MemberBendRange;
        set => _mpe.MemberBendRange = value;
    }

    /// <summary>
    /// How many member channels the lower zone holds in an explicit mode. Zero, the default, means
    /// fifteen when only the lower zone is on and seven when both zones are.
    /// </summary>
    public int MpeLowerZoneMemberCount
    {
        get => _mpe.LowerZoneMemberCount;
        set => _mpe.LowerZoneMemberCount = value;
    }

    /// <summary>The upper zone's equivalent of <see cref="MpeLowerZoneMemberCount"/>.</summary>
    public int MpeUpperZoneMemberCount
    {
        get => _mpe.UpperZoneMemberCount;
        set => _mpe.UpperZoneMemberCount = value;
    }

    /// <summary>
    /// The lower zone as it currently stands: master channel 1, its members, and the bend ranges in
    /// force. Reads back what a configuration message in the music, or automatic detection, decided.
    /// </summary>
    public MpeZoneInfo MpeLowerZone => _mpe.LowerZone;

    /// <summary>The upper zone as it currently stands, with master channel 16.</summary>
    public MpeZoneInfo MpeUpperZone => _mpe.UpperZone;

    /// <summary>
    /// The note-off ("lift") velocity of the last note-off for a key on a channel, 0 to 127.
    /// </summary>
    /// <param name="channel">The MIDI channel, 0 to 15.</param>
    /// <param name="key">The MIDI note number, 0 to 127.</param>
    /// <returns>The release velocity, or 0 when that key has not been released.</returns>
    /// <remarks>
    /// The Decent Sampler format defines no consumer for release velocity, so this changes nothing
    /// about how a preset sounds. It is captured because a MIDI file can carry it and a host may
    /// want it, and because the sounding voice keeps its own copy.
    /// </remarks>
    public int ReleaseVelocity(int channel, int key) => _mpe.ReleaseVelocity(channel, key);

    /// <inheritdoc/>
    public int SampleRate => _sampleRate;

    /// <inheritdoc/>
    public int BlockSize => _blockSize;

    /// <summary>The maximum number of simultaneously sounding voices.</summary>
    public int MaximumPolyphony => _maximumPolyphony;

    /// <summary>
    /// How many streamed notes can sound at once: the settings' own
    /// <see cref="DecentSamplerSynthesizerSettings.StreamingVoiceCount"/>, or
    /// <see cref="MaximumPolyphony"/> when that was left automatic.
    /// </summary>
    /// <remarks>
    /// Only meaningful for an instrument that streams; a synthesizer over an in-memory instrument
    /// allocates no buffers at all. The pool is built at construction, so this number never changes.
    /// </remarks>
    public int StreamingVoiceCount => _streamingVoiceCount;

    /// <summary>The number of MIDI channels, always 16. Every channel plays the same instrument.</summary>
    public int ChannelCount => MidiChannelCount;

    /// <inheritdoc/>
    public int ActiveVoiceCount => _voices.ActiveVoiceCount;

    /// <inheritdoc/>
    public float MasterVolume
    {
        get => _masterVolume;
        set => _masterVolume = value;
    }

    /// <inheritdoc/>
    public void ProcessMidiMessage(int channel, int command, int data1, int data2)
    {
        if (!(0 <= channel && channel < _channels.Length))
        {
            return;
        }

        // The MPE contract sees every channel-voice message: it keeps the per-channel bend, its
        // range from RPN 0, the registered tunings, the timbre controller, both kinds of pressure
        // and the zone configuration that ties member channels to their master.
        _mpe.ProcessMessage(channel, command, data1, data2);

        var state = _channels[channel];

        switch (command)
        {
            case 0x80: // Note off
                NoteOff(channel, data1);
                break;

            case 0x90: // Note on
                NoteOn(channel, data1, data2);
                break;

            case 0xA0: // Polyphonic key pressure - stored; the modulator phase gives it a consumer
                state.SetPolyPressure(data1, data2);
                break;

            case 0xB0: // Controller
                switch (data1)
                {
                    case 0x78: // All sound off
                        NoteOffAll(channel, true);
                        break;

                    case 0x79: // Reset all controllers
                        state.ResetControllers();
                        break;

                    case 0x7B: // All notes off
                        NoteOffAll(channel, false);
                        break;

                    default:
                        SetController(channel, state, data1, data2);
                        break;
                }
                break;

            case 0xD0: // Channel pressure - stored; the modulator phase gives it a consumer
                state.SetChannelPressure(data1);
                break;

            case 0xE0: // Pitch bend
                state.SetPitchBend(data1, data2);
                break;
        }
    }

    /// <summary>
    /// Starts a note.
    /// </summary>
    /// <param name="channel">The MIDI channel, 0 to 15.</param>
    /// <param name="key">The MIDI note number, 0 to 127.</param>
    /// <param name="velocity">The note-on velocity. Zero is a note-off, as measured.</param>
    public void NoteOn(int channel, int key, int velocity)
    {
        // Measured: a note-on with velocity 0 starts no voice and releases a sounding note of the same
        // key (plan section 7 item 2).
        if (velocity <= 0)
        {
            NoteOff(channel, key);
            return;
        }

        if (!(0 <= channel && channel < _channels.Length) || key < 0 || key > 127)
        {
            return;
        }

        // The <midi> handlers get the note BEFORE anything is played, which is what the guide says
        // and what makes a key switch a key switch: a handler carrying swallowNotes="true" consumes
        // the note and the sampler never sees it. An armed arpeggiator consumes it too, and plays its
        // own notes instead.
        if (_sequencing != null && _sequencing.HandleMidiNoteOn(channel, key, velocity))
        {
            return;
        }

        StartNoteInternal(channel, key, velocity, 0);
    }

    // Starts a note in the voice runtime, past the <midi> handlers and the arpeggiator.
    //
    // frameOffset places the note inside the block that is about to render. A note the sequencer or
    // the arpeggiator generated carries one, so it lands on its beat rather than on the nearest block
    // boundary; a note that arrived as MIDI carries zero, because a message that arrives between two
    // Render calls has no finer position than that.
    private void StartNoteInternal(int channel, int key, int velocity, int frameOffset)
    {
        var state = _channels[channel];
        var heldBefore = state.HeldKeyCount;

        _noteStamp++;
        _mpe.NoteOn(channel, key);
        _modulation?.NoteOn(channel, key, velocity);

        CollectAttackCandidates(state, key, velocity, heldBefore);
        ApplyRoundRobin();

        var retriggerFrames = 0L;

        foreach (var runtime in _candidates)
        {
            SilenceForTags(runtime);
            EnforceTagPolyphony(runtime);

            var glideSemitones = GlideOffset(runtime, key, heldBefore, out var glideSeconds);

            StartVoice(runtime, channel, key, velocity, 1f, glideSemitones, glideSeconds, frameOffset);

            if (runtime.Zone.RetriggerEnabled && retriggerFrames == 0L)
            {
                retriggerFrames = TimeToFrames(
                    runtime.Zone.RetriggerInterval, runtime.Zone.RetriggerIntervalUnit);
            }
        }

        if (retriggerFrames > 0)
        {
            _retriggers.Add(new RetriggerSchedule
            {
                Channel = channel,
                Key = key,
                Velocity = velocity,
                NextFrame = _renderedFrames + frameOffset + retriggerFrames,
                IntervalFrames = retriggerFrames,
            });
        }

        _lastTriggeredNote = key;
        state.KeyDown(key, velocity, _renderedFrames);

        // MEASURED (round 2, item 25): a release trigger only fires on a key that also started a
        // voice, so whether this note-on found anything to play is remembered until the key comes up.
        state.SetStartedVoice(key, _candidates.Count > 0);
    }

    /// <summary>
    /// Stops a note, releasing its voices and firing any matching release-trigger zones.
    /// </summary>
    /// <param name="channel">The MIDI channel, 0 to 15.</param>
    /// <param name="key">The MIDI note number, 0 to 127.</param>
    public void NoteOff(int channel, int key)
    {
        if (!(0 <= channel && channel < _channels.Length) || key < 0 || key > 127)
        {
            return;
        }

        // The same three stages as a note-on, in reverse: the <midi> handlers fire (an eventType of
        // note_off or any reaches them), a midi_key sequence started by this key stops, and an armed
        // arpeggiator takes the key out of its chord.
        if (_sequencing != null &&
            _sequencing.HandleMidiNoteOff(channel, key, _mpe.ReleaseVelocity(channel, key)))
        {
            return;
        }

        StopNoteInternal(channel, key);
    }

    // Releases a note in the voice runtime, past the <midi> handlers and the arpeggiator.
    private void StopNoteInternal(int channel, int key)
    {
        var state = _channels[channel];
        var wasHeld = state.IsKeyHeld(key);

        state.KeyUp(key);
        _mpe.NoteOff(channel, key, _mpe.ReleaseVelocity(channel, key));
        _modulation?.NoteOff(HeldKeyCount());

        if (!wasHeld)
        {
            return;
        }

        if (state.IsSustainDown)
        {
            // MEASURED (round 2, item 25): the pedal defers both the release and the release trigger,
            // and releaseTriggerDecay measures the hold TO THE PEDAL LIFT, not to the physical key
            // release - a key held 1 s under a pedal lifted at 3 s decayed by 0.5^3, the same as a key
            // held 3 s with no pedal, and 12 dB below what measuring to the key release would give.
            // The frame recorded here is therefore only a note that the key is waiting on the pedal.
            state.SustainKey(key, _renderedFrames);
            return;
        }

        CompleteNoteOff(channel, state, key, _renderedFrames);
    }

    /// <inheritdoc/>
    public void NoteOffAll(bool immediate)
    {
        _retriggers.Clear();

        // Every key is up afterwards, which is what trigger="first" and trigger="legato" count. Release
        // triggers do NOT fire: a panic is not a performance gesture.
        foreach (var state in _channels)
        {
            state.ReleaseAllKeys();
        }

        _modulation?.NoteOff(0);
        _sequencing?.StopEverything();

        if (immediate)
        {
            _voices.Clear();
            return;
        }

        foreach (var voice in _voices)
        {
            if (!voice.IsContinuous)
            {
                voice.End();
            }
        }
    }

    /// <summary>
    /// Stops every note on one channel.
    /// </summary>
    /// <param name="channel">The MIDI channel, 0 to 15.</param>
    /// <param name="immediate">When true, voices stop at once instead of releasing.</param>
    public void NoteOffAll(int channel, bool immediate)
    {
        if (!(0 <= channel && channel < _channels.Length))
        {
            return;
        }

        for (var i = _retriggers.Count - 1; i >= 0; i--)
        {
            if (_retriggers[i].Channel == channel)
            {
                _retriggers.RemoveAt(i);
            }
        }

        _channels[channel].ReleaseAllKeys();
        _modulation?.NoteOff(HeldKeyCount());
        _sequencing?.StopChannel(channel);

        foreach (var voice in _voices)
        {
            if (voice.Channel != channel || voice.IsContinuous)
            {
                continue;
            }

            if (immediate)
            {
                voice.Kill();
            }
            else
            {
                voice.End();
            }
        }
    }

    /// <inheritdoc/>
    public void Reset()
    {
        _voices.Clear();
        _retriggers.Clear();

        foreach (var channel in _channels)
        {
            channel.Reset();
        }

        _mpe.Reset();
        _modulation?.Reset();
        _sequencing?.Reset();

        foreach (var register in _registers)
        {
            register.Reset();
        }

        Array.Clear(_registerStamps, 0, _registerStamps.Length);

        // Every tail in the bus and instrument chains goes with the voices.
        _mixer.Reset();

        // Reseeding is what makes a replay of the same sequence identical, round robins and all.
        _random = new Random(_randomSeed);

        _renderedFrames = 0;
        _noteStamp = 0;
        _lastTriggeredNote = -1;
        _continuousStarted = false;
        _blockRead = _blockSize;
    }

    /// <inheritdoc/>
    public void Render(Span<float> left, Span<float> right) => RenderWithAuxiliary(left, right, null, null);

    /// <inheritdoc/>
    public int AuxiliaryOutputCount => _auxLeft.Length;

    /// <summary>
    /// Whether <see cref="Render"/> adds the auxiliary pairs into the stereo mix. True by default, so
    /// a preset that sends a layer to an auxiliary output is still heard through a stereo consumer.
    /// </summary>
    public bool FoldAuxiliaryOutputs
    {
        get => _foldAuxiliaryOutputs;
        set => _foldAuxiliaryOutputs = value;
    }

    /// <inheritdoc/>
    public void RenderWithAuxiliary(
        Span<float> left, Span<float> right, float[][] auxiliaryLeft, float[][] auxiliaryRight)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException("The output buffers for the left and right must be the same length.");
        }

        var pairs = auxiliaryLeft == null || auxiliaryRight == null
            ? 0
            : Math.Min(
                Math.Min(auxiliaryLeft.Length, auxiliaryRight.Length),
                _auxLeft.Length);

        for (var i = 0; i < pairs; i++)
        {
            if (auxiliaryLeft[i] == null || auxiliaryRight[i] == null ||
                auxiliaryLeft[i].Length < left.Length || auxiliaryRight[i].Length < left.Length)
            {
                throw new ArgumentException(
                    "Every auxiliary buffer must be at least as long as the main output buffers.");
            }
        }

        // Asking for the auxiliary pairs separately means they are not folded, whatever the flag says.
        var fold = _foldAuxiliaryOutputs && auxiliaryLeft == null;

        var wrote = 0;
        while (wrote < left.Length)
        {
            if (_blockRead == _blockSize)
            {
                RenderBlock();
                _blockRead = 0;
            }

            var available = Math.Min(_blockSize - _blockRead, left.Length - wrote);

            _blockLeft.AsSpan(_blockRead, available).CopyTo(left.Slice(wrote, available));
            _blockRight.AsSpan(_blockRead, available).CopyTo(right.Slice(wrote, available));

            for (var i = 0; i < pairs; i++)
            {
                _auxLeft[i].AsSpan(_blockRead, available).CopyTo(auxiliaryLeft[i].AsSpan(wrote, available));
                _auxRight[i].AsSpan(_blockRead, available).CopyTo(auxiliaryRight[i].AsSpan(wrote, available));
            }

            if (fold)
            {
                for (var i = 0; i < _auxLeft.Length; i++)
                {
                    var sourceLeft = _auxLeft[i];
                    var sourceRight = _auxRight[i];

                    for (var f = 0; f < available; f++)
                    {
                        left[wrote + f] += sourceLeft[_blockRead + f];
                        right[wrote + f] += sourceRight[_blockRead + f];
                    }
                }
            }

            _blockRead += available;
            wrote += available;
        }
    }

    // ---- the seams the voice reads --------------------------------------------------------------

    // The gain a voice must apply this block, from everything a binding can move while it sounds: the
    // group's enabled flag, the three volume levels, and the tag volumes.
    //
    // Measured (plan section 7 item 3): volumes MULTIPLY across <groups>, <group> and <sample>, and
    // each level is clamped to [0, 16] linear with zero or negative meaning silence.
    internal double LiveVoiceGain(DecentSamplerZoneRuntime runtime)
    {
        var group = runtime.Group;

        if (!group.Enabled)
        {
            return 0.0;
        }

        var gain = ClampVolume(group.InstrumentVolume) *
                   ClampVolume(group.Volume) *
                   ClampVolume(runtime.Zone.Volume);

        // MEASURED (round 2, item 24): tag volumes MULTIPLY over the union of the group's and the
        // sample's tag lists, in any order, and they are NOT clamped the way a group or sample volume
        // is - a tag volume of 4 really is a gain of four. A DISABLED tag silences the zone outright.
        var tags = runtime.Tags;
        for (var i = 0; i < tags.Length; i++)
        {
            if (!_tagState.IsEnabled(tags[i]))
            {
                return 0.0;
            }

            gain *= _tagState.GetVolume(tags[i]);
        }

        return gain;
    }

    // How many per-voice chains a group's template has ever built. The per-voice instantiation the
    // guide documents is not observable from the audio alone, so the tests count it here.
    internal int GroupChainInstanceCount(int groupIndex)
    {
        foreach (var runtime in AllZoneRuntimes())
        {
            if (runtime.Group.Index == groupIndex && runtime.GroupChains != null)
            {
                return runtime.GroupChains.InstantiatedCount;
            }
        }

        return 0;
    }

    // The mixer, so the bus and routing tests can read what the chains did.
    internal DecentSamplerMixer Mixer => _mixer;

    // The streaming ring buffers a voice rents from, or null when nothing in this instrument streams.
    internal StreamingVoicePool StreamingPool => _streamingPool;

    /// <summary>
    /// Who reads a streamed sample off the disk: a background reader thread
    /// (<see cref="DecentSamplerStreamingMode.RealTime"/>, the default) or the render call itself
    /// (<see cref="DecentSamplerStreamingMode.Offline"/>).
    /// </summary>
    /// <remarks>
    /// <para>
    /// Set <see cref="DecentSamplerStreamingMode.Offline"/> on any synthesizer whose
    /// <see cref="Render(Span{float}, Span{float})"/> is NOT an audio-device callback - a WAV export, a
    /// render into a buffer, a test. A render that runs faster than real time outruns the background
    /// reader, and a starved block is written as silence and reported in
    /// <see cref="DecentSamplerInstrument.Problems"/>, so an offline render in the real-time mode is
    /// not reproducible. Never set it on a synthesizer feeding a live device: the render call then
    /// opens and reads files.
    /// </para>
    /// <para>
    /// Change it before rendering starts. A voice that is already sounding keeps the mode it started
    /// with; the next note-on picks the new one up.
    /// </para>
    /// </remarks>
    public DecentSamplerStreamingMode StreamingMode
    {
        get => _streamingMode;

        set
        {
            if (value == _streamingMode)
            {
                return;
            }

            _streamingMode = value;

            if (_streamingContext == null)
            {
                return;
            }

            _streamingContext.Mode = value;

            // Exactly one of the two ever services this context: the shared reader thread in the
            // real-time mode, the render call in the offline one.
            if (value == DecentSamplerStreamingMode.RealTime)
            {
                DecentSamplerStreamingReader.Shared.Register(_streamingContext);
            }
            else
            {
                DecentSamplerStreamingReader.Shared.Unregister(_streamingContext);
            }
        }
    }

    // The streaming work this synthesizer owns, or null when it has none. The tests drive it directly.
    internal DecentSamplerStreamingContext StreamingContext => _streamingContext;


    // The pitch a voice on a channel must add, in semitones: its own bend over its own range, its
    // zone master's bend on top when MPE is in force, and the channel's registered tuning (RPN 1 and
    // RPN 2). The MPE contract owns all of it, because only it knows the zones and the RPN 0 ranges.
    internal double BendSemitones(int channel) => _mpe.PitchOffsetSemitones(channel);

    // Whether a sounding note still owns its member channel's expression, which a newer note on the
    // same channel takes from it.
    internal bool OwnsChannelExpression(int channel, int key) => _mpe.OwnsChannelExpression(channel, key);

    // The modulator runtime, or null when the settings switched it off. The voice pool reads it to
    // push each voice's own modulation into place before that voice renders.
    internal DecentSamplerModulationRuntime Modulation => _modulation;

    // The <midi> element, the note sequencer and the arpeggiator, so the tests can look at what they
    // are holding without going through the audio.
    internal DecentSamplerSequencingRuntime Sequencing => _sequencing;

    // ---- IDecentSamplerNoteHost -----------------------------------------------------------------
    //
    // Explicit implementations, because the interface is how the sequencer and the arpeggiator reach
    // the voice runtime and is not part of this type's public surface.

    int IDecentSamplerNoteHost.SampleRate => _sampleRate;

    TempoSource IDecentSamplerNoteHost.Tempo => _tempoSource;

    void IDecentSamplerNoteHost.StartNote(int channel, int key, int velocity, int frameOffset)
    {
        if (0 <= channel && channel < _channels.Length && 0 <= key && key <= 127 && velocity > 0)
        {
            StartNoteInternal(channel, key, velocity, Math.Clamp(frameOffset, 0, _blockSize - 1));
        }
    }

    void IDecentSamplerNoteHost.StopNote(int channel, int key)
    {
        if (0 <= channel && channel < _channels.Length && 0 <= key && key <= 127)
        {
            StopNoteInternal(channel, key);
        }
    }

    void IDecentSamplerNoteHost.CollectHeldKeys(List<DecentSamplerArpNote> destination)
    {
        for (var channel = 0; channel < _channels.Length; channel++)
        {
            var state = _channels[channel];

            for (var key = 0; key < 128; key++)
            {
                if (state.IsKeyHeld(key))
                {
                    destination.Add(
                        new DecentSamplerArpNote(
                            channel, key, state.NoteOnVelocity(key), destination.Count));
                }
            }
        }
    }


    // How many keys are down across every channel, which is what gates a global-scope envelope.
    private int HeldKeyCount()
    {
        var held = 0;

        for (var index = 0; index < _channels.Length; index++)
        {
            held += _channels[index].HeldKeyCount;
        }

        return held;
    }

    // One block of modulation: every global-scope modulator once, then every sounding voice's own.
    private void TickModulators()
    {
        if (_modulation == null || !_modulation.IsActive)
        {
            return;
        }

        _modulation.Tempo = _tempoSource;
        _modulation.TickGlobal();

        if (!_modulation.HasVoiceScope)
        {
            return;
        }

        foreach (var voice in _voices)
        {
            _modulation.TickVoice(voice.Id, voice.Channel, voice.Key);
        }
    }

    // The zone's release time, with the reference player's undocumented 0.5 s default filled in when the
    // preset declares no release anywhere (measured, plan section 7 item 4).
    //
    // The resolved model cannot tell "written as zero" from "not written", so the parsed elements decide
    // whether a release was declared at all. A binding that later moves the release off zero is honoured;
    // one that sets it to exactly zero on a preset that declared none reads as "not written" and gets the
    // 0.5 s default. That corner disappears the day the resolved default itself becomes 0.5.
    internal double EffectiveRelease(DecentSamplerZoneRuntime runtime)
    {
        var release = runtime.Zone.Release;

        if (runtime.HasDeclaredRelease || release != 0.0)
        {
            return release;
        }

        return DecentSamplerDefaults.ReleaseSeconds;
    }

    // ---- construction ---------------------------------------------------------------------------

    private void BuildZones(int sharedLength)
    {
        foreach (var zone in _instrument.Zones)
        {
            var runtime = BuildZone(zone);

            runtime.SetEnvelopeFacts(HasDeclaredRelease(zone));

            var explicitLength = zone.SeqLength > 0;
            runtime.SetSequence(
                explicitLength ? zone.Group.Index : -1,
                explicitLength ? zone.SeqLength : sharedLength);

            if (!runtime.IsPlayable)
            {
                continue;
            }

            if (runtime.CcTriggers.Length > 0)
            {
                // A controller-triggered zone answers to CC movement, not to notes - the same rule the
                // SFZ engine applies to on_loccN/on_hiccN regions.
                _controllerZones.Add(runtime);
                continue;
            }

            switch (zone.Trigger)
            {
                case DecentSamplerTrigger.Release:
                    _releaseZones.Add(runtime);
                    break;

                case DecentSamplerTrigger.Continuous:
                    _continuousZones.Add(runtime);
                    break;

                default:
                    _attackZones.Add(runtime);
                    break;
            }
        }

        WarnAboutMixedSequenceLengths(sharedLength);
    }

    private DecentSamplerZoneRuntime BuildZone(DecentSamplerZone zone)
    {
        if (zone.Kind == DecentSamplerZoneKind.Oscillator)
        {
            var waveform = zone.WaveformName ?? DecentSamplerEnumNamesFor(zone.Waveform);

            if (_extensions.TryGetOscillatorFactory(waveform, out var factory))
            {
                var context = new OscillatorContext(_instrument, zone, _sampleRate, waveform);
                return DecentSamplerZoneRuntime.ForOscillator(zone, factory, context);
            }

            AddProblem(DecentSamplerExtensions.MissingOscillatorMessage(waveform));
            _unsupported.Add("oscillator waveform '" + waveform + "'");
            return DecentSamplerZoneRuntime.Silent(zone);
        }

        var source = _instrument.GetSampleSource(zone);

        if (source is InMemorySampleSource memory)
        {
            return DecentSamplerZoneRuntime.ForSample(zone, memory.Data);
        }

        if (source is StreamingSampleSource streaming)
        {
            return DecentSamplerZoneRuntime.ForStreamedSample(zone, streaming);
        }

        // Nothing decoded and nothing streamed: either the instrument was loaded with DecodeSamples
        // off, in which case the first note that wants this file asks for it, or the file was missing
        // and the loader has already said so.
        var deferred = _instrument.GetDeferredSample(zone);

        if (deferred != null)
        {
            return DecentSamplerZoneRuntime.ForDeferredSample(zone, deferred);
        }

        if (source != null)
        {
            AddProblem(
                "sample not decoded in memory, so it cannot play: " + (zone.Path ?? "(no path)"));
        }

        return DecentSamplerZoneRuntime.Silent(zone);
    }

    // Builds the streaming ring buffers, but only when this instrument actually needs them, and hands
    // the whole lot to the shared reader thread. A synthesizer over an ordinary in-memory instrument
    // allocates nothing here and never registers.
    private void BuildStreaming()
    {
        var streams = false;
        var defers = false;

        foreach (var runtime in AllZoneRuntimes())
        {
            // A zone whose file has not been opened yet still counts as streaming when the policy chose
            // to stream it, because it will be the moment its first note arrives.
            streams |= runtime.IsStreaming || runtime.Deferred is { Stream: true };
            defers |= runtime.Deferred != null;
        }

        if (!streams && !defers)
        {
            return;
        }

        _streamingPool = streams
            ? new StreamingVoicePool(_streamingVoiceCount, _streamingRingFrames)
            : null;

        _streamingContext = new DecentSamplerStreamingContext(_instrument, _streamingMode, _streamingPool);

        // Only a real-time synthesizer needs the shared reader thread. An offline one does all its own
        // reading inside RenderBlock, which keeps an offline render single-threaded and therefore
        // exactly reproducible - the same MIDI renders the same samples every time.
        if (_streamingMode == DecentSamplerStreamingMode.RealTime)
        {
            DecentSamplerStreamingReader.Shared.Register(_streamingContext);
        }
    }

    private void WarnAboutMixedSequenceLengths(int sharedLength)
    {
        if (sharedLength <= 1)
        {
            return;
        }

        foreach (var group in _instrument.Groups)
        {
            var highest = 0;
            var usesRoundRobin = false;
            var declaresLength = false;

            foreach (var zone in group.Zones)
            {
                if (zone.SeqMode == DecentSamplerSeqMode.Always)
                {
                    continue;
                }

                usesRoundRobin = true;
                highest = Math.Max(highest, zone.SeqPosition);
                declaresLength |= zone.SeqLength > 0;
            }

            if (usesRoundRobin && !declaresLength && highest < sharedLength)
            {
                // The reference player behaves the same way; this is a note to the library author, not a
                // deviation. Measured, plan section 7 item 9.
                AddProblem(
                    "group " + group.Index + " has round robin positions up to " + highest +
                    " but no seqLength, and another group in the preset declares position " +
                    sharedLength + ": the shared queue length makes some notes silent");
            }
        }
    }

    private static bool HasDeclaredRelease(DecentSamplerZone zone) =>
        zone.Source?.Release != null ||
        zone.Group?.Source?.Release != null ||
        zone.Group?.Groups?.Release != null;

    private static string DecentSamplerEnumNamesFor(DecentSamplerWaveform waveform) =>
        waveform.ToString();

    // ---- note handling ---------------------------------------------------------------------------

    private void CollectAttackCandidates(
        DecentSamplerChannelState state, int key, int velocity, int heldBefore)
    {
        _candidates.Clear();

        foreach (var runtime in _attackZones)
        {
            if (!MatchesTrigger(runtime.Zone.Trigger, heldBefore))
            {
                continue;
            }

            if (!MatchesZone(runtime, state, key, velocity))
            {
                continue;
            }

            if (!MatchesPreviousNote(runtime, key))
            {
                continue;
            }

            _candidates.Add(runtime);
        }
    }

    private static bool MatchesTrigger(DecentSamplerTrigger trigger, int heldBefore) =>
        trigger switch
        {
            DecentSamplerTrigger.First => heldBefore == 0,
            DecentSamplerTrigger.Legato => heldBefore > 0,
            _ => true,
        };

    private bool MatchesZone(
        DecentSamplerZoneRuntime runtime, DecentSamplerChannelState state, int key, int velocity)
    {
        var zone = runtime.Zone;

        if (!runtime.Group.Enabled)
        {
            return false;
        }

        if (key < zone.LoNote || key > zone.HiNote)
        {
            return false;
        }

        if (velocity < zone.LoVel || velocity > zone.HiVel)
        {
            return false;
        }

        var tags = runtime.Tags;
        for (var i = 0; i < tags.Length; i++)
        {
            if (!_tagState.IsEnabled(tags[i]))
            {
                return false;
            }
        }

        var filters = runtime.CcFilters;
        for (var i = 0; i < filters.Length; i++)
        {
            if (!filters[i].Contains(state.GetController(filters[i].Controller)))
            {
                return false;
            }
        }

        return true;
    }

    // previousNotes and legatoInterval both test the last note triggered anywhere in the instrument.
    // legatoInterval is the distance FROM the previous note TO this one: the guide's example, "a C3 with
    // legatoInterval -2 plays only after a D3", is key - previous == interval.
    private bool MatchesPreviousNote(DecentSamplerZoneRuntime runtime, int key)
    {
        var previousNotes = runtime.PreviousNotes;
        if (previousNotes.Length > 0)
        {
            if (_lastTriggeredNote < 0)
            {
                return false;
            }

            var found = false;
            for (var i = 0; i < previousNotes.Length; i++)
            {
                if (previousNotes[i] == _lastTriggeredNote)
                {
                    found = true;
                    break;
                }
            }

            if (!found)
            {
                return false;
            }
        }

        var interval = runtime.Zone.LegatoInterval;
        if (interval.HasValue)
        {
            if (_lastTriggeredNote < 0 || key - _lastTriggeredNote != interval.Value)
            {
                return false;
            }
        }

        return true;
    }

    private void ApplyRoundRobin()
    {
        if (_candidates.Count == 0)
        {
            return;
        }

        // Every register a candidate draws from advances exactly once per note-on, whichever zone got
        // there first - the reference player's shared position register.
        foreach (var runtime in _candidates)
        {
            var mode = runtime.Zone.SeqMode;
            if (mode == DecentSamplerSeqMode.Always)
            {
                continue;
            }

            var index = RegisterIndex(runtime);
            if (_registerStamps[index] == _noteStamp)
            {
                continue;
            }

            _registerStamps[index] = _noteStamp;
            _registers[index].Advance(mode, _random);
        }

        for (var i = _candidates.Count - 1; i >= 0; i--)
        {
            var runtime = _candidates[i];
            if (runtime.Zone.SeqMode == DecentSamplerSeqMode.Always)
            {
                continue;
            }

            if (runtime.Zone.SeqPosition != _registers[RegisterIndex(runtime)].Position)
            {
                _candidates.RemoveAt(i);
            }
        }
    }

    private int RegisterIndex(DecentSamplerZoneRuntime runtime) =>
        runtime.SequenceRegisterKey < 0 ? 0 : runtime.SequenceRegisterKey + 1;

    // silencedByTags: when a zone with tag T starts, every voice that was already sounding and lists T
    // in its own silencedByTags stops. HOW it stops is the silenced voice's business - silencingDecay
    // first, then silencingMode - which is how the guide's legato example gives the sustain layer a long
    // fade while the hi-hat example cuts dead.
    private void SilenceForTags(DecentSamplerZoneRuntime starting)
    {
        var tags = starting.Tags;
        if (tags.Length == 0)
        {
            return;
        }

        foreach (var voice in _voices)
        {
            if (voice.StartStamp >= _noteStamp)
            {
                continue;
            }

            var silencedBy = voice.Runtime.SilencedByTags;
            if (silencedBy.Length == 0)
            {
                continue;
            }

            var hit = false;
            for (var i = 0; i < silencedBy.Length && !hit; i++)
            {
                for (var j = 0; j < tags.Length; j++)
                {
                    if (string.Equals(silencedBy[i], tags[j], StringComparison.OrdinalIgnoreCase))
                    {
                        hit = true;
                        break;
                    }
                }
            }

            if (hit)
            {
                Silence(voice);
            }
        }
    }

    // TAG_POLYPHONY: a tag may cap how many of its voices sound at once. The oldest goes, silenced by
    // the same rules voice muting uses.
    private void EnforceTagPolyphony(DecentSamplerZoneRuntime starting)
    {
        var tags = starting.Tags;

        for (var t = 0; t < tags.Length; t++)
        {
            var limit = _tagState.GetPolyphony(tags[t]);
            if (limit < 0)
            {
                continue;
            }

            while (true)
            {
                var count = 0;
                DecentSamplerVoice oldest = null;

                foreach (var voice in _voices)
                {
                    if (voice.IsReleased || !CarriesTag(voice.Runtime, tags[t]))
                    {
                        continue;
                    }

                    count++;
                    if (oldest == null || voice.StartStamp < oldest.StartStamp)
                    {
                        oldest = voice;
                    }
                }

                if (count < Math.Max(1, limit) || oldest == null)
                {
                    break;
                }

                Silence(oldest);
            }
        }
    }

    private static bool CarriesTag(DecentSamplerZoneRuntime runtime, string tag)
    {
        var tags = runtime.Tags;
        for (var i = 0; i < tags.Length; i++)
        {
            if (string.Equals(tags[i], tag, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static void Silence(DecentSamplerVoice voice)
    {
        var zone = voice.Runtime.Zone;

        if (zone.SilencingDecay > 0.0)
        {
            voice.SilenceTimed(zone.SilencingDecay);
        }
        else if (zone.SilencingMode == DecentSamplerSilencingMode.Normal)
        {
            voice.SilenceNormal();
        }
        else
        {
            voice.SilenceFast();
        }
    }

    // Measured (plan section 7 item 10b): glideMode defaults to legato, glideTime is the TOTAL time of
    // the transition in seconds whatever the interval, and "always" takes its start pitch from the last
    // note triggered ANYWHERE in the instrument - a global register, not a per-group one.
    private double GlideOffset(
        DecentSamplerZoneRuntime runtime, int key, int heldBefore, out double glideSeconds)
    {
        glideSeconds = 0.0;

        var zone = runtime.Zone;
        if (zone.GlideTime <= 0.0 || zone.GlideMode == DecentSamplerGlideMode.Off)
        {
            return 0.0;
        }

        if (_lastTriggeredNote < 0)
        {
            return 0.0;
        }

        if (zone.GlideMode == DecentSamplerGlideMode.Legato && heldBefore == 0)
        {
            return 0.0;
        }

        glideSeconds = zone.GlideTime;
        return _lastTriggeredNote - key;
    }

    private void StartVoice(
        DecentSamplerZoneRuntime runtime,
        int channel,
        int key,
        int velocity,
        float triggerGain,
        double glideSemitones,
        double glideSeconds,
        int frameOffset = 0)
    {
        // A zone whose file has not been decoded yet: ask for it and let this one note go. The worker
        // that decodes it records the miss, so nothing is allocated or reported from here.
        if (runtime.Deferred != null && !runtime.TryResolveDeferred())
        {
            _instrument.RequestDecode(runtime.Deferred);
            return;
        }

        var voice = _voices.RequestNew();
        if (voice == null)
        {
            return;
        }

        // The zone's own delay, plus where inside this block the note belongs. The voice honours a
        // sub-block delay exactly, so a generated note starts on the right frame.
        var delayFrames = TimeToFrames(runtime.Zone.Delay, runtime.Zone.DelayUnit) + frameOffset;

        voice.Start(
            runtime, channel, key, velocity, triggerGain, delayFrames,
            glideSemitones, glideSeconds, _noteStamp);

        _modulation?.VoiceStarted(voice.Id, channel, key, velocity);
    }

    private void CompleteNoteOff(
        int channel, DecentSamplerChannelState state, int key, long releaseFrame)
    {
        foreach (var voice in _voices)
        {
            if (voice.Channel == channel && voice.Key == key &&
                !voice.IsReleaseTrigger && !voice.IsContinuous)
            {
                voice.End();
            }
        }

        for (var i = _retriggers.Count - 1; i >= 0; i--)
        {
            if (_retriggers[i].Channel == channel && _retriggers[i].Key == key)
            {
                _retriggers.RemoveAt(i);
            }
        }

        // MEASURED: a key with no attack zone anywhere never fires a release trigger, however many
        // trigger="release" zones cover it.
        if (_releaseZones.Count == 0 || !state.DidStartVoice(key))
        {
            return;
        }

        var velocity = Math.Max(1, state.NoteOnVelocity(key));
        var heldSeconds = Math.Max(0.0, (releaseFrame - state.NoteOnFrame(key)) / (double)_sampleRate);

        _noteStamp++;

        foreach (var runtime in _releaseZones)
        {
            if (!MatchesZone(runtime, state, key, velocity))
            {
                continue;
            }

            StartVoice(runtime, channel, key, velocity, ReleaseTriggerGain(runtime.Zone, heldSeconds), 0.0, 0.0);
        }
    }

    // Measured (plan section 7 item 10a): the decibel form is a rate in dB per second of hold, its sign
    // ignored; the LINEAR form is the FRACTION LOST per second, so gain = (1 - value)^heldSeconds, not
    // value^heldSeconds - reading the guide the other way is wrong by a factor of three.
    private static float ReleaseTriggerGain(DecentSamplerZone zone, double heldSeconds)
    {
        var decay = zone.ReleaseTriggerDecay;

        if (decay == 0.0)
        {
            return 1f;
        }

        if (zone.ReleaseTriggerDecayInDecibels)
        {
            return (float)Math.Pow(10.0, -Math.Abs(decay) * heldSeconds / 20.0);
        }

        var fraction = Math.Clamp(decay, 0.0, 1.0);
        if (fraction <= 0.0)
        {
            return 1f;
        }

        if (fraction >= 1.0)
        {
            return heldSeconds <= 0.0 ? 1f : 0f;
        }

        return (float)Math.Pow(1.0 - fraction, heldSeconds);
    }

    private void SetController(int channel, DecentSamplerChannelState state, int controller, int value)
    {
        var previous = state.GetController(controller);
        state.SetController(controller, value);

        // The <midi><cc> handlers. They fire on a CHANGE of the controller's value only, and every
        // controller starts at 0 - both measured against the reference player. The instrument's own
        // binding engine keeps that stored value, so the check lives there rather than here.
        _sequencing?.HandleControlChange(channel, controller, value);

        if (controller == SustainController && previous >= 64 && value < 64)
        {
            ReleaseSustainedKeys(channel, state);
        }

        if (_controllerZones.Count == 0 || previous == value)
        {
            return;
        }

        // Measured behaviour worth copying: the reference player fires a controller binding on CHANGE,
        // never on a repeat of the value it already holds, and every controller starts at 0.
        var fired = false;

        foreach (var runtime in _controllerZones)
        {
            var triggers = runtime.CcTriggers;
            var entered = false;

            for (var i = 0; i < triggers.Length; i++)
            {
                if (triggers[i].Controller == controller &&
                    !triggers[i].Contains(previous) &&
                    triggers[i].Contains(value))
                {
                    entered = true;
                    break;
                }
            }

            if (!entered)
            {
                continue;
            }

            if (!fired)
            {
                _noteStamp++;
                fired = true;
            }

            // A controller has no note of its own, so the zone's root note stands in for one.
            // MEASURED (round 2, item 25): a controller-triggered zone plays at FULL velocity whatever
            // the controller's own value is - it is a trigger, not a velocity. The zone's NOTE is still
            // unmeasured; the root note is what the SFZ engine substitutes and what is used here.
            var key = Math.Clamp(runtime.Zone.RootNote, 0, 127);
            const int velocity = 127;

            if (!MatchesZone(runtime, state, key, velocity))
            {
                continue;
            }

            StartVoice(runtime, channel, key, velocity, 1f, 0.0, 0.0);
        }
    }

    private void ReleaseSustainedKeys(int channel, DecentSamplerChannelState state)
    {
        for (var key = 0; key < 128; key++)
        {
            if (!state.IsKeySustained(key))
            {
                continue;
            }

            state.ClearSustainedKey(key);

            // MEASURED: the hold that releaseTriggerDecay reads runs to the PEDAL LIFT, which is now.
            CompleteNoteOff(channel, state, key, _renderedFrames);
        }
    }

    // ---- rendering ---------------------------------------------------------------------------------

    private void RenderBlock()
    {
        if (_streamingMode == DecentSamplerStreamingMode.Offline)
        {
            // Offline: this thread may read files, so a sample a note asked for is decoded here rather
            // than on the reader thread, which makes an offline render deterministic.
            _streamingContext?.Service();
        }

        StartContinuousZones();
        AdvanceRetriggers();
        TickModulators();

        // The note sequencer and the arpeggiator, after the modulators so that a modulated ARP_*
        // parameter is current, and before the voices so that a note either of them emits sounds in
        // this very block at its own frame offset.
        _sequencing?.Tick(_renderedFrames, _blockSize);

        _voices.Process();

        _mixer.BeginBlock();

        foreach (var voice in _voices)
        {
            var sourceLeft = voice.BlockLeft;
            var sourceRight = voice.BlockRight;

            for (var output = 0; output < 8; output++)
            {
                var slot = voice.OutputSlot(output);
                if (slot < 0)
                {
                    continue;
                }

                var outputVolume = voice.OutputVolume(output);
                if (outputVolume <= 0f)
                {
                    continue;
                }

                _mixer.Touch(slot);

                WriteBlock(
                    voice.PreviousMixGainLeft * outputVolume,
                    voice.CurrentMixGainLeft * outputVolume,
                    sourceLeft,
                    _mixer.Left(slot));

                WriteBlock(
                    voice.PreviousMixGainRight * outputVolume,
                    voice.CurrentMixGainRight * outputVolume,
                    sourceRight,
                    _mixer.Right(slot));
            }
        }

        // Bus chains, bus routing and the instrument chain. Everything that reaches the main output is
        // in slot 0 afterwards, and each auxiliary pair is in its own slot.
        _mixer.FinishBlock();

        Array.Clear(_blockLeft, 0, _blockSize);
        Array.Clear(_blockRight, 0, _blockSize);

        if (_mixer.IsTouched(DecentSamplerOutputSlot.MainIndex))
        {
            ArrayMath.MultiplyAdd(_masterVolume, _mixer.Left(DecentSamplerOutputSlot.MainIndex), _blockLeft);
            ArrayMath.MultiplyAdd(_masterVolume, _mixer.Right(DecentSamplerOutputSlot.MainIndex), _blockRight);
        }

        for (var i = 0; i < _auxLeft.Length; i++)
        {
            var slot = DecentSamplerOutputSlot.FirstAuxIndex + i;

            Array.Clear(_auxLeft[i], 0, _blockSize);
            Array.Clear(_auxRight[i], 0, _blockSize);

            if (!_mixer.IsTouched(slot))
            {
                continue;
            }

            ArrayMath.MultiplyAdd(_masterVolume, _mixer.Left(slot), _auxLeft[i]);
            ArrayMath.MultiplyAdd(_masterVolume, _mixer.Right(slot), _auxRight[i]);
        }

        _renderedFrames += _blockSize;
    }

    // One group's <effects> becomes one pool, shared by every zone of that group; each voice rents its
    // own chain from it. Building the first instance here rather than at the first note-on means the
    // effect types a group asks for are resolved, and reported, at load time.
    private void BuildGroupChains()
    {
        foreach (var group in _instrument.Groups)
        {
            var element = group.Effects;

            if (element == null || element.Effects.Count == 0)
            {
                continue;
            }

            var pool = new DecentSamplerGroupChainPool(
                element, _instrument, _extensions, group.Index, _sampleRate, _blockSize, _tempoSource,
                _problems, _unsupported);

            pool.Warm();

            foreach (var runtime in AllZoneRuntimes())
            {
                if (ReferenceEquals(runtime.Group, group))
                {
                    runtime.SetGroupChains(pool);
                }
            }
        }
    }

    private IEnumerable<DecentSamplerZoneRuntime> AllZoneRuntimes()
    {
        foreach (var runtime in _attackZones)
        {
            yield return runtime;
        }

        foreach (var runtime in _releaseZones)
        {
            yield return runtime;
        }

        foreach (var runtime in _controllerZones)
        {
            yield return runtime;
        }

        foreach (var runtime in _continuousZones)
        {
            yield return runtime;
        }
    }

    // trigger="continuous" zones start when the instrument does and loop for as long as it plays.
    private void StartContinuousZones()
    {
        if (_continuousStarted || _continuousZones.Count == 0)
        {
            return;
        }

        _continuousStarted = true;
        _noteStamp++;

        foreach (var runtime in _continuousZones)
        {
            var key = Math.Clamp(runtime.Zone.RootNote, 0, 127);
            StartVoice(runtime, 0, key, 127, 1f, 0.0, 0.0);
        }
    }

    // retriggerEnabled: once the initial delay sequence has run, the whole pattern repeats every
    // retriggerInterval for as long as the key is held. The interval follows the tempo when its unit is
    // beats, so a pattern stays in time with the transport.
    //
    // AWAITING MEASUREMENT: the guide says only that "all samples in the group will retrigger according
    // to the retriggerInterval" after "the initial delay sequence completes". Read here as: the first
    // repeat lands one interval after the note-on, each repeat replays every retrigger-enabled zone with
    // its own delay, and a note-off ends the pattern. Whether the reference player instead measures the
    // first interval from the END of the longest delay, and whether it keeps going after the key is
    // released, has not been recorded.
    private void AdvanceRetriggers()
    {
        if (_retriggers.Count == 0)
        {
            return;
        }

        for (var i = _retriggers.Count - 1; i >= 0; i--)
        {
            var schedule = _retriggers[i];

            if (_renderedFrames < schedule.NextFrame)
            {
                continue;
            }

            _noteStamp++;

            foreach (var runtime in _attackZones)
            {
                if (!runtime.Zone.RetriggerEnabled)
                {
                    continue;
                }

                var state = _channels[schedule.Channel];
                if (!MatchesZone(runtime, state, schedule.Key, schedule.Velocity))
                {
                    continue;
                }

                StartVoice(runtime, schedule.Channel, schedule.Key, schedule.Velocity, 1f, 0.0, 0.0);
            }

            // Re-read the interval each time so a tempo change moves the pattern with it.
            var interval = Math.Max(1, schedule.IntervalFrames);
            schedule.NextFrame += interval;
            _retriggers[i] = schedule;
        }
    }

    private void WriteBlock(float previousGain, float currentGain, float[] source, float[] destination)
    {
        if (Math.Max(previousGain, currentGain) < SoundFontMath.NonAudible)
        {
            return;
        }

        if (MathF.Abs(currentGain - previousGain) < 1.0E-3f)
        {
            ArrayMath.MultiplyAdd(currentGain, source, destination);
        }
        else
        {
            var step = _inverseBlockSize * (currentGain - previousGain);
            ArrayMath.MultiplyAdd(previousGain, step, source, destination);
        }
    }

    private long TimeToFrames(double amount, DecentSamplerTimeUnit unit)
    {
        if (amount <= 0.0)
        {
            return 0;
        }

        switch (unit)
        {
            case DecentSamplerTimeUnit.Samples:
                // AWAITING MEASUREMENT: read as frames at the OUTPUT rate, which is what
                // "sample-accurate delay" means for a timeline. A 96 kHz library whose author counted
                // frames in the source file would want the source rate instead.
                return (long)Math.Round(amount);

            case DecentSamplerTimeUnit.Beats:
            {
                var bpm = _tempoSource.BeatsPerMinute;
                if (!(bpm > 0.0))
                {
                    bpm = TempoSource.DefaultBeatsPerMinute;
                }

                return (long)Math.Round(amount * 60.0 / bpm * _sampleRate);
            }

            default:
                return (long)Math.Round(amount * _sampleRate);
        }
    }

    private static double ClampVolume(double volume) =>
        volume > 0.0 ? Math.Min(volume, MaximumVolume) : 0.0;

    private void AddProblem(string problem)
    {
        if (!_problems.Contains(problem))
        {
            _problems.Add(problem);
        }
    }

    private struct RetriggerSchedule
    {
        public int Channel;
        public int Key;
        public int Velocity;
        public long NextFrame;
        public long IntervalFrames;
    }
}
