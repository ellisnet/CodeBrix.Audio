using System;

namespace CodeBrix.Audio.ModestSynth.Oscillators;

/// <summary>
/// The sample-rate, pitch, phase and note bookkeeping every oscillator in this package shares.
/// </summary>
/// <remarks>
/// <para>
/// Derived types implement <see cref="Render" /> and read <see cref="Phase" /> and
/// <see cref="PhaseIncrement" />, both measured in CYCLES: phase runs 0 to 1 over one period, and
/// the increment is how much of a cycle one sample advances (frequency divided by sample rate).
/// </para>
/// <para>
/// Every oscillator in the package is an <see cref="IModestVoiceOscillator" />, so anything that
/// plays notes - the standalone synthesizer, or the Decent Sampler adapter - has ONE contract to
/// drive whatever the waveform turns out to be. The default note behaviour here is the one a
/// waveform that simply runs wants: <see cref="NoteOn" /> records the velocity and marks the key
/// down, <see cref="NoteOff" /> marks it up, and <see cref="IsFinished" /> is never true because a
/// sine has no end of its own. The two waveforms that DO decay by themselves - <c>pluck1</c> and
/// <c>fm6op</c> - override <see cref="IsFinished" />.
/// </para>
/// </remarks>
public abstract class ModestOscillatorBase : IModestVoiceOscillator
{
    /// <summary>The velocity a note starts at when nothing says otherwise.</summary>
    public const int DefaultNoteVelocity = 127;

    /// <summary>The sample rate a new oscillator starts at, in Hz, until told otherwise.</summary>
    public const int DefaultSampleRate = 48000;

    /// <summary>The pitch a new oscillator starts at, in Hz, until told otherwise.</summary>
    public const double DefaultFrequency = 440.0;

    private int sampleRate = DefaultSampleRate;
    private double frequency = DefaultFrequency;
    private double phaseIncrement = DefaultFrequency / DefaultSampleRate;

    /// <summary>Initialises the oscillator at <see cref="DefaultSampleRate" /> and <see cref="DefaultFrequency" />.</summary>
    protected ModestOscillatorBase()
    {
    }

    /// <inheritdoc />
    public abstract string Waveform { get; }

    /// <inheritdoc />
    public int SampleRate => sampleRate;

    /// <inheritdoc />
    public double Frequency => frequency;

    /// <inheritdoc />
    public bool IsKeyDown { get; protected set; }

    /// <inheritdoc />
    /// <remarks>
    /// False for every waveform that simply runs. A waveform with a decay of its own overrides this
    /// and reports when there is nothing audible left.
    /// </remarks>
    public virtual bool IsFinished => false;

    /// <summary>
    /// The velocity the current note was started at, 0 to 127. It is
    /// <see cref="DefaultNoteVelocity" /> until <see cref="NoteOn" /> says otherwise, so an
    /// oscillator driven through <see cref="IModestOscillator" /> alone behaves as if the key was
    /// struck as hard as it can be.
    /// </summary>
    protected int NoteVelocity { get; private set; } = DefaultNoteVelocity;

    /// <summary>
    /// The current phase in cycles, always in [0, 1).
    /// </summary>
    protected double Phase { get; set; }

    /// <summary>
    /// How far one sample advances the phase, in cycles - <see cref="Frequency" /> divided by
    /// <see cref="SampleRate" />. Band-limiting corrections are all expressed in terms of it.
    /// </summary>
    protected double PhaseIncrement => phaseIncrement;

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate" /> is not positive.</exception>
    public virtual void SetSampleRate(int sampleRate)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(sampleRate), sampleRate, "Sample rate must be positive.");
        }

        this.sampleRate = sampleRate;
        phaseIncrement = frequency / sampleRate;
    }

    /// <inheritdoc />
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="frequencyHz" /> is negative or not finite.</exception>
    public virtual void SetFrequency(double frequencyHz)
    {
        if (double.IsNaN(frequencyHz) || double.IsInfinity(frequencyHz) || frequencyHz < 0.0)
        {
            throw new ArgumentOutOfRangeException(nameof(frequencyHz), frequencyHz,
                "Frequency must be a finite, non-negative number of Hz.");
        }

        frequency = frequencyHz;
        phaseIncrement = frequencyHz / sampleRate;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The base implementation records the velocity and marks the key down. It does NOT touch the
    /// phase, so call <see cref="Reset" /> first when the start phase matters.
    /// </remarks>
    public virtual void NoteOn(int velocity)
    {
        NoteVelocity = velocity < 0 ? 0 : velocity > DefaultNoteVelocity ? DefaultNoteVelocity : velocity;
        IsKeyDown = true;
    }

    /// <inheritdoc />
    /// <remarks>
    /// The base implementation marks the key up. A waveform that simply runs keeps rendering; what
    /// stops it is the envelope of whatever is playing the note.
    /// </remarks>
    public virtual void NoteOff() => IsKeyDown = false;

    /// <inheritdoc />
    public virtual void Reset(double phase) => Phase = WrapPhase(phase);

    /// <inheritdoc />
    public abstract void Render(Span<float> buffer);

    /// <summary>
    /// Advances <see cref="Phase" /> by one sample, wrapping it back into [0, 1).
    /// </summary>
    protected void AdvancePhase()
    {
        double next = Phase + phaseIncrement;
        if (next >= 1.0) { next -= Math.Floor(next); }
        Phase = next;
    }

    /// <summary>
    /// Reduces a phase in cycles to its fractional part in [0, 1), including for negative input.
    /// </summary>
    /// <param name="phase">The phase in cycles.</param>
    /// <returns>The equivalent phase in [0, 1); zero when the input is not a finite number.</returns>
    protected static double WrapPhase(double phase)
    {
        if (double.IsNaN(phase) || double.IsInfinity(phase)) { return 0.0; }

        double wrapped = phase - Math.Floor(phase);
        if (wrapped < 0.0 || wrapped >= 1.0) { wrapped = 0.0; }
        return wrapped;
    }
}
