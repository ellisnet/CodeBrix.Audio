using System;
using System.Collections.Generic;
using System.Threading;
using CodeBrix.Audio.Midi;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// Preparation: <see cref="GeneralMidiSynthesizer.Prepare(int)" /> and
/// <see cref="GeneralMidiSynthesizer.PreparePercussion()" />, which build a program's oscillators and
/// run its code at CREATION so that the first note on the audio thread builds and compiles nothing.
/// </summary>
/// <remarks>
/// <para>
/// THE PROMISE BEING HELD HERE IS TWO-SIDED. A prepared synthesizer's first note allocates nothing,
/// and a prepared synthesizer sounds EXACTLY - bit for bit - like one that was never prepared, for
/// every program in the bank and for the kit, whether the prepared oscillators suffice or run out,
/// whether the warm-up ran or not, and whether the preparing happened on another thread or even
/// while the audio thread was already playing.
/// </para>
/// <para>
/// The allocation checks give every attempt of <see cref="AllocationProbe" /> a FRESH synthesizer,
/// because the claim is about the first note and a repeated note on one synthesizer would pass
/// whether or not it was prepared.
/// </para>
/// </remarks>
[Collection(GeneralMidiLibraryCollection.Name)]
public class GeneralMidiPreparationTests
{
    private const int ScriptBlocks = 400;
    private const int ProbeAttempts = 5;

    /// <summary>Every program number in the bank, for the bit-identity theory.</summary>
    public static IEnumerable<object[]> AllPrograms()
    {
        for (int program = 0; program < GeneralMidi.ProgramCount; program++)
        {
            yield return new object[] { program };
        }
    }

    [Theory]
    [MemberData(nameof(AllPrograms))]
    public void a_prepared_program_renders_bit_identically_to_an_unprepared_one(int program)
    {
        //Arrange
        GeneralMidiSynthesizer lazy = GmProbe.BuildForProgram(program);

        GeneralMidiSynthesizer prepared = GmProbe.BuildForProgram(program);
        prepared.Prepare(program);

        // Two notes' worth, so the script's six overlapping notes run OUT of prepared oscillators
        // and the rest are built the old way - which must carry on exactly where the lazy path is.
        GeneralMidiSynthesizer partly = GmProbe.BuildForProgram(program);
        partly.Prepare(program, 2);

        //Act
        float[] expected = PlayScript(lazy);
        float[] fromPrepared = PlayScript(prepared);
        float[] fromPartly = PlayScript(partly);

        //Assert
        GmProbe.Peak(expected).Should().BeGreaterThan(0.0);
        FirstDifference(expected, fromPrepared).Should().Be(-1);
        FirstDifference(expected, fromPartly).Should().Be(-1);
    }

    [Fact]
    public void a_prepared_kit_renders_bit_identically_to_an_unprepared_one()
    {
        //Arrange
        GeneralMidiSynthesizer lazy = GmProbe.BuildForPercussion();

        GeneralMidiSynthesizer prepared = GmProbe.BuildForPercussion();
        prepared.PreparePercussion();

        // One strike per piece, so the roll, the hi-hats and the toms run out and build the rest.
        GeneralMidiSynthesizer partly = GmProbe.BuildForPercussion();
        partly.PreparePercussion(1);

        //Act
        float[] expected = PlayKitScript(lazy);
        float[] fromPrepared = PlayKitScript(prepared);
        float[] fromPartly = PlayKitScript(partly);

        //Assert
        GmProbe.Peak(expected).Should().BeGreaterThan(0.0);
        FirstDifference(expected, fromPrepared).Should().Be(-1);
        FirstDifference(expected, fromPartly).Should().Be(-1);
    }

