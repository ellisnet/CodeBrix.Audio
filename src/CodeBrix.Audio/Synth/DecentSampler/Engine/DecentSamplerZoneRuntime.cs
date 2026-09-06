using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler.Effects;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using CodeBrix.Audio.Synth.DecentSampler.Samples;
using CodeBrix.Audio.Synth.DecentSampler.Streaming;
using CodeBrix.Audio.Synth.Sfz;

namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

// Everything about one zone that does not change while the instrument plays: its decoded audio or its
// oscillator factory, its resolved loop bounds, the output slots it routes to, its tags, and which
// round-robin register it draws from.
//
// Anything a binding CAN change - volume, pan, tuning, the envelope, group enabled, key and velocity
// ranges - is deliberately NOT cached here. The voice reads those off the DecentSamplerZone and
// DecentSamplerGroup at note-on, and the gain-related ones again on every render block, so the
// parameter and binding engine's live values are honoured without this phase knowing about it.
internal sealed class DecentSamplerZoneRuntime
{
    private readonly Stack<IVoiceSource> _oscillatorPool = new Stack<IVoiceSource>();
    private readonly Func<OscillatorContext, IVoiceSource> _oscillatorFactory;
    private readonly OscillatorContext _oscillatorContext;

    private DecentSamplerZoneRuntime(DecentSamplerZone zone)
    {
        Zone = zone;
        Group = zone.Group;
        Tags = ToArray(zone.Tags);
        SilencedByTags = ToArray(zone.SilencedByTags);
        CcFilters = ToArray(zone.CcFilters);
        CcTriggers = ToArray(zone.CcTriggers);
        PreviousNotes = ToArray(zone.PreviousNotes);
    }

    private DecentSamplerZoneRuntime(
        DecentSamplerZone zone,
        Func<OscillatorContext, IVoiceSource> factory,
        OscillatorContext context)
        : this(zone)
    {
        _oscillatorFactory = factory;
        _oscillatorContext = context;
    }

    public DecentSamplerZone Zone { get; }

    public DecentSamplerGroup Group { get; }

    // The decoded audio, for a sample zone that resolved to a file. Null for an oscillator zone, for a
    // streamed zone, and for a sample whose file was missing.
    public SfzSampleData Data { get; private set; }

    // The streamed audio, for a sample zone the memory policy chose to stream. Null otherwise.
    public StreamingSampleSource Streaming { get; private set; }

    // The file this zone is still waiting to have decoded, for an instrument loaded with
    // DecodeSamples off. Null once it has been decoded, and for every other kind of zone.
    public DeferredSample Deferred { get; private set; }

    // Whether the zone plays from disk rather than from RAM.
    public bool IsStreaming => Streaming != null;

    // How many frames the zone's audio has, whichever way it is held.
    public long SourceFrames => Data?.Frames ?? Streaming?.Frames ?? 0;

    public string[] Tags { get; }

    public string[] SilencedByTags { get; }

    // The zone's loCCN/hiCCN filters, flattened so that matching a note-on allocates nothing.
    public DecentSamplerCcRange[] CcFilters { get; }

    // The zone's onLoCCN/onHiCCN triggers, flattened the same way.
    public DecentSamplerCcRange[] CcTriggers { get; }

    // The notes a legato zone accepts as its predecessor, flattened.
    public int[] PreviousNotes { get; }

    // Whether the zone can make any sound at all. A zone with a missing sample, or an oscillator whose
    // waveform no factory covers, is kept in the model, reported once, and never started.
    public bool IsPlayable =>
        Data != null || Streaming != null || Deferred != null || _oscillatorFactory != null;

    public bool IsOscillator => _oscillatorFactory != null;

    // The loop the zone plays, resolved once: explicit loopStart/loopEnd beat an embedded "smpl" chunk,
    // and loopEnabled defaults to true whenever a loop exists at all - both measured against the
    // reference player (plan section 7 item 11).
    public bool LoopEnabled { get; private set; }

    public long LoopStartFrame { get; private set; }

    public long LoopEndFrameInclusive { get; private set; }

    // What the last ResolveLoop was given, so that RefreshLoopIfMoved can run it again when a binding
    // has moved the zone's own loop attributes.
    private bool _loopResolved;
    private long _resolvedFrames;
    private long? _resolvedEmbeddedLoopStart;
    private long? _resolvedEmbeddedLoopEnd;
    private bool _resolvedHasEmbeddedLoop;
    private long? _resolvedLoopStart;
    private long? _resolvedLoopEnd;
    private bool? _resolvedLoopEnabled;

