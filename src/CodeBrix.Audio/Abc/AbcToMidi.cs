using System;
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.Audio.Abc.Internal;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.Abc;

/// <summary>
/// Turns a parsed abc tune into the editable MIDI model.
/// </summary>
/// <remarks>
/// <para>
/// The result is a type 1 <see cref="MidiEventCollection"/>: track 0 is the conductor, carrying the
/// tempo, the time signature, the key signature, the title as a sequence track name and the composer
/// as a text event; then one track per voice, in the tune's voice order, each carrying its voice
/// name, its program change if the tune asked for one, and its notes.
/// <see cref="MidiEventCollection.PrepareForExport"/> has already been applied, so the collection
/// goes straight to <see cref="MidiFile.Export(string, MidiEventCollection)"/> or to
/// <c>MidiSequence.FromEvents</c>.
/// </para>
/// <para>
/// REPEATS ARE UNROLLED HERE, because MIDI has none: a <c>|: ... :|</c> section appears twice in the
/// events, and numbered endings select which bars belong to which pass.
/// </para>
/// <para>
/// TIMING IS EXACT. Every position and every length is carried as a fraction of a whole note - an
/// <see cref="AbcDuration"/> - and turned into ticks once, at the moment an event is created, from
/// the accumulated exact position. Nothing is ever accumulated in ticks, so a triplet cannot push
/// the rest of the tune out of place. Where a tuplet does not land on a whole tick at the chosen
/// resolution, that is said once in the tune's <see cref="AbcTune.Problems"/>.
/// </para>
/// <para>
/// The conversion ADDS to <see cref="AbcTune.Problems"/> the things only it can know - a tuplet that
/// does not divide, a tie between two different pitches, a note outside the MIDI range, more voices
/// than there are channels. Nothing is thrown for content.
/// </para>
/// </remarks>
public static class AbcToMidi
{
    // The channels voices take when nothing says otherwise: 1 upwards, with 10 left for percussion.
    private static readonly int[] AutomaticChannels = [1, 2, 3, 4, 5, 6, 7, 8, 9, 11, 12, 13, 14, 15, 16];

    /// <summary>
    /// Converts a tune to MIDI with the default options.
    /// </summary>
    /// <param name="tune">The tune to convert.</param>
    /// <returns>A type 1 collection, ready to export or play.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tune"/> is null.</exception>
    public static MidiEventCollection Convert(AbcTune tune) => Convert(tune, null);

    /// <summary>
    /// Converts a tune to MIDI.
    /// </summary>
    /// <param name="tune">The tune to convert.</param>
    /// <param name="options">
    /// How to convert it, or <see langword="null"/> for the defaults. The options are copied, so
    /// changing them afterwards does not change the collection.
    /// </param>
    /// <returns>A type 1 collection, ready to export or play.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="tune"/> is null.</exception>
    public static MidiEventCollection Convert(AbcTune tune, AbcToMidiOptions options)
    {
        if (tune == null)
        {
            throw new ArgumentNullException(nameof(tune));
        }

        var settings = options == null ? new AbcToMidiOptions() : options.Clone();
        var collection = new MidiEventCollection(1, settings.TicksPerQuarterNote);
        var conductor = new ConductorTrack(collection, settings.TicksPerQuarterNote);

        WriteConductorHeader(tune, settings, conductor);

        var channels = AssignChannels(tune, settings);
        for (int i = 0; i < tune.Voices.Count; i++)
        {
            WriteVoice(tune, tune.Voices[i], i + 1, channels[i], settings, collection, conductor);
        }

        collection.PrepareForExport();
        return collection;
    }

    private static void WriteConductorHeader(AbcTune tune, AbcToMidiOptions options, ConductorTrack conductor)
    {
        if (tune.Title.Length > 0)
        {
            conductor.Add(new TextEvent(tune.Title, MetaEventType.SequenceTrackName, 0));
        }

        if (tune.Composer.Length > 0)
        {
            conductor.Add(new TextEvent(tune.Composer, MetaEventType.TextEvent, 0));
        }

        conductor.WriteTempo(TempoOf(tune.Tempo, options), 0);
        conductor.WriteMeter(tune.Meter, 0, tune);
        conductor.WriteKey(tune.Key, 0);
    }

