using System;
using System.Collections.Generic;
using System.Globalization;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Effects;

// The mixer: what happens to a block of audio after the voices have written it into their output
// slots.
//
//   voices -> group chains (per voice, inside the voice) -> output slots
//          -> bus chains -> bus routing -> main slot
//          -> the instrument chain -> the main output
//          -> the auxiliary pairs, kept separate
//
// SLOTS. The numbering is the sampler engine's, unchanged: slot 0 is the main mix, slots 1 to 16 are
// BUS_1 to BUS_16 and slots 17 to 32 are AUX_STEREO_OUTPUT_1 to 16. NO_OUTPUT has no slot.
//
// BUSES. Each bus applies its own chain, then busVolume, then sends itself to up to eight targets with
// a volume each. A bus that writes no target at all goes to the main output, which is the same default
// a zone gets. Buses run in declaration order.
//
// MEASURED (round 2, item 24), and both readings cost audio, so both are reported in Problems:
//   * A BUS CANNOT FEED A BUS. A bus whose own outputNTarget names another bus is SILENT in the
//     reference, so such a send is dropped. (With no other send declared the bus produces nothing,
//     which is exactly what the reference measured.) There is therefore no routing graph to order and
//     no cycle to break.
//   * A GROUP ROUTED TO AN UNDECLARED BUS IS SILENT. The reference does not fold it back to the main
//     output, so neither does this engine.
internal sealed class DecentSamplerMixer
{
    private const int OutputsPerBus = 8;

    private readonly int _blockSize;
    private readonly float[][] _slotLeft;
    private readonly float[][] _slotRight;
    private readonly bool[] _slotTouched;

    private readonly BusRuntime[] _buses;
    private readonly int[] _busOrder;

    private DecentSamplerEffectChain _mainChain;

    public DecentSamplerMixer(int blockSize)
    {
        _blockSize = blockSize;

        _slotLeft = new float[DecentSamplerOutputSlot.Count][];
        _slotRight = new float[DecentSamplerOutputSlot.Count][];
        _slotTouched = new bool[DecentSamplerOutputSlot.Count];

        for (var i = 0; i < DecentSamplerOutputSlot.Count; i++)
        {
            _slotLeft[i] = new float[blockSize];
            _slotRight[i] = new float[blockSize];
        }

        _buses = [];
        _busOrder = [];
    }

    private DecentSamplerMixer(int blockSize, BusRuntime[] buses, int[] order)
        : this(blockSize)
    {
        _buses = buses;
        _busOrder = order;
    }

    // How many buses the preset declares, capped at the format's sixteen.
    public int BusCount => _buses.Length;

    // The highest auxiliary pair any zone or bus routes to, 0 when none does. The synthesizer reports
    // it as its auxiliary output count.
    public int AuxiliaryOutputCount { get; private set; }

    public DecentSamplerEffectChain MainChain => _mainChain;

    public DecentSamplerEffectChain BusChain(int index) => _buses[index].Chain;

    // Builds the mixer for an instrument: one chain per bus, the instrument chain, and the bus order.
    public static DecentSamplerMixer Build(
        DecentSamplerInstrument instrument,
        DecentSamplerExtensionRegistry registry,
        int sampleRate,
        int blockSize,
        TempoSource tempo,
        List<string> problems,
        ISet<string> unsupported)
    {
        IReadOnlyList<DecentSamplerBus> declared =
            instrument == null ? Array.Empty<DecentSamplerBus>() : instrument.Buses;
        var count = Math.Min(declared.Count, 16);

        if (declared.Count > 16)
        {
            problems?.Add(
                $"the preset declares {declared.Count.ToString(CultureInfo.InvariantCulture)} buses; the " +
                "format allows sixteen and the rest are ignored");
        }

        var buses = new BusRuntime[count];
        for (var i = 0; i < count; i++)
        {
            buses[i] = BusRuntime.Build(
                declared[i], i, instrument, registry, sampleRate, blockSize, tempo, problems, unsupported);
        }

        var order = new int[count];
        for (var i = 0; i < count; i++)
        {
            order[i] = i;
        }

        var mixer = new DecentSamplerMixer(blockSize, buses, order)
        {
            _mainChain = DecentSamplerEffectChain.Build(
                instrument?.Effects, instrument, registry, DecentSamplerEffectPlacement.Instrument, -1,
                sampleRate, blockSize, tempo, problems, unsupported),
        };

        mixer.AuxiliaryOutputCount = mixer.MeasureAuxiliaryOutputs(instrument);
        mixer.ReportUndeclaredBuses(instrument, problems);

        return mixer;
    }

