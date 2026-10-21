using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;
using System.Threading;
using CodeBrix.Audio.Engine.Backends.MiniAudio;
using CodeBrix.Audio.Engine.Components;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Engine.Providers;
using CodeBrix.Audio.Engine.Structs;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.Engine.Tests;

/// <summary>
/// Tests for <see cref="MiniAudioEngine"/>. Engine construction initializes a miniaudio
/// context (backend probing) but opens no audio device, so the non-playback tests are
/// safe to run without audio hardware. The real-playback test is opt-in (see below).
/// </summary>
public class MiniAudioEngineTests
{
    /// <summary>
    /// Set this environment variable to "1" to run the opt-in real-device playback test.
    /// It is skipped by default because it opens an actual audio device and emits sound.
    /// </summary>
    private const string PlaybackEnvVar = "CODEBRIX_AUDIO_ENGINE_RUN_PLAYBACK_TESTS";

    private const int S_FALSE = 1;
    private const uint COINIT_APARTMENTTHREADED = 0x2;

    [DllImport("ole32.dll")]
    private static extern int CoInitializeEx(IntPtr reserved, uint coInit);

    [DllImport("ole32.dll")]
    private static extern void CoUninitialize();

    [Fact]
    public void AvailableBackends_is_not_empty_on_this_platform() =>
        MiniAudioEngine.AvailableBackends.Should().NotBeEmpty();

    [Fact]
    public void Engine_constructs_and_disposes_without_error()
    {
        //Arrange / Act
        var engine = new MiniAudioEngine();

        //Assert
        engine.Should().NotBeNull();
        engine.Dispose();
    }

    [Fact]
    public void CreateDecoder_returns_a_working_decoder_for_a_generated_wav()
    {
        //Arrange
        using var engine = new MiniAudioEngine();
        using var stream = new MemoryStream(TestAudio.BuildSineWavPcm16(44100, 1, 44100));

        //Act
        using var decoder = engine.CreateDecoder(stream, out var detected);

        //Assert
        detected.SampleRate.Should().BeGreaterThan(0);
        decoder.Channels.Should().BeGreaterThan(0);
        decoder.SampleRate.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Plays_a_generated_tone_on_a_real_device()
    {
        Assert.SkipUnless(
            Environment.GetEnvironmentVariable(PlaybackEnvVar) == "1",
            $"Opt-in playback test. Set {PlaybackEnvVar}=1 to run it against a real audio device.");

        //Arrange
        // Both test assemblies run as separate processes; this keeps their audible tests from
        // sounding on top of one another.
        using var audible = new AudibleTestScope();

        using var engine = new MiniAudioEngine();
        var format = AudioFormat.DvdHq;
        using var device = engine.InitializePlaybackDevice(null, format);
        using var stream = new MemoryStream(
            TestAudio.BuildCloseEncountersWavPcm16(format.SampleRate, format.Channels));
        using ISoundDataProvider provider = new StreamDataProvider(engine, format, stream);
        using var player = new SoundPlayer(engine, format, provider);

        //Act
        device.Start();
        device.MasterMixer.AddComponent(player);
        player.Play();

        // Long enough for all five tones to sound - this test exists to be listened to.
        Thread.Sleep(TestAudio.CloseEncountersDuration + TimeSpan.FromMilliseconds(250));
        player.Stop();
        device.MasterMixer.RemoveComponent(player);
        device.Stop();

        //Assert
        player.Should().NotBeNull();
    }

    [Fact]
    [SupportedOSPlatform("windows")]
    public void Disposing_on_another_thread_leaves_that_threads_COM_apartment_intact()
    {
        Assert.SkipUnless(OperatingSystem.IsWindows(), "COM apartments exist only on Windows.");

        //Arrange: create the engine on one thread (a game loop, say)...
        MiniAudioEngine engine = null;
        var creator = new Thread(() => engine = new MiniAudioEngine());
        creator.Start();
        creator.Join();

        //Act: ...and dispose it on a UI-style STA thread.
        var result = 0;
        Exception failure = null;
        var disposer = new Thread(() =>
        {
            try
            {
                engine.Dispose();
                // S_FALSE: this thread's STA is still initialized. S_OK would mean the dispose
                // uninitialized COM here, tearing the apartment down under the thread's own objects.
                result = CoInitializeEx(IntPtr.Zero, COINIT_APARTMENTTHREADED);
                if (result >= 0) CoUninitialize();
            }
            catch (Exception e)
            {
                failure = e;
            }
        });
        disposer.SetApartmentState(ApartmentState.STA);
        disposer.Start();
        disposer.Join();

        //Assert
        failure.Should().BeNull();
        result.Should().Be(S_FALSE);
    }
}
