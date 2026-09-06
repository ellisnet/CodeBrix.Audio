using System;

namespace CodeBrix.Audio.Synth.DecentSampler.Samples;

/// <summary>
/// Where a zone's audio comes from, whether it is decoded in memory or streamed from disk.
/// </summary>
/// <remarks>
/// <para>
/// The seam exists because the two are indistinguishable to a voice: it asks for a range of frames on
/// one channel and gets them. <see cref="InMemorySampleSource"/> is the only implementation today; the
/// streaming one arrives with the streaming phase and must produce identical output.
/// </para>
/// <para>
/// A source is immutable once created and safe to read from several voices at once. Nothing here
/// allocates, so a voice may call <see cref="Read"/> on the audio thread.
/// </para>
/// </remarks>
internal interface ISampleSource : IDisposable
{
    /// <summary>The number of channels: 1 for mono, 2 for stereo.</summary>
    int ChannelCount { get; }

    /// <summary>The sample rate the audio was recorded at.</summary>
    int SampleRate { get; }

    /// <summary>The number of sample frames per channel.</summary>
    long Frames { get; }

    /// <summary>The first frame of the file's own loop, when the file defines one.</summary>
    long? EmbeddedLoopStart { get; }

    /// <summary>The last frame (inclusive) of the file's own loop, when the file defines one.</summary>
    long? EmbeddedLoopEnd { get; }

    /// <summary>Whether the file carries usable loop points.</summary>
    bool HasEmbeddedLoop { get; }

    /// <summary>Whether the whole file is decoded in memory.</summary>
    bool IsInMemory { get; }

    /// <summary>How many bytes of decoded audio this source holds.</summary>
    long DecodedByteCount { get; }

    /// <summary>
    /// Copies frames from one channel into a buffer.
    /// </summary>
    /// <param name="channel">The channel index, 0 to <see cref="ChannelCount"/> minus one.</param>
    /// <param name="startFrame">The first frame to read.</param>
    /// <param name="destination">Where the frames go. Its length is how many are wanted.</param>
    /// <returns>
    /// How many frames were written, which is fewer than asked for at the end of the audio and 0 past
    /// it. Whatever is not written is left untouched.
    /// </returns>
    int Read(int channel, long startFrame, Span<float> destination);
}
