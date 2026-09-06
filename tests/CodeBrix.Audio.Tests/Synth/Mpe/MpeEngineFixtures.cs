using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.Sfz;
using CodeBrix.Audio.Tests.Synth.Sfz;

namespace CodeBrix.Audio.Tests.Synth.Mpe;

/// <summary>
/// The instruments and the performances the SoundFont and SFZ MPE tests share, plus a digest that
/// turns a whole render into one number.
/// </summary>
/// <remarks>
/// <para>
/// Both instruments play a steady 440 Hz sine at their root note, so a note's pitch can be measured
/// against the bend arithmetic. The SoundFont is the committed synthetic fixture, whose upper key
/// range plays a 440 Hz tone at root key 69; the SFZ instrument is a sine written here and played at
/// key 60. Nothing else about them matters to these tests.
/// </para>
/// <para>
/// The performances are the two the plan names: an export shaped like a sequencer's, with no
/// configuration message at all, and a file that says everything out loud. They are built as MIDI
/// messages rather than read from a file, because an expressive performance is only a particular
/// pattern of ordinary MIDI messages.
/// </para>
/// </remarks>
internal sealed class MpeEngineFixtures : IDisposable
{
    /// <summary>The sample rate every engine MPE test renders at.</summary>
    public const int SampleRate = 44100;

    /// <summary>The frequency both fixture instruments play at their root note.</summary>
    public const double RootHertz = 440.0;

    /// <summary>The key that plays the fixture SoundFont's 440 Hz tone at its own pitch.</summary>
    public const int SoundFontKey = 69;

    /// <summary>The key that plays the fixture SFZ instrument's 440 Hz tone at its own pitch.</summary>
    public const int SfzKey = 60;

    /// <summary>How long each note in a performance sounds.</summary>
    public const double NoteSeconds = 0.4;

    /// <summary>How far apart the notes of a performance start.</summary>
    public const double NoteSpacing = 0.6;

    /// <summary>
    /// A bend reaching three semitones of a 48-semitone member range. Bends are chosen so the 14-bit
    /// value is exact: a bend of k/8192 over 48 semitones is 3k/512 semitones, a whole number
    /// whenever k is a multiple of 512.
    /// </summary>
    public const double ThreeSemitonesOf48 = 512.0 / 8192.0;

    /// <summary>A bend reaching six semitones of a 48-semitone member range.</summary>
    public const double SixSemitonesOf48 = 1024.0 / 8192.0;

    private readonly SfzTestInstruments _files;

    private MpeEngineFixtures(SfzTestInstruments files, SfzInstrument sfz, SoundFont soundFont)
    {
        _files = files;
        SfzInstrument = sfz;
        SoundFont = soundFont;
    }

    /// <summary>The SFZ instrument under test.</summary>
    public SfzInstrument SfzInstrument { get; }

    /// <summary>The SoundFont under test.</summary>
    public SoundFont SoundFont { get; }

    /// <summary>The SoundFont synthesizer the last render used, so a test can read its state after.</summary>
    public SoundFontSynthesizer LastSoundFontSynthesizer { get; private set; }

    /// <summary>The SFZ synthesizer the last render used, so a test can read its state after.</summary>
    public SfzSynthesizer LastSfzSynthesizer { get; private set; }

    /// <summary>Builds both instruments.</summary>
    /// <param name="sfzOpcodes">Extra opcodes for the SFZ region, or null for a plain sine.</param>
    /// <returns>The fixture. The caller disposes it.</returns>
    public static MpeEngineFixtures Create(string sfzOpcodes = null) =>
        CreateWithSfzBody(
            "<region> sample=tone.wav pitch_keycenter=" + SfzKey + " lokey=0 hikey=127 " +
            "ampeg_release=0.005 " + (sfzOpcodes ?? string.Empty));

