using System;
using CodeBrix.Audio.ModestSynth.Fm;
using CodeBrix.Audio.ModestSynth.Oscillators;
using CodeBrix.Audio.ModestSynth.Patch;
using SilverAssertions;
using SilverAssertions.Numeric;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Fm;

/// <summary>
/// The six-operator oscillator: what it renders, how the 32 algorithms route it, and how it
/// answers to pitch, detune, velocity and feedback.
/// </summary>
/// <remarks>
/// The routing tests are the load-bearing ones. Silencing every modulator turns each algorithm
/// into nothing but its carriers, so the rendered block can be compared SAMPLE FOR SAMPLE against
/// a sum of plain sines - which checks the carrier list, the evaluation order and the output gain
/// in one assertion, for all 32 algorithms.
/// </remarks>
public class Fm6OpOscillatorTests
{
    private const int Rate = FmSignal.SampleRate;
    private const double Tolerance = 1e-6;

    [Fact]
    public void Waveform_is_the_name_a_preset_writes() =>
        new Fm6OpOscillator().Waveform.Should().Be("fm6op");

    [Fact]
    public void A_new_oscillator_carries_the_formats_defaults()
    {
        //Arrange
        Fm6OpOscillator oscillator = new Fm6OpOscillator();

        //Assert
        oscillator.Algorithm.Should().Be(1);
        oscillator.OutputGain.Should().Be(Fm6OpOscillator.ReferenceOutputGain);
        oscillator.Velocity.Should().Be(127);
        oscillator.IsKeyDown.Should().BeFalse();
        oscillator.IsFinished.Should().BeTrue();
        oscillator.GetOperator(1).Level.Should().Be(1.0);
        for (int number = 2; number <= 6; number++)
        {
            oscillator.GetOperator(number).Level.Should().Be(0.0);
        }
    }

    [Fact]
    public void An_oscillator_with_no_parameters_is_a_pure_sine_at_the_notes_frequency()
    {
        //Arrange
        double frequency = Spectrum.BinFrequency(171);
        Fm6OpOscillator oscillator = Create(1, frequency);
        float[] expected = FmSignal.SumOfSines(new[] { frequency }, new[] { 1.0 },
            Fm6OpOscillator.ReferenceOutputGain, 0.0, 2048);

        //Act
        float[] rendered = Render(oscillator, 2048);

        //Assert
        for (int i = 0; i < rendered.Length; i++)
        {
            ((double)rendered[i]).Should().BeApproximately(expected[i], Tolerance);
        }
    }

    [Fact]
    public void An_oscillator_with_no_parameters_sits_where_the_reference_player_puts_it()
    {
        //Arrange
        double frequency = Spectrum.BinFrequency(171);
        Fm6OpOscillator fm = Create(1, frequency);
        SineOscillator sine = new SineOscillator();
        sine.SetSampleRate(Rate);
        sine.SetFrequency(frequency);

        //Act
        float[] fmBlock = Render(fm, Spectrum.BlockLength);
        float[] sineBlock = Spectrum.RenderBlock(sine);
        double difference = 20.0 * Math.Log10(
            Spectrum.Rms(fmBlock, 0, fmBlock.Length) / Spectrum.Rms(sineBlock, 0, sineBlock.Length));

        //Assert
        difference.Should().BeApproximately(-4.44, 0.02);
    }

    [Fact]
    public void Algorithm_thirty_two_renders_the_sum_of_six_sines()
    {
        //Arrange
        double frequency = Spectrum.BinFrequency(97);
        double[] ratios = { 1.0, 2.0, 3.0, 4.0, 6.0, 8.0 };
        double[] levels = { 0.85, 0.65, 0.45, 0.30, 0.18, 0.10 };
        Fm6OpOscillator oscillator = Create(32, frequency);
        double[] frequencies = new double[6];
        for (int i = 0; i < 6; i++)
        {
            oscillator.GetOperator(i + 1).Ratio = ratios[i];
            oscillator.GetOperator(i + 1).Level = levels[i];
            frequencies[i] = frequency * ratios[i];
        }

        float[] expected = FmSignal.SumOfSines(frequencies, levels,
            Fm6OpOscillator.ReferenceOutputGain, 0.0, 4096);

        //Act
        float[] rendered = Render(oscillator, 4096);

        //Assert
        for (int i = 0; i < rendered.Length; i++)
        {
            ((double)rendered[i]).Should().BeApproximately(expected[i], Tolerance);
        }
    }

