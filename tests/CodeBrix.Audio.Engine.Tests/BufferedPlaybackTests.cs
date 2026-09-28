using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Engine.Abstracts;
using CodeBrix.Audio.Engine.Backends.MiniAudio;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Engine.Providers;
using CodeBrix.Audio.Engine.Structs;
using Xunit;

namespace CodeBrix.Audio.Engine.Tests;

public class BufferedPlaybackTests
{
    private static AudioFormat Format => new AudioFormat
    {
        Channels = 2, SampleRate = 8000, Format = SampleFormat.F32,
        Layout = AudioFormat.GetLayoutFromChannels(2)
    };

    [Fact]
    public void prepared_custom_loops_are_sample_exact_and_allocate_nothing_on_render_thread()
    {
        //Arrange
        using var fixture = new Fixture(32000);
        fixture.Player.SetLoopPoints(100, 300);
        fixture.Player.IsLooping = true;
        fixture.Player.Play();
        var output = new float[5000];
        fixture.Player.Render(output.AsSpan(0, 2)); // JIT the path before measuring.
        fixture.Player.Seek(0);
        fixture.Decoder.Threads.Clear();

        //Act
        long before = GC.GetAllocatedBytesForCurrentThread();
        fixture.Player.Render(output);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        //Assert
        Assert.Equal(0, allocated);
        for (int i = 0; i < output.Length; i++)
            Assert.Equal(Value(i < 300 ? i : 100 + (i - 300) % 200), output[i]);
        Assert.DoesNotContain(Environment.CurrentManagedThreadId, fixture.Decoder.Threads);
        Assert.Equal(PlaybackState.Playing, fixture.Player.State);
        Assert.Equal(200f / 16000, fixture.Player.Time);
    }

    [Fact]
    public void blocked_decoder_does_not_block_render_or_turn_starvation_into_eof()
    {
        //Arrange
        using var fixture = new Fixture(160000);
        fixture.Player.Play();
        fixture.Decoder.AllowDecode.Reset();
        try
        {
            //Act: drain the one-second ring while the producer is unable to refill it.
            fixture.Player.Render(new float[16000]);
            Assert.True(fixture.Decoder.DecodeBlocked.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            var output = Enumerable.Repeat(1f, 256).ToArray();
            float position = fixture.Player.Time;
            fixture.Player.Render(output);

            //Assert
            Assert.All(output, sample => Assert.Equal(0f, sample));
            Assert.Equal(position, fixture.Player.Time);
            Assert.Equal(PlaybackState.Playing, fixture.Player.State);
        }
        finally { fixture.Decoder.AllowDecode.Set(); }

        var next = new float[2];
        Assert.True(SpinWait.SpinUntil(() =>
        {
            fixture.Player.Render(next);
            return next[0] != 0;
        }, TimeSpan.FromSeconds(5)));
        Assert.Equal(Value(16000), next[0]);
    }

    [Fact]
    public async Task seek_prepares_on_worker_while_callback_returns_silence()
    {
        //Arrange
        using var fixture = new Fixture(64000);
        fixture.Player.Play();
        fixture.Decoder.AllowSeek.Reset();
        Task seek = Task.Run(() => fixture.Player.Seek(24000), TestContext.Current.CancellationToken);
        try
        {
            Assert.True(fixture.Decoder.SeekBlocked.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));
            var during = Enumerable.Repeat(1f, 128).ToArray();

            //Act
            fixture.Player.Render(during);

            //Assert
            Assert.All(during, sample => Assert.Equal(0f, sample));
            Assert.False(seek.IsCompleted);
        }
        finally { fixture.Decoder.AllowSeek.Set(); }
        await seek.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
        var after = new float[128];
        fixture.Player.Render(after);
        Assert.Equal(Value(24000), after[0]);
        Assert.Equal(Value(24127), after[127]);
    }

    [Fact]
    public void natural_end_is_raised_once_and_stop_rewinds_without_stale_pcm()
    {
        //Arrange
        using var fixture = new Fixture(300);
        int ended = 0;
        fixture.Player.PlaybackEnded += (_, _) => ended++;
        fixture.Player.Play();
        var output = new float[512];

        //Act
        fixture.Player.Render(output);
        fixture.Player.Render(new float[32]);

        //Assert
        Assert.Equal(1, ended);
        Assert.Equal(PlaybackState.Stopped, fixture.Player.State);
        Assert.Equal(Value(299), output[299]);
        Assert.All(output.Skip(300), sample => Assert.Equal(0f, sample));
        fixture.Player.Stop();
        fixture.Player.Play();
        fixture.Player.Render(output.AsSpan(0, 2));
        Assert.Equal(Value(0), output[0]);
    }

