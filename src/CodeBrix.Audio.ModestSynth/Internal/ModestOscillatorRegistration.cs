using System;
using CodeBrix.Audio.ModestSynth.Oscillators;

namespace CodeBrix.Audio.ModestSynth.Internal;

/// <summary>
/// One entry in the list this package hands to the Decent Sampler engine when it registers: the
/// waveform name a preset writes, and the factory that builds the oscillator for it.
/// </summary>
internal sealed class ModestOscillatorRegistration
{
    /// <summary>Creates a registration under the waveform's own name.</summary>
    /// <param name="waveform">The waveform.</param>
    /// <param name="factory">The factory that builds an oscillator for it.</param>
    internal ModestOscillatorRegistration(ModestWaveform waveform, Func<IModestOscillator> factory)
        : this(waveform, ModestWaveforms.ToName(waveform), factory)
    {
    }

    /// <summary>Creates a registration under a given name, which is how a synonym is registered.</summary>
    /// <param name="waveform">The waveform.</param>
    /// <param name="name">The name a preset writes for it.</param>
    /// <param name="factory">The factory that builds an oscillator for it.</param>
    internal ModestOscillatorRegistration(ModestWaveform waveform, string name, Func<IModestOscillator> factory)
    {
        Waveform = waveform;
        WaveformName = name;
        Factory = factory;
    }

    /// <summary>The waveform this entry covers.</summary>
    internal ModestWaveform Waveform { get; }

    /// <summary>The waveform name a preset spells, for example <c>"pluck1"</c>.</summary>
    internal string WaveformName { get; }

    /// <summary>Builds a new oscillator carrying its own defaults.</summary>
    internal Func<IModestOscillator> Factory { get; }
}