    [Theory]
    [MemberData(nameof(Dx7AlgorithmChart.AllAlgorithms), MemberType = typeof(Dx7AlgorithmChart))]
    public void Every_algorithm_renders_exactly_its_carriers_when_the_modulators_are_silent(int number)
    {
        //Arrange
        double frequency = Spectrum.BinFrequency(61);
        Fm6OpOscillator oscillator = Create(number, frequency);
        for (int operatorNumber = 1; operatorNumber <= 6; operatorNumber++)
        {
            ModestFmOperator settings = oscillator.GetOperator(operatorNumber);
            settings.Ratio = 1.0 + (0.5 * (operatorNumber - 1));
            settings.Level = oscillator.Topology.IsCarrier(operatorNumber)
                ? 1.0 - (0.1 * operatorNumber)
                : 0.0;
        }

        int carrierCount = oscillator.Topology.Carriers.Count;
        double[] frequencies = new double[carrierCount];
        double[] levels = new double[carrierCount];
        for (int i = 0; i < carrierCount; i++)
        {
            int carrier = oscillator.Topology.Carriers[i];
            frequencies[i] = frequency * (1.0 + (0.5 * (carrier - 1)));
            levels[i] = 1.0 - (0.1 * carrier);
        }

        float[] expected = FmSignal.SumOfSines(frequencies, levels,
            Fm6OpOscillator.ReferenceOutputGain, 0.0, 1024);

        //Act
        float[] rendered = Render(oscillator, 1024);

        //Assert
        for (int i = 0; i < rendered.Length; i++)
        {
            ((double)rendered[i]).Should().BeApproximately(expected[i], Tolerance);
        }
    }

    [Theory]
    [MemberData(nameof(Dx7AlgorithmChart.AllAlgorithms), MemberType = typeof(Dx7AlgorithmChart))]
    public void Turning_any_operator_down_changes_the_sound(int number)
    {
        //Arrange
        Fm6OpOscillator oscillator = Create(number, Spectrum.BinFrequency(61));
        for (int operatorNumber = 1; operatorNumber <= 6; operatorNumber++)
        {
            oscillator.GetOperator(operatorNumber).Ratio = 1.0 + (0.5 * (operatorNumber - 1));
            oscillator.GetOperator(operatorNumber).Level = 0.7;
        }

        float[] withEverything = Render(oscillator, 1024);

        //Act, Assert - turning any one operator down changes the sound, carrier or modulator.
        for (int operatorNumber = 1; operatorNumber <= 6; operatorNumber++)
        {
            double kept = oscillator.GetOperator(operatorNumber).Level;
            oscillator.GetOperator(operatorNumber).Level = 0.0;
            float[] without = Render(oscillator, 1024);
            oscillator.GetOperator(operatorNumber).Level = kept;

            MaximumDifference(withEverything, without).Should().BeGreaterThan(1e-4);
        }
    }

    [Fact]
    public void One_modulator_puts_the_expected_bessel_sidebands_around_the_carrier()
    {
        //Arrange
        // The carrier sits on bin 513 and the modulator on bin 171, so the sidebands fall on bins
        // 342 and 684 and nothing folds back through zero to land on top of one of them.
        const double ModulatorLevel = 0.1;
        Fm6OpOscillator oscillator = Create(1, Spectrum.BinFrequency(171));
        oscillator.GetOperator(1).Level = 1.0;
        oscillator.GetOperator(1).Ratio = 3.0;
        oscillator.GetOperator(2).Level = ModulatorLevel;
        oscillator.GetOperator(2).Ratio = 1.0;

        double index = 2.0 * Math.PI * ModulatorLevel * Fm6OpOscillator.DefaultModulationDepthCycles;
        double expectedRatio = Math.Abs(FmSignal.BesselJ(0, index)) / Math.Abs(FmSignal.BesselJ(1, index));

        //Act
        double[] magnitudes = Spectrum.Magnitudes(Render(oscillator, Spectrum.BlockLength));
        double carrier = magnitudes[513];
        double lower = magnitudes[342];
        double upper = magnitudes[684];

        //Assert
        Spectrum.PeakBin(magnitudes).Should().Be(513);
        (carrier / upper).Should().BeApproximately(expectedRatio, expectedRatio * 0.02);
        (lower / upper).Should().BeApproximately(1.0, 0.02);
    }

