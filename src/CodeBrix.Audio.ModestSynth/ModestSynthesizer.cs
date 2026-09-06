using System;
using CodeBrix.Audio.ModestSynth.Internal;
using CodeBrix.Audio.ModestSynth.Oscillators;
using CodeBrix.Audio.ModestSynth.Patch;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.ModestSynth;

/// <summary>
/// A polyphonic synthesizer that plays a <see cref="ModestPatch" /> from MIDI events, with no Decent
/// Sampler file and no registration involved.
/// </summary>
/// <remarks>
/// <para>
/// It implements the same <see cref="IMidiSynthesizer" /> contract as the SoundFont, SFZ and Decent
/// Sampler engines, so everything in CodeBrix.Audio that plays a synthesizer plays this one:
/// <c>MidiSequencer</c>, <c>MidiMusicPlayer.Load(IMidiSynthesizer, MidiSequence)</c>,
/// <c>SoundFontRenderer.Render(IMidiSynthesizer, MidiSequence)</c> and
/// <c>MultiTrackPlayer</c>'s per-track synthesizer factory.
/// </para>
/// <para>
/// WHAT IT DOES. Sixteen MIDI channels; note-on and note-off with velocity; the sustain pedal
/// (CC&#160;64); pitch bend over <see cref="ModestSynthesizerSettings.PitchBendSemitones" />; all notes
/// off, all sound off and reset-all-controllers; a fixed voice pool with oldest-first stealing; an
/// amplitude envelope, glide and a master volume from the settings; and a per-voice oscillator built
/// from the patch.
/// </para>
/// <para>
/// WHAT IT DOES NOT. There are no filters, no modulators, no layers and no key or velocity zones:
/// those belong to an instrument format, and a Decent Sampler preset played through the core engine is
/// where they live. This plays ONE patch across the whole keyboard.
/// </para>
/// <para>
/// THE PATCH IS READ WHEN THE SYNTHESIZER IS BUILT. Every voice gets its own oscillator, configured
/// then; changing the patch afterwards changes nothing. Build another synthesizer for another sound.
/// The one thing that is per-voice rather than shared is the random stream: noise and <c>pluck1</c>
/// voices are seeded from <see cref="ModestSynthesizerSettings.RandomSeed" /> and the voice's own
/// number, so a chord of noise voices is a chord and not one louder voice, and a render repeats
/// exactly.
/// </para>
/// <para>
/// Rendering allocates nothing and takes no lock. Nothing here is thread-safe: MIDI events and
/// rendering must not overlap, which is the contract every synthesizer in this family follows.
/// </para>
/// </remarks>
public sealed class ModestSynthesizer : IMidiSynthesizer
{
    private const int ChannelCountValue = 16;

    private readonly ModestPatch patch;
    private readonly ModestSynthesizerSettings settings;
    private readonly ModestSynthVoice[] voices;
    private readonly double[] bendSemitones = new double[ChannelCountValue];
    private readonly bool[] sustainDown = new bool[ChannelCountValue];
    private readonly int[] lastKey = new int[ChannelCountValue];
    private readonly bool[] hasLastKey = new bool[ChannelCountValue];
    private readonly float[] blockLeft;
    private readonly float[] blockRight;
    private readonly int blockSize;

    private int blockRead;
    private long stamp;
    private float masterVolume;

    /// <summary>Creates a synthesizer for a patch at a sample rate.</summary>
    /// <param name="patch">The sound to play. Read once, here.</param>
    /// <param name="sampleRate">Samples per second, 8,000 to 192,000.</param>
    /// <exception cref="ArgumentNullException"><paramref name="patch" /> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate" /> is outside the supported range.</exception>
    /// <exception cref="NotSupportedException">The patch names a waveform this package cannot generate.</exception>
    public ModestSynthesizer(ModestPatch patch, int sampleRate)
        : this(patch, new ModestSynthesizerSettings(sampleRate))
    {
    }

