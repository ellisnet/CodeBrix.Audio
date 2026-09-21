using System;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Tests.Synth;

namespace CodeBrix.Audio.Tests.Instruments;

/// <summary>
/// An instrument library that makes no sound, for the registry tests: they are about names,
/// defaults and errors, none of which needs a real instrument.
/// </summary>
/// <remarks>
/// Each test gives its library a name of its own, because the registry is process-wide and the
/// ungated tests share it with everything else running at the same time.
/// </remarks>
internal sealed class FakeInstrumentLibrary : IInstrumentLibrary
{
    private readonly InstrumentCoverage coverage;

    /// <summary>Creates a library under a name, offering both shapes by default.</summary>
    /// <param name="name">The registry name.</param>
    /// <param name="description">What it claims to sound like.</param>
    /// <param name="supportsPerPart">Whether the per-part shape is offered.</param>
    /// <param name="supportsMultiTimbral">Whether the multi-timbral shape is offered.</param>
    /// <param name="coverage">What it claims to cover; the full General MIDI set when omitted.</param>
    public FakeInstrumentLibrary(
        string name,
        string description = "A library that makes no sound.",
        bool supportsPerPart = true,
        bool supportsMultiTimbral = true,
        InstrumentCoverage coverage = null)
    {
        Name = name;
        Description = description;
        SupportsPerPart = supportsPerPart;
        SupportsMultiTimbral = supportsMultiTimbral;
        this.coverage = coverage ?? InstrumentCoverage.General;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Description { get; }

    /// <inheritdoc />
    public InstrumentCoverage Coverage => coverage;

    /// <inheritdoc />
    public bool SupportsPerPart { get; }

    /// <inheritdoc />
    public bool SupportsMultiTimbral { get; }

    /// <summary>How many synthesizers this library has been asked to build.</summary>
    public int CreatedCount { get; private set; }

    /// <inheritdoc />
    public IMidiSynthesizer CreateSynthesizer(int program, int sampleRate)
    {
        if (!SupportsPerPart)
        {
            throw new NotSupportedException(
                $"The instrument library '{Name}' does not offer the per-part shape.");
        }

        CreatedCount++;
        return new RecordingSynthesizer(sampleRate: sampleRate);
    }

    /// <inheritdoc />
    public IMidiSynthesizer CreatePercussionSynthesizer(int sampleRate)
    {
        if (!SupportsPerPart)
        {
            throw new NotSupportedException(
                $"The instrument library '{Name}' does not offer the per-part shape.");
        }

        CreatedCount++;
        return new RecordingSynthesizer(sampleRate: sampleRate);
    }

    /// <inheritdoc />
    public IMidiSynthesizer CreateMultiTimbralSynthesizer(int sampleRate)
    {
        if (!SupportsMultiTimbral)
        {
            throw new NotSupportedException(
                $"The instrument library '{Name}' does not offer the multi-timbral shape.");
        }

        CreatedCount++;
        return new RecordingSynthesizer(sampleRate: sampleRate);
    }
}
