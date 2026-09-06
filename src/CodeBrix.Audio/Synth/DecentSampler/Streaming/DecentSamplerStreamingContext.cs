using System;

namespace CodeBrix.Audio.Synth.DecentSampler.Streaming;

// Everything one synthesizer needs to play streamed and lazily-decoded zones: the buffer pool, the
// instrument whose decoding it drives, and the mode that decides who does the reading.
//
// The shared reader thread holds these WEAKLY, so a synthesizer that is simply dropped takes its
// streaming work with it. There is no Dispose contract to get wrong, which matters because
// DecentSamplerSynthesizer has never had one and consumers do not expect one.
internal sealed class DecentSamplerStreamingContext
{
    private readonly DecentSamplerInstrument _instrument;

    private int _reportedExhaustion;

    public DecentSamplerStreamingContext(
        DecentSamplerInstrument instrument,
        DecentSamplerStreamingMode mode,
        StreamingVoicePool pool)
    {
        _instrument = instrument;
        Mode = mode;
        Pool = pool;
    }

    /// <summary>
    /// Whether the reader thread or the render thread does the reading. The synthesizer writes it when
    /// a consumer switches a synthesizer between a live device and an offline render.
    /// </summary>
    public DecentSamplerStreamingMode Mode { get; set; }

    /// <summary>The per-voice buffers, or null when nothing in this instrument streams.</summary>
    public StreamingVoicePool Pool { get; }

    /// <summary>The instrument, for the lazy decode queue and for problem reporting.</summary>
    public DecentSamplerInstrument Instrument => _instrument;

    /// <summary>
    /// Does whatever background work is outstanding: decodes the zones a note asked for, tops up every
    /// running ring buffer, and turns the audio thread's underrun flags into problem lines. Never
    /// called from a real-time audio thread.
    /// </summary>
    public void Service()
    {
        _instrument?.ServicePendingDecodes();

        if (Pool == null)
        {
            return;
        }

        // An offline context reads on its own render thread; there is nothing here to fill for it.
        if (Mode == DecentSamplerStreamingMode.RealTime)
        {
            Pool.Service();
        }

        Pool.CollectUnderruns(ReportUnderrun);

        if (Pool.ExhaustedCount > 0 && System.Threading.Interlocked.Exchange(ref _reportedExhaustion, 1) == 0)
        {
            _instrument?.AddRuntimeProblem(
                "streaming voice buffers ran out, so a note played silence: raise " +
                "DecentSamplerSynthesizerSettings.StreamingVoiceCount");
        }
    }

    private void ReportUnderrun(string fileName) =>
        _instrument?.AddRuntimeProblem(
            "streaming underrun on " + (fileName ?? "(unknown sample)") +
            ": the reader could not keep up, so the voice played silence");
}
