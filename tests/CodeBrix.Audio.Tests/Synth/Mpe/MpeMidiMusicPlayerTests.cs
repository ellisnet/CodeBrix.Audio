using System;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.Mpe;
using CodeBrix.Audio.Tests.Synth;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.Mpe;

/// <summary>
/// The MPE settings a host reaches through <see cref="MidiMusicPlayer"/>.
/// </summary>
/// <remarks>
/// These are properties of the PLAYER, not of any one sequence, so they survive a load - the same
/// rule the speed and the message hooks follow, and they reach whichever instrument format was
/// loaded. The tests that load an instrument open the audio device and are opt-in via
/// CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1; the rest need no hardware.
/// </remarks>
[Collection("SharedAudioOutput")]
public sealed class MpeMidiMusicPlayerTests : IDisposable
{
    private static readonly bool PlaybackEnabled =
        Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS") == "1";

    private const string PlaybackSkipReason =
        "Set CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 to run tests that open the audio device.";

    /// <summary>Resets the process-wide shared output before each test for isolation.</summary>
    public MpeMidiMusicPlayerTests() => SharedAudioOutput.Shutdown();

    /// <summary>Resets the process-wide shared output after each test for isolation.</summary>
    public void Dispose() => SharedAudioOutput.Shutdown();

    [Fact]
    public void the_player_starts_with_no_mpe_zones()
    {
        //Arrange
        using var player = new MidiMusicPlayer();

        //Act
        //Assert
        player.MpeMode.Should().Be(MpeMode.Off);
        player.MpeMemberBendRange.Should().Be(48.0);
        player.MpeLowerZone.IsActive.Should().BeFalse();
        player.MpeUpperZone.IsActive.Should().BeFalse();
    }

    [Fact]
    public void the_lift_velocity_is_unavailable_until_an_instrument_is_loaded()
    {
        //Arrange
        using var player = new MidiMusicPlayer();

        //Act
        //Assert
        player.GetReleaseVelocity(0, 60).Should().Be(-1);
    }

    [Fact]
    public void the_mpe_settings_are_remembered_before_anything_is_loaded()
    {
        //Arrange
        using var player = new MidiMusicPlayer();

        //Act
        player.MpeMode = MpeMode.Auto;
        player.MpeMemberBendRange = 24.0;
        player.MpeLowerZoneMemberCount = 6;
        player.MpeUpperZoneMemberCount = 5;

        //Assert
        player.MpeMode.Should().Be(MpeMode.Auto);
        player.MpeMemberBendRange.Should().Be(24.0);
        player.MpeLowerZoneMemberCount.Should().Be(6);
        player.MpeUpperZoneMemberCount.Should().Be(5);
    }

    [Fact]
    public void a_soundfont_instrument_takes_the_mpe_settings_chosen_before_the_load()
    {
        //Arrange
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);
        // The fixture is declared first so the player - which holds its instrument - is disposed
        // before the temporary files go.
        using var fixtures = MpeEngineFixtures.Create();
        using var player = new MidiMusicPlayer();

        //Act
        player.MpeMode = MpeMode.LowerZone;
        player.MpeMemberBendRange = 24.0;
        player.MpeLowerZoneMemberCount = 6;
        player.Load(fixtures.SoundFont, Performance(MpeEngineFixtures.SoundFontKey));

        //Assert
        player.MpeLowerZone.IsActive.Should().BeTrue();
        player.MpeLowerZone.MemberCount.Should().Be(6);
        player.MpeLowerZone.MemberBendRange.Should().Be(24.0);
        player.GetReleaseVelocity(1, MpeEngineFixtures.SoundFontKey).Should().Be(0);
    }

    [Fact]
    public void an_sfz_instrument_takes_the_mpe_settings_chosen_after_the_load()
    {
        //Arrange
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);
        // The fixture is declared first so the player - which holds its instrument - is disposed
        // before the temporary files go.
        using var fixtures = MpeEngineFixtures.Create();
        using var player = new MidiMusicPlayer();

        //Act
        player.Load(fixtures.SfzInstrument, Performance(MpeEngineFixtures.SfzKey));
        player.MpeMode = MpeMode.UpperZone;
        player.MpeMemberBendRange = 12.0;
        player.MpeUpperZoneMemberCount = 4;

        //Assert
        player.MpeUpperZone.IsActive.Should().BeTrue();
        player.MpeUpperZone.MemberCount.Should().Be(4);
        player.MpeUpperZone.MemberBendRange.Should().Be(12.0);
        player.GetReleaseVelocity(14, MpeEngineFixtures.SfzKey).Should().Be(0);
    }

    private static MidiSequence Performance(int key) =>
        MpeSequences.Sequence(MpeEngineFixtures.AbletonStyleExport(key));
}