    [Fact]
    public void The_sidebands_are_spaced_by_the_modulator_frequency()
    {
        //Arrange
        Fm6OpOscillator oscillator = Create(1, Spectrum.BinFrequency(171));
        oscillator.GetOperator(1).Level = 1.0;
        oscillator.GetOperator(1).Ratio = 3.0;
        oscillator.GetOperator(2).Level = 0.1;
        oscillator.GetOperator(2).Ratio = 1.0;

        //Act
        double[] magnitudes = Spectrum.Magnitudes(Render(oscillator, Spectrum.BlockLength));
        int strongestSideband = 0;
        double strongest = 0.0;
        for (int bin = 1; bin < magnitudes.Length; bin++)
        {
            if (Math.Abs(bin - 513) <= 3) { continue; }
            if (magnitudes[bin] > strongest) { strongest = magnitudes[bin]; strongestSideband = bin; }
        }

        //Assert - the modulator sits on bin 171, so its sidebands sit 171 bins from the carrier.
        Math.Abs(strongestSideband - 513).Should().Be(171);
    }

    [Fact]
    public void Feedback_brightens_the_tone_the_further_it_is_turned_up()
    {
        //Arrange
        double[] amounts = { 0.0, 0.1, 0.2, 0.4, 0.6 };
        double[] centroids = new double[amounts.Length];

        //Act
        for (int i = 0; i < amounts.Length; i++)
        {
            Fm6OpOscillator oscillator = Create(32, Spectrum.BinFrequency(171));
            for (int number = 1; number <= 5; number++) { oscillator.GetOperator(number).Level = 0.0; }
            oscillator.GetOperator(6).Level = 1.0;
            oscillator.GetOperator(6).Feedback = amounts[i];

            float[] block = Render(oscillator, Spectrum.BlockLength);
            centroids[i] = Spectrum.PowerCentroid(block, block.Length);
        }

        //Assert
        for (int i = 1; i < centroids.Length; i++)
        {
            centroids[i].Should().BeGreaterThan(centroids[i - 1]);
        }
    }

    [Fact]
    public void EffectiveFeedback_comes_from_the_operator_the_algorithm_puts_the_loop_on()
    {
        //Arrange - algorithm 2 puts the loop on operator 2, not operator 6.
        Fm6OpOscillator oscillator = Create(2, 440.0);
        oscillator.GetOperator(2).Feedback = 0.3;
        oscillator.GetOperator(6).Feedback = 0.9;

        //Act
        Render(oscillator, 64);

        //Assert
        oscillator.Topology.FeedbackDestination.Should().Be(2);
        oscillator.EffectiveFeedback.Should().Be(0.3);
    }

    [Fact]
    public void Operator_six_feedback_stands_in_only_when_the_fallback_is_asked_for()
    {
        //Arrange
        Fm6OpOscillator oscillator = Create(2, 440.0);
        oscillator.GetOperator(6).Feedback = 0.5;
        oscillator.UseOperator6FeedbackFallback = true;

        //Act
        Render(oscillator, 64);

        //Assert
        oscillator.EffectiveFeedback.Should().Be(0.5);
    }

    [Fact]
    public void Feedback_written_on_any_operator_but_the_algorithms_own_is_ignored()
    {
        //Arrange
        // MEASURED (round 3, item 42): fmOpNFeedback acts only on the algorithm's own feedback
        // operator - op 2 in algorithm 2 - and writing it anywhere else is silently ignored.
        Fm6OpOscillator oscillator = Create(2, 440.0);
        oscillator.GetOperator(6).Feedback = 0.5;

        //Act
        Render(oscillator, 64);

        //Assert
        oscillator.EffectiveFeedback.Should().Be(0.0);
    }

    [Theory]
    [InlineData(1.0)]
    [InlineData(3.0)]
    [InlineData(7.0)]
    public void Feedback_is_clamped_at_one(double written)
    {
        //Arrange
        // MEASURED: feedback settings of 1, 3 and 7 rendered bit-identically.
        Fm6OpOscillator oscillator = Create(32, 440.0);
        oscillator.GetOperator(6).Feedback = written;

        //Act
        Render(oscillator, 64);

        //Assert
        oscillator.EffectiveFeedback.Should().Be(Fm6OpOscillator.MaximumFeedback);
    }

    [Fact]
    public void Fixed_mode_ignores_the_note_that_was_played()
    {
        //Arrange
        double fixedFrequency = Spectrum.BinFrequency(300);
        Fm6OpOscillator oscillator = Create(32, 220.0);
        for (int number = 2; number <= 6; number++) { oscillator.GetOperator(number).Level = 0.0; }
        oscillator.GetOperator(1).Mode = ModestFmOperatorMode.Fixed;
        oscillator.GetOperator(1).FixedFrequency = fixedFrequency;

        //Act
        int lowNote = Spectrum.PeakBin(Spectrum.Magnitudes(Render(oscillator, Spectrum.BlockLength)));
        oscillator.SetFrequency(880.0);
        int highNote = Spectrum.PeakBin(Spectrum.Magnitudes(Render(oscillator, Spectrum.BlockLength)));

        //Assert
        lowNote.Should().Be(300);
        highNote.Should().Be(300);
        oscillator.GetOperatorFrequencyHz(1).Should().BeApproximately(fixedFrequency, 1e-9);
    }

