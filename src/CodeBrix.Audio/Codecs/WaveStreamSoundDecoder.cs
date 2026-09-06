using System;
using CodeBrix.Audio.Wave;
using CodeBrix.Audio.Wave.SampleProviders;

namespace CodeBrix.Audio.Codecs;

/// <summary>
/// Decodes any <see cref="WaveStream"/> - and so any format the managed readers open - into the
/// channel count and sample rate a caller asks for, with no audio device anywhere in sight.
/// </summary>
/// <remarks>
/// <para>
/// The engine's own decode path resamples in native code, which is better, but it needs a running
/// <c>AudioEngine</c>, and an engine is a device context. Offline work - rendering a mix to a file,
/// measuring loudness, a test on a machine with no sound card - must not open one. This is the
/// device-free equivalent: the managed reader produces the file's own audio, and
/// <see cref="ManagedSoundDecoder"/> does the channel mapping and the linear rate conversion on top
/// of it.
/// </para>
/// <para>
/// Rate conversion is therefore linear interpolation, the same quality the managed codec fallbacks
/// give. It is used for every source in <c>MultiTrackPlayer</c>, online and offline alike, so that
/// what a mix sounds like through the speakers and what it renders to on disk are the same audio.
/// </para>
/// </remarks>
internal sealed class WaveStreamSoundDecoder : ManagedSoundDecoder
{
    private readonly WaveStream reader;
    private readonly ISampleProvider samples;
    private readonly bool ownsReader;
    private readonly int sourceBlockAlign;

    /// <summary>Creates a decoder over an open reader.</summary>
    /// <param name="reader">The reader to decode. Must be seekable and hold PCM or IEEE-float audio.</param>
    /// <param name="ownsReader">Whether disposing this decoder disposes the reader.</param>
    /// <param name="channels">The channel count to produce, or 0 to keep the file's own.</param>
    /// <param name="sampleRate">The sample rate to produce, or 0 to keep the file's own.</param>
    /// <exception cref="ArgumentNullException"><paramref name="reader"/> is null.</exception>
    /// <exception cref="ArgumentException">The reader's audio is neither PCM nor IEEE float.</exception>
    internal WaveStreamSoundDecoder(WaveStream reader, bool ownsReader, int channels, int sampleRate)
        : base(channels, sampleRate)
    {
        if (reader == null)
        {
            throw new ArgumentNullException(nameof(reader));
        }

        this.reader = reader;
        this.ownsReader = ownsReader;

        var format = SampleProviderConverters.ResolveExtensible(reader.WaveFormat);
        samples = SampleProviderConverters.ConvertWaveProviderIntoSampleProvider(reader);

        sourceBlockAlign = format.Channels * (format.BitsPerSample / 8);
        var frames = sourceBlockAlign > 0 ? reader.Length / sourceBlockAlign : 0L;

        Initialize(format.Channels, format.SampleRate, frames);
    }

    /// <inheritdoc/>
    protected override int ReadSourceSamples(Span<float> destination)
    {
        // The sample providers read straight from the reader each call and keep no state across
        // calls, so moving reader.Position under them (see SeekSource) is safe.
        return samples.Read(destination);
    }

    /// <inheritdoc/>
    protected override bool SeekSource(long frameIndex)
    {
        if (sourceBlockAlign <= 0)
        {
            return false;
        }

        try
        {
            var position = frameIndex * sourceBlockAlign;
            if (position < 0)
            {
                position = 0;
            }
            if (position > reader.Length)
            {
                position = reader.Length - reader.Length % sourceBlockAlign;
            }

            reader.Position = position;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <inheritdoc/>
    protected override void DisposeCore()
    {
        if (ownsReader)
        {
            reader.Dispose();
        }
    }
}