    [Fact]
    public void disabling_loop_discards_prepared_repetitions_and_pause_keeps_position()
    {
        //Arrange
        using var fixture = new Fixture(300);
        fixture.Player.IsLooping = true;
        fixture.Player.Play();
        fixture.Player.Render(new float[400]);
        fixture.Player.Pause();
        float paused = fixture.Player.Time;
        fixture.Player.Render(new float[128]);
        Assert.Equal(paused, fixture.Player.Time);

        //Act
        fixture.Player.IsLooping = false;
        fixture.Player.Play();
        var output = new float[256];
        fixture.Player.Render(output);

        //Assert
        Assert.Equal(Value(100), output[0]);
        Assert.Equal(Value(299), output[199]);
        Assert.All(output.Skip(200), sample => Assert.Equal(0f, sample));
        Assert.Equal(PlaybackState.Stopped, fixture.Player.State);
    }

    [Fact]
    public void failed_seek_is_reported_and_a_later_seek_can_recover()
    {
        //Arrange
        using var fixture = new Fixture(32000);
        fixture.Decoder.FailSeek = true;

        //Act / Assert
        Assert.Throws<InvalidOperationException>(() => fixture.Player.Seek(100));
        fixture.Decoder.FailSeek = false;
        fixture.Player.Seek(200);
        fixture.Player.Play();
        var output = new float[2];
        fixture.Player.Render(output);
        Assert.Equal(Value(200), output[0]);
    }

    [Fact]
    public async Task disposal_waits_for_worker_before_disposing_the_source()
    {
        //Arrange
        var fixture = new Fixture(64000);
        fixture.Player.Play();
        fixture.Decoder.AllowDecode.Reset();
        Task disposing = null;
        try
        {
            fixture.Player.Render(new float[16000]);
            Assert.True(fixture.Decoder.DecodeBlocked.Wait(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken));

            //Act
            disposing = Task.Run(() => fixture.Player.Dispose(), TestContext.Current.CancellationToken);
            await Task.Delay(25, TestContext.Current.CancellationToken);

            //Assert
            Assert.False(fixture.Decoder.IsDisposed);
            Assert.False(disposing.IsCompleted);
        }
        finally
        {
            fixture.Decoder.AllowDecode.Set();
            if (disposing != null) await disposing.WaitAsync(TimeSpan.FromSeconds(5), TestContext.Current.CancellationToken);
            fixture.Dispose();
        }
        Assert.True(fixture.Decoder.IsDisposed);
    }

    private static float Value(int sample) => (sample % 997 + 1) / 2000f;

    [Fact]
    public void repeated_ring_wraps_preserve_every_sample_without_duplicates_or_drops()
    {
        //Arrange
        using var fixture = new Fixture(128000);
        fixture.Player.Play();
        var block = new float[734];
        int consumed = 0;
        var deadline = Stopwatch.StartNew();

        //Act / Assert: deliberately run faster than real time. Silence from starvation is not
        // source PCM (the fixture has no zero samples), and must never advance the source.
        while (fixture.Player.State == PlaybackState.Playing && deadline.Elapsed < TimeSpan.FromSeconds(10))
        {
            fixture.Player.Render(block);
            foreach (float sample in block)
            {
                if (sample == 0) continue;
                Assert.Equal(Value(consumed), sample);
                consumed++;
            }
            Thread.Yield();
        }
        Assert.Equal(128000, consumed);
        Assert.Equal(PlaybackState.Stopped, fixture.Player.State);
    }

    [Fact]
    public void empty_loop_ends_instead_of_spinning_and_decoder_errors_are_observable()
    {
        //Arrange
        using (var empty = new Fixture(0))
        {
            //Act
            empty.Player.IsLooping = true;
            empty.Player.Play();
            empty.Player.Render(new float[32]);

            //Assert
            Assert.Equal(PlaybackState.Stopped, empty.Player.State);
        }

        using var fixture = new Fixture(64000);
        fixture.Player.Play();
        fixture.Decoder.FailDecode = true;
        fixture.Player.Render(new float[16000]);
        Assert.True(SpinWait.SpinUntil(() =>
        {
            try { fixture.Player.Render(new float[256]); return false; }
            catch (IOException error) { return error.Message == "test decode failure"; }
        }, TimeSpan.FromSeconds(5)));
    }

