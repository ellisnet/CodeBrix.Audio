using System;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.ModestSynth.Effects;
using CodeBrix.Audio.ModestSynth.Internal.Gm;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler.Engine;

namespace CodeBrix.Audio.ModestSynth;

/// <summary>
/// A MULTI-TIMBRAL General MIDI synthesizer: sixteen channels, each playing one of the 128 General
/// MIDI programs, with the percussion kit on channel 10 - the whole General MIDI Level 1 sound set
/// from code, with no SoundFont and no recorded samples anywhere.
/// </summary>
/// <remarks>
/// <para>
/// IT IS NOT <see cref="ModestSynthesizer" />, and it does not replace it. That one plays ONE
/// <see cref="Patch.ModestPatch" /> on every channel and ignores program change, which is what you
/// want when you are designing a sound. This one honours program change, gives every channel its own
/// voicing out of the bank, and answers the controllers a General MIDI file actually sends - so it
/// is what you want when you have a <c>.mid</c> and nothing else.
/// </para>
/// <para>
/// WHAT IT ANSWERS. Note on and note off with velocity; program change (0xC0); pitch bend over a
/// range CC&#160;101/100 plus CC&#160;6/38 can set; and the controllers General MIDI files use -
/// CC&#160;1 modulation, CC&#160;7 channel volume, CC&#160;10 pan, CC&#160;11 expression, CC&#160;64
/// sustain, CC&#160;91 reverb send, CC&#160;93 chorus send, CC&#160;120 all sound off, CC&#160;121
/// reset all controllers and CC&#160;123 all notes off. Bank select (CC&#160;0 and CC&#160;32) is
/// accepted and ignored: General MIDI Level 1 has one bank.
/// </para>
/// <para>
/// THE PERCUSSION CHANNEL is <see cref="GeneralMidi.PercussionChannel" />. On it a note number does
/// not mean a pitch: it CHOOSES A KIT PIECE, each with its own voicing, pan, level and tuning. A kit
/// piece is a ONE-SHOT - it ignores note-off and runs to its natural end - and the pieces that cannot
/// physically sound together cut each other off, so a closed or pedal hi-hat silences an open one.
/// </para>
/// <para>
/// ONE VOICE POOL serves all sixteen channels, so sixteen parts do not mean sixteen allocators and
/// the polyphony limit means what it says. When the pool is full the quietest released voice goes
/// first, then the oldest sounding one.
/// </para>
/// <para>
/// THE CHANNEL NUMBERING TRAP. Every member here that takes a channel counts 1 to 16, the way
/// <see cref="MidiEvent.Channel" /> and <see cref="GeneralMidi.PercussionChannel" /> do - EXCEPT
/// <see cref="ProcessMidiMessage" />, whose channel is the WIRE number 0 to 15, because that is what
/// <c>MidiSequencer</c> and <c>MidiStreamSequencer</c> hand every synthesizer. Percussion is
/// channel 10 here and wire channel 9 there.
/// </para>
/// <para>
/// HEADROOM. The bank is calibrated so that all 128 programs are comparably loud and so that ONE
/// note at full velocity fits inside full scale at the family's default
/// <see cref="MasterVolume" /> of 0.5. THIRTY notes at once do not, the way thirty notes at once on
/// a hardware module do not: turn <see cref="MasterVolume" /> down for a dense arrangement, or mix
/// the result with headroom of your own. Nothing here limits or compresses, because a synthesizer
/// that quietly changed its own level would make a rendition impossible to balance.
/// </para>
/// <para>
/// Rendering allocates nothing. The first few notes of each program build that program's oscillators,
/// which are then recycled forever; a program change may build an insert effect. Nothing here is
/// thread-safe: MIDI events and rendering must not overlap, which is the contract every synthesizer
/// in this family follows.
/// </para>
/// </remarks>
/// <example>
/// <code>
/// var synthesizer = new GeneralMidiSynthesizer(44100);
///
/// // Play a .mid through it - it honours the file's own program changes.
/// var samples = SoundFontRenderer.Render(synthesizer, sequence, TimeSpan.FromSeconds(2));
///
/// // Or voice the parts yourself.
/// synthesizer.SetProgram(1, (int)GeneralMidiProgram.Celesta);
/// synthesizer.SetProgram(2, (int)GeneralMidiProgram.ChoirAahs);
/// </code>
/// </example>
public sealed class GeneralMidiSynthesizer : IMidiSynthesizer
{
    /// <summary>The number of MIDI channels, which is always sixteen.</summary>
    public const int ChannelCount = 16;

