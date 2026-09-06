using System;
using System.Collections.Generic;
using CodeBrix.Audio.ModestSynth.Fm.Internal;
using CodeBrix.Audio.ModestSynth.Oscillators;
using CodeBrix.Audio.ModestSynth.Patch;

namespace CodeBrix.Audio.ModestSynth.Fm;

/// <summary>
/// The <c>fm6op</c> waveform: six sine operators wired together by one of the 32 classic
/// six-operator algorithms, each with its own frequency, level, velocity response and envelope.
/// </summary>
/// <remarks>
/// <para>
/// Every operator is a sine. What makes the sound is where each one's output goes: a CARRIER's
/// output is heard, a MODULATOR's output is added to another operator's PHASE, which bends that
/// operator's sine into something with harmonics. <see cref="Algorithm" /> chooses the wiring, and
/// its 32 values are the published charts of the six-operator hardware the format names, so a
/// patch bank's algorithm number can be used as it stands.
/// </para>
/// <para>
/// This is a VOICE, not just a waveform: it implements <see cref="IModestVoiceOscillator" />
/// because six envelopes and a velocity response need a note-on and a note-off to mean anything.
/// The order is <c>SetSampleRate</c>, <c>SetFrequency</c>, <c>Reset(startPhase)</c>,
/// <c>NoteOn(velocity)</c>, then <c>Render</c>, then <c>NoteOff()</c> and <c>Render</c> until
/// <see cref="IsFinished" />.
/// </para>
/// <para>
/// Parameters are read ONCE PER BLOCK, at the top of <see cref="Render" />. Change an operator's
/// ratio, level or feedback whenever you like; the change lands on the next block, which is what
/// makes a bound knob or an LFO smooth rather than granular. Nothing is read per sample, nothing
/// allocates, and the envelopes are never disturbed by a parameter change.
/// </para>
/// <para>
/// WHAT IS MEASURED, AND WHAT IS NOT. One thing about <c>fm6op</c> has been measured against the
/// reference player: an oscillator with no <c>fm*</c> attributes at all renders a PURE SINE at the
/// note's frequency, 4.44 dB below what that player's <c>sine</c> waveform renders. This
/// oscillator reproduces that exactly - operator 1 alone at full level, everything else silent,
/// through <see cref="ReferenceOutputGain" />. Everything else - the modulation depth a level of
/// 1.0 buys, the detune step, the velocity curve, the four-stage rate table, the feedback depth -
/// is reconstructed from the published behaviour of the hardware, and each lives in one named
/// constant or property so a measurement pass can retune it. <see cref="Dx7Tables" /> holds the
/// numbers.
/// </para>
/// </remarks>
public sealed class Fm6OpOscillator : ModestOscillatorBase, IModestVoiceOscillator
{
    /// <summary>
    /// The output trim that puts this oscillator where the reference player puts it: one carrier
    /// at full level renders 4.44 dB below a plain <c>sine</c> oscillator.
    /// </summary>
    /// <remarks>
    /// MEASURED, but only as a product: the recording pins operator 1's default level TIMES this
    /// trim, not either one alone. There is no normalisation by the number of carriers, which is
    /// why the format warns that six loud operators can clip - six carriers at full level reach
    /// 3.6, not 1.0.
    /// </remarks>
    public const double ReferenceOutputGain = 0.6;

    /// <summary>How far a modulator at full level pushes its target's phase, in cycles.</summary>
    /// <remarks>
    /// MEASURED (round 2, item 27): TWO whole cycles, i.e. a modulation index of
    /// <c>beta = 4*pi*level</c>. Five modulator levels were fitted against the closed-form Bessel
    /// sideband pattern to within 0.3 dB and gave beta/level of 12.40 to 12.67 against
    /// <c>4*pi = 12.566</c>. One cycle - the first reading - is a factor of two too small.
    /// </remarks>
    public const double DefaultModulationDepthCycles = 2.0;

    /// <summary>
    /// How far a feedback amount of 1.0 pushes the receiving operator's phase, in cycles.
    /// UNMEASURED. One cycle puts the documented behaviour where the format puts it: 0.0 to 0.15
    /// warms the tone, 0.3 to 0.6 turns it sawtooth-like, and above 0.7 it breaks into noise.
    /// </summary>
    public const double DefaultFeedbackDepthCycles = 1.0;

