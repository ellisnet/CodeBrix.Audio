using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Tests.Synth;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Builds the tiny synthetic songs the multi-track tests play: constant-valued WAV stems written in
/// code, and MIDI sequences assembled from events. Nothing third-party, and nothing on disk that
/// outlives the test.
/// </summary>
/// <remarks>
/// Constant-valued stems are deliberate: they turn every gain, pan, mute and offset assertion into
/// arithmetic a reader can check by eye, which a tone or a noise burst cannot.
/// </remarks>
internal sealed class MultiTrackTestSong : IDisposable
{
    /// <summary>The sample rate everything here is built at, so no rate conversion is in the way.</summary>
    public const int SampleRate = 44100;

    private static readonly object SoundFontGate = new object();
    private static SoundFont sharedSoundFont;

    private MultiTrackTestSong(string directory) => Directory = directory;

    /// <summary>The temp directory holding this song's files. Deleted on dispose.</summary>
    public string Directory { get; }

    /// <summary>Creates a fresh temp directory to build a song in.</summary>
    public static MultiTrackTestSong Create()
    {
        var directory = Path.Combine(Path.GetTempPath(), "codebrix-mtp-" + Path.GetRandomFileName());
        System.IO.Directory.CreateDirectory(directory);
        return new MultiTrackTestSong(directory);
    }

