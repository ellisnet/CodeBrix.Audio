using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Effects;

// What every core effect shares: the parsed element it reads its live parameters from, the sample
// rate, the enabled flag, the tags, and the FX_* parameter surface.
//
// LIVE PARAMETERS. A binding can move any FX_* parameter while the instrument plays, and the binding
// engine writes the new value onto the parsed <effect>. An effect therefore reads what it needs at the
// top of every block (Refresh) rather than caching it at construction; the derived classes recompute
// coefficients only when a value actually changed, so a steady preset costs a handful of comparisons
// per block and nothing else.
//
// AUDIO-THREAD HYGIENE. Prepare is the only place an implementation allocates. Process allocates
// nothing, locks nothing and touches no file. The one exception is the convolution effect's impulse
// response, which is loaded off the audio thread and swapped in atomically; see that file.
internal abstract class DecentSamplerEffectBase : IInstrumentEffect
{
    private static readonly string[] NoTags = [];

    private readonly string[] _parameters;

    protected DecentSamplerEffectBase(EffectContext context, params string[] parameters)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        Context = context;
        Effect = context.Effect;
        SampleRate = context.SampleRate <= 0 ? 44100 : context.SampleRate;
        BlockSize = context.BlockSize <= 0 ? 64 : context.BlockSize;

        var tags = Effect.Tags;
        TagNames = tags == null || tags.Count == 0 ? NoTags : [.. tags];

        _parameters = new string[parameters == null ? 0 : parameters.Length];
        for (var i = 0; i < _parameters.Length; i++)
        {
            _parameters[i] = DecentSamplerEffectValues.Fold(parameters[i]);
        }
    }

    // Everything the factory was told, including the tempo source a tempo-synced effect follows.
    public EffectContext Context { get; }

    // The parsed element, which is where every live parameter value lives.
    public DecentSamplerEffect Effect { get; }

    public int SampleRate { get; private set; }

    // The frame count the engine renders in. An effect that needs a fixed transform size uses it.
    public int BlockSize { get; }

    // The tag names written on the element. A chain skips an effect whose tag is disabled.
    public string[] TagNames { get; }

    // Whether the effect processes. This is the parsed element's own Enabled, which is what an ENABLED
    // binding moves, so reading and writing here is reading and writing what the preset sees.
    public bool Enabled
    {
        get => Effect.Enabled;
        set => Effect.Enabled = value;
    }

    /// <inheritdoc/>
    IReadOnlyList<string> IInstrumentEffect.Tags => TagNames;

    /// <inheritdoc/>
    public void Prepare(int sampleRate)
    {
        SampleRate = sampleRate <= 0 ? 44100 : sampleRate;
        OnPrepare();
        Refresh(force: true);
        OnReset();
    }

    /// <inheritdoc/>
    public void Reset() => OnReset();

    /// <inheritdoc/>
    public void Process(float[] left, float[] right, int frames)
    {
        if (left == null || right == null || frames <= 0)
        {
            return;
        }

        Refresh(force: false);

        if (!Enabled)
        {
            return;
        }

        ProcessCore(left, right, Math.Min(frames, Math.Min(left.Length, right.Length)));
    }

    /// <inheritdoc/>
    public bool TrySetParameter(string name, double value)
    {
        var folded = DecentSamplerEffectValues.Fold(name);

        return Knows(folded) && DecentSamplerEffectValues.Set(Effect, folded, value);
    }

    /// <inheritdoc/>
    public bool TryGetParameter(string name, out double value)
    {
        var folded = DecentSamplerEffectValues.Fold(name);

        if (!Knows(folded))
        {
            value = 0.0;
            return false;
        }

        value = DecentSamplerEffectValues.Get(Effect, folded);
        return true;
    }

    /// <inheritdoc/>
    public bool TrySetParameter(string name, string value)
    {
        var folded = DecentSamplerEffectValues.Fold(name);

        return Knows(folded) && DecentSamplerEffectValues.SetText(Effect, folded, value);
    }

    // Allocates the buffers the effect needs at the current sample rate.
    protected abstract void OnPrepare();

    // Clears every internal buffer. Parameters are untouched.
    protected virtual void OnReset()
    {
    }

    // Picks up any live parameter the binding engine has moved. Called before every block, and once
    // with force set from Prepare.
    protected abstract void Refresh(bool force);

    protected abstract void ProcessCore(float[] left, float[] right, int frames);

    // ENABLED is common to every effect type; the rest are the ones this type documents.
    private bool Knows(string folded)
    {
        if (string.IsNullOrEmpty(folded))
        {
            return false;
        }

        if (string.Equals(folded, "enabled", StringComparison.Ordinal))
        {
            return true;
        }

        for (var i = 0; i < _parameters.Length; i++)
        {
            if (string.Equals(_parameters[i], folded, StringComparison.Ordinal))
            {
                return true;
            }
        }

        return false;
    }
}
