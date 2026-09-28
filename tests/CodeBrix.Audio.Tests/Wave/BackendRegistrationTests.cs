using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Utils;
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

    [Fact]
    public void device_options_and_observer_follow_the_actual_shared_output_across_restarts()
    {
        //Arrange
        SharedAudioOutput.Shutdown();
        var options = new Options();
        int configuredRate = 0;
        int observed = 0;
        int detached = 0;
        try
        {
            SharedAudioOutput.UseEngineFactory(() => new TestEngine());
            SharedAudioOutput.UseDeviceConfigFactory(format => { configuredRate = format.SampleRate; return options; });
            SharedAudioOutput.UseOutputObserver((engine, device) =>
            {
                observed++;
                Assert.False(device.IsRunning);
                Assert.Same(engine, device.Engine);
                return new Subscription(() => { Assert.False(device.IsDisposed); detached++; });
            });
            Assert.False(SharedAudioOutput.TryObserveOutput((_, _) => throw new Exception("must remain lazy")));
            SharedAudioOutput.Configure(48000);

            //Act
            var first = SharedAudioOutput.EnsureStarted(44100);
            Assert.True(SharedAudioOutput.TryObserveOutput((engine, device) =>
            {
                Assert.Same(first, device);
                Assert.Same(first.Engine, engine);
                Assert.Same(options, device.Config);
                Assert.Throws<InvalidOperationException>(() => SharedAudioOutput.Shutdown());
            }));
            Assert.Equal(48000, configuredRate);
            Assert.Throws<InvalidOperationException>(() => SharedAudioOutput.UseDeviceConfigFactory(null));
            Assert.Throws<InvalidOperationException>(() => SharedAudioOutput.UseOutputObserver(null));
            SharedAudioOutput.Shutdown();
            Assert.Equal(1, detached);
            var second = SharedAudioOutput.EnsureStarted(44100);

            //Assert
            Assert.NotSame(first, second);
            Assert.Equal(2, observed);
            Assert.Equal(44100, configuredRate);
            Assert.Same(options, second.Config);
        }
        finally
        {
            SharedAudioOutput.Shutdown();
            SharedAudioOutput.UseDeviceConfigFactory(null);
            SharedAudioOutput.UseOutputObserver(null);
            SharedAudioOutput.UseEngineFactory(() => new MiniAudioEngine());
        }
        Assert.Equal(2, detached);
    }

    [Fact]
    public void observer_is_detached_on_start_failure_and_observation_errors_do_not_poison_output()
    {
        //Arrange
        SharedAudioOutput.Shutdown();
        var engine = new TestEngine { FailPlaybackStart = true };
        int detached = 0;
        try
        {
            SharedAudioOutput.UseEngineFactory(() => engine);
            SharedAudioOutput.UseOutputObserver((_, device) => new Subscription(() =>
            {
                Assert.False(device.IsDisposed);
                detached++;
            }));

            //Act / Assert
            Assert.Throws<InvalidOperationException>(() => SharedAudioOutput.EnsureStarted(48000));
            Assert.Equal(1, detached);
            Assert.True(engine.IsDisposed);
            Assert.False(SharedAudioOutput.IsRunning);
            SharedAudioOutput.UseEngineFactory(() => new TestEngine());
            SharedAudioOutput.EnsureStarted(48000);
            Assert.Throws<IOException>(() => SharedAudioOutput.TryObserveOutput((_, _) => throw new IOException()));
            Assert.True(SharedAudioOutput.TryObserveOutput((_, device) => Assert.True(device.IsRunning)));
        }
        finally
        {
            SharedAudioOutput.Shutdown();
            SharedAudioOutput.UseOutputObserver(null);
            SharedAudioOutput.UseEngineFactory(() => new MiniAudioEngine());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void normal_file_player_uses_observed_device_and_honors_stream_ownership(bool leaveOpen)
    {
        //Arrange
        SharedAudioOutput.Shutdown();
        using var stream = new MemoryStream();
        using (var writer = new WaveFileWriter(new IgnoreDisposeStream(stream), new WaveFormat(8000, 16, 2)))
            writer.WriteSamples(new float[800], 0, 800);
        AudioPlaybackDevice observed = null;
        try
        {
            SharedAudioOutput.UseEngineFactory(() =>
            {
                var engine = new TestEngine();
                engine.RegisterCodecFactory(new MiniAudioCodecFactory());
                return engine;
            });
            SharedAudioOutput.UseOutputObserver((_, device) => { observed = device; return null; });
            using var player = new AudioFilePlayer();

            //Act
            player.Load(stream, leaveOpen);
            player.Play();

            //Assert
            Assert.NotNull(observed);
            Assert.True(SharedAudioOutput.TryObserveOutput((_, device) => Assert.Same(observed, device)));
            player.Dispose();
            Assert.Equal(leaveOpen, stream.CanRead);
        }
        finally
        {
            SharedAudioOutput.Shutdown();
            SharedAudioOutput.UseOutputObserver(null);
            SharedAudioOutput.UseEngineFactory(() => new MiniAudioEngine());
        }
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void extension_setup_failure_cleans_up_and_allows_registration_and_retry(bool failConfig)
    {
        //Arrange
        SharedAudioOutput.Shutdown();
        var engine = new TestEngine();
        try
        {
            SharedAudioOutput.UseEngineFactory(() => engine);
            SharedAudioOutput.UseDeviceConfigFactory(_ => failConfig ? throw new IOException("config") : null);
            SharedAudioOutput.UseOutputObserver((_, _) => throw new IOException("observer"));

            //Act / Assert
            var error = Assert.Throws<IOException>(() => SharedAudioOutput.EnsureStarted(48000));
            Assert.Equal(failConfig ? "config" : "observer", error.Message);
            Assert.True(engine.IsDisposed);
            if (!failConfig) Assert.True(engine.Playback.IsDisposed);
            Assert.False(SharedAudioOutput.IsRunning);
            SharedAudioOutput.UseDeviceConfigFactory(null);
            SharedAudioOutput.UseOutputObserver(null);
            SharedAudioOutput.UseEngineFactory(() => new TestEngine());
            Assert.True(SharedAudioOutput.EnsureStarted(48000).IsRunning);
        }
        finally
        {
            SharedAudioOutput.Shutdown();
            SharedAudioOutput.UseDeviceConfigFactory(null);
            SharedAudioOutput.UseOutputObserver(null);
            SharedAudioOutput.UseEngineFactory(() => new MiniAudioEngine());
        }
    }

    [Fact]
    public async Task scoped_observation_protects_the_device_against_concurrent_shutdown()
    {
        //Arrange
        SharedAudioOutput.Shutdown();
        using var observing = new ManualResetEventSlim();
        using var release = new ManualResetEventSlim();
        using var shuttingDown = new ManualResetEventSlim();
        Task read = null;
        Task shutdown = null;
        var cancellation = TestContext.Current.CancellationToken;
        try
        {
            SharedAudioOutput.UseEngineFactory(() => new TestEngine());
            var output = SharedAudioOutput.EnsureStarted(48000);
            read = Task.Run(() => SharedAudioOutput.TryObserveOutput((_, device) =>
            {
                observing.Set();
                release.Wait(cancellation);
                Assert.False(device.IsDisposed);
            }), cancellation);
            Assert.True(observing.Wait(TimeSpan.FromSeconds(5), cancellation));

            //Act
            shutdown = Task.Run(() => { shuttingDown.Set(); SharedAudioOutput.Shutdown(); }, cancellation);
            Assert.True(shuttingDown.Wait(TimeSpan.FromSeconds(5), cancellation));

            //Assert
            Assert.False(output.IsDisposed);
            Assert.False(shutdown.IsCompleted);
            release.Set();
            await Task.WhenAll(read, shutdown).WaitAsync(TimeSpan.FromSeconds(5), cancellation);
            Assert.True(output.IsDisposed);
        }
        finally
        {
            release.Set();
            if (read != null) await read.WaitAsync(TimeSpan.FromSeconds(5), cancellation);
            if (shutdown != null) await shutdown.WaitAsync(TimeSpan.FromSeconds(5), cancellation);
            SharedAudioOutput.Shutdown();
            SharedAudioOutput.UseEngineFactory(() => new MiniAudioEngine());
        }
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
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
        { Playback = new Playback(this, format, FailPlaybackStart, config); devices.Add(Playback); return Playback; }
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
        internal Playback(AudioEngine engine, AudioFormat format, bool fail, DeviceConfig config = null) : base(engine, format, config ?? new Options()) { this.fail = fail; }
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
