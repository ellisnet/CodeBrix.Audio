using System;
using System.Collections.Generic;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Playback;

namespace CodeBrix.Audio.Tests.Utils;

/// <summary>
/// A packet source driven by a test: hands out the packets it was given, one at a time, and says
/// whether it has run dry for good or only for the moment.
/// </summary>
internal sealed class FakeAudioPacketSource : IAudioPacketSource
{
    private readonly Queue<byte[]> packets = new Queue<byte[]>();

    /// <summary>Creates a source holding the given packets.</summary>
    /// <param name="packets">The packets to hand out, in order.</param>
    public FakeAudioPacketSource(IEnumerable<byte[]> packets)
    {
        foreach (var packet in packets)
        {
            this.packets.Enqueue(packet);
        }
    }

    /// <summary>
    /// Whether running dry means the end of the audio (true) or an underrun the reader will catch up
    /// from (false). Defaults to false, the underrun case.
    /// </summary>
    public bool EndWhenDrained { get; set; }

    /// <summary>How many packets have been handed over.</summary>
    public int DeliveredCount { get; private set; }

    /// <summary>How many packets are still queued.</summary>
    public int RemainingCount => packets.Count;

    /// <inheritdoc />
    public bool EndOfStream => EndWhenDrained && packets.Count == 0;

    /// <inheritdoc />
    public bool TryReadPacket(out AudioPacket packet)
    {
        if (packets.Count == 0)
        {
            packet = default;
            return false;
        }

        packet = new AudioPacket(packets.Dequeue());
        DeliveredCount++;
        return true;
    }

    /// <summary>Adds more packets, as a reader catching up after an underrun would.</summary>
    /// <param name="more">The packets to add.</param>
    public void Add(IEnumerable<byte[]> more)
    {
        foreach (var packet in more)
        {
            packets.Enqueue(packet);
        }
    }

    /// <summary>Empties the queue and refills it, as repositioning the reader would.</summary>
    /// <param name="replacement">The packets the source will deliver from now on.</param>
    public void Reposition(IEnumerable<byte[]> replacement)
    {
        packets.Clear();
        Add(replacement);
    }
}

/// <summary>
/// A packet decoder that turns each packet into a fixed number of frames of counting samples, so a
/// test can tell exactly which audio came out and in what order.
/// </summary>
/// <remarks>
/// Sample values are the running frame index, which makes a dropped, duplicated or discarded frame
/// obvious in an assertion rather than merely wrong-sounding.
/// </remarks>
internal sealed class FakePacketSoundDecoder : IPacketSoundDecoder
{
    private int nextFrameValue;
    private bool firstPacketAfterReset = true;

    /// <summary>Creates a decoder with the given output shape.</summary>
    /// <param name="channels">Channel count of the decoded audio.</param>
    /// <param name="sampleRate">Sample rate of the decoded audio.</param>
    /// <param name="framesPerPacket">How many frames each packet decodes to.</param>
    public FakePacketSoundDecoder(int channels = 2, int sampleRate = 48000, int framesPerPacket = 100)
    {
        Channels = channels;
        SampleRate = sampleRate;
        FramesPerPacket = framesPerPacket;
    }

    /// <inheritdoc />
    public int Channels { get; }

    /// <inheritdoc />
    public int SampleRate { get; }

    /// <summary>How many frames each packet decodes to.</summary>
    public int FramesPerPacket { get; }

    /// <inheritdoc />
    public SampleFormat SampleFormat => SampleFormat.F32;

    /// <inheritdoc />
    public int MaxSamplesPerPacket => FramesPerPacket * Channels;

    /// <inheritdoc />
    public int PreSkipSamples { get; set; }

    /// <summary>
    /// When true the decoder yields nothing for the first packet after a reset, the way a
    /// lapped-transform codec does.
    /// </summary>
    public bool SilentFirstPacket { get; set; }

    /// <summary>How many times <see cref="Reset"/> has been called.</summary>
    public int ResetCount { get; private set; }

    /// <summary>How many packets have been decoded.</summary>
    public int DecodeCount { get; private set; }

    /// <summary>Whether this decoder has been disposed.</summary>
    public bool IsDisposed { get; private set; }

    /// <summary>The value the next decoded frame will carry.</summary>
    public int NextFrameValue
    {
        get => nextFrameValue;
        set => nextFrameValue = value;
    }

    /// <inheritdoc />
    public int DecodePacket(ReadOnlySpan<byte> packet, Span<float> output)
    {
        DecodeCount++;

        if (SilentFirstPacket && firstPacketAfterReset)
        {
            firstPacketAfterReset = false;
            return 0;
        }
        firstPacketAfterReset = false;

        var samples = FramesPerPacket * Channels;
        if (samples > output.Length)
        {
            throw new ArgumentException(
                $"The output buffer holds {output.Length} samples; size it to MaxSamplesPerPacket.",
                nameof(output));
        }

        for (var frame = 0; frame < FramesPerPacket; frame++)
        {
            for (var channel = 0; channel < Channels; channel++)
            {
                output[(frame * Channels) + channel] = nextFrameValue;
            }
            nextFrameValue++;
        }

        return samples;
    }

    /// <inheritdoc />
    public void Reset()
    {
        ResetCount++;
        firstPacketAfterReset = true;
    }

    /// <inheritdoc />
    public void Dispose() => IsDisposed = true;
}