    [Fact]
    public void Ratio_mode_multiplies_the_note_that_was_played()
    {
        //Arrange
        Fm6OpOscillator oscillator = Create(1, 200.0);
        oscillator.GetOperator(3).Ratio = 3.5;

        //Act, Assert
        oscillator.GetOperatorFrequencyHz(1).Should().BeApproximately(200.0, 1e-9);
        oscillator.GetOperatorFrequencyHz(3).Should().BeApproximately(700.0, 1e-9);
        oscillator.SetFrequency(400.0);
        oscillator.GetOperatorFrequencyHz(3).Should().BeApproximately(1400.0, 1e-9);
    }

    [Theory]
    [InlineData(0, 0.247171142)]
    [InlineData(7, 0.247171142)]
    [InlineData(-7, 0.247171142)]
    [InlineData(2, 0.247171142)]
    public void Detune_shifts_an_operator_by_the_measured_power_law(int detune, double expectedOffset)
    {
        //Arrange
        Fm6OpOscillator oscillator = Create(1, 440.0);
        oscillator.GetOperator(1).Detune = detune;

        //Act, Assert
        oscillator.GetOperatorFrequencyHz(1)
            .Should().BeApproximately(440.0 + (detune * expectedOffset), 1e-6);
    }

    [Fact]
    public void Detune_bites_harder_at_the_bottom_of_the_keyboard()
    {
        //Arrange
        // MEASURED (round 3, item 42): the offset grows as f0^0.63, so in CENTS it falls by a factor
        // of 2^0.37 = 1.29 per octave - four octaves apart is a factor of 2.79.
        Fm6OpOscillator oscillator = Create(1, 55.0);
        oscillator.GetOperator(1).Detune = 7;

        //Act
        double lowCents = 1200.0 * Math.Log2(oscillator.GetOperatorFrequencyHz(1) / 55.0);
        oscillator.SetFrequency(880.0);
        double highCents = 1200.0 * Math.Log2(oscillator.GetOperatorFrequencyHz(1) / 880.0);

        //Assert
        (lowCents / highCents).Should().BeApproximately(Math.Pow(2.0, 4.0 * 0.37), 0.1);
    }

    [Fact]
    public void Two_operators_a_detune_apart_beat_against_each_other()
    {
        //Arrange
        Fm6OpOscillator oscillator = Create(32, 220.0);
        for (int number = 3; number <= 6; number++) { oscillator.GetOperator(number).Level = 0.0; }
        oscillator.GetOperator(1).Level = 0.5;
        oscillator.GetOperator(2).Level = 0.5;

        // Operator 2's ratio defaults to 2, so it has to be brought back to the fundamental before
        // the two can beat at all.
        oscillator.GetOperator(2).Ratio = 1.0;
        oscillator.GetOperator(2).Detune = 7;

        //Act
        // The beat frequency is the detune offset at the operator's own frequency: MEASURED as
        // detune * 0.005341 * f0^0.63, which at 220 Hz is about 1.12 Hz. The sum cancels half a beat
        // cycle in.
        double beat = Dx7Tables.DetuneHz(7, 220.0);
        float[] block = Render(oscillator, Rate);
        double atStart = FmSignal.PeakAround(block, 240, 240);
        double atCancellation = FmSignal.PeakAround(block, (int)(Rate / beat / 2.0), 240);

        //Assert
        atStart.Should().BeGreaterThan(0.5);
        atCancellation.Should().BeLessThan(0.05);
    }

    [Fact]
    public void Velocity_sensitivity_zero_ignores_velocity()
    {
        //Arrange
        Fm6OpOscillator soft = Create(1, 440.0);
        Fm6OpOscillator hard = Create(1, 440.0);

        //Act
        soft.NoteOn(1);
        hard.NoteOn(127);
        float[] softBlock = RenderOnly(soft, 1024);
        float[] hardBlock = RenderOnly(hard, 1024);

        //Assert
        MaximumDifference(softBlock, hardBlock).Should().BeLessThan(1e-9);
    }

