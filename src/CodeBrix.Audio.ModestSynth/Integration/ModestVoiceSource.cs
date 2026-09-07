using System;
using System.Runtime.CompilerServices;
using CodeBrix.Audio.ModestSynth.Fm;
using CodeBrix.Audio.ModestSynth.Harmonic;
using CodeBrix.Audio.ModestSynth.Integration.Internal;
using CodeBrix.Audio.ModestSynth.Internal;
using CodeBrix.Audio.ModestSynth.Oscillators;
using CodeBrix.Audio.ModestSynth.Patch;
using CodeBrix.Audio.ModestSynth.Wavetable;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.ModestSynth.Integration;

/// <summary>
/// One Decent Sampler voice whose sound comes from an oscillator in this package instead of from a
/// recorded sample: the adapter between the format's <c>&lt;oscillator&gt;</c> element and
/// <see cref="IModestOscillator" />.
/// </summary>
/// <remarks>
/// <para>
/// <c>ModestSynth.Register()</c> registers one of these per waveform name, so an application never
/// constructs one directly; it is public because a host that builds its own
/// <c>DecentSamplerExtensionRegistry</c> may want to register it itself, and because a test can drive
/// it without a synthesizer.
/// </para>
/// <para>
/// WHAT IT READS. Every oscillator attribute of the developer guide, already resolved onto the zone by
/// the engine - the waveform, <c>damping</c> and <c>pluckType</c>, the wavetable file, frame size,
/// position and frame interpolation, <c>randomPhase</c>, the additive controls and all sixty-four
/// partial levels, and the FM algorithm with all twenty parameters of each of the six operators.
/// </para>
/// <para>
/// WHAT IS LIVE. The zone is the single source of truth, exactly as it is for a sample zone: an
/// <c>OSCILLATOR_*</c> binding writes the zone and this reads it back at the top of the next block, so
/// a knob or a modulator moves a note that is already sounding. The re-read is gated on the
/// instrument's <c>ParameterVersion</c>, so a block during which nothing moved costs one integer
/// comparison. <see cref="TrySetParameter" /> writes the oscillator directly for a host driving it
/// without bindings; the next binding change re-imposes the zone's own values over it.
/// </para>
/// <para>
/// PITCH. The engine hands the frequency in Hz - the MIDI note in equal temperament with A4 at 440,
/// plus the zone's tuning, its glide ramp and the channel's pitch bend - once per block through
/// <see cref="SetPitch" />, and this passes it straight to the oscillator. Nothing here converts notes
/// to frequencies.
/// </para>
/// <para>
/// LOUDNESS. The group's amplitude envelope gates the oscillator, because only <c>pluck1</c> and
/// <c>fm6op</c> stop by themselves. <see cref="ReferenceOscillatorGain" /> is the one trim that puts an
/// oscillator zone where the reference player puts one relative to a sample zone.
/// </para>
/// <para>
/// Rendering allocates nothing. Building the source and starting a note on a WAVEFORM IT HAS NOT
/// PLAYED BEFORE do allocate - and the first wavetable voice also reads a file - so the engine's
/// per-zone pooling is what keeps that off the steady state.
/// </para>
/// </remarks>
public sealed class ModestVoiceSource : IVoiceSource
{
    /// <summary>
    /// The gain applied to every oscillator, which is what puts an oscillator zone at the level the
    /// reference player puts one at relative to a sample zone.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Measured (items 12 and 13 of the reference-semantics measurements): a bare
    /// <c>waveform="sine"</c> oscillator zone was recorded at -8.81 dBFS RMS through a chain carrying
    /// the reference standalone's fixed -5.28 dB output trim, so the oscillator itself sits at
    /// -3.53 dBFS RMS - 0.52 dB below a full-scale sine, which is the level a full-scale SAMPLE zone
    /// plays at. That is this number.
    /// </para>
    /// <para>
    /// It is a single trim applied to every waveform, so the measured level RELATIONSHIPS between the
    /// waveforms - noise 2.30 dB below sine, <c>fm6op</c> 4.44 dB below it, <c>harmonic</c> and
    /// <c>wavetable</c> level with it - are the oscillators' own and are untouched by it.
    /// </para>
    /// </remarks>
    public const double ReferenceOscillatorGain = 0.9419;

    private const int WaveformSlots = 16;
    private const int OperatorCount = 6;

