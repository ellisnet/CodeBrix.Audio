using System;
using System.Collections.Generic;
using System.Diagnostics;
using CodeBrix.Audio.Synth.Mpe;

// ReSharper disable once CheckNamespace
namespace CodeBrix.Audio.Synth; //was previously: MeltySynth

/// <summary>
/// An instance of the SoundFont synthesizer.
/// </summary>
/// <remarks>
/// Note that this class does not provide thread safety.
/// If you want to send notes and render the waveform in separate threads,
/// you must make sure that the methods are not called at the same time.
/// </remarks>
public sealed class SoundFontSynthesizer : IMidiSynthesizer, IMpeSynthesizer //was previously: IAudioRenderer only; IMidiSynthesizer added for SFZ parity
{
    private static readonly int channelCount = 16;
    private static readonly int percussionChannel = 9;

    private readonly SoundFont soundFont;
    private readonly int sampleRate;
    private readonly int blockSize;
    private readonly int maximumPolyphony;
    private readonly bool enableReverbAndChorus;

    private readonly int minimumVoiceDuration;

    private readonly Dictionary<int, Preset> presetLookup;
    private readonly Preset defaultPreset;

    private readonly Channel[] channels;

    // The MIDI Polyphonic Expression contract, shared with every other synthesizer in this package:
    // zones, per-channel bend ranges from RPN 0, the registered tunings, per-note pressure and
    // timbre, and which note owns its channel's expression. It is fed every message, but nothing
    // below reads it while no zone is active - which is what keeps MpeMode.Off byte-for-byte
    // identical to the engine as it was before any of this existed.
    private readonly MpeChannelState mpe = new MpeChannelState();

    private readonly VoiceCollection voices;

    private readonly float[] blockLeft;
    private readonly float[] blockRight;

    private readonly float inverseBlockSize;

    private int blockRead;

    private float masterVolume;

    private Reverb reverb;
    private float[] reverbInput;
    private float[] reverbOutputLeft;
    private float[] reverbOutputRight;

    private Chorus chorus;
    private float[] chorusInputLeft;
    private float[] chorusInputRight;
    private float[] chorusOutputLeft;
    private float[] chorusOutputRight;

    /// <summary>
    /// Initializes a new synthesizer using a specified SoundFont and sample rate.
    /// </summary>
    /// <param name="soundFontPath">The SoundFont file name and path.</param>
    /// <param name="sampleRate">The sample rate for synthesis.</param>
    public SoundFontSynthesizer(string soundFontPath, int sampleRate) : this(new SoundFont(soundFontPath), new SoundFontSynthesizerSettings(sampleRate))
    {
    }

    /// <summary>
    /// Initializes a new synthesizer using a specified SoundFont and sample rate.
    /// </summary>
    /// <param name="soundFont">The SoundFont instance.</param>
    /// <param name="sampleRate">The sample rate for synthesis.</param>
    public SoundFontSynthesizer(SoundFont soundFont, int sampleRate) : this(soundFont, new SoundFontSynthesizerSettings(sampleRate))
    {
    }

    /// <summary>
    /// Initializes a new synthesizer using a specified SoundFont and settings.
    /// </summary>
    /// <param name="soundFontPath">The SoundFont file name and path.</param>
    /// <param name="settings">The settings for synthesis.</param>
    public SoundFontSynthesizer(string soundFontPath, SoundFontSynthesizerSettings settings) : this(new SoundFont(soundFontPath), settings)
    {
    }

