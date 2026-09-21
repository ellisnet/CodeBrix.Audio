using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.ModestSynth.Internal.Gm;
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
/// Under <c>choir-ab/</c> it also writes the choir comparison: the five pieces of
/// <see cref="GmChoirComparison" /> rendered once per voicing named in
/// <see cref="ChoirRenditions" />, with an index of their own. A file name there ends in the
/// voicing's label, so two voicings of the same piece sit side by side and differ only in the last
/// word of the name.
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

    // WHICH CHOIR VOICINGS THIS RUN WRITES. A label becomes the last word of every file name under
    // choir-ab/, so a rendition is added by adding a line here and removed by taking one away.
    // Files already in the folder under another label are left exactly as they are, which is what
    // lets a voicing rendered before a change be compared with the one rendered after it - the
    // BEFORE files were written by this test from the code as it stood before the choir was
    // rebuilt, and nothing here writes over them.
    // THE LABELS ARE THE ONES JEREMY HEARD THEM UNDER and they keep their meanings, so a first
    // impression of a file still applies to that file. Which reading the BANK carries is a separate
    // question, and the index answers it in as many words.
    private static readonly (string Label, GmChoirVoicing Voicing, GeneralMidiEnsemble Ensemble)[]
        ChoirRenditions =
        [
            ("AFTER", GmChoirVoicing.Section, GeneralMidiEnsemble.Standard),
            ("AFTER-mid", GmChoirRows.BankVoicing, GeneralMidiEnsemble.Standard),

            // THROUGH THE PUBLIC KNOB, not the internal switch - which is what makes this file the
            // proof that a consumer can reach the larger section at all.
            ("AFTER-alt", GmChoirRows.BankVoicing, GeneralMidiEnsemble.Full),
        ];

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

        List<GmAuditionPlan> choir = WriteChoirComparison(folder);

        //Assert
        foreach (GmAuditionPlan plan in plans)
        {
            FileInfo file = new FileInfo(Path.Combine(folder, plan.FileName));
            file.Exists.Should().BeTrue();
            file.Length.Should().BeGreaterThan(RenderSampleRate);   // more than a header and a click
        }

        foreach (GmAuditionPlan plan in choir)
        {
            FileInfo file = new FileInfo(
                Path.Combine(folder, GmChoirComparison.FolderName, plan.FileName));

            file.Exists.Should().BeTrue();
            file.Length.Should().BeGreaterThan(RenderSampleRate);
        }

        File.Exists(Path.Combine(folder, "INDEX.txt")).Should().BeTrue();
        File.Exists(Path.Combine(folder, GmChoirComparison.FolderName, "INDEX.txt"))
            .Should().BeTrue();
    }

    // The choir comparison: every piece rendered once per voicing this run knows about, into a
    // folder of its own with an index of its own.
    private static List<GmAuditionPlan> WriteChoirComparison(string root)
    {
        string folder = Path.Combine(root, GmChoirComparison.FolderName);
        Directory.CreateDirectory(folder);

        List<GmAuditionPlan> written = [];
        StringBuilder index = new StringBuilder();

        WriteChoirIndexHeader(index);

        foreach ((string label, GmChoirVoicing voicing, GeneralMidiEnsemble ensemble)
            in ChoirRenditions)
        {
            foreach (GmAuditionPlan plan in GmChoirComparison.Plans(label))
            {
                WriteRender(folder, plan, index, voicing, ensemble);
                written.Add(plan);
            }
        }

        File.WriteAllText(Path.Combine(folder, "INDEX.txt"), index.ToString());

        return written;
    }

    private static void WriteChoirIndexHeader(StringBuilder index)
    {
        index.AppendLine("==============================================================================");
        index.AppendLine("TestResults/GmRenders/choir-ab - the choir voicing, before and after");
        index.AppendLine("==============================================================================");
        index.AppendLine();
        index.AppendLine("Written by GeneralMidiListeningRenderTests with");
        index.AppendLine("CODEBRIX_AUDIO_WRITE_GM_RENDERS=1, on " +
            DateTime.UtcNow.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + " UTC.");
        index.AppendLine();
        index.AppendLine("Five pieces, each rendered once per voicing. A file name ends in the");
        index.AppendLine("voicing's label, so the same music under two voicings differs only in the");
        index.AppendLine("last word of the name. Everything is 16-bit PCM stereo at " +
            RenderSampleRate.ToString(CultureInfo.InvariantCulture) + " Hz,");
        index.AppendLine("through a plain GeneralMidiSynthesizer at its DEFAULT master volume, with");
        index.AppendLine("the reverb and chorus the bank asks for and nothing limited or compressed.");
        index.AppendLine();
        index.AppendLine("THE LABELS.");
        index.AppendLine("  BEFORE      the voicing as it stood before the choir was rebuilt: one");
        index.AppendLine("              formant near 2.4 kHz over the note's own fundamental, with no");
        index.AppendLine("              drift and no breath.");
        index.AppendLine("  AFTER       a close section of THREE singers on the fully open vowel with");
        index.AppendLine("              only a little air, in a bright room.");
        index.AppendLine("  AFTER-mid   THREE singers with a darker, rounder vowel and plenty of");
        index.AppendLine("              air - the same vowel and air as AFTER-alt, half the people.");
        index.AppendLine("  AFTER-alt   SIX singers in two groups, standing well apart, with plenty");
        index.AppendLine("              of air, a rounded darker vowel and a soft top.");
        index.AppendLine();
        index.AppendLine("*** THE BANK CARRIES THE READING LABELLED " + BankLabel() + ". ***");
        index.AppendLine("*** AFTER-alt IS THE PUBLIC OPT-IN: set a program's adjustment");
        index.AppendLine("***     Ensemble = GeneralMidiEnsemble.Full");
        index.AppendLine("*** and 052 and 053 sing it. These AFTER-alt files were rendered that");
        index.AppendLine("*** way - through the public knob, not through anything internal.");
        index.AppendLine();
        index.AppendLine("AFTER is reached only through an internal switch the tests can see; it is");
        index.AppendLine("not offered publicly. Changing which reading the bank carries is one");
        index.AppendLine("constant in GmChoirRows.cs.");
        index.AppendLine();
        index.AppendLine("This run wrote: " + Labels() + ". Files under any other label were written");
        index.AppendLine("by an earlier run - the BEFORE ones from the code as it stood before the");
        index.AppendLine("choir was rebuilt - and were left exactly as they were.");
        index.AppendLine();
    }

    private static void WriteRender(string folder, GmAuditionPlan plan, StringBuilder index) =>
        WriteRender(folder, plan, index, GmChoirRows.BankVoicing, GeneralMidiEnsemble.Standard);

    private static void WriteRender(
        string folder,
        GmAuditionPlan plan,
        StringBuilder index,
        GmChoirVoicing voicing,
        GeneralMidiEnsemble ensemble)
    {
        GeneralMidiSynthesizer synthesizer = new GeneralMidiSynthesizer(RenderSampleRate)
        {
            ChoirVoicing = voicing,
        };

        // The public knob, exactly as a consumer would set it, on both programs that answer it.
        synthesizer.Adjustments.Program(GmChoirComparison.ChoirProgram).Ensemble = ensemble;
        synthesizer.Adjustments.Program(GmChoirComparison.VoiceOohsProgram).Ensemble = ensemble;

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

    private static string Labels()
    {
        string[] labels = new string[ChoirRenditions.Length];

        for (int index = 0; index < labels.Length; index++)
        {
            labels[index] = ChoirRenditions[index].Label;
        }

        return string.Join(", ", labels);
    }

    // Which of the labels above is the reading the bank itself sings, with nothing asked of it.
    private static string BankLabel()
    {
        foreach ((string label, GmChoirVoicing voicing, GeneralMidiEnsemble ensemble)
            in ChoirRenditions)
        {
            if (voicing == GmChoirRows.BankVoicing && ensemble == GeneralMidiEnsemble.Standard)
            {
                return label;
            }
        }

        return GmChoirRows.BankVoicing.ToString();
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
