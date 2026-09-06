using System;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.Sfz;
using CodeBrix.Audio.Tests.Synth.Sfz;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.Mpe;

/// <summary>
/// The per-channel bend range from RPN 0, in the two engines that are not the Decent Sampler one.
/// </summary>
/// <remarks>
/// The SoundFont engine has honoured RPN 0 since it was ported; this fences that so it is not lost.
/// The SFZ engine gained it with the modulator and MPE phase: the SFZ format states a region's bend
/// range in cents with <c>bend_up</c> and <c>bend_down</c>, so RPN 0 SCALES that range against
/// MIDI's own two semitones rather than replacing it. Zones - each note bending on its own channel
/// over its own master - remain a follow-up for both engines.
/// </remarks>
public class MpeBendRangeTests
{
    private const int SampleRate = 44100;

    [Fact]
    public void the_sfz_engine_bends_its_own_range_until_a_registered_parameter_widens_it()
    {
        //Arrange
        using var files = SfzTestInstruments.Create();
        files.WriteSineWav("tone.wav", 440f, SampleRate * 4);

        var instrument = files.Load(
            "<region> sample=tone.wav pitch_keycenter=60 lokey=0 hikey=127 " +
            "bend_up=200 bend_down=-200 ampeg_release=0.01");

        //Act
        var plain = BendUpHertz(instrument, useRegisteredParameter: false);
        var widened = BendUpHertz(instrument, useRegisteredParameter: true);

        //Assert
        // The region asks for two semitones; RPN 0 set to twelve makes the same wheel position reach
        // twelve, which is what a performance recorded from an expressive controller needs.
        Cents(plain, 440.0 * Math.Pow(2.0, 2.0 / 12.0)).Should().BeLessThan(2.0);
        Cents(widened, 440.0 * Math.Pow(2.0, 12.0 / 12.0)).Should().BeLessThan(2.0);
    }

    [Fact]
    public void the_soundfont_engine_honours_the_registered_bend_range()
    {
        //Arrange
        var channel = new Channel(null, false);

        //Act
        var initial = channel.PitchBendRange;

        channel.SetRpnCoarse(0);
        channel.SetRpnFine(0);
        channel.DataEntryCoarse(12);
        channel.DataEntryFine(50);

        //Assert
        initial.Should().Be(2f);
        channel.PitchBendRange.Should().BeApproximately(12.5f, 0.001f);
    }

    private static double BendUpHertz(SfzInstrument instrument, bool useRegisteredParameter)
    {
        var synthesizer = new SfzSynthesizer(instrument, SampleRate) { MasterVolume = 1f };

        if (useRegisteredParameter)
        {
            synthesizer.ProcessMidiMessage(0, 0xB0, 101, 0);
            synthesizer.ProcessMidiMessage(0, 0xB0, 100, 0);
            synthesizer.ProcessMidiMessage(0, 0xB0, 6, 12);
        }

        synthesizer.ProcessMidiMessage(0, 0xE0, 127, 127);
        synthesizer.NoteOn(0, 60, 100);

        var left = new float[SampleRate];
        var right = new float[SampleRate];
        synthesizer.Render(left, right);

        return PitchProbe.Hertz(left, 4096, SampleRate);
    }

    private static double Cents(double measured, double expected) =>
        Math.Abs(PitchProbe.Cents(measured, expected));
}