    /// <summary>
    /// Initializes a new synthesizer using a specified SoundFont and settings.
    /// </summary>
    /// <param name="soundFont">The SoundFont instance.</param>
    /// <param name="settings">The settings for synthesis.</param>
    public SoundFontSynthesizer(SoundFont soundFont, SoundFontSynthesizerSettings settings)
    {
        if (soundFont == null)
        {
            throw new ArgumentNullException(nameof(soundFont));
        }

        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        this.soundFont = soundFont;
        this.sampleRate = settings.SampleRate;
        this.blockSize = settings.BlockSize;
        this.maximumPolyphony = settings.MaximumPolyphony;
        this.enableReverbAndChorus = settings.EnableReverbAndChorus;

        minimumVoiceDuration = sampleRate / 500;

        presetLookup = new Dictionary<int, Preset>();

        var minPresetId = int.MaxValue;
        foreach (var preset in soundFont.PresetArray)
        {
            // The preset ID is Int32, where the upper 16 bits represent the bank number
            // and the lower 16 bits represent the patch number.
            // This ID is used to search for presets by the combination of bank number
            // and patch number.
            var presetId = (preset.BankNumber << 16) | preset.PatchNumber;
            presetLookup.TryAdd(presetId, preset);

            // The preset with the minimum ID number will be default.
            // If the SoundFont is GM compatible, the piano will be chosen.
            if (presetId < minPresetId)
            {
                defaultPreset = preset;
                minPresetId = presetId;
            }
        }
        // Default preset will never be null.
        // This assertion suppresses the nullable warning.
        Debug.Assert(defaultPreset != null);

        channels = new Channel[channelCount];
        for (var i = 0; i < channels.Length; i++)
        {
            channels[i] = new Channel(this, i == percussionChannel);
        }

        voices = new VoiceCollection(this, maximumPolyphony);

        mpe.MemberBendRange = settings.MpeMemberBendRange;
        mpe.LowerZoneMemberCount = settings.MpeLowerZoneMemberCount;
        mpe.UpperZoneMemberCount = settings.MpeUpperZoneMemberCount;
        mpe.Mode = settings.MpeMode;

        blockLeft = new float[blockSize];
        blockRight = new float[blockSize];

        inverseBlockSize = 1F / blockSize;

        blockRead = blockSize;

        masterVolume = 0.5F;

        if (enableReverbAndChorus)
        {
            reverb = new Reverb(sampleRate);
            reverbInput = new float[blockSize];
            reverbOutputLeft = new float[blockSize];
            reverbOutputRight = new float[blockSize];

            chorus = new Chorus(sampleRate, 0.002, 0.0019, 0.4);
            chorusInputLeft = new float[blockSize];
            chorusInputRight = new float[blockSize];
            chorusOutputLeft = new float[blockSize];
            chorusOutputRight = new float[blockSize];
        }
    }

    /// <summary>
    /// Processes a MIDI message.
    /// </summary>
    /// <param name="channel">The channel to which the message is sent.</param>
    /// <param name="command">The type of the message.</param>
    /// <param name="data1">The first data part of the message.</param>
    /// <param name="data2">The second data part of the message.</param>
    public void ProcessMidiMessage(int channel, int command, int data1, int data2)
    {
        if (!(0 <= channel && channel < channels.Length))
        {
            return;
        }

        var channelInfo = channels[channel];

        // Everything the MPE contract knows arrives here, including the note-off lift velocity that
        // the NoteOff(channel, key) entry point has no room for.
        mpe.ProcessMessage(channel, command, data1, data2);

        switch (command)
        {
            case 0x80: // Note Off
                NoteOff(channel, data1);
                break;

            case 0x90: // Note On
                NoteOn(channel, data1, data2);
                break;

            case 0xB0: // Controller
                switch (data1)
                {
                    case 0x00: // Bank Selection
                        channelInfo.SetBank(data2);
                        break;

                    case 0x01: // Modulation Coarse
                        channelInfo.SetModulationCoarse(data2);
                        break;

                    case 0x21: // Modulation Fine
                        channelInfo.SetModulationFine(data2);
                        break;

                    case 0x06: // Data Entry Coarse
                        channelInfo.DataEntryCoarse(data2);
                        break;

                    case 0x26: // Data Entry Fine
                        channelInfo.DataEntryFine(data2);
                        break;

                    case 0x07: // Channel Volume Coarse
                        channelInfo.SetVolumeCoarse(data2);
                        break;

                    case 0x27: // Channel Volume Fine
                        channelInfo.SetVolumeFine(data2);
                        break;

                    case 0x0A: // Pan Coarse
                        channelInfo.SetPanCoarse(data2);
                        break;

                    case 0x2A: // Pan Fine
                        channelInfo.SetPanFine(data2);
                        break;

                    case 0x0B: // Expression Coarse
                        channelInfo.SetExpressionCoarse(data2);
                        break;

                    case 0x2B: // Expression Fine
                        channelInfo.SetExpressionFine(data2);
                        break;

                    case 0x40: // Hold Pedal
                        channelInfo.SetHoldPedal(data2);
                        break;

                    case 0x5B: // Reverb Send
                        channelInfo.SetReverbSend(data2);
                        break;

                    case 0x5D: // Chorus Send
                        channelInfo.SetChorusSend(data2);
                        break;

                    case 0x63: // NRPN Coarse
                        channelInfo.SetNrpnCoarse(data2);
                        break;

                    case 0x62: // NRPN Fine
                        channelInfo.SetNrpnFine(data2);
                        break;

                    case 0x65: // RPN Coarse
                        channelInfo.SetRpnCoarse(data2);
                        break;

                    case 0x64: // RPN Fine
                        channelInfo.SetRpnFine(data2);
                        break;

                    case 0x78: // All Sound Off
                        NoteOffAll(channel, true);
                        break;

                    case 0x79: // Reset All Controllers
                        ResetAllControllers(channel);
                        break;

                    case 0x7B: // All Note Off
                        NoteOffAll(channel, false);
                        break;
                }
                break;

            case 0xC0: // Program Change
                channelInfo.SetPatch(data1);
                break;

            case 0xE0: // Pitch Bend
                channelInfo.SetPitchBend(data1, data2);
                break;
        }
    }

