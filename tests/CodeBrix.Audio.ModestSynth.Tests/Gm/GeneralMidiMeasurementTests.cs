using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// The figures the plan asks to be measured and recorded: how much faster than real time the
/// synthesizer renders, how much headroom real music leaves at the default master volume, and how
/// even the bank's loudness is as a listener actually hears it. Opt-in via
/// CODEBRIX_AUDIO_RUN_GM_MEASUREMENTS=1; skipped otherwise.
/// </summary>
/// <remarks>
/// <para>
/// NOTHING HERE ASSERTS ON A CLOCK. A timing assertion on a machine that is also compiling
/// something fails for reasons that have nothing to do with the synthesizer, so these tests
/// MEASURE and PRINT, and the numbers are read by a person and written into a report. What they do
/// assert is that the render happened at all and came out as audio.
/// </para>
/// <para>
/// They are gated because they render tens of minutes of audio, and they are their own gate rather
/// than sharing the listening-render one because they write no files:
/// </para>
/// <code>
/// CODEBRIX_AUDIO_RUN_GM_MEASUREMENTS=1 dotnet test CodeBrix.Audio.slnx -c Release
/// </code>
/// <para>
/// TAKE THE FIGURES IN RELEASE, on an otherwise idle machine. Debug is roughly a third of the
/// speed and says nothing about what a consumer will see.
/// </para>
/// </remarks>
public class GeneralMidiMeasurementTests
{
    /// <summary>The rate everything here is measured at.</summary>
    public const int SampleRate = 44100;

    private static readonly bool MeasurementsEnabled =
        Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_GM_MEASUREMENTS") == "1";

    private const string MeasurementsSkipReason =
        "Set CODEBRIX_AUDIO_RUN_GM_MEASUREMENTS=1 to run the render-speed, headroom and loudness " +
        "measurements. Take the figures in Release, on an idle machine.";

