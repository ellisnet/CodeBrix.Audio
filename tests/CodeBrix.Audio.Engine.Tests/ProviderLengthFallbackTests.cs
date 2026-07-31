using System;
using System.IO;
using CodeBrix.Audio.Engine.Backends.MiniAudio;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Providers;
using CodeBrix.Audio.Engine.Structs;
using SilverAssertions;
using SilverAssertions.Numeric;
using Xunit;

namespace CodeBrix.Audio.Engine.Tests;

/// <summary>
/// Device-less tests for the length-unknown fallback in StreamDataProvider and AssetDataProvider.
/// </summary>
/// <remarks>
/// <para>
/// When a decoder cannot report its own length, both providers fall back to computing one from the
/// metadata. That computation has to be expressed in the DECODER's units, because the decoder
/// converts the file to whatever the output device asked for: a mono file feeding a stereo device
/// yields twice as many samples as the file's own channel count implies, and a 22.05 kHz file
/// feeding a 48 kHz device more than twice again.
/// </para>
/// <para>
/// AssetDataProvider is the one that really bites. Its fallback sizes the buffer that the whole
/// asset is decoded into, in a single Decode call, and the code only ever resizes that buffer
/// DOWN - so a value taken from the file's layout does not mis-describe the clip, it CUTS IT OFF.
/// A mono sound effect on a stereo device came back half as long as it should be.
/// </para>
/// </remarks>
public class ProviderLengthFallbackTests
{
    private const int DeviceSampleRate = 48000;
    private const int DeviceChannels = 2;

    private static AudioFormat DeviceFormat => new AudioFormat
    {
        Format = SampleFormat.F32,
        Channels = DeviceChannels,
        Layout = AudioFormat.GetLayoutFromChannels(DeviceChannels),
        SampleRate = DeviceSampleRate
    };

    /// <summary>An engine whose "wav" decoder reports no length of its own.</summary>
    private static MiniAudioEngine EngineWithLengthlessWav()
    {
        var engine = new MiniAudioEngine();
        engine.RegisterCodecFactory(new LengthlessCodecFactory(new MiniAudioCodecFactory(), "wav"));

        return engine;
    }

    [Theory]
    [InlineData(1, 48000)]
    [InlineData(2, 48000)]
    [InlineData(1, 22050)]
    public void StreamDataProvider_falls_back_in_the_decoders_units(int fileChannels, int fileRate)
    {
        //Arrange
        // One second of audio in every case, whatever layout the file happens to be in.
        using var engine = EngineWithLengthlessWav();
        using var stream = new MemoryStream(
            TestAudio.BuildSineWavPcm16(fileRate, fileChannels, fileRate));

        //Act
        using var provider = new StreamDataProvider(engine, DeviceFormat, stream);

        //Assert
        var seconds = (double)provider.Length / DeviceChannels / DeviceSampleRate;
        seconds.Should().BeApproximately(1.0, 0.02);
    }

    [Theory]
    [InlineData(1, 48000)]
    [InlineData(2, 48000)]
    [InlineData(1, 22050)]
    public void AssetDataProvider_decodes_the_whole_asset_not_a_fraction_of_it(
        int fileChannels, int fileRate)
    {
        //Arrange
        // The truncation case. Sized from the file's layout, a mono asset came back at half
        // length and a 22.05 kHz one at under a quarter - silently, because the buffer is only
        // ever resized down.
        using var engine = EngineWithLengthlessWav();
        using var stream = new MemoryStream(
            TestAudio.BuildSineWavPcm16(fileRate, fileChannels, fileRate));

        //Act
        using var provider = new AssetDataProvider(engine, DeviceFormat, stream);

        //Assert
        var seconds = (double)provider.Length / DeviceChannels / DeviceSampleRate;
        seconds.Should().BeApproximately(1.0, 0.02);
    }

    [Fact]
    public void A_mono_asset_is_the_same_length_as_the_same_audio_in_stereo()
    {
        //Arrange
        using var engine = EngineWithLengthlessWav();
        using var mono = new MemoryStream(TestAudio.BuildSineWavPcm16(DeviceSampleRate, 1, DeviceSampleRate));
        using var stereo = new MemoryStream(TestAudio.BuildSineWavPcm16(DeviceSampleRate, 2, DeviceSampleRate));

        //Act
        using var monoProvider = new AssetDataProvider(engine, DeviceFormat, mono);
        using var stereoProvider = new AssetDataProvider(engine, DeviceFormat, stereo);

        //Assert
        monoProvider.Length.Should().Be(stereoProvider.Length);
    }
}
