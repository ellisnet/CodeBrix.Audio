using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.Audio.Synth.Sfz;

// Collects and assembles the block-structured opcode families while a region is built: the v2 LFOs
// (lfoN_*), flexible envelopes (egN_*), EQ bands (eqN_*), ARIA variators (varNN_*), and the v1
// fixed-name blocks (fileg_*, pitcheg_*, amplfo_*, fillfo_*, pitchlfo_*). SfzRegion routes any opcode
// whose base name matches a family here instead of through its flat opcode switch.
internal sealed class SfzFamilyCollector
{
    private static readonly IReadOnlyList<SfzCcModulation> emptyModulations = [];

    private readonly SortedDictionary<int, Block> lfoBlocks = new SortedDictionary<int, Block>();
    private readonly SortedDictionary<int, Block> egBlocks = new SortedDictionary<int, Block>();
    private readonly SortedDictionary<int, Block> eqBlocks = new SortedDictionary<int, Block>();
    private readonly SortedDictionary<int, Block> varBlocks = new SortedDictionary<int, Block>();

    private readonly Block filEg = new Block();
    private readonly Block pitchEg = new Block();
    private readonly Block ampLfo = new Block();
    private readonly Block filLfo = new Block();
    private readonly Block pitchLfo = new Block();

    // Whether the opcode belongs to one of the families this collector assembles.
    public static bool IsFamilyName(string baseName) =>
        baseName.StartsWith("fileg_", StringComparison.Ordinal) ||
        baseName.StartsWith("pitcheg_", StringComparison.Ordinal) ||
        baseName.StartsWith("amplfo_", StringComparison.Ordinal) ||
        baseName.StartsWith("fillfo_", StringComparison.Ordinal) ||
        baseName.StartsWith("pitchlfo_", StringComparison.Ordinal) ||
        TryMatchBlock(baseName, out _, out _, out _);

    public void Route(SfzOpcode opcode)
    {
        Block block;
        string remainder;

        var baseName = opcode.BaseName;
        if (StripPrefix(baseName, "fileg_", out remainder))
        {
            block = filEg;
        }
        else if (StripPrefix(baseName, "pitcheg_", out remainder))
        {
            block = pitchEg;
        }
        else if (StripPrefix(baseName, "amplfo_", out remainder))
        {
            block = ampLfo;
        }
        else if (StripPrefix(baseName, "fillfo_", out remainder))
        {
            block = filLfo;
        }
        else if (StripPrefix(baseName, "pitchlfo_", out remainder))
        {
            block = pitchLfo;
        }
        else if (TryMatchBlock(baseName, out var family, out var number, out remainder))
        {
            var blocks = family switch
            {
                "lfo" => lfoBlocks,
                "eg" => egBlocks,
                "eq" => eqBlocks,
                _ => varBlocks,
            };

            if (!blocks.TryGetValue(number, out block))
            {
                block = new Block();
                blocks[number] = block;
            }
        }
        else
        {
            return;
        }

        var index = opcode.Index.GetValueOrDefault(-1);
        switch (opcode.Modulation)
        {
            case "oncc":
            case "cc":
                if (index >= 0)
                {
                    block.Depths[(remainder, index)] = opcode.AsFloat();
                }
                break;

            case "curvecc":
                if (index >= 0)
                {
                    block.Curves[(remainder, index)] = opcode.AsInt();
                }
                break;

            case "smoothcc":
                break; // No family target smooths its modulation; carried nowhere.

            default:
                if (index >= 0)
                {
                    block.Indexed[(remainder, index)] = opcode;
                }
                else
                {
                    block.Plain[remainder] = opcode;
                }
                break;
        }
    }

    public void ApplyTo(SfzRegion region)
    {
        region.SetModEnvelopes(BuildModEnvelope(filEg), BuildModEnvelope(pitchEg));
        region.SetFlexEgs(BuildFlexEgs());
        region.SetEqBands(BuildEqBands());
        region.SetLfos(BuildLfos());
        region.SetVariators(BuildVariators());
    }

    // ---- assembly -----------------------------------------------------------

    private static SfzModEnvelope BuildModEnvelope(Block block)
    {
        if (block.IsEmpty)
        {
            return null;
        }

        var envelope = new SfzModEnvelope
        {
            Delay = Math.Max(0f, block.Float("delay")),
            Attack = Math.Max(0f, block.Float("attack")),
            Hold = Math.Max(0f, block.Float("hold")),
            Decay = Math.Max(0f, block.Float("decay")),
            Sustain = Math.Clamp(block.Float("sustain", 100f), 0f, 100f),
            Release = Math.Max(0f, block.Float("release")),
            Depth = block.Float("depth"),
            Vel2Depth = block.Float("vel2depth"),
            DelayCc = block.Modulations("delay"),
            AttackCc = block.Modulations("attack"),
            HoldCc = block.Modulations("hold"),
            DecayCc = block.Modulations("decay"),
            SustainCc = block.Modulations("sustain"),
            ReleaseCc = block.Modulations("release"),
            DepthCc = block.Modulations("depth"),
        };

        return envelope;
    }

