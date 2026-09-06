using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Text;
using CodeBrix.Audio.Playback.Suno;
using CodeBrix.Audio.Tests.Midi;

namespace CodeBrix.Audio.Tests.Playback.Suno;

/// <summary>
/// Which of the defects a real stems export can carry the generated fixture should carry. All off
/// by default, so a plain <c>new FakeSongOptions()</c> produces an export the loader reports nothing
/// about.
/// </summary>
internal sealed class FakeSongOptions
{
    /// <summary>Adds a "Kazoo" stem, whose name is outside the known vocabulary.</summary>
    internal bool IncludeUnknownStem { get; set; }

    /// <summary>Adds a "Brass" stem that has a MIDI file and no audio.</summary>
    internal bool IncludeMidiOnlyStem { get; set; }

    /// <summary>Adds a "Guitar" stem whose WAV is shorter than every other stem's.</summary>
    internal bool IncludeShortStem { get; set; }

    /// <summary>Adds a "Percussion" stem whose .mid file is not a MIDI file at all.</summary>
    internal bool IncludeUnreadableMidi { get; set; }

    /// <summary>Adds a "Strings" stem whose MIDI track name encodes a different song's title.</summary>
    internal bool IncludeForeignTrackName { get; set; }

    /// <summary>Adds a "Keyboard" stem that has an MP3 and no WAV.</summary>
    internal bool IncludeMp3OnlyStem { get; set; }

    /// <summary>Adds a file whose name does not follow the naming convention at all.</summary>
    internal bool IncludeStrayFile { get; set; }

    /// <summary>Writes the full-mix "&lt;Title&gt;.wav" beside the stems. On by default.</summary>
    internal bool IncludeFullMix { get; set; } = true;
}

/// <summary>
/// Builds a synthetic stems export - the same content as a folder and as a zip - carrying every
/// shape a real Suno export is known to have: emoji in the title, the mangled track name the
/// exporter writes for it, a key signature the MIDI specification does not allow, one tempo event
/// per beat, zero-length drum notes on channel 10 with a kit program, a MIDI stem with barely any
/// notes in it beside a full-length WAV, and one stem whose audio deliberately sits 40 ms behind its
/// MIDI. Nothing here comes from a real export; only the shape is copied.
/// </summary>
internal static class FakeSongStems
{
    /// <summary>The song title, with characters outside the Basic Multilingual Plane, as Suno titles have.</summary>
    internal const string Title = "\U0001F344\U0001F48E⛪ Fake Song";

    /// <summary>The name of the folder, and the stem of the name of the zip.</summary>
    internal const string ContainerName = Title + " Stems";

    /// <summary>The sample rate of every generated WAV.</summary>
    internal const int SampleRate = 8000;

    /// <summary>How long every stem is, except the deliberately short one.</summary>
    internal const double SongSeconds = 2.0;

    /// <summary>Ticks per quarter note in every generated MIDI file, as in a real export.</summary>
    internal const int Division = 480;

    /// <summary>The sharps/flats byte a real export writes, which the specification does not allow.</summary>
    internal const int KeySignatureSharpsFlats = 13;

    /// <summary>How far the "Bass" stem's audio deliberately sits behind its MIDI.</summary>
    internal static readonly TimeSpan PlantedBassOffset = TimeSpan.FromMilliseconds(40);

    /// <summary>
    /// The alignment window the loader chooses for this fixture: half an eighth note at the
    /// slowest tempo in its map.
    /// </summary>
    internal const double TempoAwareWindowSeconds = 0.13;

    /// <summary>
    /// The note-on times of the "Bass" stem's MIDI, in seconds. Eight of them, on the off-beat
    /// eighths, so that the stem clears the estimator's minimum note count and does not share a
    /// grid with the drums.
    /// </summary>
    internal static readonly double[] BassNoteOnSeconds =
        [0.125, 0.375, 0.62, 0.86, 1.11, 1.37, 1.625, 1.875];

    // The ticks those times come from: 120 + 240n, under the tempo map below.
    private static readonly int[] BassTicks = [120, 360, 600, 840, 1080, 1320, 1560, 1800];

