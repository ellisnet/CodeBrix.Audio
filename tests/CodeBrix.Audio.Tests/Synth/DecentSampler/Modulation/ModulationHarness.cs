using System;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Modulation;

/// <summary>
/// Builds a one-zone instrument with a <c>&lt;modulators&gt;</c> section and renders it one block at
/// a time, so a test can read what a modulator did to a parameter on every block boundary.
/// </summary>
/// <remarks>
/// <para>
/// Most of the assertions here read a PARAMETER rather than the audio, because that is what a
/// modulator actually produces: the audio is the parameter's consequence, and one that a constant
/// sample turns into arithmetic. The tests that do measure audio use RMS windows, never peaks - a
/// peak cannot tell a fade from a steady level.
/// </para>
/// <para>
/// A global-scope modulator's contribution is published onto the group, zone or effect property, so
/// reading that property block by block traces the modulator exactly. A voice-scope one is not: its
/// value only exists while its own voice renders, so those tests measure the audio instead.
/// </para>
/// </remarks>
internal sealed class ModulationHarness : IDisposable
{
    /// <summary>The sample rate every modulation test renders at.</summary>
    public const int SampleRate = DecentSamplerEngineFixtures.SampleRate;

    /// <summary>The block size every modulation test renders in, matching the engine's default.</summary>
    public const int BlockSize = DecentSamplerEngineFixtures.BlockSize;

    private readonly DecentSamplerEngineFixtures _files;

    private ModulationHarness(DecentSamplerEngineFixtures files, DecentSamplerInstrument instrument)
    {
        _files = files;
        Instrument = instrument;
    }

    /// <summary>How long one rendered block lasts, in seconds.</summary>
    public static double BlockSeconds => BlockSize / (double)SampleRate;

    /// <summary>The loaded instrument.</summary>
    public DecentSamplerInstrument Instrument { get; }

    /// <summary>
    /// Loads a preset whose one group plays a constant-valued sample, with the given modulators.
    /// </summary>
    /// <param name="modulators">The inside of the <c>&lt;modulators&gt;</c> element.</param>
    /// <param name="groupAttributes">Extra attributes for the <c>&lt;group&gt;</c> element.</param>
    /// <param name="extraSections">Extra top-level sections, such as <c>&lt;effects&gt;</c>.</param>
    /// <returns>The harness. The caller disposes it.</returns>
    public static ModulationHarness Load(
        string modulators, string groupAttributes = "", string extraSections = "")
    {
        var files = DecentSamplerEngineFixtures.Create();
        files.WriteConstantWav("tone.wav", 0.5f, SampleRate * 4);

        var xml =
            "<DecentSampler>" +
            extraSections +
            "<groups><group " + groupAttributes + ">" +
            "<sample path=\"tone.wav\" rootNote=\"60\" loNote=\"0\" hiNote=\"127\" pitchKeyTrack=\"0\" />" +
            "</group></groups>" +
            "<modulators>" + modulators + "</modulators>" +
            "</DecentSampler>";

        return new ModulationHarness(files, files.LoadPreset(xml));
    }

    /// <summary>
    /// Loads a preset whose one group plays a sine, for the tests that measure pitch.
    /// </summary>
    /// <param name="modulators">The inside of the <c>&lt;modulators&gt;</c> element.</param>
    /// <param name="frequency">The recorded tone's frequency in Hz.</param>
    /// <returns>The harness. The caller disposes it.</returns>
    public static ModulationHarness LoadSine(string modulators, double frequency = 440.0)
    {
        var files = DecentSamplerEngineFixtures.Create();
        files.WriteSineWav("tone.wav", frequency, 0.5f, SampleRate * 4);

        var xml =
            "<DecentSampler><groups><group>" +
            "<sample path=\"tone.wav\" rootNote=\"60\" loNote=\"0\" hiNote=\"127\" pitchKeyTrack=\"0\" />" +
            "</group></groups>" +
            "<modulators>" + modulators + "</modulators></DecentSampler>";

        return new ModulationHarness(files, files.LoadPreset(xml));
    }

