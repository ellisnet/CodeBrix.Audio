using System;
using System.Collections.Generic;
using CodeBrix.Audio.Engine.Abstracts;
using CodeBrix.Audio.Engine.Abstracts.Devices;
using CodeBrix.Audio.Engine.Backends.MiniAudio;
using CodeBrix.Audio.Engine.Structs;
using CodeBrix.Audio.Wave;
using Xunit;

namespace CodeBrix.Audio.Tests.Wave;

[Collection("SharedAudioOutput")]
public class BackendRegistrationTests
{
    [Fact]
    public void platform_factory_is_lazy_survives_shutdown_and_preserves_format()
    {
        //Arrange
        SharedAudioOutput.Shutdown();
        var created = new List<TestEngine>();
        try
        {
            SharedAudioOutput.UseEngineFactory(() => { var engine = new TestEngine(); created.Add(engine); return engine; });
            Assert.Empty(created);
            SharedAudioOutput.Configure(48000, 2);

            //Act
            var first = SharedAudioOutput.EnsureStarted(44100);
            Assert.Equal(48000, first.Format.SampleRate);
            Assert.Throws<InvalidOperationException>(() => SharedAudioOutput.UseEngineFactory(() => new TestEngine()));
            SharedAudioOutput.Shutdown();
            var second = SharedAudioOutput.EnsureStarted(44100);

            //Assert
            Assert.Equal(2, created.Count);
            Assert.True(created[0].IsDisposed);
            Assert.True(first.IsDisposed);
            Assert.Equal(44100, second.Format.SampleRate);
            Assert.NotSame(first.Engine, second.Engine);
            Assert.NotEmpty(second.Engine.GetRegisteredCodecs("flac"));
            Assert.NotEmpty(second.Engine.GetRegisteredPacketCodecs("vorbis"));
        }
        finally
        {
            SharedAudioOutput.Shutdown();
            SharedAudioOutput.UseEngineFactory(() => new MiniAudioEngine());
        }
    }

    [Fact]
    public void failed_start_disposes_engine_and_allows_retry()
    {
        //Arrange
        SharedAudioOutput.Shutdown();
        var failed = new TestEngine { FailPlaybackStart = true };
        try
        {
            SharedAudioOutput.UseEngineFactory(() => failed);

            //Act
            Assert.Throws<InvalidOperationException>(() => SharedAudioOutput.EnsureStarted(48000));

            //Assert
            Assert.True(failed.IsDisposed);
            Assert.True(failed.Playback.IsDisposed);
            Assert.False(SharedAudioOutput.IsRunning);
            SharedAudioOutput.UseEngineFactory(() => new TestEngine());
            Assert.True(SharedAudioOutput.EnsureStarted(48000).IsRunning);
        }
        finally
        {
            SharedAudioOutput.Shutdown();
            SharedAudioOutput.UseEngineFactory(() => new MiniAudioEngine());
        }
    }

    [Fact]
    public void external_backend_duplex_closes_playback_when_capture_initialization_fails()
    {
        //Arrange
        using var engine = new TestEngine { FailCaptureOpen = true };

        //Act
        Assert.Throws<InvalidOperationException>(() => engine.InitializeFullDuplexDevice(null, null, AudioFormat.DvdHq));

        //Assert
        Assert.True(engine.Playback.IsDisposed);
    }

    [Fact]
    public void external_backend_duplex_stops_capture_when_playback_start_fails()
    {
        //Arrange
        using var engine = new TestEngine { FailPlaybackStart = true };
        using var duplex = engine.InitializeFullDuplexDevice(null, null, AudioFormat.DvdHq);

        //Act
        Assert.Throws<InvalidOperationException>(duplex.Start);

        //Assert
        Assert.False(duplex.CaptureDevice.IsRunning);
        Assert.False(duplex.IsRunning);
    }

    private sealed class Options : DeviceConfig { }
    private sealed class TestEngine : AudioEngine
    {
        private readonly List<AudioDevice> devices = new List<AudioDevice>();
        internal bool FailPlaybackStart;
        internal bool FailCaptureOpen;
        internal Playback Playback;
        protected override void CleanupBackend() { foreach (var device in devices) device.Dispose(); }
        public override void UpdateAudioDevicesInfo() { }
        public override AudioPlaybackDevice InitializePlaybackDevice(DeviceInfo? info, AudioFormat format, DeviceConfig config = null)
        { Playback = new Playback(this, format, FailPlaybackStart); devices.Add(Playback); return Playback; }
        public override AudioCaptureDevice InitializeCaptureDevice(DeviceInfo? info, AudioFormat format, DeviceConfig config = null)
        { if (FailCaptureOpen) throw new InvalidOperationException(); var device = new Capture(this, format); devices.Add(device); return device; }
        public override FullDuplexDevice InitializeFullDuplexDevice(DeviceInfo? output, DeviceInfo? input, AudioFormat format, DeviceConfig config = null) =>
            CreateFullDuplexDevice(output, input, format, config ?? new Options());
        public override AudioCaptureDevice InitializeLoopbackDevice(AudioFormat format, DeviceConfig config = null) => throw new NotSupportedException();
        public override AudioPlaybackDevice SwitchDevice(AudioPlaybackDevice old, DeviceInfo info, DeviceConfig config = null) => throw new NotSupportedException();
        public override AudioCaptureDevice SwitchDevice(AudioCaptureDevice old, DeviceInfo info, DeviceConfig config = null) => throw new NotSupportedException();
        public override FullDuplexDevice SwitchDevice(FullDuplexDevice old, DeviceInfo? output, DeviceInfo? input, DeviceConfig config = null) => throw new NotSupportedException();
    }
    private sealed class Playback : AudioPlaybackDevice
    {
        private readonly bool fail;
        internal Playback(AudioEngine engine, AudioFormat format, bool fail) : base(engine, format, new Options()) { this.fail = fail; }
        public override void Start() { if (fail) throw new InvalidOperationException(); IsRunning = true; }
        public override void Stop() { IsRunning = false; }
        public override void Dispose() { Stop(); IsDisposed = true; }
    }
    private sealed class Capture : AudioCaptureDevice
    {
        internal Capture(AudioEngine engine, AudioFormat format) : base(engine, format, new Options()) { }
        public override void Start() { IsRunning = true; }
        public override void Stop() { IsRunning = false; }
        public override void Dispose() { Stop(); IsDisposed = true; }
    }
}
