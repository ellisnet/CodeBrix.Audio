using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Playback.Suno.Internal;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Playback.Suno;

/// <summary>
/// Loads a Suno stems export - a "&lt;Title&gt; Stems.zip" straight from Suno, or the same files
/// extracted to a folder - into a <see cref="SunoSong"/>.
/// </summary>
/// <remarks>
/// <para>
/// The export to ask Suno for is "Extract Stems and MIDI" with split mode "Auto split", range "Full
/// Song", tempo "Follow tempo changes", and the WAV and MIDI download options ticked. Get the WAV
/// files: they align exactly with the MIDI, where an MP3 carries an encoder delay that nothing here
/// compensates for yet.
/// </para>
/// <para>
/// Loading is cheap and reads no audio. It lists the files, reads the MIDI stems (which are a few
/// kilobytes each), and reads the header of each WAV - for a zip, only as many bytes of each entry
/// as the header occupies. A stem's audio is decompressed the first time something asks for it, and
/// then reused: see <see cref="SunoZipExtraction"/> and <see cref="SunoSong.CacheFolder"/>.
/// </para>
/// <para>
/// Nothing here throws over the content of an export. A file that will not read, a stem of the wrong
/// length, a name nobody recognises - each leaves a line in <see cref="SunoSong.Problems"/> and the
/// rest of the song loads. Only a path that is not there, or a zip that is not a zip, throws.
/// </para>
/// </remarks>
public static class SunoStemsLoader
{
    private const string StemsSuffix = " Stems";

    private static readonly string[] AudioAndMidiExtensions = [".wav", ".mp3", ".mid", ".midi"];

    /// <summary>
    /// Where extracted zip entries go when the load options name no folder of their own: a
    /// "CodeBrix.Audio/SunoStems" folder under the system temporary path, holding one folder per
    /// song.
    /// </summary>
    public static string DefaultCacheRoot { get; } =
        Path.Combine(Path.GetTempPath(), "CodeBrix.Audio", "SunoStems");

    /// <summary>
    /// Loads a stems export with the default options.
    /// </summary>
    /// <param name="path">A "&lt;Title&gt; Stems.zip", or a folder holding the extracted files.</param>
    /// <returns>The loaded song.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or blank.</exception>
    /// <exception cref="FileNotFoundException">There is no file or folder at <paramref name="path"/>.</exception>
    /// <exception cref="InvalidDataException">The file is not a readable zip archive.</exception>
    public static SunoSong Load(string path) => Load(path, null);

    /// <summary>
    /// Loads a stems export.
    /// </summary>
    /// <param name="path">A "&lt;Title&gt; Stems.zip", or a folder holding the extracted files.</param>
    /// <param name="options">What to do beyond reading the files; null for the defaults.</param>
    /// <returns>The loaded song.</returns>
    /// <exception cref="ArgumentException"><paramref name="path"/> is null or blank.</exception>
    /// <exception cref="FileNotFoundException">There is no file or folder at <paramref name="path"/>.</exception>
    /// <exception cref="InvalidDataException">The file is not a readable zip archive.</exception>
    /// <remarks>
    /// Measuring alignment happens on worker threads, so this call blocks until it is finished. Use
    /// <see cref="LoadAsync(string, SunoLoadOptions, CancellationToken)"/> from a user interface.
    /// </remarks>
    public static SunoSong Load(string path, SunoLoadOptions options) =>
        LoadCore(path, options, CancellationToken.None);

    /// <summary>
    /// Loads a stems export on a worker thread, with the default options.
    /// </summary>
    /// <param name="path">A "&lt;Title&gt; Stems.zip", or a folder holding the extracted files.</param>
    /// <param name="cancellationToken">Cancels the load between stems.</param>
    /// <returns>The loaded song.</returns>
    public static Task<SunoSong> LoadAsync(string path, CancellationToken cancellationToken = default) =>
        LoadAsync(path, null, cancellationToken);