    [Theory]
    [InlineData((int)GeneralMidiProgram.AcousticGrandPiano)]
    [InlineData((int)GeneralMidiProgram.Pad2Warm)]
    [InlineData((int)GeneralMidiProgram.ChoirAahs)]
    [InlineData(-1)]
    public void the_warm_up_leaves_the_synthesizer_it_prepares_untouched(int program)
    {
        //Arrange
        // -1 is the kit. Forgetting makes the warm-up REALLY run here, instead of being skipped
        // because another test already warmed the voicing.
        bool kit = program < 0;
        GeneralMidiSynthesizer lazy = kit ? GmProbe.BuildForPercussion() : GmProbe.BuildForProgram(program);
        GeneralMidiSynthesizer warmed = kit ? GmProbe.BuildForPercussion() : GmProbe.BuildForProgram(program);
        GeneralMidiSynthesizer cold = kit ? GmProbe.BuildForPercussion() : GmProbe.BuildForProgram(program);
        cold.WarmUpEnabled = false;

        //Act
        GeneralMidiSynthesizer.ForgetWarmUps();
        if (kit) { warmed.PreparePercussion(); cold.PreparePercussion(); }
        else { warmed.Prepare(program); cold.Prepare(program); }

        int ringingAfterPreparation = warmed.ActiveVoiceCount;

        float[] expected = kit ? PlayKitScript(lazy) : PlayScript(lazy);
        float[] afterWarmUp = kit ? PlayKitScript(warmed) : PlayScript(warmed);
        float[] withoutWarmUp = kit ? PlayKitScript(cold) : PlayScript(cold);

        //Assert
        ringingAfterPreparation.Should().Be(0);
        FirstDifference(expected, afterWarmUp).Should().Be(-1);
        FirstDifference(expected, withoutWarmUp).Should().Be(-1);
    }

    [Fact]
    public void the_library_hands_out_prepared_synthesizers_that_sound_like_unprepared_ones()
    {
        //Arrange
        const int Program = (int)GeneralMidiProgram.Pad2Warm;
        GeneralMidiInstrumentLibrary library = GeneralMidiInstrumentLibrary.Instance;

        GeneralMidiSynthesizer fromLibrary =
            (GeneralMidiSynthesizer)library.CreateSynthesizer(Program, GmProbe.SampleRate);
        GeneralMidiSynthesizer kitFromLibrary =
            (GeneralMidiSynthesizer)library.CreatePercussionSynthesizer(GmProbe.SampleRate);

        GeneralMidiSynthesizer lazy = GeneralMidiSynthesizer.CreateForProgram(
            Program, new GeneralMidiSynthesizerSettings(GmProbe.SampleRate));
        lazy.Adjustments.CopyFrom(library.Adjustments);

        GeneralMidiSynthesizer lazyKit = GeneralMidiSynthesizer.CreateForPercussion(
            new GeneralMidiSynthesizerSettings(GmProbe.SampleRate));
        lazyKit.Adjustments.CopyFrom(library.Adjustments);

        //Act
        float[] expected = PlayScript(lazy);
        float[] actual = PlayScript(fromLibrary);
        float[] expectedKit = PlayKitScript(lazyKit);
        float[] actualKit = PlayKitScript(kitFromLibrary);

        //Assert
        fromLibrary.PreparedRuntime(Program).Should().NotBeNull();
        FirstDifference(expected, actual).Should().Be(-1);
        FirstDifference(expectedKit, actualKit).Should().Be(-1);
    }

    [Fact]
    public void a_multi_timbral_synthesizer_prepared_for_a_program_sounds_the_same_after_the_change()
    {
        //Arrange
        const int Program = (int)GeneralMidiProgram.SynthStrings1;
        GeneralMidiSynthesizer lazy = GmProbe.Build();
        GeneralMidiSynthesizer prepared = GmProbe.Build();
        prepared.Prepare(Program);

        //Act
        lazy.ProcessMidiMessage(0, 0xC0, Program, 0);
        prepared.ProcessMidiMessage(0, 0xC0, Program, 0);

        float[] expected = PlayScript(lazy);
        float[] actual = PlayScript(prepared);

        //Assert
        FirstDifference(expected, actual).Should().Be(-1);
    }