    private static readonly ConditionalWeakTable<OscillatorContext, VoiceCounter> Counters = new();

    private readonly DecentSamplerZone zone;
    private readonly DecentSamplerInstrument instrument;
    private readonly int sampleRate;
    private readonly ModestWaveform fallbackWaveform;
    private readonly ModestPatch patch = new ModestPatch();
    private readonly IModestVoiceOscillator[] built = new IModestVoiceOscillator[WaveformSlots];
    private readonly uint voiceSeed;

    private IModestVoiceOscillator oscillator;
    private Pluck1Oscillator pluck;
    private WavetableOscillator wavetable;
    private HarmonicOscillator harmonic;
    private Fm6OpOscillator fm;

    private ModestWaveform currentWaveform = ModestWaveform.Formant;
    private ModestWaveform? requestedWaveform;
    private int lastParameterVersion = int.MinValue;
    private bool finished;

    /// <summary>
    /// Creates a source for a zone, generating whatever waveform the zone names.
    /// </summary>
    /// <param name="context">What the engine tells an oscillator factory.</param>
    /// <exception cref="ArgumentNullException"><paramref name="context" /> is null.</exception>
    public ModestVoiceSource(OscillatorContext context)
        : this(context, ModestWaveform.Sine)
    {
    }

    /// <summary>
    /// Creates a source for a zone, naming the waveform to fall back to when the zone's own is not one
    /// this package generates.
    /// </summary>
    /// <param name="context">What the engine tells an oscillator factory.</param>
    /// <param name="fallback">
    /// The waveform to generate when the zone names one that is not recognised - which is how
    /// <c>formant</c> is a fixed tone of its own.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="context" /> is null.</exception>
    public ModestVoiceSource(OscillatorContext context, ModestWaveform fallback)
    {
        if (context == null) { throw new ArgumentNullException(nameof(context)); }

        zone = context.Zone;
        instrument = context.Instrument;
        sampleRate = context.SampleRate > 0 ? context.SampleRate : ModestOscillatorBase.DefaultSampleRate;
        fallbackWaveform = fallback;

        voiceSeed = NextVoiceSeed(context, zone);
    }

    /// <summary>Always false: an oscillator is one mono signal, and the voice pans it.</summary>
    public bool IsStereo => false;

    /// <inheritdoc />
    /// <remarks>
    /// True only for a waveform that ends by itself - a <c>pluck1</c> whose string has died away, or
    /// an <c>fm6op</c> whose carriers have all finished. Everything else is ended by the group's
    /// amplitude envelope, which is also what ends an <c>fm6op</c> using the <c>-1</c> release
    /// sentinel (its <c>UsesOuterRelease</c>), because such a voice never finishes on its own.
    /// </remarks>
    public bool IsFinished => finished || (oscillator != null && oscillator.IsFinished);

    /// <inheritdoc />
    /// <remarks>
    /// True for a <c>fm6op</c> voice whose operators carry real releases. MEASURED (round 4, item
    /// 56): such a voice's tail is the longest OPERATOR release, never the group envelope's. An
    /// <c>fm6op</c> on the format's <c>-1</c> release sentinel is the other way round - the group
    /// envelope ends it - and every other waveform is ended by the group envelope too.
    /// </remarks>
    public bool OwnsRelease =>
        oscillator is Fm.Fm6OpOscillator operators && !operators.UsesOuterRelease;

    /// <summary>The oscillator currently generating this voice, or null before the first note.</summary>
    /// <remarks>Diagnostics and tests. Do not hold on to it: the waveform can change at a note-on.</remarks>
    public IModestVoiceOscillator Oscillator => oscillator;

    /// <summary>The waveform being generated.</summary>
    public ModestWaveform Waveform => currentWaveform;

    /// <summary>
    /// One line saying why the zone did not get what it asked for - a wavetable file that could not be
    /// found, for instance - or null when nothing went wrong.
    /// </summary>
    /// <remarks>
    /// Written in the style of the reference player's preset validator. It is never thrown and never
    /// silences the voice: a wavetable with no usable table renders a plain sine, which is what the
    /// reference does.
    /// </remarks>
    public string Problem { get; private set; }

    /// <inheritdoc />
    public void Start(int note, int velocity, double pitchHz)
    {
        finished = false;

        EnsureOscillator();
        ApplyZoneParameters(true);
        SetPitch(pitchHz);

        oscillator.Reset(patch.GetStartPhase(voiceSeed));
        oscillator.NoteOn(velocity);
    }

