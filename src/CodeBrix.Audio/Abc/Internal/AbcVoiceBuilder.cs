using System.Collections.Generic;

namespace CodeBrix.Audio.Abc.Internal;

// One voice while it is still being read. Every piece of state that a voice carries independently
// of the others lives here, because abc lets voices interleave bar by bar - "[V:T1] ... [V:T2] ..."
// on alternating lines - so a voice's half-finished bar, its open tuplet and its pending tie all
// have to survive while another voice is being read.
internal sealed class AbcVoiceBuilder
{
    internal AbcVoiceBuilder(string id)
    {
        Id = id ?? string.Empty;
        Name = string.Empty;
        Bars = [];
        Current = [];
        Opening = AbcBarLine.None;
        PendingEndings = [];
        PendingBroken = AbcDuration.Whole;
        LastIndex = -1;
    }

    internal string Id { get; }

    internal string Name { get; set; }

    internal int? MidiProgram { get; set; }

    internal int? MidiChannel { get; set; }

    internal List<AbcBar> Bars { get; }

    // The bar being built: its contents, the bar line that opened it, and the endings it belongs to.
    internal List<AbcElement> Current { get; }

    internal AbcBarLine Opening { get; set; }

    internal List<int> PendingEndings { get; }

    // Where the last note, rest or chord went, so that a tie or a broken-rhythm marker written
    // after it can replace it. Both look BACKWARDS at the element already written, which is why the
    // position is kept rather than the element.
    internal List<AbcElement> LastList { get; set; }

    internal int LastIndex { get; set; }

    // The multiplier a broken-rhythm marker left for the NEXT note, rest or chord.
    internal AbcDuration PendingBroken { get; set; }

    // Chord state: notes collected between '[' and ']'.
    internal List<AbcNote> ChordNotes { get; set; }

    // Grace state: notes collected between '{' and '}'.
    internal List<AbcNote> GraceNotes { get; set; }

    internal bool GraceIsAcciaccatura { get; set; }

    // Tuplet state: the p, q and r of the open tuplet and what it has collected so far.
    internal int TupletP { get; set; }

    internal int TupletQ { get; set; }

    internal int TupletRemaining { get; set; }

    internal int TupletCovered { get; set; }

    internal List<AbcElement> TupletElements { get; set; }

    internal bool InChord => ChordNotes != null;

    internal bool InGrace => GraceNotes != null;

    internal bool InTuplet => TupletElements != null;
}