    /// <summary>
    /// Writes a stereo 32-bit float WAV whose two channels each hold a constant value.
    /// </summary>
    /// <param name="name">The file name to write inside this song's directory.</param>
    /// <param name="left">The value every left sample takes.</param>
    /// <param name="right">The value every right sample takes.</param>
    /// <param name="frames">How many frames to write.</param>
    /// <param name="sampleRate">The rate to declare in the header.</param>
    /// <returns>The full path to the file.</returns>
    public string WriteConstantWav(string name, float left, float right, int frames, int sampleRate = SampleRate)
    {
        var path = Path.Combine(Directory, name);
        using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 2)))
        {
            var samples = new float[frames * 2];
            for (var i = 0; i < frames; i++)
            {
                samples[i * 2] = left;
                samples[i * 2 + 1] = right;
            }

            writer.WriteSamples(samples, 0, samples.Length);
        }

        return path;
    }

    /// <summary>Writes a mono 32-bit float WAV holding a sine tone.</summary>
    /// <param name="name">The file name to write inside this song's directory.</param>
    /// <param name="frequency">The tone's frequency in Hz.</param>
    /// <param name="frames">How many frames to write.</param>
    /// <param name="amplitude">The tone's peak amplitude.</param>
    /// <param name="sampleRate">The rate to declare in the header.</param>
    /// <returns>The full path to the file.</returns>
    public string WriteSineWav(string name, double frequency, int frames, float amplitude = 0.4f,
        int sampleRate = SampleRate)
    {
        var path = Path.Combine(Directory, name);
        using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1)))
        {
            var samples = new float[frames];
            for (var i = 0; i < frames; i++)
            {
                samples[i] = (float)(amplitude * Math.Sin(2.0 * Math.PI * frequency * i / sampleRate));
            }

            writer.WriteSamples(samples, 0, samples.Length);
        }

        return path;
    }

    /// <summary>Writes the Close Encounters motif as a stereo WAV, for the audible test.</summary>
    /// <param name="name">The file name to write inside this song's directory.</param>
    /// <returns>The full path to the file.</returns>
    public string WriteCloseEncountersWav(string name)
    {
        var path = Path.Combine(Directory, name);
        var samples = TestAudio.BuildCloseEncountersSamples(SampleRate, 2);
        using (var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2)))
        {
            writer.WriteSamples(samples, 0, samples.Length);
        }

        return path;
    }

    /// <summary>The shared synthetic SoundFont, loaded once for the whole test run.</summary>
    public static SoundFont SoundFont
    {
        get
        {
            lock (SoundFontGate)
            {
                sharedSoundFont ??= SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
                return sharedSoundFont;
            }
        }
    }

    /// <summary>A synthesizer factory over the shared synthetic SoundFont.</summary>
    /// <returns>A factory taking the render sample rate.</returns>
    public static Func<int, IMidiSynthesizer> SoundFontFactory() =>
        rate => new SoundFontSynthesizer(SoundFont, rate);

    /// <summary>
    /// Builds a sequence of notes, each written with the given length, at a steady tempo.
    /// </summary>
    /// <param name="notes">The note numbers to play, in order.</param>
    /// <param name="noteTicks">How long each note is written for, in ticks.</param>
    /// <param name="stepTicks">How far apart the note-ons are, in ticks.</param>
    /// <param name="velocity">The velocity every note is struck at.</param>
    /// <param name="channel">The MIDI channel, 1-16.</param>
    /// <param name="ticksPerQuarter">The resolution. At 120 BPM, 1000 makes one tick half a millisecond.</param>
    /// <returns>The sequence.</returns>
    public static MidiSequence BuildNoteSequence(
        IReadOnlyList<int> notes,
        int noteTicks,
        int stepTicks,
        int velocity = 100,
        int channel = 1,
        int ticksPerQuarter = 1000)
    {
        var events = new MidiEventCollection(1, ticksPerQuarter);

        var tick = 0L;
        foreach (var note in notes)
        {
            events.AddEvent(new NoteEvent(tick, channel, MidiCommandCode.NoteOn, note, velocity), 1);
            events.AddEvent(new NoteEvent(tick + noteTicks, channel, MidiCommandCode.NoteOff, note, 0), 1);
            tick += stepTicks;
        }

        events.PrepareForExport();
        return MidiSequence.FromEvents(events);
    }

    /// <summary>
    /// Builds a one-note sequence with a total length longer than the note, so a renderer has room
    /// to run past the note's end.
    /// </summary>
    /// <param name="note">The note number to play.</param>
    /// <param name="noteTicks">How long the note is written for, in ticks.</param>
    /// <param name="totalTicks">How long the sequence runs for, in ticks.</param>
    /// <param name="velocity">The velocity the note is struck at.</param>
    /// <param name="ticksPerQuarter">The resolution.</param>
    /// <returns>The sequence.</returns>
    public static MidiSequence BuildSingleNoteSequence(
        int note,
        int noteTicks,
        int totalTicks,
        int velocity = 100,
        int ticksPerQuarter = 1000)
    {
        var events = new MidiEventCollection(1, ticksPerQuarter);
        events.AddEvent(new NoteEvent(0, 1, MidiCommandCode.NoteOn, note, velocity), 1);
        events.AddEvent(new NoteEvent(noteTicks, 1, MidiCommandCode.NoteOff, note, 0), 1);

        // A controller message at the end simply extends the sequence, so its Length covers the
        // window the test wants to measure over.
        events.AddEvent(new ControlChangeEvent(totalTicks, 1, MidiController.Expression, 127), 1);
        events.PrepareForExport();
        return MidiSequence.FromEvents(events);
    }

    /// <summary>Builds a sequence carrying a tempo map, for the musical-time tests.</summary>
    /// <param name="tempoChanges">The tempo changes as (tick, beats per minute) pairs.</param>
    /// <param name="lengthTicks">How long the sequence runs for, in ticks.</param>
    /// <param name="ticksPerQuarter">The resolution.</param>
    /// <returns>The sequence.</returns>
    public static MidiSequence BuildTempoSequence(
        IReadOnlyList<(int Tick, double BeatsPerMinute)> tempoChanges,
        int lengthTicks,
        int ticksPerQuarter = 480)
    {
        var events = new MidiEventCollection(1, ticksPerQuarter);

        foreach (var change in tempoChanges)
        {
            events.AddEvent(new TempoEvent((int)Math.Round(60000000.0 / change.BeatsPerMinute), change.Tick), 1);
        }

        events.AddEvent(new NoteEvent(0, 1, MidiCommandCode.NoteOn, 60, 100), 1);
        events.AddEvent(new NoteEvent(lengthTicks, 1, MidiCommandCode.NoteOff, 60, 0), 1);
        events.PrepareForExport();
        return MidiSequence.FromEvents(events);
    }

    /// <summary>Deletes the song's directory.</summary>
    public void Dispose()
    {
        try
        {
            if (System.IO.Directory.Exists(Directory))
            {
                System.IO.Directory.Delete(Directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A file still held open by a reader that has not been collected yet; the temp
            // directory is the operating system's problem then.
        }
    }
}
