using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Sequencing;

// What the binding engine calls when a <binding type="note_sequence"> with no `parameter` fires.
// That form of the binding is an ACTION - start this sequence, stop that one - rather than a
// parameter change, so it has no target in the parameter model and is dispatched here instead.
internal delegate void DecentSamplerSequenceTriggerHandler(
    DecentSamplerBinding binding, DecentSamplerSequenceTrigger trigger);
