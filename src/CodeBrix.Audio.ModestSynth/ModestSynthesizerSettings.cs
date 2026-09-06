using System;

namespace CodeBrix.Audio.ModestSynth;

/// <summary>
/// How a <see cref="ModestSynthesizer" /> plays a patch: the rate and block it renders at, how many
/// notes it holds at once, the amplitude envelope every voice runs, glide, pitch-bend range and the
/// master volume.
/// </summary>
/// <remarks>
/// <para>
/// The patch says what a voice SOUNDS like; these say how it is PLAYED. They are separate because a
/// <see cref="Patch.ModestPatch" /> is the Decent Sampler format's own oscillator model, which has no
/// envelope of its own - in a preset the group's envelope does that job, and here these do.
/// </para>
/// <para>
/// Every value is clamped rather than rejected, except the sample rate and block size, which are
/// structural. Read the settings once at construction: changing them afterwards changes nothing.
/// </para>
/// </remarks>
public sealed class ModestSynthesizerSettings
{
    /// <summary>The sample rate used when none is given.</summary>
    public const int DefaultSampleRate = 44100;

    /// <summary>The number of frames rendered per internal block.</summary>
    public const int DefaultBlockSize = 64;

    /// <summary>How many notes sound at once before the oldest is stolen.</summary>
    public const int DefaultMaximumPolyphony = 32;

    /// <summary>The pitch-bend range in semitones that MIDI assumes when nothing says otherwise.</summary>
    public const double DefaultPitchBendSemitones = 2.0;

    /// <summary>The master gain every synthesizer in this family starts at.</summary>
    public const float DefaultMasterVolume = 0.5f;

    /// <summary>The random seed a synthesizer starts from, so a render repeats exactly.</summary>
    public const int DefaultRandomSeed = 12345;

    private int sampleRate = DefaultSampleRate;
    private int blockSize = DefaultBlockSize;
    private int maximumPolyphony = DefaultMaximumPolyphony;
    private double attack;
    private double decay;
    private double sustain = 1.0;
    private double release = 0.1;
    private double glideSeconds;
    private double pitchBendSemitones = DefaultPitchBendSemitones;
    private double velocityTracking = 1.0;
    private float masterVolume = DefaultMasterVolume;

    /// <summary>Creates settings at <see cref="DefaultSampleRate" />.</summary>
    public ModestSynthesizerSettings()
    {
    }

    /// <summary>Creates settings at a sample rate.</summary>
    /// <param name="sampleRate">Samples per second, 8,000 to 192,000.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate" /> is outside that range.</exception>
    public ModestSynthesizerSettings(int sampleRate)
    {
        SampleRate = sampleRate;
    }

    /// <summary>The sample rate to synthesize at, in Hz. 8,000 to 192,000.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside that range.</exception>
    public int SampleRate
    {
        get => sampleRate;
        set
        {
            if (value < 8000 || value > 192000)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "The sample rate must be between 8,000 and 192,000 Hz.");
            }

            sampleRate = value;
        }
    }

    /// <summary>
    /// How many frames are rendered per internal block, 8 to 1,024. Parameter changes land on a block
    /// boundary, so a smaller block responds sooner and costs a little more.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside that range.</exception>
    public int BlockSize
    {
        get => blockSize;
        set
        {
            if (value < 8 || value > 1024)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "The block size must be between 8 and 1,024 frames.");
            }

            blockSize = value;
        }
    }

    /// <summary>How many notes may sound at once, 1 to 1,024. The oldest voice is stolen beyond it.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside that range.</exception>
    public int MaximumPolyphony
    {
        get => maximumPolyphony;
        set
        {
            if (value < 1 || value > 1024)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "The polyphony must be between 1 and 1,024 voices.");
            }

            maximumPolyphony = value;
        }
    }

    /// <summary>The amplitude envelope's attack time in seconds. 0 or more; clamped. Default 0.</summary>
    public double Attack
    {
        get => attack;
        set => attack = Sanitise(value, 0.0, 60.0, attack);
    }

    /// <summary>The amplitude envelope's decay time in seconds. 0 or more; clamped. Default 0.</summary>
    public double Decay
    {
        get => decay;
        set => decay = Sanitise(value, 0.0, 60.0, decay);
    }

    /// <summary>The level the envelope holds at while the key is down, 0 to 1. Default 1.</summary>
    public double Sustain
    {
        get => sustain;
        set => sustain = Sanitise(value, 0.0, 1.0, sustain);
    }

    /// <summary>The amplitude envelope's release time in seconds. 0 or more; clamped. Default 0.1.</summary>
    public double Release
    {
        get => release;
        set => release = Sanitise(value, 0.0, 60.0, release);
    }

    /// <summary>
    /// How long a new note takes to slide from the last note's pitch, in seconds. 0 - the default -
    /// turns glide off; the slide is linear in pitch and takes this long whatever the interval.
    /// </summary>
    public double GlideSeconds
    {
        get => glideSeconds;
        set => glideSeconds = Sanitise(value, 0.0, 10.0, glideSeconds);
    }

    /// <summary>How far a full pitch bend moves the pitch, in semitones. Default 2.</summary>
    public double PitchBendSemitones
    {
        get => pitchBendSemitones;
        set => pitchBendSemitones = Sanitise(value, 0.0, 48.0, pitchBendSemitones);
    }

    /// <summary>
    /// How much of the loudness follows the velocity, 0 to 1. The gain is
    /// <c>(1 - tracking) + tracking * velocity / 127</c>, the same law the Decent Sampler format's
    /// <c>ampVelTrack</c> uses. Default 1.
    /// </summary>
    /// <remarks>
    /// This is the AMPLITUDE. An <c>fm6op</c> patch also gets the raw velocity, which its operators'
    /// velocity sensitivity turns into brightness; that happens whatever this is set to.
    /// </remarks>
    public double VelocityTracking
    {
        get => velocityTracking;
        set => velocityTracking = Sanitise(value, 0.0, 1.0, velocityTracking);
    }

    /// <summary>The master output gain. Default 0.5, as every synthesizer in this family uses.</summary>
    public float MasterVolume
    {
        get => masterVolume;
        set => masterVolume = float.IsNaN(value) ? masterVolume : value < 0f ? 0f : value > 4f ? 4f : value;
    }

    /// <summary>
    /// The seed the per-voice random streams are built from, so that the same notes render the same
    /// samples every run. Default <see cref="DefaultRandomSeed" />.
    /// </summary>
    public int RandomSeed { get; set; } = DefaultRandomSeed;

    /// <summary>Makes an independent copy.</summary>
    /// <returns>A copy carrying the same values.</returns>
    public ModestSynthesizerSettings Clone() =>
        new ModestSynthesizerSettings
        {
            sampleRate = sampleRate,
            blockSize = blockSize,
            maximumPolyphony = maximumPolyphony,
            attack = attack,
            decay = decay,
            sustain = sustain,
            release = release,
            glideSeconds = glideSeconds,
            pitchBendSemitones = pitchBendSemitones,
            velocityTracking = velocityTracking,
            masterVolume = masterVolume,
            RandomSeed = RandomSeed,
        };

    private static double Sanitise(double value, double minimum, double maximum, double current)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) { return current; }

        return value < minimum ? minimum : value > maximum ? maximum : value;
    }
}
