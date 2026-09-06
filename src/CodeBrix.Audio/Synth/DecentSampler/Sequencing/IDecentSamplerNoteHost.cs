using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Sequencing;

// What the note sequencer and the arpeggiator need from the synthesizer, and nothing more: the clock
// they run against, and a way to start and stop a note in the sampler's own voice runtime.
//
// A generated note enters the voice runtime through StartNote at a FRAME OFFSET inside the block that
// is about to render, which is what makes a sequence land on the beat rather than on the nearest
// block boundary. StopNote has no offset: the amplitude envelope is computed once per block, so a
// release inside a block would not be audible anyway.
internal interface IDecentSamplerNoteHost
{
    // The synthesis rate, for turning beats into frames.
    int SampleRate { get; }

    // The live musical clock. Never null.
    TempoSource Tempo { get; }

    // Starts a note in the voice runtime, skipping the <midi> handlers, the arpeggiator and everything
    // else that has already had its say.
    void StartNote(int channel, int key, int velocity, int frameOffset);

    // Releases every voice of a note in the voice runtime.
    void StopNote(int channel, int key);

    // Every key the player is currently holding, in ascending order per channel. The arpeggiator asks
    // for this when it is switched on in the middle of a held chord.
    void CollectHeldKeys(List<DecentSamplerArpNote> destination);
}
