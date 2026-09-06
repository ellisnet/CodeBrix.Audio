using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// One <c>&lt;binding&gt;</c>: the rule that takes a value from a control, a MIDI handler or a
/// modulator and applies it somewhere in the engine.
/// </summary>
/// <remarks>
/// <para>
/// This is the parsed form only. Resolving a binding to the thing it addresses, and applying its
/// translation, belongs to the parameter and binding engine; everything the developer guide's
/// Appendix B lists is carried here so that engine has it all.
/// </para>
/// <para>
/// A binding names its target in one of three ways: an index (<see cref="Position"/> or one of the
/// typed index attributes), a set of tags, or an <see cref="Identifier"/>. The typed index attributes
/// and <see cref="Position"/> mean the same thing; <see cref="ResolvedIndex"/> gives whichever was
/// written.
/// </para>
/// </remarks>
public sealed class DecentSamplerBinding : DecentSamplerElement
{
    /// <summary>The recognised binding type, or <see cref="DecentSamplerBindingType.Unknown"/>.</summary>
    public DecentSamplerBindingType BindingType { get; internal set; } = DecentSamplerBindingType.Unknown;

    /// <summary>The <c>type</c> attribute exactly as written.</summary>
    public string TypeName { get; internal set; }

    /// <summary>The recognised binding level, or <see cref="DecentSamplerBindingLevel.Unknown"/>.</summary>
    public DecentSamplerBindingLevel Level { get; internal set; } = DecentSamplerBindingLevel.Unknown;

    /// <summary>The <c>level</c> attribute exactly as written.</summary>
    public string LevelName { get; internal set; }

    /// <summary>The 0-based index of the target (<c>position</c>).</summary>
    public int? Position { get; internal set; }

    /// <summary>The 0-based index of a target user-interface control (<c>controlIndex</c>).</summary>
    public int? ControlIndex { get; internal set; }

    /// <summary>The 0-based index of a target group (<c>groupIndex</c>).</summary>
    public int? GroupIndex { get; internal set; }

    /// <summary>The 0-based index of a target effect within its chain (<c>effectIndex</c>).</summary>
    public int? EffectIndex { get; internal set; }

    /// <summary>The 0-based index of a target modulator (<c>modulatorIndex</c>).</summary>
    public int? ModulatorIndex { get; internal set; }

    /// <summary>The 0-based index of a target bus (<c>busIndex</c>).</summary>
    public int? BusIndex { get; internal set; }

    /// <summary>The 0-based index of a target button or control state (<c>stateIndex</c>).</summary>
    public int? StateIndex { get; internal set; }

    /// <summary>The 0-based index of a target binding within its owner (<c>bindingIndex</c>).</summary>
    public int? BindingIndex { get; internal set; }

    /// <summary>
    /// The 0-based index of a target handler within the <c>&lt;midi&gt;</c> element
    /// (<c>midiElementIndex</c>, formerly <c>noteIndex</c>).
    /// </summary>
    public int? MidiElementIndex { get; internal set; }

    /// <summary>The 0-based index of a target on-screen keyboard colour range (<c>colorIndex</c>).</summary>
    public int? ColorIndex { get; internal set; }

    /// <summary>The 0-based index of a target sequence under <c>&lt;noteSequences&gt;</c> (<c>seqIndex</c>).</summary>
    public int? SeqIndex { get; internal set; }

    /// <summary>
    /// The generic <c>tags</c> list. The guide promotes it into whichever typed tag list suits the
    /// binding when that list is not written explicitly; this property keeps the value as written.
    /// </summary>
    public IReadOnlyList<string> Tags { get; internal set; } = [];

    /// <summary>Group tags the binding targets (<c>groupTags</c>).</summary>
    public IReadOnlyList<string> GroupTags { get; internal set; } = [];

    /// <summary>Sample tags the binding targets (<c>sampleTags</c>).</summary>
    public IReadOnlyList<string> SampleTags { get; internal set; } = [];

    /// <summary>Oscillator tags the binding targets (<c>oscillatorTags</c>).</summary>
    public IReadOnlyList<string> OscillatorTags { get; internal set; } = [];

    /// <summary>Effect tags the binding targets (<c>effectTags</c>).</summary>
    public IReadOnlyList<string> EffectTags { get; internal set; } = [];

    /// <summary>Modulator tags the binding targets (<c>modulatorTags</c>).</summary>
    public IReadOnlyList<string> ModulatorTags { get; internal set; } = [];

    /// <summary>User-interface control tags the binding targets (<c>controlTags</c>).</summary>
    public IReadOnlyList<string> ControlTags { get; internal set; } = [];