    /// <summary>Builds both instruments, writing the whole SFZ file rather than one region.</summary>
    /// <param name="sfzBody">The text of the SFZ file. The sine sample is <c>tone.wav</c>.</param>
    /// <returns>The fixture. The caller disposes it.</returns>
    public static MpeEngineFixtures CreateWithSfzBody(string sfzBody)
    {
        var files = SfzTestInstruments.Create();
        files.WriteSineWav("tone.wav", (float)RootHertz, SampleRate * 8);

        return new MpeEngineFixtures(
            files,
            files.Load(sfzBody),
            SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName));
    }

    /// <summary>Renders a sequence through the SoundFont engine and returns the left channel.</summary>
    /// <param name="sequence">The sequence to play.</param>
    /// <param name="seconds">How long to render for.</param>
    /// <param name="configure">Configures the synthesizer before it plays.</param>
    /// <returns>The left channel.</returns>
    public float[] RenderSoundFont(
        MidiSequence sequence, double seconds, Action<SoundFontSynthesizer> configure = null) =>
        RenderSoundFontStereo(sequence, seconds, configure).Left;

    /// <summary>Renders a sequence through the SoundFont engine and returns both channels.</summary>
    /// <param name="sequence">The sequence to play.</param>
    /// <param name="seconds">How long to render for.</param>
    /// <param name="configure">Configures the synthesizer before it plays.</param>
    /// <returns>The rendered channels.</returns>
    public (float[] Left, float[] Right) RenderSoundFontStereo(
        MidiSequence sequence, double seconds, Action<SoundFontSynthesizer> configure = null)
    {
        // Reverb and chorus are off so a note's tail does not colour the next note's pitch.
        var settings = new SoundFontSynthesizerSettings(SampleRate) { EnableReverbAndChorus = false };
        var synthesizer = new SoundFontSynthesizer(SoundFont, settings) { MasterVolume = 1f };

        configure?.Invoke(synthesizer);
        LastSoundFontSynthesizer = synthesizer;

        return Render(synthesizer, sequence, seconds);
    }

    /// <summary>Renders a sequence through the SFZ engine and returns the left channel.</summary>
    /// <param name="sequence">The sequence to play.</param>
    /// <param name="seconds">How long to render for.</param>
    /// <param name="configure">Configures the synthesizer before it plays.</param>
    /// <returns>The left channel.</returns>
    public float[] RenderSfz(
        MidiSequence sequence, double seconds, Action<SfzSynthesizer> configure = null) =>
        RenderSfzStereo(sequence, seconds, configure).Left;

    /// <summary>Renders a sequence through the SFZ engine and returns both channels.</summary>
    /// <param name="sequence">The sequence to play.</param>
    /// <param name="seconds">How long to render for.</param>
    /// <param name="configure">Configures the synthesizer before it plays.</param>
    /// <returns>The rendered channels.</returns>
    public (float[] Left, float[] Right) RenderSfzStereo(
        MidiSequence sequence, double seconds, Action<SfzSynthesizer> configure = null)
    {
        var synthesizer = new SfzSynthesizer(SfzInstrument, SampleRate) { MasterVolume = 1f };

        configure?.Invoke(synthesizer);
        LastSfzSynthesizer = synthesizer;

        return Render(synthesizer, sequence, seconds);
    }

    /// <summary>
    /// An export shaped like a sequencer's: a bend written for every channel before the first note,
    /// then one note per channel from 2 to 5, and nothing at all on channel 1 - the shape the
    /// automatic detector looks for. The four bends reach +3, -6, +6 and 0 semitones of a
    /// 48-semitone member range.
    /// </summary>
    /// <param name="key">The note to play.</param>
    /// <returns>The events, in order.</returns>
    public static IEnumerable<MidiEvent> AbletonStyleExport(int key)
    {
        double[] bends = [ThreeSemitonesOf48, -SixSemitonesOf48, SixSemitonesOf48, 0.0];

        for (var note = 0; note < bends.Length; note++)
        {
            yield return MpeSequences.Bend(0.0, note + 2, bends[note]);
        }

        for (var note = 0; note < bends.Length; note++)
        {
            var start = note * NoteSpacing;
            yield return MpeSequences.NoteOn(start, note + 2, key);
            yield return MpeSequences.NoteOff(start + NoteSeconds, note + 2, key, 64);
        }
    }

    /// <summary>
    /// A file that says everything: a four-member lower zone, a four-member upper zone, a bend range
    /// per zone, master bends on both, and one note in each zone plus one outside both.
    /// </summary>
    /// <param name="key">The note to play.</param>
    /// <returns>The events, in order.</returns>
    public static IEnumerable<MidiEvent> FullySpecified(int key)
    {
        foreach (var midiEvent in MpeSequences.Rpn(0.0, 1, 6, 4))
        {
            yield return midiEvent;
        }

        foreach (var midiEvent in MpeSequences.Rpn(0.0, 16, 6, 4))
        {
            yield return midiEvent;
        }

        // RPN 0 on a member configures every member of that zone.
        foreach (var midiEvent in MpeSequences.Rpn(0.01, 2, 0, 12))
        {
            yield return midiEvent;
        }

        foreach (var midiEvent in MpeSequences.Rpn(0.01, 13, 0, 24))
        {
            yield return midiEvent;
        }

        yield return MpeSequences.Bend(0.02, 1, 0.5);
        yield return MpeSequences.Bend(0.02, 16, -0.5);
        yield return MpeSequences.Bend(0.02, 3, 0.25);
        yield return MpeSequences.Bend(0.02, 13, 0.5);
        yield return MpeSequences.Bend(0.02, 8, 0.5);

        int[] channels = [3, 13, 8];

        for (var note = 0; note < channels.Length; note++)
        {
            var start = note * NoteSpacing;
            yield return MpeSequences.NoteOn(start, channels[note], key);
            yield return MpeSequences.NoteOff(start + NoteSeconds, channels[note], key, 64);
        }
    }

    /// <summary>
    /// The export above with every other thing an expressive performance carries piled on: a
    /// registered bend range, master volume, expression and pedal gestures on channel 1, per-channel
    /// timbre, and per-channel pressure. This is what the byte-identity fence renders.
    /// </summary>
    /// <param name="key">The note to play.</param>
    /// <returns>The events, in order.</returns>
    public static IEnumerable<MidiEvent> ExpressivePerformance(int key)
    {
        foreach (var midiEvent in MpeSequences.Rpn(0.0, 2, 0, 12))
        {
            yield return midiEvent;
        }

        yield return MpeSequences.Controller(0.0, 1, 7, 100);
        yield return MpeSequences.Controller(0.0, 1, 11, 90);
        yield return MpeSequences.Controller(0.0, 1, 10, 40);
        yield return MpeSequences.Controller(0.0, 1, 1, 30);
        yield return MpeSequences.Controller(0.0, 1, 64, 127);
        yield return MpeSequences.Pressure(0.0, 1, 70);

        yield return MpeSequences.Controller(0.0, 2, 74, 127);
        yield return MpeSequences.Controller(0.0, 3, 74, 8);
        yield return MpeSequences.Pressure(0.0, 2, 110);
        yield return MpeSequences.Pressure(0.0, 4, 20);

        foreach (var midiEvent in AbletonStyleExport(key))
        {
            yield return midiEvent;
        }

        yield return MpeSequences.Controller(2.5, 1, 64, 0);
    }

    /// <summary>
    /// One number standing for a whole render, over the raw bit patterns of every sample, so a test
    /// can say "this is exactly what it was" rather than "this is close enough".
    /// </summary>
    /// <param name="left">The left channel.</param>
    /// <param name="right">The right channel.</param>
    /// <returns>An FNV-1a digest of both channels.</returns>
    public static ulong Digest(float[] left, float[] right)
    {
        var hash = 14695981039346656037UL;

        for (var index = 0; index < left.Length; index++)
        {
            hash = Fold(hash, BitConverter.SingleToUInt32Bits(left[index]));
            hash = Fold(hash, BitConverter.SingleToUInt32Bits(right[index]));
        }

        return hash;
    }

    /// <summary>How far a note's measured pitch is from the pitch the bend arithmetic asks for.</summary>
    /// <param name="audio">The rendered channel.</param>
    /// <param name="note">Which note of the performance to measure, counting from zero.</param>
    /// <param name="semitones">The expected offset from the root, in semitones.</param>
    /// <returns>The distance in cents, never negative.</returns>
    public static double Cents(float[] audio, int note, double semitones)
    {
        var offset = (int)((note * NoteSpacing * SampleRate) + 4096);
        var measured = PitchProbe.Hertz(audio, offset, SampleRate);
        var expected = RootHertz * Math.Pow(2.0, semitones / 12.0);

        return Math.Abs(PitchProbe.Cents(measured, expected));
    }

    /// <summary>The root-mean-square level of one note's window.</summary>
    /// <param name="audio">The rendered channel.</param>
    /// <param name="note">Which note of the performance to measure, counting from zero.</param>
    /// <returns>The level.</returns>
    public static double Level(float[] audio, int note) =>
        Rms(audio, (int)((note * NoteSpacing * SampleRate) + 4096), 4096);

    /// <summary>The root-mean-square level of a window.</summary>
    /// <param name="audio">The rendered channel.</param>
    /// <param name="offset">The first frame of the window.</param>
    /// <param name="length">How many frames to measure.</param>
    /// <returns>The level.</returns>
    public static double Rms(float[] audio, int offset, int length)
    {
        var sum = 0.0;

        for (var index = offset; index < offset + length && index < audio.Length; index++)
        {
            sum += audio[index] * (double)audio[index];
        }

        return Math.Sqrt(sum / length);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        _files.Dispose();
    }

    private static (float[] Left, float[] Right) Render(
        IMidiSynthesizer synthesizer, MidiSequence sequence, double seconds)
    {
        var sequencer = new MidiSequencer(synthesizer);
        sequencer.Play(sequence, loop: false);

        var frames = (int)Math.Ceiling(seconds * SampleRate);
        var left = new float[frames];
        var right = new float[frames];
        sequencer.Render(left, right);

        return (left, right);
    }

    private static ulong Fold(ulong hash, uint bits)
    {
        for (var shift = 0; shift < 32; shift += 8)
        {
            hash ^= (byte)(bits >> shift);
            hash *= 1099511628211UL;
        }

        return hash;
    }
}
