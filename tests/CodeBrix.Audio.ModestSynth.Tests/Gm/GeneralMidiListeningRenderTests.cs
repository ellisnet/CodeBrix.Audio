using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// Writes the whole listening session to <c>.wav</c> files under <c>TestResults/GmRenders/</c>, so
/// the bank can be listened to at leisure and re-compared after a voicing change. Opt-in via
/// CODEBRIX_AUDIO_WRITE_GM_RENDERS=1; skipped otherwise.
/// </summary>
/// <remarks>
/// <para>
/// Sixteen files, one per General MIDI family, each holding that family's eight programs in order;
/// one for the percussion kit piece by piece and one for a groove on it; and one per real piece of
/// generated music. Beside them, <c>INDEX.txt</c> says what is in each file, in what order, with
/// the program numbers, the official names and the start time of each - because a <c>.wav</c> file
/// cannot announce itself the way the audible tests do.
/// </para>
/// <para>
/// IT IS GATED BECAUSE IT WRITES TENS OF MEGABYTES. That is also why it writes 16-bit PCM rather
/// than the 32-bit float an offline render produces: half the size, and every audio program on
/// every machine opens it.
/// </para>
/// <code>
/// CODEBRIX_AUDIO_WRITE_GM_RENDERS=1 dotnet test CodeBrix.Audio.slnx
/// </code>
/// <para>
/// A render is never compared with a committed sample or a digest - see MAINTAINER-README.txt,
/// "PINNED RENDERS AND THE PLATFORM MATHS LIBRARY". These files are for a person to listen to.
/// </para>
/// </remarks>
public class GeneralMidiListeningRenderTests
{
    /// <summary>The folder under <c>TestResults/</c> the renders are written to.</summary>
    public const string RenderFolderName = "GmRenders";

    /// <summary>The rate the listening renders are written at.</summary>
    public const int RenderSampleRate = 44100;

    /// <summary>The bit depth the listening renders are written at - PCM, not float.</summary>
    public const int RenderBitsPerSample = 16;

    private static readonly bool RendersEnabled =
        Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_WRITE_GM_RENDERS") == "1";

    private const string RendersSkipReason =
        "Set CODEBRIX_AUDIO_WRITE_GM_RENDERS=1 to write the listening renders under " +
        "TestResults/GmRenders. They run to tens of megabytes.";

    // Enough for the longest release plus the reverb, which is what stops a pad being cut off.
    private static readonly TimeSpan Tail = TimeSpan.FromSeconds(3.0);

    // The peak a written file is allowed to reach. A piece louder than this is turned DOWN and the
    // index says by how much, rather than being quietly clipped by the 16-bit conversion.
    private const double Ceiling = 0.95;

    [Fact]
    public void writes_the_listening_renders_and_their_index()
    {
        Assert.SkipUnless(RendersEnabled, RendersSkipReason);

        //Arrange
        string folder = RenderFolder();
        Directory.CreateDirectory(folder);

        List<GmAuditionPlan> plans = [];

        foreach (GeneralMidiProgramFamily family in GmAudition.Families())
        {
            plans.Add(GmAudition.Family(family));
        }

        plans.Add(GmAudition.PercussionRollCall());
        plans.Add(GmAudition.PercussionGroove());
        plans.AddRange(GmRealPieces.All());
        plans.Add(GmRealPieces.AirVoicedAs(
            GmRealPieces.AirAlternativeProgram, "piece-4b-mupt-air-choir-aahs.wav"));

        StringBuilder index = new StringBuilder();
        WriteIndexHeader(index, plans.Count);

        //Act
        foreach (GmAuditionPlan plan in plans)
        {
            WriteRender(folder, plan, index);
        }

        File.WriteAllText(Path.Combine(folder, "INDEX.txt"), index.ToString());

        //Assert
        foreach (GmAuditionPlan plan in plans)
        {
            FileInfo file = new FileInfo(Path.Combine(folder, plan.FileName));
            file.Exists.Should().BeTrue();
            file.Length.Should().BeGreaterThan(RenderSampleRate);   // more than a header and a click
        }

        File.Exists(Path.Combine(folder, "INDEX.txt")).Should().BeTrue();
    }

