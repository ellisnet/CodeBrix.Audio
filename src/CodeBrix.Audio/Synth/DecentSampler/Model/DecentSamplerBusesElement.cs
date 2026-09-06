using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// The <c>&lt;buses&gt;</c> element: up to sixteen mix buses that zones and groups can route to.
/// </summary>
public sealed class DecentSamplerBusesElement : DecentSamplerElement
{
    private readonly List<DecentSamplerBus> _buses = [];

    /// <summary>The buses, in document order. The first is <c>BUS_1</c>.</summary>
    public IReadOnlyList<DecentSamplerBus> Buses => _buses;

    internal void Add(DecentSamplerBus bus)
    {
        bus.Index = _buses.Count;
        _buses.Add(bus);
        AddChild(bus);
    }
}