    /// <summary>Builds a synthesizer over this instrument at unity master volume.</summary>
    /// <param name="configure">An optional hook to change the settings before construction.</param>
    /// <returns>The synthesizer.</returns>
    public DecentSamplerSynthesizer Synthesizer(
        Action<DecentSamplerSynthesizerSettings> configure = null) =>
        DecentSamplerRenderProbe.Synthesizer(Instrument, configure);

    /// <summary>
    /// Renders one block at a time and reads a value after each, which traces what a global-scope
    /// modulator wrote block by block.
    /// </summary>
    /// <param name="synthesizer">The synthesizer to render.</param>
    /// <param name="blocks">How many blocks to render.</param>
    /// <param name="read">Reads the value being traced.</param>
    /// <returns>One reading per block.</returns>
    public static double[] Trace(DecentSamplerSynthesizer synthesizer, int blocks, Func<double> read)
    {
        var left = new float[BlockSize];
        var right = new float[BlockSize];
        var trace = new double[blocks];

        for (var block = 0; block < blocks; block++)
        {
            synthesizer.Render(left, right);
            trace[block] = read();
        }

        return trace;
    }

    /// <summary>
    /// Renders one block at a time, reads a value after each, and keeps the audio too.
    /// </summary>
    /// <param name="synthesizer">The synthesizer to render.</param>
    /// <param name="blocks">How many blocks to render.</param>
    /// <param name="read">Reads the value being traced.</param>
    /// <param name="trace">One reading per block.</param>
    /// <returns>The left channel of the render.</returns>
    public static float[] TraceAudio(
        DecentSamplerSynthesizer synthesizer, int blocks, Func<double> read, out double[] trace)
    {
        var left = new float[BlockSize];
        var right = new float[BlockSize];
        var audio = new float[blocks * BlockSize];

        trace = new double[blocks];

        for (var block = 0; block < blocks; block++)
        {
            synthesizer.Render(left, right);
            left.CopyTo(audio, block * BlockSize);
            trace[block] = read();
        }

        return audio;
    }

    /// <summary>How many blocks cover a duration in seconds, rounded up.</summary>
    /// <param name="seconds">The duration.</param>
    /// <returns>The block count, at least one.</returns>
    public static int Blocks(double seconds) =>
        Math.Max(1, (int)Math.Ceiling(seconds * SampleRate / BlockSize));

    /// <summary>The largest value in a trace.</summary>
    /// <param name="trace">The trace.</param>
    /// <returns>The maximum.</returns>
    public static double Max(double[] trace)
    {
        var maximum = double.MinValue;

        foreach (var value in trace)
        {
            maximum = Math.Max(maximum, value);
        }

        return maximum;
    }

    /// <summary>The smallest value in a trace.</summary>
    /// <param name="trace">The trace.</param>
    /// <returns>The minimum.</returns>
    public static double Min(double[] trace)
    {
        var minimum = double.MaxValue;

        foreach (var value in trace)
        {
            minimum = Math.Min(minimum, value);
        }

        return minimum;
    }

    /// <summary>
    /// How many times a trace crosses its own mid-point going upward, which is how the LFO tests
    /// measure a rate without caring about phase.
    /// </summary>
    /// <param name="trace">The trace.</param>
    /// <param name="middle">The level to count crossings of.</param>
    /// <returns>The number of upward crossings.</returns>
    public static int RisingCrossings(double[] trace, double middle)
    {
        var crossings = 0;

        for (var index = 1; index < trace.Length; index++)
        {
            if (trace[index - 1] <= middle && trace[index] > middle)
            {
                crossings++;
            }
        }

        return crossings;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Instrument.Dispose();
        _files.Dispose();
    }
}
