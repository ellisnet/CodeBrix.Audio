using System.Collections.Generic;

namespace CodeBrix.Audio.Midi.Internal;

// The state a lenient MIDI read carries around: which mode it is running in, and the running list
// of things that could not be honoured. Both parsers in this package - the editable MidiFile model
// and the immutable MidiSequence used for playback - share it so that their Problems lists read the
// same way.
//
// The list is capped. A corrupt file can otherwise produce a problem per byte, and an unbounded
// list turns a bad file into a memory problem.
internal sealed class MidiReadContext
{
    internal const int MaximumProblems = 200;

    private readonly List<string> _problems = [];
    private bool _truncated;

    internal MidiReadContext(MidiReadMode readMode)
    {
        ReadMode = readMode;
    }

    internal MidiReadMode ReadMode { get; }

    internal bool IsStrict => ReadMode == MidiReadMode.Strict;

    internal IReadOnlyList<string> Problems => _problems;

    internal void Add(string problem)
    {
        if (_problems.Count < MaximumProblems)
        {
            _problems.Add(problem);
            return;
        }

        if (!_truncated)
        {
            _truncated = true;
            _problems.Add($"Further problems were found but not recorded; the first {MaximumProblems} are listed.");
        }
    }
}