    /// <summary>
    /// Stops a note.
    /// </summary>
    /// <param name="channel">The channel of the note.</param>
    /// <param name="key">The key of the note.</param>
    public void NoteOff(int channel, int key)
    {
        if (!(0 <= channel && channel < channels.Length))
        {
            return;
        }

        // The lift velocity was captured by ProcessMidiMessage when the note-off arrived as a
        // message; a caller reaching this entry point directly has none to give.
        mpe.NoteOff(channel, key, mpe.ReleaseVelocity(channel, key));

        foreach (var voice in voices)
        {
            if (voice.Channel == channel && voice.Key == key)
            {
                voice.End();
            }
        }
    }

    /// <summary>
    /// Starts a note.
    /// </summary>
    /// <param name="channel">The channel of the note.</param>
    /// <param name="key">The key of the note.</param>
    /// <param name="velocity">The velocity of the note.</param>
    public void NoteOn(int channel, int key, int velocity)
    {
        if (velocity == 0)
        {
            NoteOff(channel, key);
            return;
        }

        if (!(0 <= channel && channel < channels.Length))
        {
            return;
        }

        var channelInfo = channels[channel];

        mpe.NoteOn(channel, key);

        var presetId = (channelInfo.BankNumber << 16) | channelInfo.PatchNumber;

        Preset preset;
        if (!presetLookup.TryGetValue(presetId, out preset))
        {
            // Try fallback to the GM sound set.
            // Normally, the given patch number + the bank number 0 will work.
            // For drums (bank number >= 128), it seems to be better to select the standard set (128:0).
            var gmPresetId = channelInfo.BankNumber < 128 ? channelInfo.PatchNumber : (128 << 16);

            if (!presetLookup.TryGetValue(gmPresetId, out preset))
            {
                // No corresponding preset was found. Use the default one...
                preset = defaultPreset;
            }
        }

        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        if (preset != null)
        {
            foreach (var presetRegion in preset.RegionArray)
            {
                if (presetRegion.Contains(key, velocity))
                {
                    foreach (var instrumentRegion in presetRegion.SoundFontInstrument.RegionArray)
                    {
                        if (instrumentRegion.Contains(key, velocity))
                        {
                            var regionPair = new RegionPair(presetRegion, instrumentRegion);

                            var voice = voices.RequestNew(instrumentRegion, channel);
                            if (voice != null)
                            {
                                voice.Start(regionPair, channel, key, velocity);
                            }
                        }
                    }
                }
            }
        }
    }

    /// <summary>
    /// Stops all the notes.
    /// </summary>
    /// <param name="immediate">If <c>true</c>, notes will stop immediately without the release sound.</param>
    public void NoteOffAll(bool immediate)
    {
        if (immediate)
        {
            voices.Clear();
        }
        else
        {
            foreach (var voice in voices)
            {
                voice.End();
            }
        }
    }

