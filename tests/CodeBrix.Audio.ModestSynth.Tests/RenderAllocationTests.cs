using System;
using CodeBrix.Audio.ModestSynth.Oscillators;
using SilverAssertions;
using SilverAssertions.Numeric;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// The audio-thread claim, measured: once an oscillator has been given its sample rate, rendering
/// allocates nothing at all.
/// </summary>
/// <remarks>
/// A managed allocation on the audio thread is a garbage collection waiting to happen, and a
/// garbage collection in an audio callback is an audible drop-out. The check is
/// <c>GC.GetAllocatedBytesForCurrentThread</c> around a warmed-up render, which counts every
/// allocation this thread makes, boxing and closures included, taken through
/// <see cref="AllocationProbe" /> so that a collection on another thread cannot fake a result.
/// </remarks>
public class RenderAllocationTests
{
    private const int BlockLength = 512;
    private const int WarmUpBlocks = 4;
    private const int MeasuredBlocks = 64;

    [Theory]
    [InlineData(ModestWaveform.Sine)]
    [InlineData(ModestWaveform.Saw)]
    [InlineData(ModestWaveform.Square)]
    [InlineData(ModestWaveform.Triangle)]
    [InlineData(ModestWaveform.Noise)]
    [InlineData(ModestWaveform.Pluck1)]
    [InlineData(ModestWaveform.Fm6Op)]
    [InlineData(ModestWaveform.Wavetable)]
    [InlineData(ModestWaveform.Harmonic)]
    public void Render_allocates_nothing_once_it_is_warm(ModestWaveform waveform)
    {
        //Arrange
        IModestOscillator oscillator = ModestOscillatorFactory.Create(waveform);
        oscillator.SetSampleRate(48000);
        oscillator.SetFrequency(220.0);
        oscillator.Reset(0.0);
        float[] block = new float[BlockLength];
        for (int i = 0; i < WarmUpBlocks; i++)
        {
            oscillator.Render(block);
        }

        //Act
        Action work = () =>
        {
            for (int i = 0; i < MeasuredBlocks; i++)
            {
                oscillator.Render(block);
            }
        };

        long allocated = AllocationProbe.LowestBytes(work);

        //Assert
        allocated.Should().Be(0L);
    }

    [Theory]
    [InlineData(ModestWaveform.Sine)]
    [InlineData(ModestWaveform.Saw)]
    [InlineData(ModestWaveform.Square)]
    [InlineData(ModestWaveform.Triangle)]
    [InlineData(ModestWaveform.Noise)]
    [InlineData(ModestWaveform.Pluck1)]
    [InlineData(ModestWaveform.Fm6Op)]
    [InlineData(ModestWaveform.Wavetable)]
    [InlineData(ModestWaveform.Harmonic)]
    public void Reset_and_SetFrequency_allocate_nothing_either(ModestWaveform waveform)
    {
        //Arrange
        // Note-on and pitch changes happen on the audio thread too, so they carry the same rule.
        IModestOscillator oscillator = ModestOscillatorFactory.Create(waveform);
        oscillator.SetSampleRate(48000);
        oscillator.SetFrequency(220.0);
        oscillator.Reset(0.0);
        float[] block = new float[BlockLength];
        oscillator.Render(block);

        //Act
        Action work = () =>
        {
            for (int i = 0; i < MeasuredBlocks; i++)
            {
                oscillator.SetFrequency(220.0 + i);
                oscillator.Reset(i * 0.01);
                oscillator.Render(block);
            }
        };

        long allocated = AllocationProbe.LowestBytes(work);

        //Assert
        allocated.Should().Be(0L);
    }
}
