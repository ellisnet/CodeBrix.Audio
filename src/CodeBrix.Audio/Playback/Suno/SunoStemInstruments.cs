using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Synth.Sfz;

namespace CodeBrix.Audio.Playback.Suno;

/// <summary>
/// An instrument for ONE NAMED STEM, overriding whatever the rest of the song is being played with.
/// </summary>
/// <remarks>
/// <para>
/// KEYED BY STEM NAME, AND THAT IS THE POINT. <see cref="MappedInstrumentLibrary"/> substitutes by
/// General MIDI PROGRAM - "everything that plays Choir Aahs" - which is the wrong axis for a stems
/// export: Drums and Percussion are both on channel 10 with a kit number for a program, and two
/// melodic parts of one song can easily carry the same program. The part is the stem, so the
/// override is per stem.
/// </para>
/// <code>
/// var options = new SunoPlayerOptions { InstrumentLibraryName = "ModestSynthGm" };
///
/// options.StemInstruments["Bass"] = rate =&gt; new MyBassSynthesizer(rate);
/// options.StemInstruments.SetFromFile("Synth", "/packs/Grand Piano/Grand Piano.dspreset");
/// </code>
/// <para>
/// NAMES MATCH THE WAY <c>song["Bass"]</c> MATCHES: case-insensitively, with surrounding space
/// ignored. A name the song has no stem for is REPORTED in the player's
/// <see cref="MultiTrackPlayer.Problems"/> and never thrown - which parts an export carries is a
/// property of the download.
/// </para>
/// <para>
/// A FILE IS LOADED WHERE YOU WROTE THE PATH, not lazily at play time, so a typo is an error on
/// that line - the same rule <see cref="MappedInstrumentLibrary"/> follows. One loaded instrument
/// is shared by every synthesizer built from it, and a loaded instrument is held for the life of
/// this map. The FACTORY, however, is called once per stem per render context and may be called
/// from a worker thread, so it must never hand out the same
/// <see cref="IMidiSynthesizer"/> twice.
/// </para>
/// </remarks>
public sealed class SunoStemInstruments
{
    private static readonly string[] DecentSamplerExtensions = [".dspreset", ".dslibrary", ".dsbundle"];

    private readonly object gate = new object();

    private readonly Dictionary<string, Func<int, IMidiSynthesizer>> factories =
        new Dictionary<string, Func<int, IMidiSynthesizer>>(StringComparer.OrdinalIgnoreCase);

    private readonly List<string> order = new List<string>();

    private DecentSamplerInstrumentCache decentSamplerInstruments;
    private SfzInstrumentCache sfzInstruments;
    private Dictionary<string, SoundFontInstrumentLibrary> soundFonts;

    /// <summary>How many stems have an instrument of their own here.</summary>
    public int Count
    {
        get { lock (gate) { return order.Count; } }
    }

    /// <summary>
    /// The stem names that have an instrument of their own, in the order they were first set.
    /// </summary>
    public IReadOnlyList<string> StemNames
    {
        get { lock (gate) { return order.ToArray(); } }
    }

    /// <summary>
    /// The instrument for one stem: a factory taking the sample rate the synthesizer must render
    /// at. Reading a name that has none returns <see langword="null"/>, and setting one to
    /// <see langword="null"/> removes it.
    /// </summary>
    /// <param name="stemName">The stem's name, matched case-insensitively.</param>
    /// <returns>The factory for that stem, or null when it has none.</returns>
    /// <exception cref="ArgumentException"><paramref name="stemName"/> is null or blank.</exception>
    public Func<int, IMidiSynthesizer> this[string stemName]
    {
        get
        {
            var key = Key(stemName);
            lock (gate)
            {
                return factories.TryGetValue(key, out var factory) ? factory : null;
            }
        }

        set
        {
            if (value == null)
            {
                Remove(stemName);
                return;
            }

            Set(stemName, value);
        }
    }

    /// <summary>Plays one stem with a synthesizer of your own, built on demand.</summary>
    /// <param name="stemName">The stem's name, matched case-insensitively.</param>
    /// <param name="synthesizerFactory">
    /// Builds the instrument at the sample rate it is handed. Called once per stem per render
    /// context, possibly from a worker thread, so it must never hand out the same synthesizer twice.
    /// </param>
    /// <exception cref="ArgumentException"><paramref name="stemName"/> is null or blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="synthesizerFactory"/> is null.</exception>
    public void Set(string stemName, Func<int, IMidiSynthesizer> synthesizerFactory)
    {
        var key = Key(stemName);

        if (synthesizerFactory == null)
        {
            throw new ArgumentNullException(nameof(synthesizerFactory));
        }

        Store(key, synthesizerFactory);
    }

