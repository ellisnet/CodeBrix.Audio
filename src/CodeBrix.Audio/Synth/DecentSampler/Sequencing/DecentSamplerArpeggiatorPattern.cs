using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Sequencing;

// Turns the notes a player is holding into the note pattern the arpeggiator steps through. Nothing
// here touches time or audio, so the developer guide's nine worked examples are direct unit tests of
// this class.
//
// The guide's example, given as the reference for every order: the chord C4-E4-G4 (60, 64, 67) with
// arpOctaveRange="2" and arpOctaveMode="replayPerOctave" expands to the POOL 60, 64, 67, 72, 76, 79,
// and each arpOrder then reads that pool:
//     up                  60 64 67 72 76 79
//     down                79 76 72 67 64 60                (the exact reverse of up)
//     up_down             60 64 67 72 76 79 76 72 67 64    (turnaround notes played once)
//     up_down_inclusive   60 64 67 72 76 79 79 76 72 67 64 60
//     down_up             79 76 72 67 64 60 64 67 72 76
//     down_up_inclusive   79 76 72 67 64 60 60 64 67 72 76 79
//     as_played           the press order, replayed per octave (G, C, E -> 67 60 64 79 72 76)
//     random              a note drawn from the pool on every step
//     random_no_repeat    the same, never twice in a row
//
// The pool is built in the octave mode's own order and TRUNCATED to arpStepCount before the order is
// applied: the guide describes the ceiling as "if the held chord x octave range would produce more
// notes than this, the pattern is truncated to the first arpStepCount notes (built according to
// arpOctaveMode)", which is the note pool rather than the finished up-and-down pattern.
// UNMEASURED - the alternative reading caps the finished pattern instead.
internal static class DecentSamplerArpeggiatorPattern
{
    // The highest MIDI note an octave copy may reach. A copy above it is dropped rather than clamped,
    // because a clamped copy would sound the same note twice in one cycle. UNMEASURED.
    internal const int HighestKey = 127;

    // The lowest and highest arpStepCount the guide documents.
    internal const int MinimumStepCount = 1;
    internal const int MaximumStepCount = 16;

    // The lowest and highest arpOctaveRange the guide documents.
    internal const int MinimumOctaveRange = 1;
    internal const int MaximumOctaveRange = 8;

    // Builds the pattern for one set of held notes. Both lists are cleared first and neither
    // allocates once they have grown, so this may be called on the audio thread every step.
    internal static void Build(
        IReadOnlyList<DecentSamplerArpNote> held,
        DecentSamplerArpOrder order,
        int octaveRange,
        DecentSamplerArpOctaveMode octaveMode,
        int stepCount,
        List<DecentSamplerArpNote> pool,
        List<DecentSamplerArpNote> pattern)
    {
        pool.Clear();
        pattern.Clear();

        if (held == null || held.Count == 0)
        {
            return;
        }

        BuildPool(held, order, octaveRange, octaveMode, stepCount, pool);
        ApplyOrder(order, pool, pattern);
    }

    // The pool: the held notes in their base order, copied once per octave, then merged and sorted
    // when arpOctaveMode is interleaveByPitch, then truncated to the step count.
    private static void BuildPool(
        IReadOnlyList<DecentSamplerArpNote> held,
        DecentSamplerArpOrder order,
        int octaveRange,
        DecentSamplerArpOctaveMode octaveMode,
        int stepCount,
        List<DecentSamplerArpNote> pool)
    {
        var octaves = octaveRange < MinimumOctaveRange
            ? MinimumOctaveRange
            : octaveRange > MaximumOctaveRange ? MaximumOctaveRange : octaveRange;

        for (var index = 0; index < held.Count; index++)
        {
            pool.Add(held[index]);
        }

        // Every order but as_played reads the chord from the bottom up; as_played reads it in the
        // order the keys went down, and the guide says that order is then replayed per octave.
        if (order == DecentSamplerArpOrder.AsPlayed)
        {
            SortByPressOrder(pool, 0, pool.Count);
        }
        else
        {
            SortByPitch(pool, 0, pool.Count);
        }

        var baseCount = pool.Count;

        for (var octave = 1; octave < octaves; octave++)
        {
            var semitones = octave * 12;

            for (var index = 0; index < baseCount; index++)
            {
                var note = pool[index];

                if (note.Key + semitones > HighestKey)
                {
                    continue;
                }

                pool.Add(note.Transposed(semitones));
            }
        }

        if (octaveMode == DecentSamplerArpOctaveMode.InterleaveByPitch)
        {
            SortByPitch(pool, 0, pool.Count);
        }

        var ceiling = stepCount < MinimumStepCount
            ? MinimumStepCount
            : stepCount > MaximumStepCount ? MaximumStepCount : stepCount;

        if (pool.Count > ceiling)
        {
            pool.RemoveRange(ceiling, pool.Count - ceiling);
        }
    }

    private static void ApplyOrder(
        DecentSamplerArpOrder order,
        List<DecentSamplerArpNote> pool,
        List<DecentSamplerArpNote> pattern)
    {
        var count = pool.Count;

        switch (order)
        {
            case DecentSamplerArpOrder.Down:
                for (var index = count - 1; index >= 0; index--)
                {
                    pattern.Add(pool[index]);
                }

                break;

            case DecentSamplerArpOrder.UpDown:
                pattern.AddRange(pool);

                for (var index = count - 2; index >= 1; index--)
                {
                    pattern.Add(pool[index]);
                }

                break;

            case DecentSamplerArpOrder.UpDownInclusive:
                pattern.AddRange(pool);

                for (var index = count - 1; index >= 0; index--)
                {
                    pattern.Add(pool[index]);
                }

                break;

            case DecentSamplerArpOrder.DownUp:
                for (var index = count - 1; index >= 0; index--)
                {
                    pattern.Add(pool[index]);
                }

                for (var index = 1; index <= count - 2; index++)
                {
                    pattern.Add(pool[index]);
                }

                break;

            case DecentSamplerArpOrder.DownUpInclusive:
                for (var index = count - 1; index >= 0; index--)
                {
                    pattern.Add(pool[index]);
                }

                pattern.AddRange(pool);
                break;

            default:
                // up, as_played, random and random_no_repeat all step through the pool as it stands;
                // the two random orders draw from it rather than walking it.
                pattern.AddRange(pool);
                break;
        }
    }

    // An insertion sort, because the list is at most sixteen entries, because it allocates nothing,
    // and because it is stable: two notes at the same pitch keep the order they were pressed in.
    private static void SortByPitch(List<DecentSamplerArpNote> notes, int start, int count)
    {
        for (var index = start + 1; index < start + count; index++)
        {
            var note = notes[index];
            var position = index - 1;

            while (position >= start && notes[position].Key > note.Key)
            {
                notes[position + 1] = notes[position];
                position--;
            }

            notes[position + 1] = note;
        }
    }

    private static void SortByPressOrder(List<DecentSamplerArpNote> notes, int start, int count)
    {
        for (var index = start + 1; index < start + count; index++)
        {
            var note = notes[index];
            var position = index - 1;

            while (position >= start && notes[position].PressOrder > note.PressOrder)
            {
                notes[position + 1] = notes[position];
                position--;
            }

            notes[position + 1] = note;
        }
    }
}