    /// <summary><see cref="PinnedProgram" /> when the synthesizer is not pinned to one program.</summary>
    public const int NotPinned = -1;

    /// <summary><see cref="PinnedProgram" /> when the synthesizer is pinned to the percussion kit.</summary>
    public const int PinnedToPercussion = -2;

    private const int PercussionWireChannel = GeneralMidi.PercussionChannel - 1;
    private const int PercussionRuntimeCount =
        GeneralMidi.HighestPercussionNote - GeneralMidi.LowestPercussionNote + 1;

    private readonly GeneralMidiSynthesizerSettings settings;
    private readonly GmChannel[] channels = new GmChannel[ChannelCount];
    private readonly GmVoice[] voices;
    private readonly GmProgramRuntime[] programRuntimes = new GmProgramRuntime[GeneralMidi.ProgramCount];
    private readonly GmProgramRuntime[] percussionRuntimes = new GmProgramRuntime[PercussionRuntimeCount];

    private readonly float[] blockLeft;
    private readonly float[] blockRight;
    private readonly float[] channelLeft;
    private readonly float[] channelRight;
    private readonly float[] voiceLeft;
    private readonly float[] voiceRight;
    private readonly int blockSize;
    private readonly int pinnedProgram;

    private readonly Reverb reverb;
    private readonly Chorus chorus;
    private readonly float[] reverbInput;
    private readonly float[] reverbLeft;
    private readonly float[] reverbRight;
    private readonly float[] chorusInputLeft;
    private readonly float[] chorusInputRight;
    private readonly float[] chorusLeft;
    private readonly float[] chorusRight;

    private int blockRead;
    private long stamp;
    private float masterVolume;

    /// <summary>Creates a multi-timbral General MIDI synthesizer at a sample rate.</summary>
    /// <param name="sampleRate">Samples per second, 8,000 to 192,000.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate" /> is outside that range.</exception>
    public GeneralMidiSynthesizer(int sampleRate)
        : this(new GeneralMidiSynthesizerSettings(sampleRate), NotPinned)
    {
    }

    /// <summary>Creates a multi-timbral General MIDI synthesizer with settings of your own.</summary>
    /// <param name="settings">How to play it. Copied, so later changes to it have no effect.</param>
    /// <exception cref="ArgumentNullException"><paramref name="settings" /> is null.</exception>
    public GeneralMidiSynthesizer(GeneralMidiSynthesizerSettings settings)
        : this(settings, NotPinned)
    {
    }

    private GeneralMidiSynthesizer(GeneralMidiSynthesizerSettings settings, int pinned)
    {
        if (settings == null) { throw new ArgumentNullException(nameof(settings)); }

        this.settings = settings.Clone();
        pinnedProgram = pinned;

        blockSize = this.settings.BlockSize;
        masterVolume = this.settings.MasterVolume;

        blockLeft = new float[blockSize];
        blockRight = new float[blockSize];
        channelLeft = new float[blockSize];
        channelRight = new float[blockSize];
        voiceLeft = new float[blockSize];
        voiceRight = new float[blockSize];
        blockRead = blockSize;

        voices = new GmVoice[this.settings.MaximumPolyphony];
        for (int i = 0; i < voices.Length; i++)
        {
            voices[i] = new GmVoice(this.settings.SampleRate, blockSize);
        }

        for (int i = 0; i < channels.Length; i++)
        {
            channels[i] = new GmChannel();
        }

        if (this.settings.EnableReverbAndChorus)
        {
            reverb = new Reverb(this.settings.SampleRate);
            reverbInput = new float[blockSize];
            reverbLeft = new float[blockSize];
            reverbRight = new float[blockSize];

            // The same mild widening the SoundFont engine uses: a 2 ms delay, a 1.9 ms depth and a
            // 0.4 Hz sweep. A General MIDI bank wants chorus to be felt rather than heard.
            chorus = new Chorus(this.settings.SampleRate, 0.002, 0.0019, 0.4);
            chorusInputLeft = new float[blockSize];
            chorusInputRight = new float[blockSize];
            chorusLeft = new float[blockSize];
            chorusRight = new float[blockSize];
        }

        Adjustments = new GeneralMidiAdjustments();

        ResetChannels();
    }