    /// <summary>
    /// Stops all the notes in the specified channel.
    /// </summary>
    /// <param name="channel">The channel in which the notes will be stopped.</param>
    /// <param name="immediate">If <c>true</c>, notes will stop immediately without the release sound.</param>
    public void NoteOffAll(int channel, bool immediate)
    {
        if (immediate)
        {
            foreach (var voice in voices)
            {
                if (voice.Channel == channel)
                {
                    voice.Kill();
                }
            }
        }
        else
        {
            foreach (var voice in voices)
            {
                if (voice.Channel == channel)
                {
                    voice.End();
                }
            }
        }
    }

    /// <summary>
    /// Resets all the controllers.
    /// </summary>
    public void ResetAllControllers()
    {
        foreach (var channel in channels)
        {
            channel.ResetAllControllers();
        }

        for (var channel = 0; channel < channels.Length; channel++)
        {
            mpe.ResetControllers(channel);
        }
    }

    /// <summary>
    /// Resets all the controllers of the specified channel.
    /// </summary>
    /// <param name="channel">The channel to be reset.</param>
    public void ResetAllControllers(int channel)
    {
        if (!(0 <= channel && channel < channels.Length))
        {
            return;
        }

        channels[channel].ResetAllControllers();
        mpe.ResetControllers(channel);
    }

    /// <summary>
    /// Resets the synthesizer.
    /// </summary>
    public void Reset()
    {
        voices.Clear();

        foreach (var channel in channels)
        {
            channel.Reset();
        }

        mpe.Reset();

        if (enableReverbAndChorus)
        {
            reverb!.Mute();
            chorus!.Mute();
        }

        blockRead = blockSize;
    }

