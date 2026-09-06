using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

// The live state of the instrument's tags, as the voice runtime reads it every render block.
//
// THE CONTRACT FOR THE PARAMETER AND BINDING PHASE. TAG_ENABLED, TAG_VOLUME and TAG_POLYPHONY are
// bindable, so a knob or a button can turn a microphone layer off, trim its level, or switch a legato
// group between monophonic and polyphonic while the instrument plays. The sampler engine never caches
// these: it asks this interface per block for volume and enabled, and at note-on for polyphony.
// Supplying a live implementation is all the binding engine has to do; the default one below reads the
// parsed <tag> elements, so a preset with no bindings already behaves correctly.
//
// Every member must be cheap, allocation-free and safe to call from the audio thread.
internal interface IDecentSamplerTagState
{
    // Whether zones carrying the tag sound. A zone is silent if ANY of its tags is disabled.
    bool IsEnabled(string tag);

    // The tag's linear volume, 1.0 when it has none. A zone's tag volumes multiply together.
    double GetVolume(string tag);

    // How many voices carrying the tag may sound at once, or -1 for unlimited.
    int GetPolyphony(string tag);
}

// The default tag state: the values written on the preset's own <tag> elements, and the documented
// defaults for a tag that has no element of its own.
internal sealed class DecentSamplerStaticTagState : IDecentSamplerTagState
{
    private readonly Dictionary<string, DecentSamplerTag> _tags =
        new(StringComparer.OrdinalIgnoreCase);

    public DecentSamplerStaticTagState(IReadOnlyList<DecentSamplerTag> tags)
    {
        if (tags == null)
        {
            return;
        }

        foreach (var tag in tags)
        {
            if (!string.IsNullOrWhiteSpace(tag?.Name))
            {
                _tags[tag.Name] = tag;
            }
        }
    }

    public bool IsEnabled(string tag) =>
        !_tags.TryGetValue(tag ?? string.Empty, out var entry) || (entry.Enabled ?? true);

    public double GetVolume(string tag) =>
        _tags.TryGetValue(tag ?? string.Empty, out var entry) ? entry.Volume ?? 1.0 : 1.0;

    public int GetPolyphony(string tag) =>
        _tags.TryGetValue(tag ?? string.Empty, out var entry) ? entry.Polyphony ?? -1 : -1;
}
