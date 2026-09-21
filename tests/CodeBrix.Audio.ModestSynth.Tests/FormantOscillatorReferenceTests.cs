using System;
using CodeBrix.Audio.ModestSynth.Oscillators;
using SilverAssertions;
using SilverAssertions.Numeric;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// THE FENCE ON THE FORMANT WAVEFORM'S SOUND: <see cref="FormantOscillator" /> renders its vowel
/// spectrum with one rotating vector per partial instead of one <see cref="Math.Sin" /> per partial
/// per sample, and these tests hold it to <see cref="NaiveFormant" /> - the original sum, written
/// out in full - SAMPLE BY SAMPLE.
/// </summary>
/// <remarks>
/// <para>
/// THE TOLERANCE IS <see cref="Tolerance" />, one millionth of full scale, about 120 dB down. It is
/// chosen from what the two forms actually do rather than from what would be inaudible: they agree
/// to within ONE ULP OF THE FLOAT THEY ARE BOTH ROUNDED TO, and this waveform's peak stays under 4,
/// where a float's ulp is 2.4e-7. So the tolerance is a shade over four times the largest gap the
/// arithmetic can produce, and roughly a hundred times tighter than anything a listener, a 16-bit
/// file or a single-precision FFT could resolve.
/// <see cref="Render_is_never_further_from_the_naive_sum_than_one_float_step" /> states the sharper
/// claim directly.
/// </para>
/// <para>
/// A RECURRENCE GOES WRONG WHERE THE PITCH MOVES, so most of what is here moves it: a glide, a
/// vibrato, a sweep that pushes partials through Nyquist and back, a phase reset in the middle of a
/// note, block lengths that change every block, and a note held long enough to cross two hundred of
/// the oscillator's internal refreshes. Every case drives BOTH oscillators through the same
/// schedule and compares what came out.
/// </para>
/// </remarks>
public class FormantOscillatorReferenceTests
{
    /// <summary>The largest difference from the naive sum any of these tests will accept.</summary>
    private const double Tolerance = 1.0e-6;

    // One step between neighbouring floats anywhere below 4, which is where this waveform peaks.
    private const double FloatStepBelowFour = 1.0 / (1 << 22);

    private const int SampleRate = 44100;
    private const int BlockSize = 64;

    [Theory]
    [InlineData(27.5)]        // A0 - the bottom of a piano, 512 partials, the most there can be
    [InlineData(65.40639)]    // C2
    [InlineData(130.8128)]    // C3 - the bottom of a choir part
    [InlineData(261.6256)]    // C4 - middle C, the pitch the spectrum was measured at
    [InlineData(440.0)]       // A4
    [InlineData(1046.502)]    // C6 - the top of a choir part
    [InlineData(2093.005)]    // C7
    [InlineData(4186.009)]    // C8 - the top of a piano, five partials left below Nyquist
    public void Render_matches_the_naive_sum_at_every_useful_pitch(double frequency)
    {
        //Act
        double worst = Compare(BlockSize, 3 * SampleRate / BlockSize, (block, fast, naive) =>
        {
            if (block > 0) { return; }

            fast.SetFrequency(frequency);
            naive.SetFrequency(frequency);
        });

        //Assert
        worst.Should().BeLessThanOrEqualTo(Tolerance);
    }

    [Theory]
    [InlineData(22050)]
    [InlineData(48000)]
    [InlineData(96000)]
    public void Render_matches_the_naive_sum_at_any_sample_rate(int rate)
    {
        //Arrange
        FormantOscillator fast = new FormantOscillator();
        NaiveFormant naive = new NaiveFormant();

        fast.SetSampleRate(rate);
        naive.SetSampleRate(rate);
        fast.SetFrequency(196.0);
        naive.SetFrequency(196.0);
        fast.Reset(0.0);
        naive.Reset(0.0);

        //Act
        double worst = Run(fast, naive, BlockSize, 2 * rate / BlockSize, null);

        //Assert
        worst.Should().BeLessThanOrEqualTo(Tolerance);
    }

    [Fact]
    public void Render_matches_the_naive_sum_across_changing_block_sizes()
    {
        //Arrange
        // The reference is rendered in ONE call, because the naive sum reads the master phase and
        // nothing else: however it is chopped up, it produces the same samples. The oscillator is
        // then driven through lengths that land on, inside and well past its internal refresh.
        int[] lengths = { 1, 3, 64, 127, 512, 4096, 4097, 2, 1000, 63, 8192 };
        int total = 0;
        for (int i = 0; i < lengths.Length; i++) { total += lengths[i]; }
        total *= 3;

        FormantOscillator fast = new FormantOscillator();
        NaiveFormant naive = new NaiveFormant();

        fast.SetSampleRate(SampleRate);
        naive.SetSampleRate(SampleRate);
        fast.SetFrequency(146.8324);
        naive.SetFrequency(146.8324);
        fast.Reset(0.0);
        naive.Reset(0.0);

        float[] reference = new float[total];
        float[] rendered = new float[total];

        //Act
        naive.Render(reference);

        int written = 0;
        int step = 0;
        while (written < total)
        {
            int length = lengths[step % lengths.Length];
            if (length > total - written) { length = total - written; }

            fast.Render(rendered.AsSpan(written, length));

            written += length;
            step++;
        }

        //Assert
        Worst(rendered, reference).Should().BeLessThanOrEqualTo(Tolerance);
    }

