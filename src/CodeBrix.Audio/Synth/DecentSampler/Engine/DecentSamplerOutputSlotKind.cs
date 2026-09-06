namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

/// <summary>What kind of destination an output slot is.</summary>
public enum DecentSamplerOutputSlotKind
{
    /// <summary>Audio sent here is discarded. <c>NO_OUTPUT</c>.</summary>
    None,

    /// <summary>The main stereo mix. <c>MAIN_OUTPUT</c>.</summary>
    Main,

    /// <summary>One of the sixteen buses. <c>BUS_1</c> through <c>BUS_16</c>.</summary>
    Bus,

    /// <summary>One of the sixteen auxiliary stereo pairs. <c>AUX_STEREO_OUTPUT_1</c> through 16.</summary>
    Aux,
}
