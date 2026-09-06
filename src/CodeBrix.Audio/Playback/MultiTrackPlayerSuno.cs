using System;
using CodeBrix.Audio.Playback.Suno;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Playback;

/// <summary>
/// Building a player from a Suno stems export. A <see cref="SunoSong"/> is one loader's answer for
/// this player; nothing about the player knows or cares where its tracks came from.
/// </summary>
public sealed partial class MultiTrackPlayer
{
    /// <summary>
    /// Builds a player from a loaded stems export: one track per stem, playing the recordings.
    /// </summary>
    /// <param name="song">The song to play.</param>
    /// <returns>A player, not yet prepared. The caller owns it and disposes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="song"/> is null.</exception>
    public static MultiTrackPlayer Load(SunoSong song) => Load(song, null);

    /// <summary>
    /// Builds a player from a loaded stems export.
    /// </summary>
    /// <param name="song">The song to play.</param>
    /// <param name="options">
    /// What to do beyond putting one track on each stem; null for the defaults, which play the
    /// recordings and attach a MIDI source to any stem an instrument can be found for.
    /// </param>
    /// <returns>A player, not yet prepared. The caller owns it and disposes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="song"/> is null.</exception>
    /// <exception cref="System.IO.IOException">A stem's audio could not be read.</exception>
    /// <remarks>
    /// <para>
    /// Every stem with audio becomes a track playing that audio - the WAV when there is one, the MP3
    /// otherwise - which is the default mix: the song as it was downloaded. Where the stem also has
    /// MIDI and an instrument can be built for it, the same track carries the MIDI as its second
    /// source, so switching a part from the recording to a synthesized rendition is a property
    /// change rather than a rebuild. A stem with MIDI and no audio becomes a MIDI-only track.
    /// </para>
    /// <para>
    /// The instrument comes from <see cref="SunoPlayerOptions.InstrumentFactory"/> when one is
    /// given, and otherwise from a General MIDI SoundFont - the options' path, or the one the song
    /// was loaded with. The SoundFont itself is loaded once and shared by every track; each track
    /// still gets its own synthesizer, because a synthesizer is not thread-safe.
    /// </para>
    /// <para>
    /// Extracting a stems zip to memory rather than to a cache folder makes every stem's audio a
    /// stream, and a stream source is copied into memory when the track is built. A four-minute
    /// export is several hundred megabytes of WAV, so that combination is for hosts that cannot
    /// write to disk.
    /// </para>
    /// </remarks>
    public static MultiTrackPlayer Load(SunoSong song, SunoPlayerOptions options)
    {
        if (song == null)
        {
            throw new ArgumentNullException(nameof(song));
        }

        var settings = options == null ? new SunoPlayerOptions() : options.Clone();
        var instruments = BuildInstruments(song, settings);
        var player = new MultiTrackPlayer
        {
            AutoSetRelativeTrackLevels = settings.AutoSetRelativeTrackLevels,
        };

        try
        {
            foreach (var stem in song.Stems)
            {
                var track = BuildTrack(song, stem, settings, instruments);
                if (track != null)
                {
                    player.Add(track);
                }
            }
        }
        catch
        {
            player.Dispose();
            throw;
        }

        return player;
    }

    // The instrument every MIDI stem is played through, or null when there is nothing to play one
    // through. The SoundFont is loaded HERE, once, so that the factory the tracks hold shares it.
    private static Func<SunoStem, int, IMidiSynthesizer> BuildInstruments(SunoSong song,
        SunoPlayerOptions settings)
    {
        if (settings.InstrumentFactory != null)
        {
            return settings.InstrumentFactory;
        }

        var path = settings.GeneralMidiSoundFontPath;
        if (string.IsNullOrWhiteSpace(path))
        {
            path = song.Options == null ? null : song.Options.GeneralMidiSoundFontPath;
        }

        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var cache = settings.SoundFontCache ?? SunoPlayerOptions.SharedSoundFonts;
        var soundFont = cache.Get(path);
        return (stem, sampleRate) => new SoundFontSynthesizer(soundFont, sampleRate);
    }

    private static PlayerTrack BuildTrack(SunoSong song, SunoStem stem, SunoPlayerOptions settings,
        Func<SunoStem, int, IMidiSynthesizer> instruments)
    {
        var wantsMidi = settings.IncludeMidiSources && stem.Midi != null && instruments != null;

        PlayerTrack track;
        if (stem.HasAudio)
        {
            track = InMemory(song)
                ? new AudioTrack(stem.OpenAudio(), leaveOpen: false, name: stem.Name)
                : new AudioTrack(stem.GetAudioPath(), stem.Name);

            if (wantsMidi)
            {
                track.SetMidiSource(stem.Midi, sampleRate => instruments(stem, sampleRate));
            }
        }
        else if (wantsMidi)
        {
            track = new MidiTrack(stem.Midi, sampleRate => instruments(stem, sampleRate), stem.Name);
        }
        else
        {
            // Nothing to play: a MIDI-only stem with no instrument to play it through.
            return null;
        }

        ApplyStemSettings(song, stem, settings, track);
        return track;
    }

    // Everything a stem says about how its part should be played, whichever source is heard.
    private static void ApplyStemSettings(SunoSong song, SunoStem stem, SunoPlayerOptions settings,
        PlayerTrack track)
    {
        if (stem.GmProgram >= 0 && stem.GmProgram <= 127)
        {
            track.GmProgram = stem.GmProgram;
        }

        track.IsPercussion = stem.IsPercussion;
        track.MinimumNoteHold = song.Options == null
            ? PlayerTrack.DefaultMinimumNoteHold
            : song.Options.MinimumNoteHold;
        track.IgnoreNoteOff = settings.IgnoreNoteOffOnPercussion && stem.IsPercussion;

        if (settings.ApplyAlignmentOffsets)
        {
            track.MidiSourceOffset = stem.AlignmentOffset;
        }
    }

    private static bool InMemory(SunoSong song) =>
        song.Source == SunoSourceKind.ZipArchive &&
        song.Options != null &&
        song.Options.ZipExtraction == SunoZipExtraction.Memory;
}
