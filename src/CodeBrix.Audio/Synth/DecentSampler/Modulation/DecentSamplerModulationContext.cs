using CodeBrix.Audio.Synth.Mpe;

namespace CodeBrix.Audio.Synth.DecentSampler.Modulation;

// Everything a modulator needs from the world outside itself when it is asked for a value: the
// transport (for an LFO running in musical time), the MIDI channel state (for the controller,
// timbre and pressure sources), and which note is asking.
//
// A global-scope modulator is handed the zone master's channel and no key; a voice-scope one is
// handed the voice's own.
internal readonly struct DecentSamplerModulationContext
{
    public DecentSamplerModulationContext(
        TempoSource tempo, MpeChannelState mpe, int channel, int key, int velocity)
    {
        Tempo = tempo;
        Mpe = mpe;
        Channel = channel;
        Key = key;
        Velocity = velocity;
    }

    public TempoSource Tempo { get; }

    public MpeChannelState Mpe { get; }

    public int Channel { get; }

    // The sounding key, or -1 for a global-scope modulator that belongs to no note.
    public int Key { get; }

    public int Velocity { get; }
}