    private IReadOnlyList<SfzFlexEg> BuildFlexEgs()
    {
        if (egBlocks.Count == 0)
        {
            return Array.Empty<SfzFlexEg>();
        }

        var result = new List<SfzFlexEg>();
        foreach (var pair in egBlocks)
        {
            var block = pair.Value;

            var pointCount = 0;
            foreach (var key in block.Indexed.Keys)
            {
                if (key.Remainder == "time" || key.Remainder == "level")
                {
                    pointCount = Math.Max(pointCount, key.Index + 1);
                }
            }

            if (pointCount == 0)
            {
                continue;
            }

            var times = new float[pointCount];
            var levels = new float[pointCount];
            for (var i = 0; i < pointCount; i++)
            {
                times[i] = Math.Max(0f, block.IndexedFloat("time", i));
                levels[i] = Math.Clamp(block.IndexedFloat("level", i), -1f, 1f);
            }

            var eg = new SfzFlexEg(pair.Key, times, levels)
            {
                Pitch = block.Float("pitch"),
                PitchCc = block.Modulations("pitch"),
                Cutoff = block.Float("cutoff"),
                CutoffCc = block.Modulations("cutoff"),
                Amplitude = block.Float("amplitude"),
                AmplitudeCc = block.Modulations("amplitude"),
            };

            var sustain = block.Int("sustain", -1);
            if (0 <= sustain && sustain < pointCount)
            {
                eg.SustainPoint = sustain;
            }

            result.Add(eg);
        }

        return result;
    }

    private IReadOnlyList<SfzEqBand> BuildEqBands()
    {
        if (eqBlocks.Count == 0)
        {
            return Array.Empty<SfzEqBand>();
        }

        var result = new List<SfzEqBand>();
        foreach (var pair in eqBlocks)
        {
            var number = pair.Key;
            var block = pair.Value;

            var defaultFrequency = number switch
            {
                1 => 50f,
                2 => 500f,
                3 => 5000f,
                _ => 1000f,
            };

            result.Add(new SfzEqBand(
                number,
                block.Float("freq", defaultFrequency),
                Math.Max(0.001f, block.Float("bw", 1f)),
                block.Float("gain"),
                block.Modulations("freq"),
                block.Modulations("bw"),
                block.Modulations("gain")));
        }

        return result;
    }

    private IReadOnlyList<SfzLfo> BuildLfos()
    {
        var result = new List<SfzLfo>();

        // The v1 fixed blocks first, translated: sine LFOs with a single target each.
        AddV1Lfo(result, ampLfo, static (lfo, block) =>
        {
            lfo.Volume = block.Float("depth");
            lfo.VolumeCc = block.Modulations("depth");
        });
        AddV1Lfo(result, filLfo, static (lfo, block) =>
        {
            lfo.Cutoff = block.Float("depth");
            lfo.CutoffCc = block.Modulations("depth");
        });
        AddV1Lfo(result, pitchLfo, static (lfo, block) =>
        {
            lfo.Pitch = block.Float("depth");
            lfo.PitchCc = block.Modulations("depth");
        });

        foreach (var pair in lfoBlocks)
        {
            var block = pair.Value;
            var lfo = new SfzLfo(pair.Key)
            {
                Frequency = Math.Max(0f, block.Float("freq")),
                FrequencyCc = block.Modulations("freq"),
                Delay = Math.Max(0f, block.Float("delay")),
                DelayCc = block.Modulations("delay"),
                Fade = Math.Max(0f, block.Float("fade")),
                FadeCc = block.Modulations("fade"),
                Phase = Math.Clamp(block.Float("phase"), 0f, 1f),
                Wave = ParseWave(block.Int("wave")),
                Volume = block.Float("volume"),
                VolumeCc = block.Modulations("volume"),
                Pitch = block.Float("pitch"),
                PitchCc = block.Modulations("pitch"),
                Cutoff = block.Float("cutoff"),
                CutoffCc = block.Modulations("cutoff"),
                Pan = block.Float("pan"),
                PanCc = block.Modulations("pan"),
            };

            BuildLfoSubs(lfo, block);
            BuildLfoEqTargets(lfo, block);
            BuildLfoFrequencyModulations(lfo, block);

            result.Add(lfo);
        }

        return result.Count == 0 ? Array.Empty<SfzLfo>() : result;
    }

