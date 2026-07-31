using System;
using System.Collections.Generic;
using System.IO;

// ReSharper disable once CheckNamespace
namespace CodeBrix.Audio.Synth; //was previously: MeltySynth

/// <summary>
/// Represents an instrument in the SoundFont.
/// </summary>
public sealed class SoundFontInstrument
{
    internal static readonly SoundFontInstrument Default = new SoundFontInstrument();

    private readonly string name;
    private readonly InstrumentRegion[] regions;

    private SoundFontInstrument()
    {
        name = "Default";
        regions = Array.Empty<InstrumentRegion>();
    }

    private SoundFontInstrument(InstrumentInfo info, Zone[] zones, SampleHeader[] samples)
    {
        this.name = info.Name;

        var zoneCount = info.ZoneEndIndex - info.ZoneStartIndex + 1;
        if (zoneCount <= 0)
        {
            throw new InvalidDataException($"The instrument '{info.Name}' has no zone.");
        }

        var zoneSpan = zones.AsSpan(info.ZoneStartIndex, zoneCount);

        regions = InstrumentRegion.Create(this, zoneSpan, samples);
    }

    internal static SoundFontInstrument[] Create(InstrumentInfo[] infos, Zone[] zones, SampleHeader[] samples)
    {
        if (infos.Length <= 1)
        {
            throw new InvalidDataException("No valid instrument was found.");
        }

        // The last one is the terminator.
        var instruments = new SoundFontInstrument[infos.Length - 1];

        for (var i = 0; i < instruments.Length; i++)
        {
            instruments[i] = new SoundFontInstrument(infos[i], zones, samples);
        }

        return instruments;
    }

    /// <summary>
    /// Gets the name of the instrument.
    /// </summary>
    /// <returns>
    /// The name of the instrument.
    /// </returns>
    public override string ToString()
    {
        return name;
    }

    /// <summary>
    /// The name of the instrument.
    /// </summary>
    public string Name => name;

    /// <summary>
    /// The regions of the instrument.
    /// </summary>
    public IReadOnlyList<InstrumentRegion> Regions => regions;

    // Internally exposes the raw array for fast enumeration.
    internal InstrumentRegion[] RegionArray => regions;
}
