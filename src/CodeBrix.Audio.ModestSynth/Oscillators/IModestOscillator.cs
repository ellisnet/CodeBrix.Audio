using System;

namespace CodeBrix.Audio.ModestSynth.Oscillators;

/// <summary>
/// One monophonic sound generator: set its sample rate and its pitch, reset it at note-on, then
/// ask it for blocks of samples.
/// </summary>
/// <remarks>
/// <para>
/// An oscillator is a single voice's worth of state, so give every voice its own. Nothing here is
/// thread-safe, and nothing needs to be: an oscillator belongs to the thread rendering it.
/// </para>
/// <para>
/// <see cref="Render" /> is audio-thread safe in the sense that matters - it allocates nothing,
/// locks nothing and touches no file. Every buffer an implementation needs is allocated by
/// <see cref="SetSampleRate" />, so call that (and <see cref="SetFrequency" />) before the first
/// render rather than between blocks.
/// </para>
/// <para>
/// Output is mono, nominally in [-1, 1]. Band-limited shapes overshoot that range slightly at the
/// corners they round off, which is normal and is why a voice applies its own gain before mixing.
/// </para>
/// </remarks>
public interface IModestOscillator
{
    /// <summary>
    /// The waveform name this oscillator implements, spelled as a preset spells it - for example
    /// <c>"pluck1"</c>.
    /// </summary>
    string Waveform { get; }

    /// <summary>The sample rate blocks are rendered at, in Hz.</summary>
    int SampleRate { get; }

    /// <summary>The pitch being generated, in Hz.</summary>
    double Frequency { get; }

    /// <summary>
    /// Sets the sample rate blocks will be rendered at.
    /// </summary>
    /// <param name="sampleRate">Samples per second; must be greater than zero.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate" /> is not positive.</exception>
    /// <remarks>
    /// This is where an implementation allocates, so call it before rendering starts. Changing the
    /// rate does not reset the oscillator.
    /// </remarks>
    void SetSampleRate(int sampleRate);

    /// <summary>
    /// Sets the pitch to generate.
    /// </summary>
    /// <param name="frequencyHz">The frequency in Hz.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="frequencyHz" /> is negative, or not a finite number, or outside the range a
    /// particular implementation can produce.
    /// </exception>
    /// <remarks>
    /// Safe to call between blocks - that is how glide and vibrato work - and it never restarts
    /// the waveform.
    /// </remarks>
    void SetFrequency(double frequencyHz);

    /// <summary>
    /// Restarts the oscillator, as at note-on.
    /// </summary>
    /// <param name="phase">
    /// The starting phase, in cycles: 0 is the start of the waveform and 1 is a whole cycle later,
    /// so only the fractional part matters. Pass a random value for the <c>randomPhase</c>
    /// behaviour that keeps layered voices from cancelling each other.
    /// </param>
    void Reset(double phase);

    /// <summary>
    /// Renders the next block of samples, overwriting whatever the buffer held.
    /// </summary>
    /// <param name="buffer">The mono block to fill. An empty span is a no-op.</param>
    /// <remarks>Allocates nothing and takes no lock; safe to call from an audio callback.</remarks>
    void Render(Span<float> buffer);
}
