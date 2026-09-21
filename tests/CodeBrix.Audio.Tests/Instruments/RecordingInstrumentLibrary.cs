using System;
using System.Collections.Generic;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Tests.Synth;

namespace CodeBrix.Audio.Tests.Instruments;

/// <summary>
/// An instrument library whose synthesizers write down every message they are handed, and which
/// keeps hold of the ones it built - so a test can ask which child of a mapped library actually
/// received a note.
/// </summary>
/// <remarks>
/// <see cref="FakeInstrumentLibrary"/> answers the registry's questions and throws its
/// synthesizers away; this one is for the questions about ROUTING, where the point of the test is
/// which synthesizer heard what.
/// </remarks>
internal sealed class RecordingInstrumentLibrary : IInstrumentLibrary
{
    private readonly List<RecordingSynthesizer> parts = new List<RecordingSynthesizer>();
    private readonly InstrumentCoverage coverage;

    /// <summary>Creates a library under a name.</summary>
    /// <param name="name">The registry name.</param>
    /// <param name="level">The level every synthesizer it builds renders at.</param>
    /// <param name="coverage">What it claims to cover; the full General MIDI set when omitted.</param>
    /// <param name="supportsPerPart">Whether the per-part shape is offered.</param>
    /// <param name="supportsMultiTimbral">Whether the multi-timbral shape is offered.</param>
    public RecordingInstrumentLibrary(
        string name,
        float level = 0.25F,
        InstrumentCoverage coverage = null,
        bool supportsPerPart = true,
        bool supportsMultiTimbral = true)
    {
        Name = name;
        Level = level;
        SupportsPerPart = supportsPerPart;
        SupportsMultiTimbral = supportsMultiTimbral;
        this.coverage = coverage ?? InstrumentCoverage.General;
    }

    /// <inheritdoc />
    public string Name { get; }

    /// <inheritdoc />
    public string Description => "A library whose synthesizers write down what they are told.";

    /// <inheritdoc />
    public InstrumentCoverage Coverage => coverage;

    /// <inheritdoc />
    public bool SupportsPerPart { get; }

    /// <inheritdoc />
    public bool SupportsMultiTimbral { get; }

    /// <summary>The level every synthesizer this library builds renders at.</summary>
    public float Level { get; }

    /// <summary>The most recent multi-timbral synthesizer this library built.</summary>
    public RecordingSynthesizer MultiTimbral { get; private set; }

    /// <summary>The most recent percussion synthesizer this library built.</summary>
    public RecordingSynthesizer Percussion { get; private set; }

    /// <summary>Every per-part synthesizer this library has built, in order.</summary>
    public IReadOnlyList<RecordingSynthesizer> Parts => parts;

    /// <summary>The program each per-part synthesizer was built for, in the same order.</summary>
    public List<int> Programs { get; } = new List<int>();

    /// <inheritdoc />
    public IMidiSynthesizer CreateSynthesizer(int program, int sampleRate)
    {
        if (!SupportsPerPart)
        {
            throw new NotSupportedException(
                $"The instrument library '{Name}' does not offer the per-part shape.");
        }

        var synthesizer = new RecordingSynthesizer(Level, sampleRate);
        parts.Add(synthesizer);
        Programs.Add(program);
        return synthesizer;
    }

    /// <inheritdoc />
    public IMidiSynthesizer CreatePercussionSynthesizer(int sampleRate)
    {
        if (!SupportsPerPart)
        {
            throw new NotSupportedException(
                $"The instrument library '{Name}' does not offer the per-part shape.");
        }

        Percussion = new RecordingSynthesizer(Level, sampleRate);
        return Percussion;
    }

    /// <inheritdoc />
    public IMidiSynthesizer CreateMultiTimbralSynthesizer(int sampleRate)
    {
        if (!SupportsMultiTimbral)
        {
            throw new NotSupportedException(
                $"The instrument library '{Name}' does not offer the multi-timbral shape.");
        }

        MultiTimbral = new RecordingSynthesizer(Level, sampleRate);
        return MultiTimbral;
    }
}
