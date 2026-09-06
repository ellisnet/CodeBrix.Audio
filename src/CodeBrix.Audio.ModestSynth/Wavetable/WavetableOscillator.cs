using System;
using CodeBrix.Audio.ModestSynth.Oscillators;
using CodeBrix.Audio.ModestSynth.Wavetable.Internal;

namespace CodeBrix.Audio.ModestSynth.Wavetable;

/// <summary>
/// A multi-frame wavetable - the <c>wavetable</c> waveform. It reads one cycle per period from a
/// frame of a loaded <see cref="WavetableFile" />, and moving <see cref="Position" /> walks the
/// table from timbre to timbre.
/// </summary>
/// <remarks>
/// <para>
/// WITHOUT A TABLE IT IS A SINE. That is measured behaviour, not a guess: the reference player
/// renders an <c>&lt;oscillator waveform="wavetable"&gt;</c> with no <c>wavetableFile</c> as a pure
/// sine at the note's own frequency and at the same level as <c>waveform="sine"</c> (measurements
/// document, item 13). A file that is missing or unreadable behaves the same way, and
/// <see cref="Problem" /> then carries one line saying why so the caller can report it.
/// </para>
/// <para>
/// POSITION. <see cref="Position" /> runs 0 (the first frame) to 1 (the last). With
/// <see cref="FrameInterpolation" /> on - the default, and what a morphing table wants - adjacent
/// frames are linearly crossfaded, so 0.5 of a four-frame table is halfway between frames 1 and 2.
/// With it off the position SNAPS to the nearest whole frame, which is what a table of unrelated
/// shapes wants. Either can be changed between blocks and is meant to be: it is the target of the
/// <c>OSCILLATOR_WAVETABLE_POSITION</c> and <c>OSCILLATOR_WAVETABLE_FRAME_INTERPOLATION</c>
/// bindings, so an LFO, an envelope, a MIDI CC or a knob can sweep it.
/// </para>
/// <para>
/// A position change is RAMPED across the block it arrives in rather than applied as a step, so a
/// modulator scanning the table does not click once per block. <see cref="Reset" /> snaps instead
/// of ramping, because a new note starts wherever it was told to.
/// </para>
/// <para>
/// ALIASING. The table is band-limited per octave when it is loaded, so a frame full of harmonics
/// stays clean at the top of the keyboard. Measured on a sawtooth frame, the worst alias below
/// 10 kHz sits 71 dB under the fundamental at 1 kHz, 84 dB at 4 kHz and 90 dB at 8 kHz - better at
/// every pitch than this package's own polyBLEP sawtooth. What that costs, and why oversampled
/// playback would not have worked, is written up on the internal mip-map type.
/// </para>
/// <para>
/// RANDOM PHASE. <see cref="Reset" /> takes the start phase in cycles, so
/// <c>ModestPatch.GetStartPhase</c> drives the format's <c>randomPhase</c> attribute. Layered
/// wavetable groups that all start at phase zero cancel each other; that is what the attribute is
/// for.
/// </para>
/// </remarks>
public sealed class WavetableOscillator : ModestOscillatorBase
{
    /// <summary>The <c>wavetablePosition</c> a new oscillator starts at: the first frame.</summary>
    public const double DefaultPosition = 0.0;

    private const double TwoPi = 2.0 * Math.PI;

    private WavetableFile table;
    private WavetableMipMap mipMap;
    private double targetPosition = DefaultPosition;
    private double currentPosition = DefaultPosition;
    private bool frameInterpolation = true;
    private string problem;
    private int level;
    private bool levelIsStale = true;

    /// <inheritdoc />
    public override string Waveform => ModestWaveforms.Wavetable;

    /// <summary>
    /// The table being played, or null - in which case the oscillator renders a sine, which is what
    /// the reference player does for a wavetable with no file.
    /// </summary>
    /// <remarks>
    /// Setting it clears <see cref="Problem" />, keeps the current phase and position, and takes
    /// effect on the next rendered sample. Tables are immutable and meant to be shared: hand the
    /// same one to every voice.
    /// </remarks>
    public WavetableFile Table
    {
        get => table;
        set
        {
            table = value;
            mipMap = value?.MipMap;
            problem = null;
            levelIsStale = true;
        }
    }

    /// <summary>Whether a table is loaded. When it is not, <see cref="Render" /> produces a sine.</summary>
    public bool HasTable => table != null;

    /// <summary>
    /// One line saying why there is no table, in the style of the reference player's preset
    /// validator - or null when nothing went wrong. Put it in a <c>Problems</c> list; never throw
    /// it.
    /// </summary>
    public string Problem => problem;

    /// <summary>
    /// <c>wavetablePosition</c>: where in the table to read, from 0.0 (the first frame) to 1.0 (the
    /// last). Default <see cref="DefaultPosition" />. Clamped; a non-finite value is ignored.
    /// </summary>
    /// <remarks>
    /// Changing it between blocks is the intended use - it is what
    /// <c>OSCILLATOR_WAVETABLE_POSITION</c> drives. The change is ramped across the next block, so
    /// reading this property back gives the TARGET, which the oscillator will have reached by the
    /// end of that block.
    /// </remarks>
    public double Position
    {
        get => targetPosition;
        set
        {
            if (double.IsNaN(value) || double.IsInfinity(value)) { return; }
            targetPosition = value < 0.0 ? 0.0 : value > 1.0 ? 1.0 : value;
        }
    }