    private static double TempoOf(AbcTempo tempo, AbcToMidiOptions options)
    {
        if (tempo == null || !tempo.HasValue)
        {
            return options.DefaultBeatsPerMinute;
        }

        double quarters = tempo.QuarterNotesPerMinute;
        return quarters > 0.0 ? quarters : options.DefaultBeatsPerMinute;
    }

    // The caller's map wins over a %%MIDI channel directive, which wins over the automatic
    // assignment. The automatic one follows the voice's POSITION rather than a running counter, so
    // giving one voice a channel of its own does not shift every voice after it.
    private static int[] AssignChannels(AbcTune tune, AbcToMidiOptions options)
    {
        var channels = new int[tune.Voices.Count];
        for (int i = 0; i < tune.Voices.Count; i++)
        {
            var voice = tune.Voices[i];

            if (options.VoiceChannels.TryGetValue(voice.Id, out int chosen) && chosen >= 1 && chosen <= 16)
            {
                channels[i] = chosen;
                continue;
            }

            if (options.HonourMidiDirectives && voice.MidiChannel.HasValue)
            {
                channels[i] = voice.MidiChannel.Value;
                continue;
            }

            channels[i] = AutomaticChannels[i % AutomaticChannels.Length];
        }

        if (tune.Voices.Count > AutomaticChannels.Length)
        {
            tune.AddProblem(
                $"The tune has {tune.Voices.Count} voices and there are only {AutomaticChannels.Length} channels to give them; channels were reused.");
        }

        return channels;
    }

    private static void WriteVoice(
        AbcTune tune,
        AbcVoice voice,
        int track,
        int channel,
        AbcToMidiOptions options,
        MidiEventCollection collection,
        ConductorTrack conductor)
    {
        string trackName = voice.Name.Length > 0 ? voice.Name : voice.Id;
        if (trackName.Length > 0)
        {
            collection.AddEvent(new TextEvent(trackName, MetaEventType.SequenceTrackName, 0), track);
        }

        var writer = new VoiceWriter(tune, voice, track, channel, options, collection, conductor);
        writer.Run();
    }

    // Track 0. It also owns the de-duplication: the standard asks a transcriber to repeat an inline
    // M: or Q: in every voice it applies to, and MIDI holds one tempo map for the whole file, so the
    // same change arriving from three voices must be written once.
    private sealed class ConductorTrack
    {
        private readonly MidiEventCollection _collection;
        private readonly int _ticksPerQuarterNote;
        private readonly HashSet<string> _written = [];
        private readonly Dictionary<long, TempoEvent> _temposAtTick = [];

        internal ConductorTrack(MidiEventCollection collection, int ticksPerQuarterNote)
        {
            _collection = collection;
            _ticksPerQuarterNote = ticksPerQuarterNote;
        }

        internal int TicksPerQuarterNote => _ticksPerQuarterNote;

        internal void Add(MidiEvent midiEvent) => _collection.AddEvent(midiEvent, 0);

        internal void WriteTempo(double quarterNotesPerMinute, long tick)
        {
            if (quarterNotesPerMinute <= 0.0)
            {
                return;
            }

            int microseconds = (int)Math.Round(60000000.0 / quarterNotesPerMinute, MidpointRounding.AwayFromZero);
            if (microseconds < 1)
            {
                microseconds = 1;
            }

            // The header's default and an inline Q: at the start of the body can share tick zero.
            // A repeated Q: in another voice can share any tick. The last value at that tick wins.
            if (_temposAtTick.TryGetValue(tick, out var existing))
            {
                existing.MicrosecondsPerQuarterNote = microseconds;
                return;
            }

            var tempo = new TempoEvent(microseconds, tick);
            _temposAtTick.Add(tick, tempo);
            Add(tempo);
        }

        internal void WriteMeter(AbcMeter meter, long tick, AbcTune tune)
        {
            if (meter == null || meter.IsFree || meter.Denominator <= 0)
            {
                return;
            }

            int exponent = PowerOfTwoExponent(meter.Denominator);
            if (exponent < 0)
            {
                tune.AddProblem(
                    $"The meter {meter} has a denominator that is not a power of two, which a MIDI time signature cannot carry; no time signature was written.");
                return;
            }

            if (!_written.Add(string.Create(CultureInfo.InvariantCulture, $"M{tick}:{meter.Numerator}/{exponent}")))
            {
                return;
            }

            Add(new TimeSignatureEvent(tick, meter.Numerator, exponent, 24, 8));
        }

