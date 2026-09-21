using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Tests.Synth;

/// <summary>
/// A synthesizer that writes down every message it is handed and renders a flat level, so that a
/// mix can be checked with arithmetic rather than with ears.
/// </summary>
/// <remarks>
/// The level is DC - the same value in every frame of both channels - which makes a gain visible
/// in a single sample. The block size is settable because the routing synthesizer has to mix
/// children whose blocks do not line up with its own.
/// </remarks>
internal sealed class RecordingSynthesizer : IMidiSynthesizer
{
    private readonly List<RecordedMessage> messages = new List<RecordedMessage>();

    /// <summary>Creates a synthesizer that renders a constant level.</summary>
    /// <param name="level">The value written into every frame of both channels.</param>
    /// <param name="sampleRate">The rate it claims to render at.</param>
    /// <param name="blockSize">The block size it claims to render in.</param>
    public RecordingSynthesizer(float level = 0.25F, int sampleRate = 44100, int blockSize = 64)
    {
        Level = level;
        SampleRate = sampleRate;
        BlockSize = blockSize;
        MasterVolume = 1.0F;
    }

    /// <summary>The value rendered into every frame.</summary>
    public float Level { get; set; }

    /// <inheritdoc />
    public int SampleRate { get; }

    /// <inheritdoc />
    public int BlockSize { get; }

    /// <inheritdoc />
    public int ActiveVoiceCount { get; set; }

    /// <inheritdoc />
    public float MasterVolume { get; set; }

    /// <summary>Every message this synthesizer was handed, in order.</summary>
    public IReadOnlyList<RecordedMessage> Messages => messages;

    /// <summary>How many times <see cref="NoteOffAll"/> was called.</summary>
    public int NoteOffAllCount { get; private set; }

    /// <summary>How many times <see cref="Reset"/> was called.</summary>
    public int ResetCount { get; private set; }

    /// <summary>How many frames this synthesizer has been asked to render.</summary>
    public int FramesRendered { get; private set; }

    /// <inheritdoc />
    public void ProcessMidiMessage(int channel, int command, int data1, int data2) =>
        messages.Add(new RecordedMessage(channel, command, data1, data2));

    /// <inheritdoc />
    public void NoteOffAll(bool immediate) => NoteOffAllCount++;

    /// <inheritdoc />
    public void Reset() => ResetCount++;

    /// <inheritdoc />
    public void Render(Span<float> left, Span<float> right)
    {
        left.Fill(Level);
        right.Fill(Level);
        FramesRendered += left.Length;
    }
}

/// <summary>One MIDI message a <see cref="RecordingSynthesizer"/> was handed.</summary>
/// <param name="Channel">The wire channel, 0 to 15.</param>
/// <param name="Command">The status command.</param>
/// <param name="Data1">The first data byte.</param>
/// <param name="Data2">The second data byte.</param>
internal readonly record struct RecordedMessage(int Channel, int Command, int Data1, int Data2);