    [Fact]
    public void Render_matches_the_naive_sum_through_a_pitch_bend_glide()
    {
        //Arrange
        // Two octaves up over two seconds, re-pitched every block: the partial count climbs, every
        // Schroeder phase moves with it, and the rotation has to be re-derived each time.
        int blocks = 2 * SampleRate / BlockSize;

        //Act
        double worst = Compare(BlockSize, blocks, (block, fast, naive) =>
        {
            double frequency = 110.0 * Math.Pow(2.0, 3.0 * block / blocks);

            fast.SetFrequency(frequency);
            naive.SetFrequency(frequency);
        });

        //Assert
        worst.Should().BeLessThanOrEqualTo(Tolerance);
    }

    [Fact]
    public void Render_matches_the_naive_sum_through_a_vibrato()
    {
        //Arrange
        // A hundred cents either side at six a second, which is the shape the choir programs put on
        // this waveform - a small pitch change every single block, for ever.
        int blocks = 3 * SampleRate / BlockSize;

        //Act
        double worst = Compare(BlockSize, blocks, (block, fast, naive) =>
        {
            double seconds = block * (double)BlockSize / SampleRate;
            double cents = 100.0 * Math.Sin(2.0 * Math.PI * 6.0 * seconds);
            double frequency = 220.0 * Math.Pow(2.0, cents / 1200.0);

            fast.SetFrequency(frequency);
            naive.SetFrequency(frequency);
        });

        //Assert
        worst.Should().BeLessThanOrEqualTo(Tolerance);
    }

    [Fact]
    public void Render_matches_the_naive_sum_while_partials_fall_off_the_top_and_come_back()
    {
        //Arrange
        // Up from 2 kHz to 12 kHz and back down. Partials cross Nyquist one at a time, so the
        // active count - and with it every Schroeder phase and every rotation - is rebuilt on most
        // of these blocks, and the count comes back DOWN again on the way home.
        int blocks = 2 * SampleRate / BlockSize;

        //Act
        double worst = Compare(BlockSize, blocks, (block, fast, naive) =>
        {
            double half = blocks / 2.0;
            double travelled = block < half ? block / half : (blocks - block) / half;
            double frequency = 2000.0 + (10000.0 * travelled);

            fast.SetFrequency(frequency);
            naive.SetFrequency(frequency);
        });

        //Assert
        worst.Should().BeLessThanOrEqualTo(Tolerance);
    }

    [Fact]
    public void Render_matches_the_naive_sum_when_the_phase_is_reset_in_the_middle_of_a_note()
    {
        //Arrange
        // Reset is what a note-on does, and it is the one thing that moves the master phase from
        // outside the render. Half of these are deliberately mid-cycle rather than zero.
        int blocks = 2 * SampleRate / BlockSize;

        //Act
        double worst = Compare(BlockSize, blocks, (block, fast, naive) =>
        {
            if (block == 0)
            {
                fast.SetFrequency(329.6276);
                naive.SetFrequency(329.6276);
                return;
            }

            if (block % 97 != 0) { return; }

            double startPhase = (block % 5) * 0.2379;

            fast.Reset(startPhase);
            naive.Reset(startPhase);
        });

        //Assert
        worst.Should().BeLessThanOrEqualTo(Tolerance);
    }

    [Fact]
    public void Render_matches_the_naive_sum_over_a_note_held_for_fifteen_seconds()
    {
        //Arrange
        // The rotation is re-derived from the master phase at least every 4,096 samples, so fifteen
        // seconds crosses about a hundred and sixty of those. Nothing may creep.
        int blocks = 15 * SampleRate / BlockSize;

        //Act
        double worst = Compare(BlockSize, blocks, (block, fast, naive) =>
        {
            if (block > 0) { return; }

            fast.SetFrequency(261.6256);
            naive.SetFrequency(261.6256);
        });

        //Assert
        worst.Should().BeLessThanOrEqualTo(Tolerance);
    }

