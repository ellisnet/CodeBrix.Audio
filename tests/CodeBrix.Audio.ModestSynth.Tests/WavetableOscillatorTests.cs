using System;
using System.IO;
using CodeBrix.Audio.ModestSynth.Oscillators;
using CodeBrix.Audio.ModestSynth.Patch;
using CodeBrix.Audio.ModestSynth.Wavetable;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// Tests for <see cref="WavetableOscillator" />: which frame it plays, how the position blends or
/// snaps, that it stays clean at the top of the keyboard, and that a table it cannot load leaves it
/// sounding like a sine rather than throwing.
/// </summary>
/// <remarks>
/// The fixture table's four frames - sine, triangle, saw, square - are chosen so that "which frame
/// is sounding" is answerable from three numbers: the second harmonic (only the saw has one), the
/// third (a saw's and a square's are at -9.5 dB, a triangle's at -19.1 dB) and the level (a square
/// is far louder than a triangle). Together they separate all four.
/// </remarks>
[Collection(WavetableCacheCollection.Name)]
public class WavetableOscillatorTests : IDisposable
{
    private const int FundamentalBin = 171;

    private static readonly WavetableFile Table = WavetableFile.FromSamples(
        WavetableFixtures.BuildFourFrames(), WavetableFixtures.FrameSize, "fixture");

    private readonly string folder;