    [Fact]
    public void measures_the_render_speed_of_the_dense_fixture()
    {
        Assert.SkipUnless(MeasurementsEnabled, MeasurementsSkipReason);

        //Arrange
        MidiSequence dense = GmFixtures.DenseFixture(8.0);
        int[] polyphonies = [64, 32, 16];

        Heading("RENDER SPEED - the dense fixture (eight held chords plus a sixteenth-note kit)");

        //Act & Assert
        foreach (int polyphony in polyphonies)
        {
            double times = Measure(
                () => new GeneralMidiSynthesizer(
                    new GeneralMidiSynthesizerSettings(SampleRate) { MaximumPolyphony = polyphony }),
                dense,
                out int voices);

            Line("polyphony " + polyphony.ToString(CultureInfo.InvariantCulture).PadLeft(3) +
                "   " + Times(times) + "   " + Seconds(dense.Length) + " of audio");

            times.Should().BeGreaterThan(0.0);
            voices.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public void measures_the_render_speed_of_each_real_piece()
    {
        Assert.SkipUnless(MeasurementsEnabled, MeasurementsSkipReason);

        //Arrange
        Heading("RENDER SPEED - the four real pieces, as they are actually played");

        //Act & Assert
        foreach (GmAuditionPlan plan in GmRealPieces.All())
        {
            double times = Measure(Default, plan.Sequence, out int voices);

            Line(Seconds(plan.Length).PadLeft(8) + "   " + Times(times) + "   " + plan.Title);

            times.Should().BeGreaterThan(0.0);
            voices.Should().BeGreaterThanOrEqualTo(0);
        }
    }

    [Fact]
    public void measures_the_render_speed_of_choir_aahs_held_four_and_eight_voices_deep()
    {
        Assert.SkipUnless(MeasurementsEnabled, MeasurementsSkipReason);

        //Arrange
        // Program 52 is the one voicing in the bank built on the formant oscillator, which sums a
        // vowel spectrum partial by partial. It is the expensive one, and how expensive it is
        // decides whether a consumer can stack it.
        Heading("RENDER SPEED - Choir Aahs held, and a cheap program for comparison");

        //Act & Assert
        foreach (int voices in new[] { 4, 8 })
        {
            double choir = Measure(
                Default, Held((int)GeneralMidiProgram.ChoirAahs, voices, 8.0), out int _);

            double pad = Measure(
                Default, Held((int)GeneralMidiProgram.Pad2Warm, voices, 8.0), out int _);

            Line(voices.ToString(CultureInfo.InvariantCulture) + " voices held   Choir Aahs " +
                Times(choir) + "      Pad 2 (warm) " + Times(pad) +
                "      ratio " + (pad / choir).ToString("0.0", CultureInfo.InvariantCulture) + "x");

            choir.Should().BeGreaterThan(0.0);
        }
    }

    [Fact]
    public void measures_the_headroom_of_each_real_piece_at_the_default_master_volume()
    {
        Assert.SkipUnless(MeasurementsEnabled, MeasurementsSkipReason);

        //Arrange
        // One note at full velocity fits inside full scale at the default master volume; thirty do
        // not. What matters is whether REAL music does.
        Heading("HEADROOM - peak sample of each real piece at the DEFAULT master volume");

        //Act & Assert
        foreach (GmAuditionPlan plan in GmRealPieces.All())
        {
            float[] rendered = SoundFontRenderer.Render(
                new GeneralMidiSynthesizer(SampleRate), plan.Sequence, TimeSpan.FromSeconds(3.0));

            double peak = PeakOf(rendered);

            string verdict = peak > 1.0
                ? "OVER full scale by " +
                    (20.0 * Math.Log10(peak)).ToString("0.0", CultureInfo.InvariantCulture) + " dB"
                : (20.0 * Math.Log10(peak)).ToString("0.0", CultureInfo.InvariantCulture) +
                    " dBFS - fits";

            Line("peak " + peak.ToString("0.000", CultureInfo.InvariantCulture) + "   " +
                verdict.PadRight(26) + plan.Title);

            peak.Should().BeGreaterThan(0.0);
        }
    }

    [Fact]
    public void measures_the_loudness_of_every_program_as_the_tour_plays_it()
    {
        Assert.SkipUnless(MeasurementsEnabled, MeasurementsSkipReason);

        //Arrange
        // The bank is normalised on a held middle C at velocity 100. The tour plays each family in
        // the register and the articulation that suits it, which is what a listener actually hears
        // - so this is where an instrument that stands out from its neighbours shows up.
        Heading("LOUDNESS ACROSS THE BANK - loudest 100 ms of each program's own tour phrase");

        double[] levels = new double[GeneralMidi.ProgramCount];

        //Act
        for (int program = 0; program < GeneralMidi.ProgramCount; program++)
        {
            GmAuditionPlan plan = GmAudition.SingleProgram(program);

            float[] rendered = SoundFontRenderer.Render(
                new GeneralMidiSynthesizer(SampleRate), plan.Sequence, TimeSpan.FromSeconds(1.5));

            levels[program] = LoudestWindow(rendered);
        }

        double median = Median(levels);

        Line("median " + median.ToString("0.0000", CultureInfo.InvariantCulture) + "  (" +
            Decibels(median, 1.0) + " dBFS)");
        Line(string.Empty);

        for (int program = 0; program < GeneralMidi.ProgramCount; program++)
        {
            Line(program.ToString("000", CultureInfo.InvariantCulture) + "  " +
                Decibels(levels[program], median).PadLeft(6) + " dB   " +
                GeneralMidi.DisplayName((GeneralMidiProgram)program));
        }

        //Assert
        foreach (double level in levels)
        {
            level.Should().BeGreaterThan(0.0);
        }
    }

    private static GeneralMidiSynthesizer Default() => new GeneralMidiSynthesizer(SampleRate);

    // A chord of n notes on one program, held for a while: the shape that costs the most.
    private static MidiSequence Held(int program, int voices, double seconds)
    {
        MidiEventCollection events = GmAudition.NewCollection();
        long length = (long)(seconds * 2 * GmAudition.TicksPerQuarterNote);

        events.AddEvent(new PatchChangeEvent(0, 1, program), 1);

        for (int voice = 0; voice < voices; voice++)
        {
            int key = 48 + (voice * 4);

            events.AddEvent(new NoteEvent(0, 1, MidiCommandCode.NoteOn, key, 100), 1);
            events.AddEvent(new NoteEvent(length, 1, MidiCommandCode.NoteOff, key, 0), 1);
        }

        events.PrepareForExport();
        return MidiSequence.FromEvents(events);
    }

    // Renders once to warm the code paths, then renders again on the clock. Single thread, no
    // device: SoundFontRenderer.Render is a plain loop over the synthesizer.
    private static double Measure(
        Func<GeneralMidiSynthesizer> build, MidiSequence sequence, out int voices)
    {
        SoundFontRenderer.Render(build(), sequence, TimeSpan.Zero);

        GC.Collect();
        GC.WaitForPendingFinalizers();

        GeneralMidiSynthesizer synthesizer = build();

        Stopwatch clock = Stopwatch.StartNew();
        float[] rendered = SoundFontRenderer.Render(synthesizer, sequence, TimeSpan.Zero);
        clock.Stop();

        voices = synthesizer.ActiveVoiceCount;

        rendered.Length.Should().BeGreaterThan(0);

        return sequence.Length.TotalSeconds / clock.Elapsed.TotalSeconds;
    }

    private static double PeakOf(float[] samples)
    {
        double peak = 0.0;

        for (int i = 0; i < samples.Length; i++)
        {
            double value = Math.Abs(samples[i]);
            if (value > peak) { peak = value; }
        }

        return peak;
    }

    // The root-mean-square of the loudest tenth of a second of the mono sum - the measure the bank
    // was calibrated on, so a figure here is comparable with the one in the bank's own tests.
    private static double LoudestWindow(float[] interleaved)
    {
        int frames = interleaved.Length / 2;
        int window = SampleRate / 10;

        if (frames <= window) { return 0.0; }

        double best = 0.0;

        for (int offset = 0; offset + window <= frames; offset += window / 10)
        {
            double sum = 0.0;

            for (int frame = offset; frame < offset + window; frame++)
            {
                double value = 0.5 * (interleaved[frame * 2] + interleaved[(frame * 2) + 1]);
                sum += value * value;
            }

            double level = Math.Sqrt(sum / window);
            if (level > best) { best = level; }
        }

        return best;
    }

    private static double Median(double[] values)
    {
        List<double> sorted = new List<double>(values);
        sorted.Sort();
        return sorted[sorted.Count / 2];
    }

    private static string Decibels(double value, double reference) =>
        value <= 0.0
            ? "-inf"
            : (20.0 * Math.Log10(value / reference)).ToString("+0.0;-0.0;0.0", CultureInfo.InvariantCulture);

    private static string Times(double realTime) =>
        (realTime.ToString("0.0", CultureInfo.InvariantCulture) + "x real time").PadRight(18);

    private static string Seconds(TimeSpan length) =>
        length.TotalSeconds.ToString("0.0", CultureInfo.InvariantCulture) + " s";

    private static void Heading(string text)
    {
        Line(string.Empty);
        Line("=== " + text + " ===");
        Line(string.Empty);
    }

    private static void Line(string text)
    {
        Console.Out.WriteLine(text);
        Console.Out.Flush();
    }
}
