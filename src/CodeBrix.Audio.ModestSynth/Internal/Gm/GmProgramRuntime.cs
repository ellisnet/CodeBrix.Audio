using CodeBrix.Audio.ModestSynth.Oscillators;

namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// One voicing, built once and ready to play: the spec, plus a pool of oscillators per layer that
// voices borrow at note-on and hand back when they finish.
//
// WHY A POOL RATHER THAN A VOICE OWNING ITS OSCILLATORS. The voice pool is SHARED across all sixteen
// channels (plan 5.3), so a voice cannot know in advance which of 175 voicings it will play, and
// giving every voice an oscillator for every voicing is obviously impossible. Building one at
// note-on instead would allocate on the MIDI path - a waveguide string allocates its delay line and
// a wavetable its mip map - which is exactly what a game running this alongside model inference
// cannot afford.
//
// So: the first few notes of a voicing build its oscillators, and from then on the pool hands the
// same objects back and forth and NOTHING allocates, on either the MIDI path or the render path.
// The pool is capped at the synthesizer's polyphony, which is the most that can ever be out at once.
internal sealed class GmProgramRuntime
{
    private readonly GmVoiceSpec spec;
    private readonly IModestVoiceOscillator[][] pools;
    private readonly int[] counts;
    private readonly int sampleRate;
    private readonly uint seedBase;

    private uint seedCounter;

    internal GmProgramRuntime(GmVoiceSpec spec, int sampleRate, int capacity, uint seedBase)
    {
        this.spec = spec;
        this.sampleRate = sampleRate;
        this.seedBase = seedBase;

        pools = new IModestVoiceOscillator[spec.Layers.Length][];
        counts = new int[spec.Layers.Length];

        for (int layer = 0; layer < spec.Layers.Length; layer++)
        {
            // Capacity voices, each able to hold this layer's whole unison at once.
            pools[layer] = new IModestVoiceOscillator[capacity * spec.Layers[layer].OscillatorCount];
        }
    }

    internal GmVoiceSpec Spec => spec;

    // An oscillator for a layer, either recycled or - the first few times - newly built.
    internal IModestVoiceOscillator Rent(int layer)
    {
        if (counts[layer] > 0)
        {
            int index = --counts[layer];
            IModestVoiceOscillator recycled = pools[layer][index];
            pools[layer][index] = null;
            return recycled;
        }

        return Build(layer);
    }

    internal void Return(int layer, IModestVoiceOscillator oscillator)
    {
        if (oscillator == null) { return; }

        IModestVoiceOscillator[] pool = pools[layer];
        if (counts[layer] >= pool.Length) { return; }

        pool[counts[layer]++] = oscillator;
    }

    private IModestVoiceOscillator Build(int layer)
    {
        GmLayerSpec layerSpec = spec.Layers[layer];

        // Every noisy source gets a seed of its own, so a chord of noise voices is a chord rather
        // than one voice at three times the level - and the seeds are drawn from a counter, so a
        // render repeats exactly.
        uint seed = seedBase ^ ((++seedCounter) * 2246822519u);
        if (seed == 0u) { seed = 1u; }

        // A sung vowel is the one recipe a ModestPatch cannot describe, so it is built here instead.
        // The seed is what gives this singer a vibrato and a pitch wander of its own, which is why a
        // unison of three sounds like three people rather than one of them three times over.
        if (layerSpec.Choir != null)
        {
            return new GmChoirOscillator(sampleRate, layerSpec.Choir, seed);
        }

        IModestOscillator oscillator = layerSpec.Patch.CreateOscillator(sampleRate);

        if (oscillator is NoiseOscillator noise) { noise.Seed = seed; }
        if (oscillator is Pluck1Oscillator pluck) { pluck.Seed = seed; }

        return (IModestVoiceOscillator)oscillator;
    }
}