    private static void AddV1Lfo(List<SfzLfo> result, Block block, Action<SfzLfo, Block> applyTarget)
    {
        if (block.IsEmpty)
        {
            return;
        }

        // 7.5 Hz is the classic v1 default rate; v1 LFOs are sine. Number 0 marks a translated block.
        var lfo = new SfzLfo(0)
        {
            Wave = SfzLfoWave.Sine,
            Frequency = Math.Max(0f, block.Float("freq", 7.5f)),
            FrequencyCc = block.Modulations("freq"),
            Delay = Math.Max(0f, block.Float("delay")),
            DelayCc = block.Modulations("delay"),
            Fade = Math.Max(0f, block.Float("fade")),
            FadeCc = block.Modulations("fade"),
        };

        applyTarget(lfo, block);
        result.Add(lfo);
    }

    private static void BuildLfoSubs(SfzLfo lfo, Block block)
    {
        var indices = new SortedSet<int>();
        foreach (var key in block.Indexed.Keys)
        {
            if (key.Index >= 2 &&
                (key.Remainder == "wave" || key.Remainder == "ratio" ||
                 key.Remainder == "scale" || key.Remainder == "offset"))
            {
                indices.Add(key.Index);
            }
        }

        if (indices.Count == 0)
        {
            return;
        }

        var subs = new List<SfzLfoSub>();
        foreach (var index in indices)
        {
            subs.Add(new SfzLfoSub(
                index,
                ParseWave(block.IndexedInt("wave", index)),
                block.IndexedFloat("ratio", index, 1f),
                block.IndexedFloat("scale", index, 1f),
                block.IndexedFloat("offset", index)));
        }

        lfo.Subs = subs;
    }

    private static void BuildLfoEqTargets(SfzLfo lfo, Block block)
    {
        List<SfzLfoEqTarget> targets = null;

        for (var band = 1; band <= 3; band++)
        {
            var freqKey = "eq" + band + "freq";
            var gainKey = "eq" + band + "gain";

            var frequency = block.Float(freqKey);
            var gain = block.Float(gainKey);
            var frequencyCc = block.Modulations(freqKey);
            var gainCc = block.Modulations(gainKey);

            if (frequency == 0f && gain == 0f && frequencyCc.Count == 0 && gainCc.Count == 0)
            {
                continue;
            }

            targets ??= new List<SfzLfoEqTarget>();
            targets.Add(new SfzLfoEqTarget(band)
            {
                Frequency = frequency,
                FrequencyCc = frequencyCc,
                Gain = gain,
                GainCc = gainCc,
            });
        }

        if (targets != null)
        {
            lfo.EqTargets = targets;
        }
    }

    private static void BuildLfoFrequencyModulations(SfzLfo lfo, Block block)
    {
        List<SfzLfoFrequencyModulation> modulations = null;

        var sources = new SortedSet<int>();
        foreach (var key in block.Plain.Keys.Concat(block.Depths.Keys.Select(k => k.Remainder)))
        {
            if (TryParseFreqLfoSource(key, out var source))
            {
                sources.Add(source);
            }
        }

        foreach (var source in sources)
        {
            var remainder = "freq_lfo" + source;
            modulations ??= new List<SfzLfoFrequencyModulation>();
            modulations.Add(new SfzLfoFrequencyModulation(source)
            {
                Depth = block.Float(remainder),
                DepthCc = block.Modulations(remainder),
            });
        }

        if (modulations != null)
        {
            lfo.FrequencyLfoModulations = modulations;
        }
    }

    private static bool TryParseFreqLfoSource(string remainder, out int source)
    {
        source = 0;
        if (!remainder.StartsWith("freq_lfo", StringComparison.Ordinal))
        {
            return false;
        }

        return int.TryParse(remainder.Substring("freq_lfo".Length), out source) && source > 0;
    }

    private IReadOnlyList<SfzVariator> BuildVariators()
    {
        if (varBlocks.Count == 0)
        {
            return Array.Empty<SfzVariator>();
        }

        var result = new List<SfzVariator>();
        foreach (var pair in varBlocks)
        {
            var block = pair.Value;

            var multiply = block.Plain.TryGetValue("mod", out var mode) && mode.Value == "mult";
            var variator = new SfzVariator(pair.Key, multiply, block.Modulations(""))
            {
                Cutoff = block.Float("cutoff"),
            };

            for (var band = 1; band <= 3; band++)
            {
                variator.SetEqGain(band, block.Float("eq" + band + "gain"));
                variator.SetEqFrequency(band, block.Float("eq" + band + "freq"));
            }

            result.Add(variator);
        }

        return result;
    }

