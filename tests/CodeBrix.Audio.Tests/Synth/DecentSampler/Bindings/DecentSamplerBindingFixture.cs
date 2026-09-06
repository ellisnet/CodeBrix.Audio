using System;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Bindings;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Bindings;

/// <summary>
/// Builds a one-sample Decent Sampler library around a test-authored preset body and loads it, so a
/// binding test is one string and a using block.
/// </summary>
internal sealed class DecentSamplerBindingFixture : IDisposable
{
    private readonly DecentSamplerTestPresets _library;

    private DecentSamplerBindingFixture(DecentSamplerTestPresets library, DecentSamplerInstrument instrument)
    {
        _library = library;
        Instrument = instrument;
    }

    /// <summary>The loaded instrument, with its bindings resolved and its initial state applied.</summary>
    public DecentSamplerInstrument Instrument { get; }

    /// <summary>The parameter and binding engine behind it.</summary>
    public DecentSamplerBindingEngine Engine => Instrument.BindingEngine;

    /// <summary>Loads a preset written as the whole document.</summary>
    /// <param name="presetXml">The preset text.</param>
    /// <returns>The fixture. The caller disposes it.</returns>
    public static DecentSamplerBindingFixture Load(string presetXml)
    {
        var library = DecentSamplerTestPresets.Create();
        library.WriteWav("Samples/tone.wav");
        library.WriteWav("Samples/other.wav");
        var instrument = library.Load(presetXml);
        return new DecentSamplerBindingFixture(library, instrument);
    }

    /// <summary>
    /// Loads a preset built from an optional interface section and an optional body, around one group
    /// with two samples tagged <c>mic1</c> and <c>mic2</c>.
    /// </summary>
    /// <param name="ui">The contents of the <c>&lt;tab&gt;</c> element, or null for none.</param>
    /// <param name="body">Anything else under <c>&lt;DecentSampler&gt;</c>, or null for none.</param>
    /// <param name="groups">The contents of <c>&lt;groups&gt;</c>, or null for the default group.</param>
    /// <returns>The fixture. The caller disposes it.</returns>
    public static DecentSamplerBindingFixture Build(string ui = null, string body = null, string groups = null)
    {
        var text =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
            "<DecentSampler minVersion=\"1.0.0\">\n" +
            (ui == null ? string.Empty : "  <ui width=\"812\" height=\"375\">\n    <tab>\n" + ui + "\n    </tab>\n  </ui>\n") +
            "  <groups>\n" +
            (groups ??
             "    <group name=\"main\" tags=\"body\">\n" +
             "      <sample path=\"Samples/tone.wav\" rootNote=\"60\" loNote=\"0\" hiNote=\"127\" tags=\"mic1\" />\n" +
             "      <sample path=\"Samples/other.wav\" rootNote=\"60\" loNote=\"0\" hiNote=\"127\" tags=\"mic2\" />\n" +
             "    </group>\n") +
            "  </groups>\n" +
            (body ?? string.Empty) +
            "</DecentSampler>\n";

        return Load(text);
    }

    /// <summary>Releases the instrument and deletes the temporary library.</summary>
    public void Dispose()
    {
        Instrument.Dispose();
        _library.Dispose();
    }
}
