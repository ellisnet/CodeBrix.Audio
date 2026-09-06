using System;
using CodeBrix.Audio.Playback.Suno;

namespace CodeBrix.Audio.Tests.Midi;

/// <summary>
/// A hand-built MIDI file with every departure from the specification that a stems export is known
/// to contain, so that both readers can be held to the same fixture. Synthetic throughout: the
/// shape is copied, no corpus bytes are.
/// </summary>
internal static class SunoShapedMidi
{
    /// <summary>A title with characters outside the Basic Multilingual Plane, as such exports have.</summary>
    internal const string Title = "\U0001F344\U0001F48E⛪ Fake Song";

    /// <summary>The track name the exporter would write for the drum stem of that song.</summary>
    internal const string TrackName = Title + " (Drums)";

    /// <summary>The sharps/flats byte such an export writes, which the specification does not allow.</summary>
    internal const int KeySignatureSharpsFlats = 13;

    /// <summary>A meta event type this package does not model.</summary>
    internal const byte UnknownMetaType = 0x60;

    /// <summary>The bytes of the mangled track-name meta event.</summary>
    internal static byte[] MangledTrackName() => SunoTitleCodec.Encode(TrackName);

    /// <summary>
    /// Builds the file: a tempo-map track carrying the mangled name and the out-of-range key
    /// signature, and a drum track with zero-length notes, an unmodelled meta event and one note
    /// that is never released.
    /// </summary>
    /// <returns>The bytes of a complete Standard MIDI File.</returns>
    internal static byte[] Bytes()
    {
        var name = MangledTrackName();

        var conductor = new MidiTrackBuilder()
            .TrackName(0, name)
            .KeySignature(0, KeySignatureSharpsFlats, 0)
            .Tempo(0, 500000)
            .Tempo(SyntheticMidi.Division, 498000)
            .Tempo(SyntheticMidi.Division, 502000)
            .EndOfTrack(SyntheticMidi.Division);

        var drums = new MidiTrackBuilder()
            .TrackName(0, name)
            .ProgramChange(0, 10, 118)
            .ControlChange(0, 10, 7, 100)
            .NoteOn(0, 10, 36, 90)
            .NoteOff(1, 10, 36, 0)
            .Meta(0, UnknownMetaType, 0x01, 0x02, 0x03)
            .NoteOn(SyntheticMidi.Division - 1, 10, 38, 70)
            .NoteOff(1, 10, 38, 0)
            .NoteOn(SyntheticMidi.Division, 10, 42, 64) // never released
            .EndOfTrack(SyntheticMidi.Division);

        return SyntheticMidi.File(1, SyntheticMidi.Division, conductor.ToChunk(), drums.ToChunk());
    }

    /// <summary>The same two tracks with nothing wrong with them, as a control.</summary>
    /// <returns>The bytes of a complete Standard MIDI File.</returns>
    internal static byte[] CleanBytes()
    {
        var conductor = new MidiTrackBuilder()
            .TrackName(0, "Fake Song"u8.ToArray())
            .KeySignature(0, 2, 0)
            .Tempo(0, 500000)
            .EndOfTrack(SyntheticMidi.Division);

        var drums = new MidiTrackBuilder()
            .TrackName(0, "Fake Song (Drums)"u8.ToArray())
            .ProgramChange(0, 10, 118)
            .NoteOn(0, 10, 36, 90)
            .NoteOff(1, 10, 36, 0)
            .EndOfTrack(SyntheticMidi.Division);

        return SyntheticMidi.File(1, SyntheticMidi.Division, conductor.ToChunk(), drums.ToChunk());
    }
}
