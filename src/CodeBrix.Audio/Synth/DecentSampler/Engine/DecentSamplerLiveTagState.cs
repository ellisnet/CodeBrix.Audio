using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

// The LIVE tag state: the values a TAG_ENABLED, TAG_VOLUME or TAG_POLYPHONY binding has moved.
//
// The parameter and binding engine keeps one DecentSamplerTagState for every tag named anywhere in the
// preset - on a <tag>, on a group, on a sample or oscillator, or in a binding's identifier - and
// writes the live value there. This adapter is what the voice runtime reads it through, so a knob that
// turns a microphone layer off silences that layer's next note and a knob that trims its volume moves
// the level of the notes already sounding.
//
// The static DecentSamplerStaticTagState remains for an instrument with no binding engine at all.
//
// Every member is a dictionary lookup on a pre-built map: no allocation, safe on the audio thread.
internal sealed class DecentSamplerLiveTagState : IDecentSamplerTagState
{
    private readonly Dictionary<string, DecentSamplerTagState> _states =
        new(StringComparer.OrdinalIgnoreCase);

    public DecentSamplerLiveTagState(IReadOnlyList<DecentSamplerTagState> states)
    {
        if (states == null)
        {
            return;
        }

        foreach (var state in states)
        {
            if (state != null && !string.IsNullOrWhiteSpace(state.Name))
            {
                _states[state.Name] = state;
            }
        }
    }

    // How many tags the binding engine is tracking. A preset with none falls back to the static state.
    public int Count => _states.Count;

    public bool IsEnabled(string tag) =>
        !_states.TryGetValue(tag ?? string.Empty, out var state) || state.Enabled;

    public double GetVolume(string tag) =>
        _states.TryGetValue(tag ?? string.Empty, out var state) ? state.Volume : 1.0;

    public int GetPolyphony(string tag) =>
        _states.TryGetValue(tag ?? string.Empty, out var state) ? state.Polyphony : -1;
}
