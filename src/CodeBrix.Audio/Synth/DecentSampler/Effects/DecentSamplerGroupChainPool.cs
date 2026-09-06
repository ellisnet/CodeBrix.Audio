using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Effects;

// A group's <effects> element as a per-voice TEMPLATE, with a pool of instantiated chains.
//
// The guide is explicit that a group chain is per voice: "Group level effects are initialized every
// time a note is started and destroyed every time a note is stopped. If you play two notes
// simultaneously, two instances of this effect will be created and these will be independent of each
// other. As a result, they use more CPU than global effects."
//
// Building one on every note-on would allocate on the note path, so a finished chain is returned to a
// pool and reset rather than dropped - the same arrangement the zone runtime uses for oscillator voice
// sources. The observable behaviour is the guide's: each sounding voice has its own chain with its own
// filter state, and a chain starts every note cleared.
internal sealed class DecentSamplerGroupChainPool
{
    private readonly Stack<DecentSamplerEffectChain> _pool = new Stack<DecentSamplerEffectChain>();

    private readonly DecentSamplerEffectsElement _element;
    private readonly DecentSamplerInstrument _instrument;
    private readonly DecentSamplerExtensionRegistry _registry;
    private readonly int _groupIndex;
    private readonly int _sampleRate;
    private readonly int _blockSize;
    private readonly TempoSource _tempo;
    private readonly List<string> _problems;
    private readonly ISet<string> _unsupported;

    public DecentSamplerGroupChainPool(
        DecentSamplerEffectsElement element,
        DecentSamplerInstrument instrument,
        DecentSamplerExtensionRegistry registry,
        int groupIndex,
        int sampleRate,
        int blockSize,
        TempoSource tempo,
        List<string> problems,
        ISet<string> unsupported)
    {
        _element = element;
        _instrument = instrument;
        _registry = registry;
        _groupIndex = groupIndex;
        _sampleRate = sampleRate;
        _blockSize = blockSize;
        _tempo = tempo;
        _problems = problems;
        _unsupported = unsupported;
    }

    // How many chains have ever been built. A test counts it to prove the template is instantiated per
    // voice rather than shared.
    public int InstantiatedCount { get; private set; }

    // How many chains are sitting in the pool right now.
    public int PooledCount => _pool.Count;

    public DecentSamplerEffectChain Rent()
    {
        if (_pool.Count > 0)
        {
            var reused = _pool.Pop();
            reused.Reset();
            return reused;
        }

        InstantiatedCount++;

        // Every instance of the template would report the same problems, so only the first one's reach
        // the synthesizer.
        var first = InstantiatedCount == 1;

        var chain = DecentSamplerEffectChain.Build(
            _element, _instrument, _registry, DecentSamplerEffectPlacement.Group, _groupIndex,
            _sampleRate, _blockSize, _tempo, first ? _problems : null, first ? _unsupported : null);

        return chain;
    }

    public void Return(DecentSamplerEffectChain chain)
    {
        if (chain != null)
        {
            _pool.Push(chain);
        }
    }

    // Builds one instance up front so the first note-on does not have to, and so the effect types a
    // group asks for are resolved (and reported) at load time rather than at the first note.
    public void Warm() => Return(Rent());
}