    /// <summary>The velocity a voice starts at, so that <c>Reset</c> alone gives a full-level note.</summary>
    public const int DefaultVelocity = 127;

    /// <summary>The largest feedback amount that has any further effect.</summary>
    /// <remarks>
    /// MEASURED (round 3, item 42): <c>fmOpNFeedback</c> is clamped to 1.0 - settings of 1, 3 and 7
    /// rendered bit-identically.
    /// </remarks>
    public const double MaximumFeedback = 1.0;

    private readonly ModestFmOperator[] settings = new ModestFmOperator[Fm6OpAlgorithms.OperatorCount];
    private readonly Fm6OpOperatorState[] states = new Fm6OpOperatorState[Fm6OpAlgorithms.OperatorCount];

    private Fm6OpAlgorithm topology = Fm6OpAlgorithms.Get(Fm6OpAlgorithms.DefaultAlgorithm);
    private double outputGain = ReferenceOutputGain;
    private double modulationDepthCycles = DefaultModulationDepthCycles;
    private double feedbackDepthCycles = DefaultFeedbackDepthCycles;
    private int rateScaling;
    private int velocity = DefaultVelocity;
    private double feedbackHistory1;
    private double feedbackHistory2;
    private double effectiveFeedback;

    /// <summary>
    /// Creates a six-operator oscillator carrying the format's defaults: algorithm 1, operator 1
    /// at full level and the other five silent, which is the pure sine the reference player
    /// renders for an <c>fm6op</c> oscillator with no attributes.
    /// </summary>
    public Fm6OpOscillator()
    {
        for (int i = 0; i < settings.Length; i++)
        {
            settings[i] = new ModestFmOperator(i + 1);
            states[i] = new Fm6OpOperatorState();
        }
    }

    /// <inheritdoc />
    public override string Waveform => ModestWaveforms.Fm6Op;

    /// <summary>
    /// <c>fmAlgorithm</c>: which of the 32 routings to use, 1 to 32. Default 1. Clamped.
    /// </summary>
    /// <remarks>
    /// Changing it mid-note is allowed and takes effect on the next block. The operators keep
    /// their phases and their envelopes, so a bound knob sweeping the algorithm morphs rather than
    /// restarting.
    /// </remarks>
    public int Algorithm
    {
        get => topology.Number;
        set => topology = Fm6OpAlgorithms.Get(Fm6OpAlgorithms.Clamp(value));
    }

    /// <summary>The routing the current <see cref="Algorithm" /> selects - its carriers, its modulation paths and its feedback loop.</summary>
    public Fm6OpAlgorithm Topology => topology;

    /// <summary>
    /// What the sum of the carriers is multiplied by. Default <see cref="ReferenceOutputGain" />.
    /// Negative and non-finite values are ignored.
    /// </summary>
    public double OutputGain
    {
        get => outputGain;
        set { if (IsFinite(value) && value >= 0.0) { outputGain = value; } }
    }

    /// <summary>
    /// How far a modulator at full level pushes its target's phase, in cycles. Default
    /// <see cref="DefaultModulationDepthCycles" />. Negative and non-finite values are ignored.
    /// </summary>
    /// <remarks>The one knob that decides how bright this engine is; see the class remarks.</remarks>
    public double ModulationDepthCycles
    {
        get => modulationDepthCycles;
        set { if (IsFinite(value) && value >= 0.0) { modulationDepthCycles = value; } }
    }

    /// <summary>
    /// How far a feedback amount of 1.0 pushes the receiving operator's phase, in cycles. Default
    /// <see cref="DefaultFeedbackDepthCycles" />. Negative and non-finite values are ignored.
    /// </summary>
    public double FeedbackDepthCycles
    {
        get => feedbackDepthCycles;
        set { if (IsFinite(value) && value >= 0.0) { feedbackDepthCycles = value; } }
    }

