namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// The ONE insert effect a program may name, chosen from the effects ModestSynth already ships.
//
// It runs per CHANNEL - one instance over the channel's whole mix - not per voice, because a phaser
// or a stereo widener is a property of the instrument rather than of each note, and one instance is
// what a chord is supposed to sweep through together. A program that names no insert effect builds
// nothing and costs nothing.
internal sealed class GmInsertSpec
{
    // One of ModestEffectTypes: "phaser", "stereo_simulator", "wave_shaper" and the rest.
    internal string Type;

    // Up to four parameters, by the names the effect answers to through TrySetParameter.
    internal string Parameter1;
    internal double Value1;
    internal string Parameter2;
    internal double Value2;
    internal string Parameter3;
    internal double Value3;
    internal string Parameter4;
    internal double Value4;

    internal GmInsertSpec Clone() =>
        new GmInsertSpec
        {
            Type = Type,
            Parameter1 = Parameter1,
            Value1 = Value1,
            Parameter2 = Parameter2,
            Value2 = Value2,
            Parameter3 = Parameter3,
            Value3 = Value3,
            Parameter4 = Parameter4,
            Value4 = Value4,
        };
}
