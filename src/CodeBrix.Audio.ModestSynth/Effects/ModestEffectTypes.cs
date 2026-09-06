using System.Collections.Generic;

namespace CodeBrix.Audio.ModestSynth.Effects;

/// <summary>
/// The effect type names this package registers, spelled as a preset spells them.
/// </summary>
/// <remarks>
/// The core package carries the mixing and room effects - filters, gain, reverb, delay, chorus,
/// convolution and the compressor - and this add-on carries the creative ones. A name that is not
/// here belongs to the core.
/// </remarks>
public static class ModestEffectTypes
{
    /// <summary>An all-pass cascade swept by a low-frequency oscillator.</summary>
    public const string Phaser = "phaser";

    /// <summary>A semitone pitch shifter.</summary>
    public const string PitchShift = "pitch_shift";

    /// <summary>A triangle wave folder.</summary>
    public const string WaveFolder = "wave_folder";

    /// <summary>A saturating wave shaper, optionally oversampled.</summary>
    public const string WaveShaper = "wave_shaper";

    /// <summary>A mono-to-pseudo-stereo widener with three algorithms.</summary>
    public const string StereoSimulator = "stereo_simulator";

    /// <summary>A bit-depth and sample-rate reducer.</summary>
    public const string BitCrusher = "bit_crusher";

    /// <summary>A randomised stutter gate.</summary>
    public const string Gate = "gate";

    private static readonly string[] AllTypes =
    {
        Phaser, PitchShift, WaveFolder, WaveShaper, StereoSimulator, BitCrusher, Gate,
    };

    /// <summary>Every effect type this package supplies, in the order the guide documents them.</summary>
    public static IReadOnlyList<string> All => AllTypes;
}