    [Fact]
    public void preparing_honours_the_ensemble_the_next_note_will_use()
    {
        //Arrange
        const int Program = (int)GeneralMidiProgram.ChoirAahs;
        GeneralMidiSynthesizer lazy = GmProbe.BuildForProgram(Program);
        lazy.Adjustments.Program(Program).Ensemble = GeneralMidiEnsemble.Full;

        GeneralMidiSynthesizer prepared = GmProbe.BuildForProgram(Program);
        prepared.Adjustments.Program(Program).Ensemble = GeneralMidiEnsemble.Full;
        prepared.Prepare(Program);

        //Act
        float[] expected = PlayScript(lazy);
        float[] actual = PlayScript(prepared);

        //Assert
        // The larger section has a runtime of its own; the ordinary one was never needed.
        prepared.PreparedRuntime(Program).Should().BeNull();
        FirstDifference(expected, actual).Should().Be(-1);
    }

    [Theory]
    [InlineData((int)GeneralMidiProgram.AcousticGrandPiano)]
    [InlineData((int)GeneralMidiProgram.Pad2Warm)]
    [InlineData(-1)]
    public void the_first_note_after_the_library_creates_a_synthesizer_allocates_nothing(int program)
    {
        //Arrange
        // -1 is the kit.
        GeneralMidiInstrumentLibrary library = GeneralMidiInstrumentLibrary.Instance;
        GeneralMidiSynthesizer[] fresh = new GeneralMidiSynthesizer[ProbeAttempts];

        for (int i = 0; i < fresh.Length; i++)
        {
            fresh[i] = (GeneralMidiSynthesizer)(program < 0
                ? library.CreatePercussionSynthesizer(GmProbe.SampleRate)
                : library.CreateSynthesizer(program, GmProbe.SampleRate));
        }

        Action work = FirstNotes(fresh, program < 0);

        //Act
        long bytes = AllocationProbe.LowestBytes(work, fresh.Length);

        //Assert
        bytes.Should().Be(0L);
    }

    [Theory]
    [InlineData((int)GeneralMidiProgram.AcousticGrandPiano)]
    [InlineData((int)GeneralMidiProgram.Pad2Warm)]
    [InlineData(-1)]
    public void without_preparation_the_first_note_builds_on_the_rendering_thread(int program)
    {
        //Arrange
        // The measurement above has teeth only if the unprepared path really does allocate there.
        GeneralMidiSynthesizer[] fresh = new GeneralMidiSynthesizer[ProbeAttempts];

        for (int i = 0; i < fresh.Length; i++)
        {
            fresh[i] = program < 0 ? GmProbe.BuildForPercussion() : GmProbe.BuildForProgram(program);
        }

        Action work = FirstNotes(fresh, program < 0);

        //Act
        long bytes = AllocationProbe.LowestBytes(work, fresh.Length);

        //Assert
        bytes.Should().BeGreaterThan(0L);
    }

    [Fact]
    public void preparing_on_a_worker_thread_then_playing_allocates_nothing_and_sounds_the_same()
    {
        //Arrange
        const int Program = (int)GeneralMidiProgram.Pad2Warm;
        GeneralMidiSynthesizer[] fresh = new GeneralMidiSynthesizer[ProbeAttempts];

        for (int i = 0; i < fresh.Length; i++)
        {
            GeneralMidiSynthesizer synthesizer = GmProbe.BuildForProgram(Program);
            Thread worker = new Thread(() => synthesizer.Prepare(Program));
            worker.Start();
            worker.Join();
            fresh[i] = synthesizer;
        }

        GeneralMidiSynthesizer lazy = GmProbe.BuildForProgram(Program);
        GeneralMidiSynthesizer preparedElsewhere = GmProbe.BuildForProgram(Program);
        Thread preparer = new Thread(() => preparedElsewhere.Prepare(Program));
        preparer.Start();
        preparer.Join();

        Action work = FirstNotes(fresh, kit: false);

        //Act
        long bytes = AllocationProbe.LowestBytes(work, fresh.Length);
        float[] expected = PlayScript(lazy);
        float[] actual = PlayScript(preparedElsewhere);

        //Assert
        bytes.Should().Be(0L);
        FirstDifference(expected, actual).Should().Be(-1);
    }

