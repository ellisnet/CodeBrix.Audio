using System;
using System.Threading;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.ModestSynth.Tests.Gm;
using CodeBrix.Audio.Playback;
using CodeBrix.Audio.Wave;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Integration;

/// <summary>
/// The listening session: the General MIDI bank played OUT LOUD through the real audio device.
/// Opt-in via CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1; skipped otherwise.
/// </summary>
/// <remarks>
/// <para>
/// There is a tour of all 128 programs, one test per General MIDI family so a family can be
/// auditioned by itself, the percussion kit both piece by piece and as a groove, and four real
/// pieces of generated music. They join <see cref="ModestSynthAudibleTests" /> behind the same
/// gate and take the same <see cref="AudibleTestScope" />, so two audible tests never sound over
/// each other even across test projects.
/// </para>
/// <para>
/// EVERY ONE OF THEM SAYS WHAT IT IS PLAYING WHILE IT PLAYS IT. The program number and its
/// official General MIDI name are written to the console as the play head reaches them - straight
/// to <see cref="Console.Out" /> rather than through a test output helper, because the runner
/// holds a test's output until the test ends and a five-minute tour that names its instruments
/// afterwards would be useless.
/// </para>
/// <para>
/// To run one of them by itself, run the test assembly directly - see MAINTAINER-README.txt:
/// </para>
/// <code>
/// CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 \
///   dotnet tests/CodeBrix.Audio.ModestSynth.Tests/bin/Debug/net10.0/CodeBrix.Audio.ModestSynth.Tests.dll \
///   -method "*plays_the_ensemble_family*"
/// </code>
/// </remarks>
[Collection("SharedAudioOutput")]
public sealed class GeneralMidiAudibleTests : IDisposable
{
    private static readonly bool PlaybackEnabled =
        Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS") == "1";

    private const string PlaybackSkipReason =
        "Set CODEBRIX_AUDIO_RUN_PLAYBACK_TESTS=1 to run tests that open the audio device.";

    // How far behind the music the announcements are allowed to fall. Twenty milliseconds is under
    // a fiftieth of a second and well inside the device's own latency.
    private static readonly TimeSpan AnnouncementInterval = TimeSpan.FromMilliseconds(20);

    /// <summary>Resets the shared output before each test, for isolation.</summary>
    public GeneralMidiAudibleTests() => SharedAudioOutput.Shutdown();

    /// <summary>Resets the process-wide shared output after each test for isolation.</summary>
    public void Dispose() => SharedAudioOutput.Shutdown();

    [Fact]
    public void plays_a_tour_of_all_128_programs() => Play(GmAudition.AllPrograms);

    [Fact]
    public void plays_the_piano_family() => PlayFamily(GeneralMidiProgramFamily.Piano);

    [Fact]
    public void plays_the_chromatic_percussion_family() =>
        PlayFamily(GeneralMidiProgramFamily.ChromaticPercussion);

    [Fact]
    public void plays_the_organ_family() => PlayFamily(GeneralMidiProgramFamily.Organ);

    [Fact]
    public void plays_the_guitar_family() => PlayFamily(GeneralMidiProgramFamily.Guitar);

    [Fact]
    public void plays_the_bass_family() => PlayFamily(GeneralMidiProgramFamily.Bass);

    [Fact]
    public void plays_the_strings_family() => PlayFamily(GeneralMidiProgramFamily.Strings);

    [Fact]
    public void plays_the_ensemble_family() => PlayFamily(GeneralMidiProgramFamily.Ensemble);

    [Fact]
    public void plays_the_brass_family() => PlayFamily(GeneralMidiProgramFamily.Brass);

    [Fact]
    public void plays_the_reed_family() => PlayFamily(GeneralMidiProgramFamily.Reed);

    [Fact]
    public void plays_the_pipe_family() => PlayFamily(GeneralMidiProgramFamily.Pipe);

    [Fact]
    public void plays_the_synth_lead_family() => PlayFamily(GeneralMidiProgramFamily.SynthLead);

    [Fact]
    public void plays_the_synth_pad_family() => PlayFamily(GeneralMidiProgramFamily.SynthPad);

