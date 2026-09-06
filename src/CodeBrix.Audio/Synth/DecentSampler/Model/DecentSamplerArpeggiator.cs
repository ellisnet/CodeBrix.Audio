namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// The <c>&lt;arpeggiator&gt;</c> element: one per preset, generating notes from whatever chord is held.
/// </summary>
/// <remarks>
/// When the arpeggiator is armed it consumes every note played and emits its own, so the raw notes do
/// not also reach the sample engine.
/// </remarks>
public sealed class DecentSamplerArpeggiator : DecentSamplerElement
{
    /// <summary>Whether the arpeggiator is armed (<c>enabled</c>). Default false.</summary>
    public bool? Enabled { get; internal set; }

    /// <summary>The order held notes are played in (<c>arpOrder</c>). Default up.</summary>
    public DecentSamplerArpOrder? Order { get; internal set; }

    /// <summary>How many octaves the pattern spans, 1 to 8 (<c>arpOctaveRange</c>). Default 1.</summary>
    public int? OctaveRange { get; internal set; }

    /// <summary>How octave copies are woven in (<c>arpOctaveMode</c>). Default replay per octave.</summary>
    public DecentSamplerArpOctaveMode? OctaveMode { get; internal set; }

    /// <summary>A ceiling on the notes in one cycle, 1 to 16 (<c>arpStepCount</c>). Default 16.</summary>
    public int? StepCount { get; internal set; }

    /// <summary>How much of a step each note rings for (<c>arpGateLength</c>). Default 0.75.</summary>
    public double? GateLength { get; internal set; }

    /// <summary>Whether the rate tracks the host tempo (<c>arpFollowGlobalTempo</c>). Default true.</summary>
    public bool? FollowGlobalTempo { get; internal set; }

    /// <summary>The note value of one step (<c>arpSyncDivision</c>). Default a 16th note.</summary>
    public DecentSamplerSyncDivision? SyncDivision { get; internal set; }

    /// <summary>A continuous rate multiplier, 0.01 to 100 (<c>arpRateMultiplier</c>). Default 1.</summary>
    public double? RateMultiplier { get; internal set; }

    /// <summary>
    /// The tempo used when the arpeggiator does not follow the host, 1 to 1000
    /// (<c>arpOverrideBpm</c>). Default 120.
    /// </summary>
    public double? OverrideBpm { get; internal set; }
}
