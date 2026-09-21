namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// One envelope's shape, as a bank row states it. Times are in seconds and levels are linear.
//
// It is NOT ModestSynthesizerSettings' envelope: that one is linear in every stage and belongs to the
// whole synthesizer, where this one belongs to a voice and decays exponentially, which is how struck
// and plucked sounds behave in nature. The old envelope is untouched and still runs the old
// synthesizer.
internal sealed class GmEnvelopeSpec
{
    // A release of zero would step the signal to silence, so every release is at least this long.
    internal const double MinimumReleaseSeconds = 0.004;

    // An attack of zero would step the signal up, so every attack is at least this long. Twenty-two
    // samples at 44.1 kHz: short enough that a kick still sounds instantaneous.
    internal const double MinimumAttackSeconds = 0.0005;

    internal double Delay;

    internal double Attack = 0.005;

    internal double Hold;

    internal double Decay = 0.2;

    internal double Sustain = 1.0;

    internal double Release = 0.15;

    // A straight-line attack - what an organ or a pad wants. The default is the curved attack that
    // sounds natural on anything struck or blown.
    internal bool LinearAttack;

    internal GmEnvelopeSpec Clone() =>
        new GmEnvelopeSpec
        {
            Delay = Delay,
            Attack = Attack,
            Hold = Hold,
            Decay = Decay,
            Sustain = Sustain,
            Release = Release,
            LinearAttack = LinearAttack,
        };
}
