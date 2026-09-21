using System;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

/// <summary>
/// The reverberation unit, which became public API so that any synthesizer in the family can send to
/// one shared implementation rather than growing a second one.
/// </summary>
/// <remarks>
/// These are light tests of the surface and of the behaviour a caller depends on - that it is a SEND
/// effect writing the wet signal alone, that the tail decays, and that muting clears it. How it
/// sounds is not pinned: the algorithm is unchanged and <c>SoundFontSynthesizer</c>'s own tests
/// already fence the audio it produces.
/// </remarks>
public class ReverbTests
{
    private const int SampleRate = 44100;
    private const int BlockSize = 64;

    [Fact]
    public void a_silent_input_produces_a_silent_output()
    {
        //Arrange
        Reverb reverb = new Reverb(SampleRate);
        var (input, left, right) = Buffers();

        //Act
        reverb.Process(input, left, right);

        //Assert
        Peak(left).Should().Be(0f);
        Peak(right).Should().Be(0f);
    }

    [Fact]
    public void an_impulse_leaves_a_tail_that_goes_on_long_after_the_input_has_stopped()
    {
        //Arrange
        Reverb reverb = new Reverb(SampleRate);
        var (input, left, right) = Buffers();

        input[0] = 1f;

        //Act
        // The shortest comb delay is over a thousand samples, so nothing at all comes back inside
        // the first block: a reverb's first reflection arrives after the room is crossed.
        reverb.Process(input, left, right);
        Array.Clear(input, 0, input.Length);

        float early = 0f;
        float late = 0f;

        // Half a second of silence after the impulse, in blocks.
        int blocks = SampleRate / 2 / BlockSize;

        for (int block = 0; block < blocks; block++)
        {
            reverb.Process(input, left, right);

            float level = Peak(left);
            if (block < blocks / 8 && level > early) { early = level; }
            if (block >= blocks - (blocks / 8) && level > late) { late = level; }
        }

        //Assert
        early.Should().BeGreaterThan(0f);
        late.Should().BeGreaterThan(0f);
        late.Should().BeLessThan(early);
    }

    [Fact]
    public void muting_clears_the_tail_without_disturbing_the_settings()
    {
        //Arrange
        Reverb reverb = new Reverb(SampleRate) { RoomSize = 0.8f, Damp = 0.3f };
        var (input, left, right) = Buffers();

        input[0] = 1f;
        reverb.Process(input, left, right);
        Array.Clear(input, 0, input.Length);

        //Act
        reverb.Mute();
        reverb.Process(input, left, right);

        //Assert
        Peak(left).Should().Be(0f);
        Peak(right).Should().Be(0f);
        reverb.RoomSize.Should().BeApproximately(0.8f, 1e-4f);
        reverb.Damp.Should().BeApproximately(0.3f, 1e-4f);
    }

    [Fact]
    public void the_count_overload_leaves_the_rest_of_the_buffers_alone()
    {
        //Arrange
        Reverb reverb = new Reverb(SampleRate);
        var (input, left, right) = Buffers();

        for (int i = 0; i < input.Length; i++) { input[i] = 0.5f; }
        for (int i = 0; i < left.Length; i++) { left[i] = -3f; right[i] = -3f; }

        //Act
        reverb.Process(input, left, right, 16);

        //Assert
        left[15].Should().NotBe(-3f);
        left[16].Should().Be(-3f);
        right[16].Should().Be(-3f);
    }

    [Fact]
    public void a_count_of_zero_does_nothing_at_all()
    {
        //Arrange
        Reverb reverb = new Reverb(SampleRate);
        var (input, left, right) = Buffers();

        input[0] = 1f;
        for (int i = 0; i < left.Length; i++) { left[i] = 7f; }

        //Act
        reverb.Process(input, left, right, 0);

        //Assert
        left[0].Should().Be(7f);
    }

    [Fact]
    public void the_input_gain_is_the_small_figure_the_algorithm_expects_to_be_fed_at()
    {
        //Arrange
        Reverb reverb = new Reverb(SampleRate);

        //Act
        //Assert
        // A recirculating comb network has to be fed well below full scale or it runs away.
        reverb.InputGain.Should().BeGreaterThan(0f);
        reverb.InputGain.Should().BeLessThan(0.1f);
    }

    [Fact]
    public void the_settings_read_back_what_was_written_to_them()
    {
        //Arrange
        Reverb reverb = new Reverb(SampleRate);

        //Act
        reverb.RoomSize = 0.25f;
        reverb.Damp = 0.75f;
        reverb.Wet = 0.5f;
        reverb.Width = 0.4f;

        //Assert
        reverb.RoomSize.Should().BeApproximately(0.25f, 1e-4f);
        reverb.Damp.Should().BeApproximately(0.75f, 1e-4f);
        reverb.Wet.Should().BeApproximately(0.5f, 1e-4f);
        reverb.Width.Should().BeApproximately(0.4f, 1e-4f);
    }

    [Fact]
    public void a_width_of_zero_collapses_the_tail_to_the_middle()
    {
        //Arrange
        Reverb reverb = new Reverb(SampleRate) { Width = 0f };
        var (input, left, right) = Buffers();

        input[0] = 1f;

        //Act
        reverb.Process(input, left, right);
        Array.Clear(input, 0, input.Length);

        for (int block = 0; block < 64; block++) { reverb.Process(input, left, right); }

        //Assert
        for (int i = 0; i < left.Length; i++)
        {
            ((double)left[i]).Should().BeApproximately(right[i], 1e-6);
        }
    }

    [Fact]
    public void a_wet_level_of_zero_is_silence()
    {
        //Arrange
        Reverb reverb = new Reverb(SampleRate) { Wet = 0f };
        var (input, left, right) = Buffers();

        input[0] = 1f;

        //Act
        reverb.Process(input, left, right);

        //Assert
        Peak(left).Should().Be(0f);
        Peak(right).Should().Be(0f);
    }

    private static (float[] Input, float[] Left, float[] Right) Buffers() =>
        (new float[BlockSize], new float[BlockSize], new float[BlockSize]);

    private static float Peak(float[] samples)
    {
        float peak = 0f;

        for (int i = 0; i < samples.Length; i++)
        {
            float value = Math.Abs(samples[i]);
            if (value > peak) { peak = value; }
        }

        return peak;
    }
}