    /// <summary>Whether the binding fires at all (<c>enabled</c>). Default true.</summary>
    public bool? Enabled { get; internal set; }

    /// <summary>
    /// The name of the thing being changed (<c>identifier</c>), used when the target is addressed by
    /// name rather than index - a tag, most often.
    /// </summary>
    public string Identifier { get; internal set; }

    /// <summary>The parameter token, such as <c>AMP_VOLUME</c> (<c>parameter</c>).</summary>
    public string Parameter { get; internal set; }

    /// <summary>How the source value is mapped onto the target (<c>translation</c>). Default linear.</summary>
    public DecentSamplerTranslation Translation { get; internal set; } = DecentSamplerTranslation.Linear;

    /// <summary>The <c>translation</c> attribute exactly as written.</summary>
    public string TranslationName { get; internal set; }

    /// <summary>The lowest value a linear translation sends (<c>translationOutputMin</c>).</summary>
    public double? TranslationOutputMin { get; internal set; }

    /// <summary>The highest value a linear translation sends (<c>translationOutputMax</c>).</summary>
    public double? TranslationOutputMax { get; internal set; }

    /// <summary>Whether a linear translation runs backwards (<c>translationReversed</c>). Default false.</summary>
    public bool? TranslationReversed { get; internal set; }

    /// <summary>
    /// The points of a table translation (<c>translationTable</c>), in the order written. Never null;
    /// empty when none were written. The documented default table is <c>0,0;1,1</c>.
    /// </summary>
    public IReadOnlyList<DecentSamplerTranslationPoint> TranslationTable { get; internal set; } = [];

    /// <summary>The <c>translationTable</c> attribute exactly as written.</summary>
    public string TranslationTableText { get; internal set; }

    /// <summary>
    /// The value a fixed translation always sends (<c>translationValue</c>), kept as text because it
    /// may be a number, a boolean, an enumeration name, a colour or free text depending on the target.
    /// </summary>
    public string TranslationValue { get; internal set; }

    /// <summary>Whether the binding fires when the preset loads (<c>triggerOnLoad</c>). Default true.</summary>
    public bool? TriggerOnLoad { get; internal set; }

    /// <summary>How a modulator's value combines with the target (<c>modBehavior</c>).</summary>
    public DecentSamplerModBehavior? ModBehavior { get; internal set; }

    /// <summary>The binding's own modulation depth (<c>modAmount</c>).</summary>
    public double? ModAmount { get; internal set; }

    /// <summary>Whether a triggered sequence follows the tempo (<c>seqFollowGlobalTempo</c>). Default true.</summary>
    public bool? SeqFollowGlobalTempo { get; internal set; }

    /// <summary>What the binding does to its sequence (<c>seqTriggerBehavior</c>). Default midi_key.</summary>
    public DecentSamplerSeqTriggerBehavior? SeqTriggerBehavior { get; internal set; }

    /// <summary>The name that tracks a sequence player's state (<c>seqPlayerIdentifier</c>).</summary>
    public string SeqPlayerIdentifier { get; internal set; }

    /// <summary>How much the incoming note's velocity scales the sequence (<c>seqTrackMidiInputVelocity</c>).</summary>
    public double? SeqTrackMidiInputVelocity { get; internal set; }

    /// <summary>Transposition of the sequence in semitones (<c>seqTranspose</c>), -36 to 36.</summary>
    public double? SeqTranspose { get; internal set; }

    /// <summary>
    /// The note the sequence is transposed relative to when triggered by a key
    /// (<c>seqTransposeWithRootNote</c>).
    /// </summary>
    public double? SeqTransposeWithRootNote { get; internal set; }

    /// <summary>Sequence playback speed (<c>seqPlaybackRate</c>), 0.001 to 10000. Default 1.</summary>
    public double? SeqPlaybackRate { get; internal set; }

    /// <summary>How the sequence repeats (<c>seqLoopMode</c>). Default forward.</summary>
    public DecentSamplerSeqLoopMode? SeqLoopMode { get; internal set; }

    /// <summary>
    /// The index the binding addresses its target by: the typed index attribute if one was written,
    /// otherwise <see cref="Position"/>.
    /// </summary>
    public int? ResolvedIndex =>
        ControlIndex ?? GroupIndex ?? EffectIndex ?? ModulatorIndex ?? BusIndex ?? ColorIndex ??
        MidiElementIndex ?? SeqIndex ?? Position;

    /// <inheritdoc/>
    public override string ToString() =>
        $"<binding type=\"{TypeName}\" level=\"{LevelName}\" parameter=\"{Parameter}\"> (line {LineNumber})";
}