        internal void WriteKey(AbcKey key, long tick)
        {
            if (key == null || key.HasNoSignature)
            {
                return;
            }

            if (!_written.Add(string.Create(CultureInfo.InvariantCulture, $"K{tick}:{key.SharpsFlats}:{key.MajorMinor}")))
            {
                return;
            }

            Add(new KeySignatureEvent(key.SharpsFlats, key.MajorMinor, tick));
        }

        private static int PowerOfTwoExponent(int value)
        {
            for (int exponent = 0; exponent <= 15; exponent++)
            {
                if (1 << exponent == value)
                {
                    return exponent;
                }
            }

            return -1;
        }
    }

    // One voice's walk from the first bar to the last. It is a class rather than a method because
    // the walk carries a good deal of state - the exact position, the accidentals in force in this
    // bar, the notes held open by ties, the grace notes waiting for the note they decorate - and
    // threading all of that through a static method makes it unreadable.
    private sealed class VoiceWriter
    {
        private readonly AbcTune _tune;
        private readonly AbcVoice _voice;
        private readonly int _track;
        private readonly int _channel;
        private readonly AbcToMidiOptions _options;
        private readonly MidiEventCollection _collection;
        private readonly ConductorTrack _conductor;

        private readonly Dictionary<char, AbcAccidental> _barAccidentals = [];
        private readonly Dictionary<int, HeldNote> _held = [];
        private readonly List<AbcNote> _pendingGraces = [];

        private AbcDuration _position = AbcDuration.Zero;
        private AbcKey _key;

        internal VoiceWriter(
            AbcTune tune,
            AbcVoice voice,
            int track,
            int channel,
            AbcToMidiOptions options,
            MidiEventCollection collection,
            ConductorTrack conductor)
        {
            _tune = tune;
            _voice = voice;
            _track = track;
            _channel = channel;
            _options = options;
            _collection = collection;
            _conductor = conductor;
            _key = tune.Key;
        }

        internal void Run()
        {
            if (_options.HonourMidiDirectives && _voice.MidiProgram.HasValue)
            {
                // A tune that declared its instrument in the header, before any voice, has no
                // program-change element to sit at a point in the music; it starts on that sound.
                bool hasElement = false;
                for (int b = 0; b < _voice.Bars.Count && !hasElement; b++)
                {
                    var elements = _voice.Bars[b].Elements;
                    for (int e = 0; e < elements.Count; e++)
                    {
                        if (elements[e] is AbcProgramChange)
                        {
                            hasElement = true;
                            break;
                        }
                    }
                }

                if (!hasElement)
                {
                    _collection.AddEvent(new PatchChangeEvent(0, _channel, _voice.MidiProgram.Value), _track);
                }
            }

            var bars = AbcRepeatUnroller.Unroll(_voice.Bars, _tune);
            for (int i = 0; i < bars.Count; i++)
            {
                _barAccidentals.Clear();
                WriteElements(bars[i].Elements, AbcDuration.Whole);
            }

            FlushGraces();
            FlushHeld("A tie at the end of a voice had no note to join to; the note was played on its own.");
        }

        private void WriteElements(IReadOnlyList<AbcElement> elements, AbcDuration scale)
        {
            for (int i = 0; i < elements.Count; i++)
            {
                WriteElement(elements[i], scale);
            }
        }