    /// <summary>
    /// Loads a stems export on a worker thread.
    /// </summary>
    /// <param name="path">A "&lt;Title&gt; Stems.zip", or a folder holding the extracted files.</param>
    /// <param name="options">What to do beyond reading the files; null for the defaults.</param>
    /// <param name="cancellationToken">Cancels the load between stems.</param>
    /// <returns>The loaded song.</returns>
    public static Task<SunoSong> LoadAsync(string path, SunoLoadOptions options,
        CancellationToken cancellationToken = default) =>
        Task.Run(() => LoadCore(path, options, cancellationToken), cancellationToken);

    /// <summary>
    /// Deletes every song folder under <see cref="DefaultCacheRoot"/>. Songs loaded from a zip
    /// extract their entries again the next time something asks for them; a song loaded from a
    /// folder, or with a cache folder of its own, is unaffected.
    /// </summary>
    public static void ClearCache()
    {
        if (Directory.Exists(DefaultCacheRoot))
        {
            Directory.Delete(DefaultCacheRoot, true);
        }
    }

    private static SunoSong LoadCore(string path, SunoLoadOptions options, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            throw new ArgumentException("A path to a stems zip or folder is required.", nameof(path));
        }

        var settings = (options ?? new SunoLoadOptions()).Clone();
        var fullPath = Path.GetFullPath(path);

        SunoSong song = Directory.Exists(fullPath)
            ? BuildFromFolder(fullPath, settings)
            : BuildFromZip(fullPath, settings);

