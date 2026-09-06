using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Streaming;

/// <summary>
/// What a streamed instrument holds OPEN, and that disposing it lets every one of those files go.
/// </summary>
/// <remarks>
/// <para>
/// A streamed sample keeps its decoder, and therefore its file, open for the whole life of the
/// instrument: that is the design, because reopening a file on the reader thread for every ring-buffer
/// refill would put a syscall on the path the audio thread waits behind. The price is one handle per
/// streamed FILE - not per zone, because the sample cache shares a source between the zones that name
/// the same path - and the promise is that disposal closes them all.
/// </para>
/// <para>
/// The probe is the process's own descriptor table. On Linux <c>/proc/self/fd</c> holds one symlink per
/// open descriptor and each resolves to the file it names, so counting the links that point at a sample
/// counts the handles the process holds on it. There is no portable equivalent, so the tests here SKIP
/// where that directory does not exist rather than pretending to check something.
/// </para>
/// </remarks>
public class DecentSamplerStreamingHandleTests
{
    private const string SkipReason =
        "Counting open file handles needs /proc/self/fd, which only Linux has.";

    private const string Preset = """
        <DecentSampler>
          <groups>
            <group ampVelTrack="0" playbackMode="disk_streaming">
              <sample path="Samples/one.wav" rootNote="60" loNote="0" hiNote="59" />
              <sample path="Samples/two.wav" rootNote="60" loNote="60" hiNote="119" />
              <sample path="Samples/three.wav" rootNote="60" loNote="120" hiNote="127" />
            </group>
          </groups>
        </DecentSampler>
        """;

    [Fact]
    public void a_streamed_instrument_holds_one_handle_per_streamed_file()
    {
        Assert.SkipUnless(HandleCountingWorks, SkipReason);

        //Arrange
        using var fixtures = WriteSamples();

        //Act
        using var instrument = DecentSamplerInstrument.Load(
            fixtures.WritePreset(Preset), new DecentSamplerLoadOptions { StreamingPreloadFrames = 256 });

        //Assert - three files, three handles; nothing doubles up and nothing is missing.
        OpenHandleCount(fixtures, "Samples/one.wav").Should().Be(1);
        OpenHandleCount(fixtures, "Samples/two.wav").Should().Be(1);
        OpenHandleCount(fixtures, "Samples/three.wav").Should().Be(1);
    }

    [Fact]
    public void disposing_a_streamed_instrument_closes_every_handle_it_held()
    {
        Assert.SkipUnless(HandleCountingWorks, SkipReason);

        //Arrange
        using var fixtures = WriteSamples();
        var instrument = DecentSamplerInstrument.Load(
            fixtures.WritePreset(Preset), new DecentSamplerLoadOptions { StreamingPreloadFrames = 256 });

        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);
        synthesizer.NoteOn(0, 60, 100);
        DecentSamplerRenderProbe.RenderBlocks(synthesizer, 32);

        var whileOpen = TotalOpenHandleCount(fixtures);

        //Act
        instrument.Dispose();

        //Assert
        whileOpen.Should().Be(3);
        TotalOpenHandleCount(fixtures).Should().Be(0);
    }

    [Fact]
    public void a_second_zone_over_one_file_does_not_open_it_twice()
    {
        Assert.SkipUnless(HandleCountingWorks, SkipReason);

        //Arrange - two zones, one file, so the sample cache has to share the source.
        using var fixtures = WriteSamples();
        var preset = """
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" playbackMode="disk_streaming">
                  <sample path="Samples/one.wav" rootNote="60" loNote="0" hiNote="63" />
                  <sample path="Samples/one.wav" rootNote="60" loNote="64" hiNote="127" />
                </group>
              </groups>
            </DecentSampler>
            """;

        //Act
        using var instrument = DecentSamplerInstrument.Load(
            fixtures.WritePreset(preset), new DecentSamplerLoadOptions { StreamingPreloadFrames = 256 });

        //Assert
        OpenHandleCount(fixtures, "Samples/one.wav").Should().Be(1);
    }

    [Fact]
    public void an_in_memory_instrument_holds_no_handle_at_all()
    {
        Assert.SkipUnless(HandleCountingWorks, SkipReason);

        //Arrange
        using var fixtures = WriteSamples();

        //Act - the decoded path reads each file once and closes it before the load returns.
        using var instrument = DecentSamplerInstrument.Load(
            fixtures.WritePreset(Preset.Replace(
                "disk_streaming", "memory", StringComparison.Ordinal)));

        //Assert
        TotalOpenHandleCount(fixtures).Should().Be(0);
    }

    private static bool HandleCountingWorks => Directory.Exists("/proc/self/fd");

    private static DecentSamplerEngineFixtures WriteSamples()
    {
        var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteSineWav("Samples/one.wav", 220.0, frames: 40000);
        fixtures.WriteSineWav("Samples/two.wav", 330.0, frames: 40000);
        fixtures.WriteSineWav("Samples/three.wav", 440.0, frames: 40000);
        return fixtures;
    }

    // How many of this process's descriptors point at one of the fixture's files.
    private static int TotalOpenHandleCount(DecentSamplerEngineFixtures fixtures)
    {
        var count = 0;

        foreach (var target in OpenTargets())
        {
            if (target.StartsWith(fixtures.Directory, StringComparison.Ordinal) &&
                target.EndsWith(".wav", StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    // How many of this process's descriptors point at one particular fixture file.
    private static int OpenHandleCount(DecentSamplerEngineFixtures fixtures, string relativePath)
    {
        var wanted = fixtures.PathFor(relativePath);
        var count = 0;

        foreach (var target in OpenTargets())
        {
            if (string.Equals(target, wanted, StringComparison.Ordinal))
            {
                count++;
            }
        }

        return count;
    }

    // Every descriptor's target, skipping the ones that vanish while the directory is being read - the
    // enumeration itself opens one, and a background thread may close another under us.
    private static IEnumerable<string> OpenTargets()
    {
        var targets = new List<string>();

        foreach (var entry in Directory.GetFileSystemEntries("/proc/self/fd"))
        {
            try
            {
                var link = File.ResolveLinkTarget(entry, returnFinalTarget: false);

                if (link != null)
                {
                    targets.Add(link.FullName);
                }
            }
            catch (IOException)
            {
                // The descriptor closed between the listing and the read.
            }
            catch (UnauthorizedAccessException)
            {
                // Not ours to look at.
            }
        }

        return targets;
    }
}
