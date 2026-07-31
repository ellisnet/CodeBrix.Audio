using System.IO;
using System.Linq;
using CodeBrix.Audio.Synth.Sfz;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.Sfz;

/// <summary>
/// Covers instrument loading: sample path resolution (default_path, Windows separators, wrong case),
/// decoded sample sharing, loop defaults from embedded loop points, control-section state, curves, and
/// the tolerant handling of problems and unimplemented opcodes.
/// </summary>
public class SfzInstrumentTests
{
    [Fact]
    public void loads_regions_with_their_samples()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("kick.wav", 0.5f, 1000);
        fixture.WriteConstantWav("snare.wav", 0.25f, 500);

        //Act
        var instrument = fixture.Load("""
            <region> sample=kick.wav lokey=36 hikey=36
            <region> sample=snare.wav lokey=38 hikey=38
            """);

        //Assert
        instrument.Regions.Should().HaveCount(2);
        instrument.Problems.Should().BeEmpty();
        instrument.GetSampleData(instrument.Regions[0]).Should().NotBeNull();
        instrument.GetSampleData(instrument.Regions[1]).Should().NotBeNull();
        instrument.GetSampleData(instrument.Regions[0]).Frames.Should().Be(1000);
    }

    [Fact]
    public void regions_sharing_a_sample_share_one_decode()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("shared.wav", 0.5f, 1000);

        //Act
        var instrument = fixture.Load("""
            <region> sample=shared.wav lovel=0 hivel=63
            <region> sample=shared.wav lovel=64 hivel=127
            """);

        //Assert
        instrument.GetSampleData(instrument.Regions[0])
            .Should().BeSameAs(instrument.GetSampleData(instrument.Regions[1]));
    }

    [Fact]
    public void default_path_and_windows_separators_resolve()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        Directory.CreateDirectory(Path.Combine(fixture.Directory, "Samples"));
        fixture.WriteConstantWav(Path.Combine("Samples", "kick.wav"), 0.5f, 100);

        //Act
        var instrument = fixture.Load("""
            <control> default_path=Samples\
            <region> sample=kick.wav
            """);

        //Assert
        instrument.Problems.Should().BeEmpty();
        instrument.GetSampleData(instrument.Regions[0]).Should().NotBeNull();
    }

    [Fact]
    public void sample_paths_with_the_wrong_case_still_resolve()
    {
        //Arrange - libraries are authored on Windows; on Linux the case is routinely wrong.
        using var fixture = SfzTestInstruments.Create();
        Directory.CreateDirectory(Path.Combine(fixture.Directory, "Samples"));
        fixture.WriteConstantWav(Path.Combine("Samples", "Kick.wav"), 0.5f, 100);

        //Act
        var instrument = fixture.Load(@"<region> sample=samples\KICK.WAV");

        //Assert
        instrument.Problems.Should().BeEmpty();
        instrument.GetSampleData(instrument.Regions[0]).Should().NotBeNull();
    }

    [Fact]
    public void a_missing_sample_is_a_problem_not_a_failure()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("real.wav", 0.5f, 100);

        //Act
        var instrument = fixture.Load("""
            <region> sample=real.wav
            <region> sample=missing.wav
            """);

        //Assert
        instrument.Regions.Should().HaveCount(2);
        instrument.GetSampleData(instrument.Regions[0]).Should().NotBeNull();
        instrument.GetSampleData(instrument.Regions[1]).Should().BeNull();
        instrument.Problems.Should().ContainSingle(p => p.Contains("missing.wav"));
    }

    [Fact]
    public void generator_samples_are_reported_and_skipped()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();

        //Act
        var instrument = fixture.Load("<region> sample=*sine");

        //Assert
        instrument.GetSampleData(instrument.Regions[0]).Should().BeNull();
        instrument.Problems.Should().ContainSingle(p => p.Contains("*sine"));
    }

    // ------------------------------------------------------------------ loop defaults

    [Fact]
    public void a_sample_without_loop_points_defaults_to_no_loop()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("plain.wav", 0.5f, 1000);

        //Act
        var instrument = fixture.Load("<region> sample=plain.wav");

        //Assert
        instrument.Regions[0].LoopMode.Should().Be(SfzLoopMode.NoLoop);
    }

    [Fact]
    public void a_sample_with_an_smpl_loop_defaults_to_loop_continuous_with_its_points()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteWavWithSmplLoop("looped.wav", frames: 1000, loopStart: 200, loopEnd: 800);

        //Act
        var instrument = fixture.Load("<region> sample=looped.wav");

        //Assert
        var region = instrument.Regions[0];
        region.LoopMode.Should().Be(SfzLoopMode.Continuous);
        region.LoopStart.Should().Be(200);
        region.LoopEnd.Should().Be(800);
    }

    [Fact]
    public void explicit_loop_opcodes_win_over_embedded_loop_points()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteWavWithSmplLoop("looped.wav", frames: 1000, loopStart: 200, loopEnd: 800);

        //Act
        var instrument = fixture.Load("<region> sample=looped.wav loop_mode=no_loop loop_start=10 loop_end=20");

        //Assert
        var region = instrument.Regions[0];
        region.LoopMode.Should().Be(SfzLoopMode.NoLoop);
        region.LoopStart.Should().Be(10);
        region.LoopEnd.Should().Be(20);
    }

    [Fact]
    public void a_loop_region_without_points_loops_the_whole_sample()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("pad.wav", 0.5f, 1000);

        //Act
        var instrument = fixture.Load("<region> sample=pad.wav loop_mode=loop_continuous");

        //Assert
        var region = instrument.Regions[0];
        region.LoopStart.Should().Be(0);
        region.LoopEnd.Should().Be(999);
    }

    // ------------------------------------------------------------------ stereo and formats

    [Fact]
    public void stereo_samples_decode_to_two_channels()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteStereoWav("stereo.wav", leftValue: 0.5f, rightValue: -0.25f, frames: 100);

        //Act
        var instrument = fixture.Load("<region> sample=stereo.wav");

        //Assert
        var sample = instrument.GetSampleData(instrument.Regions[0]);
        sample.ChannelCount.Should().Be(2);
        sample.Channels[0][50].Should().BeApproximately(0.5f, 0.0001f);
        sample.Channels[1][50].Should().BeApproximately(-0.25f, 0.0001f);
    }

    [Fact]
    public void flac_samples_load_through_the_reader_registry()
    {
        //Arrange - reuse a FLAC fixture the decoder tests already ship.
        using var fixture = SfzTestInstruments.Create();
        var flacSource = Path.Combine(System.AppContext.BaseDirectory, "Assets", "audio", "flac-tone-mono-16bit-22050.flac");
        File.Copy(flacSource, Path.Combine(fixture.Directory, "tone.flac"));

        //Act
        var instrument = fixture.Load("<region> sample=tone.flac");

        //Assert
        instrument.Problems.Should().BeEmpty();
        var sample = instrument.GetSampleData(instrument.Regions[0]);
        sample.Should().NotBeNull();
        sample.Frames.Should().BeGreaterThan(0);
    }

    // ------------------------------------------------------------------ control state, curves, reporting

    [Fact]
    public void control_section_state_is_collected()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("a.wav", 0.5f, 100);

        //Act
        var instrument = fixture.Load("""
            <control> set_cc11=127 set_hd_cc1=0.5 label_cc11=Expression
            <region> sample=a.wav
            """);

        //Assert
        instrument.InitialControllers[11].Should().BeApproximately(1f, 0.001f);
        instrument.InitialControllers[1].Should().BeApproximately(0.5f, 0.001f);
        instrument.ControllerLabels[11].Should().Be("Expression");
    }

    [Fact]
    public void file_curves_override_built_ins_and_missing_indices_fall_back()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("a.wav", 0.5f, 100);

        //Act
        var instrument = fixture.Load("""
            <region> sample=a.wav
            <curve> curve_index=17 v0=1 v127=0
            """);

        //Assert
        instrument.GetCurve(17).Evaluate(0f).Should().Be(1f);
        instrument.GetCurve(17).Evaluate(1f).Should().Be(0f);
        instrument.GetCurve(2).Evaluate(0f).Should().Be(1f, "built-in 2 is the inverted line");
        instrument.GetCurve(99).Evaluate(0.75f).Should().BeApproximately(0.75f, 0.001f, "undefined curves are linear");
    }

    [Fact]
    public void unimplemented_opcodes_are_reported_canonically_once()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("a.wav", 0.5f, 100);

        //Act - the generator, auto-pan and per-band-vel2 opcodes stay outside the implemented set,
        // and start_locc is an alias spelling the engine does not fold.
        var instrument = fixture.Load("""
            <region> sample=a.wav noise_level=10 apan_depth_oncc30=6 eq1_vel2gain=3
            <region> sample=a.wav noise_level=12 start_locc4=0
            """);

        //Assert
        instrument.UnsupportedOpcodes.Should().Equal("apan_depth_onccN", "eqN_vel2gain", "noise_level", "start_loccN");
    }

    [Fact]
    public void implemented_opcodes_are_not_reported()
    {
        //Arrange
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("a.wav", 0.5f, 100);

        //Act
        var instrument = fixture.Load("""
            <region> sample=a.wav lokey=36 hikey=48 volume_oncc7=6 amplitude_cc11=100
            ampeg_release=0.5 cutoff=1200 seq_length=2 seq_position=1 locc64=64 sw_last=24
            """);

        //Assert
        instrument.UnsupportedOpcodes.Should().BeEmpty();
    }

    [Fact]
    public void a_utf8_byte_order_mark_does_not_swallow_the_first_header()
    {
        //Arrange - Windows editors save UTF-8 with a BOM; decoded naively it glues to the first
        // token and the file's first header (often <control> with default_path) vanishes.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("a.wav", 0.5f, 100);
        var path = System.IO.Path.Combine(fixture.Directory, "bom.sfz");
        var text = "<control> default_path=.\n<region> sample=a.wav key=60\n";
        System.IO.File.WriteAllBytes(path, new byte[] { 0xEF, 0xBB, 0xBF }
            .Concat(System.Text.Encoding.UTF8.GetBytes(text)).ToArray());

        //Act
        var instrument = new SfzInstrument(path);

        //Assert
        instrument.Regions.Should().HaveCount(1);
        instrument.Problems.Should().BeEmpty();
    }

    [Fact]
    public void an_include_with_the_wrong_case_still_resolves()
    {
        //Arrange - a Windows-authored library: the include is written "mappings\snare.sfz" while the
        // disk says "Mappings/Snare.sfz". Losing it silently would lose every region it carries.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("a.wav", 0.5f, 100);
        var mappings = System.IO.Path.Combine(fixture.Directory, "Mappings");
        System.IO.Directory.CreateDirectory(mappings);
        System.IO.File.WriteAllText(System.IO.Path.Combine(mappings, "Snare.sfz"),
            "<region> sample=a.wav key=60\n");

        //Act
        var instrument = fixture.Load("""#include "mappings\snare.sfz" """, "kit.sfz");

        //Assert
        instrument.Regions.Should().HaveCount(1);
        instrument.Problems.Should().BeEmpty();
    }

    [Fact]
    public void a_define_that_prefixes_another_does_not_corrupt_it()
    {
        //Arrange - $KEY and $KEY2 both defined: $KEY2 must substitute as itself, not as the $KEY
        // value with a stray 2 appended.
        using var fixture = SfzTestInstruments.Create();
        fixture.WriteConstantWav("a.wav", 0.5f, 100);

        //Act
        var instrument = fixture.Load("""
            #define $KEY 60
            #define $KEY2 62
            <region> sample=a.wav lokey=$KEY hikey=$KEY2
            """);

        //Assert
        instrument.Regions[0].LoKey.Should().Be(60);
        instrument.Regions[0].HiKey.Should().Be(62);
    }
}
