using System;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.DecentSampler.Containers;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler;

/// <summary>
/// Covers instrument loading: sample resolution in a folder and in an archive, the three sample
/// formats, one decode shared by every zone over the same file, the library sidecar, and the tolerant
/// handling of a missing file.
/// </summary>
public class DecentSamplerInstrumentTests
{
    [Fact]
    public void loads_a_preset_with_its_samples()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        fixture.WriteWav("Samples/tone.wav", 0.5f, 128);

        //Act
        using var instrument = fixture.Load(DecentSamplerTestPresets.MinimalPreset());

        //Assert
        instrument.Problems.Should().BeEmpty();
        instrument.UnsupportedFeatures.Should().BeEmpty();
        instrument.Groups.Should().ContainSingle();
        instrument.Zones.Should().ContainSingle();
        instrument.DecodedSampleCount.Should().Be(1);
        instrument.GetResolvedSamplePath(instrument.Zones[0]).Should().NotBeNull();
    }

    [Fact]
    public void decodes_wav_aiff_and_flac_zones()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        fixture.WriteWav("Samples/a.wav", 0.5f, 100);
        fixture.WriteAiff("Samples/b.aiff", 0.25f, 120);
        fixture.WriteFlac("Samples/c.flac");

        var xml = """
            <DecentSampler>
              <groups>
                <group>
                  <sample path="Samples/a.wav" rootNote="60" loNote="60" hiNote="60" />
                  <sample path="Samples/b.aiff" rootNote="61" loNote="61" hiNote="61" />
                  <sample path="Samples/c.flac" rootNote="62" loNote="62" hiNote="62" />
                </group>
              </groups>
            </DecentSampler>
            """;

        //Act
        using var instrument = fixture.Load(xml);

        //Assert
        instrument.Problems.Should().BeEmpty();
        instrument.DecodedSampleCount.Should().Be(3);
        instrument.DecodedByteCount.Should().BeGreaterThan(0);
    }

    [Fact]
    public void zones_sharing_a_file_share_one_decode()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        fixture.WriteWav("Samples/shared.wav", 0.5f, 512);

        var xml = """
            <DecentSampler>
              <groups>
                <group>
                  <sample path="Samples/shared.wav" rootNote="60" loVel="0" hiVel="63" />
                  <sample path="Samples/shared.wav" rootNote="60" loVel="64" hiVel="127" />
                </group>
              </groups>
            </DecentSampler>
            """;

        //Act
        using var instrument = fixture.Load(xml);

        //Assert
        instrument.Zones.Should().HaveCount(2);
        instrument.DecodedSampleCount.Should().Be(1);
    }

    [Fact]
    public void a_path_with_the_wrong_case_still_resolves()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        fixture.WriteWav("Samples/Tone.wav", 0.5f, 64);

        //Act
        using var instrument = fixture.Load(DecentSamplerTestPresets.MinimalPreset("SAMPLES/TONE.WAV"));

        //Assert
        instrument.Problems.Should().BeEmpty();
        instrument.DecodedSampleCount.Should().Be(1);
    }

    [Fact]
    public void a_path_with_windows_separators_still_resolves()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        fixture.WriteWav("Samples/tone.wav", 0.5f, 64);

        //Act
        using var instrument = fixture.Load(DecentSamplerTestPresets.MinimalPreset(@"Samples\tone.wav"));

        //Assert
        instrument.Problems.Should().BeEmpty();
        instrument.DecodedSampleCount.Should().Be(1);
    }

    [Fact]
    public void a_missing_sample_is_a_problem_not_an_exception()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();

        //Act
        using var instrument = fixture.Load(DecentSamplerTestPresets.MinimalPreset("Samples/gone.wav"));

        //Assert
        instrument.Zones.Should().ContainSingle();
        instrument.DecodedSampleCount.Should().Be(0);
        instrument.Problems.Should().ContainSingle();
        instrument.Problems[0].Should().Contain("sample not found");
        instrument.GetResolvedSamplePath(instrument.Zones[0]).Should().BeNull();
    }

    [Fact]
    public void lazy_loading_resolves_paths_without_decoding_anything()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        fixture.WriteWav("Samples/tone.wav", 0.5f, 4096);
        var options = new DecentSamplerLoadOptions { DecodeSamples = false };

        //Act
        using var instrument = fixture.Load(DecentSamplerTestPresets.MinimalPreset(), options);

        //Assert
        instrument.Problems.Should().BeEmpty();
        instrument.DecodedSampleCount.Should().Be(0);
        instrument.DecodedByteCount.Should().Be(0);
        instrument.GetResolvedSamplePath(instrument.Zones[0]).Should().NotBeNull();
    }

    [Fact]
    public void an_unknown_attribute_becomes_an_unsupported_feature()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        fixture.WriteWav("Samples/tone.wav", 0.5f, 64);

        var xml = """
            <DecentSampler>
              <groups>
                <group>
                  <sample path="Samples/tone.wav" rootNote="60" futureThing="1" />
                </group>
              </groups>
            </DecentSampler>
            """;

        //Act
        using var instrument = fixture.Load(xml);

        //Assert
        instrument.UnsupportedFeatures.Should().Equal("sample@futureThing");
    }

    [Fact]
    public void an_unknown_waveform_becomes_an_unsupported_feature()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();

        var xml = "<DecentSampler><groups><group><oscillator waveform=\"formant\" /></group>" +
                  "</groups></DecentSampler>";

        //Act
        using var instrument = fixture.Load(xml);

        //Assert
        instrument.Zones[0].Waveform.Should().Be(DecentSamplerWaveform.Unknown);
        instrument.UnsupportedFeatures.Should().Contain("oscillator waveform 'formant'");
    }

    [Fact]
    public void loads_a_preset_out_of_a_dslibrary_archive_without_unpacking_it()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        fixture.WriteWav("Samples/tone.wav", 0.5f, 256);
        fixture.WritePreset(DecentSamplerTestPresets.MinimalPreset(), "instrument.dspreset");

        var archive = fixture.BuildLibraryArchive(
            "library.dslibrary", "My Library", "instrument.dspreset", "Samples/tone.wav");

        //Act
        using var instrument = DecentSamplerInstrument.Load(archive);

        //Assert
        instrument.Problems.Should().BeEmpty();
        instrument.Name.Should().Be("instrument");
        instrument.Path.Should().Be(archive);
        instrument.DecodedSampleCount.Should().Be(1);
        instrument.Zones[0].RootNote.Should().Be(60);
    }

    [Fact]
    public void an_archive_can_be_listed_before_a_preset_is_chosen()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        fixture.WriteWav("Samples/tone.wav", 0.5f, 64);
        fixture.WritePreset(DecentSamplerTestPresets.MinimalPreset(), "one.dspreset");
        fixture.WritePreset(DecentSamplerTestPresets.MinimalPreset(), "two.dspreset");

        var archive = fixture.BuildLibraryArchive(
            "library.dslibrary", "Lib", "one.dspreset", "two.dspreset", "Samples/tone.wav");

        //Act
        using var container = DecentSamplerContainer.Open(archive);
        var presets = container.FindPresets();

        //Assert
        container.IsArchive.Should().BeTrue();
        presets.Should().HaveCount(2);
        presets[0].Should().Be("Lib/one.dspreset");
    }

    [Fact]
    public void a_named_preset_can_be_chosen_from_an_archive()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        fixture.WriteWav("Samples/tone.wav", 0.5f, 64);
        fixture.WritePreset(DecentSamplerTestPresets.MinimalPreset(), "one.dspreset");
        fixture.WritePreset(DecentSamplerTestPresets.MinimalPreset(), "two.dspreset");

        var archive = fixture.BuildLibraryArchive(
            "library.dslibrary", "Lib", "one.dspreset", "two.dspreset", "Samples/tone.wav");

        //Act
        using var instrument = DecentSamplerInstrument.Load(
            archive, new DecentSamplerLoadOptions { PresetName = "two" });

        //Assert
        instrument.Name.Should().Be("two");
    }

    [Fact]
    public void a_library_sidecar_beside_the_preset_is_read()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        fixture.WriteWav("Samples/tone.wav", 0.5f, 64);
        fixture.WriteLibraryInfo("""
            <?xml version="1.0" encoding="UTF-8"?>
            <DecentSamplerLibraryInfo name="My Sample Library" version="1.2.0" coverArt="CoverArt.png" />
            """);

        //Act
        using var instrument = fixture.Load(DecentSamplerTestPresets.MinimalPreset());

        //Assert
        instrument.LibraryInfo.Should().NotBeNull();
        instrument.LibraryInfo.Name.Should().Be("My Sample Library");
        instrument.LibraryInfo.Version.Should().Be("1.2.0");
        instrument.LibraryInfo.CoverArt.Should().Be("CoverArt.png");
        instrument.LibraryInfo.IsStoreTied.Should().BeFalse();
        instrument.Problems.Should().BeEmpty();
    }

    [Fact]
    public void a_store_tied_library_is_reported_and_never_activated()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        fixture.WriteWav("Samples/tone.wav", 0.5f, 64);
        fixture.WriteLibraryInfo(
            "<DecentSamplerLibraryInfo name=\"Paid\" productId=\"com.example.paid\" />");

        //Act
        using var instrument = fixture.Load(DecentSamplerTestPresets.MinimalPreset());

        //Assert
        instrument.LibraryInfo.IsStoreTied.Should().BeTrue();
        instrument.LibraryInfo.ProductId.Should().Be("com.example.paid");
        instrument.Problems.Should().ContainSingle();
        instrument.Problems[0].Should().Contain("productId");
        instrument.DecodedSampleCount.Should().Be(1);
    }

    [Fact]
    public void a_sidecar_in_the_folder_above_the_preset_is_found()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        fixture.WriteWav("Presets/Samples/tone.wav", 0.5f, 64);
        fixture.WriteLibraryInfo("<DecentSamplerLibraryInfo name=\"Above\" />");

        //Act
        using var instrument = fixture.Load(
            DecentSamplerTestPresets.MinimalPreset(), null, "Presets/instrument.dspreset");

        //Assert
        instrument.LibraryInfo.Name.Should().Be("Above");
    }

    [Fact]
    public void a_folder_can_be_loaded_directly()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        fixture.WriteWav("Samples/tone.wav", 0.5f, 64);
        fixture.WritePreset(DecentSamplerTestPresets.MinimalPreset(), "instrument.dspreset");

        //Act
        using var instrument = DecentSamplerInstrument.Load(fixture.Directory);

        //Assert
        instrument.Name.Should().Be("instrument");
        instrument.DecodedSampleCount.Should().Be(1);
    }

    [Fact]
    public void loading_something_that_does_not_exist_throws()
    {
        //Arrange
        var missing = Path.Combine(Path.GetTempPath(), "codebrix-ds-missing", "nothing.dspreset");

        //Act
        var act = () => DecentSamplerInstrument.Load(missing);

        //Assert
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void load_options_carry_the_streaming_thresholds()
    {
        //Arrange
        var options = new DecentSamplerLoadOptions();

        //Act
        var copy = options.Clone();

        //Assert
        copy.StreamingSampleThresholdBytes.Should()
            .Be(DecentSamplerLoadOptions.DefaultStreamingSampleThresholdBytes);
        copy.InstrumentMemoryBudgetBytes.Should()
            .Be(DecentSamplerLoadOptions.DefaultInstrumentMemoryBudgetBytes);
        copy.DecodeSamples.Should().BeTrue();
        copy.ResolveCacheFolder().Should().NotBeNullOrEmpty();
    }

    [Fact]
    public void a_negative_streaming_threshold_is_rejected()
    {
        //Arrange
        var options = new DecentSamplerLoadOptions();

        //Act
        var act = () => options.StreamingSampleThresholdBytes = -1;

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void the_instrument_exposes_the_whole_parsed_model()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();
        fixture.WriteWav("Samples/tone.wav", 0.5f, 64);

        var xml = """
            <DecentSampler>
              <ui width="812" height="375"><tab name="main" /></ui>
              <groups>
                <group><sample path="Samples/tone.wav" rootNote="60" /></group>
              </groups>
              <effects><effect type="reverb" wetLevel="0.3" /></effects>
              <buses><bus busVolume="0.5" /></buses>
              <midi><cc number="1" /></midi>
              <modulators><lfo shape="sine" frequency="2" /></modulators>
              <noteSequences><sequence length="4" /></noteSequences>
              <arpeggiator enabled="true" />
              <tags><tag name="a" /></tags>
            </DecentSampler>
            """;

        //Act
        using var instrument = fixture.Load(xml);

        //Assert
        instrument.Effects.Effects.Should().ContainSingle();
        instrument.Buses.Should().ContainSingle();
        instrument.MidiHandlers.Should().ContainSingle();
        instrument.Modulators.Should().ContainSingle();
        instrument.Sequences.Should().ContainSingle();
        instrument.Arpeggiator.Enabled.Should().BeTrue();
        instrument.Tags.Should().ContainSingle();
        instrument.Ui.Tabs.Should().ContainSingle();
        instrument.Preset.Should().NotBeNull();
    }

    [Fact]
    public void an_instrument_with_no_groups_still_loads()
    {
        //Arrange
        using var fixture = DecentSamplerTestPresets.Create();

        //Act
        using var instrument = fixture.Load("<DecentSampler />");

        //Assert
        instrument.Groups.Should().BeEmpty();
        instrument.Zones.Should().BeEmpty();
        instrument.Buses.Should().BeEmpty();
        instrument.Effects.Should().BeNull();
    }
}
