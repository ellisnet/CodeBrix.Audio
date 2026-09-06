using System;
using System.Collections.Generic;
using CodeBrix.Audio.ModestSynth.Fm;
using CodeBrix.Audio.ModestSynth.Harmonic;
using CodeBrix.Audio.ModestSynth.Oscillators;
using CodeBrix.Audio.ModestSynth.Wavetable;

namespace CodeBrix.Audio.ModestSynth.Patch;

/// <summary>
/// Everything that describes one oscillator: which waveform, and the parameters that waveform
/// takes. It is the standalone form of a Decent Sampler group's oscillator attributes, so the same
/// object serves a synth patch written in code and one read out of a preset.
/// </summary>
/// <remarks>
/// <para>
/// Every property is named for the attribute it mirrors, minus the element it happens to sit on:
/// <see cref="Damping" /> is <c>damping</c>, <see cref="HarmonicOddEvenBalance" /> is
/// <c>harmonicOddEvenBalance</c>, and the six entries in <see cref="FmOperators" /> carry the
/// <c>fmOpN...</c> family. Every default is the format's documented default, so a patch that is
/// only told its waveform behaves like an <c>&lt;oscillator&gt;</c> with nothing else on it.
/// </para>
/// <para>
/// A patch is a plain settings object: it holds no audio state, and one patch can build any number
/// of voices. Out-of-range values are clamped rather than rejected - a preset attribute is never
/// worth an exception - and the clamping is documented on each property.
/// </para>
/// <para>
/// This release generates <c>sine</c>, <c>saw</c>, <c>square</c>, <c>triangle</c>, <c>noise</c>,
/// <c>pluck1</c>, <c>wavetable</c>, <c>harmonic</c> and <c>fm6op</c>. The undocumented
/// <c>formant</c> waveform is recognised so that a patch survives a round trip today and starts
/// making sound when that generator ships; <see cref="CanCreateOscillator" /> is the honest
/// answer for the waveform currently selected.
/// </para>
/// </remarks>
public sealed class ModestPatch
{
    /// <summary>The number of harmonic partials the additive waveform can address.</summary>
    public const int MaximumPartials = 64;

    /// <summary>The <c>numPartials</c> default: eight active partials.</summary>
    public const int DefaultNumPartials = 8;

    /// <summary>The <c>wavetableFrameSize</c> default, in samples per frame.</summary>
    public const int DefaultWavetableFrameSize = 2048;

    private readonly double[] partialLevels = new double[MaximumPartials];
    private readonly ModestFmOperator[] fmOperators;

    private double damping = Pluck1Oscillator.DefaultDamping;
    private double pluckType = Pluck1Oscillator.DefaultPluckType;
    private int wavetableFrameSize = DefaultWavetableFrameSize;
    private double wavetablePosition;
    private int numPartials = DefaultNumPartials;
    private double harmonicTilt;
    private double harmonicOddEvenBalance = 0.5;
    private double harmonicNormalization;
    private int fmAlgorithm = 1;

    /// <summary>Creates a patch carrying every documented default: a plain sine oscillator.</summary>
    public ModestPatch()
    {
        fmOperators = new ModestFmOperator[ModestFmOperator.OperatorCount];
        for (int i = 0; i < fmOperators.Length; i++)
        {
            fmOperators[i] = new ModestFmOperator(i + 1);
        }
    }

    // ---------------------------------------------------------------- the oscillator element

    /// <summary>
    /// <c>waveform</c>: which shape to generate. Default <see cref="ModestWaveform.Sine" />, which
    /// is what an <c>&lt;oscillator&gt;</c> with no waveform attribute produces.
    /// </summary>
    public ModestWaveform Waveform { get; set; } = ModestWaveform.Sine;

    /// <summary>
    /// <c>damping</c>: how long a <c>pluck1</c> string rings, from 0.0 (heavily damped and short)
    /// to 1.0 (barely damped and long). Default 0.5. Clamped. Ignored by every other waveform.
    /// </summary>
    public double Damping
    {
        get => damping;
        set { if (IsFinite(value)) { damping = Clamp01(value); } }
    }

    /// <summary>
    /// <c>pluckType</c>: what a <c>pluck1</c> string is struck with, from 0.0 (a smooth triangle,
    /// soft and mellow) to 1.0 (a noise burst, bright and aggressive). Default 0.5. Clamped.
    /// Ignored by every other waveform.
    /// </summary>
    public double PluckType
    {
        get => pluckType;
        set { if (IsFinite(value)) { pluckType = Clamp01(value); } }
    }

