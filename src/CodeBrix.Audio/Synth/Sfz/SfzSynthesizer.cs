using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// An instance of the SFZ synthesizer: plays an <see cref="SfzInstrument"/> from MIDI events, as
/// <see cref="SoundFontSynthesizer"/> plays a SoundFont.
/// </summary>
/// <remarks>
/// <para>
/// The peer of <see cref="SoundFontSynthesizer"/> for the other instrument format, implementing the
/// same <see cref="IMidiSynthesizer"/> contract, so <see cref="MidiSequencer"/>, the offline renderer
/// and the music player drive either without caring which. Like its peer it is NOT thread-safe:
/// sending events and rendering must not overlap.
/// </para>
/// <para>
/// Region selection implements the SFZ articulation model: key and velocity ranges, controller ranges,
/// round robins (<c>seq_position</c>) and random layers (<c>lorand</c>/<c>hirand</c>), key switches
/// (<c>sw_last</c>, <c>sw_down</c>, <c>sw_up</c>, <c>sw_previous</c>), trigger modes including release
/// samples with <c>rt_decay</c>, exclusive off groups (<c>group</c>/<c>off_by</c>), and CC-triggered
/// regions (<c>on_loccN</c>/<c>on_hiccN</c>).
/// </para>
/// <para>
/// Random layer selection is deterministic: the same settings and event sequence render the same audio
/// on every run. Vary <see cref="SfzSynthesizerSettings.RandomSeed"/> for a different performance.
/// </para>
/// </remarks>
public sealed class SfzSynthesizer : IMidiSynthesizer
{
    private static readonly int channelCount = 16;

    private readonly SfzInstrument instrument;
    private readonly int sampleRate;
    private readonly int blockSize;
    private readonly int maximumPolyphony;
    private readonly int randomSeed;

    private readonly int minimumVoiceDuration;

    private readonly SfzChannel[] channels;
    private readonly SfzVoiceCollection voices;

    private readonly List<SfzRegion> attackRegions = new List<SfzRegion>();
    private readonly List<SfzRegion> releaseRegions = new List<SfzRegion>();
    private readonly List<SfzRegion> ccTriggeredRegions = new List<SfzRegion>();
    private readonly int[] seqCounters;

    private readonly bool hasKeyswitchRange;
    private readonly int keyswitchLow;
    private readonly int keyswitchHigh;

    private readonly float[] blockLeft;
    private readonly float[] blockRight;
    private readonly float inverseBlockSize;

    private Random random;
    private int blockRead;
    private long renderedFrames;
    private long noteEventStamp;
    private bool alternateFlag;

    private float masterVolume;

    /// <summary>
    /// Initializes a new synthesizer for an SFZ file and sample rate. Prefer the
    /// <see cref="SfzInstrument"/> overload with a shared, cached instrument.
    /// </summary>
    /// <param name="sfzPath">The SFZ file name and path.</param>
    /// <param name="sampleRate">The sample rate for synthesis.</param>
    public SfzSynthesizer(string sfzPath, int sampleRate) : this(new SfzInstrument(sfzPath), new SfzSynthesizerSettings(sampleRate))
    {
    }

    /// <summary>
    /// Initializes a new synthesizer using a specified instrument and sample rate.
    /// </summary>
    /// <param name="instrument">The instrument to play.</param>
    /// <param name="sampleRate">The sample rate for synthesis.</param>
    public SfzSynthesizer(SfzInstrument instrument, int sampleRate) : this(instrument, new SfzSynthesizerSettings(sampleRate))
    {
    }