    /// <inheritdoc />
    public void NoteOff() => oscillator?.NoteOff();

    /// <inheritdoc />
    public void SetPitch(double pitchHz)
    {
        // The engine can hand over a pitch a waveguide string cannot be tuned to - a note at the very
        // top of the keyboard with a tuning offset on it - so the frequency is clamped rather than
        // allowed to throw on the audio thread.
        oscillator?.SetFrequency(ModestPitch.Clamp(oscillator, sampleRate, pitchHz));
    }

    /// <inheritdoc />
    public bool Render(float[] left, float[] right, int frames)
    {
        if (left == null || frames <= 0 || oscillator == null) { return false; }
        if (finished) { return false; }

        ApplyZoneParameters(false);

        Span<float> block = new Span<float>(left, 0, frames);
        oscillator.Render(block);

        float gain = (float)ReferenceOscillatorGain;
        for (int i = 0; i < block.Length; i++)
        {
            block[i] *= gain;
        }

        // A waveform that ends by itself is allowed to finish the block it died in; the voice is
        // recycled on the next one.
        if (oscillator.IsFinished) { finished = true; }

        return true;
    }

    /// <inheritdoc />
    public bool TrySetParameter(string name, double value)
    {
        string folded = ModestOscillatorParameters.Fold(name);

        if (folded.Length == 0) { return false; }

        switch (folded)
        {
            case "waveform":
            case "shape":
                requestedWaveform = FromDecentSampler((DecentSamplerWaveform)(int)Math.Round(value));
                return true;

            case "damping":
                if (pluck == null) { return false; }
                pluck.Damping = value;
                return true;

            case "plucktype":
                if (pluck == null) { return false; }
                pluck.PluckType = value;
                return true;

            case "wavetableposition":
                if (wavetable == null) { return false; }
                wavetable.Position = value;
                return true;

            case "wavetableframeinterpolation":
                if (wavetable == null) { return false; }
                wavetable.FrameInterpolation = value >= 0.5;
                return true;

            case "harmonicnumpartials":
                if (harmonic == null) { return false; }
                harmonic.NumPartials = (int)Math.Round(value);
                return true;

            case "harmonictilt":
                if (harmonic == null) { return false; }
                harmonic.Tilt = value;
                return true;

            case "harmonicoddevenbalance":
                if (harmonic == null) { return false; }
                harmonic.OddEvenBalance = value;
                return true;

            case "harmonicnormalization":
                if (harmonic == null) { return false; }
                harmonic.Normalization = value;
                return true;

            case "fmalgorithm":
                if (fm == null) { return false; }
                fm.Algorithm = (int)Math.Round(value);
                return true;
        }

        if (ModestOscillatorParameters.TryIndexed(
                folded, "harmonicpartial", "level", ModestPatch.MaximumPartials, out int partial, out _))
        {
            if (harmonic == null) { return false; }
            harmonic.SetPartialLevel(partial, value);
            return true;
        }

        if (ModestOscillatorParameters.TryIndexed(
                folded, "fmop", null, OperatorCount, out int number, out string tail))
        {
            return fm != null && TrySetFmParameter(fm.GetOperator(number), tail, value);
        }

        return false;
    }

    /// <inheritdoc />
    public bool TryGetParameter(string name, out double value)
    {
        value = 0.0;

        string folded = ModestOscillatorParameters.Fold(name);

        if (folded.Length == 0) { return false; }

        switch (folded)
        {
            case "waveform":
            case "shape":
                value = (int)ToDecentSampler(currentWaveform);
                return true;

            case "damping":
                if (pluck == null) { return false; }
                value = pluck.Damping;
                return true;

            case "plucktype":
                if (pluck == null) { return false; }
                value = pluck.PluckType;
                return true;

            case "wavetableposition":
                if (wavetable == null) { return false; }
                value = wavetable.Position;
                return true;

            case "wavetableframeinterpolation":
                if (wavetable == null) { return false; }
                value = wavetable.FrameInterpolation ? 1.0 : 0.0;
                return true;

            case "harmonicnumpartials":
                if (harmonic == null) { return false; }
                value = harmonic.NumPartials;
                return true;

            case "harmonictilt":
                if (harmonic == null) { return false; }
                value = harmonic.Tilt;
                return true;

            case "harmonicoddevenbalance":
                if (harmonic == null) { return false; }
                value = harmonic.OddEvenBalance;
                return true;

            case "harmonicnormalization":
                if (harmonic == null) { return false; }
                value = harmonic.Normalization;
                return true;

            case "fmalgorithm":
                if (fm == null) { return false; }
                value = fm.Algorithm;
                return true;
        }

        if (ModestOscillatorParameters.TryIndexed(
                folded, "harmonicpartial", "level", ModestPatch.MaximumPartials, out int partial, out _))
        {
            if (harmonic == null) { return false; }
            value = harmonic.GetPartialLevel(partial);
            return true;
        }

        if (ModestOscillatorParameters.TryIndexed(
                folded, "fmop", null, OperatorCount, out int number, out string tail))
        {
            return fm != null && TryGetFmParameter(fm.GetOperator(number), tail, out value);
        }

        return false;
    }

