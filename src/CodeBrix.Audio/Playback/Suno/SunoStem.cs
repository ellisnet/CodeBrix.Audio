using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Wave;
using CodeBrix.Audio.Wave.SampleProviders;

namespace CodeBrix.Audio.Playback.Suno;

/// <summary>
/// One part of a Suno stems export - the vocals, the drums, the bass - with whichever of its audio
/// and MIDI files the export carried, and everything a player needs in order to put it on a track.
/// </summary>
/// <remarks>
/// <para>
/// A stem is a description, not an open file: nothing is decoded when a song loads, and the audio
/// of a stem inside a zip is not even decompressed until something asks for it. The
/// <c>Open...</c> and <c>Get...Path</c> members are what ask.
/// </para>
/// <para>
/// Whether a stem has MIDI is a property of the export, never of the stem's name: a bass part has
/// MIDI in one song and not in the next, and a MIDI file that exists may hold two notes beside a
/// four-minute WAV. <see cref="NoteCount"/> and <see cref="MidiCoverage"/> are there so that a host
/// can tell the difference and default such a stem to its audio.
/// </para>
/// </remarks>
public sealed class SunoStem
{
    private readonly List<string> _problems = new List<string>();
    private readonly SunoSong _song;
    private readonly string _wavKey;
    private readonly string _mp3Key;
    private readonly string _midiKey;

    internal SunoStem(SunoSong song, string name, string wavKey, string mp3Key, string midiKey)
    {
        _song = song;
        _wavKey = wavKey;
        _mp3Key = mp3Key;
        _midiKey = midiKey;
        Name = name;
        IsKnownName = SunoStemDefaults.IsKnown(name);

        var defaults = SunoStemDefaults.GetOrFallback(name);
        GmProgram = defaults.GmProgram;
        Channel = defaults.Channel;
        IsPercussion = defaults.IsPercussion;
    }

    /// <summary>The stem's name as the export spelled it - "Backing Vocals", "Drums", "FX".</summary>
    public string Name { get; }

    /// <summary>The song this stem belongs to.</summary>
    public SunoSong Song => _song;

    /// <summary>
    /// Whether the name is one of those in <see cref="SunoStemDefaults.KnownStemNames"/>. A stem
    /// whose name is not loads and plays exactly the same; it simply had no defaults to start from.
    /// </summary>
    public bool IsKnownName { get; }

    /// <summary>Whether the export carried a WAV for this stem.</summary>
    public bool HasWav => _wavKey != null;

    /// <summary>Whether the export carried an MP3 for this stem.</summary>
    public bool HasMp3 => _mp3Key != null;

    /// <summary>Whether the export carried audio of either kind for this stem.</summary>
    public bool HasAudio => _wavKey != null || _mp3Key != null;

    /// <summary>Whether the export carried a MIDI file for this stem.</summary>
    public bool HasMidi => _midiKey != null;

    /// <summary>The WAV's file name, without any folder, or null when there is no WAV.</summary>
    public string WavFileName => FileNameOf(_wavKey);

    /// <summary>The MP3's file name, without any folder, or null when there is no MP3.</summary>
    public string Mp3FileName => FileNameOf(_mp3Key);

    /// <summary>The MIDI file's name, without any folder, or null when there is no MIDI.</summary>
    public string MidiFileName => FileNameOf(_midiKey);

    /// <summary>
    /// The file name of the audio a player should use: the WAV when there is one, otherwise the MP3.
    /// Null when the stem has no audio at all.
    /// </summary>
    public string AudioFileName => WavFileName ?? Mp3FileName;

    /// <summary>
    /// The stem's MIDI, read tolerantly, or null when the export carried none or it could not be
    /// read. A file that could not be read leaves a line in <see cref="Problems"/>.
    /// </summary>
    public MidiSequence Midi { get; internal set; }

    /// <summary>
    /// The General MIDI program this stem plays: the program change in its MIDI file when it has
    /// one, otherwise the default for its name. A percussion stem's program is a kit number rather
    /// than an instrument, and is advisory - a SoundFont that has no such kit falls back to its
    /// standard one.
    /// </summary>
    public int GmProgram { get; internal set; }

    /// <summary>
    /// The MIDI channel this stem plays on, 1 to 16: the channel of its MIDI file when it has one,
    /// otherwise the default for its name. Percussion stems use 10.
    /// </summary>
    public int Channel { get; internal set; }

    /// <summary>Whether this stem is percussion, and so belongs on channel 10.</summary>
    public bool IsPercussion { get; internal set; }

    /// <summary>
    /// How many note-on messages the stem's MIDI holds. Zero when it has no MIDI. A handful of notes
    /// beside a full-length WAV is a real and common shape.
    /// </summary>
    public int NoteCount { get; internal set; }

