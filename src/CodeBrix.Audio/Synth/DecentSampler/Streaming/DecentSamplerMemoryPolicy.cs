using System;
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Streaming;

// Decides, once per load, which of an instrument's sample files are decoded into RAM and which are
// streamed from disk, and how big a preload head the streamed ones may afford.
//
// The rules, in order:
//   1. playbackMode="memory" on any zone that uses the file wins outright. A preset says that when its
//      bindings move SAMPLE_START, SAMPLE_END, LOOP_START or LOOP_END, which only work in memory, and
//      the engine must not overrule it.
//   2. playbackMode="disk_streaming" on every zone that uses the file streams it.
//   3. "auto" streams a file whose decoded size passes StreamingSampleThresholdBytes (8 MB).
//   4. If what is left still passes InstrumentMemoryBudgetBytes (1 GB), the "auto" files that SAVE THE
//      MOST are streamed until it fits. Largest saving first, ties broken by cache key, so the same
//      library always makes the same decision - a preset that renders one way today renders that way
//      tomorrow.
//   5. A streamed sample is not free: it keeps a preload head in RAM. When even the heads do not fit,
//      the head is shortened for the whole instrument until they do, down to a floor.
//
// Counting the heads is what makes the budget mean anything. Global Swarm streams 363 samples, and at
// the default 65,536-frame head those alone are 190 MB - more than two thirds of a 256 MB budget.
//
// Motivation, from the survey the plan was written against: eagerly decoding the nine-library corpus
// costs 4.7 GB of 32-bit float, one library 2.6 GB on its own, and one 96 kHz library nearly 1 GB.
internal static class DecentSamplerMemoryPolicy
{
    /// <summary>The shortest preload head the policy will shrink to, in frames.</summary>
    public const int MinimumPreloadFrames = 2048;

    // One distinct sample file, however many zones point at it.
    public sealed class Entry
    {
        public Entry(
            string cacheKey, string containerKey, DecentSamplerPlaybackMode mode, long decodedBytes,
            long bytesPerFrame)
        {
            CacheKey = cacheKey;
            ContainerKey = containerKey;
            Mode = mode;
            DecodedBytes = decodedBytes;
            BytesPerFrame = Math.Max(1, bytesPerFrame);
        }

        /// <summary>The cache key the file is shared under.</summary>
        public string CacheKey { get; }

        /// <summary>The container key the file is opened with.</summary>
        public string ContainerKey { get; }

        /// <summary>The playback mode the zones that use it agreed on.</summary>
        public DecentSamplerPlaybackMode Mode { get; }

        /// <summary>What the file costs decoded, measured where it was probed and estimated otherwise.</summary>
        public long DecodedBytes { get; }

        /// <summary>What one frame costs in memory: channels times four bytes.</summary>
        public long BytesPerFrame { get; }

        /// <summary>Whether the policy chose to stream it.</summary>
        public bool Stream { get; set; }

        /// <summary>What the preload head costs at a given length.</summary>
        /// <param name="preloadFrames">The head length in frames.</param>
        /// <returns>The bytes, never more than the whole file.</returns>
        public long HeadBytes(int preloadFrames) =>
            Math.Min(DecodedBytes, (long)Math.Max(1, preloadFrames) * BytesPerFrame);

        /// <summary>What the file costs in memory given the decision made about it.</summary>
        /// <param name="preloadFrames">The head length in frames.</param>
        /// <returns>The bytes.</returns>
        public long MemoryCost(int preloadFrames) => Stream ? HeadBytes(preloadFrames) : DecodedBytes;
    }

