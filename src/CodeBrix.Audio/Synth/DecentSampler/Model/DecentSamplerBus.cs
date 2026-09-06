using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// One <c>&lt;bus&gt;</c>: a mix point with its own volume, effect chain and outputs.
/// </summary>
public sealed class DecentSamplerBus : DecentSamplerElement
{
    private DecentSamplerOutputTarget?[] _outputTargets;
    private double?[] _outputVolumes;

    /// <summary>The bus's 0-based position, so index 0 is <c>BUS_1</c>.</summary>
    public int Index { get; internal set; }

    /// <summary>The bus volume, 0 to 1 (<c>busVolume</c>). Default 1.</summary>
    public double? BusVolume { get; internal set; }

    /// <summary>
    /// The eight <c>outputNTarget</c> attributes, indexed from zero. Never null; empty when none were
    /// written. A bus may only target the main output or an auxiliary pair, never another bus.
    /// </summary>
    public IReadOnlyList<DecentSamplerOutputTarget?> OutputTargets =>
        (IReadOnlyList<DecentSamplerOutputTarget?>)_outputTargets ?? [];

    /// <summary>
    /// The eight <c>outputNVolume</c> attributes, indexed from zero. Never null; empty when none were
    /// written. Each defaults to 1.
    /// </summary>
    public IReadOnlyList<double?> OutputVolumes => (IReadOnlyList<double?>)_outputVolumes ?? [];

    /// <summary>The bus's effect chain, or null when it declares none.</summary>
    public DecentSamplerEffectsElement Effects { get; internal set; }

    /// <summary>The <c>outputNTarget</c> written here, or null when this bus did not write one.</summary>
    /// <param name="index">The output index, 0 to 7.</param>
    /// <returns>The target, or null.</returns>
    public DecentSamplerOutputTarget? OutputTarget(int index) =>
        _outputTargets == null || index < 0 || index >= _outputTargets.Length ? null : _outputTargets[index];

    /// <summary>The <c>outputNVolume</c> written here, or null when this bus did not write one.</summary>
    /// <param name="index">The output index, 0 to 7.</param>
    /// <returns>The volume, or null.</returns>
    public double? OutputVolume(int index) =>
        _outputVolumes == null || index < 0 || index >= _outputVolumes.Length ? null : _outputVolumes[index];

    internal void SetOutputTarget(int index, DecentSamplerOutputTarget target)
    {
        _outputTargets ??= new DecentSamplerOutputTarget?[8];
        _outputTargets[index] = target;
    }

    internal void SetOutputVolume(int index, double volume)
    {
        _outputVolumes ??= new double?[8];
        _outputVolumes[index] = volume;
    }
}
