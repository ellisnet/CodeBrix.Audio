using System;
using CodeBrix.Audio.ModestSynth.Effects;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using SilverAssertions;
using SilverAssertions.Numeric;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Effects;

/// <summary>
/// The audio-thread claim, measured: once an effect has been prepared, processing a block allocates
/// nothing at all.
/// </summary>
/// <remarks>
/// A managed allocation on the audio thread is a garbage collection waiting to happen, and a
/// garbage collection in an audio callback is an audible drop-out. The check is
/// <c>GC.GetAllocatedBytesForCurrentThread</c> around a warmed-up render, which counts every
/// allocation this thread makes, boxing and closures included, taken through
/// <see cref="AllocationProbe" /> so that a collection on another thread cannot fake a result.
/// </remarks>
public class EffectAllocationTests
{
    private const int BlockLength = 64;
    private const int WarmUpBlocks = 4;
    private const int MeasuredBlocks = 64;

    [Theory]
    [InlineData(ModestEffectTypes.Phaser)]
    [InlineData(ModestEffectTypes.PitchShift)]
    [InlineData(ModestEffectTypes.WaveFolder)]
    [InlineData(ModestEffectTypes.WaveShaper)]
    [InlineData(ModestEffectTypes.StereoSimulator)]
    [InlineData(ModestEffectTypes.BitCrusher)]
    [InlineData(ModestEffectTypes.Gate)]
    public void Process_allocates_nothing_once_it_is_prepared(string type)
    {
        //Arrange
        IInstrumentEffect effect = Warm(type);
        float[] left = EffectSignals.Noise(BlockLength, 71u, 0.5);
        float[] right = EffectSignals.Noise(BlockLength, 73u, 0.5);

        //Act
        Action work = () =>
        {
            for (int i = 0; i < MeasuredBlocks; i++)
            {
                effect.Process(left, right, BlockLength);
            }
        };

        long allocated = AllocationProbe.LowestBytes(work);

        //Assert
        allocated.Should().Be(0L);
    }

    [Fact]
    public void The_oversampled_wave_shaper_allocates_nothing_either()
    {
        //Arrange
        WaveShaperEffect effect = new WaveShaperEffect { Drive = 30.0, HighQuality = true, Mix = 1.0 };
        effect.Prepare(EffectSignals.SampleRate);

        float[] left = EffectSignals.Noise(BlockLength, 79u, 0.5);
        float[] right = EffectSignals.Noise(BlockLength, 83u, 0.5);

        for (int i = 0; i < WarmUpBlocks; i++) { effect.Process(left, right, BlockLength); }

        //Act
        Action work = () =>
        {
            for (int i = 0; i < MeasuredBlocks; i++)
            {
                effect.Process(left, right, BlockLength);
            }
        };

        long allocated = AllocationProbe.LowestBytes(work);

        //Assert
        allocated.Should().Be(0L);
    }

    [Theory]
    [InlineData(ModestEffectTypes.Phaser)]
    [InlineData(ModestEffectTypes.PitchShift)]
    [InlineData(ModestEffectTypes.WaveFolder)]
    [InlineData(ModestEffectTypes.WaveShaper)]
    [InlineData(ModestEffectTypes.StereoSimulator)]
    [InlineData(ModestEffectTypes.BitCrusher)]
    [InlineData(ModestEffectTypes.Gate)]
    public void A_block_of_silence_stays_silent(string type)
    {
        //Arrange
        IInstrumentEffect effect = Warm(type);
        effect.Reset();
        float[] left = new float[BlockLength];
        float[] right = new float[BlockLength];

        //Act
        for (int i = 0; i < MeasuredBlocks; i++)
        {
            Array.Clear(left, 0, left.Length);
            Array.Clear(right, 0, right.Length);
            effect.Process(left, right, BlockLength);

            //Assert
            for (int n = 0; n < BlockLength; n++)
            {
                Math.Abs((double)left[n]).Should().BeLessThan(1.0e-9);
                Math.Abs((double)right[n]).Should().BeLessThan(1.0e-9);
            }
        }
    }

    private static IInstrumentEffect Warm(string type)
    {
        IInstrumentEffect effect = ModestEffectFactory.Create(type);
        effect.Prepare(EffectSignals.SampleRate);

        float[] left = EffectSignals.Noise(BlockLength, 89u, 0.5);
        float[] right = EffectSignals.Noise(BlockLength, 97u, 0.5);

        for (int i = 0; i < WarmUpBlocks; i++) { effect.Process(left, right, BlockLength); }

        return effect;
    }
}
