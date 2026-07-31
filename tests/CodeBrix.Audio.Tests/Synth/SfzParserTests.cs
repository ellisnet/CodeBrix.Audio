using System;
using System.IO;
using System.Linq;
using CodeBrix.Audio.Synth.Sfz;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

/// <summary>
/// Covers <see cref="SfzParser"/> against hand-written files, tier by tier: structure and playback
/// first, then the five families real libraries lean on hardest - key switches, off groups, CC ranges
/// and modulation, trigger modes, and round robins.
/// </summary>
public class SfzParserTests
{
    // ------------------------------------------------------------------ structure

    [Fact]
    public void parses_headers_into_sections_in_file_order()
    {
        //Arrange
        var text = """
            <global> volume=-3
            <group> lokey=36 hikey=47
            <region> sample=kick.wav
            <region> sample=snare.wav
            """;

        //Act
        var file = SfzParser.ParseText(text);

        //Assert
        file.Sections.Select(s => s.Kind).Should().Equal(
            SfzHeaderKind.Global, SfzHeaderKind.Group, SfzHeaderKind.Region, SfzHeaderKind.Region);
    }

    [Fact]
    public void parses_several_headers_and_opcodes_sharing_one_line()
    {
        //Arrange
        var text = "<group> lokey=36 <region> sample=a.wav pan=-50 <region> sample=b.wav";

        //Act
        var file = SfzParser.ParseText(text);

        //Assert
        file.Regions.Count().Should().Be(2);
        file.Sections[1].Find("sample").Value.Should().Be("a.wav");
        file.Sections[1].Find("pan").AsFloat().Should().Be(-50f);
    }

    [Fact]
    public void keeps_sample_paths_that_contain_spaces()
    {
        //Arrange
        // Truncating at the first space is the classic way to break a real library.
        var text = "<region> sample=Grand Piano/C4 soft.wav lokey=60 hikey=60";

        //Act
        var file = SfzParser.ParseText(text);

        //Assert
        var region = file.Regions.Single();
        region.Find("sample").Value.Should().Be("Grand Piano/C4 soft.wav");
        region.Find("lokey").AsInt().Should().Be(60);
    }

    [Fact]
    public void strips_line_comments()
    {
        //Arrange
        var text = """
            // a leading comment
            <region> sample=a.wav // trailing comment with an = sign
            """;

        //Act
        var file = SfzParser.ParseText(text);

        //Assert
        file.Regions.Single().Find("sample").Value.Should().Be("a.wav");
    }

    [Fact]
    public void records_control_default_path()
    {
        //Arrange
        var text = """
            <control> default_path=samples/
            <region> sample=a.wav
            """;

        //Act
        var file = SfzParser.ParseText(text);

        //Assert
        file.DefaultPath.Should().Be("samples/");
    }

    // ------------------------------------------------------------------ inheritance

    [Fact]
    public void resolves_a_region_against_group_then_global()
    {
        //Arrange
        var text = """
            <global> volume=-6 ampeg_release=0.5
            <group> lokey=36 hikey=47 volume=-3
            <region> sample=a.wav lokey=40
            """;

        //Act
        var file = SfzParser.ParseText(text);
        var resolved = file.Resolve(file.Regions.Single());

        //Assert
        resolved["lokey"].AsInt().Should().Be(40);          // region wins over group
        resolved["hikey"].AsInt().Should().Be(47);          // inherited from group
        resolved["volume"].AsFloat().Should().Be(-3f);      // group wins over global
        resolved["ampeg_release"].AsFloat().Should().Be(0.5f); // inherited from global
    }

    [Fact]
    public void a_region_inherits_only_from_its_own_group()
    {
        //Arrange
        var text = """
            <group> pan=-100
            <region> sample=a.wav
            <group> pan=100
            <region> sample=b.wav
            """;

        //Act
        var file = SfzParser.ParseText(text);
        var regions = file.Regions.ToArray();

        //Assert
        file.Resolve(regions[0])["pan"].AsFloat().Should().Be(-100f);
        file.Resolve(regions[1])["pan"].AsFloat().Should().Be(100f);
    }

