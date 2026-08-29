using System;
using CodeBrix.Audio.Engine.Backends.MiniAudio;
using CodeBrix.Audio.Engine.Interfaces;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Engine.Tests;

/// <summary>
/// Tests for the engine's PACKET codec registry - <c>RegisterPacketCodecFactory</c> and friends -
/// which mirrors the stream registry but is keyed by codec identifier ("vorbis") rather than by
/// container format identifier ("ogg").
/// </summary>
/// <remarks>
/// Engine construction initializes a miniaudio context but opens no audio device, so none of these
/// touch audio hardware.
/// </remarks>
public class PacketCodecRegistryTests
{
    private static readonly ReadOnlyMemory<byte> SomeCodecPrivate = new byte[] { 2, 30, 30 };

    [Fact]
    public void A_registered_factory_is_listed_for_its_codec()
    {
        //Arrange
        using var engine = new MiniAudioEngine();
        var factory = new FakePacketCodecFactory("test.one", 0, "vorbis");

        //Act
        engine.RegisterPacketCodecFactory(factory);

        //Assert
        engine.GetRegisteredPacketCodecs("vorbis").Should().Contain(factory);
    }

    [Fact]
    public void An_unregistered_codec_lists_nothing()
    {
        //Arrange
        using var engine = new MiniAudioEngine();

        //Act
        var registered = engine.GetRegisteredPacketCodecs("nosuchcodec");

        //Assert
        registered.Should().BeEmpty();
    }

