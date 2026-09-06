using System;

namespace CodeBrix.Audio.Playback.Suno;

/// <summary>
/// What <see cref="SunoStemsLoader"/> should do beyond reading the files: where zip entries go, how
/// long the loader may spend measuring, and the values a player built from the song starts with.
/// </summary>
/// <remarks>
/// Every property has a default that suits an ordinary export, so <c>new SunoLoadOptions()</c> is a
/// sensible thing to pass. A loaded song keeps a snapshot of the options it was loaded with; a
/// change made to the instance afterwards does not reach it.
/// </remarks>
public sealed class SunoLoadOptions
{
    private TimeSpan _minimumNoteHold = TimeSpan.FromMilliseconds(60);
    private TimeSpan _maximumAlignmentOffset = TimeSpan.FromMilliseconds(300);
    private TimeSpan _lengthTolerance = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// How the entries of a stems zip are made available to the audio readers. Ignored when the
    /// song is loaded from a folder. Defaults to <see cref="SunoZipExtraction.CacheFolder"/>.
    /// </summary>
    public SunoZipExtraction ZipExtraction { get; set; } = SunoZipExtraction.CacheFolder;

    /// <summary>
    /// The folder that extracted zip entries are written to. Null - the default - puts them in a
    /// per-song folder under <see cref="SunoStemsLoader.DefaultCacheRoot"/>, keyed by the zip's
    /// path, size and last-write time, so that loading the same zip again reuses the same files.
    /// </summary>
    public string CacheFolder { get; set; }

    /// <summary>
    /// Whether the loader measures each stem's alignment offset. Defaults to <see langword="true"/>.
    /// Measuring decodes the stem's audio, so it costs about a second per stem with MIDI and it
    /// always runs on a worker, never on the audio thread. It is a no-op until an alignment
    /// estimator has been installed; see <see cref="SunoStem.AlignmentMeasured"/>.
    /// </summary>
    public bool MeasureAlignment { get; set; } = true;

    /// <summary>
    /// How far the alignment estimator may look in either direction. Defaults to 300 ms, which is
    /// comfortably wider than the largest offset measured on real exports (about 220 ms) and still
    /// well under one beat at any tempo Suno produces.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is zero or negative.</exception>
    public TimeSpan MaximumAlignmentOffset
    {
        get => _maximumAlignmentOffset;
        set
        {
            if (value <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The maximum alignment offset must be positive.");
            }

            _maximumAlignmentOffset = value;
        }
    }

    /// <summary>
    /// Whether the alignment search window is narrowed to suit the song's tempo. Defaults to
    /// <see langword="false"/>; read the remarks before turning it on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Cross-correlating a machine transcription against its own recording cannot always tell one
    /// beat from the next: on a repeating pattern every subdivision is a peak of the same comb and
    /// which tooth is tallest can be a matter of luck. One defence is a search window narrower than
    /// the distance between the teeth. With this on and a tempo map available, the window becomes
    /// half an eighth note at the song's slowest tempo - never below 80 ms, never wider than
    /// <see cref="MaximumAlignmentOffset"/> - which at ordinary tempos is 110 to 175 ms.
    /// </para>
    /// <para>
    /// It is OFF by default because on real exports it costs more than it buys. Measured offsets
    /// reach a quarter of a second: a window of 110 to 175 ms then cannot express the right answer
    /// at all, and what it finds instead is a small bump that has the window to itself - which
    /// scores as a confident, clean, wrong answer. Leave the window at
    /// <see cref="MaximumAlignmentOffset"/> and let the estimator's own rival-peak arbitration and
    /// its confidence do the work; turn this on only for a song you know has small offsets and
    /// very periodic parts.
    /// </para>
    /// <para>
    /// The window actually used for each stem is on the stem, as
    /// <see cref="SunoStem.AlignmentWindow"/>.
    /// </para>
    /// </remarks>
    public bool TempoAwareAlignmentWindow { get; set; }

    /// <summary>
    /// The shortest a note is taken to sound. Defaults to 60 ms, the same figure the player uses as
    /// its percussion rule, and it is applied here for the same reason: a stems export writes drum
    /// hits as note-on immediately followed by note-off, so measuring
    /// <see cref="SunoStem.MidiCoverage"/> from the written lengths would say a full drum
    /// transcription covers none of the song.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public TimeSpan MinimumNoteHold
    {
        get => _minimumNoteHold;
        set
        {
            if (value < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The minimum note hold cannot be negative.");
            }

            _minimumNoteHold = value;
        }
    }

    /// <summary>
    /// How far a stem's audio length may differ from the longest stem's before it is reported in
    /// <see cref="SunoSong.Problems"/>. Defaults to 50 ms; the stems of a real export are the same
    /// length to the sample.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public TimeSpan StemLengthTolerance
    {
        get => _lengthTolerance;
        set
        {
            if (value < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), "The stem length tolerance cannot be negative.");
            }

            _lengthTolerance = value;
        }
    }

    /// <summary>
    /// The General MIDI SoundFont a player built from the song uses for a MIDI stem the consumer
    /// gives no instrument of its own. Null - the default - means the consumer supplies instruments.
    /// FluidR3_GM is the usual recommendation; it is MIT licensed and may be redistributed.
    /// </summary>
    /// <remarks>
    /// Loading a song never opens this file. It is carried on the song so that building a player
    /// from it needs nothing but the song.
    /// </remarks>
    public string GeneralMidiSoundFontPath { get; set; }

    /// <summary>
    /// Whether the loader looks for a full-mix "&lt;Title&gt;.wav" or "&lt;Title&gt;.mp3" beside the
    /// stems - inside the folder, and in the folder that holds the folder or the zip, which is where
    /// a full-song download lands. Defaults to <see langword="true"/>.
    /// </summary>
    public bool FindFullMix { get; set; } = true;

    /// <summary>
    /// Returns an independent copy of these options.
    /// </summary>
    /// <returns>A new instance carrying the same values.</returns>
    public SunoLoadOptions Clone() =>
        new SunoLoadOptions
        {
            ZipExtraction = ZipExtraction,
            CacheFolder = CacheFolder,
            MeasureAlignment = MeasureAlignment,
            MaximumAlignmentOffset = MaximumAlignmentOffset,
            TempoAwareAlignmentWindow = TempoAwareAlignmentWindow,
            MinimumNoteHold = MinimumNoteHold,
            StemLengthTolerance = StemLengthTolerance,
            GeneralMidiSoundFontPath = GeneralMidiSoundFontPath,
            FindFullMix = FindFullMix,
        };
}