    [Fact]
    public void plays_the_synth_effects_family() => PlayFamily(GeneralMidiProgramFamily.SynthEffects);

    [Fact]
    public void plays_the_ethnic_family() => PlayFamily(GeneralMidiProgramFamily.Ethnic);

    [Fact]
    public void plays_the_percussive_family() => PlayFamily(GeneralMidiProgramFamily.Percussive);

    [Fact]
    public void plays_the_sound_effects_family() => PlayFamily(GeneralMidiProgramFamily.SoundEffects);

    [Fact]
    public void plays_every_piece_of_the_percussion_kit() => Play(GmAudition.PercussionRollCall);

    [Fact]
    public void plays_a_groove_on_the_percussion_kit() => Play(() => GmAudition.PercussionGroove());

    [Fact]
    public void plays_the_skytnt_ordinary_piece() => Play(GmRealPieces.Ordinary);

    [Fact]
    public void plays_the_skytnt_dense_piece() => Play(GmRealPieces.Dense);

    [Fact]
    public void plays_the_mupt_duet_on_celesta_and_choir_aahs() => Play(GmRealPieces.Duet);

    [Fact]
    public void plays_the_mupt_air() => Play(GmRealPieces.Air);

    private static void PlayFamily(GeneralMidiProgramFamily family) =>
        Play(() => GmAudition.Family(family));

    private static void Play(Func<GmAuditionPlan> build) => Play(build, null);

    private static void Play(Func<GmAuditionPlan> build, Action<GeneralMidiSynthesizer> configure)
    {
        Assert.SkipUnless(PlaybackEnabled, PlaybackSkipReason);

        //Arrange
        GmAuditionPlan plan = build();

        using var scope = new AudibleTestScope();
        using var player = new MidiMusicPlayer();
        using var ended = new ManualResetEventSlim(false);

        player.PlaybackEnded += (_, _) => ended.Set();

        // The device settles on its own rate, so the synthesizer is built at that rate rather than
        // at a rate of our choosing - otherwise everything is transposed by the ratio between them.
        player.Load(
            rate =>
            {
                GeneralMidiSynthesizer synthesizer =
                    new GeneralMidiSynthesizer(new GeneralMidiSynthesizerSettings(rate));

                if (configure != null) { configure(synthesizer); }

                return synthesizer;
            },
            plan.Sequence);

        player.Volume = 0.7f;

        Announce(plan);

        //Act
        player.Play();
        bool finished = Follow(player, plan, ended);

        //Assert - what matters is heard rather than asserted; these three say the transport really
        // ran the whole sequence and stopped at its natural end rather than failing quietly.
        finished.Should().BeTrue();
        player.Position.Should().BeGreaterThan(TimeSpan.Zero);
        player.PlaybackState.Should().Be(PlaybackState.Stopped);
    }

    // Follows the play head, announcing each cue as the music reaches it, until the sequence ends.
    private static bool Follow(
        MidiMusicPlayer player, GmAuditionPlan plan, ManualResetEventSlim ended)
    {
        DateTime deadline = DateTime.UtcNow + plan.Length + TimeSpan.FromSeconds(30);
        int next = 0;

        while (!ended.IsSet && DateTime.UtcNow < deadline)
        {
            TimeSpan position = player.Position;

            while (next < plan.Cues.Count && plan.Cues[next].Start <= position)
            {
                Write(plan.Cues[next].Describe());
                next++;
            }

            ended.Wait(AnnouncementInterval, TestContext.Current.CancellationToken);
        }

        // A piece shorter than the poll interval, or one that ended early, still gets said.
        while (next < plan.Cues.Count)
        {
            Write(plan.Cues[next].Describe());
            next++;
        }

        return ended.IsSet;
    }

    private static void Announce(GmAuditionPlan plan)
    {
        Write(string.Empty);
        Write("=== " + plan.Title + " ===");
        Write("    " + plan.Length.ToString(@"m\:ss") + " long, " + plan.Cues.Count + " to listen for");
        Write(string.Empty);
    }

    // Straight to the console and flushed: the runner buffers a test's own output until the test
    // ends, which is no use at all during a five-minute tour.
    private static void Write(string line)
    {
        Console.Out.WriteLine(line);
        Console.Out.Flush();
    }
}
