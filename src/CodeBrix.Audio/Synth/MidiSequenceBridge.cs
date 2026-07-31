using System;
using System.IO;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.Synth;

// The bridge from the editable MIDI file model to the immutable playback sequence.
//
// This package has two MIDI representations, on purpose:
//   CodeBrix.Audio.Midi.MidiFile       the editable file model - read, edit, write
//   CodeBrix.Audio.Synth.MidiSequence  the immutable decoded sequence - play
//
// FromEvents turns the first into the second, so a sequence can be built in code, or loaded
// and edited, and then heard. There is deliberately no conversion the other way: a
// MidiSequence has already flattened its tracks into one absolute-time message stream and
// discarded everything that does not affect playback - track structure, text, key and time
// signatures. Converting back would silently drop all of it. To edit, keep the
// MidiEventCollection; it is the lossless representation.
//
// This file is NOT part of the MeltySynth port; it is CodeBrix code added alongside it.
public sealed partial class MidiSequence
{
    /// <summary>
    /// Builds a playable sequence from an editable MIDI event collection.
    /// </summary>
    /// <param name="events">
    /// The event collection to convert. Each track should end with an end-of-track meta event, as a
    /// well-formed MIDI file does.
    /// </param>
    /// <param name="loopType">How to interpret loop markers in the events, if any.</param>
    /// <returns>An immutable sequence ready for <see cref="MidiSequencer"/> or a music player.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="events"/> is null.</exception>
    /// <exception cref="ArgumentException">The collection is a type 0 file with more than one track.</exception>
    /// <exception cref="InvalidDataException">The resulting MIDI data could not be decoded.</exception>
    /// <remarks>
    /// <para>
    /// The conversion goes through the standard MIDI file encoding rather than reaching into either
    /// model's internals. That keeps exactly one implementation of the tempo map and the track merge -
    /// the same one used for files on disk - rather than a second copy that could drift from it.
    /// </para>
    /// <para>
    /// There is no reverse conversion; see the note at the top of this file for why.
    /// </para>
    /// </remarks>
    public static MidiSequence FromEvents(
        MidiEventCollection events,
        MidiSequenceLoopType loopType = MidiSequenceLoopType.None)
    {
        if (events == null)
        {
            throw new ArgumentNullException(nameof(events));
        }

        using (var stream = new MemoryStream())
        {
            MidiFile.Export(stream, events, leaveOpen: true);
            stream.Position = 0;
            return new MidiSequence(stream, loopType);
        }
    }
}
