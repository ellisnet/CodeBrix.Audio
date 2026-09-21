namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// A voice's filter, as a bank row states it: where it sits, how sharp it is, how it follows the
// keyboard and the velocity, and the envelope that moves it.
internal sealed class GmFilterSpec
{
    internal GmFilterMode Mode = GmFilterMode.Off;

    // The cutoff at middle C (key 60) at velocity 64, in Hz.
    internal double Cutoff = 8000.0;

    // 0 is critically damped and 1 is a pronounced peak at the cutoff.
    internal double Resonance;

    // How far the cutoff follows the keyboard: 1.0 tracks it exactly (an octave up doubles the
    // cutoff), 0.0 leaves the cutoff where it is whatever is played. Most acoustic instruments sit
    // between; a resonant body that does not move with the note sits at 0.
    internal double KeyTracking = 0.5;

    // How many octaves a full-velocity note opens the filter beyond a middle one.
    internal double VelocityOctaves;

    // How many octaves the filter envelope moves the cutoff at full depth. Negative closes it.
    internal double EnvelopeOctaves;

    // The filter's own envelope. Null means the amplitude envelope drives it, which is the usual
    // shape for anything struck: the tone darkens as it dies.
    internal GmEnvelopeSpec Envelope;

    internal GmFilterSpec Clone() =>
        new GmFilterSpec
        {
            Mode = Mode,
            Cutoff = Cutoff,
            Resonance = Resonance,
            KeyTracking = KeyTracking,
            VelocityOctaves = VelocityOctaves,
            EnvelopeOctaves = EnvelopeOctaves,
            Envelope = Envelope == null ? null : Envelope.Clone(),
        };
}
