using System;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.ModestSynth.Internal.Gm;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// The voice architecture the General MIDI bank is written against: the filter and its envelope, the
/// exponential curves, the layers, the pitch envelope, the delayed low-frequency oscillator, the
/// velocity and key scaling, the stereo unison and the per-program insert effect.
/// </summary>
/// <remarks>
/// Most of these measure ONE VOICE with no synthesizer around it, so what is being tested is the
/// voice rather than the channel, the sends or the master gain. The ones that need a channel - the
/// modulation wheel, the insert effect - say so.
/// </remarks>
public class GeneralMidiVoiceTests
{
    // ------------------------------------------------------------------- the filter

    [Fact]
    public void the_filter_darkens_a_sound_as_its_cutoff_falls()
    {
        //Arrange
        // Noise rather than a saw: a saw's energy sits almost entirely in its fundamental, so its
        // centroid barely moves however the filter is set, and a measurement of "darker" would be
        // measuring the note rather than the filter.
        GmVoiceSpec open = GmVoiceProbe.Spec(GmTone.Noise);
        open.Filter = new GmFilterSpec
        {
            Mode = GmFilterMode.LowPass, Cutoff = 8000.0, KeyTracking = 0.0,
        };

        GmVoiceSpec closed = GmVoiceProbe.Spec(GmTone.Noise);
        closed.Filter = new GmFilterSpec
        {
            Mode = GmFilterMode.LowPass, Cutoff = 700.0, KeyTracking = 0.0,
        };

        //Act
        var bright = GmVoiceProbe.Render(open);
        var dark = GmVoiceProbe.Render(closed);

        //Assert
        GmProbe.Centroid(dark.Left).Should().BeLessThan(GmProbe.Centroid(bright.Left) * 0.3);
    }

    [Fact]
    public void the_high_pass_takes_the_body_out_instead()
    {
        //Arrange
        GmVoiceSpec low = GmVoiceProbe.Spec(GmTone.Noise);
        low.Filter = new GmFilterSpec { Mode = GmFilterMode.LowPass, Cutoff = 1200.0, KeyTracking = 0.0 };

        GmVoiceSpec high = GmVoiceProbe.Spec(GmTone.Noise);
        high.Filter = new GmFilterSpec { Mode = GmFilterMode.HighPass, Cutoff = 1200.0, KeyTracking = 0.0 };

        //Act
        var dark = GmVoiceProbe.Render(low);
        var bright = GmVoiceProbe.Render(high);

        //Assert
        GmProbe.Centroid(bright.Left).Should().BeGreaterThan(GmProbe.Centroid(dark.Left) * 3.0);
    }

    [Fact]
    public void the_filter_envelope_moves_the_spectrum_while_the_note_sounds()
    {
        //Arrange
        GmVoiceSpec spec = GmVoiceProbe.Spec(GmTone.Noise);
        spec.Filter = new GmFilterSpec
        {
            Mode = GmFilterMode.LowPass,
            Cutoff = 500.0,
            KeyTracking = 0.0,
            EnvelopeOctaves = 4.0,

            // A whole second of decay, because the shortest window a spectrum can be measured over
            // is a sixth of a second and an exponential is most of the way down inside that.
            Envelope = new GmEnvelopeSpec { Attack = 0.002, Decay = 1.0, Sustain = 0.0, Release = 0.05 },
        };

        //Act
        var render = GmVoiceProbe.Render(spec, hold: 2.0, tail: 0.1);

        //Assert
        // The envelope opens the filter four octaves and then closes it again, so the start of the
        // note is far brighter than the end of it.
        int window = Spectrum.BlockLength;
        double early = GmProbe.Centroid(Slice(render.Left, 0, window), window);
        double late = GmProbe.Centroid(Slice(render.Left, GmProbe.SampleRate * 3 / 2, window), window);

        early.Should().BeGreaterThan(late * 3.0);
    }

    [Fact]
    public void a_harder_note_is_brighter_as_well_as_louder()
    {
        //Arrange
        GmVoiceSpec spec = GmVoiceProbe.Spec(GmTone.Noise);
        spec.Filter = new GmFilterSpec
        {
            Mode = GmFilterMode.LowPass, Cutoff = 1200.0, KeyTracking = 0.0, VelocityOctaves = 2.5,
        };
        spec.VelocityToLevel = 0.9;

        //Act
        var soft = GmVoiceProbe.Render(spec, velocity: 30);
        var hard = GmVoiceProbe.Render(spec, velocity: 127);

        //Assert
        GmProbe.Centroid(hard.Left).Should().BeGreaterThan(GmProbe.Centroid(soft.Left) * 1.5);
        GmProbe.Rms(hard.Left).Should().BeGreaterThan(GmProbe.Rms(soft.Left) * 2.0);
    }

