using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// The extension seam: the registry an add-on package plugs oscillator waveforms and effect types into,
/// what happens to a preset whose waveform nothing covers, and how a registered voice source is driven.
/// </summary>
/// <remarks>
/// Every test builds its OWN registry rather than touching the process-wide one, so the suite never
/// depends on what some other test registered.
/// </remarks>
public class DecentSamplerExtensionsTests
{
    private const string OscillatorPreset = """
        <DecentSampler>
          <groups>
            <group ampVelTrack="0">
              <oscillator waveform="fm6op" />
            </group>
          </groups>
        </DecentSampler>
        """;

    [Fact]
    public void the_core_registers_no_oscillator_waveforms_of_its_own()
    {
        //Arrange
        //Act
        //Assert - every waveform in the format lives in the add-on.
        DecentSamplerExtensions.IsOscillatorRegistered("sine").Should().BeFalse();
        DecentSamplerExtensions.IsOscillatorRegistered("fm6op").Should().BeFalse();
    }

    [Fact]
    public void a_registry_reports_what_it_holds()
    {
        //Arrange
        var registry = new DecentSamplerExtensionRegistry();

        //Act
        registry.RegisterOscillator("fm6op", _ => new FakeVoiceSource());
        registry.RegisterEffect("bit_crusher", _ => new FakeEffect());

        //Assert
        registry.IsOscillatorRegistered("fm6op").Should().BeTrue();
        registry.IsEffectRegistered("bit_crusher").Should().BeTrue();
        registry.RegisteredOscillators.Should().Equal("fm6op");
        registry.RegisteredEffects.Should().Equal("bit_crusher");
    }

    [Fact]
    public void names_fold_case_and_separators()
    {
        //Arrange
        var registry = new DecentSamplerExtensionRegistry();

        //Act
        registry.RegisterOscillator("white_noise", _ => new FakeVoiceSource());

        //Assert
        registry.IsOscillatorRegistered("WHITE-NOISE").Should().BeTrue();
        registry.IsOscillatorRegistered("whiteNoise").Should().BeTrue();
        registry.IsOscillatorRegistered("pink_noise").Should().BeFalse();
    }

    [Fact]
    public void a_second_registration_replaces_the_first()
    {
        //Arrange
        var registry = new DecentSamplerExtensionRegistry();
        var first = new FakeVoiceSource();
        var second = new FakeVoiceSource();

        //Act
        registry.RegisterOscillator("sine", _ => first);
        registry.RegisterOscillator("sine", _ => second);
        registry.TryGetOscillatorFactory("sine", out var factory);

        //Assert
        factory(null).Should().BeSameAs(second);
        registry.RegisteredOscillators.Count.Should().Be(1);
    }

    [Fact]
    public void clearing_a_registry_empties_it()
    {
        //Arrange
        var registry = new DecentSamplerExtensionRegistry();
        registry.RegisterOscillator("saw", _ => new FakeVoiceSource());

        //Act
        registry.Clear();

        //Assert
        registry.IsOscillatorRegistered("saw").Should().BeFalse();
        registry.RegisteredOscillators.Should().BeEmpty();
    }

    [Fact]
    public void registration_rejects_a_null_or_blank_name_and_a_null_factory()
    {
        //Arrange
        var registry = new DecentSamplerExtensionRegistry();

        //Act
        var nullName = () => registry.RegisterOscillator(null, _ => new FakeVoiceSource());
        var blankName = () => registry.RegisterEffect("  ", _ => new FakeEffect());
        var nullFactory = () => registry.RegisterOscillator("saw", null);

        //Assert
        nullName.Should().Throw<ArgumentNullException>();
        blankName.Should().Throw<ArgumentException>();
        nullFactory.Should().Throw<ArgumentNullException>();
    }

    [Fact]
    public void an_unregistered_waveform_is_silent_and_reported()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        using var instrument = fixtures.LoadPreset(OscillatorPreset);

        //Act
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(
            instrument, settings => settings.Extensions = new DecentSamplerExtensionRegistry());

