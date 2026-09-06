using System;
using System.Collections.Generic;
using System.Threading;

namespace CodeBrix.Audio.Synth.DecentSampler.Streaming;

// The one background thread that feeds every streamed voice in the process.
//
// WHY ONE, SHARED. A thread per synthesizer needs a lifetime, and DecentSamplerSynthesizer has never
// been disposable - a multi-track player builds one per track and drops them. So the reader is a
// process-wide background thread holding WEAK references to the synthesizers' streaming contexts: a
// dropped synthesizer simply stops being serviced at the next collection, and the thread never keeps
// the process alive because it is a background thread.
//
// It wakes on a signal (a note-on that needs data) and otherwise on a short timer, because a running
// voice consumes its ring continuously and there is nothing to signal about. One millisecond of latency
// against an 8192-frame ring - 186 ms at 44.1 kHz - is a wide margin.
internal sealed class DecentSamplerStreamingReader
{
    private const int IdlePollMilliseconds = 200;
    private const int BusyPollMilliseconds = 1;

    private static readonly Lazy<DecentSamplerStreamingReader> LazyShared =
        new(() => new DecentSamplerStreamingReader(), LazyThreadSafetyMode.ExecutionAndPublication);

    private readonly List<WeakReference<DecentSamplerStreamingContext>> _contexts = [];
    private readonly List<DecentSamplerStreamingContext> _live = [];
    private readonly object _gate = new object();
    private readonly AutoResetEvent _signal = new AutoResetEvent(false);

    private Thread _thread;

    private DecentSamplerStreamingReader()
    {
    }

    /// <summary>The process-wide reader.</summary>
    public static DecentSamplerStreamingReader Shared => LazyShared.Value;

    /// <summary>How many contexts the reader is still servicing. For the tests.</summary>
    public int ContextCount
    {
        get
        {
            lock (_gate)
            {
                return _contexts.Count;
            }
        }
    }

    /// <summary>
    /// Starts servicing a synthesizer's streaming work, starting the reader thread on first use.
    /// </summary>
    /// <param name="context">The context to service. Held weakly.</param>
    public void Register(DecentSamplerStreamingContext context)
    {
        if (context == null)
        {
            return;
        }

        lock (_gate)
        {
            // Registering twice would service the context twice a round, so a synthesizer switched from
            // the offline mode back to the real-time one re-registers without duplicating itself.
            for (var i = 0; i < _contexts.Count; i++)
            {
                if (_contexts[i].TryGetTarget(out var existing) && ReferenceEquals(existing, context))
                {
                    _signal.Set();
                    return;
                }
            }

            _contexts.Add(new WeakReference<DecentSamplerStreamingContext>(context));

            if (_thread == null)
            {
                _thread = new Thread(Run)
                {
                    IsBackground = true,
                    Name = "CodeBrix.Audio Decent Sampler streaming",
                };

                _thread.Start();
            }
        }

        _signal.Set();
    }

    /// <summary>
    /// Stops servicing a synthesizer's streaming work, because its render call has taken the reading
    /// over. Doing nothing when the context was never registered.
    /// </summary>
    /// <param name="context">The context to drop.</param>
    public void Unregister(DecentSamplerStreamingContext context)
    {
        if (context == null)
        {
            return;
        }

        lock (_gate)
        {
            for (var i = _contexts.Count - 1; i >= 0; i--)
            {
                if (!_contexts[i].TryGetTarget(out var existing) || ReferenceEquals(existing, context))
                {
                    _contexts.RemoveAt(i);
                }
            }
        }
    }

    /// <summary>Asks the reader to do a round now rather than at the next poll.</summary>
    public void Wake() => _signal.Set();

    private void Run()
    {
        while (true)
        {
            var busy = ServiceRound();
            _signal.WaitOne(busy ? BusyPollMilliseconds : IdlePollMilliseconds);
        }
    }

    // One servicing round, in a frame of its own.
    //
    // THE FRAME BOUNDARY IS LOAD-BEARING. A strong reference to a context taken in Run's OWN frame -
    // a foreach variable, an enumerator's Current - can stay live in a register or a stack slot for
    // as long as that frame lives, and Run's frame lives for the whole process. That would pin the
    // last context serviced, and through it the whole instrument, defeating the weak references this
    // class exists for: a dropped synthesizer would never be forgotten and its samples never freed.
    // Every strong reference is therefore taken inside this method, which returns before the wait.
    private bool ServiceRound()
    {
        var busy = false;

        lock (_gate)
        {
            _live.Clear();

            for (var i = _contexts.Count - 1; i >= 0; i--)
            {
                if (_contexts[i].TryGetTarget(out var context))
                {
                    _live.Add(context);
                }
                else
                {
                    _contexts.RemoveAt(i);
                }
            }
        }

        for (var i = 0; i < _live.Count; i++)
        {
            try
            {
                var context = _live[i];
                context.Service();
                busy |= context.Pool != null && context.Pool.InUseCount > 0;
            }
            catch (Exception)
            {
                // A reader that throws must not take the process down. The voice it was filling
                // starves, which the audio thread already reports as an underrun.
            }

            // Dropped as soon as it has been serviced, so a round never holds the previous round's
            // contexts either.
            _live[i] = null;
        }

        _live.Clear();
        return busy;
    }
}