    [Fact]
    public void key_tracking_opens_the_filter_as_the_keyboard_climbs()
    {
        //Arrange
        GmVoiceSpec spec = GmVoiceProbe.Spec(GmTone.Noise);
        spec.Filter = new GmFilterSpec
        {
            Mode = GmFilterMode.LowPass, Cutoff = 900.0, KeyTracking = 1.0,
        };

        //Act
        var low = GmVoiceProbe.Render(spec, key: 36);
        var high = GmVoiceProbe.Render(spec, key: 84);

        //Assert
        // Four octaves up the keyboard is four octaves up the cutoff at full tracking.
        GmProbe.Centroid(high.Left).Should().BeGreaterThan(GmProbe.Centroid(low.Left) * 4.0);
    }

    [Fact]
    public void a_program_with_no_filter_costs_nothing_and_changes_nothing()
    {
        //Arrange
        GmVoiceSpec spec = GmVoiceProbe.Spec(GmTone.Saw);

        //Act
        var render = GmVoiceProbe.Render(spec);

        //Assert
        spec.Filter.Mode.Should().Be(GmFilterMode.Off);
        GmProbe.Rms(render.Left).Should().BeGreaterThan(0.05);
    }

    // ---------------------------------------------------------------- the envelopes

    [Fact]
    public void the_decay_falls_exponentially_rather_than_in_a_straight_line()
    {
        //Arrange
        GmVoiceSpec spec = GmVoiceProbe.Spec();
        spec.Amplitude = new GmEnvelopeSpec
        {
            Attack = 0.001, Decay = 1.2, Sustain = 0.0, Release = 0.05,
        };

        //Act
        var render = GmVoiceProbe.Render(spec, hold: 1.0, tail: 0.05);

        //Assert
        // An exponential loses the same FRACTION over each equal stretch of time, so the ratio
        // between successive windows is constant. A straight line would give falling ratios.
        int window = GmProbe.SampleRate / 10;
        double first = GmProbe.Rms(render.Left, GmProbe.SampleRate / 10, window);
        double second = GmProbe.Rms(render.Left, 3 * GmProbe.SampleRate / 10, window);
        double third = GmProbe.Rms(render.Left, 5 * GmProbe.SampleRate / 10, window);

        double earlyRatio = second / first;
        double lateRatio = third / second;

        earlyRatio.Should().BeApproximately(lateRatio, 0.06);
        earlyRatio.Should().BeLessThan(0.75);
    }

    [Fact]
    public void the_release_falls_exponentially_too_and_finishes_when_it_says_it_will()
    {
        //Arrange
        GmVoiceSpec spec = GmVoiceProbe.Spec();
        spec.Amplitude = new GmEnvelopeSpec
        {
            Attack = 0.002, Decay = 0.05, Sustain = 1.0, Release = 0.4,
        };

        //Act
        var render = GmVoiceProbe.Render(spec, hold: 0.3, tail: 0.6);

        //Assert
        int releaseStart = (int)(0.3 * GmProbe.SampleRate);
        int window = GmProbe.SampleRate / 50;

        double atStart = GmProbe.Rms(render.Left, releaseStart, window);
        double halfway = GmProbe.Rms(render.Left, releaseStart + (GmProbe.SampleRate / 5), window);
        double afterwards = GmProbe.Rms(render.Left, releaseStart + (GmProbe.SampleRate / 2), window);

        halfway.Should().BeLessThan(atStart * 0.2);
        afterwards.Should().BeApproximately(0.0, 1e-5);
    }

    [Fact]
    public void a_higher_note_runs_its_envelope_faster_when_key_scaling_is_on()
    {
        //Arrange
        GmVoiceSpec spec = GmVoiceProbe.Spec();
        spec.Amplitude = new GmEnvelopeSpec
        {
            Attack = 0.001, Decay = 2.0, Sustain = 0.0, Release = 0.05,
        };
        spec.KeyToTime = 1.0;

        //Act
        var low = GmVoiceProbe.Render(spec, key: 48, hold: 1.0, tail: 0.05);
        var high = GmVoiceProbe.Render(spec, key: 84, hold: 1.0, tail: 0.05);

        //Assert
        // Two octaves above middle C at full key scaling is a quarter of the decay time; three
        // octaves above it is an eighth.
        int window = GmProbe.SampleRate / 20;
        int late = GmProbe.SampleRate / 2;

        GmProbe.Rms(high.Left, late, window)
            .Should().BeLessThan(GmProbe.Rms(low.Left, late, window) * 0.25);
    }

