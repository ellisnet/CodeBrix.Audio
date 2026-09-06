using System;

namespace CodeBrix.Audio.Playback.Suno.Internal;

/// <summary>
/// What the header of a WAV file says about it. Enough to report a stem's length and format without
/// decoding a byte of audio.
/// </summary>
internal readonly struct SunoWavInfo
{
    internal SunoWavInfo(int sampleRate, int channels, int bitsPerSample, long dataBytes)
    {
        SampleRate = sampleRate;
        Channels = channels;
        BitsPerSample = bitsPerSample;
        DataBytes = dataBytes;
    }

    internal int SampleRate { get; }

    internal int Channels { get; }

    internal int BitsPerSample { get; }

    internal long DataBytes { get; }

    internal bool IsValid => SampleRate > 0 && Channels > 0 && BitsPerSample > 0 && DataBytes > 0;

    internal TimeSpan Duration
    {
        get
        {
            if (!IsValid)
            {
                return TimeSpan.Zero;
            }

            var bytesPerSecond = (long)SampleRate * Channels * (BitsPerSample / 8);
            return bytesPerSecond > 0
                ? TimeSpan.FromTicks((long)(TimeSpan.TicksPerSecond * (DataBytes / (double)bytesPerSecond)))
                : TimeSpan.Zero;
        }
    }
}
