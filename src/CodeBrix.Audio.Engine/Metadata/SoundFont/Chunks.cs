namespace CodeBrix.Audio.Engine.Metadata.SoundFont;  //was previously: SoundFlow.Metadata.SoundFont

/// <summary>
/// A container for parsed preset records from an SF2 file.
/// </summary>
public sealed class PresetChunk(PresetRecord[] presets)
{
    /// <summary>
    /// The preset ('phdr') records, including the terminal sentinel record required by the SF2 specification.
    /// </summary>
    public readonly PresetRecord[] Presets = presets;
}

/// <summary>
/// A container for parsed instrument records from an SF2 file.
/// </summary>
public sealed class InstrumentChunk(InstrumentRecord[] instruments)
{
    /// <summary>
    /// The instrument ('inst') records, including the terminal sentinel record required by the SF2 specification.
    /// </summary>
    public readonly InstrumentRecord[] Instruments = instruments;
}

/// <summary>
/// A container for parsed bag (zone) records from an SF2 file.
/// </summary>
public sealed class BagChunk(BagRecord[] bags)
{
    /// <summary>
    /// The zone/bag records ('pbag' or 'ibag'), including the terminal sentinel record required by the SF2 specification.
    /// </summary>
    public readonly BagRecord[] Bags = bags;
}

/// <summary>
/// A container for parsed generator records from an SF2 file.
/// </summary>
public sealed class GeneratorChunk(GeneratorRecord[] generators)
{
    /// <summary>
    /// The generator records ('pgen' or 'igen') that parameterize each zone.
    /// </summary>
    public readonly GeneratorRecord[] Generators = generators;
}

/// <summary>
/// A container for parsed sample header records from an SF2 file.
/// </summary>
public sealed class SampleHeaderChunk(SampleHeaderRecord[] sampleHeaders)
{
    /// <summary>
    /// The sample header ('shdr') records, including the terminal sentinel record required by the SF2 specification.
    /// </summary>
    public readonly SampleHeaderRecord[] SampleHeaders = sampleHeaders;
}

/// <summary>
/// A high-level container for all parsed metadata chunks from an SF2 file.
/// </summary>
public sealed class ParsedSoundFont
{
    /// <summary>
    /// The preset header ('phdr') chunk, or <see langword="null"/> if the file did not contain one.
    /// </summary>
    public PresetChunk? Presets { get; set; }

    /// <summary>
    /// The preset zone ('pbag') chunk, or <see langword="null"/> if the file did not contain one.
    /// </summary>
    public BagChunk? PresetBags { get; set; }

    /// <summary>
    /// The preset generator ('pgen') chunk, or <see langword="null"/> if the file did not contain one.
    /// </summary>
    public GeneratorChunk? PresetGenerators { get; set; }

    /// <summary>
    /// The instrument ('inst') chunk, or <see langword="null"/> if the file did not contain one.
    /// </summary>
    public InstrumentChunk? Instruments { get; set; }

    /// <summary>
    /// The instrument zone ('ibag') chunk, or <see langword="null"/> if the file did not contain one.
    /// </summary>
    public BagChunk? InstrumentBags { get; set; }

    /// <summary>
    /// The instrument generator ('igen') chunk, or <see langword="null"/> if the file did not contain one.
    /// </summary>
    public GeneratorChunk? InstrumentGenerators { get; set; }

    /// <summary>
    /// The sample header ('shdr') chunk, or <see langword="null"/> if the file did not contain one.
    /// </summary>
    public SampleHeaderChunk? SampleHeaders { get; set; }
}