    [Fact]
    public void a_harder_note_starts_faster_when_velocity_drives_the_attack()
    {
        //Arrange
        GmVoiceSpec spec = GmVoiceProbe.Spec();
        spec.Amplitude = new GmEnvelopeSpec
        {
            Attack = 0.25, Decay = 1.0, Sustain = 1.0, Release = 0.05,
        };
        spec.VelocityToAttack = 1.0;
        spec.VelocityToLevel = 0.0;

        //Act
        var soft = GmVoiceProbe.Render(spec, velocity: 1);
        var hard = GmVoiceProbe.Render(spec, velocity: 127);

        //Assert
        int window = GmProbe.SampleRate / 100;
        int early = GmProbe.SampleRate / 50;

        GmProbe.Rms(hard.Left, early, window)
            .Should().BeGreaterThan(GmProbe.Rms(soft.Left, early, window) * 3.0);
    }

    // ------------------------------------------------------------------- the layers

    [Fact]
    public void two_layers_sum_to_exactly_the_two_of_them_rendered_alone()
    {
        //Arrange
        GmVoiceSpec first = GmVoiceProbe.Spec(GmTone.Sine);
        // Both layers are sines: a waveform that asks for a RANDOM START PHASE would start at a
        // different point depending on which slot it occupied, and the test would be measuring that
        // rather than the summing.
        GmVoiceSpec second = GmVoiceProbe.Spec(GmTone.Sine);
        second.Layers[0].Level = 0.4;
        second.Layers[0].Transpose = 12.0;

        GmVoiceSpec both = GmVoiceProbe.Spec(GmTone.Sine);
        both.Layers =
        [
            both.Layers[0],
            GmVoiceProbe.Layer(GmTone.Sine, 0.4, 12.0),
        ];

        //Act
        var one = GmVoiceProbe.Render(first);
        var two = GmVoiceProbe.Render(second);
        var together = GmVoiceProbe.Render(both);

        //Assert
        double worst = 0.0;

        for (int i = 0; i < together.Left.Length; i++)
        {
            double difference = Math.Abs(together.Left[i] - (one.Left[i] + two.Left[i]));
            if (difference > worst) { worst = difference; }
        }

        worst.Should().BeLessThan(1e-6);
    }

    [Fact]
    public void a_layer_may_carry_an_envelope_of_its_own()
    {
        //Arrange
        GmVoiceSpec plain = GmVoiceProbe.Spec(GmTone.Sine);
        plain.Layers[0].Level = 0.2;

        GmVoiceSpec struck = GmVoiceProbe.Spec(GmTone.Sine);
        struck.Layers[0].Level = 0.2;
        struck.Layers =
        [
            struck.Layers[0],
            new GmLayerSpec
            {
                Tone = GmTone.Noise,
                Level = 1.0,
                Envelope = new GmEnvelopeSpec
                {
                    Attack = 0.0005, Decay = 0.08, Sustain = 0.0, Release = 0.01,
                },
            },
        ];

        //Act
        var without = GmVoiceProbe.Render(plain, hold: 0.4, tail: 0.1);
        var with = GmVoiceProbe.Render(struck, hold: 0.4, tail: 0.1);

        //Assert
        // The noise layer is a burst on the front of the note. It runs its OWN envelope, so it is
        // gone a tenth of a second later while the voice carries on unchanged - which is exactly the
        // thing a single envelope for the whole voice could not do.
        int transient = GmProbe.SampleRate / 100;
        int late = GmProbe.SampleRate / 4;

        GmProbe.Rms(with.Left, 0, transient)
            .Should().BeGreaterThan(GmProbe.Rms(without.Left, 0, transient) * 2.0);

        GmProbe.Rms(with.Left, late, transient)
            .Should().BeApproximately(GmProbe.Rms(without.Left, late, transient), 1e-6);
    }

    // --------------------------------------------------- the pitch envelope and the LFO