        private void WriteElement(AbcElement element, AbcDuration scale)
        {
            if (element is AbcNote note)
            {
                BreakTiesNotIn([ResolvePitch(note)]);
                TakeGraceTimeFrom(note.Length * scale, out AbcDuration stolen);
                WriteNote(note, (note.Length * scale) - stolen, note.Length * scale, note.TiedToNext);
                return;
            }

            if (element is AbcChord chord)
            {
                var pitches = new int[chord.Notes.Count];
                for (int i = 0; i < chord.Notes.Count; i++)
                {
                    pitches[i] = ResolvePitch(chord.Notes[i]);
                }

                BreakTiesNotIn(pitches);
                var total = chord.Length * scale;
                TakeGraceTimeFrom(total, out AbcDuration stolen);

                var start = _position + stolen;
                var sounding = total - stolen;
                for (int i = 0; i < chord.Notes.Count; i++)
                {
                    HoldOrEmit(pitches[i], start, sounding, chord.TiedToNext || chord.Notes[i].TiedToNext);
                }

                _position += total;
                return;
            }

            if (element is AbcRest rest)
            {
                BreakTiesNotIn([]);
                // Grace notes before a rest have nothing to take their time from, so they sound at
                // their own length and the rest keeps its place.
                FlushGraces();
                _position += rest.Length * scale;
                return;
            }

            if (element is AbcGraceGroup grace)
            {
                for (int i = 0; i < grace.Notes.Count; i++)
                {
                    _pendingGraces.Add(grace.Notes[i]);
                }

                return;
            }

            if (element is AbcTupletGroup tuplet)
            {
                var ratio = tuplet.Ratio * scale;
                CheckTupletResolution(tuplet, ratio);
                WriteElements(tuplet.Elements, ratio);
                return;
            }

            if (element is AbcInlineField field)
            {
                WriteInlineField(field);
                return;
            }

            if (element is AbcProgramChange program)
            {
                if (!_options.HonourMidiDirectives)
                {
                    return;
                }

                int channel = program.Channel >= 1 && program.Channel <= 16 ? program.Channel : _channel;
                _collection.AddEvent(
                    new PatchChangeEvent(_position.ToTicks(_conductor.TicksPerQuarterNote), channel, program.Program),
                    _track);
            }
        }

        private void WriteInlineField(AbcInlineField field)
        {
            long tick = _position.ToTicks(_conductor.TicksPerQuarterNote);

            if (field.Key != null)
            {
                _key = field.Key;
                _barAccidentals.Clear();
                _conductor.WriteKey(field.Key, tick);
                return;
            }

            if (field.Meter != null)
            {
                _conductor.WriteMeter(field.Meter, tick, _tune);
                return;
            }

            if (field.Tempo != null && field.Tempo.HasValue)
            {
                _conductor.WriteTempo(field.Tempo.QuarterNotesPerMinute, tick);
            }

            // An inline L: changes what a bare letter means, and the reader has already applied it
            // to every length that follows, so there is nothing left to write here.
        }

        private void WriteNote(AbcNote note, AbcDuration sounding, AbcDuration advance, bool tied)
        {
            int pitch = ResolvePitch(note);
            var start = _position + (advance - sounding);
            HoldOrEmit(pitch, start, sounding, tied);
            _position += advance;
        }

        // A tie does not sound twice: the two notes become one, so a tied note is HELD until the
        // note it joins to arrives and the pair is written as a single longer note.
        private void HoldOrEmit(int pitch, AbcDuration start, AbcDuration length, bool tied)
        {
            if (_held.TryGetValue(pitch, out HeldNote held))
            {
                _held.Remove(pitch);
                start = held.Start;
                length = held.Length + length;
            }

            if (tied)
            {
                _held[pitch] = new HeldNote(start, length);
                return;
            }

            Emit(pitch, start, length);
        }

        private void BreakTiesNotIn(IReadOnlyList<int> pitches)
        {
            if (_held.Count == 0)
            {
                return;
            }

            List<int> orphaned = null;
            foreach (var pair in _held)
            {
                bool found = false;
                for (int i = 0; i < pitches.Count; i++)
                {
                    if (pitches[i] == pair.Key)
                    {
                        found = true;
                        break;
                    }
                }

                if (!found)
                {
                    orphaned ??= [];
                    orphaned.Add(pair.Key);
                }
            }

            if (orphaned == null)
            {
                return;
            }

            for (int i = 0; i < orphaned.Count; i++)
            {
                var held = _held[orphaned[i]];
                _held.Remove(orphaned[i]);
                Emit(orphaned[i], held.Start, held.Length);
            }

            _tune.AddProblem("A tie joined notes of different pitches; they were played as separate notes.");
        }

        private void FlushHeld(string problem)
        {
            if (_held.Count == 0)
            {
                return;
            }

            foreach (var pair in _held)
            {
                Emit(pair.Key, pair.Value.Start, pair.Value.Length);
            }

            _held.Clear();
            _tune.AddProblem(problem);
        }

