using System;

namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// The settings for SFZ synthesis: sample rate, block size, polyphony, and the random seed behind
/// round-robin layer selection.
/// </summary>
/// <remarks>
/// The seed exists because <c>lorand</c>/<c>hirand</c> regions draw a random number per note-on. With a
/// fixed seed (the default) the same MIDI sequence renders identically run after run, which is what the
/// offline renderer and the regression tests want; set <see cref="RandomSeed"/> to vary performances.
/// </remarks>
public sealed class SfzSynthesizerSettings
{
    private static readonly int defaultBlockSize = 64;
    private static readonly int defaultMaximumPolyphony = 64;
    private static readonly int defaultRandomSeed = 12345;

    private int sampleRate;
    private int blockSize;
    private int maximumPolyphony;

    /// <summary>Creates settings for the given output sample rate.</summary>
    /// <param name="sampleRate">The synthesis sample rate in Hz.</param>
    public SfzSynthesizerSettings(int sampleRate)
    {
        CheckSampleRate(sampleRate);

        this.sampleRate = sampleRate;
        blockSize = defaultBlockSize;
        maximumPolyphony = defaultMaximumPolyphony;
        RandomSeed = defaultRandomSeed;
    }

    /// <summary>The synthesis sample rate in Hz. 16000 to 192000.</summary>
    public int SampleRate
    {
        get => sampleRate;
        set
        {
            CheckSampleRate(value);
            sampleRate = value;
        }
    }

    /// <summary>The number of frames rendered per internal block. 8 to 1024.</summary>
    public int BlockSize
    {
        get => blockSize;
        set
        {
            CheckBlockSize(value);
            blockSize = value;
        }
    }

    /// <summary>The maximum number of simultaneously sounding voices. 8 to 256.</summary>
    public int MaximumPolyphony
    {
        get => maximumPolyphony;
        set
        {
            CheckMaximumPolyphony(value);
            maximumPolyphony = value;
        }
    }

    /// <summary>
    /// The seed for random layer selection. The default is fixed so identical input renders
    /// identically.
    /// </summary>
    public int RandomSeed { get; set; }

    private static void CheckSampleRate(int value)
    {
        if (!(16000 <= value && value <= 192000))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The sample rate must be between 16000 and 192000.");
        }
    }

    private static void CheckBlockSize(int value)
    {
        if (!(8 <= value && value <= 1024))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The block size must be between 8 and 1024.");
        }
    }

    private static void CheckMaximumPolyphony(int value)
    {
        if (!(8 <= value && value <= 256))
        {
            throw new ArgumentOutOfRangeException(nameof(value), value, "The maximum polyphony must be between 8 and 256.");
        }
    }
}
