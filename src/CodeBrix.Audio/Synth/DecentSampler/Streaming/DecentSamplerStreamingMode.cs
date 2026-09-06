namespace CodeBrix.Audio.Synth.DecentSampler.Streaming;

/// <summary>
/// How a synthesizer gets streamed audio off the disk and into its voices.
/// </summary>
public enum DecentSamplerStreamingMode
{
    /// <summary>
    /// A background reader thread fills the voices' buffers; the render call never touches a file. A
    /// voice the reader could not keep up with plays silence for the block it was short and the
    /// instrument records one problem naming the sample. The default, and the only safe choice when
    /// the render call is a real audio callback.
    /// </summary>
    RealTime,

    /// <summary>
    /// The render call fills its own voices' buffers before rendering them, so a streamed voice can
    /// never fall behind however fast the renderer runs. Reading a file on the render thread is exactly
    /// what an offline render wants and exactly what a live audio callback must not do, so this is for
    /// rendering to a file, to a buffer or in a test - not for playback.
    /// </summary>
    Offline,
}