    public void Reset()
    {
        _mainChain?.Reset();

        for (var i = 0; i < _buses.Length; i++)
        {
            _buses[i].Chain.Reset();
        }
    }

    // Clears the "written this block" flags. A slot buffer itself is cleared the first time a voice
    // touches it, so a preset that only uses the main output pays for one buffer.
    public void BeginBlock() => Array.Clear(_slotTouched, 0, _slotTouched.Length);

    public bool IsTouched(int slot) => _slotTouched[slot];

    public float[] Left(int slot) => _slotLeft[slot];

    public float[] Right(int slot) => _slotRight[slot];

    // Makes a slot ready to be added into, clearing it on first use in the block.
    public void Touch(int slot)
    {
        if (_slotTouched[slot])
        {
            return;
        }

        Array.Clear(_slotLeft[slot], 0, _blockSize);
        Array.Clear(_slotRight[slot], 0, _blockSize);
        _slotTouched[slot] = true;
    }

    // Runs every bus and the instrument chain, leaving the finished main mix in slot 0 and every
    // auxiliary pair in its own slot.
    public void FinishBlock()
    {
        for (var i = 0; i < _busOrder.Length; i++)
        {
            ProcessBus(_buses[_busOrder[i]]);
        }

        if (_mainChain != null && !_mainChain.IsEmpty)
        {
            Touch(DecentSamplerOutputSlot.MainIndex);
            _mainChain.Process(
                _slotLeft[DecentSamplerOutputSlot.MainIndex],
                _slotRight[DecentSamplerOutputSlot.MainIndex],
                _blockSize);
        }
    }

    private void ReportUndeclaredBuses(DecentSamplerInstrument instrument, List<string> problems)
    {
        if (instrument == null || problems == null)
        {
            return;
        }

        var reported = new HashSet<int>();

        foreach (var zone in instrument.Zones)
        {
            for (var output = 0; output < OutputsPerBus; output++)
            {
                var slot = DecentSamplerOutputSlot.FromTarget(zone.OutputTargets[output]);

                if (slot.Kind != DecentSamplerOutputSlotKind.Bus || slot.Number <= _buses.Length ||
                    !reported.Add(slot.Number))
                {
                    continue;
                }

                problems.Add(
                    $"a zone routes to BUS_{Ordinal(slot.Number)}, which the preset does not declare; " +
                    "the reference player silences it and so does this engine");
            }
        }
    }

    private void ProcessBus(BusRuntime bus)
    {
        var slot = DecentSamplerOutputSlot.FirstBusIndex + bus.Index;

        // A bus with a chain always runs it, even with nothing routed in, so a reverb tail on a bus
        // keeps ringing after the last voice that fed it has stopped.
        if (!_slotTouched[slot] && bus.Chain.IsEmpty)
        {
            return;
        }

        Touch(slot);

        var left = _slotLeft[slot];
        var right = _slotRight[slot];

        bus.Chain.Process(left, right, _blockSize);

        // busVolume and every outputNVolume are bindable, so they are read here rather than cached.
        var busVolume = Math.Clamp(bus.Source.BusVolume ?? 1.0, 0.0, 1.0);

        for (var i = 0; i < bus.Sends.Length; i++)
        {
            var send = bus.Sends[i];
            var volume = Math.Clamp(bus.Source.OutputVolume(send.Output) ?? 1.0, 0.0, 1.0);
            var gain = (float)(busVolume * volume);

            if (gain == 0f)
            {
                continue;
            }

            Touch(send.Slot);

            var destinationLeft = _slotLeft[send.Slot];
            var destinationRight = _slotRight[send.Slot];

            for (var f = 0; f < _blockSize; f++)
            {
                destinationLeft[f] += left[f] * gain;
                destinationRight[f] += right[f] * gain;
            }
        }
    }