    /// <summary>Creates a synthesizer for a patch with settings of your own.</summary>
    /// <param name="patch">The sound to play. Read once, here.</param>
    /// <param name="settings">How to play it. Copied, so later changes to it have no effect.</param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    /// <exception cref="NotSupportedException">The patch names a waveform this package cannot generate.</exception>
    public ModestSynthesizer(ModestPatch patch, ModestSynthesizerSettings settings)
    {
        if (patch == null) { throw new ArgumentNullException(nameof(patch)); }
        if (settings == null) { throw new ArgumentNullException(nameof(settings)); }

        this.patch = patch;
        this.settings = settings.Clone();

        blockSize = this.settings.BlockSize;
        blockLeft = new float[blockSize];
        blockRight = new float[blockSize];
        blockRead = blockSize;
        masterVolume = this.settings.MasterVolume;

        voices = new ModestSynthVoice[this.settings.MaximumPolyphony];

        for (int i = 0; i < voices.Length; i++)
        {
            voices[i] = new ModestSynthVoice(BuildOscillator(i), this.settings.SampleRate, blockSize);
        }
    }

    /// <summary>The patch being played.</summary>
    /// <remarks>It was read when the synthesizer was built; changing it now changes nothing.</remarks>
    public ModestPatch Patch => patch;

    /// <inheritdoc />
    public int SampleRate => settings.SampleRate;

    /// <inheritdoc />
    public int BlockSize => blockSize;

    /// <summary>How many voices may sound at once before the oldest is stolen.</summary>
    public int MaximumPolyphony => voices.Length;

    /// <summary>The number of MIDI channels, which is always sixteen.</summary>
    public int ChannelCount => ChannelCountValue;

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

    /// <inheritdoc />
    public void ProcessMidiMessage(int channel, int command, int data1, int data2)
    {
        if (channel < 0 || channel >= ChannelCountValue) { return; }

        switch (command)
        {
            case 0x80:
                NoteOff(channel, data1);
                break;

            case 0x90:
                NoteOn(channel, data1, data2);
                break;

            case 0xB0:
                ProcessController(channel, data1, data2);
                break;

            case 0xE0:
                bendSemitones[channel] =
                    (((data2 << 7) | data1) - 8192) / 8192.0 * settings.PitchBendSemitones;
                break;
        }
    }

    /// <summary>Starts a note. A velocity of zero is a note-off, as MIDI requires.</summary>
    /// <param name="channel">The MIDI channel, 0 to 15.</param>
    /// <param name="key">The MIDI note number, 0 to 127.</param>
    /// <param name="velocity">The velocity, 0 to 127.</param>
    public void NoteOn(int channel, int key, int velocity)
    {
        if (channel < 0 || channel >= ChannelCountValue || key < 0 || key > 127) { return; }

        if (velocity <= 0)
        {
            NoteOff(channel, key);
            return;
        }

        ModestSynthVoice voice = TakeVoice();

        double glideFrom = hasLastKey[channel] && settings.GlideSeconds > 0.0
            ? lastKey[channel] - key
            : 0.0;

        voice.Start(
            channel, key, velocity, stamp++,
            patch.GetStartPhase(VoiceSeed(IndexOf(voice))),
            settings.Attack, settings.Decay, settings.Sustain, settings.Release,
            settings.VelocityTracking, glideFrom, settings.GlideSeconds);

        lastKey[channel] = key;
        hasLastKey[channel] = true;
    }

    /// <summary>Releases a note. It keeps sounding if the sustain pedal is down.</summary>
    /// <param name="channel">The MIDI channel, 0 to 15.</param>
    /// <param name="key">The MIDI note number, 0 to 127.</param>
    public void NoteOff(int channel, int key)
    {
        if (channel < 0 || channel >= ChannelCountValue) { return; }

        for (int i = 0; i < voices.Length; i++)
        {
            ModestSynthVoice voice = voices[i];

            if (!voice.IsActive || !voice.IsKeyDown || voice.Channel != channel || voice.Key != key)
            {
                continue;
            }

            if (sustainDown[channel])
            {
                voice.IsSustained = true;
            }
            else
            {
                voice.Release();
            }
        }
    }

    /// <inheritdoc />
    public void NoteOffAll(bool immediate)
    {
        for (int channel = 0; channel < ChannelCountValue; channel++)
        {
            NoteOffAll(channel, immediate);
        }
    }