    // One tempo event per beat, as "Follow tempo changes" produces. The four values below add up to
    // exactly two seconds over four beats, so the generated WAVs and MIDIs are the same length.
    private static readonly int[] TempoPerBeat = [500000, 480000, 520000, 500000];

    /// <summary>
    /// Writes the export as a folder under <paramref name="root"/>, plus the full mix beside it.
    /// </summary>
    /// <param name="root">The folder to write into.</param>
    /// <param name="options">Which defects to include; null for a clean export.</param>
    /// <returns>The path of the "&lt;Title&gt; Stems" folder.</returns>
    internal static string WriteFolder(string root, FakeSongOptions options = null)
    {
        var settings = options ?? new FakeSongOptions();
        var folder = Path.Combine(root, ContainerName);
        Directory.CreateDirectory(folder);

        foreach (var file in BuildFiles(settings))
        {
            File.WriteAllBytes(Path.Combine(folder, file.Name), file.Content);
        }

        WriteFullMix(root, settings);
        return folder;
    }

    /// <summary>
    /// Writes the export as a zip under <paramref name="root"/>, plus the full mix beside it. The
    /// entries are byte-for-byte what <see cref="WriteFolder"/> writes.
    /// </summary>
    /// <param name="root">The folder to write into.</param>
    /// <param name="options">Which defects to include; null for a clean export.</param>
    /// <returns>The path of the "&lt;Title&gt; Stems.zip" file.</returns>
    internal static string WriteZip(string root, FakeSongOptions options = null)
    {
        var settings = options ?? new FakeSongOptions();
        Directory.CreateDirectory(root);
        var zipPath = Path.Combine(root, ContainerName + ".zip");

        using (var stream = File.Create(zipPath))
        using (var archive = new ZipArchive(stream, ZipArchiveMode.Create))
        {
            foreach (var file in BuildFiles(settings))
            {
                var entry = archive.CreateEntry(file.Name, CompressionLevel.Fastest);
                using var target = entry.Open();
                target.Write(file.Content, 0, file.Content.Length);
            }
        }

        WriteFullMix(root, settings);
        return zipPath;
    }

    /// <summary>The name of one of the export's files, "&lt;Title&gt; (&lt;Stem&gt;).&lt;ext&gt;".</summary>
    /// <param name="stemName">The stem's name.</param>
    /// <param name="extension">The extension, including its dot.</param>
    /// <returns>The file name.</returns>
    internal static string FileNameFor(string stemName, string extension) =>
        $"{Title} ({stemName}){extension}";