    /// <summary>
    /// Creates a synthesizer PINNED to one program: it plays that program on every one of the
    /// sixteen channels and ignores any program change the music sends.
    /// </summary>
    /// <param name="program">The General MIDI program, 0 to 127.</param>
    /// <param name="sampleRate">Samples per second, 8,000 to 192,000.</param>
    /// <returns>The synthesizer.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The program is outside 0 to 127, or the rate is unsupported.</exception>
    /// <remarks>
    /// This is the PER-PART shape an instrument library hands back. A part exists because the CALLER
    /// decided what it sounds like, and a generated piece usually carries program changes of its own;
    /// letting them through would silently re-voice a part that was voiced deliberately. It plays on
    /// every channel because a router forwards whatever channel the music used.
    /// </remarks>
    public static GeneralMidiSynthesizer CreateForProgram(int program, int sampleRate) =>
        CreateForProgram(program, new GeneralMidiSynthesizerSettings(sampleRate));

    /// <summary>
    /// Creates a synthesizer pinned to one program, with settings of your own.
    /// </summary>
    /// <param name="program">The General MIDI program, 0 to 127.</param>
    /// <param name="settings">How to play it. Copied, so later changes to it have no effect.</param>
    /// <returns>The synthesizer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="settings" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="program" /> is outside 0 to 127.</exception>
    public static GeneralMidiSynthesizer CreateForProgram(int program, GeneralMidiSynthesizerSettings settings)
    {
        if (program < 0 || program >= GeneralMidi.ProgramCount)
        {
            throw new ArgumentOutOfRangeException(nameof(program), program,
                "A General MIDI program is 0 to 127.");
        }

        return new GeneralMidiSynthesizer(settings, program);
    }

    /// <summary>
    /// Creates a synthesizer PINNED to the percussion kit: every channel plays the kit, so the drums
    /// sound wherever a router puts them rather than only on channel 10.
    /// </summary>
    /// <param name="sampleRate">Samples per second, 8,000 to 192,000.</param>
    /// <returns>The synthesizer.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate" /> is outside the supported range.</exception>
    public static GeneralMidiSynthesizer CreateForPercussion(int sampleRate) =>
        CreateForPercussion(new GeneralMidiSynthesizerSettings(sampleRate));

    /// <summary>
    /// Creates a synthesizer pinned to the percussion kit, with settings of your own.
    /// </summary>
    /// <param name="settings">How to play it. Copied, so later changes to it have no effect.</param>
    /// <returns>The synthesizer.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="settings" /> is null.</exception>
    public static GeneralMidiSynthesizer CreateForPercussion(GeneralMidiSynthesizerSettings settings) =>
        new GeneralMidiSynthesizer(settings, PinnedToPercussion);

    /// <inheritdoc />
    public int SampleRate => settings.SampleRate;

    /// <inheritdoc />
    public int BlockSize => blockSize;

    /// <summary>How many voices may sound at once, across every channel together.</summary>
    public int MaximumPolyphony => voices.Length;

    /// <summary>The number of MIDI channels, which is always sixteen.</summary>
    public int Channels => ChannelCount;

    /// <summary>Whether the shared reverb and chorus buses are running.</summary>
    public bool ReverbAndChorusEnabled => reverb != null;

    /// <summary>
    /// Whether this synthesizer is pinned to one voicing and therefore ignores program change.
    /// </summary>
    public bool IsPinned => pinnedProgram != NotPinned;

    /// <summary>
    /// The program this synthesizer is pinned to: 0 to 127, <see cref="NotPinned" /> when it is
    /// multi-timbral, or <see cref="PinnedToPercussion" /> when it is pinned to the kit.
    /// </summary>
    public int PinnedProgram => pinnedProgram;