    /// <summary>
    /// The fraction of the song, 0 to 1, during which this stem's MIDI has a note sounding. Zero
    /// when the stem has no MIDI.
    /// </summary>
    /// <remarks>
    /// Every note counts for at least <see cref="SunoLoadOptions.MinimumNoteHold"/>, because a stems
    /// export writes drum hits with a note-off one tick after the note-on and measuring those as
    /// written would say a complete drum transcription covers none of the song.
    /// </remarks>
    public double MidiCoverage { get; internal set; }

    /// <summary>How long the stem's audio is. Zero when the stem has no audio.</summary>
    public TimeSpan Duration { get; internal set; }

    /// <summary>The sample rate of the stem's WAV, or 0 when it has none.</summary>
    public int AudioSampleRate { get; internal set; }

    /// <summary>The channel count of the stem's WAV, or 0 when it has none.</summary>
    public int AudioChannels { get; internal set; }

    /// <summary>
    /// Where this stem's audio sits relative to its MIDI: audio time = MIDI time +
    /// <see cref="AlignmentOffset"/>, so a positive value means the audio lags the MIDI and a MIDI
    /// track has to be delayed by this much to line up with the stems. Zero until it is measured,
    /// and settable so that a host can override the measurement.
    /// </summary>
    /// <remarks>
    /// A real export sits anywhere from about -120 ms to +220 ms, differs from stem to stem within
    /// one song, and does not drift over the length of a song.
    /// </remarks>
    public TimeSpan AlignmentOffset { get; set; }

    /// <summary>
    /// Whether <see cref="AlignmentOffset"/> came from a measurement. False when the stem has no
    /// MIDI or no audio, when the load options turned measuring off, and when no alignment estimator
    /// is installed.
    /// </summary>
    public bool AlignmentMeasured { get; internal set; }

    /// <summary>
    /// How much the estimator believed its own answer, 0 to 1, or 0 when nothing was measured.
    /// </summary>
    public double AlignmentConfidence { get; internal set; }

    /// <summary>
    /// Whether the estimator considered its answer usable. False when nothing was measured.
    /// </summary>
    public bool AlignmentIsReliable { get; internal set; }

    /// <summary>
    /// What this stem's OWN measurement said, before any per-song fallback was applied and before
    /// a host overrode anything. Zero when nothing was measured. Compare it with
    /// <see cref="AlignmentOffset"/> to see whether the song's fallback was used for this stem.
    /// </summary>
    public TimeSpan MeasuredAlignmentOffset { get; internal set; }

    /// <summary>
    /// Whether <see cref="AlignmentOffset"/> holds the song's fallback rather than this stem's own
    /// answer. The fallback is the median offset of the stems whose own measurement WAS reliable,
    /// and it is applied to a MIDI stem that could not be measured, that has no audio of its own,
    /// or whose measurement the estimator would not stand behind.
    /// </summary>
    public bool AlignmentIsFallback { get; internal set; }

    /// <summary>
    /// The half-width of the search the estimator was given for this stem. Zero when nothing was
    /// measured. It is the load options' maximum unless a tempo map made a narrower one possible;
    /// see <see cref="SunoLoadOptions.TempoAwareAlignmentWindow"/>.
    /// </summary>
    public TimeSpan AlignmentWindow { get; internal set; }

    /// <summary>
    /// What could not be honoured about this stem, one human-readable line each. Every line also
    /// appears, prefixed with the stem's name, in <see cref="SunoSong.Problems"/>. Never thrown.
    /// </summary>
    public IReadOnlyList<string> Problems => _problems;

    /// <summary>Every note-on time in the stem's MIDI, in seconds, ascending.</summary>
    internal IReadOnlyList<double> NoteOnTimes { get; set; } = [];

    /// <summary>The tempo map this stem's own MIDI carries. Every MIDI stem of a song carries the same one.</summary>
    internal SunoTempoChange[] TempoMap { get; set; } = [];

    /// <summary>
    /// The path of this stem's WAV on disk, extracting it from the zip first if that is where it
    /// lives and it is not in the cache yet.
    /// </summary>
    /// <returns>The full path of the WAV.</returns>
    /// <exception cref="InvalidOperationException">
    /// The stem has no WAV, or the song was loaded with <see cref="SunoZipExtraction.Memory"/> and
    /// so has no files to point at.
    /// </exception>
    public string GetWavPath() => PathOf(_wavKey, "WAV");