    [Fact]
    public void preparing_while_the_audio_thread_already_plays_the_program_cannot_change_the_sound()
    {
        //Arrange
        // Whoever gets to the program first - the worker preparing it or the render playing it -
        // the other one's copy is dropped, and either way the render is the lazy one.
        const int Program = (int)GeneralMidiProgram.Pad2Warm;
        float[] expected = PlayScript(ProgramOnChannelOne(Program));

        //Act
        //Assert
        for (int attempt = 0; attempt < 12; attempt++)
        {
            GeneralMidiSynthesizer synthesizer = ProgramOnChannelOne(Program);
            using ManualResetEventSlim go = new ManualResetEventSlim(false);

            Thread worker = new Thread(() =>
            {
                go.Wait();
                synthesizer.Prepare(Program);
            });

            worker.Start();
            go.Set();
            float[] actual = PlayScript(synthesizer);
            worker.Join();

            FirstDifference(expected, actual).Should().Be(-1);
        }
    }

    [Fact]
    public void preparing_twice_changes_nothing_the_second_time()
    {
        //Arrange
        const int Program = (int)GeneralMidiProgram.SynthStrings1;
        GeneralMidiSynthesizer synthesizer = GmProbe.BuildForProgram(Program);

        //Act
        synthesizer.Prepare(Program);
        var first = synthesizer.PreparedRuntime(Program);
        int built = first.BuiltCount;
        int pooled = first.PooledCount(0);

        synthesizer.Prepare(Program);
        synthesizer.Prepare(Program, 64);

        //Assert
        synthesizer.PreparedRuntime(Program).Should().BeSameAs(first);
        first.BuiltCount.Should().Be(built);
        first.PooledCount(0).Should().Be(pooled);
    }

    [Fact]
    public void a_program_that_has_already_played_is_left_alone()
    {
        //Arrange
        const int Program = (int)GeneralMidiProgram.Flute;
        GeneralMidiSynthesizer synthesizer = GmProbe.BuildForProgram(Program);
        GmProbe.PlayNote(synthesizer, 1, 72, 100, 0.05, 0.05);

        var runtime = synthesizer.PreparedRuntime(Program);
        int built = runtime.BuiltCount;

        //Act
        synthesizer.Prepare(Program);

        //Assert
        synthesizer.PreparedRuntime(Program).Should().BeSameAs(runtime);
        runtime.BuiltCount.Should().Be(built);
    }

    [Fact]
    public void it_builds_the_default_number_of_notes_capped_at_the_polyphony()
    {
        //Arrange
        const int Program = (int)GeneralMidiProgram.SynthStrings1;
        GeneralMidiSynthesizer ordinary = GmProbe.BuildForProgram(Program);
        GeneralMidiSynthesizer small = GmProbe.BuildForProgram(Program, s => s.MaximumPolyphony = 3);
        GeneralMidiSynthesizer none = GmProbe.BuildForProgram(Program);

        //Act
        ordinary.Prepare(Program);
        small.Prepare(Program);
        none.Prepare(Program, 0);

        //Assert
        var runtime = ordinary.PreparedRuntime(Program);
        int perNote = runtime.Spec.OscillatorCount;

        runtime.BuiltCount.Should().Be(GeneralMidiSynthesizer.DefaultPreparedVoices * perNote);
        small.PreparedRuntime(Program).BuiltCount.Should().Be(3 * perNote);
        none.PreparedRuntime(Program).Should().NotBeNull();
        none.PreparedRuntime(Program).BuiltCount.Should().Be(0);
    }

    [Fact]
    public void the_kit_is_prepared_piece_by_piece()
    {
        //Arrange
        GeneralMidiSynthesizer kit = GmProbe.BuildForPercussion();

        //Act
        kit.PreparePercussion();

        //Assert
        for (int note = GeneralMidi.LowestPercussionNote; note <= GeneralMidi.HighestPercussionNote; note++)
        {
            var runtime = kit.PreparedPercussionRuntime(note);
            runtime.Should().NotBeNull();
            runtime.BuiltCount.Should().Be(
                GeneralMidiSynthesizer.DefaultPreparedPercussionVoices * runtime.Spec.OscillatorCount);
        }
    }

