using System;

namespace CodeBrix.Audio.ModestSynth;

/// <summary>
/// How a <see cref="GeneralMidiSynthesizer" /> plays the General MIDI bank: the rate and block it
/// renders at, how many notes it holds at once across all sixteen channels, the master volume, and
/// whether its shared reverb and chorus buses and its per-program insert effects run at all.
/// </summary>
/// <remarks>
/// <para>
/// This is NOT <see cref="ModestSynthesizerSettings" />. That one carries the single envelope, glide
/// and velocity law the standalone single-patch synthesizer applies to every note; here every one of
/// those lives in the bank, per program, because 128 programs cannot share one envelope.
/// </para>
/// <para>
/// The settings are read once when the synthesizer is built: changing them afterwards changes
/// nothing. Everything except the sample rate and the block size is clamped rather than rejected.
/// </para>
/// </remarks>
public sealed class GeneralMidiSynthesizerSettings
{
    /// <summary>The sample rate used when none is given.</summary>
    public const int DefaultSampleRate = 44100;

    /// <summary>The number of frames rendered per internal block.</summary>
    public const int DefaultBlockSize = 64;

    /// <summary>
    /// How many notes sound at once, across all sixteen channels together, before the synthesizer
    /// starts stealing.
    /// </summary>
    public const int DefaultMaximumPolyphony = 64;

    /// <summary>The master gain every synthesizer in this family starts at.</summary>
    public const float DefaultMasterVolume = 0.5f;

    /// <summary>The random seed a synthesizer starts from, so a render repeats exactly.</summary>
    public const int DefaultRandomSeed = 12345;

    private int sampleRate = DefaultSampleRate;
    private int blockSize = DefaultBlockSize;
    private int maximumPolyphony = DefaultMaximumPolyphony;
    private float masterVolume = DefaultMasterVolume;

    /// <summary>Creates settings at <see cref="DefaultSampleRate" />.</summary>
    public GeneralMidiSynthesizerSettings()
    {
    }

    /// <summary>Creates settings at a sample rate.</summary>
    /// <param name="sampleRate">Samples per second, 8,000 to 192,000.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate" /> is outside that range.</exception>
    public GeneralMidiSynthesizerSettings(int sampleRate)
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
    /// How many frames are rendered per internal block, 8 to 1,024. Controller changes and the
    /// low-frequency oscillator land on a block boundary, so a smaller block responds sooner and
    /// costs a little more.
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

    /// <summary>
    /// How many notes may sound at once across every channel together, 1 to 512. Beyond it the
    /// synthesizer steals - the quietest released voice first, then the oldest.
    /// </summary>
    /// <remarks>
    /// One pool serves all sixteen channels, so a piece that spends its polyphony on a held pad has
    /// less left for its drums. That is the same arithmetic a hardware module does, and it is the
    /// reason this number rather than a per-channel one is what a consumer turns down on a slow
    /// device.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside that range.</exception>
    public int MaximumPolyphony
    {
        get => maximumPolyphony;
        set
        {
            if (value < 1 || value > 512)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "The polyphony must be between 1 and 512 voices.");
            }

            maximumPolyphony = value;
        }
    }

    /// <summary>The master output gain. Default 0.5, as every synthesizer in this family uses.</summary>
    public float MasterVolume
    {
        get => masterVolume;
        set => masterVolume = float.IsNaN(value) ? masterVolume : value < 0f ? 0f : value > 4f ? 4f : value;
    }

    /// <summary>
    /// Whether the shared reverb and chorus buses run. Default <see langword="true" />.
    /// </summary>
    /// <remarks>
    /// Every program asks the buses for a send level of its own and CC&#160;91 and CC&#160;93 move
    /// it, exactly as a General MIDI file expects. Turn them off when the application has reverb of
    /// its own downstream - a game engine usually does - so the music is not reverberated twice, or
    /// to get the cost back on a slow device.
    /// </remarks>
    public bool EnableReverbAndChorus { get; set; } = true;

    /// <summary>
    /// Whether a program's insert effect runs. Default <see langword="true" />.
    /// </summary>
    /// <remarks>
    /// A handful of programs name one effect from this package's own set - the phaser on the tine
    /// electric piano, the shaper on the overdriven guitar, the stereo widener on the metallic pad -
    /// and it runs once for the whole channel rather than once per note. A program that names none
    /// pays nothing either way.
    /// </remarks>
    public bool EnableInsertEffects { get; set; } = true;

    /// <summary>
    /// The seed the per-voice random streams are built from, so the same notes render the same
    /// samples every run. Default <see cref="DefaultRandomSeed" />.
    /// </summary>
    public int RandomSeed { get; set; } = DefaultRandomSeed;

    /// <summary>Makes an independent copy.</summary>
    /// <returns>A copy carrying the same values.</returns>
    public GeneralMidiSynthesizerSettings Clone() =>
        new GeneralMidiSynthesizerSettings
        {
            sampleRate = sampleRate,
            blockSize = blockSize,
            maximumPolyphony = maximumPolyphony,
            masterVolume = masterVolume,
            EnableReverbAndChorus = EnableReverbAndChorus,
            EnableInsertEffects = EnableInsertEffects,
            RandomSeed = RandomSeed,
        };
}
