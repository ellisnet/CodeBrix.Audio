using CodeBrix.Audio.ModestSynth.Internal;
using CodeBrix.Audio.ModestSynth.Oscillators;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// The one note contract every waveform in the package answers to, so that whatever plays notes - the
/// standalone synthesizer, or the Decent Sampler adapter - drives them all the same way.
/// </summary>
public class ModestVoiceOscillatorTests
{
    [Fact]
    public void every_registered_waveform_is_a_voice_oscillator()
    {
        //Act, Assert
        foreach (ModestOscillatorRegistration entry in ModestSynthRegistry.OscillatorRegistrations)
        {
            entry.Factory().Should().BeAssignableTo<IModestVoiceOscillator>();
        }
    }

    [Theory]
    [InlineData(ModestWaveform.Sine)]
    [InlineData(ModestWaveform.Saw)]
    [InlineData(ModestWaveform.Square)]
    [InlineData(ModestWaveform.Triangle)]
    [InlineData(ModestWaveform.Noise)]
    [InlineData(ModestWaveform.Pluck1)]
    [InlineData(ModestWaveform.Wavetable)]
    [InlineData(ModestWaveform.Harmonic)]
    [InlineData(ModestWaveform.Fm6Op)]
    public void the_key_goes_down_at_a_note_on_and_up_at_a_note_off(ModestWaveform waveform)
    {
        //Arrange
        IModestVoiceOscillator oscillator =
            (IModestVoiceOscillator)ModestOscillatorFactory.Create(waveform);
        oscillator.SetSampleRate(48000);
        oscillator.SetFrequency(220.0);

        //Act
        oscillator.Reset(0.0);
        oscillator.NoteOn(100);
        bool down = oscillator.IsKeyDown;
        oscillator.NoteOff();

        //Assert
        down.Should().BeTrue();
        oscillator.IsKeyDown.Should().BeFalse();
    }

    [Theory]
    [InlineData(ModestWaveform.Sine)]
    [InlineData(ModestWaveform.Saw)]
    [InlineData(ModestWaveform.Square)]
    [InlineData(ModestWaveform.Triangle)]
    [InlineData(ModestWaveform.Noise)]
    [InlineData(ModestWaveform.Wavetable)]
    [InlineData(ModestWaveform.Harmonic)]
    public void a_waveform_that_simply_runs_never_finishes_on_its_own(ModestWaveform waveform)
    {
        //Arrange
        IModestVoiceOscillator oscillator =
            (IModestVoiceOscillator)ModestOscillatorFactory.Create(waveform);
        oscillator.SetSampleRate(48000);
        oscillator.SetFrequency(220.0);
        float[] block = new float[4096];

        //Act
        oscillator.Reset(0.0);
        oscillator.NoteOn(100);
        oscillator.NoteOff();

        for (int i = 0; i < 40; i++) { oscillator.Render(block); }

        //Assert
        oscillator.IsFinished.Should().BeFalse();
    }

    [Fact]
    public void a_plucked_string_finishes_once_it_has_died_away()
    {
        //Arrange
        Pluck1Oscillator oscillator = new Pluck1Oscillator();
        oscillator.SetSampleRate(48000);
        oscillator.SetFrequency(2093.0);
        oscillator.Damping = 0.0;
        float[] block = new float[4096];

        //Act
        bool finishedBeforeThePluck = oscillator.IsFinished;
        oscillator.Reset(0.0);
        oscillator.NoteOn(100);
        bool finishedAtTheStart = oscillator.IsFinished;

        int blocks = 0;
        while (!oscillator.IsFinished && blocks < 400)
        {
            oscillator.Render(block);
            blocks++;
        }

        //Assert
        finishedBeforeThePluck.Should().BeTrue();
        finishedAtTheStart.Should().BeFalse();
        oscillator.IsFinished.Should().BeTrue();
        blocks.Should().BeLessThan(400);
    }

    [Fact]
    public void a_note_on_does_not_disturb_the_phase_of_a_running_waveform()
    {
        //Arrange
        SineOscillator plain = new SineOscillator();
        SineOscillator struck = new SineOscillator();
        plain.SetSampleRate(48000);
        struck.SetSampleRate(48000);
        plain.SetFrequency(440.0);
        struck.SetFrequency(440.0);

        float[] first = new float[256];
        float[] second = new float[256];

        //Act
        plain.Reset(0.25);
        struck.Reset(0.25);
        struck.NoteOn(64);
        plain.Render(first);
        struck.Render(second);

        //Assert
        first.Should().Equal(second);
    }
}
