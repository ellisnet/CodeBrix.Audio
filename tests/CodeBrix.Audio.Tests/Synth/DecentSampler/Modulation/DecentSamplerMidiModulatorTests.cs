using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Modulation;

/// <summary>
/// The <c>&lt;midiCC&gt;</c> and <c>&lt;midiVelocity&gt;</c> modulators, including the
/// <c>channel="voice"</c> rule that gives every note its own controller values.
/// </summary>
public class DecentSamplerMidiModulatorTests
{
    private const string VolumeBinding =
        "<binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"GROUP_VOLUME\" " +
        "modBehavior=\"set\" translation=\"linear\" translationOutputMin=\"0\" translationOutputMax=\"1\" />";

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(64, 0.504)]
    [InlineData(127, 1.0)]
    public void a_global_controller_modulator_follows_its_channels_controller(int value, double expected)
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<midiCC number=\"1\" channel=\"1\" scope=\"global\">" + VolumeBinding + "</midiCC>");
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 1, value);
        var trace = ModulationHarness.Trace(synthesizer, 2, () => harness.Instrument.Groups[0].Volume);

        //Assert
        trace[0].Should().BeApproximately(expected, 0.005);
    }

    [Fact]
    public void a_fixed_channel_ignores_every_other_channel()
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<midiCC number=\"11\" channel=\"3\" scope=\"global\">" + VolumeBinding + "</midiCC>");
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 11, 127);
        var otherChannel = ModulationHarness.Trace(
            synthesizer, 2, () => harness.Instrument.Groups[0].Volume);

        synthesizer.ProcessMidiMessage(2, 0xB0, 11, 127);
        var ownChannel = ModulationHarness.Trace(
            synthesizer, 2, () => harness.Instrument.Groups[0].Volume);

        //Assert
        otherChannel[0].Should().Be(0.0);
        ownChannel[0].Should().BeApproximately(1.0, 0.005);
    }

    [Fact]
    public void channel_voice_reads_the_channel_the_note_arrived_on()
    {
        //Arrange
        using var loud = ModulationHarness.Load(
            "<midiCC number=\"1\" channel=\"voice\" scope=\"voice\">" + VolumeBinding + "</midiCC>");
        using var quiet = ModulationHarness.Load(
            "<midiCC number=\"1\" channel=\"voice\" scope=\"voice\">" + VolumeBinding + "</midiCC>");

        var loudSynth = loud.Synthesizer();
        var quietSynth = quiet.Synthesizer();

        //Act
        // The controller is written on channel 4 in both, but only one plays its note there.
        loudSynth.ProcessMidiMessage(3, 0xB0, 1, 127);
        quietSynth.ProcessMidiMessage(3, 0xB0, 1, 127);

        loudSynth.NoteOn(3, 60, 100);
        quietSynth.NoteOn(0, 60, 100);

        var (loudLeft, _) = DecentSamplerRenderProbe.RenderBlocks(loudSynth, 64);
        var (quietLeft, _) = DecentSamplerRenderProbe.RenderBlocks(quietSynth, 64);

        //Assert
        DecentSamplerRenderProbe.Rms(loudLeft).Should().BeGreaterThan(0.3);
        DecentSamplerRenderProbe.Rms(quietLeft).Should().BeLessThan(0.001);
    }

    [Theory]
    [InlineData(20, 0.157)]
    [InlineData(100, 0.787)]
    [InlineData(127, 1.0)]
    public void a_global_velocity_modulator_follows_the_last_note_on(int velocity, double expected)
    {
        //Arrange
        using var harness = ModulationHarness.Load(
            "<midiVelocity scope=\"global\">" + VolumeBinding + "</midiVelocity>");
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.NoteOn(0, 60, velocity);
        var trace = ModulationHarness.Trace(synthesizer, 2, () => harness.Instrument.Groups[0].Volume);

        //Assert
        trace[0].Should().BeApproximately(expected, 0.005);
    }

    [Fact]
    public void a_voice_velocity_modulator_gives_each_note_its_own_value()
    {
        //Arrange
        // GROUP_TUNING rather than volume, so the two notes stay tellable apart from one another and
        // the reading is per voice rather than a sum.
        const string tuningBinding =
            "<binding type=\"amp\" level=\"group\" position=\"0\" parameter=\"GROUP_VOLUME\" " +
            "modBehavior=\"multiply\" translation=\"linear\" translationOutputMin=\"0\" " +
            "translationOutputMax=\"1\" />";

        using var harness = ModulationHarness.Load(
            "<midiVelocity scope=\"voice\">" + tuningBinding + "</midiVelocity>");
        var synthesizer = harness.Synthesizer();

        //Act
        synthesizer.NoteOn(0, 60, 127);
        var (loudLeft, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 32);

        var second = harness.Synthesizer();
        second.NoteOn(0, 60, 20);
        var (quietLeft, _) = DecentSamplerRenderProbe.RenderBlocks(second, 32);

        //Assert
        // Velocity already scales the amplitude through ampVelTrack, so the modulator squares it:
        // (127/127)^2 against (20/127)^2, which is a factor of forty.
        var ratio = DecentSamplerRenderProbe.Rms(loudLeft) / DecentSamplerRenderProbe.Rms(quietLeft);
        ratio.Should().BeGreaterThan(20.0);
    }
}
