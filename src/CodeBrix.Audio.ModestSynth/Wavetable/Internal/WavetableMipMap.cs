using System;
using CodeBrix.Audio.ModestSynth.Internal;

namespace CodeBrix.Audio.ModestSynth.Wavetable.Internal;

/// <summary>
/// The band-limited copies of every frame in a wavetable - one set per octave of pitch - and the
/// rule for picking the right set for a given note.
/// </summary>
/// <remarks>
/// <para>
/// WHY MIP MAPS RATHER THAN OVERSAMPLED PLAYBACK. A wavetable frame is one cycle of an arbitrary
/// shape, so it can hold every harmonic up to half its length: a 2048-sample frame carries 1024 of
/// them. Played at 1 kHz on a 48 kHz stream, harmonics 25 and up are above Nyquist and fold back
/// into the audible band as inharmonic aliases. Rendering at 4x and decimating does not fix that -
/// harmonic 192 of a 1 kHz note lands on exactly 192 kHz, which folds to DC at any oversampling
/// factor - so the harmonics have to be REMOVED before playback, not filtered afterwards.
/// </para>
/// <para>
/// Each level keeps harmonics 1 to <c>H</c>, halving <c>H</c> as the level rises, and stores the
/// result at <see cref="OversampleFactor" /> samples per cycle of its own top harmonic (floored at
/// <see cref="MinimumLevelLength" /> and capped at the analysis length). That oversampling is what
/// makes plain linear interpolation good enough in the render loop: the images linear interpolation
/// leaves sit at the store's own sampling rate minus the top harmonic, which
/// <see cref="OversampleFactor" /> pushes into the deep stopband of the interpolator. Measured on a
/// sawtooth frame, the worst alias below 10 kHz sits 71 dB under the fundamental at 1 kHz, 84 dB at
/// 4 kHz and 90 dB at 8 kHz - better at every pitch than this package's own polyBLEP sawtooth, whose
/// figures are -56, -49 and -42. Halving <see cref="OversampleFactor" /> to 4 and the floor to 64
/// costs about 26 dB of that at 1 kHz (measured -45.0) and saves a third of the memory, which is
/// the trade if it ever needs making.
/// </para>
/// <para>
/// The cost is memory: about 4.5 times the frame data, once, for a 2048-sample frame. A 256-frame
/// Serum table therefore occupies roughly 9 MB. That is why a table is loaded through
/// <see cref="WavetableFileCache" /> and shared by every voice rather than decoded per voice.
/// </para>
/// <para>
/// Band limiting is done in the frequency domain, which is exact: transform one cycle, keep the
/// bins the level is allowed, and transform back at the level's own length. There is no filter
/// design and no transition band.
/// </para>
/// </remarks>
internal sealed class WavetableMipMap
{
    /// <summary>How many stored samples there are per cycle of a level's highest kept harmonic.</summary>
    internal const int OversampleFactor = 8;

    /// <summary>The shortest a stored level may be, so the top levels stay cheap to interpolate.</summary>
    internal const int MinimumLevelLength = 256;

    /// <summary>The longest cycle the analysis will run at; longer frames are analysed at this length.</summary>
    internal const int MaximumAnalysisLength = 65536;

    private readonly int frameCount;
    private readonly int[] lengths;
    private readonly int[] harmonics;
    private readonly float[][] levels;

    private WavetableMipMap(int frameCount, int[] lengths, int[] harmonics, float[][] levels)
    {
        this.frameCount = frameCount;
        this.lengths = lengths;
        this.harmonics = harmonics;
        this.levels = levels;
    }

    /// <summary>How many mip levels there are; level 0 is the full-bandwidth one.</summary>
    internal int LevelCount => levels.Length;

    /// <summary>The total number of bytes the stored levels occupy.</summary>
    internal long ByteCount
    {
        get
        {
            long total = 0L;
            for (int i = 0; i < levels.Length; i++) { total += (long)levels[i].Length * sizeof(float); }
            return total;
        }
    }