    /// <summary>
    /// <c>randomPhase</c>: whether each note-on starts the oscillator at a random point in its
    /// cycle instead of always at zero. Default <see langword="false" />.
    /// </summary>
    /// <remarks>
    /// Turn it on when layering several voices of the same waveform - detuned unison, or a stack of
    /// groups - because voices that all start at phase zero reinforce and cancel each other instead
    /// of thickening. The format documents the attribute for the wavetable waveform; here it drives
    /// <see cref="GetStartPhase" /> for every waveform that has a phase to randomise, which includes
    /// a <c>pluck1</c> string's excitation. It means nothing to <c>noise</c>.
    /// </remarks>
    public bool RandomPhase { get; set; }

    /// <summary>
    /// The seed handed to whichever oscillator draws random numbers - <c>noise</c> and the noise
    /// part of a <c>pluck1</c> excitation. Default 0, which leaves each oscillator on its own
    /// default seed.
    /// </summary>
    /// <remarks>
    /// Nothing here is ever un-seeded: the same patch renders the same samples every run. Give
    /// layered voices different seeds when you want them to differ, rather than hoping they will.
    /// </remarks>
    public uint Seed { get; set; }

    // ------------------------------------------------------------------ the wavetable family

    /// <summary>
    /// <c>wavetableFile</c>: the path to the multi-frame .wav file. Default null, which renders a
    /// plain sine - the reference player's own behaviour for a wavetable oscillator with no file.
    /// </summary>
    /// <remarks>
    /// The format writes this path RELATIVE to the .dspreset file, so combine it with the preset's
    /// own folder before building a voice: <see cref="CreateOscillator(int)" /> hands it to
    /// <see cref="WavetableFileCache" /> as it stands, and a relative path there resolves against
    /// the process's current directory, which is rarely what a preset meant.
    /// </remarks>
    public string WavetableFile { get; set; }

    /// <summary>
    /// A wavetable you already have, used INSTEAD of reading <see cref="WavetableFile" />. Default
    /// null.
    /// </summary>
    /// <remarks>
    /// The format has no attribute for this: it is how a table built in code - through
    /// <c>WavetableFile.FromSamples</c> - or one already decoded is played, which is what
    /// <see cref="ModestSynthPresets" />' wavetable pad does. It also makes
    /// <see cref="CreateOscillator(int)" /> touch no file at all, which matters when voices are built
    /// on a time-critical path.
    /// </remarks>
    public WavetableFile WavetableTable { get; set; }

    /// <summary>
    /// <c>wavetableFrameSize</c>: samples per wavetable frame. Default
    /// <see cref="DefaultWavetableFrameSize" />. A file carrying a Serum-compatible <c>clm</c>
    /// chunk overrides whatever is set here. Values below 2 are ignored.
    /// </summary>
    public int WavetableFrameSize
    {
        get => wavetableFrameSize;
        set { if (value >= 2) { wavetableFrameSize = value; } }
    }

    /// <summary>
    /// <c>wavetablePosition</c>: where in the table to read, from 0.0 (first frame) to 1.0 (last).
    /// Default 0.0. Clamped.
    /// </summary>
    public double WavetablePosition
    {
        get => wavetablePosition;
        set { if (IsFinite(value)) { wavetablePosition = Clamp01(value); } }
    }

    /// <summary>
    /// <c>wavetableFrameInterpolation</c>: whether adjacent frames crossfade as the position moves
    /// (<see langword="true" />, the default, and what a morphing wavetable wants) or the position
    /// snaps to the nearest whole frame (<see langword="false" />, for a table of unrelated shapes).
    /// </summary>
    public bool WavetableFrameInterpolation { get; set; } = true;

    // ------------------------------------------------------------------- the harmonic family

    /// <summary>
    /// <c>numPartials</c>: how many harmonic partials are active, from 1 to
    /// <see cref="MaximumPartials" />. Default <see cref="DefaultNumPartials" />. Clamped.
    /// </summary>
    public int NumPartials
    {
        get => numPartials;
        set => numPartials = value < 1 ? 1 : value > MaximumPartials ? MaximumPartials : value;
    }

