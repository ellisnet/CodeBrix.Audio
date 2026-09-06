using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Sequencing;

/// <summary>
/// A tiny synthetic library, an instrument and a synthesizer, plus the onset measurement every timing
/// test in this folder reads. The sample is a short constant-valued blip played with no key tracking,
/// so every generated note is the same shape and its first frame is exactly its onset.
/// </summary>
internal sealed class SequencingWorld : IDisposable
{
    /// <summary>How many frames the blip sample runs for. Shorter than any step the tests use.</summary>
    public const int BlipFrames = 1200;

    /// <summary>The blip's sample value.</summary>
    public const float BlipLevel = 0.5f;

    private SequencingWorld(
        DecentSamplerEngineFixtures fixtures,
        DecentSamplerInstrument instrument,
        DecentSamplerSynthesizer synthesizer)
    {
        Fixtures = fixtures;
        Instrument = instrument;
        Synthesizer = synthesizer;
    }

    /// <summary>The temp library.</summary>
    public DecentSamplerEngineFixtures Fixtures { get; }

    /// <summary>The loaded instrument.</summary>
    public DecentSamplerInstrument Instrument { get; }

    /// <summary>The synthesizer under test, at unity master volume.</summary>
    public DecentSamplerSynthesizer Synthesizer { get; }

    /// <summary>Builds a world around a preset body, writing the blip sample beside it.</summary>
    /// <param name="preset">The preset XML.</param>
    /// <param name="configure">An optional hook to change the synthesizer settings.</param>
    /// <param name="prepare">An optional hook to write more samples before the preset loads.</param>
    /// <returns>The world. The caller disposes it.</returns>
    public static SequencingWorld Build(
        string preset,
        Action<DecentSamplerSynthesizerSettings> configure = null,
        Action<DecentSamplerEngineFixtures> prepare = null)
    {
        var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/blip.wav", BlipLevel, BlipFrames);
        fixtures.WriteConstantWav("Samples/long.wav", BlipLevel, DecentSamplerEngineFixtures.SampleRate * 4);
        prepare?.Invoke(fixtures);

        var instrument = fixtures.LoadPreset(preset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument, configure);

        return new SequencingWorld(fixtures, instrument, synthesizer);
    }

    /// <summary>The standard one-zone group: a blip at any key, no velocity tracking, no key tracking.</summary>
    /// <returns>The XML.</returns>
    public static string BlipGroup() =>
        """
          <groups>
            <group ampVelTrack="0" attack="0" decay="0" sustain="1" release="0.001">
              <sample path="Samples/blip.wav" rootNote="60" loNote="0" hiNote="127" pitchKeyTrack="0" />
            </group>
          </groups>
        """;

    /// <summary>
    /// A group whose sample runs for four seconds, so a note's own gate rather than the end of the
    /// file decides how long it sounds.
    /// </summary>
    /// <returns>The XML.</returns>
    public static string SustainGroup() =>
        """
          <groups>
            <group ampVelTrack="0" attack="0" decay="0" sustain="1" release="0.001">
              <sample path="Samples/long.wav" rootNote="60" loNote="0" hiNote="127" pitchKeyTrack="0" />
            </group>
          </groups>
        """;

    /// <summary>
    /// A group whose keys each play the blip at their own volume, so that the RENDER LEVEL says which
    /// key sounded. Key <paramref name="lowKey"/> plays at a tenth of the blip, the next at two
    /// tenths, and so on. That is how the sequencer tests read a transposition or a loop order
    /// without needing to measure pitch.
    /// </summary>
    /// <param name="lowKey">The first key.</param>
    /// <param name="count">How many consecutive keys to map, at most ten.</param>
    /// <returns>The XML.</returns>
    public static string KeyedBlipGroup(int lowKey, int count)
    {
        var builder = new StringBuilder();
        builder.AppendLine("  <groups>");
        builder.AppendLine(
            "    <group ampVelTrack=\"0\" attack=\"0\" decay=\"0\" sustain=\"1\" release=\"0.001\">");

        for (var index = 0; index < count; index++)
        {
            var key = (lowKey + index).ToString(CultureInfo.InvariantCulture);
            var volume = ((index + 1) / 10.0).ToString("0.0#", CultureInfo.InvariantCulture);

            builder.AppendLine(
                "      <sample path=\"Samples/blip.wav\" rootNote=\"" + key + "\" loNote=\"" + key +
                "\" hiNote=\"" + key + "\" pitchKeyTrack=\"0\" volume=\"" + volume + "\" />");
        }

        builder.AppendLine("    </group>");
        builder.Append("  </groups>");
        return builder.ToString();
    }

    /// <summary>The steady level of the blip that starts at a frame.</summary>
    /// <param name="samples">The rendered channel.</param>
    /// <param name="onset">The onset frame.</param>
    /// <returns>The level.</returns>
    public static double LevelAt(float[] samples, int onset) =>
        DecentSamplerRenderProbe.Rms(samples, onset, 900);

    /// <summary>Renders whole blocks and returns the left channel.</summary>
    /// <param name="blocks">How many blocks to render.</param>
    /// <returns>The left channel.</returns>
    public float[] Render(int blocks) => DecentSamplerRenderProbe.RenderBlocks(Synthesizer, blocks).Left;

    /// <summary>Renders the whole blocks covering a duration and returns the left channel.</summary>
    /// <param name="seconds">How long to render.</param>
    /// <returns>The left channel.</returns>
    public float[] RenderSeconds(double seconds) =>
        DecentSamplerRenderProbe.RenderSeconds(Synthesizer, seconds).Left;

    /// <summary>
    /// The frame of every rising edge out of silence: where each generated note started. The blip is a
    /// constant, and a voice's first block does not ramp, so an onset is exact to the frame.
    /// </summary>
    /// <param name="samples">The rendered channel.</param>
    /// <param name="threshold">The level a note is considered sounding above.</param>
    /// <returns>The onset frames, in order.</returns>
    public static IReadOnlyList<int> Onsets(float[] samples, float threshold = 0.02f)
    {
        var onsets = new List<int>();
        var sounding = false;

        for (var index = 0; index < samples.Length; index++)
        {
            var level = Math.Abs(samples[index]);

            if (!sounding && level > threshold)
            {
                onsets.Add(index);
                sounding = true;
            }
            else if (sounding && level <= threshold)
            {
                sounding = false;
            }
        }

        return onsets;
    }

    /// <summary>The frame each note stopped sounding at, for gate-length measurements.</summary>
    /// <param name="samples">The rendered channel.</param>
    /// <param name="threshold">The level a note is considered sounding above.</param>
    /// <returns>The release frames, in order.</returns>
    public static IReadOnlyList<int> Offsets(float[] samples, float threshold = 0.02f)
    {
        var offsets = new List<int>();
        var sounding = false;

        for (var index = 0; index < samples.Length; index++)
        {
            var level = Math.Abs(samples[index]);

            if (!sounding && level > threshold)
            {
                sounding = true;
            }
            else if (sounding && level <= threshold)
            {
                offsets.Add(index);
                sounding = false;
            }
        }

        return offsets;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Instrument.Dispose();
        Fixtures.Dispose();
    }
}