    [Fact]
    public void the_multi_timbral_shape_is_not_prepared_because_nothing_says_what_it_will_play()
    {
        //Arrange
        GeneralMidiSynthesizer whole = (GeneralMidiSynthesizer)GeneralMidiInstrumentLibrary.Instance
            .CreateMultiTimbralSynthesizer(GmProbe.SampleRate);

        //Act
        //Assert
        for (int program = 0; program < GeneralMidi.ProgramCount; program++)
        {
            whole.PreparedRuntime(program).Should().BeNull();
        }
    }

    [Fact]
    public void the_arguments_are_checked()
    {
        //Arrange
        GeneralMidiSynthesizer synthesizer = GmProbe.Build();

        //Act
        Action low = () => synthesizer.Prepare(-1);
        Action high = () => synthesizer.Prepare(GeneralMidi.ProgramCount);
        Action negative = () => synthesizer.Prepare(0, -1);
        Action negativeKit = () => synthesizer.PreparePercussion(-1);

        //Assert
        low.Should().Throw<ArgumentOutOfRangeException>();
        high.Should().Throw<ArgumentOutOfRangeException>();
        negative.Should().Throw<ArgumentOutOfRangeException>();
        negativeKit.Should().Throw<ArgumentOutOfRangeException>();
    }

    // A multi-timbral synthesizer already on the program, as a host would have it when it decides to
    // prepare ahead of the music's own first note.
    private static GeneralMidiSynthesizer ProgramOnChannelOne(int program)
    {
        GeneralMidiSynthesizer synthesizer = GmProbe.Build();
        synthesizer.SetProgram(1, program);
        return synthesizer;
    }

    // The first notes of each fresh synthesizer in turn - one synthesizer per probe attempt. The
    // closure is built here, before anything is measured, and captures only what it was given.
    private static Action FirstNotes(GeneralMidiSynthesizer[] fresh, bool kit)
    {
        int next = 0;
        float[] left = new float[fresh[0].BlockSize * 4];
        float[] right = new float[left.Length];

        return () =>
        {
            GeneralMidiSynthesizer synthesizer = fresh[next++];

            if (kit)
            {
                synthesizer.ProcessMidiMessage(9, 0x90, (int)GeneralMidiPercussion.AcousticBassDrum, 110);
                synthesizer.ProcessMidiMessage(9, 0x90, (int)GeneralMidiPercussion.AcousticSnare, 100);
                synthesizer.ProcessMidiMessage(9, 0x90, (int)GeneralMidiPercussion.ClosedHiHat, 90);
                synthesizer.ProcessMidiMessage(9, 0x90, (int)GeneralMidiPercussion.CrashCymbal1, 100);
            }
            else
            {
                synthesizer.ProcessMidiMessage(0, 0x90, 60, 100);
                synthesizer.ProcessMidiMessage(0, 0x90, 64, 100);
            }

            synthesizer.Render(left, right);
            synthesizer.Render(left, right);

            if (!kit) { synthesizer.ProcessMidiMessage(0, 0x80, 60, 0); }

            synthesizer.Render(left, right);
        };
    }

