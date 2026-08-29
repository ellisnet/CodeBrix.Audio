using System;
using System.Collections.Generic;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Engine.Structs;

namespace CodeBrix.Audio.Engine.Tests;

/// <summary>
/// A packet codec factory that decodes nothing, for exercising the engine's registration, priority
/// and fall-through behaviour without a real codec in the way.
/// </summary>
internal sealed class FakePacketCodecFactory : IPacketCodecFactory
{
    /// <summary>Creates a factory with the given identity, priority and codecs.</summary>
    /// <param name="factoryId">The factory's unique id.</param>
    /// <param name="priority">The priority it registers at.</param>
    /// <param name="codecIds">The codec identifiers it claims to serve.</param>
    public FakePacketCodecFactory(string factoryId, int priority, params string[] codecIds)
    {
        FactoryId = factoryId;
        Priority = priority;
        SupportedCodecIds = codecIds;
    }

    public string FactoryId { get; }

    public IReadOnlyCollection<string> SupportedCodecIds { get; }

    public int Priority { get; }

    /// <summary>When true the factory declines every request, as one that cannot serve it should.</summary>
    public bool Declines { get; set; }

    /// <summary>When true the factory throws, so the engine's fall-through can be observed.</summary>
    public bool Throws { get; set; }

    /// <summary>How many times the engine asked this factory for a decoder.</summary>
    public int CreateCallCount { get; private set; }

    /// <summary>The codec id of the last request the engine made.</summary>
    public string LastCodecId { get; private set; }

    /// <summary>The codec-private data of the last request the engine made.</summary>
    public ReadOnlyMemory<byte> LastCodecPrivate { get; private set; }

    public IPacketSoundDecoder CreateDecoder(string codecId, ReadOnlyMemory<byte> codecPrivate, AudioFormat? hint)
    {
        CreateCallCount++;
        LastCodecId = codecId;
        LastCodecPrivate = codecPrivate;

        if (Throws) throw new InvalidOperationException("This factory always fails.");
        if (Declines) return null;

        return new FakePacketSoundDecoder(FactoryId);
    }
}

/// <summary>
/// A packet decoder that produces silence, so a test can tell WHICH factory served a request.
/// </summary>
internal sealed class FakePacketSoundDecoder : IPacketSoundDecoder
{
    /// <summary>Creates a decoder tagged with the id of the factory that made it.</summary>
    /// <param name="factoryId">The making factory's id.</param>
    public FakePacketSoundDecoder(string factoryId) => FactoryId = factoryId;

    /// <summary>The id of the factory that made this decoder.</summary>
    public string FactoryId { get; }

    /// <summary>Whether this decoder has been disposed.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>How many times <see cref="Reset"/> was called.</summary>
    public int ResetCount { get; private set; }

    public int Channels => 2;

    public int SampleRate => 48000;

    public SampleFormat SampleFormat => SampleFormat.F32;

    public int MaxSamplesPerPacket => 960 * 2;

    public int PreSkipSamples => 0;

    public int DecodePacket(ReadOnlySpan<byte> packet, Span<float> output)
    {
        var samples = Math.Min(MaxSamplesPerPacket, output.Length);
        output.Slice(0, samples).Clear();
        return samples;
    }

    public void Reset() => ResetCount++;

    public void Dispose() => IsDisposed = true;
}
