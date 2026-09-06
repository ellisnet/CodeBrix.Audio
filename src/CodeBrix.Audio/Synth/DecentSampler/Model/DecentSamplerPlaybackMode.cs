namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// Where a zone's audio is played from.
/// </summary>
public enum DecentSamplerPlaybackMode
{
    /// <summary>Fully decoded into RAM (<c>memory</c>).</summary>
    Memory,

    /// <summary>Streamed from disk (<c>disk_streaming</c>).</summary>
    DiskStreaming,

    /// <summary>The engine chooses, using the thresholds in the load options (<c>auto</c>). The default.</summary>
    Auto,
}