    /// <summary>
    /// Whether <c>fmOp6Feedback</c> stands in for the feedback control in the twelve algorithms
    /// whose feedback loop is not on operator 6. Default <see langword="false" />.
    /// </summary>
    /// <remarks>
    /// The format says two things about feedback that do not quite agree: that only the algorithm's
    /// own feedback operator is audible, and that <c>fmOp6Feedback</c> is the control that works in
    /// all 32 algorithms. MEASURED (round 3, item 42): the first is what the reference does -
    /// feedback written on any operator but the algorithm's own is silently ignored - so the stand-in
    /// is off by default. Turn it on for a patch bank that relies on the second reading.
    /// </remarks>
    public bool UseOperator6FeedbackFallback { get; set; }

    /// <summary>
    /// How much faster the four-stage envelopes run at the top of the keyboard than at the bottom,
    /// 0 to 7. Default 0, which is off. Clamped.
    /// </summary>
    /// <remarks>
    /// The hardware calls this rate scaling and carries it per operator; the format has NO
    /// attribute for it, so it is one value for the whole voice and it starts switched off. It
    /// exists so that a patch imported from hardware can keep its key-follow, and it is
    /// UNMEASURED. The note it scales by is worked out from <see cref="ModestOscillatorBase.Frequency" />.
    /// </remarks>
    public int RateScaling
    {
        get => rateScaling;
        set => rateScaling = value < 0 ? 0
            : value > Dx7Tables.MaximumRateScaling ? Dx7Tables.MaximumRateScaling
            : value;
    }

    /// <summary>The MIDI velocity the current note was started with, 0 to 127.</summary>
    public int Velocity => velocity;

    /// <inheritdoc />
    /// <remarks>
    /// True when the key is up and every carrier that is not turned all the way down has finished
    /// its envelope. An operator whose release is the <c>-1</c> sentinel never finishes, so a
    /// voice built from the format's defaults is ended by the group's envelope rather than by
    /// this.
    /// </remarks>
    public override bool IsFinished
    {
        get
        {
            if (IsKeyDown) { return false; }

            int[] carriers = topology.CarrierNumbers;
            for (int i = 0; i < carriers.Length; i++)
            {
                Fm6OpOperatorState state = states[carriers[i] - 1];

                // A carrier turned all the way down cannot keep a voice alive however long its
                // envelope is: the algorithms hand every voice two to six carriers and a patch
                // usually silences most of them.
                if (!state.IsFinished && state.LevelScale > 0.0) { return false; }
            }

            return true;
        }
    }

