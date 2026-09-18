using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.Abc.Internal;

// Plays a voice's repeats out into the bars that are actually heard.
//
// MIDI has no repeat marks, so somebody has to decide what a "|:" and a ":|" and a "[1" mean in
// time, and it is this. The rules are the standard's:
//
//   - "|: ... :|" plays its section twice; "|:: ... ::|" three times, and so on.
//   - numbered endings select which bars belong to which pass, in either spelling ("[1" and "|1"),
//     and an ending mark can carry a list or a range ("[1,3", "[1-3").
//   - a ":|" with no "|:" before it repeats from the START of the tune - or, once a section has
//     already ended, from the end of that section, which is the same rule one level down.
//
// Where the standard says nothing - a "|:" that is never closed - the section is played once and a
// problem is reported, because silently doubling music nobody asked to repeat is worse than not
// repeating it.
internal static class AbcRepeatUnroller
{
    internal static List<AbcBar> Unroll(IReadOnlyList<AbcBar> bars, AbcTune tune)
    {
        var result = new List<AbcBar>();
        if (bars == null || bars.Count == 0)
        {
            return result;
        }

        int sectionStart = 0;
        while (sectionStart < bars.Count)
        {
            // Where the next repeated section begins; this one cannot reach past it.
            int nextStart = bars.Count;
            for (int j = sectionStart + 1; j < bars.Count; j++)
            {
                if (bars[j].OpeningBarLine.StartsRepeat)
                {
                    nextStart = j;
                    break;
                }
            }

            // Where this section is repeated from.
            int repeatEnd = -1;
            for (int j = sectionStart; j < nextStart; j++)
            {
                if (bars[j].ClosingBarLine.EndsRepeat)
                {
                    repeatEnd = j;
                    break;
                }
            }

            if (repeatEnd < 0)
            {
                if (bars[sectionStart].OpeningBarLine.StartsRepeat)
                {
                    tune.AddProblem("A repeat was opened and never closed; the section was played once.");
                }

                for (int j = sectionStart; j < nextStart; j++)
                {
                    result.Add(bars[j]);
                }

                sectionStart = nextStart;
                continue;
            }

            int firstEnding = -1;
            for (int j = sectionStart; j <= repeatEnd; j++)
            {
                if (bars[j].Endings.Count > 0)
                {
                    firstEnding = j;
                    break;
                }
            }

            if (firstEnding < 0)
            {
                int plainPasses = Math.Max(2, bars[repeatEnd].ClosingBarLine.RepeatCount);
                for (int pass = 0; pass < plainPasses; pass++)
                {
                    for (int j = sectionStart; j <= repeatEnd; j++)
                    {
                        result.Add(bars[j]);
                    }
                }

                sectionStart = repeatEnd + 1;
                continue;
            }

            var blocks = CollectEndingBlocks(bars, firstEnding);

            int highestEnding = 0;
            for (int i = 0; i < blocks.Count; i++)
            {
                var numbers = blocks[i].Endings;
                for (int n = 0; n < numbers.Count; n++)
                {
                    if (numbers[n] > highestEnding)
                    {
                        highestEnding = numbers[n];
                    }
                }
            }

            int passes = Math.Max(Math.Max(2, bars[repeatEnd].ClosingBarLine.RepeatCount), highestEnding);

            for (int pass = 1; pass <= passes; pass++)
            {
                for (int j = sectionStart; j < firstEnding; j++)
                {
                    result.Add(bars[j]);
                }

                for (int i = 0; i < blocks.Count; i++)
                {
                    if (!ContainsNumber(blocks[i].Endings, pass))
                    {
                        continue;
                    }

                    for (int j = blocks[i].Start; j <= blocks[i].End; j++)
                    {
                        result.Add(bars[j]);
                    }

                    break;
                }
            }

            sectionStart = blocks.Count > 0 ? blocks[blocks.Count - 1].End + 1 : repeatEnd + 1;
        }

        return result;
    }

    // An ending block starts at a bar carrying an ending mark and runs until the bar that closes a
    // repeat, until just before the next ending mark, or until the next section begins.
    private static List<EndingBlock> CollectEndingBlocks(IReadOnlyList<AbcBar> bars, int firstEnding)
    {
        var blocks = new List<EndingBlock>();
        int start = firstEnding;

        while (start < bars.Count && bars[start].Endings.Count > 0)
        {
            int end = start;
            while (true)
            {
                if (bars[end].ClosingBarLine.EndsRepeat)
                {
                    break;
                }

                if (end + 1 >= bars.Count)
                {
                    break;
                }

                if (bars[end + 1].Endings.Count > 0)
                {
                    break;
                }

                if (bars[end + 1].OpeningBarLine.StartsRepeat)
                {
                    break;
                }

                end++;
            }

            blocks.Add(new EndingBlock(start, end, bars[start].Endings));
            start = end + 1;
        }

        return blocks;
    }

    private readonly struct EndingBlock
    {
        internal EndingBlock(int start, int end, IReadOnlyList<int> endings)
        {
            Start = start;
            End = end;
            Endings = endings;
        }

        internal int Start { get; }

        internal int End { get; }

        internal IReadOnlyList<int> Endings { get; }
    }

    private static bool ContainsNumber(IReadOnlyList<int> numbers, int value)
    {
        for (int i = 0; i < numbers.Count; i++)
        {
            if (numbers[i] == value)
            {
                return true;
            }
        }

        return false;
    }
}