    /// <summary>
    /// Plays one stem with an instrument file: a Decent Sampler preset, an SFZ file, or one program
    /// of a SoundFont.
    /// </summary>
    /// <param name="stemName">The stem's name, matched case-insensitively.</param>
    /// <param name="instrumentPath">
    /// A <c>.dspreset</c>, <c>.dslibrary</c> or <c>.dsbundle</c> (Decent Sampler) or a Decent
    /// Sampler library folder; a <c>.sfz</c>; or a <c>.sf2</c>, whose
    /// <paramref name="soundFontProgram"/> is used. The file is loaded HERE, once, and shared by
    /// every synthesizer built from it.
    /// </param>
    /// <param name="soundFontProgram">
    /// Which program to take, 0 to 127. Used only for a <c>.sf2</c>; every other kind of file names
    /// one instrument already.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="stemName"/> or <paramref name="instrumentPath"/> is null or blank.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="soundFontProgram"/> is outside 0 to 127.</exception>
    /// <exception cref="NotSupportedException">The file is not a kind this can load.</exception>
    /// <exception cref="FileNotFoundException">There is no file at that path.</exception>
    /// <remarks>
    /// A Decent Sampler instrument loaded here is rendered OFFLINE by
    /// <see cref="MultiTrackPlayer.Render(int, TimeSpan?)"/> and in real time by
    /// <see cref="MultiTrackPlayer.Play"/>, because the player switches its streaming mode for the
    /// render and back; nothing has to be set for that.
    /// </remarks>
    public void SetFromFile(string stemName, string instrumentPath, int soundFontProgram = 0)
    {
        var key = Key(stemName);

        if (soundFontProgram < 0 || soundFontProgram > 127)
        {
            throw new ArgumentOutOfRangeException(
                nameof(soundFontProgram), soundFontProgram, "A General MIDI program number is 0 to 127.");
        }

        Store(key, LoadInstrument(instrumentPath, soundFontProgram, forPercussion: false));
    }

    /// <summary>
    /// Plays one stem as a percussion KIT, where each note number is its own instrument rather than
    /// a pitch - what a Drums or Percussion stem needs.
    /// </summary>
    /// <param name="stemName">The stem's name, matched case-insensitively.</param>
    /// <param name="instrumentPath">
    /// The instrument file. For a <c>.sf2</c> this takes the SoundFont's percussion kit rather than
    /// a melodic program; for every other kind it is the same as <see cref="SetFromFile"/>, because
    /// a Decent Sampler or SFZ kit is one instrument whose notes are already kit pieces.
    /// </param>
    /// <exception cref="ArgumentException">
    /// <paramref name="stemName"/> or <paramref name="instrumentPath"/> is null or blank.
    /// </exception>
    /// <exception cref="NotSupportedException">The file is not a kind this can load.</exception>
    /// <exception cref="FileNotFoundException">There is no file at that path.</exception>
    public void SetPercussionFromFile(string stemName, string instrumentPath)
    {
        var key = Key(stemName);

        Store(key, LoadInstrument(instrumentPath, 0, forPercussion: true));
    }

    /// <summary>Whether a stem has an instrument of its own here.</summary>
    /// <param name="stemName">The stem's name, matched case-insensitively. Null returns false.</param>
    /// <returns><see langword="true"/> when that stem has one.</returns>
    public bool Contains(string stemName)
    {
        if (string.IsNullOrWhiteSpace(stemName))
        {
            return false;
        }

        lock (gate) { return factories.ContainsKey(stemName.Trim()); }
    }

    /// <summary>Tries to get the instrument set for one stem.</summary>
    /// <param name="stemName">The stem's name, matched case-insensitively. Null returns false.</param>
    /// <param name="synthesizerFactory">Receives the factory, or null when there is none.</param>
    /// <returns><see langword="true"/> when that stem has one.</returns>
    public bool TryGet(string stemName, out Func<int, IMidiSynthesizer> synthesizerFactory)
    {
        synthesizerFactory = null;

        if (string.IsNullOrWhiteSpace(stemName))
        {
            return false;
        }

        lock (gate) { return factories.TryGetValue(stemName.Trim(), out synthesizerFactory); }
    }

    /// <summary>Removes the instrument set for one stem, so it plays with the rest of the song.</summary>
    /// <param name="stemName">The stem's name, matched case-insensitively. Null returns false.</param>
    /// <returns><see langword="true"/> when there was one to remove.</returns>
    /// <remarks>
    /// Anything loaded from a file for that stem is still held, so setting the same path again is
    /// free.
    /// </remarks>
    public bool Remove(string stemName)
    {
        if (string.IsNullOrWhiteSpace(stemName))
        {
            return false;
        }

        var wanted = stemName.Trim();
        lock (gate)
        {
            if (!factories.Remove(wanted))
            {
                return false;
            }

            for (var i = 0; i < order.Count; i++)
            {
                if (string.Equals(order[i], wanted, StringComparison.OrdinalIgnoreCase))
                {
                    order.RemoveAt(i);
                    break;
                }
            }

            return true;
        }
    }