    /// <inheritdoc/>
    public void Render(Span<float> left, Span<float> right)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException("The output buffers for the left and right must be the same length.");
        }

        var wrote = 0;
        while (wrote < left.Length)
        {
            if (blockRead == blockSize)
            {
                RenderBlock();
                blockRead = 0;
            }

            var srcRem = blockSize - blockRead;
            var dstRem = left.Length - wrote;
            var rem = Math.Min(srcRem, dstRem);

            blockLeft.AsSpan(blockRead, rem).CopyTo(left.Slice(wrote, rem));
            blockRight.AsSpan(blockRead, rem).CopyTo(right.Slice(wrote, rem));

            blockRead += rem;
            wrote += rem;
        }
    }

    private void RenderBlock()
    {
        voices.Process();

        Array.Clear(blockLeft, 0, blockLeft.Length);
        Array.Clear(blockRight, 0, blockRight.Length);
        foreach (var voice in voices)
        {
            var previousGainLeft = masterVolume * voice.PreviousMixGainLeft;
            var currentGainLeft = masterVolume * voice.CurrentMixGainLeft;
            WriteBlock(previousGainLeft, currentGainLeft, voice.Block, blockLeft);
            var previousGainRight = masterVolume * voice.PreviousMixGainRight;
            var currentGainRight = masterVolume * voice.CurrentMixGainRight;
            WriteBlock(previousGainRight, currentGainRight, voice.Block, blockRight);
        }

        if (enableReverbAndChorus)
        {
            Array.Clear(chorusInputLeft!, 0, chorusInputLeft!.Length);
            Array.Clear(chorusInputRight!, 0, chorusInputRight!.Length);
            foreach (var voice in voices)
            {
                var previousGainLeft = voice.PreviousChorusSend * voice.PreviousMixGainLeft;
                var currentGainLeft = voice.CurrentChorusSend * voice.CurrentMixGainLeft;
                WriteBlock(previousGainLeft, currentGainLeft, voice.Block, chorusInputLeft);
                var previousGainRight = voice.PreviousChorusSend * voice.PreviousMixGainRight;
                var currentGainRight = voice.CurrentChorusSend * voice.CurrentMixGainRight;
                WriteBlock(previousGainRight, currentGainRight, voice.Block, chorusInputRight);
            }
            chorus!.Process(chorusInputLeft, chorusInputRight, chorusOutputLeft!, chorusOutputRight!);
            ArrayMath.MultiplyAdd(masterVolume, chorusOutputLeft!, blockLeft);
            ArrayMath.MultiplyAdd(masterVolume, chorusOutputRight!, blockRight);

            Array.Clear(reverbInput!, 0, reverbInput!.Length);
            foreach (var voice in voices)
            {
                var previousGain = reverb!.InputGain * voice.PreviousReverbSend * (voice.PreviousMixGainLeft + voice.PreviousMixGainRight);
                var currentGain = reverb!.InputGain * voice.CurrentReverbSend * (voice.CurrentMixGainLeft + voice.CurrentMixGainRight);
                WriteBlock(previousGain, currentGain, voice.Block, reverbInput);
            }
            reverb!.Process(reverbInput, reverbOutputLeft!, reverbOutputRight!);
            ArrayMath.MultiplyAdd(masterVolume, reverbOutputLeft!, blockLeft);
            ArrayMath.MultiplyAdd(masterVolume, reverbOutputRight!, blockRight);
        }
    }

    private void WriteBlock(float previousGain, float currentGain, float[] source, float[] destination)
    {
        if (Math.Max(previousGain, currentGain) < SoundFontMath.NonAudible)
        {
            return;
        }

        if (MathF.Abs(currentGain - previousGain) < 1.0E-3)
        {
            ArrayMath.MultiplyAdd(currentGain, source, destination);
        }
        else
        {
            var step = inverseBlockSize * (currentGain - previousGain);
            ArrayMath.MultiplyAdd(previousGain, step, source, destination);
        }
    }

    /// <summary>
    /// The block size for rendering waveform.
    /// </summary>
    public int BlockSize => blockSize;

    /// <summary>
    /// The number of maximum polyphony.
    /// </summary>
    public int MaximumPolyphony => maximumPolyphony;

    /// <summary>
    /// The number of channels.
    /// </summary>
    /// <remarks>
    /// This value is always 16.
    /// </remarks>
    public int ChannelCount => channelCount;

    /// <summary>
    /// The percussion channel.
    /// </summary>
    /// <remarks>
    /// This value is always 9.
    /// </remarks>
    public int PercussionChannel => percussionChannel;

    /// <summary>
    /// The SoundFont used as the audio source.
    /// </summary>
    public SoundFont SoundFont => soundFont;

    /// <inheritdoc/>
    /// <remarks>
    /// <para>
    /// Changing this reconfigures the zones at once and forgets any configuration message the music
    /// has already delivered. An MPE Configuration Message arriving later is honoured in every mode
    /// but <see cref="Mpe.MpeMode.Off"/>, which is the way to insist a file is played as plain MIDI.
    /// </para>
    /// <para>
    /// While no zone is active - which is always the case in <see cref="Mpe.MpeMode.Off"/> - the
    /// engine reads its own per-channel state exactly as it always has, so a file that is not an
    /// expressive performance renders identically whatever this is set to.
    /// </para>
    /// </remarks>
    public MpeMode MpeMode
    {
        get => mpe.Mode;
        set => mpe.Mode = value;
    }

    /// <inheritdoc/>
    public double MpeMemberBendRange
    {
        get => mpe.MemberBendRange;
        set => mpe.MemberBendRange = value;
    }

    /// <inheritdoc/>
    public int MpeLowerZoneMemberCount
    {
        get => mpe.LowerZoneMemberCount;
        set => mpe.LowerZoneMemberCount = value;
    }

    /// <inheritdoc/>
    public int MpeUpperZoneMemberCount
    {
        get => mpe.UpperZoneMemberCount;
        set => mpe.UpperZoneMemberCount = value;
    }

    /// <inheritdoc/>
    public MpeZoneInfo MpeLowerZone => mpe.LowerZone;

    /// <inheritdoc/>
    public MpeZoneInfo MpeUpperZone => mpe.UpperZone;

    /// <inheritdoc/>
    /// <remarks>
    /// The SoundFont format defines no consumer for release velocity, so this changes nothing about
    /// how a preset sounds. It is captured because a MIDI file can carry it and a host may want it.
    /// </remarks>
    public int ReleaseVelocity(int channel, int key) => mpe.ReleaseVelocity(channel, key);

    /// <summary>
    /// The timbre ("slide" on an expressive controller) of a channel, 0 to 1: its CC 74 with its
    /// zone master's folded in.
    /// </summary>
    /// <param name="channel">The MIDI channel, 0 to 15.</param>
    /// <returns>The timbre, 0 to 1.</returns>
    /// <remarks>
    /// The SoundFont modulator model names no default destination for CC 74, so this engine stores
    /// the value and changes nothing about the sound with it. It is here for hosts, and because the
    /// SFZ and Decent Sampler engines DO have consumers for it and all three read the same state.
    /// </remarks>
    public double MpeTimbre(int channel) => mpe.Timbre(channel);

    /// <summary>
    /// The pressure of one sounding note, 0 to 1: its own polyphonic pressure when the music carries
    /// one and its channel's otherwise, with its zone master's folded in.
    /// </summary>
    /// <param name="channel">The MIDI channel, 0 to 15.</param>
    /// <param name="key">The MIDI note number, 0 to 127.</param>
    /// <returns>The pressure, 0 to 1.</returns>
    /// <remarks>
    /// The SoundFont default modulator set routes channel pressure to vibrato depth, and this engine
    /// does the same while an MPE zone is active: pressure deepens a note's vibrato exactly as the
    /// modulation wheel does, fifty cents at full scale.
    /// </remarks>
    public double MpePressure(int channel, int key) => mpe.Pressure(channel, key);

    /// <summary>
    /// The sample rate for synthesis.
    /// </summary>
    public int SampleRate => sampleRate;

    /// <summary>
    /// The number of voices currently playing.
    /// </summary>
    public int ActiveVoiceCount => voices.ActiveVoiceCount;

    /// <summary>
    /// Gets or sets the master volume.
    /// </summary>
    public float MasterVolume
    {
        get => masterVolume;
        set => masterVolume = value;
    }

    internal int MinimumVoiceDuration => minimumVoiceDuration;
    internal Channel[] Channels => channels;

    // ---- what a voice reads, with the MPE zone rules folded in --------------------------------
    //
    // Every reader below takes the same shape: while no zone is active it hands back exactly the
    // channel state the engine has always read, and while one is it applies the master/member rules.
    // No zone is EVER active in MpeMode.Off, so that mode is byte-for-byte what it was.

    // Whether a zone is switched on at all - the one test a voice makes before doing any of this.
    internal bool MpeZoneActive => mpe.HasActiveZone;

    // Whether this sounding note still owns its channel's expression. A newer note on the same
    // member channel takes it, and the older one freezes where it was.
    internal bool OwnsChannelExpression(int channel, int key) => mpe.OwnsChannelExpression(channel, key);

    // The semitones a voice adds to its key: bend over the channel's own range, the zone master's
    // bend over the master's, and the registered tunings of both.
    internal float ChannelPitch(int channel) =>
        mpe.HasActiveZone
            ? (float)mpe.PitchOffsetSemitones(channel)
            : channels[channel].Tune + channels[channel].PitchBend;

    // The SoundFont default modulator set routes channel pressure to vibrato depth with the same
    // fifty-cent full scale as the modulation wheel. Under MPE it is the NOTE's pressure - its own
    // polyphonic pressure when the music sends one, its channel's otherwise - plus the zone master's.
    internal float PressureVibratoCents(int channel, int key) => (float)(50.0 * mpe.Pressure(channel, key));

    internal float ChannelModulation(int channel) => ControllerSource(channel, 1).Modulation;

    internal float ChannelVolume(int channel) => ControllerSource(channel, 7).Volume;

    internal float ChannelPan(int channel) => ControllerSource(channel, 10).Pan;

    internal float ChannelExpression(int channel) => ControllerSource(channel, 11).Expression;

    internal float ChannelReverbSend(int channel) => ControllerSource(channel, 91).ReverbSend;

    internal float ChannelChorusSend(int channel) => ControllerSource(channel, 93).ChorusSend;

    // A switch takes whichever of the member and its master is down, so a master's sustain pedal
    // holds every note in the zone - which is what a performance's global pedal is for.
    internal bool ChannelHoldPedal(int channel) => ControllerSource(channel, 64).HoldPedal;

    // Which channel's own state a controller must be read from. Reading the CHANNEL rather than the
    // contract's seven-bit copy keeps the fourteen-bit coarse-and-fine resolution the SoundFont
    // engine stores.
    private Channel ControllerSource(int channel, int controller) =>
        mpe.HasActiveZone ? channels[mpe.ControllerSourceChannel(channel, controller)] : channels[channel];
}