    /// <summary>Every file of the export, in a fixed order.</summary>
    /// <param name="options">Which defects to include.</param>
    /// <returns>The file names and their bytes.</returns>
    internal static IReadOnlyList<(string Name, byte[] Content)> BuildFiles(FakeSongOptions options)
    {
        var files = new List<(string, byte[])>();

        // Vocals: an ordinary melodic stem, with an MP3 beside its WAV.
        files.Add((FileNameFor("Vocals", ".wav"), BuildWav(SongSeconds, [0.0, 0.5, 0.98, 1.5])));
        files.Add((FileNameFor("Vocals", ".mp3"), TestAudio.BuildSilentMp3(77)));
        files.Add((FileNameFor("Vocals", ".mid"),
            BuildMidi("Vocals", 54, 1, Quarters(240), Title)));

        // Drums: channel 10, a kit number for a program, and every hit written as a note-on with a
        // note-off one tick later.
        files.Add((FileNameFor("Drums", ".wav"), BuildWav(SongSeconds, [0.0, 0.24, 0.5, 0.74, 0.98, 1.24, 1.5, 1.74])));
        files.Add((FileNameFor("Drums", ".mid"), BuildMidi("Drums", 118, 10, ZeroLengthHits(), Title)));

        // Bass: its audio deliberately sits 40 ms behind its MIDI.
        var bassClicks = new double[BassNoteOnSeconds.Length];
        for (var i = 0; i < bassClicks.Length; i++)
        {
            bassClicks[i] = BassNoteOnSeconds[i] + PlantedBassOffset.TotalSeconds;
        }

        files.Add((FileNameFor("Bass", ".wav"), BuildWav(SongSeconds, bassClicks)));
        files.Add((FileNameFor("Bass", ".mid"), BuildMidi("Bass", 32, 1, BassNotes(), Title)));

        // Synth: two notes beside a full-length WAV, which is a real and common shape.
        files.Add((FileNameFor("Synth", ".wav"), BuildWav(SongSeconds, [0.0, 1.0])));
        files.Add((FileNameFor("Synth", ".mid"),
            BuildMidi("Synth", 80, 1, [(0, 120, 60, 90), (240, 120, 62, 90)], Title)));

        // FX: audio only, no MIDI at all.
        files.Add((FileNameFor("FX", ".wav"), BuildWav(SongSeconds, [0.7])));

        if (options.IncludeUnknownStem)
        {
            files.Add((FileNameFor("Kazoo", ".wav"), BuildWav(SongSeconds, [0.3])));
        }

        if (options.IncludeMidiOnlyStem)
        {
            files.Add((FileNameFor("Brass", ".mid"), BuildMidi("Brass", 61, 1, Quarters(240), Title)));
        }

        if (options.IncludeShortStem)
        {
            files.Add((FileNameFor("Guitar", ".wav"), BuildWav(1.5, [0.1])));
        }

        if (options.IncludeUnreadableMidi)
        {
            files.Add((FileNameFor("Percussion", ".wav"), BuildWav(SongSeconds, [0.2])));
            files.Add((FileNameFor("Percussion", ".mid"),
                Encoding.ASCII.GetBytes("this is not a Standard MIDI File")));
        }

        if (options.IncludeForeignTrackName)
        {
            files.Add((FileNameFor("Strings", ".wav"), BuildWav(SongSeconds, [0.4])));
            files.Add((FileNameFor("Strings", ".mid"),
                BuildMidi("Strings", 48, 1, Quarters(240), "Somebody Else's Song")));
        }

        if (options.IncludeMp3OnlyStem)
        {
            files.Add((FileNameFor("Keyboard", ".mp3"), TestAudio.BuildSilentMp3(77)));
        }

        if (options.IncludeStrayFile)
        {
            files.Add(("read me first.wav", BuildWav(0.1, [])));
        }

        return files;
    }

    private static void WriteFullMix(string root, FakeSongOptions options)
    {
        if (options.IncludeFullMix)
        {
            File.WriteAllBytes(Path.Combine(root, Title + ".wav"), BuildWav(SongSeconds, [0.0, 0.5, 1.0, 1.5]));
        }
    }

    private static (int Tick, int Length, int Note, int Velocity)[] Quarters(int length) =>
    [
        (0, length, 60, 90),
        (480, length, 62, 88),
        (960, length, 64, 92),
        (1440, length, 65, 86),
    ];

    private static (int Tick, int Length, int Note, int Velocity)[] BassNotes()
    {
        var notes = new (int, int, int, int)[BassTicks.Length];
        for (var i = 0; i < notes.Length; i++)
        {
            notes[i] = (BassTicks[i], 120, 36 + (i % 3), 90);
        }

        return notes;
    }

    private static (int Tick, int Length, int Note, int Velocity)[] ZeroLengthHits()
    {
        var hits = new (int, int, int, int)[8];
        for (var i = 0; i < hits.Length; i++)
        {
            hits[i] = (i * 240, 1, i % 2 == 0 ? 36 : 38, 100);
        }

        return hits;
    }