    // Whether the preset writes an envelope stage anywhere in the zone's inheritance chain. Only the
    // release matters: its undocumented default is 0.5 s, and the resolved model cannot tell "written
    // as zero" from "not written".
    public bool HasDeclaredRelease { get; private set; }

    // The round-robin register this zone draws from: -1 for the instrument-wide one that an omitted
    // seqLength shares, otherwise the group index, because a group with an explicit seqLength keeps a
    // counter of its own (measured, plan section 7 item 9).
    public int SequenceRegisterKey { get; private set; }

    // The length of that register's queue.
    public int SequenceLength { get; private set; }

    // The group's <effects> element as a per-voice template, or null when the group declares none.
    // Every zone of one group shares the pool, and each voice rents its own chain from it.
    public DecentSamplerGroupChainPool GroupChains { get; private set; }

    public static DecentSamplerZoneRuntime ForSample(DecentSamplerZone zone, SfzSampleData data)
    {
        var runtime = new DecentSamplerZoneRuntime(zone);
        runtime.SetSampleData(data);
        return runtime;
    }

    public static DecentSamplerZoneRuntime ForStreamedSample(
        DecentSamplerZone zone, StreamingSampleSource source)
    {
        var runtime = new DecentSamplerZoneRuntime(zone);
        runtime.Streaming = source;
        runtime.ResolveLoop(
            source.Frames, source.EmbeddedLoopStart, source.EmbeddedLoopEnd, source.HasEmbeddedLoop);
        return runtime;
    }

    // A zone whose file has not been decoded yet. It counts as playable, because it will be as soon as
    // a note asks for it; the note that asks is silent.
    public static DecentSamplerZoneRuntime ForDeferredSample(
        DecentSamplerZone zone, DeferredSample deferred)
    {
        var runtime = new DecentSamplerZoneRuntime(zone);
        runtime.Deferred = deferred;
        return runtime;
    }

    // Takes the decoded audio a worker produced, if it has arrived. Called on the note-on path, so it
    // does no work beyond a volatile read until the audio is actually there.
    public bool TryResolveDeferred()
    {
        if (Deferred == null)
        {
            return Data != null;
        }

        if (Deferred.Source is InMemorySampleSource memory)
        {
            SetSampleData(memory.Data);
            Deferred = null;
            return true;
        }

        if (Deferred.Source is StreamingSampleSource streaming)
        {
            Streaming = streaming;
            ResolveLoop(
                streaming.Frames, streaming.EmbeddedLoopStart, streaming.EmbeddedLoopEnd,
                streaming.HasEmbeddedLoop);

            Deferred = null;
            return true;
        }

        return false;
    }

    private void SetSampleData(SfzSampleData data)
    {
        Data = data;

        if (data != null)
        {
            ResolveLoop(data.Frames, data.EmbeddedLoopStart, data.EmbeddedLoopEnd, data.HasEmbeddedLoop);
        }
    }

    public static DecentSamplerZoneRuntime ForOscillator(
        DecentSamplerZone zone, Func<OscillatorContext, IVoiceSource> factory, OscillatorContext context)
    {
        return new DecentSamplerZoneRuntime(zone, factory, context);
    }

    public static DecentSamplerZoneRuntime Silent(DecentSamplerZone zone) =>
        new DecentSamplerZoneRuntime(zone);

    public void SetEnvelopeFacts(bool hasDeclaredRelease) => HasDeclaredRelease = hasDeclaredRelease;

    public void SetGroupChains(DecentSamplerGroupChainPool pool) => GroupChains = pool;

    public void SetSequence(int registerKey, int length)
    {
        SequenceRegisterKey = registerKey;
        SequenceLength = length;
    }

    // Takes an oscillator source for one voice, reusing a returned one so that a steady stream of notes
    // allocates nothing. Returns null for a sample zone.
    public IVoiceSource RentOscillator()
    {
        if (_oscillatorFactory == null)
        {
            return null;
        }

        if (_oscillatorPool.Count > 0)
        {
            return _oscillatorPool.Pop();
        }

        return _oscillatorFactory(_oscillatorContext);
    }

    public void ReturnOscillator(IVoiceSource source)
    {
        if (source != null && _oscillatorFactory != null)
        {
            _oscillatorPool.Push(source);
        }
    }

