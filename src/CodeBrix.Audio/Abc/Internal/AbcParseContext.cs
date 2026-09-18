using System.Collections.Generic;

namespace CodeBrix.Audio.Abc.Internal;

// The running list of things an abc read could not honour, with the two habits the rest of this
// package's readers already have:
//
//   - the list is CAPPED. A file of noise can otherwise produce a problem per character, and an
//     unbounded list turns a bad file into a memory problem. Same cap, and the same closing line,
//     as Midi/Internal/MidiReadContext.
//   - a KIND is reported ONCE. Abc is full of things a playback reader ignores - decorations, chord
//     symbols, annotations, lyrics - and a real tune carries hundreds of them. One line saying
//     decorations were ignored is information; four hundred lines saying it is noise, and it would
//     push everything else off the end of a capped list.
internal sealed class AbcParseContext
{
    internal const int MaximumProblems = 200;

    private readonly List<string> _problems = [];
    private readonly HashSet<string> _reportedKinds = [];
    private bool _truncated;

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

    // Reports a problem the first time its KIND is seen and never again.
    internal void AddOnce(string kind, string problem)
    {
        if (_reportedKinds.Add(kind))
        {
            Add(problem);
        }
    }

    internal bool HasReported(string kind) => _reportedKinds.Contains(kind);
}