    private static void WriteRender(string folder, GmAuditionPlan plan, StringBuilder index)
    {
        GeneralMidiSynthesizer synthesizer = new GeneralMidiSynthesizer(RenderSampleRate);

        float[] samples = SoundFontRenderer.Render(synthesizer, plan.Sequence, Tail);

        double peak = Peak(samples);
        double trim = peak > Ceiling ? Ceiling / peak : 1.0;

        if (trim < 1.0)
        {
            for (int i = 0; i < samples.Length; i++) { samples[i] = (float)(samples[i] * trim); }
        }

        string path = Path.Combine(folder, plan.FileName);

        // Part A's writer seam, writing the samples we already have rather than rendering twice.
        using (FileStream stream = File.Create(path))
        using (IAudioFileWriter writer = AudioFileWriterRegistry.Create(
            path, stream, new WaveFormat(RenderSampleRate, RenderBitsPerSample, 2)))
        {
            writer.Write(samples);
        }

        WriteIndexEntry(index, plan, peak, trim);
    }

    private static void WriteIndexHeader(StringBuilder index, int fileCount)
    {
        index.AppendLine("==============================================================================");
        index.AppendLine("TestResults/GmRenders - the General MIDI listening renders");
        index.AppendLine("==============================================================================");
        index.AppendLine();
        index.AppendLine("Written by GeneralMidiListeningRenderTests with");
        index.AppendLine("CODEBRIX_AUDIO_WRITE_GM_RENDERS=1, on " +
            DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC.");
        index.AppendLine();
        index.AppendLine(fileCount.ToString(CultureInfo.InvariantCulture) +
            " files, " + RenderBitsPerSample.ToString(CultureInfo.InvariantCulture) +
            "-bit PCM stereo at " + RenderSampleRate.ToString(CultureInfo.InvariantCulture) + " Hz,");
        index.AppendLine("rendered at the synthesizer's DEFAULT master volume. Where the peak came");
        index.AppendLine("out above full scale the file was turned down to fit and the entry says so;");
        index.AppendLine("nothing is limited or compressed.");
        index.AppendLine();
        index.AppendLine("Each entry lists what is in the file, in order: the start time, the General");
        index.AppendLine("MIDI number and the official name. A line with no number is a marker rather");
        index.AppendLine("than a sound.");
        index.AppendLine();
    }

    private static void WriteIndexEntry(
        StringBuilder index, GmAuditionPlan plan, double peak, double trim)
    {
        index.AppendLine("------------------------------------------------------------------------------");
        index.AppendLine(plan.FileName);
        index.AppendLine("  " + plan.Title);
        index.Append("  ").Append(plan.Length.ToString(@"m\:ss")).Append(" long, peak ")
            .Append(peak.ToString("0.000", CultureInfo.InvariantCulture));

        if (trim < 1.0)
        {
            index.Append(" - TURNED DOWN by ")
                .Append((-20.0 * Math.Log10(trim)).ToString("0.0", CultureInfo.InvariantCulture))
                .Append(" dB to fit");
        }

        index.AppendLine();
        index.AppendLine("------------------------------------------------------------------------------");

        foreach (GmAuditionCue cue in plan.Cues)
        {
            index.AppendLine(cue.Describe());
        }

        index.AppendLine();
    }

    private static double Peak(float[] samples)
    {
        double peak = 0.0;

        for (int i = 0; i < samples.Length; i++)
        {
            double value = Math.Abs(samples[i]);
            if (value > peak) { peak = value; }
        }

        return peak;
    }

    /// <summary>The folder the renders are written to: <c>TestResults/GmRenders</c> at the repository root.</summary>
    /// <returns>The folder path.</returns>
    public static string RenderFolder() =>
        Path.Combine(RepositoryRoot(), "TestResults", RenderFolderName);

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

        throw new InvalidOperationException(
            "The repository root was not found above " + AppContext.BaseDirectory +
            ", so there is nowhere to write the listening renders.");
    }
}
