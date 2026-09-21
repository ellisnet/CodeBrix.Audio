namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// One EXTRA component on a bank row: a second or third layer written beside the row's main one.
//
// Everything is optional. A field left alone is inherited - the tone knobs from the recipe, the
// envelope from the voice's own. That is what lets "a snare is a shell plus a rattle" be two lines
// rather than a paragraph.
internal sealed class GmLayerRow
{
    internal GmTone Tone = GmTone.None;

    internal double Shape = double.NaN;

    internal double Ring = double.NaN;

    internal double Level = 1.0;

    internal double Transpose;

    internal double Fine;

    internal double Pan;

    internal double FixedKey = double.NaN;

    internal int Unison = 1;

    internal double Detune = double.NaN;

    internal double Spread = double.NaN;

    // The layer's own envelope. Any one of these being set gives the layer an envelope of its own,
    // starting from a copy of the voice's and overriding only what is stated here.
    internal double Delay = double.NaN;

    internal double Attack = double.NaN;

    internal double Hold = double.NaN;

    internal double Decay = double.NaN;

    internal double Sustain = double.NaN;

    internal double Release = double.NaN;

    internal bool LinearAttack;

    internal bool HasOwnEnvelope =>
        !double.IsNaN(Delay) || !double.IsNaN(Attack) || !double.IsNaN(Hold) ||
        !double.IsNaN(Decay) || !double.IsNaN(Sustain) || !double.IsNaN(Release) || LinearAttack;
}