    // ------------------------------------------------------------------ opcode name structure

    [Theory]
    [InlineData("volume_oncc74", "volume", 74, "oncc")]
    [InlineData("cutoff_oncc1", "cutoff", 1, "oncc")]
    [InlineData("locc64", "locc", 64, "cc")]
    [InlineData("hicc64", "hicc", 64, "cc")]
    [InlineData("amp_velcurve_82", "amp_velcurve", 82, null)]
    [InlineData("volume", "volume", null, null)]
    [InlineData("sample", "sample", null, null)]
    public void decomposes_opcode_names(string name, string baseName, int? index, string modulation)
    {
        //Arrange
        var text = $"<region> {name}=1";

        //Act
        var opcode = SfzParser.ParseText(text).Regions.Single().Opcodes.Single();

        //Assert
        opcode.BaseName.Should().Be(baseName);
        opcode.Index.Should().Be(index);
        opcode.Modulation.Should().Be(modulation);
    }

    [Theory]
    [InlineData("60", 60)]
    [InlineData("c4", 60)]
    [InlineData("C4", 60)]
    [InlineData("a4", 69)]
    [InlineData("f#3", 54)]
    [InlineData("c-1", 0)]
    public void reads_note_numbers_in_both_forms(string value, int expected)
    {
        //Arrange
        var text = $"<region> lokey={value}";

        //Act
        var opcode = SfzParser.ParseText(text).Regions.Single().Find("lokey");

        //Assert
        opcode.AsNoteNumber().Should().Be(expected);
    }

    // ------------------------------------------------------------------ the five hard families

    [Fact]
    public void parses_key_switches()
    {
        //Arrange
        var text = "<region> sw_lokey=24 sw_hikey=35 sw_last=24 sw_default=24 sample=sustain.wav";

        //Act
        var region = SfzParser.ParseText(text).Regions.Single();

        //Assert
        region.Find("sw_lokey").AsInt().Should().Be(24);
        region.Find("sw_last").AsInt().Should().Be(24);
        region.Find("sw_default").AsInt().Should().Be(24);
    }

    [Fact]
    public void parses_off_groups()
    {
        //Arrange
        var text = """
            <region> sample=hihat_open.wav group=1 off_by=1 off_mode=fast
            <region> sample=hihat_closed.wav group=1 off_by=1
            """;

        //Act
        var regions = SfzParser.ParseText(text).Regions.ToArray();

        //Assert
        regions[0].Find("group").AsInt().Should().Be(1);
        regions[0].Find("off_by").AsInt().Should().Be(1);
        regions[0].Find("off_mode").Value.Should().Be("fast");
    }

    [Fact]
    public void parses_cc_ranges_and_oncc_modulation()
    {
        //Arrange
        var text = "<region> sample=a.wav locc64=64 hicc64=127 volume_oncc11=6 cutoff_oncc1=1200";

        //Act
        var region = SfzParser.ParseText(text).Regions.Single();

        //Assert
        region.Find("locc64").AsInt().Should().Be(64);
        region.Find("volume_oncc11").AsFloat().Should().Be(6f);
        region.Find("cutoff_oncc1").AsInt().Should().Be(1200);
    }

    [Theory]
    [InlineData("attack")]
    [InlineData("release")]
    [InlineData("first")]
    [InlineData("legato")]
    public void parses_trigger_modes(string mode)
    {
        //Arrange
        var text = $"<region> sample=a.wav trigger={mode}";

        //Act
        var region = SfzParser.ParseText(text).Regions.Single();

        //Assert
        region.Find("trigger").Value.Should().Be(mode);
    }

    [Fact]
    public void parses_round_robins_and_random_layers()
    {
        //Arrange
        var text = """
            <region> sample=hit1.wav seq_length=3 seq_position=1
            <region> sample=hit2.wav seq_length=3 seq_position=2
            <region> sample=soft.wav lorand=0.0 hirand=0.5
            """;

        //Act
        var regions = SfzParser.ParseText(text).Regions.ToArray();

        //Assert
        regions[0].Find("seq_length").AsInt().Should().Be(3);
        regions[1].Find("seq_position").AsInt().Should().Be(2);
        regions[2].Find("hirand").AsFloat().Should().Be(0.5f);
    }

