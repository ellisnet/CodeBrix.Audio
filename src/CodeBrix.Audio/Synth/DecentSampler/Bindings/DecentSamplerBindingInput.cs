namespace CodeBrix.Audio.Synth.DecentSampler.Bindings;

/// <summary>
/// What a binding's source is offering: the raw value and the range it moves in, which is what a
/// linear translation normalises against.
/// </summary>
/// <remarks>
/// A knob's range is its <c>minValue</c> and <c>maxValue</c>; a MIDI controller's is 0 to 127; an X-Y
/// pad axis's is 0 to 1; a menu option's is 1 to the number of options; a button state's is 0 to the
/// number of states minus one; a modulator's is 0 to 1.
/// </remarks>
internal readonly struct DecentSamplerBindingInput(double value, double minimum, double maximum)
{
    /// <summary>The raw value the source is offering.</summary>
    public double Value { get; } = value;

    /// <summary>The lowest value the source produces.</summary>
    public double Minimum { get; } = minimum;

    /// <summary>The highest value the source produces.</summary>
    public double Maximum { get; } = maximum;

    /// <summary>A controller input, 0 to 127.</summary>
    /// <param name="value">The controller value.</param>
    /// <returns>The input.</returns>
    public static DecentSamplerBindingInput Controller(double value) =>
        new DecentSamplerBindingInput(value, 0.0, 127.0);

    /// <summary>A normalised input, 0 to 1.</summary>
    /// <param name="value">The value.</param>
    /// <returns>The input.</returns>
    public static DecentSamplerBindingInput Normalised(double value) =>
        new DecentSamplerBindingInput(value, 0.0, 1.0);
}
