using System;
using CodeBrix.Audio.Dmo;

namespace CodeBrix.Audio.Wave.SampleProviders; //was previously: NAudio.Wave.SampleProviders;

/// <summary>
/// Utility class for converting to SampleProvider
/// </summary>
static class SampleProviderConverters
{
    /// <summary>
    /// Helper function to go from IWaveProvider to a SampleProvider
    /// Must already be PCM or IEEE float
    /// </summary>
    /// <param name="waveProvider">The WaveProvider to convert</param>
    /// <returns>A sample provider</returns>
    public static ISampleProvider ConvertWaveProviderIntoSampleProvider(IWaveProvider waveProvider)
    {
        ISampleProvider sampleProvider;

        // WAVE_FORMAT_EXTENSIBLE is a wrapper, not a different way of storing samples: a file
        // written that way holds ordinary PCM or IEEE float, and its sub-format says which. It is
        // also how most 24-bit and 32-bit WAV files in the wild are written, so resolving it here
        // is the difference between supporting those depths and rejecting them.
        var format = ResolveExtensible(waveProvider.WaveFormat);

        if (format.Encoding == WaveFormatEncoding.Pcm)
        {
            // go to float
            if (format.BitsPerSample == 8)
            {
                sampleProvider = new Pcm8BitToSampleProvider(waveProvider);
            }
            else if (format.BitsPerSample == 16)
            {
                sampleProvider = new Pcm16BitToSampleProvider(waveProvider);
            }
            else if (format.BitsPerSample == 24)
            {
                sampleProvider = new Pcm24BitToSampleProvider(waveProvider);
            }
            else if (format.BitsPerSample == 32)
            {
                sampleProvider = new Pcm32BitToSampleProvider(waveProvider);
            }
            else
            {
                throw new InvalidOperationException("Unsupported bit depth");
            }
        }
        else if (format.Encoding == WaveFormatEncoding.IeeeFloat)
        {
            if (format.BitsPerSample == 64)
                sampleProvider = new WaveToSampleProvider64(waveProvider);
            else
                sampleProvider = new WaveToSampleProvider(waveProvider);
        }
        else
        {
            throw new ArgumentException("Unsupported source encoding");
        }
        return sampleProvider;
    }

    /// <summary>
    /// Unwraps a WAVE_FORMAT_EXTENSIBLE header to the plain format it describes.
    /// </summary>
    /// <param name="format">The format to resolve.</param>
    /// <returns>
    /// The equivalent plain PCM or IEEE-float format when the input is an extensible wrapper
    /// around one; otherwise the format unchanged.
    /// </returns>
    /// <remarks>
    /// The sample data is byte-for-byte identical either way - only the header differs - so this
    /// changes nothing about how the samples are read.
    /// </remarks>
    public static WaveFormat ResolveExtensible(WaveFormat format)
    {
        if (format == null) return null;
        if (format is WaveFormatExtensible extensible) return extensible.ToStandardWaveFormat();
        if (format.Encoding != WaveFormatEncoding.Extensible) return format;

        // A WAV file's format chunk is parsed into WaveFormatExtraData, not WaveFormatExtensible,
        // so an extensible file arrives here as a plain WaveFormat with its sub-format sitting in
        // the extra bytes: validBitsPerSample (2), channel mask (4), then the sub-format GUID (16).
        if (format is not WaveFormatExtraData extra || extra.ExtraData == null || extra.ExtraData.Length < 22)
        {
            return format;
        }

        var subFormat = new Guid(new ReadOnlySpan<byte>(extra.ExtraData, 6, 16));

        if (subFormat == AudioMediaSubtypes.MEDIASUBTYPE_IEEE_FLOAT)
        {
            return WaveFormat.CreateIeeeFloatWaveFormat(format.SampleRate, format.Channels);
        }

        if (subFormat == AudioMediaSubtypes.MEDIASUBTYPE_PCM)
        {
            return new WaveFormat(format.SampleRate, format.BitsPerSample, format.Channels);
        }

        return format;
    }
}