    [Fact]
    public void Registering_null_is_refused()
    {
        //Arrange
        using var engine = new MiniAudioEngine();

        //Act
        var register = () => engine.RegisterPacketCodecFactory(null);

        //Assert
        register.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void Factories_are_listed_highest_priority_first()
    {
        //Arrange
        using var engine = new MiniAudioEngine();
        var low = new FakePacketCodecFactory("test.low", -10, "vorbis");
        var high = new FakePacketCodecFactory("test.high", 5, "vorbis");

        //Act
        engine.RegisterPacketCodecFactory(low);
        engine.RegisterPacketCodecFactory(high);

        //Assert
        var registered = engine.GetRegisteredPacketCodecs("vorbis");
        registered[0].Should().BeSameAs(high);
        registered[1].Should().BeSameAs(low);
    }

    [Fact]
    public void The_later_registration_wins_a_priority_tie()
    {
        //Arrange
        using var engine = new MiniAudioEngine();
        var first = new FakePacketCodecFactory("test.first", 0, "vorbis");
        var second = new FakePacketCodecFactory("test.second", 0, "vorbis");

        //Act
        engine.RegisterPacketCodecFactory(first);
        engine.RegisterPacketCodecFactory(second);

        //Assert
        engine.GetRegisteredPacketCodecs("vorbis")[0].Should().BeSameAs(second);
    }

    [Fact]
    public void A_codec_id_is_matched_without_regard_to_case()
    {
        //Arrange
        using var engine = new MiniAudioEngine();
        var factory = new FakePacketCodecFactory("test.case", 0, "vorbis");
        engine.RegisterPacketCodecFactory(factory);

        //Act
        var registered = engine.GetRegisteredPacketCodecs("VORBIS");

        //Assert
        registered.Should().Contain(factory);
    }

    [Fact]
    public void SetPacketCodecPriority_reorders_the_factories()
    {
        //Arrange
        using var engine = new MiniAudioEngine();
        var low = new FakePacketCodecFactory("test.low", -10, "vorbis");
        var high = new FakePacketCodecFactory("test.high", 5, "vorbis");
        engine.RegisterPacketCodecFactory(low);
        engine.RegisterPacketCodecFactory(high);

        //Act
        var updated = engine.SetPacketCodecPriority("test.low", 100);

        //Assert
        updated.Should().BeTrue();
        engine.GetRegisteredPacketCodecs("vorbis")[0].Should().BeSameAs(low);
    }

    [Fact]
    public void SetPacketCodecPriority_reports_an_unknown_factory()
    {
        //Arrange
        using var engine = new MiniAudioEngine();

        //Act
        var updated = engine.SetPacketCodecPriority("test.absent", 3);

        //Assert
        updated.Should().BeFalse();
    }

    [Fact]
    public void UnregisterPacketCodecFactory_removes_every_registration_of_that_factory()
    {
        //Arrange
        using var engine = new MiniAudioEngine();
        var factory = new FakePacketCodecFactory("test.both", 0, "vorbis", "opus");
        engine.RegisterPacketCodecFactory(factory);

        //Act
        var removed = engine.UnregisterPacketCodecFactory("test.both");

        //Assert
        removed.Should().BeTrue();
        engine.GetRegisteredPacketCodecs("vorbis").Should().BeEmpty();
        engine.GetRegisteredPacketCodecs("opus").Should().BeEmpty();
    }

    [Fact]
    public void UnregisterPacketCodecFactory_reports_an_unknown_factory()
    {
        //Arrange
        using var engine = new MiniAudioEngine();

        //Act
        var removed = engine.UnregisterPacketCodecFactory("test.absent");

        //Assert
        removed.Should().BeFalse();
    }

    [Fact]
    public void CreatePacketDecoder_uses_the_highest_priority_factory()
    {
        //Arrange
        using var engine = new MiniAudioEngine();
        engine.RegisterPacketCodecFactory(new FakePacketCodecFactory("test.low", -10, "vorbis"));
        engine.RegisterPacketCodecFactory(new FakePacketCodecFactory("test.high", 5, "vorbis"));

        //Act
        using var decoder = engine.CreatePacketDecoder("vorbis", SomeCodecPrivate);

        //Assert
        ((FakePacketSoundDecoder)decoder).FactoryId.Should().Be("test.high");
    }

    [Fact]
    public void CreatePacketDecoder_passes_the_codec_id_and_private_data_through()
    {
        //Arrange
        using var engine = new MiniAudioEngine();
        var factory = new FakePacketCodecFactory("test.echo", 0, "vorbis");
        engine.RegisterPacketCodecFactory(factory);

        //Act
        using var decoder = engine.CreatePacketDecoder("vorbis", SomeCodecPrivate);

        //Assert
        factory.LastCodecId.Should().Be("vorbis");
        factory.LastCodecPrivate.Length.Should().Be(SomeCodecPrivate.Length);
    }

    [Fact]
    public void CreatePacketDecoder_moves_on_from_a_factory_that_declines()
    {
        //Arrange
        using var engine = new MiniAudioEngine();
        var declining = new FakePacketCodecFactory("test.declines", 5, "vorbis") { Declines = true };
        engine.RegisterPacketCodecFactory(declining);
        engine.RegisterPacketCodecFactory(new FakePacketCodecFactory("test.serves", 0, "vorbis"));

        //Act
        using var decoder = engine.CreatePacketDecoder("vorbis", SomeCodecPrivate);

        //Assert
        declining.CreateCallCount.Should().Be(1);
        ((FakePacketSoundDecoder)decoder).FactoryId.Should().Be("test.serves");
    }

    [Fact]
    public void CreatePacketDecoder_moves_on_from_a_factory_that_throws()
    {
        //Arrange
        using var engine = new MiniAudioEngine();
        engine.RegisterPacketCodecFactory(new FakePacketCodecFactory("test.throws", 5, "vorbis") { Throws = true });
        engine.RegisterPacketCodecFactory(new FakePacketCodecFactory("test.serves", 0, "vorbis"));

        //Act
        using var decoder = engine.CreatePacketDecoder("vorbis", SomeCodecPrivate);

        //Assert
        ((FakePacketSoundDecoder)decoder).FactoryId.Should().Be("test.serves");
    }

    [Fact]
    public void CreatePacketDecoder_names_the_codec_when_nothing_serves_it()
    {
        //Arrange
        using var engine = new MiniAudioEngine();

        //Act
        var create = () => engine.CreatePacketDecoder("opus", SomeCodecPrivate);

        //Assert
        create.Should().Throw<NotSupportedException>().WithMessage("*opus*");
    }

    [Fact]
    public void CreatePacketDecoder_names_the_codec_when_every_factory_declines()
    {
        //Arrange
        using var engine = new MiniAudioEngine();
        engine.RegisterPacketCodecFactory(new FakePacketCodecFactory("test.declines", 0, "opus") { Declines = true });

        //Act
        var create = () => engine.CreatePacketDecoder("opus", SomeCodecPrivate);

        //Assert
        create.Should().Throw<NotSupportedException>().WithMessage("*opus*");
    }

    [Fact]
    public void The_packet_registry_is_separate_from_the_stream_registry()
    {
        //Arrange
        using var engine = new MiniAudioEngine();

        //Act
        // A packet factory declaring "ogg" must not turn up where the STREAM registry is consulted -
        // the two are keyed differently and a mix-up would hand an Ogg file to a packet decoder.
        engine.RegisterPacketCodecFactory(new FakePacketCodecFactory("test.ogg.packets", 0, "ogg"));

        //Assert
        engine.GetRegisteredPacketCodecs("ogg").Should().NotBeEmpty();
        foreach (var factory in engine.GetRegisteredCodecs("ogg"))
        {
            factory.FactoryId.Should().NotBe("test.ogg.packets");
        }
    }
}