    /// <summary>
    /// The per-program adjustments applied on top of the bank - level, brightness, attack, release,
    /// vibrato depth, reverb send and pan, per program and per kit piece.
    /// </summary>
    /// <remarks>
    /// They are read when a note STARTS, so a change reaches the next note rather than the one
    /// already sounding, and they belong to the PROGRAM rather than to the channel, so they survive
    /// a program change.
    /// </remarks>
    public GeneralMidiAdjustments Adjustments { get; }

    /// <inheritdoc />
    public int ActiveVoiceCount
    {
        get
        {
            int active = 0;

            for (int i = 0; i < voices.Length; i++)
            {
                if (voices[i].IsActive) { active++; }
            }

            return active;
        }
    }

    /// <inheritdoc />
    public float MasterVolume
    {
        get => masterVolume;
        set => masterVolume = float.IsNaN(value) ? masterVolume : value < 0f ? 0f : value;
    }

    /// <summary>The General MIDI program a channel is playing.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <returns>The program number, 0 to 127. A percussion channel reports 0, because the kit is not a program.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel" /> is outside 1 to 16.</exception>
    public int GetProgram(int channel) => ChannelAt(channel).Program;

    /// <summary>Chooses the General MIDI program a channel plays.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="program">The program number, 0 to 127.</param>
    /// <exception cref="ArgumentOutOfRangeException">The channel is outside 1 to 16, or the program outside 0 to 127.</exception>
    /// <exception cref="InvalidOperationException">This synthesizer is pinned to one voicing.</exception>
    /// <remarks>
    /// Setting a program on the percussion channel takes that channel OFF the kit and gives it the
    /// melodic program, which is what a file asking for a melodic part on channel 10 means.
    /// </remarks>
    public void SetProgram(int channel, int program)
    {
        if (program < 0 || program >= GeneralMidi.ProgramCount)
        {
            throw new ArgumentOutOfRangeException(nameof(program), program,
                "A General MIDI program is 0 to 127.");
        }

        if (IsPinned)
        {
            throw new InvalidOperationException(
                "This synthesizer is pinned to one voicing and plays it on every channel, so its " +
                "program cannot be changed. Build a multi-timbral GeneralMidiSynthesizer when the " +
                "program has to move.");
        }

        GmChannel state = ChannelAt(channel);
        state.IsPercussion = false;
        ApplyProgram(state, program);
    }

    /// <summary>Whether a channel is playing the percussion kit rather than a melodic program.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <returns><see langword="true" /> when note numbers on that channel choose kit pieces.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel" /> is outside 1 to 16.</exception>
    public bool IsPercussionChannel(int channel) => ChannelAt(channel).IsPercussion;