    private static bool TrySetFmParameter(ModestFmOperator target, string tail, double value)
    {
        switch (tail)
        {
            case "ratio": target.Ratio = value; return true;
            case "detune": target.Detune = (int)Math.Round(value); return true;
            case "mode":
                target.Mode = value >= 0.5 ? ModestFmOperatorMode.Fixed : ModestFmOperatorMode.Ratio;
                return true;
            case "fixedfreq": target.FixedFrequency = value; return true;
            case "level": target.Level = value; return true;
            case "velocitysensitivity": target.VelocitySensitivity = (int)Math.Round(value); return true;
            case "feedback": target.Feedback = value; return true;
            case "attack": target.Attack = value; return true;
            case "decay": target.Decay = value; return true;
            case "sustain": target.Sustain = value; return true;
            case "release": target.Release = value; return true;
            case "egtype":
                target.EnvelopeType = value >= 0.5 ? ModestFmEnvelopeType.Dx7 : ModestFmEnvelopeType.Adsr;
                return true;
            case "egrate1": target.EgRate1 = (int)Math.Round(value); return true;
            case "egrate2": target.EgRate2 = (int)Math.Round(value); return true;
            case "egrate3": target.EgRate3 = (int)Math.Round(value); return true;
            case "egrate4": target.EgRate4 = (int)Math.Round(value); return true;
            case "eglevel1": target.EgLevel1 = (int)Math.Round(value); return true;
            case "eglevel2": target.EgLevel2 = (int)Math.Round(value); return true;
            case "eglevel3": target.EgLevel3 = (int)Math.Round(value); return true;
            case "eglevel4": target.EgLevel4 = (int)Math.Round(value); return true;
            default: return false;
        }
    }

    private static bool TryGetFmParameter(ModestFmOperator target, string tail, out double value)
    {
        switch (tail)
        {
            case "ratio": value = target.Ratio; return true;
            case "detune": value = target.Detune; return true;
            case "mode": value = (int)target.Mode; return true;
            case "fixedfreq": value = target.FixedFrequency; return true;
            case "level": value = target.Level; return true;
            case "velocitysensitivity": value = target.VelocitySensitivity; return true;
            case "feedback": value = target.Feedback; return true;
            case "attack": value = target.Attack; return true;
            case "decay": value = target.Decay; return true;
            case "sustain": value = target.Sustain; return true;
            case "release": value = target.Release; return true;
            case "egtype": value = (int)target.EnvelopeType; return true;
            case "egrate1": value = target.EgRate1; return true;
            case "egrate2": value = target.EgRate2; return true;
            case "egrate3": value = target.EgRate3; return true;
            case "egrate4": value = target.EgRate4; return true;
            case "eglevel1": value = target.EgLevel1; return true;
            case "eglevel2": value = target.EgLevel2; return true;
            case "eglevel3": value = target.EgLevel3; return true;
            case "eglevel4": value = target.EgLevel4; return true;
            default: value = 0.0; return false;
        }
    }

    private void EnsureOscillator()
    {
        ModestWaveform wanted = WantedWaveform();

        if (oscillator != null && wanted == currentWaveform) { return; }

        int slot = (int)wanted;

        if (slot < 0 || slot >= WaveformSlots) { slot = (int)ModestWaveform.Sine; wanted = ModestWaveform.Sine; }

        built[slot] ??= Build(wanted);

        currentWaveform = wanted;
        oscillator = built[slot];
        pluck = oscillator as Pluck1Oscillator;
        wavetable = oscillator as WavetableOscillator;
        harmonic = oscillator as HarmonicOscillator;
        fm = oscillator as Fm6OpOscillator;
        lastParameterVersion = int.MinValue;
    }

