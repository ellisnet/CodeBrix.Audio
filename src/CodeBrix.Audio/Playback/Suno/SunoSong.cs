using System;
using System.Collections.Generic;
using System.Linq;
using CodeBrix.Audio.Playback.Suno.Internal;

namespace CodeBrix.Audio.Playback.Suno;

/// <summary>
/// A Suno stems export, loaded: the song's title, its stems, how long it is, its tempo map, and
/// everything that could not be honoured about it.
/// </summary>
/// <remarks>
/// <para>
/// Produced by <see cref="SunoStemsLoader"/> from a "&lt;Title&gt; Stems.zip" or from a folder
/// holding the same files. Loading reads names, MIDI files and WAV headers; it decodes no audio and,
/// for a zip, decompresses nothing. Audio is materialised the first time a stem is asked for it.
/// </para>
/// <para>
/// A song is immutable except for the per-stem <see cref="SunoStem.AlignmentOffset"/>, which a host
/// may override.
/// </para>
/// </remarks>
public sealed partial class SunoSong
{
    private readonly List<string> _problems = new List<string>();
    private readonly SunoContentStore _store;
    private SunoStem[] _stems = [];

    internal SunoSong(SunoContentStore store, string sourcePath, string title, SunoLoadOptions options)
    {
        _store = store;
        SourcePath = sourcePath;
        Title = title;
        Options = options;
    }

    /// <summary>
    /// The song's title, taken from the file names - "&lt;Title&gt; (&lt;Stem&gt;).wav" - and not
    /// from the MIDI track name, which a stems export mangles beyond recovery whenever the title
    /// holds anything but plain text.
    /// </summary>
    public string Title { get; }

    /// <summary>The zip or folder this song was loaded from.</summary>
    public string SourcePath { get; }

    /// <summary>Which of the two shapes it was loaded from.</summary>
    public SunoSourceKind Source => _store.Kind;

    /// <summary>
    /// The song's stems, in a stable order: the names this package knows about first, in the order
    /// of <see cref="SunoStemDefaults.KnownStemNames"/>, then any others alphabetically. The order
    /// does not depend on how the files happened to be listed, so a zip and the same files extracted
    /// to a folder produce the same song.
    /// </summary>
    public IReadOnlyList<SunoStem> Stems => _stems;

    /// <summary>
    /// The stems that carry audio. This is the default mix: playing all of them reproduces the song
    /// even when no full mix was downloaded.
    /// </summary>
    public IReadOnlyList<SunoStem> AudioStems { get; private set; } = [];

    /// <summary>The stems that carry a MIDI file.</summary>
    public IReadOnlyList<SunoStem> MidiStems { get; private set; } = [];

    /// <summary>
    /// How long the song is: the longest stem's audio, or, for an export with no audio at all, the
    /// longest stem's MIDI.
    /// </summary>
    public TimeSpan Duration { get; internal set; }

    /// <summary>
    /// The song's tempo map, taken from the first stem that has MIDI. Every MIDI stem of an export
    /// carries the same map. Empty when no stem has MIDI.
    /// </summary>
    /// <remarks>
    /// An export made with Suno's "Follow tempo changes" option - the only one worth using - carries
    /// one entry per beat, so expect several hundred entries and a tempo that wanders by a few BPM
    /// throughout.
    /// </remarks>
    public IReadOnlyList<SunoTempoChange> TempoMap { get; internal set; } = [];

    /// <summary>
    /// The tempo the song starts at, or 120 when no stem has MIDI to say otherwise.
    /// </summary>
    public double InitialBeatsPerMinute => TempoMap.Count > 0 ? TempoMap[0].BeatsPerMinute : 120.0;

    /// <summary>
    /// The full-mix WAV that sits beside the stems, or null when there is none. Suno downloads it
    /// separately from the stems, so it is usually beside the zip rather than inside it.
    /// </summary>
    public string FullMixWavPath { get; internal set; }

    /// <summary>The full-mix MP3 that sits beside the stems, or null when there is none.</summary>
    public string FullMixMp3Path { get; internal set; }

    /// <summary>
    /// The full mix to prefer - the WAV when there is one, otherwise the MP3 - or null when neither
    /// was found. Nothing here plays it; <c>AudioFilePlayer</c> does, and needs nothing else.
    /// </summary>
    public string FullMixPath => FullMixWavPath ?? FullMixMp3Path;

    /// <summary>Whether a full mix was found beside the stems.</summary>
    public bool HasFullMix => FullMixPath != null;

    /// <summary>
    /// Where entries extracted from a stems zip are written, or null when the song came from a
    /// folder or was loaded with <see cref="SunoZipExtraction.Memory"/>. The folder is keyed by the
    /// zip's path, size and last-write time, so loading the same download again reuses it.
    /// </summary>
    public string CacheFolder => _store.CacheFolder;

    /// <summary>
    /// Everything that could not be honoured about this export, one human-readable line each:
    /// missing files, MIDI that would not read, stems of the wrong length, track names that belong
    /// to another song, names outside the known vocabulary. Empty for a clean export. Never thrown.
    /// </summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>
    /// The options this song was loaded with. A snapshot: changing it now changes nothing about the
    /// song, but a player built from the song reads its
    /// <see cref="SunoLoadOptions.GeneralMidiSoundFontPath"/> and
    /// <see cref="SunoLoadOptions.MinimumNoteHold"/> from here.
    /// </summary>
    public SunoLoadOptions Options { get; }

    /// <summary>
    /// The stem of a given name, or null when the song has no such stem. Names are matched
    /// case-insensitively.
    /// </summary>
    /// <param name="stemName">The stem name, for example "Drums".</param>
    /// <returns>The stem, or null.</returns>
    public SunoStem this[string stemName] => FindStem(stemName);

    /// <summary>
    /// Finds a stem by name, case-insensitively.
    /// </summary>
    /// <param name="stemName">The stem name, for example "Backing Vocals".</param>
    /// <returns>The stem, or null when the song has no such stem.</returns>
    public SunoStem FindStem(string stemName)
    {
        if (stemName == null)
        {
            return null;
        }

        var wanted = stemName.Trim();
        foreach (var stem in _stems)
        {
            if (string.Equals(stem.Name, wanted, StringComparison.OrdinalIgnoreCase))
            {
                return stem;
            }
        }

        return null;
    }

    /// <summary>
    /// Deletes whatever this song has extracted from its zip - the files in its cache folder, or the
    /// bytes held in memory - and then the cache folder itself if nothing else is left in it.
    /// Harmless at any time; the next request for a stem's audio extracts it again. Does nothing for
    /// a song loaded from a folder, which never extracts anything.
    /// </summary>
    public void ClearCache() => _store.ClearCache();

    /// <summary>A short description, for diagnostics.</summary>
    /// <returns>The title, stem count and duration.</returns>
    public override string ToString() =>
        $"{Title} - {_stems.Length} stem(s), {Duration:mm\\:ss}";

    internal SunoContentStore Store => _store;

    internal void SetStems(SunoStem[] stems)
    {
        _stems = stems;
        AudioStems = Array.AsReadOnly(stems.Where(stem => stem.HasAudio).ToArray());
        MidiStems = Array.AsReadOnly(stems.Where(stem => stem.Midi != null).ToArray());
    }

    internal void AddProblem(string problem) => _problems.Add(problem);
}