    /// <summary>
    /// Whether this voice hands the end of the note to the group's envelope, because at least one
    /// carrier that can be heard carries the <c>-1</c> release sentinel.
    /// </summary>
    /// <remarks>
    /// This is the flag whatever owns the group's amplitude envelope has to honour: when it is
    /// true, <see cref="IsFinished" /> will never become true on its own and the voice must be
    /// stopped from outside. It is true for a voice built from the format's defaults, because the
    /// default <c>fmOpNRelease</c> is the sentinel.
    /// </remarks>
    public bool UsesOuterRelease
    {
        get
        {
            int[] carriers = topology.CarrierNumbers;
            for (int i = 0; i < carriers.Length; i++)
            {
                if (OperatorUsesOuterRelease(carriers[i]) && settings[carriers[i] - 1].Level > 0.0)
                {
                    return true;
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Whether one operator hands its release to the group's envelope - its <c>fmOpNRelease</c> or
    /// its <c>fmOpNAttack</c> is the <c>-1</c> sentinel.
    /// </summary>
    /// <param name="number">The operator, 1 to 6.</param>
    /// <returns><see langword="true" /> when the group's envelope decides when it stops.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="number" /> is outside 1 to 6.</exception>
    /// <remarks>
    /// Always false for an operator running the four-stage envelope: there the release is R4 and
    /// L4, and the ADSR attributes - the sentinel among them - are ignored.
    /// </remarks>
    public bool OperatorUsesOuterRelease(int number)
    {
        RequireOperator(number);

        ModestFmOperator operatorSettings = settings[number - 1];
        if (operatorSettings.EnvelopeType != ModestFmEnvelopeType.Adsr) { return false; }

        return operatorSettings.Release <= ModestFmOperator.OuterEnvelopeSentinel
            || operatorSettings.Attack <= ModestFmOperator.OuterEnvelopeSentinel;
    }

    /// <summary>
    /// The feedback amount actually in use, after the algorithm and
    /// <see cref="UseOperator6FeedbackFallback" /> have had their say. Refreshed at the top of each
    /// rendered block.
    /// </summary>
    public double EffectiveFeedback => effectiveFeedback;

    /// <summary>The six operators' parameters, in order, so <c>Operators[0]</c> is operator 1.</summary>
    /// <remarks>
    /// These are this voice's own copies: changing one changes this voice and nothing else. They
    /// are read at the top of every rendered block.
    /// </remarks>
    public IReadOnlyList<ModestFmOperator> Operators => settings;

    /// <summary>The parameters of one operator, numbered the way the attributes number them.</summary>
    /// <param name="number">The operator, 1 to 6.</param>
    /// <returns>Its parameters; never null.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="number" /> is outside 1 to 6.</exception>
    public ModestFmOperator GetOperator(int number)
    {
        RequireOperator(number);
        return settings[number - 1];
    }

    /// <summary>
    /// What frequency an operator is actually running at, after its ratio or fixed frequency and
    /// its detune.
    /// </summary>
    /// <param name="number">The operator, 1 to 6.</param>
    /// <returns>The frequency in Hz; never negative.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="number" /> is outside 1 to 6.</exception>
    public double GetOperatorFrequencyHz(int number)
    {
        RequireOperator(number);
        return OperatorFrequency(settings[number - 1]);
    }

    /// <summary>
    /// Copies a patch's FM parameters - the algorithm and all six operators - onto this voice.
    /// </summary>
    /// <param name="patch">The patch to copy from.</param>
    /// <exception cref="ArgumentNullException"><paramref name="patch" /> is null.</exception>
    /// <remarks>
    /// The values are copied, not shared, so the patch stays a template and this voice can be
    /// modulated on its own. It does not touch the phase, the envelopes or the note.
    /// </remarks>
    public void ApplyPatch(ModestPatch patch)
    {
        if (patch == null) { throw new ArgumentNullException(nameof(patch)); }

        Algorithm = patch.FmAlgorithm;
        for (int i = 0; i < settings.Length; i++)
        {
            patch.FmOperators[i].CopyTo(settings[i]);
        }
    }

    /// <inheritdoc />
    public override void NoteOn(int velocity)
    {
        base.NoteOn(velocity);
        this.velocity = NoteVelocity;
        StartEnvelopes();
    }

    /// <inheritdoc />
    public override void NoteOff()
    {
        if (!IsKeyDown) { return; }

        base.NoteOff();
        for (int i = 0; i < states.Length; i++)
        {
            states[i].NoteOff();
        }
    }

    /// <summary>
    /// Restarts the voice at a given phase, as at note-on, at whatever velocity the voice is
    /// carrying.
    /// </summary>
    /// <param name="phase">
    /// The starting phase in cycles, given to EVERY operator, so the operators start in the fixed
    /// relationship the algorithm's harmonics depend on. Only the fractional part matters.
    /// </param>
    /// <remarks>
    /// This is a note-on in its own right, so an <see cref="IModestOscillator" /> consumer that
    /// knows nothing about velocity still gets sound. When velocity matters, call
    /// <see cref="NoteOn" /> after it.
    /// </remarks>
    public override void Reset(double phase)
    {
        double start = WrapPhase(phase);
        for (int i = 0; i < states.Length; i++)
        {
            states[i].Phase = start;
        }

        StartEnvelopes();
    }

    /// <inheritdoc />
    public override void Render(Span<float> buffer)
    {
        if (buffer.Length == 0) { return; }

        RefreshFromSettings();

        int[] order = topology.RenderOrderNumbers;
        int[][] modulatorsOf = topology.ModulatorTable;
        int[] carriers = topology.CarrierNumbers;
        int feedbackDestination = topology.FeedbackDestination;
        int feedbackSource = topology.FeedbackSource;
        double modulationDepth = modulationDepthCycles;
        double feedbackDepth = feedbackDepthCycles * effectiveFeedback;
        double gain = outputGain;
        double history1 = feedbackHistory1;
        double history2 = feedbackHistory2;

        for (int n = 0; n < buffer.Length; n++)
        {
            for (int k = 0; k < order.Length; k++)
            {
                int number = order[k];
                Fm6OpOperatorState state = states[number - 1];

                double phaseOffset = 0.0;
                int[] sources = modulatorsOf[number - 1];
                for (int i = 0; i < sources.Length; i++)
                {
                    phaseOffset += states[sources[i] - 1].Output;
                }

                phaseOffset *= modulationDepth;

                if (number == feedbackDestination)
                {
                    // Averaging the last two samples is what keeps a feedback loop from screaming
                    // at Nyquist; the loop is one sample old by definition.
                    phaseOffset += feedbackDepth * 0.5 * (history1 + history2);
                }

                double envelope = state.Advance();
                state.Output = state.LevelScale * envelope *
                    Math.Sin(2.0 * Math.PI * (state.Phase + phaseOffset));

                double next = state.Phase + state.Increment;
                if (next >= 1.0) { next -= Math.Floor(next); }
                state.Phase = next;
            }

            history2 = history1;
            history1 = states[feedbackSource - 1].Output;

            double mix = 0.0;
            for (int i = 0; i < carriers.Length; i++)
            {
                mix += states[carriers[i] - 1].Output;
            }

            buffer[n] = (float)(gain * mix);
        }

        feedbackHistory1 = history1;
        feedbackHistory2 = history2;
    }

    private void StartEnvelopes()
    {
        RefreshFromSettings();

        feedbackHistory1 = 0.0;
        feedbackHistory2 = 0.0;
        IsKeyDown = true;

        for (int i = 0; i < states.Length; i++)
        {
            states[i].Output = 0.0;
            states[i].NoteOn();
        }
    }

    private void RefreshFromSettings()
    {
        double rateScalingUnits = rateScaling > 0
            ? Dx7Tables.RateScalingUnits(rateScaling, Dx7Tables.FrequencyToMidiNote(Frequency))
            : 0.0;

        for (int i = 0; i < settings.Length; i++)
        {
            ModestFmOperator operatorSettings = settings[i];
            Fm6OpOperatorState state = states[i];

            state.Increment = OperatorFrequency(operatorSettings) / SampleRate;
            state.LevelScale = operatorSettings.Level *
                Dx7Tables.VelocityScale(operatorSettings.VelocitySensitivity, velocity);
            state.Configure(operatorSettings, SampleRate, rateScalingUnits);
        }

        // MEASURED (round 3, item 42): fmOpNFeedback acts ONLY on the algorithm's own feedback
        // operator - op 6 in algorithms 1 and 32, op 2 in algorithm 2, op 4 in algorithm 8 - and
        // writing it on any other operator is silently ignored. It is also CLAMPED TO 1.0: values of
        // 1, 3 and 7 rendered bit-identically.
        int destination = topology.FeedbackDestination;
        double amount = settings[destination - 1].Feedback;
        if (amount <= 0.0 && UseOperator6FeedbackFallback && destination != Fm6OpAlgorithms.OperatorCount)
        {
            amount = settings[Fm6OpAlgorithms.OperatorCount - 1].Feedback;
        }

        effectiveFeedback = amount > MaximumFeedback ? MaximumFeedback : amount;
    }

    private double OperatorFrequency(ModestFmOperator operatorSettings)
    {
        double frequency = operatorSettings.Mode == ModestFmOperatorMode.Fixed
            ? operatorSettings.FixedFrequency
            : Frequency * operatorSettings.Ratio;

        // MEASURED (round 3, item 42): the detune offset is a power law in the operator's own
        // frequency, so it is read from the frequency the ratio or the fixed setting produced.
        frequency += Dx7Tables.DetuneHz(operatorSettings.Detune, frequency);
        return frequency < 0.0 ? 0.0 : frequency;
    }

    private static bool IsFinite(double value) => !double.IsNaN(value) && !double.IsInfinity(value);

    private static void RequireOperator(int number)
    {
        if (number < 1 || number > Fm6OpAlgorithms.OperatorCount)
        {
            throw new ArgumentOutOfRangeException(nameof(number), number,
                "An fm6op oscillator has operators 1 to " + Fm6OpAlgorithms.OperatorCount + ".");
        }
    }
}
