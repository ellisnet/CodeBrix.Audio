using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Effects;

// One <effects> element turned into running digital signal processing: the effects in document order,
// each processing the stereo block in place.
//
// The same class serves all three placements the format has. An instrument chain and a bus chain are
// built once and process the mix; a group chain is a TEMPLATE that is instantiated per voice, which is
// what the guide documents ("Group level effects are initialized every time a note is started ... If
// you play two notes simultaneously, two instances of this effect will be created and these will be
// independent of each other"). DecentSamplerGroupChainPool does the instantiating.
//
// An effect whose type no factory covers is left out of the chain and reported once; the rest of the
// chain still runs. A DISABLED effect is bypassed for as long as that is true and comes back the
// moment it is not. Its TAGS do not gate it: measured (round 2, item 24), a disabled tag on an effect
// does not bypass it and a tag volume does not scale it - tags on an effect exist so a binding can
// address it by name, and tag enable and tag volume act on ZONES only.
internal sealed class DecentSamplerEffectChain
{
    private readonly IInstrumentEffect[] _effects;

    private DecentSamplerEffectChain(IInstrumentEffect[] effects) => _effects = effects;

    // How many effects actually run. An unregistered type is not one of them.
    public int Count => _effects.Length;

    public bool IsEmpty => _effects.Length == 0;

    public IInstrumentEffect this[int index] => _effects[index];

    // Builds a chain from a parsed <effects> element. Every effect that could not be built adds one
    // line to `problems` and one name to `unsupported`.
    public static DecentSamplerEffectChain Build(
        DecentSamplerEffectsElement element,
        DecentSamplerInstrument instrument,
        DecentSamplerExtensionRegistry registry,
        DecentSamplerEffectPlacement placement,
        int chainIndex,
        int sampleRate,
        int blockSize,
        TempoSource tempo,
        List<string> problems,
        ISet<string> unsupported)
    {
        if (element == null || element.Effects.Count == 0)
        {
            return new DecentSamplerEffectChain([]);
        }

        var built = new List<IInstrumentEffect>(element.Effects.Count);

        foreach (var effect in element.Effects)
        {
            var type = effect.TypeName;

            if (registry == null || !registry.TryGetEffectFactory(type, out var factory))
            {
                var message = DecentSamplerExtensions.MissingEffectMessage(type);
                if (problems != null && !problems.Contains(message))
                {
                    problems.Add(message);
                }

                unsupported?.Add("effect:" + (type ?? "(unnamed)"));
                continue;
            }

            var context = new EffectContext(
                instrument, effect, sampleRate, placement, chainIndex, blockSize, tempo);

            IInstrumentEffect instance;
            try
            {
                instance = factory(context);
            }
            catch (Exception exception)
            {
                problems?.Add($"effect type '{type}' could not be built ({exception.Message})");
                continue;
            }

            if (instance == null)
            {
                problems?.Add($"effect type '{type}' produced no effect");
                continue;
            }

            instance.Prepare(sampleRate);

            if (instance is DecentSamplerConvolutionEffect convolution && problems != null)
            {
                foreach (var problem in convolution.Problems)
                {
                    if (!problems.Contains(problem))
                    {
                        problems.Add(problem);
                    }
                }
            }

            built.Add(instance);
        }

        return new DecentSamplerEffectChain([.. built]);
    }

    // Runs the chain over one block, in place.
    //
    // MEASURED (round 2, item 24): a DISABLED tag on an effect does NOT bypass it, and a tag volume
    // does not scale it either. Tag enable and tag volume act on ZONES only, so the chain runs every
    // effect regardless of what its tags say; the tags are there for a binding to address the effect
    // by name.
    public void Process(float[] left, float[] right, int frames)
    {
        for (var i = 0; i < _effects.Length; i++)
        {
            _effects[i].Process(left, right, frames);
        }
    }

    public void Reset()
    {
        for (var i = 0; i < _effects.Length; i++)
        {
            _effects[i].Reset();
        }
    }

}