    /// <summary>
    /// Applies the policy to a set of files.
    /// </summary>
    /// <param name="entries">The distinct files. Their <see cref="Entry.Stream"/> flags are set here.</param>
    /// <param name="sampleThresholdBytes">The per-sample threshold, or zero to disable it.</param>
    /// <param name="instrumentBudgetBytes">The per-instrument budget, or zero to disable it.</param>
    /// <param name="requestedPreloadFrames">The preload head the load options asked for.</param>
    /// <param name="budgetForced">Whether the budget, rather than the threshold, changed a decision.</param>
    /// <param name="preloadFrames">The head length the instrument will actually use.</param>
    /// <returns>A one-line summary of what was decided.</returns>
    public static string Decide(
        IReadOnlyList<Entry> entries, long sampleThresholdBytes, long instrumentBudgetBytes,
        int requestedPreloadFrames, out bool budgetForced, out int preloadFrames)
    {
        budgetForced = false;
        preloadFrames = Math.Max(1, requestedPreloadFrames);

        foreach (var entry in entries)
        {
            entry.Stream =
                entry.Mode == DecentSamplerPlaybackMode.DiskStreaming ||
                (entry.Mode == DecentSamplerPlaybackMode.Auto &&
                 sampleThresholdBytes > 0 &&
                 entry.DecodedBytes > sampleThresholdBytes);
        }

        if (instrumentBudgetBytes > 0)
        {
            budgetForced = ApplyBudget(entries, instrumentBudgetBytes, ref preloadFrames);
        }

        var memoryBytes = 0L;
        var avoidedBytes = 0L;
        var streamed = 0;

        foreach (var entry in entries)
        {
            memoryBytes += entry.MemoryCost(preloadFrames);

            if (entry.Stream)
            {
                streamed++;
                avoidedBytes += entry.DecodedBytes - entry.HeadBytes(preloadFrames);
            }
        }

        var head = preloadFrames == requestedPreloadFrames
            ? string.Empty
            : string.Format(
                CultureInfo.InvariantCulture,
                ", preload head shortened to {0} frames",
                preloadFrames.ToString(CultureInfo.InvariantCulture));

        return string.Format(
            CultureInfo.InvariantCulture,
            "memory policy: {0} of {1} samples in memory, {2} streamed, {3} held in all " +
            "({4} of decoding avoided); per-sample threshold {5}, instrument budget {6}{7}",
            entries.Count - streamed, entries.Count, streamed, Megabytes(memoryBytes),
            Megabytes(avoidedBytes),
            sampleThresholdBytes > 0 ? Megabytes(sampleThresholdBytes) : "off",
            instrumentBudgetBytes > 0 ? Megabytes(instrumentBudgetBytes) : "off",
            head);
    }

    /// <summary>A byte count as megabytes, for a summary line.</summary>
    /// <param name="bytes">The count.</param>
    /// <returns>The text, such as "12.5 MB".</returns>
    public static string Megabytes(long bytes) =>
        (bytes / (1024.0 * 1024.0)).ToString("0.##", CultureInfo.InvariantCulture) + " MB";

    private static bool ApplyBudget(
        IReadOnlyList<Entry> entries, long budgetBytes, ref int preloadFrames)
    {
        var forced = false;

        if (Total(entries, preloadFrames) <= budgetBytes)
        {
            return false;
        }

        // Stream the files that save the most first. A file smaller than its own preload head saves
        // nothing by streaming, so the walk stops there rather than churning through the small ones.
        var candidates = new List<Entry>();

        foreach (var entry in entries)
        {
            if (!entry.Stream && entry.Mode == DecentSamplerPlaybackMode.Auto)
            {
                candidates.Add(entry);
            }
        }

        var head = preloadFrames;

        candidates.Sort((left, right) =>
        {
            var bySaving = Saving(right, head).CompareTo(Saving(left, head));
            return bySaving != 0 ? bySaving : string.CompareOrdinal(left.CacheKey, right.CacheKey);
        });

        foreach (var entry in candidates)
        {
            if (Total(entries, preloadFrames) <= budgetBytes || Saving(entry, preloadFrames) <= 0)
            {
                break;
            }

            entry.Stream = true;
            forced = true;
        }

        if (Total(entries, preloadFrames) <= budgetBytes)
        {
            return forced;
        }

        // Still over: the heads themselves are the weight. Halve the head until they fit, or until the
        // floor - below which a streamed note would start on the reader rather than on RAM.
        while (preloadFrames > MinimumPreloadFrames && Total(entries, preloadFrames) > budgetBytes)
        {
            preloadFrames = Math.Max(MinimumPreloadFrames, preloadFrames / 2);
            forced = true;
        }

        return forced;
    }

    private static long Saving(Entry entry, int preloadFrames) =>
        entry.DecodedBytes - entry.HeadBytes(preloadFrames);

    private static long Total(IReadOnlyList<Entry> entries, int preloadFrames)
    {
        var total = 0L;

        foreach (var entry in entries)
        {
            total += entry.MemoryCost(preloadFrames);
        }

        return total;
    }
}
