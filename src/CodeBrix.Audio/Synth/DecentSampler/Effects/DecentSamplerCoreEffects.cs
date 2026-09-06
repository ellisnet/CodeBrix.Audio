using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Effects;

/// <summary>
/// The effect types CodeBrix.Audio implements itself, and the registration that makes them available
/// to the Decent Sampler engine.
/// </summary>
/// <remarks>
/// <para>
/// The split follows the package rule: everything a SAMPLED instrument mixes with - filters, gain,
/// room, delay, chorus, convolution and dynamics - is in the core, so a consumer with only
/// CodeBrix.Audio can play a sample library in full. The creative, nonlinear effects (phaser,
/// pitch_shift, wave_folder, wave_shaper, stereo_simulator, bit_crusher and gate) belong to the
/// CodeBrix.Audio.ModestSynth add-on and are registered by its own <c>Register()</c> call.
/// </para>
/// <para>
/// The core set is registered into <see cref="DecentSamplerExtensions.Shared"/> when that class is
/// first touched, which is before any synthesizer can be built, so nothing has to be called to make a
/// reverb work. Call <see cref="RegisterInto"/> when a test or a host builds a registry of its own.
/// </para>
/// </remarks>
public static class DecentSamplerCoreEffects
{
    /// <summary>
    /// Every effect type name this package implements, in the spelling the format uses.
    /// </summary>
    public static IReadOnlyList<string> TypeNames { get; } =
    [
        "lowpass", "lowpass_4pl", "lowpass_1pl", "bandpass", "highpass", "notch", "peak", "gain",
        "reverb", "delay", "chorus", "convolution", "compressor",
    ];

    /// <summary>
    /// Registers every core effect type into a registry, replacing anything already registered under
    /// the same names.
    /// </summary>
    /// <param name="registry">The registry to fill.</param>
    /// <exception cref="ArgumentNullException"><paramref name="registry"/> is null.</exception>
    public static void RegisterInto(DecentSamplerExtensionRegistry registry)
    {
        if (registry == null)
        {
            throw new ArgumentNullException(nameof(registry));
        }

        // "lowpass_4pl" is the legacy spelling of "lowpass" and measures identically to it: one 2-pole
        // filter. There is no 4-pole filter in the format whatever the name says.
        registry.RegisterEffect(
            "lowpass", context => new DecentSamplerFilterEffect(context, DecentSamplerEffectType.Lowpass));
        registry.RegisterEffect(
            "lowpass_4pl", context => new DecentSamplerFilterEffect(context, DecentSamplerEffectType.Lowpass));
        registry.RegisterEffect(
            "bandpass", context => new DecentSamplerFilterEffect(context, DecentSamplerEffectType.Bandpass));
        registry.RegisterEffect(
            "highpass", context => new DecentSamplerFilterEffect(context, DecentSamplerEffectType.Highpass));
        registry.RegisterEffect(
            "notch", context => new DecentSamplerFilterEffect(context, DecentSamplerEffectType.Notch));
        registry.RegisterEffect(
            "peak", context => new DecentSamplerFilterEffect(context, DecentSamplerEffectType.Peak));

        registry.RegisterEffect("lowpass_1pl", context => new DecentSamplerOnePoleLowpassEffect(context));
        registry.RegisterEffect("gain", context => new DecentSamplerGainEffect(context));
        registry.RegisterEffect("reverb", context => new DecentSamplerReverbEffect(context));
        registry.RegisterEffect("delay", context => new DecentSamplerDelayEffect(context));
        registry.RegisterEffect("chorus", context => new DecentSamplerChorusEffect(context));
        registry.RegisterEffect("convolution", context => new DecentSamplerConvolutionEffect(context));
        registry.RegisterEffect("compressor", context => new DecentSamplerCompressorEffect(context));
    }
}