    // The auxiliary pairs a preset can write to, so a host knows how many outputs to offer. Slots are
    // sparse - a preset may use AUX_STEREO_OUTPUT_3 alone - so this is the HIGHEST one used.
    private int MeasureAuxiliaryOutputs(DecentSamplerInstrument instrument)
    {
        var highest = 0;

        if (instrument != null)
        {
            foreach (var zone in instrument.Zones)
            {
                for (var output = 0; output < OutputsPerBus; output++)
                {
                    highest = Math.Max(highest, AuxNumber(zone.OutputTargets[output]));
                }
            }
        }

        for (var i = 0; i < _buses.Length; i++)
        {
            for (var send = 0; send < _buses[i].Sends.Length; send++)
            {
                var slot = _buses[i].Sends[send].Slot;
                if (slot >= DecentSamplerOutputSlot.FirstAuxIndex)
                {
                    highest = Math.Max(highest, slot - DecentSamplerOutputSlot.FirstAuxIndex + 1);
                }
            }
        }

        return highest;
    }

    private static int AuxNumber(DecentSamplerOutputTarget target)
    {
        var slot = DecentSamplerOutputSlot.FromTarget(target);
        return slot.Kind == DecentSamplerOutputSlotKind.Aux ? slot.Number : 0;
    }

    private static string Ordinal(int value) => value.ToString(CultureInfo.InvariantCulture);

    // One bus: its chain, its volume and where it sends itself.
    private sealed class BusRuntime
    {
        public int Index;
        public DecentSamplerBus Source;
        public DecentSamplerEffectChain Chain;
        public BusSend[] Sends;

        public static BusRuntime Build(
            DecentSamplerBus bus,
            int index,
            DecentSamplerInstrument instrument,
            DecentSamplerExtensionRegistry registry,
            int sampleRate,
            int blockSize,
            TempoSource tempo,
            List<string> problems,
            ISet<string> unsupported)
        {
            var sends = new List<BusSend>(OutputsPerBus);
            var wroteAny = false;

            for (var output = 0; output < OutputsPerBus; output++)
            {
                var target = bus.OutputTarget(output);

                if (!target.HasValue)
                {
                    continue;
                }

                wroteAny = true;

                var slot = DecentSamplerOutputSlot.FromTarget(target.Value);

                if (slot.Index < 0)
                {
                    continue;
                }

                // MEASURED: a bus whose output names another bus is silent. The send is dropped rather
                // than routed, which is what leaves such a bus silent when it declares nothing else.
                if (slot.Kind == DecentSamplerOutputSlotKind.Bus)
                {
                    problems?.Add(
                        $"bus {Ordinal(index + 1)} sends to BUS_{Ordinal(slot.Number)}; a bus cannot " +
                        "feed another bus, so that send produces no sound");
                    continue;
                }

                sends.Add(new BusSend { Slot = slot.Index, Output = output });
            }

            // A bus that names no output at all goes to the main mix, which is where audio goes by
            // default everywhere else in the format.
            if (!wroteAny)
            {
                sends.Add(new BusSend { Slot = DecentSamplerOutputSlot.MainIndex, Output = 0 });
            }

            return new BusRuntime
            {
                Index = index,
                Source = bus,
                Sends = [.. sends],
                Chain = DecentSamplerEffectChain.Build(
                    bus.Effects, instrument, registry, DecentSamplerEffectPlacement.Bus, index,
                    sampleRate, blockSize, tempo, problems, unsupported),
            };
        }
    }

    private struct BusSend
    {
        // The dense slot the send lands in.
        public int Slot;

        // Which of the bus's eight outputs this is, so its volume can be read live.
        public int Output;
    }
}