    private ModestWaveform WantedWaveform()
    {
        if (requestedWaveform.HasValue) { return requestedWaveform.Value; }

        return FromDecentSampler(zone.Waveform);
    }

    private ModestWaveform FromDecentSampler(DecentSamplerWaveform waveform) =>
        waveform switch
        {
            DecentSamplerWaveform.Sine => ModestWaveform.Sine,
            DecentSamplerWaveform.Saw => ModestWaveform.Saw,
            DecentSamplerWaveform.Square => ModestWaveform.Square,
            DecentSamplerWaveform.Triangle => ModestWaveform.Triangle,
            DecentSamplerWaveform.Noise => ModestWaveform.Noise,
            DecentSamplerWaveform.WhiteNoise => ModestWaveform.Noise,
            DecentSamplerWaveform.Pluck1 => ModestWaveform.Pluck1,
            DecentSamplerWaveform.Wavetable => ModestWaveform.Wavetable,
            DecentSamplerWaveform.Harmonic => ModestWaveform.Harmonic,
            DecentSamplerWaveform.Fm6Op => ModestWaveform.Fm6Op,
            _ => fallbackWaveform,
        };

    private static DecentSamplerWaveform ToDecentSampler(ModestWaveform waveform) =>
        waveform switch
        {
            ModestWaveform.Sine => DecentSamplerWaveform.Sine,
            ModestWaveform.Saw => DecentSamplerWaveform.Saw,
            ModestWaveform.Square => DecentSamplerWaveform.Square,
            ModestWaveform.Triangle => DecentSamplerWaveform.Triangle,
            ModestWaveform.Noise => DecentSamplerWaveform.Noise,
            ModestWaveform.Pluck1 => DecentSamplerWaveform.Pluck1,
            ModestWaveform.Wavetable => DecentSamplerWaveform.Wavetable,
            ModestWaveform.Harmonic => DecentSamplerWaveform.Harmonic,
            ModestWaveform.Fm6Op => DecentSamplerWaveform.Fm6Op,
            _ => DecentSamplerWaveform.Unknown,
        };

    private IModestVoiceOscillator Build(ModestWaveform waveform)
    {
        patch.Waveform = waveform;
        patch.RandomPhase = zone.RandomPhase;
        patch.Seed = voiceSeed;
        patch.WavetableFile = null;
        patch.WavetableFrameSize = zone.WavetableFrameSize;

        FillPatchFromZone();

        IModestOscillator created = patch.CreateOscillator(sampleRate);
        IModestVoiceOscillator voice = (IModestVoiceOscillator)created;

        if (created is WavetableOscillator table) { LoadWavetable(table); }

        return voice;
    }

    private void LoadWavetable(WavetableOscillator table)
    {
        if (string.IsNullOrWhiteSpace(zone.WavetableFile))
        {
            // The patch already recorded "no file"; the reference plays a plain sine for exactly this.
            Problem = table.Problem;
            return;
        }

        if (ModestPresetFiles.TryLoadWavetable(
                instrument, zone.WavetableFile, zone.WavetableFrameSize, out WavetableFile file,
                out string problem))
        {
            table.Table = file;
            return;
        }

        table.ReportProblem(problem);
        Problem = problem;
    }

    // Copies the zone onto the patch. Called when an oscillator is built; the per-block refresh writes
    // the oscillator directly, because that is the path that must not allocate.
    private void FillPatchFromZone()
    {
        patch.Damping = zone.Damping;
        patch.PluckType = zone.PluckType;
        patch.WavetablePosition = zone.WavetablePosition;
        patch.WavetableFrameInterpolation = zone.WavetableFrameInterpolation;

        patch.NumPartials = zone.NumPartials;
        patch.HarmonicTilt = zone.HarmonicTilt;
        patch.HarmonicOddEvenBalance = zone.HarmonicOddEvenBalance;
        patch.HarmonicNormalization = zone.HarmonicNormalization;

        var levels = zone.HarmonicPartialLevels;
        for (int partial = 1; partial <= ModestPatch.MaximumPartials; partial++)
        {
            patch.SetPartialLevel(partial, levels[partial - 1]);
        }

        CopyFmOperators();
    }

