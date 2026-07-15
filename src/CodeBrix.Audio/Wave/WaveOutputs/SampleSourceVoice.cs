using System;
using CodeBrix.Audio.Engine.Abstracts;
using CodeBrix.Audio.Engine.Structs;

namespace CodeBrix.Audio.Wave;

/// <summary>
/// A single mixer voice that pulls 32-bit float samples from an <see cref="ISampleProvider"/> and
/// writes them into the shared playback device's master mixer. One <see cref="WaveOutEvent"/> owns
/// one of these while it is playing; when the source ends the voice keeps producing silence until
/// the shared output's sweep removes it.
/// </summary>
/// <remarks>
/// The <see cref="GenerateAudio"/> override runs on miniaudio's native audio thread, so it never
/// allocates and never takes a lock: it reads straight into the supplied buffer and flips two
/// <c>volatile</c> flags. The provider handed in is already at the device's channel count and sample
/// rate (the owning <see cref="WaveOutEvent"/> guarantees that), so no conversion happens here.
/// </remarks>
internal sealed class SampleSourceVoice : SoundComponent
{
    private readonly ISampleProvider _source;
    private volatile bool _playing;
    private volatile bool _ended;

    /// <summary>Creates a voice over the given source, already matched to <paramref name="format"/>.</summary>
    /// <param name="engine">The shared engine the voice belongs to.</param>
    /// <param name="format">The device format (channels + sample rate) the source is already in.</param>
    /// <param name="source">The float sample source, at the device's channel count and sample rate.</param>
    public SampleSourceVoice(AudioEngine engine, AudioFormat format, ISampleProvider source)
        : base(engine, format)
    {
        _source = source;
    }

    /// <summary>Whether the source has signalled end-of-stream (a read returned zero samples).</summary>
    public bool HasEnded => _ended;

    /// <summary>Starts (or resumes) pulling from the source on the next audio callback.</summary>
    public void ResumePlaying() => _playing = true;

    /// <summary>Stops pulling from the source; the voice produces silence but stays in the mixer.</summary>
    public void PausePlaying() => _playing = false;

    /// <summary>
    /// Fills <paramref name="buffer"/> with interleaved float samples pulled from the source. Runs on
    /// the native audio thread: no allocation, no locking.
    /// </summary>
    /// <param name="buffer">Interleaved output buffer (length is frameCount * <paramref name="channels"/>).</param>
    /// <param name="channels">The device channel count (authoritative for buffer layout).</param>
    protected override void GenerateAudio(Span<float> buffer, int channels)
    {
        if (!_playing)
        {
            buffer.Clear();
            return;
        }

        int read = _source.Read(buffer);
        if (read < buffer.Length)
        {
            // Zero any tail the source did not fill (partial final buffer, or silence after the end).
            buffer.Slice(read).Clear();
        }
        if (read == 0)
        {
            // End of stream (an ISampleProvider returns 0 only when exhausted). Latch it for the
            // shared output's sweep to notice, and stop pulling so we do not spin the source.
            _ended = true;
            _playing = false;
        }
    }
}