    /// <summary>Removes every per-stem instrument. Anything loaded from a file is still held.</summary>
    public void Clear()
    {
        lock (gate)
        {
            factories.Clear();
            order.Clear();
        }
    }

    /// <summary>A short description, for diagnostics.</summary>
    /// <returns>How many stems are overridden, and which.</returns>
    public override string ToString()
    {
        lock (gate)
        {
            return order.Count == 0
                ? "no per-stem instruments"
                : $"{order.Count} per-stem instrument(s): {string.Join(", ", order)}";
        }
    }

    private void Store(string stemName, Func<int, IMidiSynthesizer> factory)
    {
        lock (gate)
        {
            if (!factories.ContainsKey(stemName))
            {
                order.Add(stemName);
            }

            factories[stemName] = factory;
        }
    }

    private static string Key(string stemName)
    {
        if (string.IsNullOrWhiteSpace(stemName))
        {
            throw new ArgumentException(
                "A stem name is required: a per-stem instrument is addressed by the stem's name.",
                nameof(stemName));
        }

        return stemName.Trim();
    }

    private Func<int, IMidiSynthesizer> LoadInstrument(string instrumentPath, int soundFontProgram, bool forPercussion)
    {
        if (string.IsNullOrWhiteSpace(instrumentPath))
        {
            throw new ArgumentException("An instrument file path is required.", nameof(instrumentPath));
        }

        var extension = Path.GetExtension(instrumentPath);

        if (IsDecentSampler(extension) || (extension.Length == 0 && Directory.Exists(instrumentPath)))
        {
            var instrument = DecentSamplerInstruments().Get(instrumentPath);
            return sampleRate => new DecentSamplerSynthesizer(instrument, sampleRate);
        }

        if (string.Equals(extension, ".sfz", StringComparison.OrdinalIgnoreCase))
        {
            var instrument = SfzInstruments().Get(instrumentPath);
            return sampleRate => new SfzSynthesizer(instrument, sampleRate);
        }

        if (string.Equals(extension, ".sf2", StringComparison.OrdinalIgnoreCase))
        {
            var soundFont = SoundFontFor(instrumentPath);

            return forPercussion
                ? sampleRate => soundFont.CreatePercussionSynthesizer(sampleRate)
                : sampleRate => soundFont.CreateSynthesizer(soundFontProgram, sampleRate);
        }

        throw new NotSupportedException(
            $"'{extension}' is not an instrument file a stem can be played with. It understands " +
            ".dspreset, .dslibrary and .dsbundle (Decent Sampler), a Decent Sampler library " +
            "folder, .sfz (SFZ) and .sf2 (SoundFont). Anything else is one line of your own: " +
            "StemInstruments[\"Bass\"] = sampleRate => new MySynthesizer(sampleRate).");
    }

    private static bool IsDecentSampler(string extension)
    {
        foreach (var known in DecentSamplerExtensions)
        {
            if (string.Equals(extension, known, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private DecentSamplerInstrumentCache DecentSamplerInstruments()
    {
        lock (gate)
        {
            return decentSamplerInstruments ??= new DecentSamplerInstrumentCache();
        }
    }

    private SfzInstrumentCache SfzInstruments()
    {
        lock (gate)
        {
            return sfzInstruments ??= new SfzInstrumentCache();
        }
    }

    private SoundFontInstrumentLibrary SoundFontFor(string soundFontPath)
    {
        var key = SafeFullPath(soundFontPath);

        lock (gate)
        {
            soundFonts ??= new Dictionary<string, SoundFontInstrumentLibrary>(StringComparer.OrdinalIgnoreCase);

            if (soundFonts.TryGetValue(key, out var existing))
            {
                return existing;
            }

            // Parsed inside the lock, as the instrument caches do it and for the same reason: a
            // bank is large and slow to read, and racing threads would double the peak memory to no
            // purpose. The library built here is never registered - it exists only to hold the one
            // parsed SoundFont.
            var library = new SoundFontInstrumentLibrary(
                $"SunoStemInstruments:{Path.GetFileName(soundFontPath)}",
                $"The SoundFont '{Path.GetFileName(soundFontPath)}', loaded for one stem of a song.",
                soundFontPath);

            soundFonts.Add(key, library);
            return library;
        }
    }

    private static string SafeFullPath(string path)
    {
        try
        {
            return Path.GetFullPath(path);
        }
        catch (Exception)
        {
            return path;
        }
    }
}