        synthesizer.NoteOn(0, 60, 127);
        var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);

        //Assert
        DecentSamplerRenderProbe.Peak(left).Should().Be(0.0);
        synthesizer.Problems.Should().Contain(
            "oscillator waveform 'fm6op' needs CodeBrix.Audio.ModestSynth: reference it and call " +
            "ModestSynth.Register() before loading");
        synthesizer.UnsupportedFeatures.Should().Contain("oscillator waveform 'fm6op'");
    }

    [Fact]
    public void a_sampled_group_still_plays_beside_an_unregistered_oscillator()
    {
        //Arrange
        const string preset = """
            <DecentSampler>
              <groups>
                <group ampVelTrack="0"><oscillator waveform="fm6op" /></group>
                <group ampVelTrack="0">
                  <sample path="Samples/half.wav" rootNote="60" loNote="60" hiNote="60" />
                </group>
              </groups>
            </DecentSampler>
            """;

        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteConstantWav("Samples/half.wav", value: 0.5f);
        using var instrument = fixtures.LoadPreset(preset);

        //Act
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(
            instrument, settings => settings.Extensions = new DecentSamplerExtensionRegistry());

        synthesizer.NoteOn(0, 60, 127);
        var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);

        //Assert
        DecentSamplerRenderProbe.Rms(left).Should().BeApproximately(0.5, 0.002);
    }

    [Fact]
    public void a_registered_voice_source_is_driven_by_the_voice_runtime()
    {
        //Arrange
        var registry = new DecentSamplerExtensionRegistry();
        var sources = new List<FakeVoiceSource>();

        registry.RegisterOscillator("fm6op", context =>
        {
            var source = new FakeVoiceSource { Context = context };
            sources.Add(source);
            return source;
        });

        using var fixtures = DecentSamplerEngineFixtures.Create();
        using var instrument = fixtures.LoadPreset(OscillatorPreset);

        //Act
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(
            instrument, settings => settings.Extensions = registry);

        synthesizer.NoteOn(0, 69, 100);
        var (left, _) = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 4);
        synthesizer.NoteOff(0, 69);
        DecentSamplerRenderProbe.RenderBlocks(synthesizer, 1);

        //Assert
        sources.Count.Should().Be(1);

        var source = sources[0];
        source.StartedNote.Should().Be(69);
        source.StartedVelocity.Should().Be(100);
        source.StartedPitchHz.Should().BeApproximately(440.0, 0.01);
        source.LastPitchHz.Should().BeApproximately(440.0, 0.01);
        source.RenderCalls.Should().BeGreaterThan(3);
        source.NoteOffCalls.Should().Be(1);
        source.Context.Zone.Should().BeSameAs(instrument.Zones[0]);
        source.Context.SampleRate.Should().Be(DecentSamplerEngineFixtures.SampleRate);
        source.Context.Waveform.Should().Be("fm6op");
        source.Context.Instrument.Should().BeSameAs(instrument);

        DecentSamplerRenderProbe.Rms(left).Should().BeApproximately(0.25, 0.002);
        synthesizer.Problems.Should().NotContain(problem => problem.Contains("fm6op"));
    }

    [Fact]
    public void a_voice_source_is_pooled_across_notes()
    {
        //Arrange
        var registry = new DecentSamplerExtensionRegistry();
        var created = 0;
        registry.RegisterOscillator("fm6op", _ =>
        {
            created++;
            return new FakeVoiceSource();
        });

        using var fixtures = DecentSamplerEngineFixtures.Create();
        using var instrument = fixtures.LoadPreset(OscillatorPreset);
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(
            instrument, settings => settings.Extensions = registry);

        //Act - eight notes, one after another, each finished before the next.
        for (var i = 0; i < 8; i++)
        {
            synthesizer.NoteOn(0, 60, 100);
            DecentSamplerRenderProbe.RenderBlocks(synthesizer, 2);
            synthesizer.NoteOffAll(immediate: true);
            DecentSamplerRenderProbe.RenderBlocks(synthesizer, 1);
        }

        //Assert - a steady stream of notes does not keep allocating voice sources.
        created.Should().Be(1);
    }

    [Fact]
    public void the_missing_feature_messages_name_the_add_on_package()
    {
        DecentSamplerExtensions.MissingOscillatorMessage("pluck1")
            .Should().Contain("CodeBrix.Audio.ModestSynth").And.Contain("ModestSynth.Register()");

        DecentSamplerExtensions.MissingEffectMessage("phaser")
            .Should().Contain("CodeBrix.Audio.ModestSynth");
    }

    [Fact]
    public void an_effect_factory_can_be_looked_up()
    {
        //Arrange
        var registry = new DecentSamplerExtensionRegistry();
        registry.RegisterEffect("gate", _ => new FakeEffect());

        //Act
        var found = registry.TryGetEffectFactory("GATE", out var factory);

        //Assert
        found.Should().BeTrue();
        factory(null).Should().BeOfType<FakeEffect>();
    }

    // A voice source that records what the voice runtime did to it and renders a constant, so a test
    // can both watch the calls and measure the audio.
    private sealed class FakeVoiceSource : IVoiceSource
    {
        public OscillatorContext Context { get; set; }

        public int StartedNote { get; private set; } = -1;

        public int StartedVelocity { get; private set; }

        public double StartedPitchHz { get; private set; }

        public double LastPitchHz { get; private set; }

        public int RenderCalls { get; private set; }

        public int NoteOffCalls { get; private set; }

        public bool IsStereo => false;

        public bool IsFinished => false;

        public void Start(int note, int velocity, double pitchHz)
        {
            StartedNote = note;
            StartedVelocity = velocity;
            StartedPitchHz = pitchHz;
            LastPitchHz = pitchHz;
            RenderCalls = 0;
            NoteOffCalls = 0;
        }

        public void NoteOff() => NoteOffCalls++;

        public void SetPitch(double pitchHz) => LastPitchHz = pitchHz;

        public bool Render(float[] left, float[] right, int frames)
        {
            RenderCalls++;
            Array.Fill(left, 0.25f, 0, frames);
            return true;
        }

        public bool TrySetParameter(string name, double value) => false;

        public bool TryGetParameter(string name, out double value)
        {
            value = 0.0;
            return false;
        }
    }

    private sealed class FakeEffect : IInstrumentEffect
    {
        public bool Enabled { get; set; } = true;

        public IReadOnlyList<string> Tags { get; } = [];

        public void Prepare(int sampleRate)
        {
        }

        public void Reset()
        {
        }

        public void Process(float[] left, float[] right, int frames)
        {
        }

        public bool TrySetParameter(string name, double value) => false;

        public bool TryGetParameter(string name, out double value)
        {
            value = 0.0;
            return false;
        }

        public bool TrySetParameter(string name, string value) => false;
    }
}
