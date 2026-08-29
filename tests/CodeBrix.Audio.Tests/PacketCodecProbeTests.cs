using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Tests.Utils;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Tests for <see cref="SharedAudioOutput.IsPacketCodecSupported"/> and
/// <see cref="SharedAudioOutput.SupportedPacketCodecIds"/>: asking what the packet seam can decode
/// WITHOUT starting the shared output or opening the audio device.
/// </summary>
/// <remarks>
/// <para>
/// The whole point of the probe is that it starts nothing, so most of these tests assert
/// <see cref="SharedAudioOutput.IsRunning"/> is false on both sides of the call. The one test that
/// checks the probe against what <see cref="SharedAudioOutput.CreatePacketDecoder"/> actually
/// resolves does open a device, and is opt-in via CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1.
/// </para>
/// <para>
/// A packet codec factory registered on the shared output is remembered for the lifetime of the
/// PROCESS and survives <see cref="SharedAudioOutput.Shutdown"/>, so every test here that registers
/// one uses a codec identifier of its own and no test asserts a registered identifier is absent.
/// </para>
/// </remarks>
[Collection("SharedAudioOutput")]
public sealed class PacketCodecProbeTests : IDisposable
{
    private static readonly bool PlaybackEnabled =
        Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS") == "1";

    private const string PlaybackSkipReason =
        "Set CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 to run tests that open the audio device.";

    /// <summary>Resets the process-wide shared output before each test for isolation.</summary>
    public PacketCodecProbeTests() => SharedAudioOutput.Shutdown();

    /// <summary>Resets the process-wide shared output after each test for isolation.</summary>
    public void Dispose() => SharedAudioOutput.Shutdown();

