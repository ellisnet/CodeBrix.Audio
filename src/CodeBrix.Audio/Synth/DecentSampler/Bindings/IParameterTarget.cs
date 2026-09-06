namespace CodeBrix.Audio.Synth.DecentSampler.Bindings;

/// <summary>
/// Something a <c>&lt;binding&gt;</c> can change: one parameter of one group, zone, effect, tag, bus,
/// modulator, sequence, MIDI handler or user-interface control.
/// </summary>
/// <remarks>
/// <para>
/// A target keeps a BASE value, which permanent bindings (controls, MIDI CC, MIDI note and velocity
/// handlers) write, and a set of temporary CONTRIBUTIONS, which modulators add and remove. The
/// EFFECTIVE value is the base value with every contribution folded in, in the order they were added.
/// </para>
/// <para>
/// Contributions are keyed by a source identity and a voice: a global modulator uses
/// <see cref="GlobalVoice"/>, and a voice-scope modulator uses the voice's own identifier so that each
/// sounding note carries its own contribution. The effective value a group or zone property exposes is
/// the GLOBAL one; the voice runtime asks for a voice's own value when it needs it.
/// </para>
/// </remarks>
internal interface IParameterTarget
{
    /// <summary>The voice identifier a global-scope contribution uses.</summary>
    const int GlobalVoice = -1;

    /// <summary>
    /// A name for the target, such as <c>group[0].AMP_VOLUME</c>. Used in problem messages and in the
    /// parameter-changed notification.
    /// </summary>
    string Name { get; }

    /// <summary>The parameter token this target answers to, such as <c>AMP_VOLUME</c>.</summary>
    string Parameter { get; }

    /// <summary>What the target holds.</summary>
    DecentSamplerParameterValueKind Kind { get; }

    /// <summary>The base value, before any modulation.</summary>
    DecentSamplerParameterValue BaseValue { get; }

    /// <summary>Writes the base value and republishes the effective value.</summary>
    /// <param name="value">The new base value.</param>
    void SetBaseValue(DecentSamplerParameterValue value);

    /// <summary>The base value with every global contribution folded in.</summary>
    /// <returns>The effective value.</returns>
    DecentSamplerParameterValue GetEffectiveValue();

    /// <summary>The base value with the global contributions and one voice's own folded in.</summary>
    /// <param name="voiceId">The voice, or <see cref="GlobalVoice"/> for the global value.</param>
    /// <returns>The effective value.</returns>
    DecentSamplerParameterValue GetEffectiveValue(int voiceId);

    /// <summary>Adds or replaces one source's contribution.</summary>
    /// <param name="sourceId">Which modulator the contribution comes from.</param>
    /// <param name="voiceId">The voice it belongs to, or <see cref="GlobalVoice"/>.</param>
    /// <param name="contribution">The contribution.</param>
    void SetModulation(int sourceId, int voiceId, DecentSamplerModulationContribution contribution);

    /// <summary>Removes one source's contribution.</summary>
    /// <param name="sourceId">Which modulator to remove.</param>
    /// <param name="voiceId">The voice it belonged to, or <see cref="GlobalVoice"/>.</param>
    void ClearModulation(int sourceId, int voiceId);

    /// <summary>Removes every contribution belonging to one voice.</summary>
    /// <param name="voiceId">The voice that has finished.</param>
    void ClearVoice(int voiceId);
}