    [Fact]
    public void Velocity_sensitivity_seven_follows_velocity_all_the_way_down()
    {
        //Arrange
        Fm6OpOscillator soft = Create(1, 440.0);
        Fm6OpOscillator hard = Create(1, 440.0);
        soft.GetOperator(1).VelocitySensitivity = 7;
        hard.GetOperator(1).VelocitySensitivity = 7;

        //Act
        soft.NoteOn(1);
        hard.NoteOn(127);
        double softPeak = FmSignal.PeakAround(RenderOnly(soft, 1024), 512, 512);
        double hardPeak = FmSignal.PeakAround(RenderOnly(hard, 1024), 512, 512);

        //Assert
        // MEASURED: velocity 96 is the neutral point, so velocity 127 at sensitivity 7 is +5.3 dB
        // rather than unity, and velocity 1 is silence.
        hardPeak.Should().BeApproximately(
            Fm6OpOscillator.ReferenceOutputGain * Dx7Tables.VelocityScale(7, 127), 0.02);
        softPeak.Should().BeLessThan(hardPeak * 0.001);
    }

    [Fact]
    public void Velocity_sensitivity_on_a_modulator_moves_the_brightness_not_the_level()
    {
        //Arrange
        Fm6OpOscillator soft = Create(1, Spectrum.BinFrequency(171));
        Fm6OpOscillator hard = Create(1, Spectrum.BinFrequency(171));
        foreach (Fm6OpOscillator oscillator in new[] { soft, hard })
        {
            oscillator.GetOperator(2).Level = 0.5;
            oscillator.GetOperator(2).Ratio = 2.0;
            oscillator.GetOperator(2).VelocitySensitivity = 6;
        }

        //Act
        soft.NoteOn(30);
        hard.NoteOn(127);
        double softCentroid = Spectrum.PowerCentroid(RenderOnly(soft, Spectrum.BlockLength), Spectrum.BlockLength);
        double hardCentroid = Spectrum.PowerCentroid(RenderOnly(hard, Spectrum.BlockLength), Spectrum.BlockLength);

        //Assert
        hardCentroid.Should().BeGreaterThan(softCentroid * 1.5);
    }

    [Fact]
    public void The_same_settings_render_the_same_samples()
    {
        //Arrange
        Fm6OpOscillator first = Configured();
        Fm6OpOscillator second = Configured();

        //Act
        float[] a = Render(first, 4096);
        float[] b = Render(second, 4096);

        //Assert
        MaximumDifference(a, b).Should().Be(0.0);
    }

    [Fact]
    public void Render_allocates_nothing_once_it_is_warm()
    {
        //Arrange
        Fm6OpOscillator oscillator = Configured();
        float[] block = new float[512];
        oscillator.Reset(0.0);
        for (int i = 0; i < 4; i++) { oscillator.Render(block); }

        //Act
        Action work = () =>
        {
            for (int i = 0; i < 64; i++) { oscillator.Render(block); }
        };

        long allocated = AllocationProbe.LowestBytes(work);

        //Assert
        allocated.Should().Be(0L);
    }

    [Fact]
    public void A_voice_that_was_never_started_renders_silence()
    {
        //Arrange
        Fm6OpOscillator oscillator = Create(1, 440.0);

        //Act
        float[] block = RenderOnly(oscillator, 512);

        //Assert
        oscillator.IsFinished.Should().BeTrue();
        FmSignal.PeakAround(block, 256, 256).Should().Be(0.0);
    }

    [Fact]
    public void NoteOn_starts_the_voice_and_NoteOff_releases_it()
    {
        //Arrange
        Fm6OpOscillator oscillator = Create(1, 440.0);
        oscillator.GetOperator(1).Release = 0.05;

        //Act, Assert
        oscillator.NoteOn(100);
        oscillator.IsKeyDown.Should().BeTrue();
        oscillator.IsFinished.Should().BeFalse();
        oscillator.Velocity.Should().Be(100);

        RenderOnly(oscillator, 1024);
        oscillator.NoteOff();
        oscillator.IsKeyDown.Should().BeFalse();
        oscillator.IsFinished.Should().BeFalse();

        RenderOnly(oscillator, Rate / 4);
        oscillator.IsFinished.Should().BeTrue();
    }

    [Theory]
    [InlineData(-40, 0)]
    [InlineData(200, 127)]
    public void NoteOn_clamps_the_velocity(int given, int expected)
    {
        //Arrange
        Fm6OpOscillator oscillator = Create(1, 440.0);

        //Act
        oscillator.NoteOn(given);

        //Assert
        oscillator.Velocity.Should().Be(expected);
    }

    [Fact]
    public void Reset_starts_every_operator_at_the_phase_it_is_given()
    {
        //Arrange
        double frequency = Spectrum.BinFrequency(171);
        Fm6OpOscillator oscillator = Create(32, frequency);
        oscillator.GetOperator(2).Level = 0.5;
        oscillator.GetOperator(2).Ratio = 2.0;
        float[] expected = FmSignal.SumOfSines(new[] { frequency, frequency * 2.0 },
            new[] { 1.0, 0.5 }, Fm6OpOscillator.ReferenceOutputGain, 0.25, 512);

        //Act
        oscillator.Reset(0.25);
        float[] rendered = RenderOnly(oscillator, 512);

        //Assert
        for (int i = 0; i < rendered.Length; i++)
        {
            ((double)rendered[i]).Should().BeApproximately(expected[i], Tolerance);
        }
    }