    /// <summary>
    /// <c>harmonicTilt</c>: spectral tilt, from -1.0 to 1.0. Positive values pull the higher
    /// partials down (darker), negative values push them up (brighter). Default 0.0. Clamped.
    /// </summary>
    public double HarmonicTilt
    {
        get => harmonicTilt;
        set { if (IsFinite(value)) { harmonicTilt = value < -1.0 ? -1.0 : value > 1.0 ? 1.0 : value; } }
    }

    /// <summary>
    /// <c>harmonicOddEvenBalance</c>: crossfades emphasis between odd partials (0.0) and even ones
    /// (1.0), balanced at 0.5. Default 0.5. Clamped.
    /// </summary>
    public double HarmonicOddEvenBalance
    {
        get => harmonicOddEvenBalance;
        set { if (IsFinite(value)) { harmonicOddEvenBalance = Clamp01(value); } }
    }

    /// <summary>
    /// <c>harmonicNormalization</c>: how much loudness compensation to apply to the summed
    /// partials, from 0.0 (none) to 1.0 (full). Default 0.0. Clamped.
    /// </summary>
    public double HarmonicNormalization
    {
        get => harmonicNormalization;
        set { if (IsFinite(value)) { harmonicNormalization = Clamp01(value); } }
    }

    /// <summary>
    /// Reads one partial's level - the <c>harmonicPartialNLevel</c> attributes.
    /// </summary>
    /// <param name="partial">Which partial, 1 (the fundamental) to <see cref="MaximumPartials" />.</param>
    /// <returns>The level, 0.0 to 1.0. Every partial starts at 0.0, which is the format's default.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="partial" /> is outside 1 to 64.</exception>
    public double GetPartialLevel(int partial)
    {
        ValidatePartial(partial);
        return partialLevels[partial - 1];
    }

    /// <summary>
    /// Sets one partial's level - the <c>harmonicPartialNLevel</c> attributes.
    /// </summary>
    /// <param name="partial">Which partial, 1 (the fundamental) to <see cref="MaximumPartials" />.</param>
    /// <param name="level">The level, 0.0 to 1.0. Clamped; a non-finite value is ignored.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="partial" /> is outside 1 to 64.</exception>
    public void SetPartialLevel(int partial, double level)
    {
        ValidatePartial(partial);
        if (IsFinite(level)) { partialLevels[partial - 1] = Clamp01(level); }
    }

    // ------------------------------------------------------------------------- the FM family

    /// <summary>
    /// <c>fmAlgorithm</c>: which of the 32 classic operator topologies to use, 1 to 32. Default 1.
    /// Clamped.
    /// </summary>
    public int FmAlgorithm
    {
        get => fmAlgorithm;
        set => fmAlgorithm = value < 1 ? 1 : value > 32 ? 32 : value;
    }

    /// <summary>
    /// The six FM operators, in order, so <c>FmOperators[0]</c> is operator 1. The list itself is
    /// fixed; the operators in it are settings objects you change in place.
    /// </summary>
    public IReadOnlyList<ModestFmOperator> FmOperators => fmOperators;

