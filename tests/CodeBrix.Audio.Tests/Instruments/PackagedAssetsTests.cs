using System;
using System.IO;
using CodeBrix.Audio.Instruments;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Instruments;

public sealed class PackagedAssetsTests : IDisposable
{
    private readonly string root = Path.Combine(Path.GetTempPath(), "codebrix-audio-packaged-assets-" + Guid.NewGuid().ToString("N"));

    public PackagedAssetsTests()
    {
        Directory.CreateDirectory(root);
    }

    public void Dispose()
    {
        PackagedAssets.ResetToDefault();
        Directory.Delete(root, true);
    }

    [Fact]
    public void default_locator_looks_beside_the_application()
    {
        //Arrange
        PackagedAssets.ResetToDefault();

        //Act
        var path = PackagedAssets.Locate("FluidR3_GM.sf2");

        //Assert
        path.Should().Be(Path.Combine(AppContext.BaseDirectory, "FluidR3_GM.sf2"));
        PackagedAssets.HasPlatformLocator.Should().BeFalse();
        PackagedAssets.Locator.Should().BeOfType<ApplicationDirectoryAssetLocator>();
    }

    [Fact]
    public void ApplicationDirectoryAssetLocator_reports_files_and_folders_under_its_root()
    {
        //Arrange
        var locator = new ApplicationDirectoryAssetLocator(root);
        File.WriteAllText(Path.Combine(root, "piano.sf2"), "x");
        Directory.CreateDirectory(Path.Combine(root, "strings", "violin"));

        //Act / Assert
        locator.RootDirectory.Should().Be(root);
        locator.Exists("piano.sf2").Should().BeTrue();
        locator.Exists("strings/violin").Should().BeTrue();
        locator.Exists("missing.sf2").Should().BeFalse();
        locator.Locate("strings/violin").Should().Be(Path.Combine(root, "strings", "violin"));
    }

    [Fact]
    public void ApplicationDirectoryAssetLocator_requires_a_root()
    {
        //Arrange
        Action construct = () => new ApplicationDirectoryAssetLocator(" ");

        //Act / Assert
        construct.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Use_routes_Locate_and_Exists_through_the_platform_locator()
    {
        //Arrange
        var platform = new RecordingLocator(Path.Combine(root, "extracted"));
        PackagedAssets.Use(platform);

        //Act
        var path = PackagedAssets.Locate("instruments\\FluidR3_GM.sf2");
        var exists = PackagedAssets.Exists("instruments/FluidR3_GM.sf2");

        //Assert
        PackagedAssets.HasPlatformLocator.Should().BeTrue();
        PackagedAssets.Locator.Should().BeSameAs(platform);
        path.Should().Be(Path.Combine(root, "extracted", "instruments/FluidR3_GM.sf2"));
        exists.Should().BeFalse();
        platform.LocateCalls.Should().Be(1);
        platform.ExistsCalls.Should().Be(1);
        platform.LastAssetPath.Should().Be("instruments/FluidR3_GM.sf2");
    }

    [Fact]
    public void ResetToDefault_puts_the_default_back()
    {
        //Arrange
        PackagedAssets.Use(new RecordingLocator(root));

        //Act
        PackagedAssets.ResetToDefault();

        //Assert
        PackagedAssets.HasPlatformLocator.Should().BeFalse();
        PackagedAssets.Locate("a.sf2").Should().Be(Path.Combine(AppContext.BaseDirectory, "a.sf2"));
    }

    [Fact]
    public void Use_rejects_null()
    {
        //Arrange
        Action use = () => PackagedAssets.Use(null);

        //Act / Assert
        use.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData("FluidR3_GM.sf2", "FluidR3_GM.sf2")]
    [InlineData("instruments/FluidR3_GM.sf2", "instruments/FluidR3_GM.sf2")]
    [InlineData("instruments\\piano\\", "instruments/piano")]
    [InlineData("/leading.sfz", "leading.sfz")]
    [InlineData("  padded.wav  ", "padded.wav")]
    public void NormalizeAssetPath_accepts_relative_paths_and_normalizes_them(string input, string expected)
        => PackagedAssets.NormalizeAssetPath(input).Should().Be(expected);

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("/")]
    [InlineData("../escape.sf2")]
    [InlineData("instruments/../../escape.sf2")]
    [InlineData("./here.sf2")]
    [InlineData("a//b.sf2")]
    [InlineData("C:/absolute.sf2")]
    public void NormalizeAssetPath_rejects_paths_that_are_empty_absolute_or_escape_the_assets(string input)
    {
        //Arrange
        Action normalize = () => PackagedAssets.NormalizeAssetPath(input);

        //Act / Assert
        normalize.Should().Throw<ArgumentException>();
    }

    private sealed class RecordingLocator : IPackagedAssetLocator
    {
        private readonly string root;

        internal RecordingLocator(string root)
        {
            this.root = root;
        }

        internal int LocateCalls { get; private set; }

        internal int ExistsCalls { get; private set; }

        internal string LastAssetPath { get; private set; }

        public string Locate(string assetPath)
        {
            LocateCalls++;
            LastAssetPath = assetPath;
            return Path.Combine(root, assetPath);
        }

        public bool Exists(string assetPath)
        {
            ExistsCalls++;
            LastAssetPath = assetPath;
            return File.Exists(Path.Combine(root, assetPath));
        }
    }
}