        cancellationToken.ThrowIfCancellationRequested();
        MeasureAlignment(song, settings, cancellationToken);
        return song;
    }

    // ---------------------------------------------------------------------------------------
    // Discovery
    // ---------------------------------------------------------------------------------------

    private static SunoSong BuildFromFolder(string folder, SunoLoadOptions options)
    {
        var files = Directory.EnumerateFiles(folder)
            .Where(file => HasKnownExtension(file))
            .OrderBy(file => file, StringComparer.Ordinal)
            .ToList();

        var names = files.Select(Path.GetFileName).ToList();
        var title = ChooseTitle(names, ContainerTitle(Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar))));

        var store = new SunoFolderStore(folder, files);
        return Build(store, folder, title, options);
    }

    private static SunoSong BuildFromZip(string zipPath, SunoLoadOptions options)
    {
        if (!File.Exists(zipPath))
        {
            throw new FileNotFoundException(
                $"There is no stems zip or folder at '{zipPath}'.", zipPath);
        }

        SunoZipStore store;
        string title;

        using (var archive = ZipFile.OpenRead(zipPath))
        {
            var entries = archive.Entries
                .Where(entry => entry.Name.Length > 0 && HasKnownExtension(entry.Name))
                .OrderBy(entry => entry.FullName, StringComparer.Ordinal)
                .ToList();

            title = ChooseTitle(entries.Select(entry => entry.Name).ToList(),
                ContainerTitle(Path.GetFileNameWithoutExtension(zipPath)));

            var cacheFolder = options.ZipExtraction == SunoZipExtraction.CacheFolder
                ? options.CacheFolder ?? SunoZipStore.CacheFolderFor(zipPath, DefaultCacheRoot, title)
                : null;

            store = new SunoZipStore(zipPath, entries, options.ZipExtraction, cacheFolder);
        }

        return Build(store, zipPath, title, options);
    }

    private static bool HasKnownExtension(string fileName)
    {
        var extension = Path.GetExtension(fileName);
        foreach (var known in AudioAndMidiExtensions)
        {
            if (string.Equals(extension, known, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string ContainerTitle(string containerName)
    {
        if (string.IsNullOrEmpty(containerName))
        {
            return null;
        }

        return containerName.EndsWith(StemsSuffix, StringComparison.OrdinalIgnoreCase)
            ? containerName.Substring(0, containerName.Length - StemsSuffix.Length).TrimEnd()
            : containerName;
    }

    /// <summary>
    /// The title is whatever the "&lt;Title&gt; (&lt;Stem&gt;)" file names agree on. The container's
    /// own name only decides a tie, or steps in when nothing follows the convention at all.
    /// </summary>
    private static string ChooseTitle(IReadOnlyList<string> fileNames, string containerTitle)
    {
        var counts = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var fileName in fileNames)
        {
            if (TryParseStemFileName(fileName, out var title, out _))
            {
                counts.TryGetValue(title, out var count);
                counts[title] = count + 1;
            }
        }

        if (counts.Count == 0)
        {
            return containerTitle ?? string.Empty;
        }

        if (containerTitle != null && counts.ContainsKey(containerTitle))
        {
            return containerTitle;
        }

        return counts
            .OrderByDescending(pair => pair.Value)
            .ThenBy(pair => pair.Key, StringComparer.Ordinal)
            .First().Key;
    }

    /// <summary>
    /// Splits "&lt;Title&gt; (&lt;Stem&gt;).&lt;ext&gt;" into its two halves.
    /// </summary>
    private static bool TryParseStemFileName(string fileName, out string title, out string stemName)
    {
        title = null;
        stemName = null;

        var baseName = Path.GetFileNameWithoutExtension(fileName);
        if (baseName.Length < 4 || baseName[baseName.Length - 1] != ')')
        {
            return false;
        }

        var open = baseName.LastIndexOf('(');
        if (open <= 1 || baseName[open - 1] != ' ')
        {
            return false;
        }

        var stem = baseName.Substring(open + 1, baseName.Length - open - 2).Trim();
        if (stem.Length == 0 || stem.IndexOf('(') >= 0 || stem.IndexOf(')') >= 0)
        {
            return false;
        }

        title = baseName.Substring(0, open - 1);
        stemName = stem;
        return title.Length > 0;
    }

    // ---------------------------------------------------------------------------------------
    // Model building and validation
    // ---------------------------------------------------------------------------------------

    private sealed class StemFiles
    {
        internal string Name;
        internal string Wav;
        internal string Mp3;
        internal string Midi;
    }

    private static SunoSong Build(SunoContentStore store, string sourcePath, string title, SunoLoadOptions options)
    {
        var song = new SunoSong(store, sourcePath, title, options);
        var grouped = new Dictionary<string, StemFiles>(StringComparer.OrdinalIgnoreCase);
        var ignored = new List<string>();
        var otherTitles = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var key in store.Keys)
        {
            var fileName = Path.GetFileName(key);
            if (!TryParseStemFileName(fileName, out var fileTitle, out var stemName))
            {
                // The full mix, "<Title>.wav", lives beside the stems and is not one of them.
                if (!string.Equals(Path.GetFileNameWithoutExtension(fileName), title, StringComparison.Ordinal))
                {
                    ignored.Add(fileName);
                }

                continue;
            }

            if (!string.Equals(fileTitle, title, StringComparison.Ordinal))
            {
                otherTitles.Add(fileTitle);
                continue;
            }

            if (!grouped.TryGetValue(stemName, out var files))
            {
                files = new StemFiles { Name = stemName };
                grouped[stemName] = files;
            }

            switch (Path.GetExtension(fileName).ToLowerInvariant())
            {
                case ".wav":
                    files.Wav = key;
                    break;
                case ".mp3":
                    files.Mp3 = key;
                    break;
                default:
                    files.Midi = key;
                    break;
            }
        }

        var stems = grouped.Values
            .OrderBy(files => SunoStemDefaults.SortIndex(files.Name))
            .ThenBy(files => files.Name, StringComparer.OrdinalIgnoreCase)
            .Select(files => new SunoStem(song, files.Name, files.Wav, files.Mp3, files.Midi))
            .ToArray();

        var sounding = new TimeSpan[stems.Length];
        for (var i = 0; i < stems.Length; i++)
        {
            sounding[i] = LoadStem(store, stems[i], title, options);
        }

        song.SetStems(stems);
        song.Duration = MeasureDuration(stems);

        for (var i = 0; i < stems.Length; i++)
        {
            stems[i].MidiCoverage = song.Duration > TimeSpan.Zero
                ? Math.Min(1.0, sounding[i].TotalSeconds / song.Duration.TotalSeconds)
                : 0.0;
        }

        song.TempoMap = ReadTempoMap(stems, song);

        if (options.FindFullMix)
        {
            FindFullMix(song, store, sourcePath, title);
        }

        Validate(song, stems, ignored, otherTitles, options);
        return song;
    }

    /// <summary>
    /// Reads everything one stem's files can say without decoding audio, and returns how long its
    /// MIDI has a note sounding (the song's duration is not known yet, so coverage is not either).
    /// </summary>
    private static TimeSpan LoadStem(SunoContentStore store, SunoStem stem, string title, SunoLoadOptions options)
    {
        if (stem.WavKey != null)
        {
            SunoWavInfo info;
            using (var header = store.OpenSequential(stem.WavKey))
            {
                info = SunoWavProbe.Read(header, store.GetLength(stem.WavKey));
            }

            if (info.IsValid)
            {
                stem.Duration = info.Duration;
                stem.AudioSampleRate = info.SampleRate;
                stem.AudioChannels = info.Channels;
            }
            else
            {
                stem.AddProblem($"the WAV file '{stem.WavFileName}' does not have a readable WAV header.");
            }
        }
        else if (stem.Mp3Key != null)
        {
            try
            {
                using var stream = store.OpenSeekable(stem.Mp3Key);
                using var reader = new Mp3FileReader(stream);
                stem.Duration = reader.TotalTime;
                stem.AudioSampleRate = reader.WaveFormat.SampleRate;
                stem.AudioChannels = reader.WaveFormat.Channels;
            }
            catch (Exception ex)
            {
                stem.AddProblem($"the MP3 file '{stem.Mp3FileName}' could not be read ({ex.Message}).");
            }
        }

        if (stem.MidiKey == null)
        {
            return TimeSpan.Zero;
        }

        var midiBytes = store.ReadAllBytes(stem.MidiKey);

        MidiSequence sequence;
        try
        {
            using var stream = new MemoryStream(midiBytes, false);
            sequence = new MidiSequence(stream, MidiReadMode.Tolerant);
        }
        catch (Exception ex)
        {
            stem.AddProblem($"the MIDI file '{stem.MidiFileName}' could not be read ({ex.Message}).");
            return TimeSpan.Zero;
        }

        stem.Midi = sequence;
        stem.TempoMap = SunoMidiAnalysis.ReadTempoMap(midiBytes);
        foreach (var problem in sequence.Problems)
        {
            stem.AddProblem($"in '{stem.MidiFileName}': {problem}");
        }

        CheckTrackName(stem, sequence, title);

        var analysis = SunoMidiAnalysis.Analyse(sequence, options.MinimumNoteHold);
        stem.NoteCount = analysis.NoteCount;
        stem.NoteOnTimes = analysis.NoteOnTimes;

        if (analysis.Program >= 0)
        {
            stem.GmProgram = analysis.Program;
        }

        if (analysis.Channel > 0)
        {
            stem.Channel = analysis.Channel;
            stem.IsPercussion = analysis.Channel == 10 || stem.IsPercussion;
        }

        return analysis.SoundingTime;
    }

    private static void CheckTrackName(SunoStem stem, MidiSequence sequence, string title)
    {
        var found = false;
        var matched = false;

        foreach (var meta in sequence.TextMetas)
        {
            if (meta.Kind != MetaEventType.SequenceTrackName)
            {
                continue;
            }

            found = true;
            if (SunoTitleCodec.StartsWith(meta.Data, title))
            {
                matched = true;
                break;
            }
        }

        if (found && !matched)
        {
            stem.AddProblem(
                $"the track name in '{stem.MidiFileName}' does not belong to the song '{title}'; " +
                "the file names are the authority on the title, so the MIDI was kept.");
        }
    }

    private static TimeSpan MeasureDuration(SunoStem[] stems)
    {
        var longest = TimeSpan.Zero;
        foreach (var stem in stems)
        {
            if (stem.Duration > longest)
            {
                longest = stem.Duration;
            }
        }

        if (longest > TimeSpan.Zero)
        {
            return longest;
        }

        foreach (var stem in stems)
        {
            if (stem.Midi != null && stem.Midi.Length > longest)
            {
                longest = stem.Midi.Length;
            }
        }

        return longest;
    }

    private static SunoTempoChange[] ReadTempoMap(SunoStem[] stems, SunoSong song)
    {
        SunoTempoChange[] map = null;
        string mapSource = null;

        foreach (var stem in stems)
        {
            if (stem.Midi == null)
            {
                continue;
            }

            var candidate = stem.TempoMap;
            if (map == null)
            {
                if (candidate.Length > 0)
                {
                    map = candidate;
                    mapSource = stem.Name;
                }

                continue;
            }

            if (candidate.Length > 0 && !SameMap(map, candidate))
            {
                song.AddProblem(
                    $"Stem '{stem.Name}' carries a different tempo map from stem '{mapSource}'; " +
                    $"the map from '{mapSource}' was used.");
            }
        }

        return map ?? [];
    }

    private static bool SameMap(SunoTempoChange[] left, SunoTempoChange[] right)
    {
        if (left.Length != right.Length)
        {
            return false;
        }

        for (var i = 0; i < left.Length; i++)
        {
            if (Math.Abs((left[i].Time - right[i].Time).TotalMilliseconds) > 1.0 ||
                Math.Abs(left[i].BeatsPerMinute - right[i].BeatsPerMinute) > 1e-6)
            {
                return false;
            }
        }

        return true;
    }

    private static void FindFullMix(SunoSong song, SunoContentStore store, string sourcePath, string title)
    {
        if (string.IsNullOrEmpty(title))
        {
            return;
        }

        var folders = new List<string>(2);
        if (store.Kind == SunoSourceKind.Folder)
        {
            folders.Add(sourcePath);
        }

        var parent = Path.GetDirectoryName(sourcePath.TrimEnd(Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar));
        if (!string.IsNullOrEmpty(parent))
        {
            folders.Add(parent);
        }

        foreach (var folder in folders)
        {
            if (song.FullMixWavPath == null)
            {
                var wav = Path.Combine(folder, title + ".wav");
                if (File.Exists(wav))
                {
                    song.FullMixWavPath = wav;
                }
            }

            if (song.FullMixMp3Path == null)
            {
                var mp3 = Path.Combine(folder, title + ".mp3");
                if (File.Exists(mp3))
                {
                    song.FullMixMp3Path = mp3;
                }
            }
        }
    }

    private static void Validate(SunoSong song, SunoStem[] stems, List<string> ignored,
        SortedSet<string> otherTitles, SunoLoadOptions options)
    {
        if (stems.Length == 0)
        {
            song.AddProblem(
                $"No files following the '<Title> (<Stem>).<ext>' convention were found in '{song.SourcePath}'.");
        }

        var unknown = new List<string>();
        var withoutWav = new List<string>();

        foreach (var stem in stems)
        {
            foreach (var problem in stem.Problems)
            {
                song.AddProblem($"Stem '{stem.Name}': {problem}");
            }

            if (!stem.IsKnownName)
            {
                unknown.Add(stem.Name);
            }

            if (!stem.HasAudio)
            {
                song.AddProblem(
                    $"Stem '{stem.Name}' has a MIDI file but no audio file; it can only be played through an instrument.");
            }
            else if (!stem.HasWav)
            {
                withoutWav.Add(stem.Name);
            }

            if (stem.HasAudio && song.Duration > TimeSpan.Zero &&
                (song.Duration - stem.Duration).Duration() > options.StemLengthTolerance)
            {
                song.AddProblem(
                    $"Stem '{stem.Name}' is {Format(stem.Duration)} long; the song is {Format(song.Duration)}. " +
                    "The stems of an export are all the same length, so one of them is not from this song.");
            }
        }

        if (unknown.Count > 0)
        {
            song.AddProblem(
                $"Unrecognised stem name(s): {Quote(unknown)}. They load and play, but no General MIDI " +
                "program or channel was assumed for them.");
        }

        if (withoutWav.Count > 0)
        {
            song.AddProblem(
                $"No WAV was downloaded for {Quote(withoutWav)}; the MP3 will be used instead. MP3 carries " +
                "an encoder delay that the WAV does not, so those stems may sit a few milliseconds late.");
        }

        if (otherTitles.Count > 0)
        {
            song.AddProblem(
                $"Files belonging to another title were ignored: {Quote(otherTitles.ToList())}.");
        }

        if (ignored.Count > 0)
        {
            song.AddProblem(
                $"{ignored.Count} file(s) did not follow the '<Title> (<Stem>).<ext>' convention and were " +
                $"ignored: {Quote(ignored)}.");
        }
    }

    private static string Format(TimeSpan value) => value.ToString(@"m\:ss\.fff");

    private static string Quote(List<string> names)
    {
        const int maximum = 8;
        var shown = names.Count <= maximum ? names : names.GetRange(0, maximum);
        var text = string.Join(", ", shown.Select(name => $"'{name}'"));
        return names.Count > maximum ? $"{text} and {names.Count - maximum} more" : text;
    }

    // ---------------------------------------------------------------------------------------
    // Alignment
    // ---------------------------------------------------------------------------------------

    /// <summary>
    /// The narrowest tempo-aware alignment window. Below this the search stops being able to find
    /// the offsets real exports actually carry.
    /// </summary>
    private static readonly TimeSpan MinimumTempoAwareWindow = TimeSpan.FromMilliseconds(80);

    // The shortest rhythmic subdivision the window is protected against, as a fraction of a
    // quarter note. An eighth note: nothing shorter has ever been the aliasing distance on a real
    // export, and half of one is still wide enough for every measured offset.
    private const double SubdivisionOfAQuarterNote = 0.5;

    private static void MeasureAlignment(SunoSong song, SunoLoadOptions options,
        CancellationToken cancellationToken)
    {
        var estimator = SunoAlignmentSeam.Estimator;
        if (!options.MeasureAlignment || estimator == null)
        {
            return;
        }

        var candidates = song.Stems
            .Where(stem => stem.Midi != null && stem.NoteOnTimes.Count > 0 && stem.HasAudio)
            .ToArray();

        if (candidates.Length == 0)
        {
            ApplyFallbackOffsets(song);
            return;
        }

        // Decoding a four-minute stem to mono costs about 40 MB, so a handful at a time is as much
        // parallelism as this is worth.
        var parallel = new ParallelOptions
        {
            CancellationToken = cancellationToken,
            MaxDegreeOfParallelism = Math.Max(1, Math.Min(Environment.ProcessorCount, 4)),
        };

        var failures = new string[candidates.Length];

        Parallel.For(0, candidates.Length, parallel, index =>
        {
            var stem = candidates[index];
            try
            {
                var mono = stem.ReadMonoAudio(out var sampleRate);
                if (mono.Length == 0 || sampleRate <= 0)
                {
                    failures[index] = "its audio decoded to nothing.";
                    return;
                }

                var window = WindowFor(song, stem, options);
                stem.AlignmentWindow = window;

                var estimate = estimator(stem.NoteOnTimes, mono, sampleRate, window.TotalSeconds);
                stem.MeasuredAlignmentOffset = TimeSpan.FromSeconds(estimate.OffsetSeconds);
                stem.AlignmentOffset = stem.MeasuredAlignmentOffset;
                stem.AlignmentConfidence = estimate.Confidence;
                stem.AlignmentIsReliable = estimate.IsReliable;
                stem.AlignmentMeasured = true;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                failures[index] = $"the alignment could not be measured ({ex.Message}).";
            }
        });

        // Problems are gathered here rather than inside the loop: the lists are not thread-safe, and
        // a song's problems should read in stem order however the work was scheduled.
        for (var index = 0; index < candidates.Length; index++)
        {
            if (failures[index] != null)
            {
                candidates[index].AddProblem(failures[index]);
                song.AddProblem($"Stem '{candidates[index].Name}': {failures[index]}");
            }
        }

        ApplyFallbackOffsets(song);
    }

    /// <summary>
    /// How far the estimator may look for this stem. The load options' maximum, unless a tempo map
    /// says a narrower window is enough - which is the only thing that stops the search from
    /// answering with a beat multiple on a repeating pattern.
    /// </summary>
    /// <param name="song">The song being loaded, for its tempo map.</param>
    /// <param name="stem">The stem being measured, for its own tempo map when it has one.</param>
    /// <param name="options">The load options.</param>
    /// <returns>The half-width of the search.</returns>
    private static TimeSpan WindowFor(SunoSong song, SunoStem stem, SunoLoadOptions options)
    {
        var maximum = options.MaximumAlignmentOffset;
        if (!options.TempoAwareAlignmentWindow)
        {
            return maximum;
        }

        IReadOnlyList<SunoTempoChange> tempoMap =
            stem.TempoMap != null && stem.TempoMap.Length > 0 ? stem.TempoMap : song.TempoMap;
        if (tempoMap == null || tempoMap.Count == 0)
        {
            return maximum;
        }

        var slowest = double.MaxValue;
        foreach (var change in tempoMap)
        {
            if (change.BeatsPerMinute > 0.0 && change.BeatsPerMinute < slowest)
            {
                slowest = change.BeatsPerMinute;
            }
        }

        if (slowest <= 0.0 || double.IsInfinity(slowest) || slowest == double.MaxValue)
        {
            return maximum;
        }

        return TempoAwareWindow(slowest, maximum);
    }

    /// <summary>
    /// Half of the shortest rhythmic subdivision, at the tempo where that subdivision is longest,
    /// clamped to the load options' maximum and to a floor of 80 ms.
    /// </summary>
    /// <param name="slowestBeatsPerMinute">The lowest tempo anywhere in the song.</param>
    /// <param name="maximum">The load options' <see cref="SunoLoadOptions.MaximumAlignmentOffset"/>.</param>
    /// <returns>The half-width of the search.</returns>
    internal static TimeSpan TempoAwareWindow(double slowestBeatsPerMinute, TimeSpan maximum)
    {
        var subdivisionSeconds = 60.0 / slowestBeatsPerMinute * SubdivisionOfAQuarterNote;
        var window = TimeSpan.FromSeconds(subdivisionSeconds / 2.0);

        var floor = MinimumTempoAwareWindow < maximum ? MinimumTempoAwareWindow : maximum;
        if (window > maximum) { window = maximum; }
        if (window < floor) { window = floor; }
        return window;
    }

    /// <summary>
    /// Gives every MIDI stem that has no answer of its own the song's answer: the median offset of
    /// the stems whose measurement the estimator stood behind, or zero when there were none.
    /// </summary>
    /// <param name="song">The song whose stems to settle.</param>
    private static void ApplyFallbackOffsets(SunoSong song)
    {
        var reliable = new List<double>();
        foreach (var stem in song.Stems)
        {
            if (stem.AlignmentMeasured && stem.AlignmentIsReliable)
            {
                reliable.Add(stem.MeasuredAlignmentOffset.TotalSeconds);
            }
        }

        var fallback = TimeSpan.Zero;
        if (reliable.Count > 0)
        {
            reliable.Sort();
            var middle = reliable.Count / 2;
            fallback = TimeSpan.FromSeconds((reliable.Count % 2) == 1
                ? reliable[middle]
                : (reliable[middle - 1] + reliable[middle]) / 2.0);
        }

        foreach (var stem in song.Stems)
        {
            if (stem.Midi == null || (stem.AlignmentMeasured && stem.AlignmentIsReliable))
            {
                continue;
            }

            stem.AlignmentOffset = fallback;
            stem.AlignmentIsFallback = true;
        }
    }
}
