using System;
using System.Linq;
using CodeBrix.Audio.Engine.Abstracts;
using CodeBrix.Audio.Engine.Abstracts.Devices;
using CodeBrix.Audio.Engine.Backends.MiniAudio;
using CodeBrix.Audio.Engine.Backends.MiniAudio.Devices;
using CodeBrix.Audio.Engine.Components;
using CodeBrix.Audio.Engine.Structs;
using SilverAssertions;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.Engine.Tests;

/// <summary>
/// Tests for the <see cref="AudioPlaybackDevice.MutedSwitchName"/> switch. A device with no backend
/// drives the same render path a real device's callback does, so no audio device is opened and
/// nothing is heard either way.
/// </summary>
public class OutputMuteTests
{
    [Fact]
    public void Muted_device_renders_silence_while_its_mixer_plays()
    {
        //Arrange
        using var engine = new MiniAudioEngine();
        using var device = CreateDevice(engine, muted: true);
        device.MasterMixer.AddComponent(new Oscillator(engine, device.Format));

        //Act
        var output = device.Render(1024);

        //Assert
        device.IsOutputMuted.Should().BeTrue();
        output.All(sample => sample == 0f).Should().BeTrue();
    }

    [Fact]
    public void Device_created_without_the_switch_renders_its_mixer()
    {
        //Arrange
        using var engine = new MiniAudioEngine();
        using var device = CreateDevice(engine, muted: false);
        device.MasterMixer.AddComponent(new Oscillator(engine, device.Format));

        //Act
        var output = device.Render(1024);

        //Assert
        device.IsOutputMuted.Should().BeFalse();
        output.Max(Math.Abs).Should().BeGreaterThan(0.1f);
    }

    // The switch is read when a device is created, so it is restored straight after: other tests in
    // this assembly may create devices at the same time.
    private static RenderOnlyDevice CreateDevice(AudioEngine engine, bool muted)
    {
        var previous = AppContext.TryGetSwitch(AudioPlaybackDevice.MutedSwitchName, out var wasMuted) && wasMuted;
        AppContext.SetSwitch(AudioPlaybackDevice.MutedSwitchName, muted);
        try
        {
            return new RenderOnlyDevice(engine, AudioFormat.Cd);
        }
        finally
        {
            AppContext.SetSwitch(AudioPlaybackDevice.MutedSwitchName, previous);
        }
    }

    private sealed class RenderOnlyDevice(AudioEngine engine, AudioFormat format)
        : AudioPlaybackDevice(engine, format, new MiniAudioDeviceConfig())
    {
        public float[] Render(int frames)
        {
            var buffer = new float[frames * Format.Channels];
            RenderOutput(buffer, Format.Channels);
            return buffer;
        }

        public override void Start() => IsRunning = true;

        public override void Stop() => IsRunning = false;

        public override void Dispose() => IsDisposed = true;
    }
}