    private void CopyFmOperators()
    {
        patch.FmAlgorithm = zone.FmAlgorithm;

        var operators = zone.FmOperators;

        for (int index = 0; index < OperatorCount; index++)
        {
            DecentSamplerFmOperator source = operators[index];
            ModestFmOperator target = patch.FmOperators[index];

            target.Ratio = source.Ratio ?? 1.0;
            target.Detune = (int)Math.Round(source.Detune ?? 0.0);
            target.Mode = source.Mode == DecentSamplerFmOperatorMode.Fixed
                ? ModestFmOperatorMode.Fixed
                : ModestFmOperatorMode.Ratio;
            target.FixedFrequency = source.FixedFrequency ?? 440.0;
            target.Level = source.Level ?? (index == 0 ? 1.0 : 0.0);
            target.VelocitySensitivity = (int)Math.Round(source.VelocitySensitivity ?? 0.0);
            target.Feedback = source.Feedback ?? 0.0;
            target.Attack = source.Attack ?? 0.0;
            target.Decay = source.Decay ?? 0.0;
            target.Sustain = source.Sustain ?? 1.0;
            target.Release = source.Release ?? ModestFmOperator.OuterEnvelopeSentinel;
            target.EnvelopeType = source.EnvelopeType == DecentSamplerFmEnvelopeType.Dx7
                ? ModestFmEnvelopeType.Dx7
                : ModestFmEnvelopeType.Adsr;

            target.EgRate1 = (int)Math.Round(source.EgRates[0] ?? 99.0);
            target.EgRate2 = (int)Math.Round(source.EgRates[1] ?? 99.0);
            target.EgRate3 = (int)Math.Round(source.EgRates[2] ?? 0.0);
            target.EgRate4 = (int)Math.Round(source.EgRates[3] ?? 99.0);
            target.EgLevel1 = (int)Math.Round(source.EgLevels[0] ?? 99.0);
            target.EgLevel2 = (int)Math.Round(source.EgLevels[1] ?? 99.0);
            target.EgLevel3 = (int)Math.Round(source.EgLevels[2] ?? 99.0);
            target.EgLevel4 = (int)Math.Round(source.EgLevels[3] ?? 0.0);
        }
    }

    // Pushes the zone's current values onto the oscillator. Gated on the instrument's parameter version
    // so that a block during which no binding fired costs one comparison and no work at all.
    private void ApplyZoneParameters(bool force)
    {
        if (instrument != null)
        {
            int version = instrument.ParameterVersion;
            if (!force && version == lastParameterVersion) { return; }
            lastParameterVersion = version;
        }

        if (pluck != null)
        {
            pluck.Damping = zone.Damping;
            pluck.PluckType = zone.PluckType;
            return;
        }

        if (wavetable != null)
        {
            wavetable.Position = zone.WavetablePosition;
            wavetable.FrameInterpolation = zone.WavetableFrameInterpolation;
            return;
        }

        if (harmonic != null)
        {
            harmonic.NumPartials = zone.NumPartials;
            harmonic.Tilt = zone.HarmonicTilt;
            harmonic.OddEvenBalance = zone.HarmonicOddEvenBalance;
            harmonic.Normalization = zone.HarmonicNormalization;

            var levels = zone.HarmonicPartialLevels;
            for (int partial = 1; partial <= ModestPatch.MaximumPartials; partial++)
            {
                harmonic.SetPartialLevel(partial, levels[partial - 1]);
            }

            return;
        }

        if (fm != null)
        {
            CopyFmOperators();
            fm.ApplyPatch(patch);
        }
    }

    // A seed per voice per zone, handed out in creation order, so that layered noise and pluck voices
    // do not render the same samples and sum to one louder copy - and so that the same instrument
    // playing the same notes renders identically every run.
    private static uint NextVoiceSeed(OscillatorContext context, DecentSamplerZone zone)
    {
        VoiceCounter counter = Counters.GetValue(context, _ => new VoiceCounter());
        int ordinal;

        lock (counter)
        {
            ordinal = counter.Next++;
        }

        uint identity = (uint)((zone.Group?.Index ?? 0) * 131 + zone.Index + 1);
        uint seed = (identity * 2654435761u) ^ ((uint)ordinal * 2246822519u);

        return seed == 0u ? 1u : seed;
    }

    private sealed class VoiceCounter
    {
        internal int Next;
    }
}