    [Theory]
    [InlineData(0.75f)]
    [InlineData(1.5f)]
    public void speed_adjusted_streaming_reaches_eof_and_can_restart(float speed)
    {
        //Arrange
        using var fixture = new Fixture(12000);
        fixture.Player.PlaybackSpeed = speed;
        fixture.Player.Play();
        var output = new float[256];
        bool sounded = false;

        //Act
        for (int i = 0; i < 200 && fixture.Player.State == PlaybackState.Playing; i++)
        {
            fixture.Player.Render(output);
            sounded |= output.Any(sample => sample != 0);
        }

        //Assert
        Assert.True(sounded);
        Assert.Equal(PlaybackState.Stopped, fixture.Player.State);
        fixture.Player.Stop();
        fixture.Player.PlaybackSpeed = 1;
        fixture.Player.Play();
        fixture.Player.Render(output);
        Assert.Equal(Value(0), output[0]);
    }

    private sealed class TestPlayer(AudioEngine engine, ChunkedDataProvider provider)
        : SoundPlayerBase(engine, BufferedPlaybackTests.Format, provider)
    {
        public void Render(Span<float> output) => GenerateAudio(output, 2);
    }

    private sealed class Fixture : IDisposable
    {
        private readonly MiniAudioEngine engine = new MiniAudioEngine();
        internal readonly Decoder Decoder;
        internal readonly TestPlayer Player;

        internal Fixture(int length)
        {
            Decoder = new Decoder(length);
            engine.RegisterCodecFactory(new Factory(Decoder));
            var stream = new MemoryStream(TestAudio.BuildSineWavPcm16(8000, 2, length / 2));
            Player = new TestPlayer(engine, new ChunkedDataProvider(engine, Format, stream, 64));
        }

        public void Dispose()
        {
            Decoder.AllowDecode.Set();
            Decoder.AllowSeek.Set();
            Player.Dispose();
            engine.Dispose();
        }
    }

    private sealed class Factory(Decoder decoder) : ICodecFactory
    {
        public string FactoryId => "Test.BufferedPlayback";
        public IReadOnlyCollection<string> SupportedFormatIds => new[] { "wav" };
        public int Priority => 1000;
        public ISoundDecoder CreateDecoder(Stream stream, string id, AudioFormat format) => decoder;
        public ISoundDecoder TryCreateDecoder(Stream stream, out AudioFormat detectedFormat, AudioFormat? hintFormat = null)
        { detectedFormat = Format; return decoder; }
        public ISoundEncoder CreateEncoder(Stream stream, string id, AudioFormat format) => null;
    }

    private sealed class Decoder(int length) : ISoundDecoder
    {
        private int position;
        internal readonly ManualResetEventSlim AllowDecode = new ManualResetEventSlim(true);
        internal readonly ManualResetEventSlim DecodeBlocked = new ManualResetEventSlim(false);
        internal readonly ManualResetEventSlim AllowSeek = new ManualResetEventSlim(true);
        internal readonly ManualResetEventSlim SeekBlocked = new ManualResetEventSlim(false);
        internal readonly ConcurrentQueue<int> Threads = new ConcurrentQueue<int>();
        internal bool FailSeek;
        internal volatile bool FailDecode;
        public int Length => length;
        public bool IsDisposed { get; private set; }
        public SampleFormat SampleFormat => SampleFormat.F32;
        public int Channels => 2;
        public int SampleRate => 8000;
        public event EventHandler<EventArgs> EndOfStreamReached { add { } remove { } }
        public bool Seek(int offset)
        {
            Threads.Enqueue(Environment.CurrentManagedThreadId);
            if (!AllowSeek.IsSet) { SeekBlocked.Set(); AllowSeek.Wait(); }
            if (FailSeek) return false;
            position = offset;
            return true;
        }
        public int Decode(Span<float> samples)
        {
            Threads.Enqueue(Environment.CurrentManagedThreadId);
            if (FailDecode) throw new IOException("test decode failure");
            if (!AllowDecode.IsSet) { DecodeBlocked.Set(); AllowDecode.Wait(); }
            int count = Math.Min(samples.Length, length - position);
            for (int i = 0; i < count; i++) samples[i] = Value(position++);
            return count;
        }
        public void Dispose() => IsDisposed = true;
    }
}
