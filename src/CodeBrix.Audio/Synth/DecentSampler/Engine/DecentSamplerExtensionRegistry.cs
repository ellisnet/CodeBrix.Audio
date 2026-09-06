using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

/// <summary>
/// A set of oscillator and effect factories that the Decent Sampler engine can build voices and
/// chains from. <see cref="DecentSamplerExtensions"/> owns the process-wide instance; construct your
/// own when a test needs an isolated one.
/// </summary>
/// <remarks>
/// <para>
/// Every member is thread-safe. Registration is expected at start-up and lookup during loading, but
/// the two may overlap safely.
/// </para>
/// <para>
/// Names are matched case-insensitively and with underscores and hyphens folded away, the same way the
/// parser matches enumerated values, so <c>white_noise</c>, <c>whitenoise</c> and <c>WHITE-NOISE</c>
/// are one waveform.
/// </para>
/// </remarks>
public sealed class DecentSamplerExtensionRegistry
{
    private readonly object _gate = new();

    private readonly Dictionary<string, Func<OscillatorContext, IVoiceSource>> _oscillators =
        new(StringComparer.Ordinal);

    private readonly Dictionary<string, Func<EffectContext, IInstrumentEffect>> _effects =
        new(StringComparer.Ordinal);

    private readonly Dictionary<string, string> _oscillatorNames = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _effectNames = new(StringComparer.Ordinal);

    /// <summary>Creates an empty registry.</summary>
    public DecentSamplerExtensionRegistry()
    {
    }

    /// <summary>The waveform names registered here, as they were spelled at registration.</summary>
    public IReadOnlyList<string> RegisteredOscillators
    {
        get
        {
            lock (_gate)
            {
                return _oscillatorNames.Values.OrderBy(name => name, StringComparer.Ordinal).ToArray();
            }
        }
    }

    /// <summary>The effect type names registered here, as they were spelled at registration.</summary>
    public IReadOnlyList<string> RegisteredEffects
    {
        get
        {
            lock (_gate)
            {
                return _effectNames.Values.OrderBy(name => name, StringComparer.Ordinal).ToArray();
            }
        }
    }

    /// <summary>
    /// Registers a factory for an oscillator waveform, replacing any factory already registered under
    /// the same name.
    /// </summary>
    /// <param name="waveform">The waveform name, for example <c>fm6op</c>.</param>
    /// <param name="factory">Builds one voice source per voice.</param>
    /// <exception cref="ArgumentNullException"><paramref name="waveform"/> or <paramref name="factory"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="waveform"/> is blank.</exception>
    public void RegisterOscillator(string waveform, Func<OscillatorContext, IVoiceSource> factory)
    {
        var key = RequireKey(waveform, nameof(waveform));

        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        lock (_gate)
        {
            _oscillators[key] = factory;
            _oscillatorNames[key] = waveform;
        }
    }

    /// <summary>
    /// Registers a factory for an effect type, replacing any factory already registered under the same
    /// name.
    /// </summary>
    /// <param name="type">The effect type name, for example <c>bit_crusher</c>.</param>
    /// <param name="factory">Builds one effect instance per chain position.</param>
    /// <exception cref="ArgumentNullException"><paramref name="type"/> or <paramref name="factory"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="type"/> is blank.</exception>
    public void RegisterEffect(string type, Func<EffectContext, IInstrumentEffect> factory)
    {
        var key = RequireKey(type, nameof(type));

        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        lock (_gate)
        {
            _effects[key] = factory;
            _effectNames[key] = type;
        }
    }

    /// <summary>Whether a factory is registered for a waveform.</summary>
    /// <param name="waveform">The waveform name.</param>
    /// <returns><see langword="true"/> when a factory is registered.</returns>
    public bool IsOscillatorRegistered(string waveform)
    {
        var key = Key(waveform);
        if (key == null)
        {
            return false;
        }

        lock (_gate)
        {
            return _oscillators.ContainsKey(key);
        }
    }

    /// <summary>Whether a factory is registered for an effect type.</summary>
    /// <param name="type">The effect type name.</param>
    /// <returns><see langword="true"/> when a factory is registered.</returns>
    public bool IsEffectRegistered(string type)
    {
        var key = Key(type);
        if (key == null)
        {
            return false;
        }

        lock (_gate)
        {
            return _effects.ContainsKey(key);
        }
    }

    /// <summary>
    /// Looks up the factory for a waveform.
    /// </summary>
    /// <param name="waveform">The waveform name.</param>
    /// <param name="factory">The factory, or null when none is registered.</param>
    /// <returns><see langword="true"/> when a factory is registered.</returns>
    public bool TryGetOscillatorFactory(string waveform, out Func<OscillatorContext, IVoiceSource> factory)
    {
        factory = null;

        var key = Key(waveform);
        if (key == null)
        {
            return false;
        }

        lock (_gate)
        {
            return _oscillators.TryGetValue(key, out factory);
        }
    }

    /// <summary>
    /// Looks up the factory for an effect type.
    /// </summary>
    /// <param name="type">The effect type name.</param>
    /// <param name="factory">The factory, or null when none is registered.</param>
    /// <returns><see langword="true"/> when a factory is registered.</returns>
    public bool TryGetEffectFactory(string type, out Func<EffectContext, IInstrumentEffect> factory)
    {
        factory = null;

        var key = Key(type);
        if (key == null)
        {
            return false;
        }

        lock (_gate)
        {
            return _effects.TryGetValue(key, out factory);
        }
    }

    /// <summary>Removes every registration. Meant for tests that build their own registry.</summary>
    public void Clear()
    {
        lock (_gate)
        {
            _oscillators.Clear();
            _effects.Clear();
            _oscillatorNames.Clear();
            _effectNames.Clear();
        }
    }

    // Folds a name the way the parser folds enumerated values: case and the separators libraries write
    // interchangeably ("white_noise", "whiteNoise", "white-noise") all reach one key.
    internal static string Key(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            return null;
        }

        Span<char> buffer = stackalloc char[name.Length];
        var length = 0;

        foreach (var character in name)
        {
            if (character == '_' || character == '-' || character == ' ')
            {
                continue;
            }

            buffer[length++] = char.ToLowerInvariant(character);
        }

        return length == 0 ? null : new string(buffer[..length]);
    }

    private static string RequireKey(string name, string parameterName)
    {
        if (name == null)
        {
            throw new ArgumentNullException(parameterName);
        }

        var key = Key(name);
        if (key == null)
        {
            throw new ArgumentException("The name cannot be blank.", parameterName);
        }

        return key;
    }
}