    [Fact]
    public void Render_is_no_further_from_the_naive_sum_at_the_end_of_a_long_note_than_at_the_start()
    {
        //Arrange
        // The same fifteen seconds, but asking the question drift would answer: is the last second
        // any worse than the first? A recurrence that wandered would say yes.
        FormantOscillator fast = new FormantOscillator();
        NaiveFormant naive = new NaiveFormant();

        fast.SetSampleRate(SampleRate);
        naive.SetSampleRate(SampleRate);
        fast.SetFrequency(174.6141);
        naive.SetFrequency(174.6141);
        fast.Reset(0.0);
        naive.Reset(0.0);

        int blocksPerSecond = SampleRate / BlockSize;

        //Act
        double first = Run(fast, naive, BlockSize, blocksPerSecond, null);
        Run(fast, naive, BlockSize, 13 * blocksPerSecond, null);
        double last = Run(fast, naive, BlockSize, blocksPerSecond, null);

        //Assert
        last.Should().BeLessThanOrEqualTo(Tolerance);
        last.Should().BeLessThanOrEqualTo(Math.Max(first, Tolerance));
    }

    [Fact]
    public void Render_is_never_further_from_the_naive_sum_than_one_float_step()
    {
        //Arrange
        // The sharp version of the claim, and where the tolerance above comes from: the two renders
        // are the SAME NUMBER computed two ways, and all that is left between them is the rounding
        // of a double onto the float that is handed out. This waveform peaks under 4, where a
        // float's step is 2^-22, so no sample may be further than that from its reference.
        FormantOscillator fast = new FormantOscillator();
        NaiveFormant naive = new NaiveFormant();

        fast.SetSampleRate(SampleRate);
        naive.SetSampleRate(SampleRate);
        fast.SetFrequency(261.6256);
        naive.SetFrequency(261.6256);
        fast.Reset(0.0);
        naive.Reset(0.0);

        float[] rendered = new float[BlockSize];
        float[] reference = new float[BlockSize];
        double worst = 0.0;
        double peak = 0.0;

        //Act
        for (int block = 0; block < 5 * SampleRate / BlockSize; block++)
        {
            fast.Render(rendered);
            naive.Render(reference);

            for (int i = 0; i < BlockSize; i++)
            {
                double difference = Math.Abs((double)rendered[i] - reference[i]);
                if (difference > worst) { worst = difference; }
                if (Math.Abs(reference[i]) > peak) { peak = Math.Abs(reference[i]); }
            }
        }

        //Assert
        peak.Should().BeLessThan(4.0);
        worst.Should().BeLessThanOrEqualTo(FloatStepBelowFour);
    }

    [Fact]
    public void ActivePartialCount_matches_the_naive_reference_at_every_pitch()
    {
        //Arrange
        // The band limiting is part of the waveform: a partial at or above Nyquist is not rendered.
        FormantOscillator fast = new FormantOscillator();
        NaiveFormant naive = new NaiveFormant();

        fast.SetSampleRate(SampleRate);
        naive.SetSampleRate(SampleRate);

        //Act & Assert
        for (int semitone = 0; semitone <= 108; semitone++)
        {
            double frequency = 27.5 * Math.Pow(2.0, semitone / 12.0);

            fast.SetFrequency(frequency);
            naive.SetFrequency(frequency);

            fast.ActivePartialCount.Should().Be(naive.ActivePartialCount);
        }
    }

    // Builds a pair at the standard rate and drives them through the same schedule, block by block.
    private static double Compare(
        int blockSize, int blocks, Action<int, FormantOscillator, NaiveFormant> beforeEachBlock)
    {
        FormantOscillator fast = new FormantOscillator();
        NaiveFormant naive = new NaiveFormant();

        fast.SetSampleRate(SampleRate);
        naive.SetSampleRate(SampleRate);
        fast.Reset(0.0);
        naive.Reset(0.0);

        return Run(fast, naive, blockSize, blocks, beforeEachBlock);
    }

    // Renders both, block by block, and returns the largest absolute difference between them.
    private static double Run(
        FormantOscillator fast, NaiveFormant naive, int blockSize, int blocks,
        Action<int, FormantOscillator, NaiveFormant> beforeEachBlock)
    {
        float[] rendered = new float[blockSize];
        float[] reference = new float[blockSize];
        double worst = 0.0;

        for (int block = 0; block < blocks; block++)
        {
            if (beforeEachBlock != null) { beforeEachBlock(block, fast, naive); }

            fast.Render(rendered);
            naive.Render(reference);

            double difference = Worst(rendered, reference);
            if (difference > worst) { worst = difference; }
        }

        return worst;
    }

    private static double Worst(float[] rendered, float[] reference)
    {
        double worst = 0.0;

        for (int i = 0; i < rendered.Length; i++)
        {
            double difference = Math.Abs((double)rendered[i] - reference[i]);
            if (difference > worst) { worst = difference; }
        }

        return worst;
    }
}