    // Six overlapping notes with re-strikes and releases, in whole blocks, on wire channel 0 - enough
    // to recycle oscillators and to run past two prepared notes. Returns left and right, interleaved.
    private static float[] PlayScript(GeneralMidiSynthesizer synthesizer)
    {
        return Play(synthesizer, (block, s) =>
        {
            switch (block)
            {
                case 0:
                    s.ProcessMidiMessage(0, 0x90, 60, 100);
                    break;
                case 20:
                    s.ProcessMidiMessage(0, 0x90, 64, 90);
                    s.ProcessMidiMessage(0, 0x90, 67, 80);
                    break;
                case 40:
                    s.ProcessMidiMessage(0, 0x80, 60, 0);
                    s.ProcessMidiMessage(0, 0x90, 72, 110);
                    break;
                case 60:
                    s.ProcessMidiMessage(0, 0x90, 60, 70);
                    break;
                case 80:
                    s.ProcessMidiMessage(0, 0x80, 64, 0);
                    s.ProcessMidiMessage(0, 0x80, 67, 0);
                    break;
                case 100:
                    s.ProcessMidiMessage(0, 0x90, 48, 100);
                    s.ProcessMidiMessage(0, 0x90, 55, 100);
                    s.ProcessMidiMessage(0, 0x90, 76, 100);
                    break;
                case 160:
                    s.ProcessMidiMessage(0, 0x80, 48, 0);
                    s.ProcessMidiMessage(0, 0x80, 55, 0);
                    s.ProcessMidiMessage(0, 0x80, 60, 0);
                    s.ProcessMidiMessage(0, 0x80, 72, 0);
                    s.ProcessMidiMessage(0, 0x80, 76, 0);
                    break;
                case 220:
                    s.ProcessMidiMessage(0, 0x90, 62, 100);
                    break;
                case 260:
                    s.ProcessMidiMessage(0, 0x80, 62, 0);
                    break;
            }
        });
    }

    // A bar of drums: kick and snare, sixteenth hi-hats with an open one cut by the next closed one,
    // a snare roll, a tom fill and a crash - the overlaps and the choke groups a real part has.
    private static float[] PlayKitScript(GeneralMidiSynthesizer synthesizer)
    {
        return Play(synthesizer, (block, s) =>
        {
            if (block >= 240) { return; }

            if (block % 12 == 0)
            {
                int hat = block % 48 == 36
                    ? (int)GeneralMidiPercussion.OpenHiHat
                    : (int)GeneralMidiPercussion.ClosedHiHat;

                s.ProcessMidiMessage(9, 0x90, hat, 90);
            }

            if (block % 48 == 0) { s.ProcessMidiMessage(9, 0x90, (int)GeneralMidiPercussion.AcousticBassDrum, 110); }
            if (block % 48 == 24) { s.ProcessMidiMessage(9, 0x90, (int)GeneralMidiPercussion.AcousticSnare, 100); }

            if (block >= 144 && block < 168 && block % 3 == 0)
            {
                s.ProcessMidiMessage(9, 0x90, (int)GeneralMidiPercussion.AcousticSnare, 60 + block - 144);
            }

            if (block == 168) { s.ProcessMidiMessage(9, 0x90, (int)GeneralMidiPercussion.HighTom, 100); }
            if (block == 176) { s.ProcessMidiMessage(9, 0x90, (int)GeneralMidiPercussion.HiMidTom, 100); }
            if (block == 184) { s.ProcessMidiMessage(9, 0x90, (int)GeneralMidiPercussion.LowTom, 100); }
            if (block == 192) { s.ProcessMidiMessage(9, 0x90, (int)GeneralMidiPercussion.CrashCymbal1, 110); }
        });
    }

    private static float[] Play(GeneralMidiSynthesizer synthesizer, Action<int, GeneralMidiSynthesizer> atBlock)
    {
        int blockSize = synthesizer.BlockSize;
        float[] left = new float[blockSize];
        float[] right = new float[blockSize];
        float[] output = new float[ScriptBlocks * blockSize * 2];

        for (int block = 0; block < ScriptBlocks; block++)
        {
            atBlock(block, synthesizer);
            synthesizer.Render(left, right);

            for (int i = 0; i < blockSize; i++)
            {
                output[(((block * blockSize) + i) * 2) + 0] = left[i];
                output[(((block * blockSize) + i) * 2) + 1] = right[i];
            }
        }

        return output;
    }

    // The first sample whose BITS differ, or -1. Bits rather than values, so a NaN or a negative zero
    // cannot hide a difference.
    private static int FirstDifference(float[] expected, float[] actual)
    {
        if (expected.Length != actual.Length) { return 0; }

        for (int i = 0; i < expected.Length; i++)
        {
            if (BitConverter.SingleToInt32Bits(expected[i]) != BitConverter.SingleToInt32Bits(actual[i]))
            {
                return i;
            }
        }

        return -1;
    }
}
