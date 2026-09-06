using System;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

/// <summary>
/// One destination a voice can render into: the main mix, a bus, an auxiliary pair, or nowhere.
/// </summary>
/// <remarks>
/// <para>
/// THE SLOT CONTRACT. A zone declares up to eight outputs, each an
/// <see cref="DecentSamplerOutputTarget"/> with its own volume. The voice runtime resolves each target
/// to a slot and adds its audio into that slot's stereo buffer, scaled by the output's volume. Slots
/// are numbered densely so a mixer can hold them in one array:
/// </para>
/// <list type="bullet">
/// <item><description>slot 0 is the main mix,</description></item>
/// <item><description>slots 1 to 16 are buses 1 to 16,</description></item>
/// <item><description>slots 17 to 32 are auxiliary pairs 1 to 16,</description></item>
/// <item><description><c>NO_OUTPUT</c> has no slot: <see cref="Index"/> is -1 and nothing is written.</description></item>
/// </list>
/// <para>
/// The sampler engine fills the slots and, having no bus or auxiliary processing of its own yet, sums
/// every filled slot into the stereo output. The effects phase keeps the same numbering and inserts
/// each bus's chain between the slot and the mix, and gives the auxiliary pairs their own outputs.
/// </para>
/// </remarks>
public readonly struct DecentSamplerOutputSlot : IEquatable<DecentSamplerOutputSlot>
{
    /// <summary>How many slots there are: the main mix, sixteen buses and sixteen auxiliary pairs.</summary>
    public const int Count = 33;

    /// <summary>The slot index of the main mix.</summary>
    public const int MainIndex = 0;

    /// <summary>The slot index of the first bus. Bus <c>n</c> is <c>FirstBusIndex + n - 1</c>.</summary>
    public const int FirstBusIndex = 1;

    /// <summary>The slot index of the first auxiliary pair. Aux <c>n</c> is <c>FirstAuxIndex + n - 1</c>.</summary>
    public const int FirstAuxIndex = 17;

    private DecentSamplerOutputSlot(DecentSamplerOutputSlotKind kind, int number, int index)
    {
        Kind = kind;
        Number = number;
        Index = index;
    }

    /// <summary>What kind of destination this is.</summary>
    public DecentSamplerOutputSlotKind Kind { get; }

    /// <summary>The 1-based bus or auxiliary number, or 0 for the main mix and for nowhere.</summary>
    public int Number { get; }

    /// <summary>The dense slot index, 0 to 32, or -1 when nothing is written.</summary>
    public int Index { get; }

    /// <summary>The slot audio is discarded into.</summary>
    public static DecentSamplerOutputSlot None { get; } =
        new DecentSamplerOutputSlot(DecentSamplerOutputSlotKind.None, 0, -1);

    /// <summary>The main mix.</summary>
    public static DecentSamplerOutputSlot Main { get; } =
        new DecentSamplerOutputSlot(DecentSamplerOutputSlotKind.Main, 0, MainIndex);

    /// <summary>
    /// The slot a routing target resolves to.
    /// </summary>
    /// <param name="target">The target written on the zone, group or bus.</param>
    /// <returns>The slot. <see cref="None"/> for <c>NO_OUTPUT</c>.</returns>
    public static DecentSamplerOutputSlot FromTarget(DecentSamplerOutputTarget target)
    {
        if (target == DecentSamplerOutputTarget.NoOutput)
        {
            return None;
        }

        if (target == DecentSamplerOutputTarget.MainOutput)
        {
            return Main;
        }

        if (target >= DecentSamplerOutputTarget.Bus1 && target <= DecentSamplerOutputTarget.Bus16)
        {
            var number = target - DecentSamplerOutputTarget.Bus1 + 1;
            return new DecentSamplerOutputSlot(
                DecentSamplerOutputSlotKind.Bus, number, FirstBusIndex + number - 1);
        }

        if (target >= DecentSamplerOutputTarget.AuxStereoOutput1 &&
            target <= DecentSamplerOutputTarget.AuxStereoOutput16)
        {
            var number = target - DecentSamplerOutputTarget.AuxStereoOutput1 + 1;
            return new DecentSamplerOutputSlot(
                DecentSamplerOutputSlotKind.Aux, number, FirstAuxIndex + number - 1);
        }

        return None;
    }

    /// <summary>
    /// The slot with a given dense index.
    /// </summary>
    /// <param name="index">The index, 0 to 32. Anything else gives <see cref="None"/>.</param>
    /// <returns>The slot.</returns>
    public static DecentSamplerOutputSlot FromIndex(int index)
    {
        if (index == MainIndex)
        {
            return Main;
        }

        if (FirstBusIndex <= index && index < FirstAuxIndex)
        {
            return new DecentSamplerOutputSlot(
                DecentSamplerOutputSlotKind.Bus, index - FirstBusIndex + 1, index);
        }

        if (FirstAuxIndex <= index && index < Count)
        {
            return new DecentSamplerOutputSlot(
                DecentSamplerOutputSlotKind.Aux, index - FirstAuxIndex + 1, index);
        }

        return None;
    }

    /// <inheritdoc/>
    public bool Equals(DecentSamplerOutputSlot other) => Index == other.Index;

    /// <inheritdoc/>
    public override bool Equals(object obj) => obj is DecentSamplerOutputSlot other && Equals(other);

    /// <inheritdoc/>
    public override int GetHashCode() => Index;

    /// <summary>Compares two slots.</summary>
    /// <param name="left">The first slot.</param>
    /// <param name="right">The second slot.</param>
    /// <returns><see langword="true"/> when they are the same slot.</returns>
    public static bool operator ==(DecentSamplerOutputSlot left, DecentSamplerOutputSlot right) =>
        left.Equals(right);

    /// <summary>Compares two slots.</summary>
    /// <param name="left">The first slot.</param>
    /// <param name="right">The second slot.</param>
    /// <returns><see langword="true"/> when they are different slots.</returns>
    public static bool operator !=(DecentSamplerOutputSlot left, DecentSamplerOutputSlot right) =>
        !left.Equals(right);

    /// <inheritdoc/>
    public override string ToString() =>
        Kind switch
        {
            DecentSamplerOutputSlotKind.Main => "MAIN_OUTPUT",
            DecentSamplerOutputSlotKind.Bus => "BUS_" + Number,
            DecentSamplerOutputSlotKind.Aux => "AUX_STEREO_OUTPUT_" + Number,
            _ => "NO_OUTPUT",
        };
}
