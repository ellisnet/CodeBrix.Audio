using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Bindings;

/// <summary>
/// The one implementation of <see cref="IParameterTarget"/>: a base value, a list of temporary
/// contributions, and a delegate that writes the effective value onto whatever runtime object owns it.
/// </summary>
/// <remarks>
/// <para>
/// Targets are created on demand, when a binding resolves to one or when the engine needs one for the
/// initial state, so a preset with twenty bindings holds twenty targets rather than one per parameter
/// of every group.
/// </para>
/// <para>
/// The effective value is PUSHED, not pulled: whenever the base value or a contribution changes, the
/// publish delegate writes the result onto the group, zone, effect or control that owns it. That keeps
/// the runtime objects' own properties plain fields, so the voice runtime reads them with no lookup and
/// no allocation.
/// </para>
/// </remarks>
internal sealed class DecentSamplerParameter : IParameterTarget
{
    private readonly Action<DecentSamplerParameterValue> _publish;
    private readonly List<Slot> _slots = [];
    private DecentSamplerParameterValue _base;

    /// <summary>Builds a target.</summary>
    /// <param name="name">A name for problem messages, such as <c>group[0].AMP_VOLUME</c>.</param>
    /// <param name="parameter">The parameter token, such as <c>AMP_VOLUME</c>.</param>
    /// <param name="kind">What the target holds.</param>
    /// <param name="initialValue">The value the preset was written with.</param>
    /// <param name="publish">Writes an effective value onto the runtime object. Never null.</param>
    internal DecentSamplerParameter(
        string name,
        string parameter,
        DecentSamplerParameterValueKind kind,
        DecentSamplerParameterValue initialValue,
        Action<DecentSamplerParameterValue> publish)
    {
        Name = name;
        Parameter = parameter;
        Kind = kind;
        _base = initialValue;
        _publish = publish;
    }

    /// <summary>Raised after the effective value has been written to its owner.</summary>
    internal event Action<DecentSamplerParameter, DecentSamplerParameterValue> Changed;

    /// <inheritdoc/>
    public string Name { get; }

    /// <inheritdoc/>
    public string Parameter { get; }

    /// <inheritdoc/>
    public DecentSamplerParameterValueKind Kind { get; }

    /// <inheritdoc/>
    public DecentSamplerParameterValue BaseValue => _base;

    /// <summary>How many temporary contributions the target currently carries.</summary>
    internal int ModulationCount => _slots.Count;

    /// <inheritdoc/>
    public void SetBaseValue(DecentSamplerParameterValue value)
    {
        _base = value;
        Publish();
    }

    /// <inheritdoc/>
    public DecentSamplerParameterValue GetEffectiveValue() => GetEffectiveValue(IParameterTarget.GlobalVoice);

    /// <inheritdoc/>
    public DecentSamplerParameterValue GetEffectiveValue(int voiceId)
    {
        if (_slots.Count == 0 || Kind == DecentSamplerParameterValueKind.Text ||
            Kind == DecentSamplerParameterValueKind.Action)
        {
            return _base;
        }

        var value = _base.AsNumber;
        var modulated = false;

        foreach (var slot in _slots)
        {
            if (slot.VoiceId != IParameterTarget.GlobalVoice && slot.VoiceId != voiceId)
            {
                continue;
            }

            value = slot.Contribution.Apply(value);
            modulated = true;
        }

        if (!modulated)
        {
            return _base;
        }

        return Kind == DecentSamplerParameterValueKind.Boolean
            ? DecentSamplerParameterValue.FromBoolean(value != 0.0)
            : DecentSamplerParameterValue.FromNumber(value);
    }

    /// <inheritdoc/>
    public void SetModulation(int sourceId, int voiceId, DecentSamplerModulationContribution contribution)
    {
        for (var index = 0; index < _slots.Count; index++)
        {
            if (_slots[index].SourceId != sourceId || _slots[index].VoiceId != voiceId)
            {
                continue;
            }

            _slots[index] = new Slot(sourceId, voiceId, contribution);
            Publish();
            return;
        }

        _slots.Add(new Slot(sourceId, voiceId, contribution));
        Publish();
    }

    /// <inheritdoc/>
    public void ClearModulation(int sourceId, int voiceId)
    {
        for (var index = 0; index < _slots.Count; index++)
        {
            if (_slots[index].SourceId != sourceId || _slots[index].VoiceId != voiceId)
            {
                continue;
            }

            _slots.RemoveAt(index);
            Publish();
            return;
        }
    }

    /// <inheritdoc/>
    public void ClearVoice(int voiceId)
    {
        // A manual sweep rather than List.RemoveAll, whose predicate closure would allocate on the
        // audio thread every time a voice ends.
        var removed = false;

        for (var index = _slots.Count - 1; index >= 0; index--)
        {
            if (_slots[index].VoiceId != voiceId)
            {
                continue;
            }

            _slots.RemoveAt(index);
            removed = true;
        }

        if (removed)
        {
            Publish();
        }
    }

    /// <summary>Writes the current effective value to its owner, without changing anything.</summary>
    internal void Republish() => Publish();

    /// <summary>
    /// Writes ONE VOICE's effective value to the owner, so that voice renders with its own
    /// modulation. Always followed by <see cref="PublishGlobally"/>; raises no change notification,
    /// because the swap happens many times per block and is not a parameter move a host should see.
    /// </summary>
    /// <param name="voiceId">The voice whose value to publish.</param>
    internal void PublishForVoice(int voiceId) => _publish(GetEffectiveValue(voiceId));

    /// <summary>Puts the global effective value back after <see cref="PublishForVoice"/>.</summary>
    internal void PublishGlobally() => _publish(GetEffectiveValue(IParameterTarget.GlobalVoice));

    /// <inheritdoc/>
    public override string ToString() => $"{Name} = {GetEffectiveValue()}";

    private void Publish()
    {
        var effective = GetEffectiveValue();
        _publish(effective);
        Changed?.Invoke(this, effective);
    }

    private readonly struct Slot(int sourceId, int voiceId, DecentSamplerModulationContribution contribution)
    {
        public int SourceId { get; } = sourceId;

        public int VoiceId { get; } = voiceId;

        public DecentSamplerModulationContribution Contribution { get; } = contribution;
    }
}