    /// <summary>
    /// Initializes a new synthesizer using a specified instrument and settings.
    /// </summary>
    /// <param name="instrument">The instrument to play.</param>
    /// <param name="settings">The settings for synthesis.</param>
    public SfzSynthesizer(SfzInstrument instrument, SfzSynthesizerSettings settings)
    {
        if (instrument == null)
        {
            throw new ArgumentNullException(nameof(instrument));
        }

        if (settings == null)
        {
            throw new ArgumentNullException(nameof(settings));
        }

        this.instrument = instrument;
        sampleRate = settings.SampleRate;
        blockSize = settings.BlockSize;
        maximumPolyphony = settings.MaximumPolyphony;
        randomSeed = settings.RandomSeed;

        minimumVoiceDuration = sampleRate / 500;

        var swLow = int.MaxValue;
        var swHigh = int.MinValue;

        foreach (var region in instrument.Regions)
        {
            if (region.IsDisabled || instrument.GetSampleData(region) == null)
            {
                continue;
            }

            if (region.OnCcRanges.Count > 0)
            {
                // A CC-triggered region responds to controller movement, not to notes.
                ccTriggeredRegions.Add(region);
                continue;
            }

            if (region.Trigger == SfzTrigger.Release)
            {
                releaseRegions.Add(region);
            }
            else
            {
                attackRegions.Add(region);
            }

            if (region.SwLast.HasValue)
            {
                swLow = Math.Min(swLow, region.SwLoKey ?? 0);
                swHigh = Math.Max(swHigh, region.SwHiKey ?? 127);
            }

            // sw_lolast/sw_hilast regions declare a keyswitch range of their own; pressing any key in
            // it must record the last keyswitch even without sw_lokey/sw_hikey.
            if (region.SwLoLast.HasValue || region.SwHiLast.HasValue)
            {
                swLow = Math.Min(swLow, region.SwLoKey ?? region.SwLoLast ?? 0);
                swHigh = Math.Max(swHigh, region.SwHiKey ?? region.SwHiLast ?? 127);
            }
        }

        hasKeyswitchRange = swLow <= swHigh;
        keyswitchLow = swLow;
        keyswitchHigh = swHigh;

        seqCounters = new int[instrument.Regions.Count];

        channels = new SfzChannel[channelCount];
        for (var i = 0; i < channels.Length; i++)
        {
            channels[i] = new SfzChannel(instrument);
        }

        voices = new SfzVoiceCollection(this, maximumPolyphony);

        blockLeft = new float[blockSize];
        blockRight = new float[blockSize];
        inverseBlockSize = 1F / blockSize;

        blockRead = blockSize;
        masterVolume = 0.5F;

        random = new Random(randomSeed);
    }