    /// <summary>
    /// <c>wavetableFrameInterpolation</c>: whether adjacent frames are crossfaded as the position
    /// moves (<see langword="true" />, the default) or the position snaps to the nearest whole
    /// frame (<see langword="false" />). Changeable between blocks.
    /// </summary>
    public bool FrameInterpolation
    {
        get => frameInterpolation;
        set => frameInterpolation = value;
    }

    /// <summary>
    /// Which band-limited copy of the table the current pitch is being played from - 0 is the
    /// full-bandwidth one. Diagnostics only.
    /// </summary>
    public int MipLevel
    {
        get
        {
            EnsureLevel();
            return level;
        }
    }

    /// <summary>
    /// Loads a table through <see cref="WavetableFileCache" /> and plays it, recording the reason
    /// in <see cref="Problem" /> and falling back to a sine when it cannot.
    /// </summary>
    /// <param name="path">The .wav file, already resolved against the preset's own folder.</param>
    /// <param name="frameSize">
    /// The frame size to use when the file carries no <c>clm&#160;</c> chunk; 0 for
    /// <see cref="WavetableFile.DefaultFrameSize" />.
    /// </param>
    /// <returns><see langword="true" /> when a table was loaded.</returns>
    /// <remarks>This opens a file. Call it while building a voice, never inside a render callback.</remarks>
    public bool TryLoad(string path, int frameSize)
    {
        bool loaded = WavetableFileCache.TryGetOrLoad(path, frameSize, out WavetableFile file,
            out string reason);

        table = loaded ? file : null;
        mipMap = loaded ? file.MipMap : null;
        problem = loaded ? null : reason;
        levelIsStale = true;
        return loaded;
    }

    /// <summary>
    /// Records why there is no table, for the cases the caller detects rather than this oscillator
    /// - a preset with no <c>wavetableFile</c> attribute at all, for instance.
    /// </summary>
    /// <param name="reason">
    /// One human-readable line. A blank reason clears the problem instead of setting one.
    /// </param>
    public void ReportProblem(string reason)
        => problem = string.IsNullOrWhiteSpace(reason) ? null : reason;

    /// <inheritdoc />
    public override void SetSampleRate(int sampleRate)
    {
        base.SetSampleRate(sampleRate);
        levelIsStale = true;
    }

    /// <inheritdoc />
    public override void SetFrequency(double frequencyHz)
    {
        base.SetFrequency(frequencyHz);
        levelIsStale = true;
    }

    /// <summary>
    /// Restarts the oscillator at a given phase and snaps the position to its target.
    /// </summary>
    /// <param name="phase">
    /// The start phase in cycles; pass <c>ModestPatch.GetStartPhase</c> to honour
    /// <c>randomPhase</c>.
    /// </param>
    public override void Reset(double phase)
    {
        base.Reset(phase);
        currentPosition = targetPosition;
    }

    /// <inheritdoc />
    public override void Render(Span<float> buffer)
    {
        if (buffer.Length == 0) { return; }

        if (mipMap == null)
        {
            RenderSine(buffer);
            return;
        }

        EnsureLevel();

        float[] data = mipMap.LevelData(level);
        int length = mipMap.LevelLength(level);
        int frames = mipMap.FrameCount;
        int lastFrame = frames - 1;

        double phase = Phase;
        double increment = PhaseIncrement;
        double position = currentPosition;
        double positionStep = (targetPosition - currentPosition) / buffer.Length;
        bool blendFrames = frameInterpolation && frames > 1;

        for (int i = 0; i < buffer.Length; i++)
        {
            double framePosition = position * lastFrame;

            int lowFrame;
            int highFrame;
            double frameBlend;

            if (blendFrames)
            {
                lowFrame = (int)framePosition;
                if (lowFrame < 0) { lowFrame = 0; }
                if (lowFrame >= lastFrame)
                {
                    lowFrame = lastFrame;
                    highFrame = lastFrame;
                    frameBlend = 0.0;
                }
                else
                {
                    highFrame = lowFrame + 1;
                    frameBlend = framePosition - lowFrame;
                }
            }
            else
            {
                lowFrame = (int)(framePosition + 0.5);
                if (lowFrame < 0) { lowFrame = 0; }
                if (lowFrame > lastFrame) { lowFrame = lastFrame; }
                highFrame = lowFrame;
                frameBlend = 0.0;
            }

            double x = phase * length;
            int low = (int)x;
            if (low >= length) { low = length - 1; }
            else if (low < 0) { low = 0; }

            double fraction = x - low;
            int high = low + 1;
            if (high >= length) { high = 0; }

            int lowBase = lowFrame * length;
            double a = data[lowBase + low];
            double sample = a + ((data[lowBase + high] - a) * fraction);

            if (frameBlend > 0.0)
            {
                int highBase = highFrame * length;
                double b = data[highBase + low];
                double other = b + ((data[highBase + high] - b) * fraction);
                sample += (other - sample) * frameBlend;
            }

            buffer[i] = (float)sample;

            phase += increment;
            if (phase >= 1.0) { phase -= Math.Floor(phase); }
            position += positionStep;
        }

        Phase = phase;
        currentPosition = targetPosition;
    }

    private void EnsureLevel()
    {
        if (!levelIsStale) { return; }

        level = mipMap == null ? 0 : mipMap.SelectLevel(Frequency, SampleRate);
        levelIsStale = false;
    }

    private void RenderSine(Span<float> buffer)
    {
        double phase = Phase;
        double increment = PhaseIncrement;

        for (int i = 0; i < buffer.Length; i++)
        {
            buffer[i] = (float)Math.Sin(TwoPi * phase);

            phase += increment;
            if (phase >= 1.0) { phase -= Math.Floor(phase); }
        }

        Phase = phase;
        currentPosition = targetPosition;
    }
}
