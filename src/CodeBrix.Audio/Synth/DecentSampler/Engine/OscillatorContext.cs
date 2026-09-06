using System;

namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

/// <summary>
/// Everything an oscillator factory is told when the engine builds a voice source for a zone.
/// </summary>
/// <remarks>
/// A factory is called once per voice, on the thread that started the note, which is the audio thread
/// during playback. Read what you need from <see cref="Zone"/> - every oscillator attribute of the
/// developer guide is already resolved there - and return a source that allocates nothing afterwards.
/// </remarks>
public sealed class OscillatorContext
{
    /// <summary>
    /// Creates a context.
    /// </summary>
    /// <param name="instrument">The instrument being played.</param>
    /// <param name="zone">The oscillator zone the source is for.</param>
    /// <param name="sampleRate">The synthesis sample rate in Hz.</param>
    /// <param name="waveform">The waveform name as the preset spelled it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="zone"/> is null.</exception>
    public OscillatorContext(
        DecentSamplerInstrument instrument, DecentSamplerZone zone, int sampleRate, string waveform)
    {
        Instrument = instrument;
        Zone = zone ?? throw new ArgumentNullException(nameof(zone));
        SampleRate = sampleRate;
        Waveform = waveform;
    }

    /// <summary>The instrument being played. Null when a source is built outside an instrument.</summary>
    public DecentSamplerInstrument Instrument { get; }

    /// <summary>The zone, with every oscillator attribute already resolved to its effective value.</summary>
    public DecentSamplerZone Zone { get; }

    /// <summary>The synthesis sample rate in Hz.</summary>
    public int SampleRate { get; }

    /// <summary>The waveform name as the preset spelled it, which is what the factory was keyed by.</summary>
    public string Waveform { get; }

    /// <summary>The group the zone belongs to.</summary>
    public DecentSamplerGroup Group => Zone.Group;
}
