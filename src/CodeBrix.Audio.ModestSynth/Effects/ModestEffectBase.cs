using System;
using System.Collections.Generic;
using CodeBrix.Audio.ModestSynth.Effects.Internal;
using CodeBrix.Audio.Synth.DecentSampler.Engine;

namespace CodeBrix.Audio.ModestSynth.Effects;

/// <summary>
/// What every creative effect in this package has in common: the sample rate, the enabled flag, the
/// tags, the parameter-name plumbing, and the argument checking around a block.
/// </summary>
/// <remarks>
/// <para>
/// Derived effects implement <see cref="OnPrepare" />, <see cref="OnReset" /> and
/// <see cref="OnProcess" />, and add their own parameters by overriding
/// <see cref="OnTrySetParameter(string, double)" /> and <see cref="OnTryGetParameter" />. The names
/// those overrides see are already folded - lower case, letters and digits only, and without the
/// <c>FX_</c> prefix - so <c>FX_MOD_RATE</c>, <c>modRate</c> and <c>fx mod rate</c> all arrive as
/// <c>modrate</c>.
/// </para>
/// <para>
/// LIFECYCLE. <see cref="Prepare" /> allocates and is the only member that does; call it once, off
/// the audio thread, before the first block. An effect that is processed without ever having been
/// prepared prepares itself at <see cref="DefaultSampleRate" /> on its first block rather than
/// throwing, so a standalone caller who forgets still gets sound - but it is the wrong rate unless
/// 44,100 Hz is genuinely what you are rendering at.
/// </para>
/// <para>
/// THREADING. An effect instance belongs to the thread rendering it. Nothing here is thread-safe
/// and nothing needs to be: a Decent Sampler group chain is instantiated per voice, and an
/// instrument or bus chain is owned by whatever renders it.
/// </para>
/// </remarks>
public abstract class ModestEffectBase : IInstrumentEffect
{
    /// <summary>The rate an effect prepares itself at when nothing told it otherwise: 44,100 Hz.</summary>
    public const int DefaultSampleRate = 44100;

    /// <summary>The lowest sample rate <see cref="Prepare" /> accepts.</summary>
    public const int MinimumSampleRate = 4000;

    /// <summary>The highest sample rate <see cref="Prepare" /> accepts.</summary>
    public const int MaximumSampleRate = 768000;

    private static readonly string[] NoTags = new string[0];

    private IReadOnlyList<string> tags = NoTags;

    private bool prepared;

    /// <summary>Creates an effect at <see cref="DefaultSampleRate" />, not yet prepared.</summary>
    protected ModestEffectBase()
    {
    }

    /// <summary>
    /// Whether the effect processes. A disabled effect is bypassed - the block passes through
    /// untouched - not removed, and its internal state is left exactly as it was.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// The effect's tags, as written on the <c>&lt;effect&gt;</c> element. Never null; setting null
    /// clears the list.
    /// </summary>
    public IReadOnlyList<string> Tags
    {
        get { return tags; }
        set { tags = value ?? NoTags; }
    }

    /// <summary>The sample rate the effect is configured for, in Hz.</summary>
    public int SampleRate { get; private set; } = DefaultSampleRate;

    /// <summary>Whether <see cref="Prepare" /> has run, so that <see cref="Process" /> allocates nothing.</summary>
    public bool IsPrepared
    {
        get { return prepared; }
    }

