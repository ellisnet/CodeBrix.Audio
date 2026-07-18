using System.Runtime.InteropServices;

namespace CodeBrix.Audio.Engine.Metadata.SoundFont;  //was previously: SoundFlow.Metadata.SoundFont

#pragma warning disable CS0649 // Field is never assigned to

/// <summary>
/// Maps to the 'phdr' chunk record, defining a preset.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct PresetRecord
{
    /// <summary>The preset name (20-byte ASCII, null-terminated).</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
    public byte[] Name;

    /// <summary>The MIDI preset (program) number.</summary>
    public ushort Preset;

    /// <summary>The MIDI bank number.</summary>
    public ushort Bank;

    /// <summary>Index into the preset zone ('pbag') list where this preset's zones begin.</summary>
    public ushort PresetBagIndex;

    /// <summary>Reserved by the SF2 specification (unused); preserved from the file.</summary>
    public uint Library;

    /// <summary>Reserved by the SF2 specification (unused); preserved from the file.</summary>
    public uint Genre;

    /// <summary>Reserved by the SF2 specification (unused); preserved from the file.</summary>
    public uint Morphology;
}

/// <summary>
/// Maps to the 'ibag' and 'pbag' chunk records, defining a zone.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct BagRecord
{
    /// <summary>Index into the generator ('pgen' or 'igen') list where this zone's generators begin.</summary>
    public ushort GeneratorIndex;

    /// <summary>Index into the modulator ('pmod' or 'imod') list where this zone's modulators begin.</summary>
    public ushort ModulatorIndex;
}

/// <summary>
/// Maps to the 'igen' and 'pgen' chunk records, defining a generator.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct GeneratorRecord
{
    /// <summary>The generator operator, identifying which synthesis parameter this record sets.</summary>
    public GeneratorType Operator;

    /// <summary>The generator amount; its signed 16-bit value, interpreted according to the <see cref="Operator"/>.</summary>
    public short Amount;
}

/// <summary>
/// Maps to the 'inst' chunk record, defining an instrument.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct InstrumentRecord
{
    /// <summary>The instrument name (20-byte ASCII, null-terminated).</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
    public byte[] Name;

    /// <summary>Index into the instrument zone ('ibag') list where this instrument's zones begin.</summary>
    public ushort InstrumentBagIndex;
}

/// <summary>
/// Maps to the 'shdr' chunk record, defining a sample.
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 1)]
public struct SampleHeaderRecord
{
    /// <summary>The sample name (20-byte ASCII, null-terminated).</summary>
    [MarshalAs(UnmanagedType.ByValArray, SizeConst = 20)]
    public byte[] Name;

    /// <summary>Index of the first sample point of this sample within the sample data pool.</summary>
    public uint Start;

    /// <summary>Index of the sample point immediately following the last point of this sample.</summary>
    public uint End;

    /// <summary>Index of the first sample point of the loop.</summary>
    public uint StartLoop;

    /// <summary>Index of the sample point immediately following the last point of the loop.</summary>
    public uint EndLoop;

    /// <summary>The sample rate, in hertz, at which this sample was recorded.</summary>
    public uint SampleRate;

    /// <summary>The MIDI key number of the recorded pitch of the sample.</summary>
    public byte OriginalKey;

    /// <summary>Pitch correction, in cents, to apply to the sample's recorded pitch.</summary>
    public sbyte Correction;

    /// <summary>Index of the linked sample header for stereo (left/right) samples.</summary>
    public ushort SampleLink;

    /// <summary>The sample type flags (mono, left, right, linked, or ROM).</summary>
    public ushort SampleType;
}

#pragma warning restore CS0649