    [Fact]
    public void the_pitch_envelope_starts_the_note_off_its_pitch_and_settles_it()
    {
        //Arrange
        GmVoiceSpec spec = GmVoiceProbe.Spec();
        spec.PitchEnvelopeSemitones = 12.0;

        // Slower than a drum's pitch drop would be, because the shortest window a spectrum can be
        // measured over is a sixth of a second and a forty-millisecond drop would be over inside it.
        spec.PitchEnvelopeSeconds = 0.6;
        spec.Amplitude = new GmEnvelopeSpec { Attack = 0.001, Decay = 4.0, Sustain = 1.0, Release = 0.05 };

        //Act
        var render = GmVoiceProbe.Render(spec, key: 69, hold: 2.0, tail: 0.05);

        //Assert
        // A4 is 440 Hz; the note starts an octave above it and has settled onto it a second and a
        // half later.
        Integration.RenderProbe.Frequency(render.Left, 0).Should().BeGreaterThan(560.0);
        Integration.RenderProbe.Frequency(render.Left, GmProbe.SampleRate * 3 / 2)
            .Should().BeApproximately(440.0, 4.0);
    }

    [Fact]
    public void the_low_frequency_oscillator_waits_for_its_delay_before_it_modulates()
    {
        //Arrange
        GmVoiceSpec spec = GmVoiceProbe.Spec();

        // Routed to the LEVEL rather than to the pitch, because the delay is what is being measured
        // and a level is readable inside a hundredth of a second where a pitch is not.
        spec.Lfo = new GmLfoSpec
        {
            Rate = 6.0, Delay = 0.5, FadeIn = 0.05, ToLevel = 0.9,
        };
        spec.Amplitude = new GmEnvelopeSpec { Attack = 0.001, Decay = 4.0, Sustain = 1.0, Release = 0.05 };

        //Act
        var render = GmVoiceProbe.Render(spec, key: 69, hold: 1.4, tail: 0.05);

        //Assert
        Ripple(render.Left, GmProbe.SampleRate / 10, GmProbe.SampleRate * 2 / 5).Should().BeLessThan(0.1);
        Ripple(render.Left, GmProbe.SampleRate * 3 / 4, GmProbe.SampleRate * 5 / 4).Should().BeGreaterThan(0.6);
    }

    [Fact]
    public void the_modulation_wheel_deepens_the_vibrato_on_a_program_that_has_none()
    {
        //Arrange
        GeneralMidiSynthesizer plain = GmProbe.BuildForProgram((int)GeneralMidiProgram.ChurchOrgan);
        GeneralMidiSynthesizer modulated = GmProbe.BuildForProgram((int)GeneralMidiProgram.ChurchOrgan);

        //Act
        modulated.ProcessMidiMessage(0, 0xB0, 1, 127);

        var straight = GmProbe.PlayNote(plain, 1, 69, 100, 1.4, 0.1);
        var wobbling = GmProbe.PlayNote(modulated, 1, 69, 100, 1.4, 0.1);

        //Assert
        double[] steady = new double[4];
        double[] moving = new double[4];

        for (int i = 0; i < 4; i++)
        {
            int offset = (GmProbe.SampleRate * 3 / 4) + (i * Spectrum.BlockLength);
            steady[i] = Integration.RenderProbe.Frequency(straight.Left, offset);
            moving[i] = Integration.RenderProbe.Frequency(wobbling.Left, offset);
        }

        Spread(steady).Should().BeLessThan(3.0);
        Spread(moving).Should().BeGreaterThan(5.0);
    }

    [Fact]
    public void the_low_frequency_oscillator_can_modulate_the_level_instead()
    {
        //Arrange
        GmVoiceSpec spec = GmVoiceProbe.Spec();
        spec.Lfo = new GmLfoSpec { Rate = 6.0, Delay = 0.0, FadeIn = 0.02, ToLevel = 0.9 };
        spec.Amplitude = new GmEnvelopeSpec { Attack = 0.001, Decay = 2.0, Sustain = 1.0, Release = 0.05 };

        //Act
        var render = GmVoiceProbe.Render(spec, hold: 1.0, tail: 0.05);

        //Assert
        // Six cycles a second over a second: somewhere in there is a trough at a tenth of the level
        // of the peaks.
        int window = GmProbe.SampleRate / 60;
        double quietest = double.MaxValue;
        double loudest = 0.0;

        for (int offset = GmProbe.SampleRate / 10; offset + window < GmProbe.SampleRate; offset += window)
        {
            double level = GmProbe.Rms(render.Left, offset, window);
            if (level < quietest) { quietest = level; }
            if (level > loudest) { loudest = level; }
        }

        quietest.Should().BeLessThan(loudest * 0.35);
    }

