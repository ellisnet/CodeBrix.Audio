using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

/// <summary>
/// One effect in a chain: an in-place stereo processor with named parameters and tags.
/// </summary>
/// <remarks>
/// <para>
/// The Decent Sampler format places effect chains at three levels - the instrument, a bus, and a group
/// (where the chain is a template instantiated per voice). All three drive this same contract. The
/// effect DSP and the chain plumbing arrive with the effects phase; this interface exists now so that
/// an add-on package can be written against it, and so the extension registry has something to hand
/// back.
/// </para>
/// <para>
/// <see cref="Prepare"/> is called before the first block and again whenever the sample rate changes;
/// it is the only place an implementation may allocate. <see cref="Process"/> runs on the audio thread
/// and must not allocate, lock or touch a file.
/// </para>
/// </remarks>
public interface IInstrumentEffect
{
    /// <summary>Whether the effect processes. A disabled effect is bypassed, not removed.</summary>
    bool Enabled { get; set; }

    /// <summary>The effect's tags, as written on the <c>&lt;effect&gt;</c> element. Never null.</summary>
    IReadOnlyList<string> Tags { get; }

    /// <summary>
    /// Prepares the effect for a sample rate. Called before the first block and on any rate change.
    /// </summary>
    /// <param name="sampleRate">The sample rate in Hz.</param>
    void Prepare(int sampleRate);

    /// <summary>Clears every internal buffer without changing any parameter.</summary>
    void Reset();

    /// <summary>
    /// Processes one block in place.
    /// </summary>
    /// <param name="left">The left channel.</param>
    /// <param name="right">The right channel.</param>
    /// <param name="frames">How many frames of each buffer to process.</param>
    void Process(float[] left, float[] right, int frames);

    /// <summary>
    /// Sets one of the effect's numeric parameters, named as the developer guide's <c>FX_*</c> binding
    /// parameters are, matched case-insensitively.
    /// </summary>
    /// <param name="name">The parameter name, for example <c>FX_FILTER_FREQUENCY</c>.</param>
    /// <param name="value">The new value.</param>
    /// <returns><see langword="true"/> when the effect knows the parameter.</returns>
    bool TrySetParameter(string name, double value);

    /// <summary>
    /// Reads one of the effect's numeric parameters.
    /// </summary>
    /// <param name="name">The parameter name, matched case-insensitively.</param>
    /// <param name="value">The current value, or zero when the parameter is unknown.</param>
    /// <returns><see langword="true"/> when the effect knows the parameter.</returns>
    bool TryGetParameter(string name, out double value);

    /// <summary>
    /// Sets one of the effect's text parameters - <c>FX_IR_FILE</c> is the only documented one.
    /// </summary>
    /// <param name="name">The parameter name, matched case-insensitively.</param>
    /// <param name="value">The new value.</param>
    /// <returns><see langword="true"/> when the effect knows the parameter.</returns>
    bool TrySetParameter(string name, string value);
}
