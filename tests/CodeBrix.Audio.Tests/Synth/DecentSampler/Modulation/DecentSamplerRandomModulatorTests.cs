using System;
using System.Linq;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Modulation;

/// <summary>
/// The <c>&lt;random&gt;</c> modulator: its two modes, its seed, and the reproducibility the rest of
/// this library insists on.
/// </summary>
/// <remarks>
/// The guide says a random modulator "always produces a value between -1 and 1", so its output is
/// offered to the binding against that range rather than the 0-to-1 every other modulator uses. The
/// guide's own example - a linear translation onto 0.5 to 1 - only reaches the whole of its output
/// range under that reading, which is what these tests pin down.
/// </remarks>
public class DecentSamplerRandomModulatorTests
{
    private const string VolumeBinding =
        "<binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"GROUP_VOLUME\" " +
        "modBehavior=\"set\" translation=\"linear\" translationOutputMin=\"0.5\" " +
        "translationOutputMax=\"1\" />";

    [Fact]
    public void note_on_mode_produces_one_value_per_note_inside_the_bindings_output_range()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<random mode=\"note_on\" seed=\"12345\" scope=\"global\">" + VolumeBinding + "</random>");
        var synthesizer = harness.Synthesizer();
        var values = new double[8];

        //Act
        for (var note = 0; note < values.Length; note++)
        {
            synthesizer.NoteOn(0, 60 + note, 100);
            values[note] = ModulationHarness.Trace(
                synthesizer, 2, () => harness.Instrument.Groups[0].Volume)[0];
            synthesizer.NoteOff(0, 60 + note);
        }

        //Assert
        values.Should().AllSatisfy(value =>
        {
            value.Should().BeGreaterThanOrEqualTo(0.5);
            value.Should().BeLessThanOrEqualTo(1.0);
        });

        values.Distinct().Count().Should().BeGreaterThan(6);
    }

    [Fact]
    public void note_on_mode_holds_its_value_between_notes()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<random mode=\"note_on\" seed=\"7\" scope=\"global\">" + VolumeBinding + "</random>");
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        var trace = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(0.5), () => harness.Instrument.Groups[0].Volume);

        //Assert
        trace.Should().AllSatisfy(value => value.Should().Be(trace[0]));
    }

    [Fact]
    public void the_same_seed_replays_the_same_numbers()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<random mode=\"note_on\" seed=\"4242\" scope=\"global\">" + VolumeBinding + "</random>");

        //Act
        var first = Draw(harness);
        var second = Draw(harness);

        //Assert
        second.Should().Equal(first);
    }

    [Fact]
    public void a_different_seed_draws_different_numbers()
    {
        //Arrange
        using var one = ModulationHarness.Load(
            "<random mode=\"note_on\" seed=\"1\" scope=\"global\">" + VolumeBinding + "</random>");
        using var other = ModulationHarness.Load(
            "<random mode=\"note_on\" seed=\"2\" scope=\"global\">" + VolumeBinding + "</random>");

        //Act
        var first = Draw(one);
        var second = Draw(other);

        //Assert
        second.Should().NotEqual(first);
    }

    [Fact]
    public void an_unseeded_random_still_replays_from_the_synthesizers_own_seed()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<random mode=\"note_on\" scope=\"global\">" + VolumeBinding + "</random>");

        //Act
        var first = Draw(harness);
        var second = Draw(harness);

        //Assert
        second.Should().Equal(first);
    }

    [Fact]
    public void periodic_mode_draws_a_new_value_at_its_frequency()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<random mode=\"periodic\" frequency=\"10\" seed=\"99\" scope=\"global\">" +
            VolumeBinding + "</random>");
        var synthesizer = harness.Synthesizer();

        //Act
        var trace = ModulationHarness.Trace(
            synthesizer, ModulationHarness.Blocks(1.0), () => harness.Instrument.Groups[0].Volume);

        //Assert
        var changes = 0;

        for (var block = 1; block < trace.Length; block++)
        {
            if (Math.Abs(trace[block] - trace[block - 1]) > 1.0E-9)
            {
                changes++;
            }
        }

        changes.Should().Be(10);
    }

    [Fact]
    public void a_voice_scope_random_gives_each_note_its_own_value()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<random mode=\"note_on\" seed=\"31\" scope=\"voice\">" + VolumeBinding + "</random>");
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 100);
        synthesizer.NoteOn(0, 64, 100);
        ModulationHarness.Trace(synthesizer, 4, () => harness.Instrument.Groups[0].Volume);

        //Assert
        // A voice-scope contribution never reaches the published property, which is exactly what
        // makes it per voice: the group's own volume stays where the preset put it.
        harness.Instrument.Groups[0].Volume.Should().Be(1.0);
        synthesizer.ActiveVoiceCount.Should().Be(2);
    }

    private static double[] Draw(ModulationHarness harness)
    {
        var synthesizer = harness.Synthesizer();
        var values = new double[6];

        for (var note = 0; note < values.Length; note++)
        {
            synthesizer.NoteOn(0, 60 + note, 100);
            values[note] = ModulationHarness.Trace(
                synthesizer, 2, () => harness.Instrument.Groups[0].Volume)[0];
            synthesizer.NoteOff(0, 60 + note);
        }

        return values;
    }
}