    /// <inheritdoc/>
    public void ProcessMidiMessage(int channel, int command, int data1, int data2)
    {
        if (!(0 <= channel && channel < channels.Length))
        {
            return;
        }

        var channelInfo = channels[channel];

        switch (command)
        {
            case 0x80: // Note Off
                NoteOff(channel, data1);
                break;

            case 0x90: // Note On
                NoteOn(channel, data1, data2);
                break;

            case 0xA0: // Polyphonic Aftertouch - stored as the extended source CC 130
                SetControllerValue(channel, channelInfo, 130, data2);
                break;

            case 0xB0: // Controller
                switch (data1)
                {
                    case 0x78: // All Sound Off
                        NoteOffAll(channel, true);
                        break;

                    case 0x79: // Reset All Controllers
                        channelInfo.ResetControllers();
                        break;

                    case 0x7B: // All Note Off
                        NoteOffAll(channel, false);
                        break;

                    default:
                        SetControllerValue(channel, channelInfo, data1, data2);
                        break;
                }
                break;

            case 0xC0: // Program Change - what loprog/hiprog regions select on
                channelInfo.Program = data1;
                break;

            case 0xD0: // Channel Aftertouch - stored as the extended source CC 129
                SetControllerValue(channel, channelInfo, 129, data1);
                break;

            case 0xE0: // Pitch Bend
                channelInfo.SetPitchBend(data1, data2);
                break;
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

        if (!(0 <= channel && channel < channels.Length) || key < 0 || key > 127)
        {
            return;
        }

        var channelInfo = channels[channel];

        if (hasKeyswitchRange && keyswitchLow <= key && key <= keyswitchHigh)
        {
            channelInfo.SetLastKeyswitch(key);
        }

        // trigger=first and trigger=legato test the held count as it was BEFORE this note.
        var heldBefore = channelInfo.HeldKeyCount;

        // One random number per note-on, shared by every region: adjacent lorand/hirand ranges then
        // select exactly one layer, which is the point of the mechanism. The alternate source
        // (extended CC 137) flips on every note-on, before any voice latches it.
        var randomValue = (float)random.NextDouble();
        noteEventStamp++;
        alternateFlag = !alternateFlag;

        foreach (var region in attackRegions)
        {
            if (!MatchesNoteOn(region, channelInfo, key, velocity, randomValue, heldBefore))
            {
                continue;
            }

            if (!PassesSequence(region))
            {
                continue;
            }

            StartVoice(region, channelInfo, channel, key, velocity, 1f);
        }

        channelInfo.KeyDown(key, velocity, renderedFrames);
    }

    /// <summary>
    /// Stops a note, releasing its voices and firing any matching release regions.
    /// </summary>
    /// <param name="channel">The channel of the note.</param>
    /// <param name="key">The key of the note.</param>
    public void NoteOff(int channel, int key)
    {
        if (!(0 <= channel && channel < channels.Length) || key < 0 || key > 127)
        {
            return;
        }

        var channelInfo = channels[channel];
        var wasHeld = channelInfo.IsKeyHeld(key);

        foreach (var voice in voices)
        {
            if (voice.Channel == channel && voice.Key == key && voice.Region.Trigger != SfzTrigger.Release)
            {
                voice.End();
            }
        }

        if (!wasHeld)
        {
            channelInfo.KeyUp(key);
            return;
        }

        // Release regions play with the velocity of the note-on they release, attenuated by rt_decay
        // for the time the note was held.
        var noteOnVelocity = channelInfo.NoteOnVelocity(key);
        var heldSeconds = (renderedFrames - channelInfo.NoteOnFrame(key)) / (double)sampleRate;

        channelInfo.KeyUp(key);

        var randomValue = (float)random.NextDouble();
        noteEventStamp++;

        foreach (var region in releaseRegions)
        {
            if (!MatchesSelection(region, channelInfo, key, noteOnVelocity, randomValue))
            {
                continue;
            }

            if (!PassesSequence(region))
            {
                continue;
            }

            var rtGain = region.RtDecay > 0f
                ? SoundFontMath.DecibelsToLinear((float)(-region.RtDecay * heldSeconds))
                : 1f;

            StartVoice(region, channelInfo, channel, key, noteOnVelocity, rtGain);
        }
    }

    /// <inheritdoc/>
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
        foreach (var voice in voices)
        {
            if (voice.Channel == channel)
            {
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
    }

    /// <inheritdoc/>
    public void Reset()
    {
        voices.Clear();

        foreach (var channel in channels)
        {
            channel.Reset();
        }

        Array.Clear(seqCounters, 0, seqCounters.Length);

        // Reseeding here is what makes a re-play of the same sequence identical, round robins and all.
        random = new Random(randomSeed);

        renderedFrames = 0;
        noteEventStamp = 0;
        alternateFlag = false;
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

    /// <summary>The instrument being played.</summary>
    public SfzInstrument Instrument => instrument;

    /// <inheritdoc/>
    public int BlockSize => blockSize;

    /// <summary>The number of maximum polyphony.</summary>
    public int MaximumPolyphony => maximumPolyphony;

    /// <summary>The number of channels, always 16. Every channel plays the same instrument.</summary>
    public int ChannelCount => channelCount;

    /// <inheritdoc/>
    public int SampleRate => sampleRate;

    /// <inheritdoc/>
    public int ActiveVoiceCount => voices.ActiveVoiceCount;

    /// <inheritdoc/>
    public float MasterVolume
    {
        get => masterVolume;
        set => masterVolume = value;
    }

    internal int MinimumVoiceDuration => minimumVoiceDuration;

    // The per-voice random source, from the same seeded stream as layer selection, so identical input
    // renders identically. Voices draw at start for delay_random, offset_random, amp_random,
    // fil_random and the extended random/sample-and-hold sources.
    internal float NextRandomValue() => (float)random.NextDouble();

    // The extended alternate source (CC 137): flips on every note-on event.
    internal bool AlternateFlag => alternateFlag;

    private void SetControllerValue(int channel, SfzChannel channelInfo, int cc, int value)
    {
        var previous = channelInfo.GetCcMidiValue(cc);
        channelInfo.SetCc(cc, value / 127f);

        if (ccTriggeredRegions.Count == 0)
        {
            return;
        }

        // A CC-triggered region fires when this controller ENTERS one of its on_locc/on_hicc ranges.
        var randomValue = -1f;

        foreach (var region in ccTriggeredRegions)
        {
            var triggered = false;
            foreach (var range in region.OnCcRanges)
            {
                if (range.CcNumber == cc && !range.Contains(previous) && range.Contains(value))
                {
                    triggered = true;
                    break;
                }
            }

            if (!triggered)
            {
                continue;
            }

            if (randomValue < 0f)
            {
                randomValue = (float)random.NextDouble();
                noteEventStamp++;
            }

            // The region's pitch keycenter stands in for the note a controller does not have; the
            // controller value stands in for velocity, floored so a low pedal still sounds.
            var key = Math.Clamp(region.PitchKeycenter, 0, 127);
            var velocity = Math.Clamp(value, 1, 127);

            if (!MatchesSelection(region, channelInfo, key, velocity, randomValue))
            {
                continue;
            }

            if (!PassesSequence(region))
            {
                continue;
            }

            StartVoice(region, channelInfo, channel, key, velocity, 1f);
        }
    }

    private bool MatchesNoteOn(SfzRegion region, SfzChannel channelInfo, int key, int velocity, float randomValue, int heldBefore)
    {
        switch (region.Trigger)
        {
            case SfzTrigger.First:
                if (heldBefore > 0)
                {
                    return false;
                }
                break;

            case SfzTrigger.Legato:
                if (heldBefore == 0)
                {
                    return false;
                }
                break;
        }

        return MatchesSelection(region, channelInfo, key, velocity, randomValue);
    }

    private bool MatchesSelection(SfzRegion region, SfzChannel channelInfo, int key, int velocity, float randomValue)
    {
        if (key < region.LoKey || key > region.HiKey)
        {
            return false;
        }

        // sw_vel=previous: the velocity checks look at the previous note's velocity, not this one's.
        // Matching runs before this note's KeyDown, so the last completed note-on IS the previous note.
        var testVelocity = region.SwVelPrevious ? channelInfo.LastNoteOnVelocity : velocity;
        if (testVelocity < region.LoVel || testVelocity > region.HiVel)
        {
            return false;
        }

        if (channelInfo.Program < region.LoProg || channelInfo.Program > region.HiProg)
        {
            return false;
        }

        var ccRanges = region.CcRanges;
        for (var i = 0; i < ccRanges.Count; i++)
        {
            if (!ccRanges[i].Contains(channelInfo.GetCcMidiValue(ccRanges[i].CcNumber)))
            {
                return false;
            }
        }

        // The random layer test is half-open so adjacent ranges pick exactly one layer; a hirand of 1
        // keeps its top edge, since the random draw itself is in [0, 1).
        if (randomValue < region.LoRand || (randomValue >= region.HiRand && region.HiRand < 1f))
        {
            return false;
        }

        if (region.SwLast.HasValue)
        {
            var effective = channelInfo.LastKeyswitch >= 0
                ? channelInfo.LastKeyswitch
                : region.SwDefault ?? -1;
            if (effective != region.SwLast.Value)
            {
                return false;
            }
        }

        // sw_lolast/sw_hilast: like sw_last, but the last-pressed keyswitch may be anywhere in a range.
        if (region.SwLoLast.HasValue || region.SwHiLast.HasValue)
        {
            var effective = channelInfo.LastKeyswitch >= 0
                ? channelInfo.LastKeyswitch
                : region.SwDefault ?? -1;
            if (effective < (region.SwLoLast ?? 0) || effective > (region.SwHiLast ?? 127))
            {
                return false;
            }
        }

        if (region.SwDown.HasValue && !channelInfo.IsKeyHeld(region.SwDown.Value))
        {
            return false;
        }

        if (region.SwUp.HasValue && channelInfo.IsKeyHeld(region.SwUp.Value))
        {
            return false;
        }

        if (region.SwPrevious.HasValue && channelInfo.PreviousNote != region.SwPrevious.Value)
        {
            return false;
        }

        return true;
    }

    // The round-robin counter advances on every note-on the region otherwise matched, and the region
    // sounds when the counter lands on its seq_position.
    private bool PassesSequence(SfzRegion region)
    {
        if (region.SeqLength <= 1)
        {
            return true;
        }

        var counter = seqCounters[region.Index]++;
        return (counter % region.SeqLength) + 1 == region.SeqPosition;
    }

    private void StartVoice(SfzRegion region, SfzChannel channelInfo, int channel, int key, int velocity, float rtGain)
    {
        // polyphony: cap simultaneous voices across the region's polyphony scope - its group when it
        // has one, else the region itself - stealing the oldest, whatever note it plays.
        if (region.Polyphony.HasValue)
        {
            var limit = region.Polyphony.Value;
            var count = 0;
            SfzVoice oldest = null;

            foreach (var voice in voices)
            {
                var sameScope = region.Group != 0
                    ? voice.Region.Group == region.Group
                    : voice.Region == region;
                if (!sameScope)
                {
                    continue;
                }

                count++;
                if (oldest == null || voice.VoiceLength > oldest.VoiceLength)
                {
                    oldest = voice;
                }
            }

            if (count >= limit && oldest != null)
            {
                oldest.Choke(SfzOffMode.Fast);
            }
        }

        // note_polyphony: cap simultaneous voices of this note within the region's group, stealing
        // the oldest.
        if (region.NotePolyphony.HasValue)
        {
            var limit = region.NotePolyphony.Value;
            var count = 0;
            SfzVoice oldest = null;

            foreach (var voice in voices)
            {
                if (voice.Key != key || voice.Channel != channel)
                {
                    continue;
                }

                var sameScope = region.Group != 0
                    ? voice.Region.Group == region.Group
                    : voice.Region == region;
                if (!sameScope)
                {
                    continue;
                }

                count++;
                if (oldest == null || voice.VoiceLength > oldest.VoiceLength)
                {
                    oldest = voice;
                }
            }

            if (count >= limit && oldest != null)
            {
                oldest.Choke(SfzOffMode.Fast);
            }
        }

        // Off groups: a starting voice in group G chokes every voice whose region says off_by=G.
        // Voices born of this same note-on event are exempt, so layered regions sharing a group do
        // not strangle each other at birth.
        if (region.Group != 0)
        {
            foreach (var voice in voices)
            {
                if (voice.EventStamp != noteEventStamp &&
                    voice.Region.OffBy != 0 &&
                    voice.Region.OffBy == region.Group)
                {
                    voice.Choke(voice.Region.OffMode);
                }
            }
        }

        var sample = instrument.GetSampleData(region);
        if (sample == null)
        {
            return;
        }

        var voiceSlot = voices.RequestNew();
        if (voiceSlot != null)
        {
            voiceSlot.Start(region, sample, channelInfo, channel, key, velocity, rtGain);
            voiceSlot.EventStamp = noteEventStamp;
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
            WriteBlock(previousGainLeft, currentGainLeft, voice.BlockLeft, blockLeft);
            var previousGainRight = masterVolume * voice.PreviousMixGainRight;
            var currentGainRight = masterVolume * voice.CurrentMixGainRight;
            WriteBlock(previousGainRight, currentGainRight, voice.BlockRight, blockRight);
        }

        renderedFrames += blockSize;
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
}
