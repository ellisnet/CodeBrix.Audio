using System;
using System.IO;
using CodeBrix.Audio.Engine.Backends.MiniAudio;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Engine.Providers;
using CodeBrix.Audio.Engine.Structs;
using SilverAssertions;
using SilverAssertions.Numeric;
using Xunit;

namespace CodeBrix.Audio.Engine.Tests;

/// <summary>
/// Device-less tests for <see cref="ChunkedDataProvider" />, the provider AudioFilePlayer streams
/// through.
/// </summary>
/// <remarks>
/// <para>
/// The provider's Length is what a media transport's duration is derived from:
/// SoundPlayerBase.Duration is <c>Length / Format.Channels / Format.SampleRate</c>, using the
/// OUTPUT device's format. So Length has to be expressed in the decoder's units too.
/// </para>
/// <para>
/// It was not. Length multiplied by the FILE's channel count while the division used the DEVICE's,
/// so every mono file reported exactly half its true duration on a stereo device - a two-minute
/// podcast showed as one minute. Stereo files hid it, because there the two counts agree, which is
/// why the mono cases below matter more than they look.
/// </para>
/// </remarks>
public class ChunkedDataProviderTests
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

    /// <summary>Duration the way SoundPlayerBase derives it, from the output device's format.</summary>
    private static double DurationOf(ISoundDataProvider provider) =>
        (double)provider.Length / DeviceChannels / DeviceSampleRate;

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void Length_is_in_the_decoders_units_not_the_files(int fileChannels)
    {
        //Arrange
        // One second of audio, played on a stereo device. The decoder converts to the device's
        // layout, so a mono file yields two channels of output - and Length has to say so.
        using var engine = new MiniAudioEngine();
        using var stream = new MemoryStream(
            TestAudio.BuildSineWavPcm16(DeviceSampleRate, fileChannels, DeviceSampleRate));

        //Act
        using var provider = new ChunkedDataProvider(engine, DeviceFormat, stream);

        //Assert
        provider.Length.Should().Be(DeviceSampleRate * DeviceChannels);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void A_transport_reads_the_true_duration_whatever_the_file_layout(int fileChannels)
    {
        //Arrange
        // The symptom the bug actually produced, asserted the way a transport sees it.
        using var engine = new MiniAudioEngine();
        using var stream = new MemoryStream(
            TestAudio.BuildSineWavPcm16(DeviceSampleRate, fileChannels, DeviceSampleRate));

        //Act
        using var provider = new ChunkedDataProvider(engine, DeviceFormat, stream);

        //Assert
        DurationOf(provider).Should().BeApproximately(1.0, 0.01);
    }

    [Fact]
    public void A_mono_file_does_not_report_half_its_length()
    {
        //Arrange
        // Named for the regression it guards: mono on a stereo device used to come back at
        // exactly 0.5x, which looks plausible enough to ship unnoticed.
        using var engine = new MiniAudioEngine();
        using var mono = new MemoryStream(
            TestAudio.BuildSineWavPcm16(DeviceSampleRate, 1, DeviceSampleRate * 2));
        using var stereo = new MemoryStream(
            TestAudio.BuildSineWavPcm16(DeviceSampleRate, 2, DeviceSampleRate * 2));

        //Act
        using var monoProvider = new ChunkedDataProvider(engine, DeviceFormat, mono);
        using var stereoProvider = new ChunkedDataProvider(engine, DeviceFormat, stereo);

        //Assert
        DurationOf(monoProvider).Should().BeApproximately(2.0, 0.01);
        DurationOf(monoProvider).Should().BeApproximately(DurationOf(stereoProvider), 0.01);
    }

    [Fact]
    public void A_file_at_another_rate_still_reports_its_own_duration()
    {
        //Arrange
        // The rate is converted on the way to the device as well, so Length must be in the
        // device's rate while the DURATION stays the file's.
        using var engine = new MiniAudioEngine();
        using var stream = new MemoryStream(TestAudio.BuildSineWavPcm16(22050, 1, 22050));

        //Act
        using var provider = new ChunkedDataProvider(engine, DeviceFormat, stream);

        //Assert
        DurationOf(provider).Should().BeApproximately(1.0, 0.02);
    }
}