    /// <summary>
    /// Prepares the effect for a sample rate, allocating whatever it needs and clearing its state.
    /// </summary>
    /// <param name="sampleRate">The sample rate in Hz.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="sampleRate" /> is outside <see cref="MinimumSampleRate" /> to
    /// <see cref="MaximumSampleRate" />.
    /// </exception>
    public void Prepare(int sampleRate)
    {
        if (sampleRate < MinimumSampleRate || sampleRate > MaximumSampleRate)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate),
                sampleRate,
                "The sample rate must be between " + MinimumSampleRate + " and " + MaximumSampleRate + " Hz.");
        }

        SampleRate = sampleRate;
        OnPrepare(sampleRate);
        prepared = true;
        OnReset();
    }

    /// <summary>
    /// Clears every internal buffer without changing any parameter. An unprepared effect prepares
    /// itself at its current sample rate instead.
    /// </summary>
    public void Reset()
    {
        if (!prepared)
        {
            Prepare(SampleRate);
            return;
        }

        OnReset();
    }

    /// <summary>
    /// Processes one block in place.
    /// </summary>
    /// <param name="left">The left channel. Must not be the same array as <paramref name="right" />.</param>
    /// <param name="right">The right channel.</param>
    /// <param name="frames">How many frames of each buffer to process.</param>
    /// <exception cref="ArgumentNullException"><paramref name="left" /> or <paramref name="right" /> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="left" /> and <paramref name="right" /> are the same array.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="frames" /> is negative or longer than either buffer.
    /// </exception>
    public void Process(float[] left, float[] right, int frames)
    {
        if (left == null) { throw new ArgumentNullException(nameof(left)); }
        if (right == null) { throw new ArgumentNullException(nameof(right)); }

        if (ReferenceEquals(left, right))
        {
            throw new ArgumentException(
                "The left and right buffers must be different arrays; an effect writes both.", nameof(right));
        }

        if (frames < 0 || frames > left.Length || frames > right.Length)
        {
            throw new ArgumentOutOfRangeException(
                nameof(frames), frames, "The frame count must fit inside both buffers.");
        }

        if (!prepared) { Prepare(SampleRate); }

        if (!Enabled || frames == 0) { return; }

        OnProcess(left, right, frames);
    }

    /// <summary>
    /// Sets one of the effect's numeric parameters, named as the developer guide's attributes or its
    /// <c>FX_*</c> binding parameters are. Matching ignores case, punctuation and the <c>FX_</c>
    /// prefix.
    /// </summary>
    /// <param name="name">The parameter name, for example <c>FX_MOD_RATE</c> or <c>modRate</c>.</param>
    /// <param name="value">The new value. Out-of-range values are clamped; NaN and infinity are ignored.</param>
    /// <returns><see langword="true" /> when the effect knows the parameter.</returns>
    public bool TrySetParameter(string name, double value)
    {
        string key = ModestEffectParameters.Fold(name);
        if (key == null) { return false; }

        if (key == "enabled")
        {
            Enabled = value >= 0.5;
            return true;
        }

        if (double.IsNaN(value) || double.IsInfinity(value))
        {
            // A value that is not a number leaves the parameter alone, but the NAME was still one
            // this effect knows, and the caller is entitled to know that.
            double ignored;
            return OnTryGetParameter(key, out ignored);
        }

        return OnTrySetParameter(key, value);
    }

    /// <summary>
    /// Reads one of the effect's numeric parameters.
    /// </summary>
    /// <param name="name">The parameter name, matched the same way <see cref="TrySetParameter(string, double)" /> matches.</param>
    /// <param name="value">The current value, or zero when the parameter is unknown.</param>
    /// <returns><see langword="true" /> when the effect knows the parameter.</returns>
    public bool TryGetParameter(string name, out double value)
    {
        value = 0.0;

        string key = ModestEffectParameters.Fold(name);
        if (key == null) { return false; }

        if (key == "enabled")
        {
            value = Enabled ? 1.0 : 0.0;
            return true;
        }

        return OnTryGetParameter(key, out value);
    }

    /// <summary>
    /// Sets one of the effect's text parameters. None of the creative effects has one of the
    /// format's own text parameters; <c>ENABLED</c> and the stereo simulator's algorithm name are
    /// accepted here because a host has nowhere else to put them.
    /// </summary>
    /// <param name="name">The parameter name, matched the same way the numeric setter matches.</param>
    /// <param name="value">The new value.</param>
    /// <returns><see langword="true" /> when the effect knows the parameter.</returns>
    public bool TrySetParameter(string name, string value)
    {
        string key = ModestEffectParameters.Fold(name);
        if (key == null) { return false; }

        if (key == "enabled")
        {
            string folded = ModestEffectParameters.Fold(value);
            if (folded == "true" || folded == "1" || folded == "yes" || folded == "on")
            {
                Enabled = true;
                return true;
            }

            if (folded == "false" || folded == "0" || folded == "no" || folded == "off")
            {
                Enabled = false;
                return true;
            }

            return true;
        }

        return OnTrySetParameter(key, value);
    }

    /// <summary>
    /// Allocates whatever the effect needs at a sample rate. The only member that may allocate.
    /// </summary>
    /// <param name="sampleRate">The sample rate in Hz.</param>
    protected abstract void OnPrepare(int sampleRate);

    /// <summary>Clears the effect's internal state, leaving every parameter alone.</summary>
    protected abstract void OnReset();

    /// <summary>
    /// Processes a block. Called only when the effect is enabled, prepared and given a positive
    /// frame count that fits both buffers.
    /// </summary>
    /// <param name="left">The left channel.</param>
    /// <param name="right">The right channel.</param>
    /// <param name="frames">How many frames to process.</param>
    protected abstract void OnProcess(float[] left, float[] right, int frames);

    /// <summary>
    /// Sets one of the derived effect's numeric parameters.
    /// </summary>
    /// <param name="foldedName">The parameter name, already folded.</param>
    /// <param name="value">The new value, guaranteed finite.</param>
    /// <returns><see langword="true" /> when the effect knows the parameter.</returns>
    protected virtual bool OnTrySetParameter(string foldedName, double value) => false;

    /// <summary>
    /// Sets one of the derived effect's text parameters.
    /// </summary>
    /// <param name="foldedName">The parameter name, already folded.</param>
    /// <param name="value">The new value.</param>
    /// <returns><see langword="true" /> when the effect knows the parameter.</returns>
    protected virtual bool OnTrySetParameter(string foldedName, string value) => false;

    /// <summary>
    /// Reads one of the derived effect's numeric parameters.
    /// </summary>
    /// <param name="foldedName">The parameter name, already folded.</param>
    /// <param name="value">The current value, or zero when the parameter is unknown.</param>
    /// <returns><see langword="true" /> when the effect knows the parameter.</returns>
    protected virtual bool OnTryGetParameter(string foldedName, out double value)
    {
        value = 0.0;
        return false;
    }

    /// <summary>
    /// Clamps a value into a range, ignoring anything that is not a number.
    /// </summary>
    /// <param name="value">The value to clamp.</param>
    /// <param name="minimum">The lowest allowed value.</param>
    /// <param name="maximum">The highest allowed value.</param>
    /// <param name="current">What to return when <paramref name="value" /> is NaN or infinite.</param>
    /// <returns>The clamped value.</returns>
    protected static double Clamp(double value, double minimum, double maximum, double current)
    {
        if (double.IsNaN(value) || double.IsInfinity(value)) { return current; }
        if (value < minimum) { return minimum; }
        if (value > maximum) { return maximum; }
        return value;
    }
}