    /// <summary>
    /// The path of this stem's MP3 on disk, extracting it from the zip first if that is where it
    /// lives and it is not in the cache yet.
    /// </summary>
    /// <returns>The full path of the MP3.</returns>
    /// <exception cref="InvalidOperationException">
    /// The stem has no MP3, or the song was loaded with <see cref="SunoZipExtraction.Memory"/>.
    /// </exception>
    public string GetMp3Path() => PathOf(_mp3Key, "MP3");

    /// <summary>
    /// The path of the audio a player should use - the WAV when there is one, otherwise the MP3 -
    /// extracting it from the zip first if need be.
    /// </summary>
    /// <returns>The full path of the audio file.</returns>
    /// <exception cref="InvalidOperationException">
    /// The stem has no audio, or the song was loaded with <see cref="SunoZipExtraction.Memory"/>.
    /// </exception>
    public string GetAudioPath() => PathOf(_wavKey ?? _mp3Key, "audio");

    /// <summary>
    /// Opens this stem's WAV as a seekable stream. The caller owns the stream and must dispose it.
    /// </summary>
    /// <returns>A seekable stream over the WAV.</returns>
    /// <exception cref="InvalidOperationException">The stem has no WAV.</exception>
    public Stream OpenWav() => Open(_wavKey, "WAV");

    /// <summary>
    /// Opens this stem's MP3 as a seekable stream. The caller owns the stream and must dispose it.
    /// </summary>
    /// <returns>A seekable stream over the MP3.</returns>
    /// <exception cref="InvalidOperationException">The stem has no MP3.</exception>
    public Stream OpenMp3() => Open(_mp3Key, "MP3");

    /// <summary>
    /// Opens the audio a player should use - the WAV when there is one, otherwise the MP3 - as a
    /// seekable stream. The caller owns the stream and must dispose it.
    /// </summary>
    /// <returns>A seekable stream over the audio file.</returns>
    /// <exception cref="InvalidOperationException">The stem has no audio.</exception>
    public Stream OpenAudio() => Open(_wavKey ?? _mp3Key, "audio");

    /// <summary>A short description, for diagnostics.</summary>
    /// <returns>The stem's name and what it carries.</returns>
    public override string ToString()
    {
        var sources = HasWav ? "wav" : HasMp3 ? "mp3" : "no audio";
        return HasMidi ? $"{Name} ({sources}, midi: {NoteCount} notes)" : $"{Name} ({sources}, no midi)";
    }

    internal string WavKey => _wavKey;

    internal string Mp3Key => _mp3Key;

    internal string MidiKey => _midiKey;

    internal void AddProblem(string problem) => _problems.Add(problem);

    /// <summary>
    /// Decodes the stem's audio to mono float samples, for the alignment estimator. Never called on
    /// the audio thread, and never called at all unless an estimator is installed.
    /// </summary>
    internal float[] ReadMonoAudio(out int sampleRate)
    {
        sampleRate = 0;
        var key = _wavKey ?? _mp3Key;
        if (key == null)
        {
            return [];
        }

        using var stream = _song.Store.OpenSeekable(key);
        using WaveStream reader = key == _wavKey
            ? new WaveFileReader(stream)
            : new Mp3FileReader(stream);

        var source = SampleProviderConverters.ConvertWaveProviderIntoSampleProvider(reader);
        var channels = source.WaveFormat.Channels;
        sampleRate = source.WaveFormat.SampleRate;
        if (channels <= 0)
        {
            return [];
        }

        var scratch = new float[channels * 16384];
        var mono = new List<float>(EstimatedFrames(reader, channels));
        int read;
        while ((read = source.Read(scratch.AsSpan())) > 0)
        {
            for (var i = 0; i + channels <= read; i += channels)
            {
                var sum = 0f;
                for (var c = 0; c < channels; c++)
                {
                    sum += scratch[i + c];
                }

                mono.Add(sum / channels);
            }
        }

        return mono.ToArray();
    }

    private static int EstimatedFrames(WaveStream reader, int channels)
    {
        var bytesPerFrame = reader.WaveFormat.BlockAlign;
        if (bytesPerFrame <= 0 || reader.Length <= 0)
        {
            return 1024;
        }

        var frames = reader.Length / bytesPerFrame;
        return frames > 0 && frames < int.MaxValue ? (int)frames : 1024;
    }

    private string FileNameOf(string key) => key == null ? null : Path.GetFileName(key);

    private string PathOf(string key, string what)
    {
        if (key == null)
        {
            throw new InvalidOperationException($"The stem '{Name}' has no {what} file.");
        }

        return _song.Store.GetFilePath(key);
    }

    private Stream Open(string key, string what)
    {
        if (key == null)
        {
            throw new InvalidOperationException($"The stem '{Name}' has no {what} file.");
        }

        return _song.Store.OpenSeekable(key);
    }
}
