using System;
using System.Threading;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Effects;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Effects;

/// <summary>
/// The convolution effect: partitioned overlap-save convolution against an impulse response read from a
/// file beside the preset.
/// </summary>
public class DecentSamplerConvolutionEffectTests
{
    [Fact]
    public void it_equals_the_direct_convolution_of_a_short_response_sample_for_sample()
    {
        //Arrange
        using var world = new ConvolutionWorld([0.5f, -0.25f, 0.125f, 0f, 0.0625f]);
        var input = DecentSamplerEffectHarness.Sine(220, 0.05);

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(world.Effect("1.0"), input);
        var expected = Direct(input, world.Impulse);

        //Assert - zero latency, because the effect is driven a whole block at a time.
        for (var i = 0; i < input.Length; i++)
        {
            left[i].Should().BeApproximately(expected[i], 1.0e-4f);
        }
    }

    [Fact]
    public void it_convolves_a_response_that_spans_several_partitions()
    {
        //Arrange - three blocks long, so the frequency-domain delay line has to hold three partitions.
        var impulse = new float[DecentSamplerEffectHarness.BlockSize * 3];
        for (var i = 0; i < impulse.Length; i++)
        {
            impulse[i] = (float)(Math.Sin(i * 0.3) * Math.Exp(-i / 60.0) * 0.5);
        }

        using var world = new ConvolutionWorld(impulse);
        var input = DecentSamplerEffectHarness.Impulse(DecentSamplerEffectHarness.BlockSize * 8);

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(world.Effect("1.0"), input);

        //Assert - convolving an impulse with the response gives the response back.
        for (var i = 0; i < impulse.Length; i++)
        {
            left[i].Should().BeApproximately(impulse[i], 1.0e-4f);
        }
    }

    [Fact]
    public void a_stereo_response_drives_each_channel_from_its_own_side()
    {
        //Arrange
        using var world = new ConvolutionWorld([0.5f], [0.25f]);
        var input = DecentSamplerEffectHarness.Impulse(DecentSamplerEffectHarness.BlockSize * 2);

        //Act
        var (left, right) = DecentSamplerEffectHarness.Run(world.Effect("1.0"), input);

        //Assert
        left[0].Should().BeApproximately(0.5f, 1.0e-5f);
        right[0].Should().BeApproximately(0.25f, 1.0e-5f);
    }

    [Fact]
    public void mix_crossfades_the_convolution_against_the_dry_signal()
    {
        //Arrange
        using var world = new ConvolutionWorld([0.5f]);
        var input = DecentSamplerEffectHarness.Impulse(DecentSamplerEffectHarness.BlockSize * 2);

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(world.Effect("0.25"), input);

        //Assert
        left[0].Should().BeApproximately(0.75f * 1f + 0.25f * 0.5f, 1.0e-5f);
    }

    [Fact]
    public void a_missing_impulse_response_is_reported_and_the_signal_passes_through()
    {
        //Arrange
        using var world = new ConvolutionWorld([0.5f]);
        var effect = world.Effect("1.0", irFile: "Samples/not-here.wav");
        var input = DecentSamplerEffectHarness.Impulse(DecentSamplerEffectHarness.BlockSize);

        //Act
        var (left, _) = DecentSamplerEffectHarness.Run(effect, input);

        //Assert
        ((DecentSamplerConvolutionEffect)effect).Problems.Should().ContainSingle();
        left[0].Should().Be(1f);
    }

    [Fact]
    public void rebinding_the_impulse_response_file_reloads_it()
    {
        //Arrange
        using var world = new ConvolutionWorld([0.5f]);
        world.WriteImpulse("Samples/other.wav", [0.125f]);

        var effect = (DecentSamplerConvolutionEffect)world.Effect("1.0");
        var input = DecentSamplerEffectHarness.Impulse(DecentSamplerEffectHarness.BlockSize * 2);

        //Act - a binding writes the new path; the effect notices it and loads it off the audio thread.
        effect.TrySetParameter("FX_IR_FILE", "Samples/other.wav");
        DecentSamplerEffectHarness.Run(effect, input);
        WaitForLoad(effect);

        var (left, _) = DecentSamplerEffectHarness.Run(effect, input);

        //Assert
        effect.ImpulsePath.Should().Be("Samples/other.wav");
        left[0].Should().BeApproximately(0.125f, 1.0e-5f);
    }

    private static void WaitForLoad(DecentSamplerConvolutionEffect effect)
    {
        for (var i = 0; i < 500 && effect.IsLoading; i++)
        {
            Thread.Sleep(5);
        }
    }

    private static float[] Direct(float[] input, float[] impulse)
    {
        var output = new float[input.Length];

        for (var i = 0; i < input.Length; i++)
        {
            var sum = 0.0;

            for (var k = 0; k < impulse.Length && k <= i; k++)
            {
                sum += (double)input[i - k] * impulse[k];
            }

            output[i] = (float)sum;
        }

        return output;
    }

    // A one-preset library with an impulse response file beside it, and the effect that reads it.
    private sealed class ConvolutionWorld : IDisposable
    {
        private readonly DecentSamplerEngineFixtures _fixtures = DecentSamplerEngineFixtures.Create();
        private readonly DecentSamplerInstrument _instrument;

        public ConvolutionWorld(float[] impulse, float[] rightImpulse = null)
        {
            Impulse = impulse;

            if (rightImpulse == null)
            {
                WriteImpulse("Samples/ir.wav", impulse);
            }
            else
            {
                _fixtures.WriteStereoImpulseWav("Samples/ir.wav", impulse, rightImpulse);
            }

            _fixtures.WriteConstantWav("Samples/note.wav", 0.5f, 1000);

            _instrument = _fixtures.LoadPreset("""
                <DecentSampler>
                  <groups>
                    <group>
                      <sample path="Samples/note.wav" rootNote="60" loNote="60" hiNote="60" />
                    </group>
                  </groups>
                </DecentSampler>
                """);
        }

        public float[] Impulse { get; }

        public void WriteImpulse(string path, float[] samples) =>
            _fixtures.WriteRawWav(path, samples);

        public IInstrumentEffect Effect(string mix, string irFile = "Samples/ir.wav")
        {
            var element = DecentSamplerEffectHarness.Element(
                $"<effect type=\"convolution\" mix=\"{mix}\" irFile=\"{irFile}\" />");

            DecentSamplerExtensions.Shared.TryGetEffectFactory("convolution", out var factory);

            var effect = factory(new EffectContext(
                _instrument, element, DecentSamplerEffectHarness.SampleRate,
                DecentSamplerEffectPlacement.Instrument, -1, DecentSamplerEffectHarness.BlockSize, null));

            effect.Prepare(DecentSamplerEffectHarness.SampleRate);
            return effect;
        }

        public void Dispose()
        {
            _instrument.Dispose();
            _fixtures.Dispose();
        }
    }
}