    // ------------------------------------------------------------------ robustness

    [Fact]
    public void an_unknown_opcode_is_carried_not_rejected()
    {
        //Arrange
        // Files routinely carry opcodes aimed at other players. Loading must not fail.
        var text = "<region> sample=a.wav some_other_players_opcode=42 lokey=36";

        //Act
        var file = SfzParser.ParseText(text);

        //Assert
        var region = file.Regions.Single();
        region.Find("some_other_players_opcode").AsInt().Should().Be(42);
        region.Find("lokey").AsInt().Should().Be(36);
        file.Problems.Should().BeEmpty();
    }

    [Fact]
    public void an_unknown_header_is_carried_not_rejected()
    {
        //Arrange
        var text = """
            <someotherheader> foo=1
            <region> sample=a.wav
            """;

        //Act
        var file = SfzParser.ParseText(text);

        //Assert
        file.Sections[0].Kind.Should().Be(SfzHeaderKind.Unknown);
        file.Regions.Count().Should().Be(1);
    }

    [Fact]
    public void an_opcode_before_any_header_is_reported_and_skipped()
    {
        //Arrange
        var text = """
            volume=-3
            <region> sample=a.wav
            """;

        //Act
        var file = SfzParser.ParseText(text);

        //Assert
        file.Problems.Should().ContainSingle();
        file.Regions.Count().Should().Be(1);
    }

    [Fact]
    public void substitutes_define_variables()
    {
        //Arrange
        var text = """
            #define $KICK kick_samples
            <region> sample=$KICK/kick1.wav
            """;

        //Act
        var file = SfzParser.ParseText(text);

        //Assert
        file.Defines["KICK"].Should().Be("kick_samples");
        file.Regions.Single().Find("sample").Value.Should().Be("kick_samples/kick1.wav");
    }