    public WavetableOscillatorTests()
    {
        folder = Path.Combine(Path.GetTempPath(), "modestsynth-wto-" + Path.GetRandomFileName());
        Directory.CreateDirectory(folder);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(folder)) { Directory.Delete(folder, true); }
        }
        catch (IOException)
        {
            // A leftover temp folder is not worth failing a test over.
        }
    }

    [Fact]
    public void Waveform_is_the_name_a_preset_spells() =>
        new WavetableOscillator().Waveform.Should().Be("wavetable");

    [Fact]
    public void Position_zero_plays_the_first_frame()
    {
        //Arrange
        double[] magnitudes = Spectrum.Magnitudes(RenderBlock(0.0, true));

        //Act, Assert
        RelativeDb(magnitudes, 2).Should().BeLessThan(-80.0);
        RelativeDb(magnitudes, 3).Should().BeLessThan(-80.0);
    }

    [Fact]
    public void Position_one_plays_the_last_frame()
    {
        //Arrange
        double[] magnitudes = Spectrum.Magnitudes(RenderBlock(1.0, true));

        //Act, Assert
        RelativeDb(magnitudes, 2).Should().BeLessThan(-80.0);            // a square has no even harmonics
        RelativeDb(magnitudes, 3).Should().BeApproximately(-9.54, 0.2);  // and a third at one third
    }

    [Fact]
    public void Position_lands_exactly_on_each_frame_of_a_four_frame_table()
    {
        //Arrange
        double[] triangle = Spectrum.Magnitudes(RenderBlock(1.0 / 3.0, true));
        double[] saw = Spectrum.Magnitudes(RenderBlock(2.0 / 3.0, true));

        //Act, Assert
        RelativeDb(triangle, 2).Should().BeLessThan(-80.0);
        RelativeDb(triangle, 3).Should().BeApproximately(-19.08, 0.2);
        RelativeDb(saw, 2).Should().BeApproximately(-6.02, 0.2);
        RelativeDb(saw, 3).Should().BeApproximately(-9.54, 0.2);
    }

    [Fact]
    public void Position_between_frames_blends_them_when_interpolation_is_on()
    {
        //Arrange
        // Five sixths of a four-frame table is frame index 2.5 - half the saw, half the square.
        float[] blended = RenderBlock(5.0 / 6.0, true);
        float[] saw = RenderBlock(2.0 / 3.0, true);

        //Act
        double blendedSecond = RelativeDb(Spectrum.Magnitudes(blended), 2);
        double sawSecond = RelativeDb(Spectrum.Magnitudes(saw), 2);

        //Assert
        blendedSecond.Should().BeGreaterThan(-80.0);                     // the saw's evens survive
        blendedSecond.Should().BeApproximately(sawSecond, 0.2);
        Rms(blended).Should().BeApproximately(Rms(saw) / 2.0, 0.02);     // the fundamentals oppose
    }

    [Fact]
    public void Position_between_frames_snaps_to_the_nearest_when_interpolation_is_off()
    {
        //Arrange
        float[] snapped = RenderBlock(5.0 / 6.0, false);
        float[] square = RenderBlock(1.0, true);

        //Act
        double[] magnitudes = Spectrum.Magnitudes(snapped);

        //Assert
        RelativeDb(magnitudes, 2).Should().BeLessThan(-80.0);            // the square, not the blend
        RelativeDb(magnitudes, 3).Should().BeApproximately(-9.54, 0.2);
        Rms(snapped).Should().BeApproximately(Rms(square), 0.01);
    }

    [Fact]
    public void Position_snapping_rounds_a_half_way_position_up()
    {
        //Arrange
        // Half of a four-frame table is frame index 1.5, which rounds to the saw at index 2.
        double[] magnitudes = Spectrum.Magnitudes(RenderBlock(0.5, false));

        //Act, Assert
        RelativeDb(magnitudes, 2).Should().BeApproximately(-6.02, 0.2);
        RelativeDb(magnitudes, 3).Should().BeApproximately(-9.54, 0.2);
    }

    [Theory]
    [InlineData(43)]
    [InlineData(171)]
    [InlineData(683)]
    [InlineData(1367)]
    [InlineData(2731)]
    public void Render_puts_the_fundamental_exactly_where_it_was_asked_to(int bin)
    {
        //Arrange
        WavetableOscillator oscillator = Build(2.0 / 3.0, true, Spectrum.BinFrequency(bin));

        //Act
        double[] magnitudes = Spectrum.Magnitudes(Spectrum.RenderBlock(oscillator));

        //Assert
        Spectrum.PeakBin(magnitudes).Should().Be(bin);
    }

    [Theory]
    [InlineData(171)]
    [InlineData(683)]
    [InlineData(1367)]
    public void Render_keeps_a_sawtooth_frame_free_of_audible_aliasing(int bin)
    {
        //Arrange
        WavetableOscillator oscillator = Build(2.0 / 3.0, false, Spectrum.BinFrequency(bin));

        //Act
        double aliasFloor = Spectrum.AliasFloorDb(
            Spectrum.PreciseMagnitudes(Spectrum.RenderBlock(oscillator)), bin, 10000.0);

        //Assert
        // The band-limited saw this package generates from a formula measures -56 dB at 1 kHz, and
        // -42 dB at 8 kHz; the mip-mapped wavetable is better than that everywhere, which is the
        // bar this test holds it to.
        aliasFloor.Should().BeLessThan(-60.0);
    }

    [Fact]
    public void Render_uses_a_higher_mip_level_as_the_note_rises()
    {
        //Arrange
        WavetableOscillator low = Build(0.0, true, 55.0);
        WavetableOscillator high = Build(0.0, true, 3520.0);

        //Act, Assert
        low.MipLevel.Should().BeLessThan(high.MipLevel);
    }

    [Fact]
    public void Render_without_a_table_is_a_pure_sine_at_the_note_frequency()
    {
        //Arrange
        // Measured behaviour: the reference player renders a wavetable oscillator that names no
        // wavetableFile as a sine at the note's own frequency and at a sine's own level.
        WavetableOscillator oscillator = new WavetableOscillator();
        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(Spectrum.BinFrequency(FundamentalBin));

        SineOscillator sine = new SineOscillator();
        sine.SetSampleRate(Spectrum.SampleRate);
        sine.SetFrequency(Spectrum.BinFrequency(FundamentalBin));

        //Act
        float[] block = Spectrum.RenderBlock(oscillator);
        float[] reference = Spectrum.RenderBlock(sine);
        double[] magnitudes = Spectrum.PreciseMagnitudes(block);

        //Assert
        oscillator.HasTable.Should().BeFalse();
        Spectrum.PeakBin(magnitudes).Should().Be(FundamentalBin);
        Spectrum.AliasFloorDb(magnitudes, FundamentalBin, 24000.0).Should().BeLessThan(-60.0);
        Rms(block).Should().BeApproximately(Rms(reference), 1e-5);
    }

    [Fact]
    public void TryLoad_reports_a_missing_file_and_still_renders()
    {
        //Arrange
        WavetableOscillator oscillator = new WavetableOscillator();
        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(440.0);
        float[] block = new float[512];

        //Act
        bool loaded = oscillator.TryLoad(Path.Combine(folder, "not-here.wav"), 2048);
        oscillator.Reset(0.0);
        oscillator.Render(block);

        //Assert
        loaded.Should().BeFalse();
        oscillator.HasTable.Should().BeFalse();
        oscillator.Problem.Should().Contain("Missing file reference: <oscillator> @wavetableFile=");
        Rms(block).Should().BeGreaterThan(0.5);
    }

    [Fact]
    public void TryLoad_plays_a_file_it_can_read()
    {
        //Arrange
        string path = Path.Combine(folder, "table.wav");
        WavetableFixtures.WriteWav(path, WavetableFixtures.BuildFourFrames(), 44100, 32, 1,
            WavetableFixtures.ClmText(WavetableFixtures.FrameSize));
        WavetableOscillator oscillator = new WavetableOscillator();
        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(Spectrum.BinFrequency(FundamentalBin));
        float[] block = new float[Spectrum.BlockLength];

        //Act
        bool loaded = oscillator.TryLoad(path, 0);
        oscillator.Position = 2.0 / 3.0;
        oscillator.Reset(0.0);
        oscillator.Render(block);

        //Assert
        loaded.Should().BeTrue();
        oscillator.Problem.Should().BeNull();
        oscillator.Table.HasClmChunk.Should().BeTrue();
        RelativeDb(Spectrum.Magnitudes(block), 2).Should().BeApproximately(-6.02, 0.2);
    }

    [Fact]
    public void Setting_a_table_clears_a_problem_left_by_a_failed_load()
    {
        //Arrange
        WavetableOscillator oscillator = new WavetableOscillator();
        oscillator.TryLoad(Path.Combine(folder, "not-here.wav"), 2048);

        //Act
        oscillator.Table = Table;

        //Assert
        oscillator.Problem.Should().BeNull();
        oscillator.HasTable.Should().BeTrue();
    }

    [Fact]
    public void ReportProblem_records_a_reason_the_caller_found()
    {
        //Arrange
        WavetableOscillator oscillator = new WavetableOscillator();

        //Act
        oscillator.ReportProblem("No file reference: <oscillator> @wavetableFile is not set.");

        //Assert
        oscillator.Problem.Should().Contain("No file reference");
    }

    [Fact]
    public void Reset_starts_the_same_place_for_the_same_phase()
    {
        //Arrange
        WavetableOscillator first = Build(2.0 / 3.0, true, 220.0);
        WavetableOscillator second = Build(2.0 / 3.0, true, 220.0);
        float[] a = new float[1024];
        float[] b = new float[1024];

        //Act
        first.Reset(0.37);
        first.Render(a);
        second.Reset(0.37);
        second.Render(b);

        //Assert
        a.Should().Equal(b);
    }

    [Fact]
    public void Reset_at_a_different_phase_starts_somewhere_else()
    {
        //Arrange
        WavetableOscillator first = Build(2.0 / 3.0, true, 220.0);
        WavetableOscillator second = Build(2.0 / 3.0, true, 220.0);
        float[] a = new float[1024];
        float[] b = new float[1024];

        //Act
        first.Reset(0.0);
        first.Render(a);
        second.Reset(0.5);
        second.Render(b);

        //Assert
        a.Should().NotEqual(b);
    }

    [Fact]
    public void Random_phase_from_a_patch_differs_per_voice_and_repeats()
    {
        //Arrange
        ModestPatch patch = new ModestPatch { Waveform = ModestWaveform.Wavetable, RandomPhase = true };

        //Act
        double first = patch.GetStartPhase(1u);
        double second = patch.GetStartPhase(2u);
        double again = patch.GetStartPhase(1u);

        //Assert
        first.Should().NotBe(second);
        again.Should().Be(first);
        first.Should().BeInRange(0.0, 1.0);
    }

    [Fact]
    public void Random_phase_actually_moves_the_waveform()
    {
        //Arrange
        ModestPatch patch = new ModestPatch { Waveform = ModestWaveform.Wavetable, RandomPhase = true };
        WavetableOscillator first = Build(2.0 / 3.0, true, 220.0);
        WavetableOscillator second = Build(2.0 / 3.0, true, 220.0);
        float[] a = new float[512];
        float[] b = new float[512];

        //Act
        first.Reset(patch.GetStartPhase(1u));
        first.Render(a);
        second.Reset(patch.GetStartPhase(2u));
        second.Render(b);

        //Assert
        a.Should().NotEqual(b);
    }

    [Fact]
    public void A_position_change_is_reached_by_the_end_of_the_block_it_arrives_in()
    {
        //Arrange
        WavetableOscillator ramped = Build(0.0, true, Spectrum.BinFrequency(FundamentalBin));
        WavetableOscillator settled = Build(1.0, true, Spectrum.BinFrequency(FundamentalBin));
        float[] rampedFirst = new float[Spectrum.BlockLength];
        float[] rampedSecond = new float[Spectrum.BlockLength];
        float[] settledFirst = new float[Spectrum.BlockLength];
        float[] settledSecond = new float[Spectrum.BlockLength];

        //Act
        ramped.Reset(0.0);
        ramped.Position = 1.0;
        ramped.Render(rampedFirst);           // ramps from the sine frame to the square frame
        ramped.Render(rampedSecond);          // settled on the square frame
        settled.Reset(0.0);
        settled.Render(settledFirst);
        settled.Render(settledSecond);

        //Assert
        rampedFirst.Should().NotEqual(settledFirst);
        rampedSecond.Should().Equal(settledSecond);
    }

    [Fact]
    public void Reset_snaps_the_position_instead_of_ramping_it()
    {
        //Arrange
        WavetableOscillator oscillator = Build(0.0, true, Spectrum.BinFrequency(FundamentalBin));
        WavetableOscillator reference = Build(1.0, true, Spectrum.BinFrequency(FundamentalBin));
        float[] block = new float[Spectrum.BlockLength];
        float[] expected = new float[Spectrum.BlockLength];

        //Act
        oscillator.Position = 1.0;
        oscillator.Reset(0.0);
        oscillator.Render(block);
        reference.Reset(0.0);
        reference.Render(expected);

        //Assert
        block.Should().Equal(expected);
    }

    [Fact]
    public void Position_is_clamped_and_ignores_a_value_that_is_not_a_number()
    {
        //Arrange
        WavetableOscillator oscillator = new WavetableOscillator { Position = 0.25 };

        //Act
        oscillator.Position = 9.0;
        double high = oscillator.Position;
        oscillator.Position = -9.0;
        double low = oscillator.Position;
        oscillator.Position = double.NaN;
        double kept = oscillator.Position;

        //Assert
        high.Should().Be(1.0);
        low.Should().Be(0.0);
        kept.Should().Be(0.0);
    }

    [Fact]
    public void Render_with_a_table_allocates_nothing_once_it_is_warm()
    {
        //Arrange
        WavetableOscillator oscillator = Build(0.4, true, 220.0);
        float[] block = new float[512];
        for (int i = 0; i < 4; i++) { oscillator.Render(block); }

        // Measured through AllocationProbe for the same reason RenderAllocationTests is: one
        // difference is not reliable under parallel test load.
        Action work = () =>
        {
            for (int i = 0; i < 64; i++)
            {
                oscillator.Position = 0.4 + (i * 0.001);
                oscillator.SetFrequency(220.0 + i);
                oscillator.Render(block);
            }
        };

        //Act
        long allocated = AllocationProbe.LowestBytes(work);

        //Assert
        allocated.Should().Be(0L);
    }

    [Fact]
    public void A_patch_hands_its_wavetable_settings_to_the_oscillator()
    {
        //Arrange
        string path = Path.Combine(folder, "patch-table.wav");
        WavetableFixtures.WriteWav(path, WavetableFixtures.BuildFourFrames(), 44100, 32, 1, null);
        ModestPatch patch = new ModestPatch
        {
            Waveform = ModestWaveform.Wavetable,
            WavetableFile = path,
            WavetableFrameSize = WavetableFixtures.FrameSize,
            WavetablePosition = 0.75,
            WavetableFrameInterpolation = false,
        };

        //Act
        WavetableOscillator oscillator = (WavetableOscillator)patch.CreateOscillator(Spectrum.SampleRate);

        //Assert
        oscillator.HasTable.Should().BeTrue();
        oscillator.Position.Should().Be(0.75);
        oscillator.FrameInterpolation.Should().BeFalse();
        oscillator.Table.FrameCount.Should().Be(WavetableFixtures.FrameCount);
    }

    [Fact]
    public void A_patch_with_no_wavetable_file_reports_it_and_builds_a_sine()
    {
        //Arrange
        ModestPatch patch = new ModestPatch { Waveform = ModestWaveform.Wavetable };

        //Act
        WavetableOscillator oscillator = (WavetableOscillator)patch.CreateOscillator(Spectrum.SampleRate);

        //Assert
        oscillator.HasTable.Should().BeFalse();
        oscillator.Problem.Should().Contain("@wavetableFile is not set");
    }

    [Fact]
    public void A_single_frame_table_plays_that_frame_whatever_the_position_is()
    {
        //Arrange
        float[] samples = new float[WavetableFixtures.FrameSize];
        WavetableFixtures.BuildFourFrames().AsSpan(
            WavetableFixtures.SawFrame * WavetableFixtures.FrameSize,
            WavetableFixtures.FrameSize).CopyTo(samples);
        WavetableFile single = WavetableFile.FromSamples(samples, WavetableFixtures.FrameSize, "one frame");

        //Act
        float[] atZero = RenderBlock(single, 0.0, true);
        float[] atOne = RenderBlock(single, 1.0, false);

        //Assert
        single.FrameCount.Should().Be(1);
        atZero.Should().Equal(atOne);
        RelativeDb(Spectrum.Magnitudes(atZero), 2).Should().BeApproximately(-6.02, 0.2);
    }

    [Fact]
    public void The_lowest_note_a_keyboard_can_send_plays_from_the_full_bandwidth_level()
    {
        //Arrange
        WavetableOscillator oscillator = Build(2.0 / 3.0, false, 8.1758);
        float[] block = new float[Spectrum.BlockLength];

        //Act
        oscillator.Render(block);

        //Assert
        oscillator.MipLevel.Should().Be(0);
        Rms(block).Should().BeGreaterThan(0.4);
    }

    [Fact]
    public void The_highest_note_a_keyboard_can_send_still_plays()
    {
        //Arrange
        WavetableOscillator oscillator = Build(2.0 / 3.0, false, 12543.85);
        float[] block = new float[Spectrum.BlockLength];

        //Act
        oscillator.Render(block);

        //Assert
        Rms(block).Should().BeGreaterThan(0.3);
    }

    private static float[] RenderBlock(WavetableFile table, double position, bool interpolation)
    {
        WavetableOscillator oscillator = new WavetableOscillator
        {
            Table = table,
            Position = position,
            FrameInterpolation = interpolation,
        };

        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(Spectrum.BinFrequency(FundamentalBin));
        return Spectrum.RenderBlock(oscillator);
    }

    private static float[] RenderBlock(double position, bool interpolation)
        => Spectrum.RenderBlock(Build(position, interpolation, Spectrum.BinFrequency(FundamentalBin)));

    private static WavetableOscillator Build(double position, bool interpolation, double frequency)
    {
        WavetableOscillator oscillator = new WavetableOscillator
        {
            Table = Table,
            Position = position,
            FrameInterpolation = interpolation,
        };

        oscillator.SetSampleRate(Spectrum.SampleRate);
        oscillator.SetFrequency(frequency);
        oscillator.Reset(0.0);
        return oscillator;
    }

    private static double RelativeDb(double[] magnitudes, int harmonic)
    {
        int bin = FundamentalBin * harmonic;
        double value = bin < magnitudes.Length ? magnitudes[bin] : 0.0;
        return 20.0 * Math.Log10(Math.Max(value, 1e-12) / magnitudes[FundamentalBin]);
    }

    private static double Rms(float[] samples) => Spectrum.Rms(samples, 0, samples.Length);
}