    /// <summary>Stops every note on one channel.</summary>
    /// <param name="channel">The MIDI channel, 0 to 15.</param>
    /// <param name="immediate">
    /// When true the voices are choked in five milliseconds; when false they run their release.
    /// </param>
    public void NoteOffAll(int channel, bool immediate)
    {
        if (channel < 0 || channel >= ChannelCountValue) { return; }

        for (int i = 0; i < voices.Length; i++)
        {
            ModestSynthVoice voice = voices[i];

            if (!voice.IsActive || voice.Channel != channel) { continue; }

            if (immediate) { voice.Kill(); }
            else { voice.Release(); }
        }

        hasLastKey[channel] = false;
    }

    /// <inheritdoc />
    public void Reset()
    {
        for (int i = 0; i < voices.Length; i++)
        {
            voices[i].Stop();
        }

        for (int channel = 0; channel < ChannelCountValue; channel++)
        {
            bendSemitones[channel] = 0.0;
            sustainDown[channel] = false;
            hasLastKey[channel] = false;
            lastKey[channel] = 60;
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

        for (int i = 0; i < voices.Length; i++)
        {
            ModestSynthVoice voice = voices[i];

            if (!voice.IsActive) { continue; }

            voice.Render(blockLeft, blockRight, blockSize, bendSemitones[voice.Channel]);
        }

        for (int i = 0; i < blockSize; i++)
        {
            blockLeft[i] *= masterVolume;
            blockRight[i] *= masterVolume;
        }
    }

    private void ProcessController(int channel, int controller, int value)
    {
        switch (controller)
        {
            case 64:
                SetSustain(channel, value >= 64);
                break;

            case 120:
                NoteOffAll(channel, true);
                break;

            case 121:
                bendSemitones[channel] = 0.0;
                SetSustain(channel, false);
                break;

            case 123:
                NoteOffAll(channel, false);
                break;
        }
    }

    private void SetSustain(int channel, bool down)
    {
        if (sustainDown[channel] == down) { return; }

        sustainDown[channel] = down;

        if (down) { return; }

        for (int i = 0; i < voices.Length; i++)
        {
            ModestSynthVoice voice = voices[i];

            if (voice.IsActive && voice.IsSustained && voice.Channel == channel)
            {
                voice.IsSustained = false;
                voice.Release();
            }
        }
    }

    // A free voice, or the oldest sounding one. Released voices are taken before held ones, which is
    // what keeps a sustained chord intact while a melody plays over it.
    private ModestSynthVoice TakeVoice()
    {
        ModestSynthVoice oldestReleased = null;
        ModestSynthVoice oldest = null;

        for (int i = 0; i < voices.Length; i++)
        {
            ModestSynthVoice voice = voices[i];

            if (!voice.IsActive) { return voice; }

            if (!voice.IsKeyDown && !voice.IsSustained)
            {
                if (oldestReleased == null || voice.StartStamp < oldestReleased.StartStamp)
                {
                    oldestReleased = voice;
                }
            }

            if (oldest == null || voice.StartStamp < oldest.StartStamp) { oldest = voice; }
        }

        return oldestReleased ?? oldest;
    }

    private int IndexOf(ModestSynthVoice voice)
    {
        for (int i = 0; i < voices.Length; i++)
        {
            if (ReferenceEquals(voices[i], voice)) { return i; }
        }

        return 0;
    }

    private IModestVoiceOscillator BuildOscillator(int index)
    {
        IModestOscillator oscillator = patch.CreateOscillator(settings.SampleRate);

        uint seed = VoiceSeed(index);

        if (oscillator is NoiseOscillator noise) { noise.Seed = seed; }
        if (oscillator is Pluck1Oscillator pluck) { pluck.Seed = seed; }

        return (IModestVoiceOscillator)oscillator;
    }

    private uint VoiceSeed(int index)
    {
        uint seed = ((uint)settings.RandomSeed * 2654435761u) ^ ((uint)(index + 1) * 2246822519u);
        return seed == 0u ? 1u : seed;
    }
}
