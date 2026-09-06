using System;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// The synthesizer's own contract: construction, the IMidiSynthesizer surface, gain arithmetic, the
/// tempo source, determinism, and resampling to the output rate.
/// </summary>
public class DecentSamplerSynthesizerTests
{
    private const string OneZonePreset = """
        <?xml version="1.0" encoding="UTF-8"?>
        <DecentSampler>
          <groups>
            <group>
              <sample path="Samples/half.wav" rootNote="60" loNote="0" hiNote="127" />
            </group>
          </groups>
        </DecentSampler>
        """;

    [Fact]
    public void constructor_rejects_a_null_instrument() =>
        ((Action)(() => new DecentSamplerSynthesizer(null, 44100)))
            .Should().Throw<ArgumentNullException>();

    [Fact]
    public void constructor_rejects_null_settings()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav");
        using var instrument = fixtures.LoadPreset(OneZonePreset);

        //Act
        var construct = () => new DecentSamplerSynthesizer(instrument, null);

        //Assert
        construct.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void a_new_synthesizer_reports_the_settings_it_was_given()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav");
        using var instrument = fixtures.LoadPreset(OneZonePreset);

        //Act
        using var _ = instrument;
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Assert
        synthesizer.SampleRate.Should().Be(DecentSamplerEngineFixtures.SampleRate);
        synthesizer.BlockSize.Should().Be(DecentSamplerEngineFixtures.BlockSize);
        synthesizer.MaximumPolyphony.Should().Be(DecentSamplerSynthesizerSettings.DefaultMaximumPolyphony);
        synthesizer.ChannelCount.Should().Be(16);
        synthesizer.ActiveVoiceCount.Should().Be(0);
        synthesizer.Instrument.Should().BeSameAs(instrument);
    }

    [Fact]
    public void a_note_on_plays_the_zone_at_its_arithmetic_level()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav", value: 0.5f);
        using var instrument = fixtures.LoadPreset(OneZonePreset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 60, 127);
        var (left, right) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);

        //Assert
        DecentSamplerRenderProbe.Rms(left).Should().BeApproximately(0.5, 0.002);
        DecentSamplerRenderProbe.Rms(right).Should().BeApproximately(0.5, 0.002);
        synthesizer.ActiveVoiceCount.Should().Be(1);
    }

    [Fact]
    public void a_note_on_with_velocity_zero_is_a_note_off()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav");
        using var instrument = fixtures.LoadPreset(OneZonePreset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 60, 0);
        DecentSamplerRenderProbe.RenderBlocks(synthesizer, 2);

        //Assert
        synthesizer.ActiveVoiceCount.Should().Be(0);
    }

    [Fact]
    public void a_note_on_of_velocity_zero_releases_the_sounding_note()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav");
        using var instrument = fixtures.LoadPreset(OneZonePreset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);
        synthesizer.NoteOn(0, 60, 100);
        DecentSamplerRenderProbe.RenderBlocks(synthesizer, 2);

        //Act
        synthesizer.ProcessMidiMessage(0, 0x90, 60, 0);
        var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 1.0);

        //Assert - the undocumented 0.5 s default release has run out well inside a second.
        DecentSamplerRenderProbe.Rms(left, left.Length - 1024, 1024).Should().BeLessThan(0.0005);
    }

    [Theory]
    [InlineData(127, 1.0)]
    [InlineData(64, 64.0 / 127.0)]
    [InlineData(1, 1.0 / 127.0)]
    public void velocity_scales_the_level_linearly_when_ampVelTrack_is_one(int velocity, double expected)
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav", value: 0.5f);
        using var instrument = fixtures.LoadPreset(OneZonePreset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 60, velocity);
        var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);

        //Assert
        DecentSamplerRenderProbe.Rms(left).Should().BeApproximately(0.5 * expected, 0.002);
    }

    [Fact]
    public void ampVelTrack_zero_ignores_velocity_and_intermediate_values_blend()
    {
        //Arrange
        const string preset = """
            <DecentSampler>
              <groups>
                <group ampVelTrack="0"><sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" /></group>
                <group ampVelTrack="0.5"><sample path="Samples/half.wav" rootNote="62" loNote="62" hiNote="62" /></group>
              </groups>
            </DecentSampler>
            """;

        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav", value: 0.5f);
        using var instrument = fixtures.LoadPreset(preset);

        //Act
        var noTracking = DecentSamplerRenderProbe.Synthesizer(instrument);
        noTracking.NoteOn(0, 60, 64);
        var (flat, _) = DecentSamplerRenderProbe.RenderBlocks(noTracking, 4);

        var halfTracking = DecentSamplerRenderProbe.Synthesizer(instrument);
        halfTracking.NoteOn(0, 62, 64);
        var (blended, _) = DecentSamplerRenderProbe.RenderBlocks(halfTracking, 4);

        //Assert - measured law: (1 - track) + track * velocity/127.
        DecentSamplerRenderProbe.Rms(flat).Should().BeApproximately(0.5, 0.002);
        DecentSamplerRenderProbe.Rms(blended)
            .Should().BeApproximately(0.5 * (0.5 + 0.5 * (64.0 / 127.0)), 0.002);
    }

    [Fact]
    public void volumes_multiply_across_the_three_levels_and_a_bare_number_is_linear()
    {
        //Arrange
        const string preset = """
            <DecentSampler>
              <groups volume="0.5">
                <group volume="0.5">
                  <sample path="Samples/one.wav" rootNote="60" loNote="60" hiNote="60" volume="0.5" />
                </group>
              </groups>
            </DecentSampler>
            """;

        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/one.wav", value: 1.0f);
        using var instrument = fixtures.LoadPreset(preset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 60, 127);
        var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);

        //Assert
        DecentSamplerRenderProbe.Rms(left).Should().BeApproximately(0.125, 0.002);
    }

    [Fact]
    public void a_decibel_volume_is_read_as_decibels()
    {
        //Arrange
        const string preset = """
            <DecentSampler>
              <groups>
                <group volume="-6dB"><sample path="Samples/one.wav" rootNote="60" loNote="60" hiNote="60" /></group>
              </groups>
            </DecentSampler>
            """;

        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/one.wav", value: 1.0f);
        using var instrument = fixtures.LoadPreset(preset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 60, 127);
        var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);

        //Assert
        DecentSamplerRenderProbe.Rms(left).Should().BeApproximately(0.501187, 0.002);
    }

    [Theory]
    [InlineData(-100.0, 1.41421, 0.0)]
    [InlineData(0.0, 1.0, 1.0)]
    [InlineData(100.0, 0.0, 1.41421)]
    public void pan_uses_the_measured_constant_power_law(double pan, double expectedLeft, double expectedRight)
    {
        //Arrange
        var preset = $"""
            <DecentSampler>
              <groups>
                <group pan="{pan.ToString(System.Globalization.CultureInfo.InvariantCulture)}">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """;

        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav", value: 0.5f);
        using var instrument = fixtures.LoadPreset(preset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 60, 127);
        var (left, right) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);

        //Assert - unity at centre, +3.01 dB hard over, digital silence in the other channel.
        DecentSamplerRenderProbe.Rms(left).Should().BeApproximately(0.5 * expectedLeft, 0.003);
        DecentSamplerRenderProbe.Rms(right).Should().BeApproximately(0.5 * expectedRight, 0.003);
    }

    [Fact]
    public void tuning_is_in_semitones_and_adds_across_levels()
    {
        //Arrange - 200 Hz at the root, then +12 semitones from the three tuning attributes together.
        const string preset = """
            <DecentSampler>
              <groups globalTuning="3">
                <group tuning="4">
                  <sample path="Samples/tone.wav" rootNote="60" loNote="60" hiNote="60" tuning="5" />
                </group>
              </groups>
            </DecentSampler>
            """;

        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteSineWav("Samples/tone.wav", frequency: 200.0);
        using var instrument = fixtures.LoadPreset(preset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 60, 127);
        var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.2);

        //Assert
        DecentSamplerRenderProbe
            .Frequency(left, 512, 4096, DecentSamplerEngineFixtures.SampleRate)
            .Should().BeApproximately(400.0, 2.0);
    }

    [Fact]
    public void pitchKeyTrack_zero_holds_the_pitch_whatever_the_key()
    {
        //Arrange
        const string preset = """
            <DecentSampler>
              <groups>
                <group pitchKeyTrack="0">
                  <sample path="Samples/tone.wav" rootNote="60" loNote="0" hiNote="127" />
                </group>
              </groups>
            </DecentSampler>
            """;

        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteSineWav("Samples/tone.wav", frequency: 300.0);
        using var instrument = fixtures.LoadPreset(preset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 72, 127);
        var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.2);

        //Assert
        DecentSamplerRenderProbe
            .Frequency(left, 512, 4096, DecentSamplerEngineFixtures.SampleRate)
            .Should().BeApproximately(300.0, 2.0);
    }

    [Fact]
    public void a_ninety_six_kilohertz_sample_plays_at_the_output_rate()
    {
        //Arrange
        const string preset = """
            <DecentSampler>
              <groups>
                <group><sample path="Samples/tone96.wav" rootNote="60" loNote="60" hiNote="60" /></group>
              </groups>
            </DecentSampler>
            """;

        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteSineWav("Samples/tone96.wav", frequency: 500.0, frames: 96000, sampleRate: 96000);
        using var instrument = fixtures.LoadPreset(preset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 60, 127);
        var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.2);

        //Assert - resampled to 44.1 kHz, the tone is still 500 Hz.
        DecentSamplerRenderProbe
            .Frequency(left, 512, 4096, DecentSamplerEngineFixtures.SampleRate)
            .Should().BeApproximately(500.0, 3.0);
    }

    [Fact]
    public void pitch_bend_moves_the_note_by_two_semitones_at_full_deflection()
    {
        //Arrange
        const string preset = """
            <DecentSampler>
              <groups>
                <group><sample path="Samples/tone.wav" rootNote="60" loNote="60" hiNote="60" /></group>
              </groups>
            </DecentSampler>
            """;

        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteSineWav("Samples/tone.wav", frequency: 400.0);
        using var instrument = fixtures.LoadPreset(preset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.ProcessMidiMessage(0, 0xE0, 127, 127);
        synthesizer.NoteOn(0, 60, 127);
        var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 0.2);

        //Assert
        DecentSamplerRenderProbe
            .Frequency(left, 512, 4096, DecentSamplerEngineFixtures.SampleRate)
            .Should().BeApproximately(400.0 * Math.Pow(2.0, 2.0 / 12.0), 3.0);
    }

    [Fact]
    public void master_volume_scales_the_whole_mix()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav", value: 0.5f);
        using var instrument = fixtures.LoadPreset(OneZonePreset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument, settings => settings.MasterVolume = 0.25f);

        //Act
        synthesizer.NoteOn(0, 60, 127);
        var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);

        //Assert
        DecentSamplerRenderProbe.Rms(left).Should().BeApproximately(0.125, 0.002);
    }

    [Fact]
    public void the_default_tempo_source_reports_a_hundred_and_twenty()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav");
        using var instrument = fixtures.LoadPreset(OneZonePreset);

        //Act
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Assert
        synthesizer.TempoSource.BeatsPerMinute.Should().Be(120.0);
    }

    [Fact]
    public void setting_the_tempo_source_to_null_leaves_a_usable_one()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav");
        using var instrument = fixtures.LoadPreset(OneZonePreset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.TempoSource = null;

        //Assert
        synthesizer.TempoSource.Should().NotBeNull();
    }

    [Fact]
    public void a_shared_tempo_source_can_be_handed_in()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav");
        using var instrument = fixtures.LoadPreset(OneZonePreset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);
        var tempo = new TempoSource { BeatsPerMinute = 90.0 };

        //Act
        synthesizer.TempoSource = tempo;

        //Assert
        synthesizer.TempoSource.Should().BeSameAs(tempo);
    }

    [Fact]
    public void the_instruments_problems_pass_through()
    {
        //Arrange - the preset names a sample that is not there.
        const string preset = """
            <DecentSampler>
              <groups>
                <group><sample path="Samples/missing.wav" rootNote="60" /></group>
              </groups>
            </DecentSampler>
            """;

        using var fixtures = DecentSamplerEngineFixtures.Create();
        using var instrument = fixtures.LoadPreset(preset);

        //Act
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Assert
        synthesizer.Problems.Should().NotBeEmpty();
        synthesizer.Problems.Should().Contain(problem => problem.Contains("missing.wav"));
    }

    [Fact]
    public void reset_silences_everything_and_reseeds()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav");
        using var instrument = fixtures.LoadPreset(OneZonePreset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);
        synthesizer.NoteOn(0, 60, 127);
        DecentSamplerRenderProbe.RenderBlocks(synthesizer, 2);

        //Act
        synthesizer.Reset();
        var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 2);

        //Assert
        synthesizer.ActiveVoiceCount.Should().Be(0);
        DecentSamplerRenderProbe.Peak(left).Should().Be(0.0);
    }

    [Fact]
    public void note_off_all_immediate_stops_at_once()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav");
        using var instrument = fixtures.LoadPreset(OneZonePreset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);
        synthesizer.NoteOn(0, 60, 127);
        DecentSamplerRenderProbe.RenderBlocks(synthesizer, 2);

        //Act
        synthesizer.NoteOffAll(immediate: true);
        var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 2);

        //Assert
        DecentSamplerRenderProbe.Peak(left).Should().Be(0.0);
    }

    [Fact]
    public void all_notes_off_releases_and_all_sound_off_cuts()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav");
        using var instrument = fixtures.LoadPreset(OneZonePreset);

        var releasing = DecentSamplerRenderProbe.Synthesizer(instrument);
        releasing.NoteOn(0, 60, 127);
        DecentSamplerRenderProbe.RenderBlocks(releasing, 2);

        var cutting = DecentSamplerRenderProbe.Synthesizer(instrument);
        cutting.NoteOn(0, 60, 127);
        DecentSamplerRenderProbe.RenderBlocks(cutting, 2);

        //Act
        releasing.ProcessMidiMessage(0, 0xB0, 123, 0);
        var (released, _) = DecentSamplerRenderProbe.RenderBlocks(releasing, 2);

        cutting.ProcessMidiMessage(0, 0xB0, 120, 0);
        var (cut, _) = DecentSamplerRenderProbe.RenderBlocks(cutting, 2);

        //Assert - the release runs the 0.5 s default, so audio is still there; all sound off is silence.
        DecentSamplerRenderProbe.Peak(released).Should().BeGreaterThan(0.1);
        DecentSamplerRenderProbe.Peak(cut).Should().Be(0.0);
    }

    [Fact]
    public void identical_input_renders_byte_for_byte_identically()
    {
        //Arrange
        const string preset = """
            <DecentSampler>
              <groups>
                <group seqMode="random" seqLength="4">
                  <sample path="Samples/a.wav" rootNote="60" loNote="60" hiNote="60" seqPosition="1" />
                  <sample path="Samples/b.wav" rootNote="60" loNote="60" hiNote="60" seqPosition="2" />
                  <sample path="Samples/c.wav" rootNote="60" loNote="60" hiNote="60" seqPosition="3" />
                  <sample path="Samples/d.wav" rootNote="60" loNote="60" hiNote="60" seqPosition="4" />
                </group>
              </groups>
            </DecentSampler>
            """;

        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/a.wav", value: 0.1f);
        fixtures.WriteConstantWav("Samples/b.wav", value: 0.2f);
        fixtures.WriteConstantWav("Samples/c.wav", value: 0.3f);
        fixtures.WriteConstantWav("Samples/d.wav", value: 0.4f);
        using var instrument = fixtures.LoadPreset(preset);

        //Act
        var first = RenderRun(instrument);
        var second = RenderRun(instrument);

        //Assert
        second.Should().Equal(first);
    }

    [Fact]
    public void polyphony_is_capped_by_the_settings()
    {
        //Arrange
        const string preset = """
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" pitchKeyTrack="0">
                  <sample path="Samples/half.wav" rootNote="60" loNote="0" hiNote="127" />
                </group>
              </groups>
            </DecentSampler>
            """;

        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav", value: 0.5f, frames: 44100);
        using var instrument = fixtures.LoadPreset(preset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(
            instrument, settings => settings.MaximumPolyphony = 8);

        //Act
        for (var key = 40; key < 60; key++)
        {
            synthesizer.NoteOn(0, key, 100);
        }

        DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);

        //Assert - the pool never grows; the quietest voice is stolen instead.
        synthesizer.ActiveVoiceCount.Should().BeLessThanOrEqualTo(8);
    }

    [Fact]
    public void a_continuous_zone_keeps_sounding_past_the_end_of_its_sample()
    {
        //Arrange - a tenth of a second of audio, rendered for a whole second.
        const string preset = """
            <DecentSampler>
              <groups>
                <group trigger="continuous" ampVelTrack="0">
                  <sample path="Samples/short.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """;

        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/short.wav", value: 0.5f, frames: 4410);
        using var instrument = fixtures.LoadPreset(preset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act
        var (left, _) = DecentSamplerRenderProbe.RenderSeconds(synthesizer, 1.0);

        //Assert
        DecentSamplerRenderProbe.Rms(left, left.Length - 1024, 1024).Should().BeApproximately(0.5, 0.005);
    }

    [Fact]
    public void poly_and_channel_pressure_are_accepted_without_a_consumer()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav");
        using var instrument = fixtures.LoadPreset(OneZonePreset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act
        var send = () =>
        {
            synthesizer.ProcessMidiMessage(0, 0xD0, 90, 0);
            synthesizer.ProcessMidiMessage(0, 0xA0, 60, 70);
            synthesizer.ProcessMidiMessage(0, 0xC0, 12, 0);
            synthesizer.ProcessMidiMessage(99, 0x90, 60, 100);
            synthesizer.NoteOn(0, 200, 100);
            synthesizer.NoteOff(0, -5);
            DecentSamplerRenderProbe.RenderBlocks(synthesizer, 2);
        };

        //Assert
        send.Should().NotThrow();
        synthesizer.ActiveVoiceCount.Should().Be(0);
    }

    [Fact]
    public void reset_all_controllers_clears_a_zone_filter()
    {
        //Arrange
        const string preset = """
            <DecentSampler>
              <groups>
                <group ampVelTrack="0">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60"
                          loCC64="90" hiCC64="127" />
                </group>
              </groups>
            </DecentSampler>
            """;

        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav", value: 0.5f);
        using var instrument = fixtures.LoadPreset(preset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.ProcessMidiMessage(0, 0xB0, 64, 100);
        synthesizer.ProcessMidiMessage(0, 0xB0, 121, 0);
        synthesizer.NoteOn(0, 60, 100);
        var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 2);

        //Assert - the pedal value went back to zero, so the zone no longer matches.
        DecentSamplerRenderProbe.Peak(left).Should().Be(0.0);
    }

    [Fact]
    public void render_rejects_mismatched_buffers()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav");
        using var instrument = fixtures.LoadPreset(OneZonePreset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act
        var render = () => synthesizer.Render(new float[64], new float[32]);

        //Assert
        render.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void a_partial_block_carries_on_where_the_last_render_stopped()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav", value: 0.5f);
        using var instrument = fixtures.LoadPreset(OneZonePreset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);
        synthesizer.NoteOn(0, 60, 127);

        //Act - three renders that do not line up with the 64-frame block.
        var left = new float[100];
        var right = new float[100];
        synthesizer.Render(left, right);
        synthesizer.Render(left, right);
        synthesizer.Render(left, right);

        //Assert
        DecentSamplerRenderProbe.Rms(left).Should().BeApproximately(0.5, 0.002);
    }

    private static float[] RenderRun(DecentSamplerInstrument instrument)
    {
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);
        var output = new float[16 * DecentSamplerEngineFixtures.BlockSize];
        var right = new float[output.Length];

        for (var note = 0; note < 8; note++)
        {
            synthesizer.NoteOn(0, 60, 100);
            var left = new float[2 * DecentSamplerEngineFixtures.BlockSize];
            var rightBlock = new float[left.Length];
            synthesizer.Render(left, rightBlock);
            Array.Copy(left, 0, output, note * left.Length, left.Length);
            synthesizer.NoteOff(0, 60);
        }

        return output;
    }
}