    [Fact]
    public void The_built_in_vorbis_packet_codec_is_supported_without_starting_anything()
    {
        //Arrange
        SharedAudioOutput.IsRunning.Should().BeFalse();

        //Act
        var supported = SharedAudioOutput.IsPacketCodecSupported("vorbis");

        //Assert
        supported.Should().BeTrue();
        SharedAudioOutput.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void A_codec_nothing_serves_is_not_supported()
    {
        //Arrange / Act
        var supported = SharedAudioOutput.IsPacketCodecSupported("no.such.codec.at.all");

        //Assert
        supported.Should().BeFalse();
        SharedAudioOutput.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void A_missing_codec_identifier_is_not_supported()
    {
        //Arrange / Act
        var forNull = SharedAudioOutput.IsPacketCodecSupported(null);
        var forEmpty = SharedAudioOutput.IsPacketCodecSupported(string.Empty);

        //Assert
        forNull.Should().BeFalse();
        forEmpty.Should().BeFalse();
    }

    [Fact]
    public void The_probe_matches_a_codec_identifier_whatever_its_case()
    {
        //Arrange / Act
        var upper = SharedAudioOutput.IsPacketCodecSupported("VORBIS");
        var mixed = SharedAudioOutput.IsPacketCodecSupported("VoRbIs");

        //Assert
        // The engine's packet registry is case-insensitive, so the probe has to be as well or it
        // would disagree with what CreatePacketDecoder resolves.
        upper.Should().BeTrue();
        mixed.Should().BeTrue();
        SharedAudioOutput.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void Opus_is_unsupported_here_until_a_package_registers_it()
    {
        //Arrange
        // This package carries no Opus packet decoder; the add-on package does. Both halves of that
        // are asserted in one test because a registration lasts for the life of the process.
        var before = SharedAudioOutput.IsPacketCodecSupported("opus");

        //Act
        SharedAudioOutput.RegisterPacketCodecFactory(
            new FakeNamedPacketCodecFactory("codebrix.audio.tests.opus", "opus"));
        var after = SharedAudioOutput.IsPacketCodecSupported("opus");

        //Assert
        before.Should().BeFalse();
        after.Should().BeTrue();
        SharedAudioOutput.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void A_registered_factory_makes_its_codecs_supported_without_starting_anything()
    {
        //Arrange
        const string codecId = "codebrix.audio.tests.probe.alpha";
        var before = SharedAudioOutput.IsPacketCodecSupported(codecId);

        //Act
        SharedAudioOutput.RegisterPacketCodecFactory(
            new FakeNamedPacketCodecFactory("codebrix.audio.tests.probe.alpha.factory", codecId));
        var after = SharedAudioOutput.IsPacketCodecSupported(codecId);

        //Assert
        before.Should().BeFalse();
        after.Should().BeTrue();
        SharedAudioOutput.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void The_supported_identifiers_start_with_the_built_in_ones()
    {
        //Arrange
        const string codecId = "codebrix.audio.tests.probe.beta";

        //Act
        SharedAudioOutput.RegisterPacketCodecFactory(
            new FakeNamedPacketCodecFactory("codebrix.audio.tests.probe.beta.factory", codecId));
        var ids = SharedAudioOutput.SupportedPacketCodecIds;

        //Assert
        // Built-ins first, registered ones after, and every identifier the probe answers true for.
        ids.First().Should().Be("vorbis");
        ids.Should().Contain(codecId);
        SharedAudioOutput.IsRunning.Should().BeFalse();
        foreach (var id in ids)
        {
            SharedAudioOutput.IsPacketCodecSupported(id).Should().BeTrue();
        }
    }

    [Fact]
    public void The_same_codec_from_two_factories_is_listed_once()
    {
        //Arrange
        const string codecId = "codebrix.audio.tests.probe.gamma";

        //Act
        SharedAudioOutput.RegisterPacketCodecFactory(
            new FakeNamedPacketCodecFactory("codebrix.audio.tests.probe.gamma.one", codecId));
        SharedAudioOutput.RegisterPacketCodecFactory(
            new FakeNamedPacketCodecFactory("codebrix.audio.tests.probe.gamma.two", codecId.ToUpperInvariant()));
        var ids = SharedAudioOutput.SupportedPacketCodecIds;

        //Assert
        ids.Count(id => string.Equals(id, codecId, StringComparison.OrdinalIgnoreCase)).Should().Be(1);
    }

    [Fact]
    public void Reading_the_probe_never_starts_the_shared_output()
    {
        //Arrange
        SharedAudioOutput.IsRunning.Should().BeFalse();

        //Act
        var supported = SharedAudioOutput.IsPacketCodecSupported("vorbis");
        var ids = SharedAudioOutput.SupportedPacketCodecIds;

        //Assert
        // The contrast that makes the probe worth having: CreatePacketDecoder would have opened the
        // audio device to answer the same question, because the codec registry lives on the engine.
        supported.Should().BeTrue();
        ids.Should().NotBeEmpty();
        SharedAudioOutput.IsRunning.Should().BeFalse();
    }

    [Fact]
    public void The_probe_agrees_with_what_the_shared_output_then_resolves()
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange
        using var audible = new AudibleTestScope();
        var packets = OggPacketReader.ReadPackets(TestAssets.Path(TestAssets.VorbisToneStereo));
        var codecPrivate = OggPacketReader.BuildXiphCodecPrivate(packets[0], packets[1], packets[2]);
        var probedSupported = SharedAudioOutput.IsPacketCodecSupported("vorbis");
        var probedMissing = SharedAudioOutput.IsPacketCodecSupported("no.such.codec.at.all");
        var probedIds = SharedAudioOutput.SupportedPacketCodecIds;
        SharedAudioOutput.IsRunning.Should().BeFalse();

        //Act
        // Now start the output for real and ask it the same questions.
        using var decoder = SharedAudioOutput.CreatePacketDecoder("vorbis", codecPrivate);
        var resolveMissing = () => SharedAudioOutput.CreatePacketDecoder("no.such.codec.at.all", codecPrivate);

        //Assert
        probedSupported.Should().BeTrue();
        decoder.Should().NotBeNull();
        probedMissing.Should().BeFalse();
        resolveMissing.Should().Throw<NotSupportedException>();
        SharedAudioOutput.IsRunning.Should().BeTrue();

        // Everything the probe listed really is registered on the engine the shared output started.
        var engine = SharedAudioOutput.EnsureStarted(48000).Engine;
        foreach (var id in probedIds)
        {
            engine.GetRegisteredPacketCodecs(id).Should().NotBeEmpty();
        }
        engine.GetRegisteredPacketCodecs("no.such.codec.at.all").Should().BeEmpty();
    }
}