    /// <summary>
    /// One stem's MIDI, shaped exactly as an export writes it: a first track carrying the mangled
    /// track name, the out-of-range key signature and one tempo event per beat, and a second track
    /// carrying the notes on one channel with one program change and one CC7.
    /// </summary>
    private static byte[] BuildMidi(string stemName, int program, int channel,
        (int Tick, int Length, int Note, int Velocity)[] notes, string titleInTrackName)
    {
        var trackName = SunoTitleCodec.Encode($"{titleInTrackName} ({stemName})");

        var conductor = new MidiTrackBuilder()
            .TrackName(0, trackName)
            .KeySignature(0, KeySignatureSharpsFlats, 0);

        for (var beat = 0; beat < TempoPerBeat.Length; beat++)
        {
            conductor.Tempo(beat == 0 ? 0 : Division, TempoPerBeat[beat]);
        }

        conductor.EndOfTrack(Division);

        var track = new MidiTrackBuilder()
            .TrackName(0, trackName)
            .ProgramChange(0, channel, program)
            .ControlChange(0, channel, 7, 100);

        var tick = 0;
        var events = new List<(int Tick, bool On, int Note, int Velocity)>();
        foreach (var note in notes)
        {
            events.Add((note.Tick, true, note.Note, note.Velocity));
            events.Add((note.Tick + note.Length, false, note.Note, 0));
        }

        events.Sort((left, right) => left.Tick != right.Tick
            ? left.Tick.CompareTo(right.Tick)
            : left.On.CompareTo(right.On));

        foreach (var midiEvent in events)
        {
            var delta = midiEvent.Tick - tick;
            tick = midiEvent.Tick;
            if (midiEvent.On)
            {
                track.NoteOn(delta, channel, midiEvent.Note, midiEvent.Velocity);
            }
            else
            {
                track.NoteOff(delta, channel, midiEvent.Note, midiEvent.Velocity);
            }
        }

        track.EndOfTrack(Math.Max(0, (Division * TempoPerBeat.Length) - tick));
        return SyntheticMidi.File(1, Division, conductor.ToChunk(), track.ToChunk());
    }

    /// <summary>
    /// A 16-bit mono WAV of the given length, silent except for a short click at each of the given
    /// times, so that an alignment estimator has something to lock on to.
    /// </summary>
    private static byte[] BuildWav(double seconds, double[] clickTimesSeconds)
    {
        var frames = (int)Math.Round(seconds * SampleRate);
        var samples = new short[frames];

        foreach (var time in clickTimesSeconds)
        {
            var start = (int)Math.Round(time * SampleRate);
            for (var i = 0; i < 40 && start + i < frames; i++)
            {
                samples[start + i] = (short)(i % 2 == 0 ? 12000 : -12000);
            }
        }

        var dataBytes = samples.Length * 2;
        using var stream = new MemoryStream(44 + dataBytes);
        using var writer = new BinaryWriter(stream, Encoding.ASCII, true);

        writer.Write("RIFF"u8);
        writer.Write(36 + dataBytes);
        writer.Write("WAVE"u8);
        writer.Write("fmt "u8);
        writer.Write(16);
        writer.Write((short)1);              // PCM
        writer.Write((short)1);              // mono
        writer.Write(SampleRate);
        writer.Write(SampleRate * 2);        // bytes per second
        writer.Write((short)2);              // block align
        writer.Write((short)16);             // bits per sample
        writer.Write("data"u8);
        writer.Write(dataBytes);
        foreach (var sample in samples)
        {
            writer.Write(sample);
        }

        writer.Flush();
        return stream.ToArray();
    }
}

/// <summary>
/// A folder under the system temporary path that deletes itself. The fixture writer needs one and
/// so does every test that loads what it wrote.
/// </summary>
internal sealed class TemporaryFolder : IDisposable
{
    internal TemporaryFolder()
    {
        Path = System.IO.Path.Combine(System.IO.Path.GetTempPath(),
            "CodeBrix.Audio.Tests", System.IO.Path.GetRandomFileName());
        Directory.CreateDirectory(Path);
    }

    /// <summary>The folder's full path.</summary>
    internal string Path { get; }

    /// <summary>Deletes the folder and everything in it.</summary>
    public void Dispose()
    {
        try
        {
            if (Directory.Exists(Path))
            {
                Directory.Delete(Path, true);
            }
        }
        catch (IOException)
        {
            // A test that leaves a file open should not fail because of the clean-up.
        }
    }
}
