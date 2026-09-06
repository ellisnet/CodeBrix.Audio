namespace CodeBrix.Audio.ModestSynth.Tests.Integration;

/// <summary>Builds the small preset documents the integration tests play.</summary>
public static class PresetXml
{
    /// <summary>Wraps an interface section and a set of groups in a whole preset document.</summary>
    /// <param name="ui">The contents of the <c>tab</c> element, or null for no interface.</param>
    /// <param name="groups">The contents of the <c>groups</c> element.</param>
    /// <param name="body">Anything else under the root - an effect chain, for instance.</param>
    /// <returns>The preset text.</returns>
    public static string Wrap(string ui, string groups, string body = null) =>
        "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n" +
        "<DecentSampler minVersion=\"1.11.0\">\n" +
        (ui == null
            ? string.Empty
            : "  <ui width=\"812\" height=\"375\">\n    <tab name=\"main\">\n" + ui + "\n    </tab>\n  </ui>\n") +
        "  <groups>\n" + groups + "\n  </groups>\n" +
        (body ?? string.Empty) +
        "</DecentSampler>\n";

    /// <summary>One group holding one oscillator with a flat envelope and a short release.</summary>
    /// <param name="attributes">The oscillator's attributes, starting with the waveform.</param>
    /// <param name="groupAttributes">Extra attributes for the group.</param>
    /// <returns>The preset text.</returns>
    public static string OneOscillator(string attributes, string groupAttributes = "") =>
        Wrap(null,
            "    <group attack=\"0\" decay=\"0\" sustain=\"1\" release=\"0.05\" " + groupAttributes + ">\n" +
            "      <oscillator " + attributes + " />\n" +
            "    </group>");

    /// <summary>A knob whose only binding writes one group parameter.</summary>
    /// <param name="parameter">The binding parameter name.</param>
    /// <param name="minimum">The knob's lowest value.</param>
    /// <param name="maximum">The knob's highest value.</param>
    /// <param name="value">The knob's starting value.</param>
    /// <returns>The interface text.</returns>
    public static string Knob(string parameter, double minimum = 0.0, double maximum = 1.0, double value = 0.0) =>
        "      <labeled-knob x=\"0\" y=\"0\" width=\"40\" height=\"40\" label=\"knob\" parameterName=\"knob\"\n" +
        "                    valueType=\"float\" minValue=\"" + minimum + "\" maxValue=\"" + maximum +
        "\" value=\"" + value + "\">\n" +
        "        <binding type=\"amp\" level=\"group\" groupIndex=\"0\" parameter=\"" + parameter + "\"\n" +
        "                 translation=\"linear\" translationOutputMin=\"" + minimum +
        "\" translationOutputMax=\"" + maximum + "\" />\n" +
        "      </labeled-knob>";
}