    /// <summary>
    /// Builds the mip map for a whole wavetable.
    /// </summary>
    /// <param name="samples">Every frame's samples, concatenated: <c>frameCount * frameSize</c> long.</param>
    /// <param name="frameSize">The samples per frame.</param>
    /// <param name="frameCount">How many frames there are.</param>
    /// <returns>The mip map.</returns>
    internal static WavetableMipMap Build(float[] samples, int frameSize, int frameCount)
    {
        int analysisLength = FourierPlan.NextPowerOfTwo(frameSize);
        if (analysisLength > MaximumAnalysisLength) { analysisLength = MaximumAnalysisLength; }

        int baseHarmonics = analysisLength / 2;
        int levelCount = 1;
        for (int h = baseHarmonics; h > 1; h >>= 1) { levelCount++; }

        int[] lengths = new int[levelCount];
        int[] harmonics = new int[levelCount];
        float[][] levels = new float[levelCount][];

        int minimumLength = MinimumLevelLength < analysisLength ? MinimumLevelLength : analysisLength;

        for (int level = 0; level < levelCount; level++)
        {
            int kept = baseHarmonics >> level;
            if (kept < 1) { kept = 1; }

            int length = FourierPlan.NextPowerOfTwo(OversampleFactor * kept);
            if (length < minimumLength) { length = minimumLength; }
            if (length > analysisLength) { length = analysisLength; }

            harmonics[level] = kept;
            lengths[level] = length;
        }

        // Level 0 keeps every harmonic at the analysis length, so when the frames are already that
        // long it IS the file's own samples and there is nothing to compute or to copy.
        bool shareLevelZero = analysisLength == frameSize;
        if (shareLevelZero) { levels[0] = samples; }

        FourierPlan plan = new FourierPlan(analysisLength);
        double[] real = new double[analysisLength];
        double[] imaginary = new double[analysisLength];
        double[] outputReal = new double[analysisLength];
        double[] outputImaginary = new double[analysisLength];
        double[] spectrumReal = new double[analysisLength];
        double[] spectrumImaginary = new double[analysisLength];

        for (int level = shareLevelZero ? 1 : 0; level < levelCount; level++)
        {
            long stored = (long)frameCount * lengths[level];
            if (stored > int.MaxValue)
            {
                throw new InvalidOperationException("The wavetable is too large to band-limit.");
            }

            levels[level] = new float[(int)stored];
        }

        for (int frame = 0; frame < frameCount; frame++)
        {
            LoadCycle(samples, frame, frameSize, analysisLength, real, imaginary);
            plan.Transform(real, imaginary, analysisLength, false);
            Array.Copy(real, spectrumReal, analysisLength);
            Array.Copy(imaginary, spectrumImaginary, analysisLength);

            for (int level = shareLevelZero ? 1 : 0; level < levelCount; level++)
            {
                Synthesise(plan, spectrumReal, spectrumImaginary, analysisLength, harmonics[level],
                    lengths[level], outputReal, outputImaginary);

                float[] destination = levels[level];
                int offset = frame * lengths[level];
                double scale = 1.0 / analysisLength;
                for (int i = 0; i < lengths[level]; i++)
                {
                    destination[offset + i] = (float)(outputReal[i] * scale);
                }
            }
        }

        return new WavetableMipMap(frameCount, lengths, harmonics, levels);
    }

    /// <summary>
    /// Picks the level to play a note from: the fullest-bandwidth one whose harmonics all fit
    /// below Nyquist.
    /// </summary>
    /// <param name="frequencyHz">The pitch being played, in Hz.</param>
    /// <param name="sampleRate">The sample rate, in Hz.</param>
    /// <returns>The level index.</returns>
    internal int SelectLevel(double frequencyHz, int sampleRate)
    {
        if (frequencyHz <= 0.0 || sampleRate <= 0) { return 0; }

        double allowed = (sampleRate * 0.5) / frequencyHz;
        for (int level = 0; level < harmonics.Length; level++)
        {
            if (harmonics[level] <= allowed) { return level; }
        }

        return harmonics.Length - 1;
    }

    /// <summary>The stored samples of one level, all frames concatenated.</summary>
    /// <param name="level">The level index.</param>
    /// <returns>The level's samples.</returns>
    internal float[] LevelData(int level) => levels[level];

    /// <summary>How many stored samples one frame of a level holds.</summary>
    /// <param name="level">The level index.</param>
    /// <returns>The stored frame length.</returns>
    internal int LevelLength(int level) => lengths[level];

    /// <summary>The highest harmonic a level keeps.</summary>
    /// <param name="level">The level index.</param>
    /// <returns>The harmonic number.</returns>
    internal int LevelHarmonics(int level) => harmonics[level];

    /// <summary>How many frames the map covers.</summary>
    internal int FrameCount => frameCount;

    private static void LoadCycle(float[] samples, int frame, int frameSize, int analysisLength,
        double[] real, double[] imaginary)
    {
        Array.Clear(imaginary, 0, analysisLength);
        int offset = frame * frameSize;

        if (analysisLength == frameSize)
        {
            for (int i = 0; i < analysisLength; i++) { real[i] = samples[offset + i]; }
            return;
        }

        // A frame whose length is not a power of two is resampled onto one that is, treating the
        // frame as the single cycle it is meant to be, so the wrap at the end interpolates back to
        // the start rather than to silence.
        double ratio = (double)frameSize / analysisLength;
        for (int i = 0; i < analysisLength; i++)
        {
            double position = i * ratio;
            int low = (int)position;
            if (low >= frameSize) { low = frameSize - 1; }

            int high = low + 1;
            if (high >= frameSize) { high = 0; }

            double fraction = position - low;
            double a = samples[offset + low];
            double b = samples[offset + high];
            real[i] = a + ((b - a) * fraction);
        }
    }

    private static void Synthesise(FourierPlan plan, double[] spectrumReal, double[] spectrumImaginary,
        int analysisLength, int keptHarmonics, int length, double[] outputReal, double[] outputImaginary)
    {
        Array.Clear(outputReal, 0, length);
        Array.Clear(outputImaginary, 0, length);

        // DC survives every level: a frame may legitimately be offset, and dropping the offset at
        // some pitches and not others would make the table's level jump as a note moves.
        outputReal[0] = spectrumReal[0];
        outputImaginary[0] = spectrumImaginary[0];

        int highest = keptHarmonics;
        int limit = (length / 2) - 1;
        if (length == analysisLength) { limit = length / 2; }
        if (highest > limit) { highest = limit; }

        for (int k = 1; k <= highest; k++)
        {
            outputReal[k] = spectrumReal[k];
            outputImaginary[k] = spectrumImaginary[k];
            outputReal[length - k] = spectrumReal[analysisLength - k];
            outputImaginary[length - k] = spectrumImaginary[analysisLength - k];
        }

        plan.Transform(outputReal, outputImaginary, length, true);
    }
}