    // ---- name matching ------------------------------------------------------

    private static bool StripPrefix(string name, string prefix, out string remainder)
    {
        if (name.StartsWith(prefix, StringComparison.Ordinal))
        {
            remainder = name.Substring(prefix.Length);
            return true;
        }

        remainder = null;
        return false;
    }

    // Matches lfo01_freq / lfo3_freq_lfo1 / eg06_pitch / eq1_gain / var01 (empty remainder), giving
    // the family, the numeric block id, and the remainder after the block segment.
    private static bool TryMatchBlock(string baseName, out string family, out int number, out string remainder)
    {
        family = null;
        number = 0;
        remainder = null;

        foreach (var prefix in familyPrefixes)
        {
            if (!baseName.StartsWith(prefix, StringComparison.Ordinal) || baseName.Length == prefix.Length)
            {
                continue;
            }

            var digitEnd = prefix.Length;
            while (digitEnd < baseName.Length && char.IsAsciiDigit(baseName[digitEnd]))
            {
                digitEnd++;
            }

            if (digitEnd == prefix.Length)
            {
                continue;
            }

            if (digitEnd == baseName.Length)
            {
                family = prefix;
                number = ParseNumber(baseName, prefix.Length, digitEnd);
                remainder = "";
                return true;
            }

            if (baseName[digitEnd] != '_')
            {
                continue;
            }

            family = prefix;
            number = ParseNumber(baseName, prefix.Length, digitEnd);
            remainder = baseName.Substring(digitEnd + 1);
            return true;
        }

        return false;
    }

    private static int ParseNumber(string name, int start, int end) =>
        int.TryParse(name.AsSpan(start, end - start), out var value) ? value : 0;

    private static SfzLfoWave ParseWave(int value)
    {
        switch (value)
        {
            case -1:
            case 12:
            case 13:
                // -1 is the deprecated ARIA random, 13 the stepped random; both land on sample-and-hold.
                return SfzLfoWave.RandomSampleHold;

            case 1: return SfzLfoWave.Sine;
            case 2: return SfzLfoWave.Pulse75;
            case 3: return SfzLfoWave.Square;
            case 4: return SfzLfoWave.Pulse25;
            case 5: return SfzLfoWave.Pulse12;
            case 6: return SfzLfoWave.SawUp;
            case 7: return SfzLfoWave.SawDown;
            default: return SfzLfoWave.Triangle;
        }
    }

    private static readonly string[] familyPrefixes = ["lfo", "eq", "eg", "var"];

    // One family block's raw opcodes, keyed by the remainder after the block id: plain values,
    // structurally indexed values (time0, wave2), and CC modulation depth/curve pairs.
    private sealed class Block
    {
        public Dictionary<string, SfzOpcode> Plain { get; } = new Dictionary<string, SfzOpcode>(StringComparer.Ordinal);
        public Dictionary<(string Remainder, int Index), SfzOpcode> Indexed { get; } = new Dictionary<(string, int), SfzOpcode>();
        public Dictionary<(string Remainder, int Cc), float> Depths { get; } = new Dictionary<(string, int), float>();
        public Dictionary<(string Remainder, int Cc), int> Curves { get; } = new Dictionary<(string, int), int>();

        public bool IsEmpty => Plain.Count == 0 && Indexed.Count == 0 && Depths.Count == 0;

        public float Float(string remainder, float fallback = 0f) =>
            Plain.TryGetValue(remainder, out var opcode) ? opcode.AsFloat(fallback) : fallback;

        public int Int(string remainder, int fallback = 0) =>
            Plain.TryGetValue(remainder, out var opcode) ? opcode.AsInt(fallback) : fallback;

        public float IndexedFloat(string remainder, int index, float fallback = 0f) =>
            Indexed.TryGetValue((remainder, index), out var opcode) ? opcode.AsFloat(fallback) : fallback;

        public int IndexedInt(string remainder, int index, int fallback = 0) =>
            Indexed.TryGetValue((remainder, index), out var opcode) ? opcode.AsInt(fallback) : fallback;

        public IReadOnlyList<SfzCcModulation> Modulations(string remainder)
        {
            List<SfzCcModulation> result = null;

            foreach (var pair in Depths)
            {
                if (pair.Key.Remainder != remainder)
                {
                    continue;
                }

                var curveIndex = Curves.TryGetValue(pair.Key, out var curve) ? curve : 0;
                result ??= new List<SfzCcModulation>();
                result.Add(new SfzCcModulation(pair.Key.Cc, pair.Value, curveIndex));
            }

            if (result == null)
            {
                return emptyModulations;
            }

            result.Sort((a, b) => a.CcNumber.CompareTo(b.CcNumber));
            return result;
        }
    }
}
