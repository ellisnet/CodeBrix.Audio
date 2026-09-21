using CodeBrix.Audio.ModestSynth.Patch;

namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// One COMPONENT of a voice: an oscillator recipe with its own level, tuning and envelope.
//
// This is what a single-oscillator voice could never do. A snare is a tuned shell plus a noise
// rattle; a kick is a sine body plus a click; a flute is a tone plus the breath around it; a piano
// is a string plus the thump of the hammer; an octave doubling or a detuned second saw is the same
// idea with the second component tuned rather than coloured. A voice carries up to
// GmVoiceSpec.MaximumLayers of them, and a layer that is not there costs nothing.
internal sealed class GmLayerSpec
{
    // The most detuned copies one layer may spread across the stereo field.
    internal const int MaximumUnison = 3;

    // Which recipe the layer plays, and its two knobs. The bank carries these rather than a built
    // patch so that a row can change the tone of a template's layer without rebuilding the rest of
    // it; GmBank turns them into the Patch once the row has been applied.
    internal GmTone Tone = GmTone.Sine;

    internal double Shape = double.NaN;

    internal double Ring = double.NaN;

    // Which oscillator, and how it is set up. The whole ModestPatch parameter model is reused, so a
    // layer can be any waveform the package generates, FM algorithms and wavetables included.
    internal ModestPatch Patch;

    internal double Level = 1.0;

    internal double Transpose;

    internal double FineCents;

    // Where the layer sits, -1 (left) to 1 (right), ADDED to the voice's own pan.
    internal double Pan;

    // The layer's own amplitude envelope. Null means the layer simply follows the voice's envelope,
    // which is what a plain octave doubling wants; a hammer thump or a breath transient needs its
    // own, shorter one.
    internal GmEnvelopeSpec Envelope;

    // 1, 2 or 3 detuned copies spread across the stereo field. A stereo unison MULTIPLIES the cost
    // of the layer, so the bank uses it on the handful of programs that are defined by it - string
    // ensembles, the wide pads, a couple of leads - and nowhere else.
    internal int Unison = 1;

    internal double UnisonDetuneCents = 8.0;

    internal double UnisonSpread = 0.7;

    // A layer pinned to one pitch whatever key was played - the tuned shell of a drum, a fixed
    // noise band, the rattle under a snare. Not a number when the layer follows the key.
    internal double FixedKey = double.NaN;

    internal int OscillatorCount => Unison < 1 ? 1 : Unison > MaximumUnison ? MaximumUnison : Unison;

    internal GmLayerSpec Clone() =>
        new GmLayerSpec
        {
            Tone = Tone,
            Shape = Shape,
            Ring = Ring,
            Patch = Patch,
            Level = Level,
            Transpose = Transpose,
            FineCents = FineCents,
            Pan = Pan,
            Envelope = Envelope == null ? null : Envelope.Clone(),
            Unison = Unison,
            UnisonDetuneCents = UnisonDetuneCents,
            UnisonSpread = UnisonSpread,
            FixedKey = FixedKey,
        };
}