    [Theory]
    [InlineData(-3, 1)]
    [InlineData(0, 1)]
    [InlineData(19, 19)]
    [InlineData(33, 32)]
    public void The_algorithm_number_is_clamped_rather_than_rejected(int given, int expected)
    {
        //Arrange
        Fm6OpOscillator oscillator = new Fm6OpOscillator();

        //Act
        oscillator.Algorithm = given;

        //Assert
        oscillator.Algorithm.Should().Be(expected);
        oscillator.Topology.Number.Should().Be(expected);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void GetOperator_rejects_an_operator_that_does_not_exist(int number)
    {
        //Act
        Action act = () => new Fm6OpOscillator().GetOperator(number);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void The_operators_belong_to_the_voice_not_to_the_patch()
    {
        //Arrange
        ModestPatch patch = new ModestPatch { Waveform = ModestWaveform.Fm6Op, FmAlgorithm = 5 };
        patch.GetFmOperator(2).Level = 0.4;

        //Act
        Fm6OpOscillator first = (Fm6OpOscillator)patch.CreateOscillator(Rate);
        Fm6OpOscillator second = (Fm6OpOscillator)patch.CreateOscillator(Rate);
        first.GetOperator(2).Level = 0.9;

        //Assert
        first.Algorithm.Should().Be(5);
        second.Algorithm.Should().Be(5);
        second.GetOperator(2).Level.Should().Be(0.4);
        patch.GetFmOperator(2).Level.Should().Be(0.4);
    }

    [Fact]
    public void ApplyPatch_copies_every_operator_parameter()
    {
        //Arrange
        ModestPatch patch = new ModestPatch { FmAlgorithm = 7 };
        ModestFmOperator source = patch.GetFmOperator(4);
        source.Ratio = 3.5;
        source.Detune = -4;
        source.Mode = ModestFmOperatorMode.Fixed;
        source.FixedFrequency = 987.5;
        source.Level = 0.42;
        source.VelocitySensitivity = 5;
        source.Feedback = 0.25;
        source.Attack = 0.01;
        source.Decay = 0.2;
        source.Sustain = 0.3;
        source.Release = 0.4;
        source.EnvelopeType = ModestFmEnvelopeType.Dx7;
        source.EgRate1 = 90;
        source.EgLevel4 = 12;

        //Act
        Fm6OpOscillator oscillator = new Fm6OpOscillator();
        oscillator.ApplyPatch(patch);
        ModestFmOperator copy = oscillator.GetOperator(4);

        //Assert
        oscillator.Algorithm.Should().Be(7);
        copy.Number.Should().Be(4);
        copy.Ratio.Should().Be(3.5);
        copy.Detune.Should().Be(-4);
        copy.Mode.Should().Be(ModestFmOperatorMode.Fixed);
        copy.FixedFrequency.Should().Be(987.5);
        copy.Level.Should().Be(0.42);
        copy.VelocitySensitivity.Should().Be(5);
        copy.Feedback.Should().Be(0.25);
        copy.Attack.Should().Be(0.01);
        copy.Decay.Should().Be(0.2);
        copy.Sustain.Should().Be(0.3);
        copy.Release.Should().Be(0.4);
        copy.EnvelopeType.Should().Be(ModestFmEnvelopeType.Dx7);
        copy.EgRate1.Should().Be(90);
        copy.EgLevel4.Should().Be(12);
    }

    [Fact]
    public void ApplyPatch_rejects_a_missing_patch()
    {
        //Act
        Action act = () => new Fm6OpOscillator().ApplyPatch(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void An_empty_block_is_a_no_op()
    {
        //Arrange
        Fm6OpOscillator oscillator = Configured();
        oscillator.Reset(0.0);

        //Act
        Action act = () => oscillator.Render(Span<float>.Empty);

        //Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void OutputGain_and_the_depths_ignore_a_value_that_makes_no_sense()
    {
        //Arrange
        Fm6OpOscillator oscillator = new Fm6OpOscillator();

        //Act
        oscillator.OutputGain = -1.0;
        oscillator.ModulationDepthCycles = double.NaN;
        oscillator.FeedbackDepthCycles = double.PositiveInfinity;
        oscillator.RateScaling = 99;

        //Assert
        oscillator.OutputGain.Should().Be(Fm6OpOscillator.ReferenceOutputGain);
        oscillator.ModulationDepthCycles.Should().Be(Fm6OpOscillator.DefaultModulationDepthCycles);
        oscillator.FeedbackDepthCycles.Should().Be(Fm6OpOscillator.DefaultFeedbackDepthCycles);
        oscillator.RateScaling.Should().Be(7);
    }

    [Fact]
    public void ModulationDepthCycles_decides_how_bright_a_given_level_is()
    {
        //Arrange
        Fm6OpOscillator shallow = Create(1, Spectrum.BinFrequency(171));
        Fm6OpOscillator deep = Create(1, Spectrum.BinFrequency(171));
        foreach (Fm6OpOscillator oscillator in new[] { shallow, deep })
        {
            oscillator.GetOperator(2).Level = 0.3;
            oscillator.GetOperator(2).Ratio = 2.0;
        }

        deep.ModulationDepthCycles = Fm6OpOscillator.DefaultModulationDepthCycles * 2.0;
        shallow.ModulationDepthCycles = Fm6OpOscillator.DefaultModulationDepthCycles * 0.5;

        //Act
        double shallowCentroid = Spectrum.PowerCentroid(Render(shallow, Spectrum.BlockLength), Spectrum.BlockLength);
        double deepCentroid = Spectrum.PowerCentroid(Render(deep, Spectrum.BlockLength), Spectrum.BlockLength);

        //Assert
        deepCentroid.Should().BeGreaterThan(shallowCentroid);
    }

    [Fact]
    public void Feedback_all_the_way_up_stays_finite()
    {
        //Arrange
        Fm6OpOscillator oscillator = Create(32, 220.0);
        for (int number = 1; number <= 5; number++) { oscillator.GetOperator(number).Level = 0.0; }
        oscillator.GetOperator(6).Level = 1.0;
        oscillator.GetOperator(6).Feedback = 1.0;

        //Act
        float[] block = Render(oscillator, Rate);

        //Assert - noisy and distorted is the documented behaviour; unbounded is not.
        for (int i = 0; i < block.Length; i++)
        {
            float.IsFinite(block[i]).Should().BeTrue();
            ((double)Math.Abs(block[i])).Should().BeLessThan(1.0);
        }
    }

    [Fact]
    public void The_electric_piano_recipe_starts_bright_and_settles()
    {
        //Arrange - the format's own worked example: algorithm 5, three carrier and modulator
        //pairs, the modulators decaying away to leave the carriers ringing.
        Fm6OpOscillator oscillator = Create(5, 261.6255653);
        double[] carrierLevels = { 0.90, 0.70, 0.55 };
        double[] modulatorLevels = { 0.55, 0.40, 0.30 };
        double[] modulatorDecays = { 0.40, 0.30, 0.25 };
        for (int pair = 0; pair < 3; pair++)
        {
            ModestFmOperator carrier = oscillator.GetOperator(1 + (pair * 2));
            carrier.Level = carrierLevels[pair];

            ModestFmOperator modulator = oscillator.GetOperator(2 + (pair * 2));
            modulator.Level = modulatorLevels[pair];
            modulator.Ratio = 14.0;
            modulator.Attack = 0.001;
            modulator.Decay = modulatorDecays[pair];
            modulator.Sustain = 0.0;
        }

        //Act
        float[] block = Render(oscillator, Rate);
        float[] start = new float[Spectrum.BlockLength];
        float[] end = new float[Spectrum.BlockLength];
        Array.Copy(block, 0, start, 0, Spectrum.BlockLength);
        Array.Copy(block, Rate - Spectrum.BlockLength, end, 0, Spectrum.BlockLength);

        //Assert
        Spectrum.PowerCentroid(start, start.Length)
            .Should().BeGreaterThan(Spectrum.PowerCentroid(end, end.Length) * 2.0);
        Spectrum.PeakBin(Spectrum.Magnitudes(end)).Should().Be(45);
    }

    [Theory]
    [InlineData(1, -6.0)]
    [InlineData(2, -3.5)]
    [InlineData(3, -9.1)]
    [InlineData(4, -12.4)]
    [InlineData(5, -22.8)]
    [InlineData(6, -33.7)]
    public void The_modulation_index_is_four_pi_times_the_level(int line, double expectedDb)
    {
        //Arrange
        // MEASURED (round 2, item 27): algorithm 1, carrier at ratio 1 level 1.0, modulator at ratio 1
        // and level 0.2, read against the same patch with the modulator silent. Five modulator levels
        // fitted the closed-form Bessel pattern to 0.3 dB and gave beta/level of 12.55 against
        // 4*pi = 12.566, so the modulator swings the carrier's phase by TWO whole cycles at level 1.
        double fundamental = Spectrum.BinFrequency(171);

        Fm6OpOscillator modulated = Create(1, fundamental);
        modulated.GetOperator(2).Ratio = 1.0;
        modulated.GetOperator(2).Level = 0.2;

        Fm6OpOscillator plain = Create(1, fundamental);
        plain.GetOperator(2).Ratio = 1.0;
        plain.GetOperator(2).Level = 0.0;

        //Act
        double[] withModulation = Spectrum.PreciseMagnitudes(Render(modulated, Spectrum.BlockLength));
        double[] carrierOnly = Spectrum.PreciseMagnitudes(Render(plain, Spectrum.BlockLength));

        double reference = carrierOnly[171];
        double measured = 20.0 * Math.Log10(withModulation[171 * line] / reference);

        //Assert
        measured.Should().BeApproximately(expectedDb, 1.5);
    }

    [Fact]
    public void With_no_ratios_written_the_carriers_land_on_the_first_six_harmonics()
    {
        //Arrange
        // MEASURED (round 2, item 27): in algorithm 32 with the six levels set to 1.0 and NO ratios
        // given, the spectrum is six equal lines at 1 to 6 times the note frequency.
        double fundamental = Spectrum.BinFrequency(101);
        Fm6OpOscillator oscillator = Create(32, fundamental);

        for (int number = 1; number <= 6; number++)
        {
            oscillator.GetOperator(number).Level = 1.0;
        }

        //Act
        double[] magnitudes = Spectrum.PreciseMagnitudes(Render(oscillator, Spectrum.BlockLength));
        double reference = magnitudes[101];

        //Assert
        for (int harmonic = 2; harmonic <= 6; harmonic++)
        {
            (20.0 * Math.Log10(magnitudes[101 * harmonic] / reference))
                .Should().BeApproximately(0.0, 0.5);
        }
    }

    [Theory]
    [InlineData(2, 3.01)]
    [InlineData(6, 7.78)]
    public void Carriers_sum_with_no_count_normalisation(int carriers, double expectedDb)
    {
        //Arrange
        // MEASURED (round 2, item 27): one carrier -37.33 dBFS, two -34.32 and six -29.55, which is
        // 10*log10(2) and 10*log10(6) exactly. The carriers simply sum.
        Fm6OpOscillator one = Create(32, 220.0);
        Fm6OpOscillator many = Create(32, 220.0);

        for (int number = 2; number <= 6; number++) { one.GetOperator(number).Level = 0.0; }

        for (int number = 1; number <= 6; number++)
        {
            many.GetOperator(number).Level = number <= carriers ? 1.0 : 0.0;
        }

        //Act
        double single = Spectrum.Rms(Render(one, Rate / 4), 0, Rate / 4);
        double summed = Spectrum.Rms(Render(many, Rate / 4), 0, Rate / 4);

        //Assert
        (20.0 * Math.Log10(summed / single)).Should().BeApproximately(expectedDb, 0.2);
    }

    private static Fm6OpOscillator Create(int algorithm, double frequency)
    {
        Fm6OpOscillator oscillator = new Fm6OpOscillator { Algorithm = algorithm };
        oscillator.SetSampleRate(Rate);
        oscillator.SetFrequency(frequency);
        return oscillator;
    }

    private static Fm6OpOscillator Configured()
    {
        Fm6OpOscillator oscillator = Create(5, 261.6255653);
        oscillator.GetOperator(1).Level = 0.9;
        oscillator.GetOperator(2).Level = 0.55;
        oscillator.GetOperator(2).Ratio = 14.0;
        oscillator.GetOperator(2).Attack = 0.001;
        oscillator.GetOperator(2).Decay = 0.4;
        oscillator.GetOperator(2).Sustain = 0.0;
        oscillator.GetOperator(3).Level = 0.7;
        oscillator.GetOperator(6).Feedback = 0.08;
        return oscillator;
    }

    private static float[] Render(Fm6OpOscillator oscillator, int length)
    {
        oscillator.Reset(0.0);
        return RenderOnly(oscillator, length);
    }

    private static float[] RenderOnly(Fm6OpOscillator oscillator, int length)
    {
        float[] block = new float[length];
        oscillator.Render(block);
        return block;
    }

    private static double MaximumDifference(float[] first, float[] second)
    {
        double worst = 0.0;
        for (int i = 0; i < first.Length; i++)
        {
            double difference = Math.Abs(first[i] - second[i]);
            if (difference > worst) { worst = difference; }
        }

        return worst;
    }
}
