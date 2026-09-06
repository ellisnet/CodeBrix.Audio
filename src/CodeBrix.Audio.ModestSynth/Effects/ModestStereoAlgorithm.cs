namespace CodeBrix.Audio.ModestSynth.Effects;

/// <summary>
/// Which pseudo-stereo algorithm a <see cref="StereoSimulatorEffect" /> uses to turn one channel
/// into two.
/// </summary>
public enum ModestStereoAlgorithm
{
    /// <summary>
    /// Complementary comb filters (<c>lauridsen</c>): one delayed copy added on the left and
    /// subtracted on the right. Fully decorrelated and the cheapest of the three.
    /// </summary>
    Lauridsen,

    /// <summary>
    /// Double-delay comb filters (<c>schroeder</c>): two delays of different lengths, giving the
    /// subtlest widening of the three.
    /// </summary>
    Schroeder,

    /// <summary>
    /// Artificial double tracking (<c>adt</c>): a delay whose length is moved by a low-frequency
    /// oscillator. The format's default, and the most convincing.
    /// </summary>
    Adt,
}