    /// <summary>
    /// One FM operator by its number, which is how the attributes name them.
    /// </summary>
    /// <param name="number">The operator number, 1 to <see cref="ModestFmOperator.OperatorCount" />.</param>
    /// <returns>The operator's settings.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="number" /> is outside 1 to 6.</exception>
    public ModestFmOperator GetFmOperator(int number)
    {
        if (number < 1 || number > ModestFmOperator.OperatorCount)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number,
                "An fm6op oscillator has operators 1 to " + ModestFmOperator.OperatorCount + ".");
        }

        return fmOperators[number - 1];
    }

    // ---------------------------------------------------------------------- building a voice

    /// <summary>
    /// Whether this release can build an oscillator for the currently selected
    /// <see cref="Waveform" />.
    /// </summary>
    public bool CanCreateOscillator => ModestOscillatorFactory.IsSupported(Waveform);

    /// <summary>
    /// Builds one voice's oscillator from this patch, at <see cref="ModestOscillatorBase.DefaultSampleRate" />.
    /// </summary>
    /// <returns>A new oscillator carrying this patch's parameters.</returns>
    /// <exception cref="NotSupportedException">
    /// The selected waveform's sound generator has not shipped yet; see
    /// <see cref="CanCreateOscillator" />.
    /// </exception>
    public IModestOscillator CreateOscillator()
        => CreateOscillator(ModestOscillatorBase.DefaultSampleRate);

    /// <summary>
    /// Builds one voice's oscillator from this patch.
    /// </summary>
    /// <param name="sampleRate">The rate the voice will be rendered at, in Hz.</param>
    /// <returns>A new oscillator carrying this patch's parameters.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate" /> is not positive.</exception>
    /// <exception cref="NotSupportedException">
    /// The selected waveform's sound generator has not shipped yet; see
    /// <see cref="CanCreateOscillator" />.
    /// </exception>
    /// <remarks>
    /// The oscillator comes back at the requested rate and at its default pitch, NOT reset: set the
    /// pitch with <see cref="IModestOscillator.SetFrequency" /> and then call
    /// <see cref="IModestOscillator.Reset" /> with <see cref="GetStartPhase" /> at note-on. Give
    /// every voice its own oscillator; they hold per-voice state.
    /// </remarks>
    public IModestOscillator CreateOscillator(int sampleRate)
    {
        IModestOscillator oscillator = ModestOscillatorFactory.Create(Waveform);
        oscillator.SetSampleRate(sampleRate);

        NoiseOscillator noise = oscillator as NoiseOscillator;
        if (noise != null && Seed != 0u) { noise.Seed = Seed; }

        Pluck1Oscillator pluck = oscillator as Pluck1Oscillator;
        if (pluck != null)
        {
            pluck.Damping = damping;
            pluck.PluckType = pluckType;
            if (Seed != 0u) { pluck.Seed = Seed; }
        }

        Fm6OpOscillator fm = oscillator as Fm6OpOscillator;
        if (fm != null) { fm.ApplyPatch(this); }
        WavetableOscillator wavetable = oscillator as WavetableOscillator;
        if (wavetable != null)
        {
            wavetable.Position = wavetablePosition;
            wavetable.FrameInterpolation = WavetableFrameInterpolation;

            if (WavetableTable != null)
            {
                wavetable.Table = WavetableTable;
            }
            else if (string.IsNullOrWhiteSpace(WavetableFile))
            {
                // The reference player renders a wavetable oscillator that names no file as a plain
                // sine, which is what an oscillator with no table does. Recording the reason lets a
                // caller put it in Problems instead of wondering why the patch sounds like a sine.
                wavetable.ReportProblem(
                    "No file reference: <oscillator> @wavetableFile is not set; " +
                    "the wavetable oscillator falls back to a sine.");
            }
            else
            {
                wavetable.TryLoad(WavetableFile, wavetableFrameSize);
            }
        }

        HarmonicOscillator harmonic = oscillator as HarmonicOscillator;
        if (harmonic != null)
        {
            harmonic.NumPartials = numPartials;
            harmonic.Tilt = harmonicTilt;
            harmonic.OddEvenBalance = harmonicOddEvenBalance;
            harmonic.Normalization = harmonicNormalization;

            for (int partial = 1; partial <= MaximumPartials; partial++)
            {
                harmonic.SetPartialLevel(partial, partialLevels[partial - 1]);
            }
        }

        return oscillator;
    }

    /// <summary>
    /// The phase to reset a new voice to - 0 when <see cref="RandomPhase" /> is off, and a
    /// scattered value in [0, 1) when it is on.
    /// </summary>
    /// <param name="voiceSeed">
    /// Something that differs from voice to voice: a note-on counter, a voice index, a note number
    /// combined with a counter. The same seed always yields the same phase, which is what keeps a
    /// rendered result reproducible.
    /// </param>
    /// <returns>A phase in cycles, in [0, 1).</returns>
    public double GetStartPhase(uint voiceSeed)
    {
        if (!RandomPhase) { return 0.0; }

        // A cheap integer hash (the 32-bit finaliser shape), so neighbouring voice numbers land far
        // apart in the cycle instead of a step apart.
        uint x = voiceSeed + Seed + 0x9E3779B9u;
        x ^= x >> 16;
        x *= 0x7FEB352Du;
        x ^= x >> 15;
        x *= 0x846CA68Bu;
        x ^= x >> 16;

        return (x >> 8) * (1.0 / 16777216.0);
    }

    private static void ValidatePartial(int partial)
    {
        if (partial < 1 || partial > MaximumPartials)
        {
            throw new ArgumentOutOfRangeException(nameof(partial), partial,
                "Harmonic partials are numbered 1 to " + MaximumPartials + ".");
        }
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static double Clamp01(double value) => value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
}
