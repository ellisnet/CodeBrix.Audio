using System;
using System.Threading;

namespace CodeBrix.Audio.Synth.DecentSampler.Streaming;

// The per-voice streaming buffers, all allocated once when the synthesizer is built and handed out at
// note-on. Renting is a compare-and-swap over a fixed array, so the audio thread neither allocates nor
// locks; a pool with nothing free means the note plays silence and the instrument says so, which is the
// same failure mode as running out of voices.
//
// Every buffer carries a stereo ring whether or not the sample is stereo, because a pool cannot know
// which zone will want it. The default 32 buffers of 8192 frames cost 2 MB - two thousandths of what
// the samples they stand in for would.
internal sealed class StreamingVoicePool
{
    private readonly StreamingVoiceBuffer[] _buffers;

    private int _exhaustedCount;

    public StreamingVoicePool(int count, int ringFrames)
    {
        var size = Math.Clamp(count, 1, 1024);
        _buffers = new StreamingVoiceBuffer[size];

        for (var i = 0; i < size; i++)
        {
            _buffers[i] = new StreamingVoiceBuffer(2, ringFrames);
        }
    }

    /// <summary>How many buffers the pool holds.</summary>
    public int Count => _buffers.Length;

    /// <summary>How many buffers are in use right now.</summary>
    public int InUseCount
    {
        get
        {
            var used = 0;
            foreach (var buffer in _buffers)
            {
                if (buffer.State != StreamingVoiceBuffer.StateFree)
                {
                    used++;
                }
            }

            return used;
        }
    }

    /// <summary>
    /// Takes a free buffer, or null when every one is busy. Safe to call from the audio thread: it
    /// allocates nothing and takes no lock.
    /// </summary>
    /// <returns>The buffer, already marked as priming, or null.</returns>
    public StreamingVoiceBuffer Rent()
    {
        for (var i = 0; i < _buffers.Length; i++)
        {
            var buffer = _buffers[i];

            if (buffer.TryClaim())
            {
                return buffer;
            }
        }

        Interlocked.Increment(ref _exhaustedCount);
        return null;
    }

    /// <summary>How many note-ons found every buffer busy.</summary>
    public int ExhaustedCount => Volatile.Read(ref _exhaustedCount);

    /// <summary>Fills every rented buffer that has room. Never called from the audio thread.</summary>
    public void Service()
    {
        for (var i = 0; i < _buffers.Length; i++)
        {
            var buffer = _buffers[i];

            // Priming buffers belong to the note-on that claimed them until it says otherwise; only
            // a running one is the reader's to fill. A released one comes back to the pool here, on a
            // pass after the one that might have been filling it.
            if (buffer.State == StreamingVoiceBuffer.StateRunning)
            {
                buffer.Service();
            }
            else if (buffer.State == StreamingVoiceBuffer.StateReleasing)
            {
                buffer.MarkFree();
            }
        }
    }

    /// <summary>
    /// Collects the underruns the audio thread flagged, clearing each as it goes.
    /// </summary>
    /// <param name="report">Called once per starved buffer with the sample's name.</param>
    public void CollectUnderruns(Action<string> report)
    {
        for (var i = 0; i < _buffers.Length; i++)
        {
            if (_buffers[i].TakeUnderrun(out var fileName))
            {
                report(fileName);
            }
        }
    }

    /// <summary>Every buffer, for the tests that count rentals.</summary>
    /// <returns>The buffers, in pool order.</returns>
    public StreamingVoiceBuffer[] Buffers() => _buffers;
}