    // ------------------------------------------------------- unison and the insert effect

    [Fact]
    public void a_stereo_unison_widens_a_layer_that_would_otherwise_be_centred()
    {
        //Arrange
        GmVoiceSpec single = GmVoiceProbe.Spec(GmTone.Saw);

        GmVoiceSpec wide = GmVoiceProbe.Spec(GmTone.Saw);
        wide.Layers[0].Unison = 3;
        wide.Layers[0].UnisonDetuneCents = 10.0;
        wide.Layers[0].UnisonSpread = 0.8;

        //Act
        var centred = GmVoiceProbe.Render(single);
        var spread = GmVoiceProbe.Render(wide);

        //Assert
        GmProbe.LargestDifference(centred.Left, centred.Right).Should().Be(0.0);
        GmProbe.LargestDifference(spread.Left, spread.Right).Should().BeGreaterThan(0.05);
    }

    [Fact]
    public void the_insert_effect_a_program_names_changes_what_the_channel_sounds_like()
    {
        //Arrange
        GeneralMidiSynthesizer with = GmProbe.BuildForProgram((int)GeneralMidiProgram.ElectricPiano1);
        GeneralMidiSynthesizer without = GmProbe.BuildForProgram(
            (int)GeneralMidiProgram.ElectricPiano1, settings => settings.EnableInsertEffects = false);

        //Act
        var effected = GmProbe.PlayNote(with, 1, 60, 100, 0.6, 0.3);
        var plain = GmProbe.PlayNote(without, 1, 60, 100, 0.6, 0.3);

        //Assert
        GmProbe.LargestDifference(effected.Left, plain.Left).Should().BeGreaterThan(0.005);
    }

    [Fact]
    public void a_program_that_names_no_insert_effect_is_unaffected_by_switching_them_off()
    {
        //Arrange
        GeneralMidiSynthesizer with = GmProbe.BuildForProgram((int)GeneralMidiProgram.Celesta);
        GeneralMidiSynthesizer without = GmProbe.BuildForProgram(
            (int)GeneralMidiProgram.Celesta, settings => settings.EnableInsertEffects = false);

        //Act
        var one = GmProbe.PlayNote(with, 1, 72, 100, 0.4, 0.3);
        var two = GmProbe.PlayNote(without, 1, 72, 100, 0.4, 0.3);

        //Assert
        GmProbe.LargestDifference(one.Left, two.Left).Should().Be(0.0);
    }

    [Fact]
    public void the_insert_effect_runs_once_for_the_channel_rather_than_once_per_note()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.BuildForProgram((int)GeneralMidiProgram.ElectricPiano1);

        //Act
        // Two notes on one channel share one phaser, so the second note joins a sweep that is
        // already running rather than starting one of its own.
        synthesizer.NoteOn(1, 60, 100);
        GmProbe.Render(synthesizer, 0.3);
        synthesizer.NoteOn(1, 60, 100);
        var (left, _) = GmProbe.Render(synthesizer, 0.3);

        GeneralMidiSynthesizer fresh = GmProbe.BuildForProgram((int)GeneralMidiProgram.ElectricPiano1);
        fresh.NoteOn(1, 60, 100);
        var (alone, _) = GmProbe.Render(fresh, 0.3);

        //Assert
        GmProbe.LargestDifference(left, alone).Should().BeGreaterThan(0.001);
    }

    private static float[] Slice(float[] samples, int offset, int length)
    {
        float[] slice = new float[length];
        Array.Copy(samples, offset, slice, 0, Math.Min(length, samples.Length - offset));
        return slice;
    }

    // How far the level wanders over a stretch, as a fraction of its own peak: 0 is dead steady.
    private static double Ripple(float[] samples, int from, int to)
    {
        int window = GmProbe.SampleRate / 60;
        double quietest = double.MaxValue;
        double loudest = 0.0;

        for (int offset = from; offset + window <= to; offset += window)
        {
            double level = GmProbe.Rms(samples, offset, window);
            if (level < quietest) { quietest = level; }
            if (level > loudest) { loudest = level; }
        }

        return loudest <= 0.0 ? 0.0 : (loudest - quietest) / loudest;
    }

    private static double Spread(double[] values)
    {
        double lowest = double.MaxValue;
        double highest = double.MinValue;

        for (int i = 0; i < values.Length; i++)
        {
            if (values[i] < lowest) { lowest = values[i]; }
            if (values[i] > highest) { highest = values[i]; }
        }

        return highest - lowest;
    }
}