        // Grace notes take their time from the note they precede: each lasts GraceNoteLength, the
        // note starts that much later, and it is shortened by the same amount. They never take more
        // than half of it, however many there are.
        private void TakeGraceTimeFrom(AbcDuration following, out AbcDuration stolen)
        {
            stolen = AbcDuration.Zero;
            if (_pendingGraces.Count == 0)
            {
                return;
            }

            var each = _options.GraceNoteLength;
            var total = each * _pendingGraces.Count;
            var half = following / 2;
            if (total > half)
            {
                each = half / _pendingGraces.Count;
                total = each * _pendingGraces.Count;
            }

            var start = _position;
            for (int i = 0; i < _pendingGraces.Count; i++)
            {
                Emit(ResolvePitch(_pendingGraces[i]), start, each);
                start += each;
            }

            _pendingGraces.Clear();
            stolen = total;
        }

        // Grace notes with nothing after them to take time from sound at their own length, and
        // whatever follows keeps its place.
        private void FlushGraces()
        {
            if (_pendingGraces.Count == 0)
            {
                return;
            }

            var each = _options.GraceNoteLength;
            var start = _position;
            for (int i = 0; i < _pendingGraces.Count; i++)
            {
                Emit(ResolvePitch(_pendingGraces[i]), start, each);
                start += each;
            }

            _pendingGraces.Clear();
        }

        private void CheckTupletResolution(AbcTupletGroup tuplet, AbcDuration ratio)
        {
            for (int i = 0; i < tuplet.Elements.Count; i++)
            {
                AbcDuration length;
                if (tuplet.Elements[i] is AbcNote note)
                {
                    length = note.Length;
                }
                else if (tuplet.Elements[i] is AbcChord chord)
                {
                    length = chord.Length;
                }
                else if (tuplet.Elements[i] is AbcRest rest)
                {
                    length = rest.Length;
                }
                else
                {
                    continue;
                }

                if (!(length * ratio).IsWholeTicks(_conductor.TicksPerQuarterNote))
                {
                    _tune.AddProblem(
                        $"A {tuplet.NotesInTuplet}-note tuplet does not divide into whole ticks at {_conductor.TicksPerQuarterNote} ticks per quarter note; its notes were rounded to the nearest tick.");
                    return;
                }
            }
        }

        private void Emit(int pitch, AbcDuration start, AbcDuration length)
        {
            int ticksPerQuarterNote = _conductor.TicksPerQuarterNote;
            long startTick = start.ToTicks(ticksPerQuarterNote);
            long endTick = (start + length).ToTicks(ticksPerQuarterNote);
            long duration = endTick - startTick;
            if (duration < 1)
            {
                duration = 1;
            }

            var noteOn = new NoteOnEvent(startTick, _channel, pitch, _options.Velocity, (int)duration);
            _collection.AddEvent(noteOn, _track);
            _collection.AddEvent(noteOn.OffEvent, _track);
        }

        // The standard's DEFAULT accidental rule is %%propagate-accidentals pitch: an accidental
        // holds for the same note letter in EVERY octave until the end of the bar. A written natural
        // cancels both that and the key signature.
        private int ResolvePitch(AbcNote note)
        {
            char letter = char.ToUpperInvariant(note.Letter);
            AbcAccidental accidental = note.Accidental;

            if (accidental == AbcAccidental.None)
            {
                if (!_barAccidentals.TryGetValue(letter, out accidental))
                {
                    accidental = _key.AccidentalFor(letter);
                }
            }
            else
            {
                _barAccidentals[letter] = accidental;
            }

            int pitch = AbcNote.NaturalSemitone(letter) + SemitoneOffset(accidental) + (note.Octave * 12) + 60;
            if (pitch < 0 || pitch > 127)
            {
                _tune.AddProblem(
                    "A note fell outside the MIDI range of 0 to 127; it was moved to the nearest note inside it.");
                pitch = pitch < 0 ? pitch % 12 : 120 + (pitch % 12);
                if (pitch < 0)
                {
                    pitch += 12;
                }

                if (pitch > 127)
                {
                    pitch -= 12;
                }
            }

            return pitch;
        }

        private static int SemitoneOffset(AbcAccidental accidental)
        {
            switch (accidental)
            {
                case AbcAccidental.DoubleFlat: return -2;
                case AbcAccidental.Flat: return -1;
                case AbcAccidental.Sharp: return 1;
                case AbcAccidental.DoubleSharp: return 2;
                default: return 0;
            }
        }

        private readonly struct HeldNote
        {
            internal HeldNote(AbcDuration start, AbcDuration length)
            {
                Start = start;
                Length = length;
            }

            internal AbcDuration Start { get; }

            internal AbcDuration Length { get; }
        }
    }
}