    // Re-resolves the loop when a binding has moved the zone's own loop points since the last
    // resolution, and does nothing at all when it has not. Called on the note-on path, so it is three
    // nullable comparisons in the ordinary case and allocates nothing either way.
    //
    // A SAMPLE_START, SAMPLE_END, LOOP_START or LOOP_END binding therefore takes effect at the NEXT
    // NOTE-ON and a sounding voice keeps the bounds it started with. Nothing about this was measured -
    // the guide says only that the four parameters need in-memory playback and says nothing about a
    // sounding voice - so this engine takes the cheaper of the two defensible readings. Following a
    // change live would mean re-resolving every sounding voice from the render callback for a
    // parameter presets drive from a knob.
    public void RefreshLoopIfMoved()
    {
        if (!_loopResolved ||
            (Zone.LoopStart == _resolvedLoopStart &&
             Zone.LoopEnd == _resolvedLoopEnd &&
             Zone.LoopEnabled == _resolvedLoopEnabled))
        {
            return;
        }

        ResolveLoop(
            _resolvedFrames, _resolvedEmbeddedLoopStart, _resolvedEmbeddedLoopEnd,
            _resolvedHasEmbeddedLoop);
    }

    private void ResolveLoop(long frames, long? embeddedLoopStart, long? embeddedLoopEnd, bool hasEmbeddedLoop)
    {
        _loopResolved = true;
        _resolvedFrames = frames;
        _resolvedEmbeddedLoopStart = embeddedLoopStart;
        _resolvedEmbeddedLoopEnd = embeddedLoopEnd;
        _resolvedHasEmbeddedLoop = hasEmbeddedLoop;
        _resolvedLoopStart = Zone.LoopStart;
        _resolvedLoopEnd = Zone.LoopEnd;
        _resolvedLoopEnabled = Zone.LoopEnabled;

        var lastFrame = Math.Max(0, frames - 1);

        var declared = Zone.LoopStart.HasValue || Zone.LoopEnd.HasValue;
        var embedded = hasEmbeddedLoop;

        if (!declared && !embedded)
        {
            // MEASURED (round 2, item 25): a zone with no loop points PLAYS ONCE AND STOPS - a
            // one-second sample under a key held for four seconds sounded for exactly one second.
            //
            // A trigger="continuous" zone is the one exception this engine makes, and it is a
            // DELIBERATE DIVERGENCE: in the reference, a trigger="continuous" zone produced no sound
            // at all, at any point of a four-second window, so there is nothing to match. The guide
            // says such a zone "will always play", so here it does - and with no loop of its own it
            // loops over the whole file rather than falling silent after one pass.
            var continuous = Zone.Trigger == DecentSamplerTrigger.Continuous;

            LoopEnabled = Zone.LoopEnabled ?? continuous;
            LoopStartFrame = 0;
            LoopEndFrameInclusive = lastFrame;
            return;
        }

        // Explicit attributes override the embedded markers, attribute by attribute.
        var loopStart = Zone.LoopStart ?? embeddedLoopStart ?? 0;
        var loopEnd = Zone.LoopEnd ?? embeddedLoopEnd ?? lastFrame;

        loopStart = Math.Clamp(loopStart, 0, lastFrame);
        loopEnd = Math.Clamp(loopEnd, loopStart, lastFrame);

        LoopStartFrame = loopStart;
        LoopEndFrameInclusive = loopEnd;
        LoopEnabled = (Zone.LoopEnabled ?? true) && loopEnd > loopStart;
    }

    private static DecentSamplerCcRange[] ToArray(IReadOnlyDictionary<int, DecentSamplerCcRange> ranges)
    {
        if (ranges == null || ranges.Count == 0)
        {
            return [];
        }

        var result = new DecentSamplerCcRange[ranges.Count];
        var index = 0;
        foreach (var pair in ranges)
        {
            result[index++] = pair.Value;
        }

        return result;
    }

    private static int[] ToArray(IReadOnlyList<int> notes)
    {
        if (notes == null || notes.Count == 0)
        {
            return [];
        }

        var result = new int[notes.Count];
        for (var i = 0; i < notes.Count; i++)
        {
            result[i] = notes[i];
        }

        return result;
    }

    private static string[] ToArray(IReadOnlyList<string> tags)
    {
        if (tags == null || tags.Count == 0)
        {
            return [];
        }

        var result = new string[tags.Count];
        for (var i = 0; i < tags.Count; i++)
        {
            result[i] = tags[i];
        }

        return result;
    }

    public override string ToString() =>
        (IsOscillator ? "oscillator " + Zone.WaveformName
            : (IsStreaming ? "streamed sample " : "sample ") + Zone.Path) +
        " (group " + Group.Index + ")";
}
