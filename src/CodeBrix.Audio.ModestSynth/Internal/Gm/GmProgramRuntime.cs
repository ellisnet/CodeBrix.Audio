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
//
// PREBUILDING (GeneralMidiSynthesizer.Prepare). "The first few notes build the oscillators" put that
// building - and the first-ever JIT of every constructor it runs - on whatever thread played those
// notes, which in a live host is the AUDIO thread. Prebuild moves it to the thread that creates the
// instrument. It must not change a single sample, so it builds EXACTLY the objects the lazy path
// would have built, with exactly the same seeds, and stacks them so that the pool hands them out in
// exactly the order the lazy path would have built them. The argument, in full: the lazy path builds
// only when a layer's pool is EMPTY, and builds whole note-sets (every layer's unison, layer by
// layer, copy by copy - the order GmVoice.Start rents in) from one seed counter; the prebuilt sets
// sit at the BOTTOM of the stack, in that order, below everything returned later. So whenever the
// lazy pool would have been empty and built note-set k, the prebuilt pool pops note-set k instead -
// the same object, the same seed - and when the prebuilt sets run out the seed counter stands where
// the lazy one would have stood, so building carries on identically. The render is bit-identical,
// and a test holds it to that.
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

    // How many oscillators this runtime has built so far, prebuilt or lazily. Tests read it.
    internal int BuiltCount => (int)seedCounter;

    // How many oscillators are waiting in one layer's pool right now. Tests read it.
    internal int PooledCount(int layer) => counts[layer];

    // How many whole note-sets the pool can ever hold: the synthesizer's polyphony.
    internal int Capacity => spec.Layers.Length == 0
        ? 0
        : pools[0].Length / spec.Layers[0].OscillatorCount;

    // Builds up to noteSets whole note-sets NOW, in the order and with the seeds the lazy path would
    // have used, and stacks them so they come out in that order (see the class comment). It only
    // ever runs on a runtime nothing has played yet - a FRESH one that has not been published to
    // the audio thread - and does nothing otherwise, which is what makes it safe off the audio
    // thread and what makes preparing twice a no-op.
    internal void Prebuild(int noteSets)
    {
        if (seedCounter != 0u || noteSets <= 0 || spec.Layers.Length == 0) { return; }

        int sets = noteSets < Capacity ? noteSets : Capacity;
        int layerCount = spec.Layers.Length;
        IModestVoiceOscillator[][] built = new IModestVoiceOscillator[layerCount][];

        for (int layer = 0; layer < layerCount; layer++)
        {
            built[layer] = new IModestVoiceOscillator[sets * spec.Layers[layer].OscillatorCount];
        }

        // The order GmVoice.Start rents in, one whole note at a time, which is the order the lazy
        // path draws seeds in.
        for (int set = 0; set < sets; set++)
        {
            for (int layer = 0; layer < layerCount; layer++)
            {
                int unison = spec.Layers[layer].OscillatorCount;

                for (int copy = 0; copy < unison; copy++)
                {
                    built[layer][(set * unison) + copy] = Build(layer);
                }
            }
        }

        // Rent pops from the top, so the FIRST one built goes on top.
        for (int layer = 0; layer < layerCount; layer++)
        {
            IModestVoiceOscillator[] layerBuilt = built[layer];
            IModestVoiceOscillator[] pool = pools[layer];

            for (int i = 0; i < layerBuilt.Length; i++)
            {
                pool[layerBuilt.Length - 1 - i] = layerBuilt[i];
            }

            counts[layer] = layerBuilt.Length;
        }
    }

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
