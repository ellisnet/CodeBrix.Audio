using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Xml.Linq;
using CodeBrix.Audio.ModestSynth.Oscillators;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using SilverAssertions.Primitives;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// The two-packages-one-version rule, checked against the packages the build actually produced:
/// this add-on's declared dependency on CodeBrix.Audio must name the very version of CodeBrix.Audio
/// that was built alongside it, or the pair cannot be published together.
/// </summary>
/// <remarks>
/// <para>
/// The versions come from Directory.Build.props at the repository root, which computes
/// <c>BuildVersion</c> once and hands the same value to every project. The dependency version
/// NuGet writes into this package's nuspec is read out of the CodeBrix.Audio project as it was
/// evaluated in the same build, so the two agree by construction; this test is what keeps that
/// true if the arrangement is ever changed.
/// </para>
/// <para>
/// It reads the .nupkg files rather than trusting anything: with GeneratePackageOnBuild on, an
/// ordinary build writes both. If they are not where they should be - a build with packaging
/// turned off, or a test run against a stale output folder - the test SKIPS with a message rather
/// than failing, because their absence says nothing about the versions.
/// </para>
/// </remarks>
public class PackageVersionTests
{
    private const string CorePackageId = "CodeBrix.Audio.MitLicenseForever";
    private const string AddOnPackageId = "CodeBrix.Audio.ModestSynth.MitLicenseForever";

    [Fact]
    public void The_add_on_depends_on_the_core_at_exactly_the_version_that_was_built()
    {
        //Arrange
        string addOnVersion = VersionOf(typeof(ModestOscillatorFactory));
        string coreVersion = VersionOf(typeof(WaveFormat));
        string nupkg = FindPackage("CodeBrix.Audio.ModestSynth", AddOnPackageId, addOnVersion);
        Assert.SkipWhen(nupkg == null,
            "No " + AddOnPackageId + "." + addOnVersion + ".nupkg was found; nothing to check.");

        //Act
        XElement nuspec = ReadNuspec(nupkg, AddOnPackageId);
        XNamespace ns = nuspec.Name.Namespace;
        XElement dependency = nuspec
            .Descendants(ns + "dependency")
            .FirstOrDefault(d => (string)d.Attribute("id") == CorePackageId);

        //Assert
        dependency.Should().NotBeNull();
        ((string)dependency.Attribute("version")).Should().Be(coreVersion);
    }

    [Fact]
    public void Both_packages_carry_the_same_version()
    {
        //Arrange
        string addOnVersion = VersionOf(typeof(ModestOscillatorFactory));
        string coreVersion = VersionOf(typeof(WaveFormat));
        string addOnPackage = FindPackage("CodeBrix.Audio.ModestSynth", AddOnPackageId, addOnVersion);
        string corePackage = FindPackage("CodeBrix.Audio", CorePackageId, coreVersion);
        Assert.SkipWhen(addOnPackage == null || corePackage == null,
            "One of the two .nupkg files was not found; nothing to check.");

        //Act
        XElement addOnNuspec = ReadNuspec(addOnPackage, AddOnPackageId);
        XElement coreNuspec = ReadNuspec(corePackage, CorePackageId);
        XNamespace ns = addOnNuspec.Name.Namespace;

        //Assert
        addOnNuspec.Element(ns + "version").Value
            .Should().Be(coreNuspec.Element(coreNuspec.Name.Namespace + "version").Value);
    }

    [Fact]
    public void The_two_assemblies_are_stamped_from_the_same_build_version()
    {
        //Arrange, Act
        string addOnVersion = VersionOf(typeof(ModestOscillatorFactory));
        string coreVersion = VersionOf(typeof(WaveFormat));

        //Assert
        // Both read $(BuildVersion) from the repository's Directory.Build.props. They can only
        // differ if a build straddled a UTC minute boundary; a publish pins the value on the
        // command line, which is why MAINTAINER-README says to.
        addOnVersion.Should().Be(coreVersion);
    }

    private static string VersionOf(Type typeInAssembly)
        => typeInAssembly.Assembly.GetName().Version.ToString();

    private static XElement ReadNuspec(string nupkgPath, string packageId)
    {
        using (ZipArchive archive = ZipFile.OpenRead(nupkgPath))
        {
            ZipArchiveEntry entry = archive.GetEntry(packageId + ".nuspec");
            using (Stream stream = entry.Open())
            {
                return XDocument.Load(stream).Root.Elements()
                    .First(e => e.Name.LocalName == "metadata");
            }
        }
    }

    private static string FindPackage(string projectFolder, string packageId, string version)
    {
        string root = RepositoryRoot();
        if (root == null) { return null; }

        string configuration = new DirectoryInfo(AppContext.BaseDirectory).Parent.Name;
        string path = Path.Combine(root, "src", projectFolder, "bin", configuration,
            packageId + "." + version + ".nupkg");

        return File.Exists(path) ? path : null;
    }

    private static string RepositoryRoot()
    {
        DirectoryInfo directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            if (File.Exists(Path.Combine(directory.FullName, "CodeBrix.Audio.slnx")))
            {
                return directory.FullName;
            }

            directory = directory.Parent;
        }

        return null;
    }
}
