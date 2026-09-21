using CodeBrix.Audio.Synth.DecentSampler.Engine;

namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// Everything one of the sixteen MIDI channels remembers: which program it is playing, where its
// controllers sit, its registered-parameter state, and the insert effect its program asked for.
//
// The General MIDI defaults are the ones a file assumes without saying: volume 100, expression 127,
// pan centred, modulation off, reverb send 40, chorus send 0, pitch-bend range two semitones.
internal sealed class GmChannel
{
    internal const int DefaultVolume = 100;
    internal const int DefaultExpression = 127;
    internal const int DefaultPan = 64;
    internal const double DefaultBendSemitones = 2.0;
    internal const int DefaultReverbSend = 40;
    internal const int DefaultChorusSend = 0;

    // How deep the modulation wheel's vibrato goes when it is pushed all the way up, in cents. Fifty
    // cents either side is a half semitone, which is a pronounced vibrato and about as far as a
    // General MIDI file ever means CC1 to go.
    internal const double FullModulationCents = 50.0;

    private int rpnMsb = 0x7F;
    private int rpnLsb = 0x7F;

    internal int Program { get; set; }

    internal bool IsPercussion { get; set; }

    internal int Volume { get; set; } = DefaultVolume;

    internal int Expression { get; set; } = DefaultExpression;

    internal int Pan { get; set; } = DefaultPan;

    internal int Modulation { get; set; }

    internal double BendSemitones { get; set; }

    internal double BendRange { get; set; } = DefaultBendSemitones;

    internal bool SustainDown { get; set; }

    // Where CC 91 and CC 93 put the channel. They are only read once the music has actually sent
    // them - see the two flags below - because a voicing states a send of its own and there is no
    // sense in a General MIDI default overriding it.
    internal double ReverbSend { get; set; } = DefaultReverbSend / 127.0;

    internal double ChorusSend { get; set; }

    // True once the music has set CC 91 or CC 93 itself. Until then a new note takes the send its own
    // VOICING asks for; afterwards it takes the channel's, because what the file asked for outranks
    // what the bank suggests. Reset-all-controllers puts it back.
    internal bool ReverbSendIsExplicit { get; set; }

    internal bool ChorusSendIsExplicit { get; set; }

    internal IInstrumentEffect Insert { get; set; }

    // The insert effect's type, so a program change that lands on the same effect keeps the instance
    // rather than rebuilding it.
    internal string InsertType { get; set; }

    internal double ModulationCents => Modulation / 127.0 * FullModulationCents;

    internal double Gain => Volume / 127.0 * (Expression / 127.0);

    // -1 to 1, as a voice's pan is counted.
    internal double PanPosition => (Pan - 64) / 63.0;

    internal void ResetControllers()
    {
        Volume = DefaultVolume;
        Expression = DefaultExpression;
        Pan = DefaultPan;
        Modulation = 0;
        BendSemitones = 0.0;
        BendRange = DefaultBendSemitones;
        SustainDown = false;
        ReverbSend = DefaultReverbSend / 127.0;
        ChorusSend = DefaultChorusSend / 127.0;
        ReverbSendIsExplicit = false;
        ChorusSendIsExplicit = false;
        rpnMsb = 0x7F;
        rpnLsb = 0x7F;
    }

    internal void SelectRpn(bool msb, int value)
    {
        if (msb) { rpnMsb = value; }
        else { rpnLsb = value; }
    }

    // Registered parameter 0 is the pitch-bend range: the MSB is whole semitones and the LSB is
    // cents. It is the one RPN a General MIDI file actually uses.
    internal bool IsBendRangeSelected => rpnMsb == 0 && rpnLsb == 0;

    internal void SetBendRangeSemitones(int semitones)
    {
        BendRange = semitones + (BendRange - (int)BendRange);
    }

    internal void SetBendRangeCents(int cents)
    {
        BendRange = (int)BendRange + (cents / 100.0);
    }
}