    [Fact]
    public void follows_include_relative_to_the_including_file()
    {
        //Arrange
        var dir = Path.Combine(Path.GetTempPath(), $"codebrix-sfz-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "shared.sfz"), "<group> ampeg_release=0.3\n");
            var main = Path.Combine(dir, "main.sfz");
            File.WriteAllText(main, "#include \"shared.sfz\"\n<region> sample=a.wav\n");

            //Act
            var file = SfzParser.ParseFile(main);

            //Assert
            file.IncludedFiles.Should().ContainSingle();
            file.Resolve(file.Regions.Single())["ampeg_release"].AsFloat().Should().Be(0.3f);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void resolves_include_relative_to_the_root_file_not_the_including_file()
    {
        //Arrange
        // ARIA and sfizz resolve #include against the ROOT .sfz file's directory. Real libraries are
        // written that way - DrumGizmo's kits include "../Data/x.txt" from files already inside
        // Data/, which only resolves from the root. Getting this wrong loses every region silently.
        var dir = Path.Combine(Path.GetTempPath(), $"codebrix-sfz-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "Kit"));
        Directory.CreateDirectory(Path.Combine(dir, "Data"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "Data", "regions.sfz"), "<region> sample=kick.wav\n");

            // Included from Data/mid.sfz, "../Data/regions.sfz" would resolve to Data/Data/... and
            // fail; from the root (Kit/) it resolves correctly.
            File.WriteAllText(Path.Combine(dir, "Data", "mid.sfz"), "#include \"../Data/regions.sfz\"\n");

            var main = Path.Combine(dir, "Kit", "kit.sfz");
            File.WriteAllText(main, "#include \"../Data/mid.sfz\"\n");

            //Act
            var file = SfzParser.ParseFile(main);

            //Assert
            file.Problems.Should().BeEmpty();
            file.Regions.Count().Should().Be(1);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void falls_back_to_the_including_file_directory_when_the_root_does_not_resolve()
    {
        //Arrange
        // Some libraries are written the other way round, so the including file's own directory
        // stays as a fallback rather than being abandoned.
        var dir = Path.Combine(Path.GetTempPath(), $"codebrix-sfz-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(dir, "nested"));
        try
        {
            File.WriteAllText(Path.Combine(dir, "nested", "leaf.sfz"), "<region> sample=a.wav\n");
            File.WriteAllText(Path.Combine(dir, "nested", "mid.sfz"), "#include \"leaf.sfz\"\n");

            var main = Path.Combine(dir, "main.sfz");
            File.WriteAllText(main, "#include \"nested/mid.sfz\"\n");

            //Act
            var file = SfzParser.ParseFile(main);

            //Assert
            file.Problems.Should().BeEmpty();
            file.Regions.Count().Should().Be(1);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void a_missing_include_is_reported_not_thrown()
    {
        //Arrange
        var dir = Path.Combine(Path.GetTempPath(), $"codebrix-sfz-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            var main = Path.Combine(dir, "main.sfz");
            File.WriteAllText(main, "#include \"nope.sfz\"\n<region> sample=a.wav\n");

            //Act
            var file = SfzParser.ParseFile(main);

            //Assert
            file.Problems.Should().ContainSingle();
            file.Regions.Count().Should().Be(1);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void handles_a_define_that_appears_mid_line_after_a_header()
    {
        //Arrange
        // Straight out of a real library: header, directive, opcodes, directive — all on one line.
        var text = "<region> #define $KEY 21 lokey=21 hikey=22";

        //Act
        var file = SfzParser.ParseText(text);

        //Assert
        file.Defines["KEY"].Should().Be("21");
        var region = file.Regions.Single();
        region.Find("lokey").AsInt().Should().Be(21);
        region.Find("hikey").AsInt().Should().Be(22);
        region.Opcodes.Count.Should().Be(2);   // and no opcode named "#define $KEY 21 lokey"
    }

    [Fact]
    public void substitutes_define_variables_inside_opcode_names()
    {
        //Arrange
        // amplitude_oncc$ch_hh and amp_velcurve_$v11h are real. Substituting only in values would
        // invent one distinct "opcode" per variable.
        var text = """
            #define $MICCC 30
            <region> sample=a.wav amplitude_oncc$MICCC=100
            """;

        //Act
        var region = SfzParser.ParseText(text).Regions.Single();

        //Assert
        region.Find("amplitude_oncc30").Should().NotBeNull();
        region.Find("amplitude_oncc30").BaseName.Should().Be("amplitude");
        region.Find("amplitude_oncc30").Index.Should().Be(30);
    }

    [Fact]
    public void a_mid_line_include_does_not_get_swallowed_into_the_previous_value()
    {
        //Arrange
        var dir = Path.Combine(Path.GetTempPath(), $"codebrix-sfz-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        try
        {
            File.WriteAllText(Path.Combine(dir, "sample.txt"), "sample=piano.wav\n");
            var main = Path.Combine(dir, "main.sfz");
            File.WriteAllText(main, "<region> lokey=21 hikey=22 #include \"sample.txt\"\n");

            //Act
            var file = SfzParser.ParseFile(main);

            //Assert
            var region = file.Regions.Single();
            region.Find("hikey").Value.Should().Be("22");          // not "22 #include ..."
            region.Find("sample").Value.Should().Be("piano.wav");  // the include was followed
            file.Problems.Should().BeEmpty();
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void distinct_base_names_fold_away_the_cc_number()
    {
        //Arrange
        var text = "<region> sample=a.wav volume_oncc11=6 volume_oncc7=3 cutoff_oncc1=1200";

        //Act
        var baseNames = SfzParser.ParseText(text).DistinctBaseNames().OrderBy(n => n).ToArray();

        //Assert
        baseNames.Should().Equal("cutoff", "sample", "volume");
    }

    [Fact]
    public void parse_file_rejects_a_missing_file()
    {
        //Act
        var act = () => SfzParser.ParseFile(Path.Combine(Path.GetTempPath(), "definitely-not-here.sfz"));

        //Assert
        act.Should().Throw<FileNotFoundException>();
    }

    [Fact]
    public void parse_text_rejects_null()
    {
        //Act
        var act = () => SfzParser.ParseText(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }
}
