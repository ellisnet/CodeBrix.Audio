namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// Where a zone, group or bus sends its audio.
/// </summary>
/// <remarks>
/// Buses are only meaningful when the preset declares a matching <c>&lt;bus&gt;</c> element;
/// auxiliary outputs are separate stereo pairs that a host may collect on their own.
/// </remarks>
public enum DecentSamplerOutputTarget
{
    /// <summary>The main stereo output (<c>MAIN_OUTPUT</c>). The default for output 1.</summary>
    MainOutput,

    /// <summary>Nothing; the audio is discarded (<c>NO_OUTPUT</c>). The default for outputs 2 to 8.</summary>
    NoOutput,

    /// <summary>Bus 1, the first <c>&lt;bus&gt;</c> element (<c>BUS_1</c>).</summary>
    Bus1,

    /// <summary>Bus 2, the second <c>&lt;bus&gt;</c> element (<c>BUS_2</c>).</summary>
    Bus2,

    /// <summary>Bus 3, the third <c>&lt;bus&gt;</c> element (<c>BUS_3</c>).</summary>
    Bus3,

    /// <summary>Bus 4, the fourth <c>&lt;bus&gt;</c> element (<c>BUS_4</c>).</summary>
    Bus4,

    /// <summary>Bus 5, the fifth <c>&lt;bus&gt;</c> element (<c>BUS_5</c>).</summary>
    Bus5,

    /// <summary>Bus 6, the sixth <c>&lt;bus&gt;</c> element (<c>BUS_6</c>).</summary>
    Bus6,

    /// <summary>Bus 7, the seventh <c>&lt;bus&gt;</c> element (<c>BUS_7</c>).</summary>
    Bus7,

    /// <summary>Bus 8, the eighth <c>&lt;bus&gt;</c> element (<c>BUS_8</c>).</summary>
    Bus8,

    /// <summary>Bus 9, the ninth <c>&lt;bus&gt;</c> element (<c>BUS_9</c>).</summary>
    Bus9,

    /// <summary>Bus 10, the tenth <c>&lt;bus&gt;</c> element (<c>BUS_10</c>).</summary>
    Bus10,

    /// <summary>Bus 11, the eleventh <c>&lt;bus&gt;</c> element (<c>BUS_11</c>).</summary>
    Bus11,

    /// <summary>Bus 12, the twelfth <c>&lt;bus&gt;</c> element (<c>BUS_12</c>).</summary>
    Bus12,

    /// <summary>Bus 13, the thirteenth <c>&lt;bus&gt;</c> element (<c>BUS_13</c>).</summary>
    Bus13,

    /// <summary>Bus 14, the fourteenth <c>&lt;bus&gt;</c> element (<c>BUS_14</c>).</summary>
    Bus14,

    /// <summary>Bus 15, the fifteenth <c>&lt;bus&gt;</c> element (<c>BUS_15</c>).</summary>
    Bus15,

    /// <summary>Bus 16, the sixteenth <c>&lt;bus&gt;</c> element (<c>BUS_16</c>).</summary>
    Bus16,

    /// <summary>Auxiliary stereo output 1 (<c>AUX_STEREO_OUTPUT_1</c>).</summary>
    AuxStereoOutput1,

    /// <summary>Auxiliary stereo output 2 (<c>AUX_STEREO_OUTPUT_2</c>).</summary>
    AuxStereoOutput2,

    /// <summary>Auxiliary stereo output 3 (<c>AUX_STEREO_OUTPUT_3</c>).</summary>
    AuxStereoOutput3,

    /// <summary>Auxiliary stereo output 4 (<c>AUX_STEREO_OUTPUT_4</c>).</summary>
    AuxStereoOutput4,

    /// <summary>Auxiliary stereo output 5 (<c>AUX_STEREO_OUTPUT_5</c>).</summary>
    AuxStereoOutput5,

    /// <summary>Auxiliary stereo output 6 (<c>AUX_STEREO_OUTPUT_6</c>).</summary>
    AuxStereoOutput6,

    /// <summary>Auxiliary stereo output 7 (<c>AUX_STEREO_OUTPUT_7</c>).</summary>
    AuxStereoOutput7,

    /// <summary>Auxiliary stereo output 8 (<c>AUX_STEREO_OUTPUT_8</c>).</summary>
    AuxStereoOutput8,

    /// <summary>Auxiliary stereo output 9 (<c>AUX_STEREO_OUTPUT_9</c>).</summary>
    AuxStereoOutput9,

    /// <summary>Auxiliary stereo output 10 (<c>AUX_STEREO_OUTPUT_10</c>).</summary>
    AuxStereoOutput10,

    /// <summary>Auxiliary stereo output 11 (<c>AUX_STEREO_OUTPUT_11</c>).</summary>
    AuxStereoOutput11,

    /// <summary>Auxiliary stereo output 12 (<c>AUX_STEREO_OUTPUT_12</c>).</summary>
    AuxStereoOutput12,

    /// <summary>Auxiliary stereo output 13 (<c>AUX_STEREO_OUTPUT_13</c>).</summary>
    AuxStereoOutput13,

    /// <summary>Auxiliary stereo output 14 (<c>AUX_STEREO_OUTPUT_14</c>).</summary>
    AuxStereoOutput14,

    /// <summary>Auxiliary stereo output 15 (<c>AUX_STEREO_OUTPUT_15</c>).</summary>
    AuxStereoOutput15,

    /// <summary>Auxiliary stereo output 16 (<c>AUX_STEREO_OUTPUT_16</c>).</summary>
    AuxStereoOutput16,
}
