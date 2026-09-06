using System;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

/// <summary>
/// Everything an effect factory is told when the engine builds one instance of an effect.
/// </summary>
/// <remarks>
/// A group chain is a template instantiated once per voice, so a factory for a group-level effect is
/// called once for every voice that group starts. Keep construction cheap and read the parsed
/// attributes off <see cref="Effect"/>.
/// </remarks>
public sealed class EffectContext
{
    /// <summary>
    /// Creates a context.
    /// </summary>
    /// <param name="instrument">The instrument being played.</param>
    /// <param name="effect">The parsed <c>&lt;effect&gt;</c> element.</param>
    /// <param name="sampleRate">The synthesis sample rate in Hz.</param>
    /// <param name="placement">Where in the signal path the chain sits.</param>
    /// <param name="chainIndex">
    /// The 0-based index of the bus or group the chain belongs to, or -1 for the instrument chain.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="effect"/> is null.</exception>
    public EffectContext(
        DecentSamplerInstrument instrument,
        DecentSamplerEffect effect,
        int sampleRate,
        DecentSamplerEffectPlacement placement,
        int chainIndex)
        : this(instrument, effect, sampleRate, placement, chainIndex, 0, null)
    {
    }

    /// <summary>
    /// Creates a context, including the block size the engine renders in and the musical clock a
    /// tempo-synced effect follows.
    /// </summary>
    /// <param name="instrument">The instrument being played.</param>
    /// <param name="effect">The parsed <c>&lt;effect&gt;</c> element.</param>
    /// <param name="sampleRate">The synthesis sample rate in Hz.</param>
    /// <param name="placement">Where in the signal path the chain sits.</param>
    /// <param name="chainIndex">
    /// The 0-based index of the bus or group the chain belongs to, or -1 for the instrument chain.
    /// </param>
    /// <param name="blockSize">
    /// How many frames the engine renders at a time, or 0 for the engine's own default. An effect that
    /// needs a fixed transform size, such as the partitioned convolution, uses it.
    /// </param>
    /// <param name="tempoSource">
    /// The musical clock behind <c>delayTimeFormat="musical_time"</c>. Null gives a fresh 120 BPM
    /// source, which is what the reference standalone runs at with no host.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="effect"/> is null.</exception>
    public EffectContext(
        DecentSamplerInstrument instrument,
        DecentSamplerEffect effect,
        int sampleRate,
        DecentSamplerEffectPlacement placement,
        int chainIndex,
        int blockSize,
        TempoSource tempoSource)
    {
        Instrument = instrument;
        Effect = effect ?? throw new ArgumentNullException(nameof(effect));
        SampleRate = sampleRate;
        Placement = placement;
        ChainIndex = chainIndex;
        BlockSize = blockSize;
        TempoSource = tempoSource ?? new TempoSource { BeatsPerMinute = DecentSamplerDefaults.BeatsPerMinute };
    }

    /// <summary>The instrument being played. Null when an effect is built outside an instrument.</summary>
    public DecentSamplerInstrument Instrument { get; }

    /// <summary>The parsed effect element, with every documented attribute already typed.</summary>
    public DecentSamplerEffect Effect { get; }

    /// <summary>The synthesis sample rate in Hz.</summary>
    public int SampleRate { get; }

    /// <summary>Where in the signal path the chain sits.</summary>
    public DecentSamplerEffectPlacement Placement { get; }

    /// <summary>
    /// The 0-based index of the bus or group the chain belongs to, or -1 for the instrument chain.
    /// </summary>
    public int ChainIndex { get; }

    /// <summary>
    /// How many frames the engine renders at a time, or 0 when the builder did not say. An effect that
    /// needs a fixed transform size treats 0 as "use your own default".
    /// </summary>
    public int BlockSize { get; }

    /// <summary>
    /// The musical clock behind <c>delayTimeFormat="musical_time"</c>. Never null: a context built
    /// without one gets a fresh 120 BPM source.
    /// </summary>
    public TempoSource TempoSource { get; }

    /// <summary>The effect type name as the preset spelled it, which is what the factory was keyed by.</summary>
    public string TypeName => Effect.TypeName;
}
