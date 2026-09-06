using System.Collections.Generic;
using CodeBrix.Audio.ModestSynth.Effects;
using CodeBrix.Audio.ModestSynth.Effects.Internal;
using CodeBrix.Audio.ModestSynth.Fm;
using CodeBrix.Audio.ModestSynth.Harmonic;
using CodeBrix.Audio.ModestSynth.Oscillators;
using CodeBrix.Audio.ModestSynth.Wavetable;

namespace CodeBrix.Audio.ModestSynth.Internal;

/// <summary>
/// What this package offers the Decent Sampler engine: one registration per waveform it can
/// generate, built once and never changed.
/// </summary>
/// <remarks>
/// <para>
/// The list is what <c>ModestSynth.Register()</c> walks. Keeping it here, separate from the
/// registration call itself, means the set of waveforms can grow release by release without the
/// registration code changing, and it lets the tests check the offer without registering anything.
/// </para>
/// <para>
/// The creative effects have their own list beside it, built the same way. The two are separate
/// because the engine registers them through two different calls and because an oscillator is
/// per-voice state while an effect is per-chain-position state.
/// </para>
/// </remarks>
internal static class ModestSynthRegistry
{
    private static readonly ModestOscillatorRegistration[] Oscillators =
    {
        new ModestOscillatorRegistration(ModestWaveform.Sine, () => new SineOscillator()),
        new ModestOscillatorRegistration(ModestWaveform.Saw, () => new SawOscillator()),
        new ModestOscillatorRegistration(ModestWaveform.Square, () => new SquareOscillator()),
        new ModestOscillatorRegistration(ModestWaveform.Triangle, () => new TriangleOscillator()),
        new ModestOscillatorRegistration(ModestWaveform.Noise, () => new NoiseOscillator()),

        // The format spells white noise two ways, so the engine has to know both names.
        new ModestOscillatorRegistration(ModestWaveform.Noise, ModestWaveforms.WhiteNoise, () => new NoiseOscillator()),
        new ModestOscillatorRegistration(ModestWaveform.Pluck1, () => new Pluck1Oscillator()),
        new ModestOscillatorRegistration(ModestWaveform.Fm6Op, () => new Fm6OpOscillator()),
        new ModestOscillatorRegistration(ModestWaveform.Wavetable, () => new WavetableOscillator()),
        new ModestOscillatorRegistration(ModestWaveform.Harmonic, () => new HarmonicOscillator()),

        // MEASURED (round 2, item 26): the reference accepts "formant", makes one fixed tone with it
        // and ignores every attribute a preset writes. FormantOscillator renders that tone.
        new ModestOscillatorRegistration(ModestWaveform.Formant, () => new FormantOscillator()),
    };

    private static readonly ModestEffectRegistration[] Effects =
    {
        new ModestEffectRegistration(ModestEffectTypes.Phaser, ModestEffectBuilder.CreatePhaser),
        new ModestEffectRegistration(ModestEffectTypes.PitchShift, ModestEffectBuilder.CreatePitchShift),
        new ModestEffectRegistration(ModestEffectTypes.WaveFolder, ModestEffectBuilder.CreateWaveFolder),
        new ModestEffectRegistration(ModestEffectTypes.WaveShaper, ModestEffectBuilder.CreateWaveShaper),
        new ModestEffectRegistration(
            ModestEffectTypes.StereoSimulator, ModestEffectBuilder.CreateStereoSimulator),
        new ModestEffectRegistration(ModestEffectTypes.BitCrusher, ModestEffectBuilder.CreateBitCrusher),
        new ModestEffectRegistration(ModestEffectTypes.Gate, ModestEffectBuilder.CreateGate),
    };

    /// <summary>
    /// Every oscillator this package registers, in the order the waveforms are documented.
    /// </summary>
    internal static IReadOnlyList<ModestOscillatorRegistration> OscillatorRegistrations => Oscillators;

    /// <summary>
    /// Every creative effect this package registers, in the order the effects are documented.
    /// </summary>
    internal static IReadOnlyList<ModestEffectRegistration> EffectRegistrations => Effects;
}