    /// <summary>Makes a channel play the percussion kit, or a melodic program.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="percussion">Whether note numbers on it choose kit pieces.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel" /> is outside 1 to 16.</exception>
    /// <exception cref="InvalidOperationException">This synthesizer is pinned to one voicing.</exception>
    public void SetPercussionChannel(int channel, bool percussion)
    {
        if (IsPinned)
        {
            throw new InvalidOperationException(
                "This synthesizer is pinned to one voicing and plays it on every channel, so its " +
                "program cannot be changed. Build a multi-timbral GeneralMidiSynthesizer when the " +
                "program has to move.");
        }

        GmChannel state = ChannelAt(channel);
        state.IsPercussion = percussion;

        if (percussion)
        {
            SetInsert(state, null);
        }
        else
        {
            ApplyProgram(state, state.Program);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// <paramref name="channel" /> is the WIRE channel, 0 to 15 - what a sequencer hands every
    /// synthesizer - so percussion arrives here as channel 9, not 10. A program change that arrives
    /// here on the percussion channel does NOT take that channel off the kit, so a MIDI file keeps
    /// its drums; only a deliberate call to <see cref="SetProgram" /> or
    /// <see cref="SetPercussionChannel" /> does that.
    /// </remarks>
    public void ProcessMidiMessage(int channel, int command, int data1, int data2)
    {
        if (channel < 0 || channel >= ChannelCount) { return; }

        switch (command)
        {
            case 0x80:
                NoteOffWire(channel, data1);
                break;

            case 0x90:
                NoteOnWire(channel, data1, data2);
                break;

            case 0xB0:
                ProcessController(channel, data1, data2);
                break;

            case 0xC0:
                // A pinned synthesizer was voiced by its caller and must not re-voice itself.
                if (!IsPinned && data1 >= 0 && data1 < GeneralMidi.ProgramCount)
                {
                    GmChannel state = channels[channel];
                    if (!state.IsPercussion) { ApplyProgram(state, data1); }
                    else { state.Program = data1; }
                }

                break;

            case 0xE0:
                channels[channel].BendSemitones =
                    (((data2 << 7) | data1) - 8192) / 8192.0 * channels[channel].BendRange;
                break;
        }
    }

    /// <summary>Starts a note. A velocity of zero is a note-off, as MIDI requires.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="key">The MIDI note number, 0 to 127. On a percussion channel it chooses a kit piece.</param>
    /// <param name="velocity">The velocity, 0 to 127.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel" /> is outside 1 to 16.</exception>
    public void NoteOn(int channel, int key, int velocity)
    {
        ChannelAt(channel);
        NoteOnWire(channel - 1, key, velocity);
    }

    /// <summary>Releases a note. It keeps sounding if the sustain pedal is down, and a kit piece ignores it.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="key">The MIDI note number, 0 to 127.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel" /> is outside 1 to 16.</exception>
    public void NoteOff(int channel, int key)
    {
        ChannelAt(channel);
        NoteOffWire(channel - 1, key);
    }

    /// <inheritdoc />
    public void NoteOffAll(bool immediate)
    {
        for (int i = 0; i < voices.Length; i++)
        {
            GmVoice voice = voices[i];
            if (!voice.IsActive) { continue; }

            if (immediate) { voice.Kill(); }
            else { voice.Release(force: true); }
        }
    }

    /// <summary>Stops every note on one channel.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="immediate">
    /// When true the voices are choked in five milliseconds; when false they run their release.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel" /> is outside 1 to 16.</exception>
    public void NoteOffAll(int channel, bool immediate)
    {
        ChannelAt(channel);
        NoteOffAllWire(channel - 1, immediate);
    }

    /// <inheritdoc />
    /// <remarks>
    /// Voices stop, controllers go back to the General MIDI defaults, and the reverb and chorus tails
    /// are cleared. The PROGRAMS go back to their starting point too: program 0 everywhere and the
    /// kit on channel 10, or the pinned voicing on every channel. The per-program
    /// <see cref="Adjustments" /> are configuration rather than state and are left alone.
    /// </remarks>
    public void Reset()
    {
        for (int i = 0; i < voices.Length; i++) { voices[i].Stop(); }

        ResetChannels();

        if (reverb != null)
        {
            reverb.Mute();
            chorus.Mute();
        }

        Array.Clear(blockLeft, 0, blockLeft.Length);
        Array.Clear(blockRight, 0, blockRight.Length);
        blockRead = blockSize;
        stamp = 0;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentException">The two buffers are different lengths.</exception>
    public void Render(Span<float> left, Span<float> right)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException("The output buffers for the left and right must be the same length.");
        }

        int written = 0;

        while (written < left.Length)
        {
            if (blockRead == blockSize)
            {
                RenderBlock();
                blockRead = 0;
            }

            int available = blockSize - blockRead;
            int wanted = left.Length - written;
            int count = available < wanted ? available : wanted;

            blockLeft.AsSpan(blockRead, count).CopyTo(left.Slice(written, count));
            blockRight.AsSpan(blockRead, count).CopyTo(right.Slice(written, count));

            blockRead += count;
            written += count;
        }
    }

    private void RenderBlock()
    {
        Array.Clear(blockLeft, 0, blockSize);
        Array.Clear(blockRight, 0, blockSize);

        bool sends = reverb != null;

        if (sends)
        {
            Array.Clear(reverbInput, 0, blockSize);
            Array.Clear(chorusInputLeft, 0, blockSize);
            Array.Clear(chorusInputRight, 0, blockSize);
        }

        // One channel at a time, because an insert effect belongs to the channel's whole mix rather
        // than to each note, and because the channel's volume, expression and pan then apply once.
        for (int channel = 0; channel < ChannelCount; channel++)
        {
            GmChannel state = channels[channel];

            double gain = state.Gain;
            double pan = state.PanPosition;

            // CC 10 is a BALANCE: it only ever turns one side down, so a centred channel is not
            // quietly attenuated and a hard-panned one does not jump in level. A voicing's own
            // placing - the kit's layout, a unison's spread - happens inside the voice and is not
            // touched by this.
            float gainLeft = (float)(gain * (pan > 0.0 ? 1.0 - pan : 1.0));
            float gainRight = (float)(gain * (pan < 0.0 ? 1.0 + pan : 1.0));

            if (!RenderChannel(channel, state, sends, gainLeft, gainRight)) { continue; }

            if (state.Insert != null && settings.EnableInsertEffects)
            {
                state.Insert.Process(channelLeft, channelRight, blockSize);
            }

            for (int i = 0; i < blockSize; i++)
            {
                blockLeft[i] += channelLeft[i] * gainLeft;
                blockRight[i] += channelRight[i] * gainRight;
            }
        }

        if (sends)
        {
            chorus.Process(chorusInputLeft, chorusInputRight, chorusLeft, chorusRight);
            reverb.Process(reverbInput, reverbLeft, reverbRight);

            for (int i = 0; i < blockSize; i++)
            {
                blockLeft[i] += chorusLeft[i] + reverbLeft[i];
                blockRight[i] += chorusRight[i] + reverbRight[i];
            }
        }

        for (int i = 0; i < blockSize; i++)
        {
            blockLeft[i] *= masterVolume;
            blockRight[i] *= masterVolume;
        }
    }

    // Renders every sounding voice of one channel into the channel scratch, and feeds the send buses
    // as it goes. False when the channel had nothing to say, in which case the scratch was not even
    // cleared.
    //
    // THE SENDS ARE TAKEN PER VOICE, not per channel, and BEFORE the channel's insert effect. Per
    // voice, because the percussion kit is sixteen different instruments on one channel and a dry
    // kick beside a wet snare is most of what makes a kit sound placed rather than pasted; before
    // the insert, because a send is an aux feed off the dry signal and a phaser's or a shaper's
    // output is not what a room would have heard.
    private bool RenderChannel(int channel, GmChannel state, bool sends, float gainLeft, float gainRight)
    {
        bool any = false;

        for (int i = 0; i < voices.Length; i++)
        {
            GmVoice voice = voices[i];

            if (!voice.IsActive || voice.Channel != channel) { continue; }

            if (!any)
            {
                Array.Clear(channelLeft, 0, blockSize);
                Array.Clear(channelRight, 0, blockSize);
                any = true;
            }

            Array.Clear(voiceLeft, 0, blockSize);
            Array.Clear(voiceRight, 0, blockSize);

            voice.Render(voiceLeft, voiceRight, blockSize, state.BendSemitones, state.ModulationCents);

            for (int frame = 0; frame < blockSize; frame++)
            {
                channelLeft[frame] += voiceLeft[frame];
                channelRight[frame] += voiceRight[frame];
            }

            if (!sends) { continue; }

            if (voice.ReverbSend > 0.0)
            {
                float send = (float)(voice.ReverbSend * reverb.InputGain);

                for (int frame = 0; frame < blockSize; frame++)
                {
                    reverbInput[frame] +=
                        ((voiceLeft[frame] * gainLeft) + (voiceRight[frame] * gainRight)) * send;
                }
            }

            if (voice.ChorusSend > 0.0)
            {
                float send = (float)voice.ChorusSend;

                for (int frame = 0; frame < blockSize; frame++)
                {
                    chorusInputLeft[frame] += voiceLeft[frame] * gainLeft * send;
                    chorusInputRight[frame] += voiceRight[frame] * gainRight * send;
                }
            }
        }

        return any;
    }

    private void NoteOnWire(int channel, int key, int velocity)
    {
        if (key < 0 || key > 127) { return; }

        if (velocity <= 0)
        {
            NoteOffWire(channel, key);
            return;
        }

        GmChannel state = channels[channel];
        GmProgramRuntime runtime;
        GmVoiceAdjustment adjustment;

        if (state.IsPercussion)
        {
            if (!GmBank.IsPercussionNote(key)) { return; }

            runtime = PercussionRuntime(key);
            adjustment = Adjustments.ResolvePercussion(key);

            // Exclusive groups: a closed or pedal hi-hat cuts a sounding open one, and the same rule
            // covers the whistles, the guiros, the cuicas and the triangles.
            int group = runtime.Spec.ExclusiveGroup;

            if (group != 0)
            {
                for (int i = 0; i < voices.Length; i++)
                {
                    GmVoice sounding = voices[i];

                    if (sounding.IsActive && sounding.Channel == channel && sounding.ExclusiveGroup == group)
                    {
                        sounding.Kill();
                    }
                }
            }
        }
        else
        {
            runtime = ProgramRuntime(state.Program);
            adjustment = Adjustments.ResolveProgram(state.Program);
        }

        // The voicing says what it would like; the consumer's adjustment may override it; a CC 91 or
        // CC 93 the music has sent outranks both, which is what makes a file's own mix win.
        double reverbSend = state.ReverbSendIsExplicit
            ? state.ReverbSend
            : double.IsNaN(adjustment.ReverbSend) ? runtime.Spec.ReverbSend : adjustment.ReverbSend;

        double chorusSend = state.ChorusSendIsExplicit ? state.ChorusSend : runtime.Spec.ChorusSend;

        GmVoice voice = TakeVoice();
        voice.Start(
            channel, key, velocity, stamp, runtime, adjustment, reverbSend, chorusSend,
            (uint)(stamp * 2654435761u));
        stamp++;
    }

    private void NoteOffWire(int channel, int key)
    {
        GmChannel state = channels[channel];

        for (int i = 0; i < voices.Length; i++)
        {
            GmVoice voice = voices[i];

            if (!voice.IsActive || !voice.IsKeyDown || voice.Channel != channel || voice.Key != key)
            {
                continue;
            }

            if (state.SustainDown) { voice.IsSustained = true; }
            else { voice.Release(force: false); }
        }
    }

    private void NoteOffAllWire(int channel, bool immediate)
    {
        for (int i = 0; i < voices.Length; i++)
        {
            GmVoice voice = voices[i];

            if (!voice.IsActive || voice.Channel != channel) { continue; }

            if (immediate) { voice.Kill(); }
            else { voice.Release(force: true); }
        }
    }

    private void ProcessController(int channel, int controller, int value)
    {
        GmChannel state = channels[channel];

        switch (controller)
        {
            case 0:
            case 32:
                // Bank select. General MIDI Level 1 has one bank, so there is nothing to select.
                break;

            case 1:
                state.Modulation = Clamp7(value);
                break;

            case 6:
                if (state.IsBendRangeSelected) { state.SetBendRangeSemitones(Clamp7(value)); }
                break;

            case 7:
                state.Volume = Clamp7(value);
                break;

            case 10:
                state.Pan = Clamp7(value);
                break;

            case 11:
                state.Expression = Clamp7(value);
                break;

            case 38:
                if (state.IsBendRangeSelected) { state.SetBendRangeCents(Clamp7(value)); }
                break;

            case 64:
                SetSustain(channel, value >= 64);
                break;

            case 91:
                state.ReverbSend = Clamp7(value) / 127.0;
                state.ReverbSendIsExplicit = true;
                break;

            case 93:
                state.ChorusSend = Clamp7(value) / 127.0;
                state.ChorusSendIsExplicit = true;
                break;

            case 100:
                state.SelectRpn(msb: false, value: Clamp7(value));
                break;

            case 101:
                state.SelectRpn(msb: true, value: Clamp7(value));
                break;

            case 120:
                NoteOffAllWire(channel, true);
                break;

            case 121:
                state.ResetControllers();
                SetSustain(channel, false);
                break;

            case 123:
                NoteOffAllWire(channel, false);
                break;
        }
    }

    private void SetSustain(int channel, bool down)
    {
        GmChannel state = channels[channel];

        if (state.SustainDown == down) { return; }

        state.SustainDown = down;

        if (down) { return; }

        for (int i = 0; i < voices.Length; i++)
        {
            GmVoice voice = voices[i];

            if (voice.IsActive && voice.IsSustained && voice.Channel == channel)
            {
                voice.IsSustained = false;
                voice.Release(force: false);
            }
        }
    }

    // A free voice, or the one worth losing. Released voices go before held ones and the quietest
    // released voice goes first, which is what keeps a sustained chord intact under a melody.
    private GmVoice TakeVoice()
    {
        GmVoice quietestReleased = null;
        GmVoice oldest = null;
        double quietest = double.MaxValue;

        for (int i = 0; i < voices.Length; i++)
        {
            GmVoice voice = voices[i];

            if (!voice.IsActive) { return voice; }

            if (!voice.IsKeyDown && !voice.IsSustained)
            {
                double loudness = voice.Loudness;

                if (loudness < quietest)
                {
                    quietest = loudness;
                    quietestReleased = voice;
                }
            }

            if (oldest == null || voice.StartStamp < oldest.StartStamp) { oldest = voice; }
        }

        return quietestReleased ?? oldest;
    }

    private GmProgramRuntime ProgramRuntime(int program)
    {
        GmProgramRuntime runtime = programRuntimes[program];

        if (runtime == null)
        {
            runtime = new GmProgramRuntime(
                GmBank.Program(program), settings.SampleRate, voices.Length, Seed(program));

            programRuntimes[program] = runtime;
        }

        return runtime;
    }

    private GmProgramRuntime PercussionRuntime(int noteNumber)
    {
        int index = noteNumber - GeneralMidi.LowestPercussionNote;
        GmProgramRuntime runtime = percussionRuntimes[index];

        if (runtime == null)
        {
            runtime = new GmProgramRuntime(
                GmBank.PercussionNote(noteNumber), settings.SampleRate, voices.Length,
                Seed(1000 + noteNumber));

            percussionRuntimes[index] = runtime;
        }

        return runtime;
    }

    private uint Seed(int salt)
    {
        uint seed = ((uint)settings.RandomSeed * 2654435761u) ^ ((uint)(salt + 1) * 2246822519u);
        return seed == 0u ? 1u : seed;
    }

    private void ResetChannels()
    {
        for (int channel = 0; channel < ChannelCount; channel++)
        {
            GmChannel state = channels[channel];

            state.ResetControllers();

            if (pinnedProgram == PinnedToPercussion)
            {
                state.IsPercussion = true;
                state.Program = 0;
                SetInsert(state, null);
            }
            else if (pinnedProgram >= 0)
            {
                state.IsPercussion = false;
                ApplyProgram(state, pinnedProgram);
            }
            else
            {
                state.IsPercussion = channel == PercussionWireChannel;
                state.Program = 0;

                if (state.IsPercussion)
                {
                    SetInsert(state, null);
                }
                else
                {
                    ApplyProgram(state, 0);
                }
            }
        }
    }

    private void ApplyProgram(GmChannel state, int program)
    {
        state.Program = program;

        SetInsert(state, GmBank.Program(program).Insert);
    }

    private void SetInsert(GmChannel state, GmInsertSpec insert)
    {
        if (insert == null || insert.Type == null)
        {
            state.Insert = null;
            state.InsertType = null;
            return;
        }

        if (!string.Equals(state.InsertType, insert.Type, StringComparison.Ordinal))
        {
            IInstrumentEffect effect;

            if (!ModestEffectFactory.TryCreate(insert.Type, out effect))
            {
                state.Insert = null;
                state.InsertType = null;
                return;
            }

            effect.Prepare(settings.SampleRate);
            state.Insert = effect;
            state.InsertType = insert.Type;
        }
        else
        {
            state.Insert.Reset();
        }

        ApplyInsertParameter(state.Insert, insert.Parameter1, insert.Value1);
        ApplyInsertParameter(state.Insert, insert.Parameter2, insert.Value2);
        ApplyInsertParameter(state.Insert, insert.Parameter3, insert.Value3);
        ApplyInsertParameter(state.Insert, insert.Parameter4, insert.Value4);
    }

    private static void ApplyInsertParameter(IInstrumentEffect effect, string name, double value)
    {
        if (name != null) { effect.TrySetParameter(name, value); }
    }

    private GmChannel ChannelAt(int channel)
    {
        if (channel < 1 || channel > ChannelCount)
        {
            throw new ArgumentOutOfRangeException(nameof(channel), channel,
                "A MIDI channel is 1 to 16, the way MidiEvent counts them.");
        }

        return channels[channel - 1];
    }

    private static int Clamp7(int value) => value < 0 ? 0 : value > 127 ? 127 : value;
}
