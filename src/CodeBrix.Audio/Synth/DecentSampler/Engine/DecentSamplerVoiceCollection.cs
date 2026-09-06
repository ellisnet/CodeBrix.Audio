using System;
using System.Collections;
using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

// The fixed pool of Decent Sampler voices, allocated once. Follows SfzVoiceCollection exactly: free
// voices come off the end, an exhausted pool steals the lowest-priority (then oldest) active voice, and
// Process compacts finished voices out of the active range.
internal sealed class DecentSamplerVoiceCollection
{
    private readonly DecentSamplerSynthesizer _synthesizer;
    private readonly DecentSamplerVoice[] _voices;

    private int _activeVoiceCount;

    public DecentSamplerVoiceCollection(DecentSamplerSynthesizer synthesizer, int sampleRate, int blockSize, int maximumPolyphony)
    {
        _synthesizer = synthesizer;
        _voices = new DecentSamplerVoice[maximumPolyphony];
        for (var i = 0; i < _voices.Length; i++)
        {
            // The identifier is the voice's slot in the pool: stable for the life of the
            // synthesizer, bounded by the polyphony, and therefore what the modulation runtime keys
            // its per-voice state and its per-voice contributions on.
            _voices[i] = new DecentSamplerVoice(synthesizer, sampleRate, blockSize, i);
        }

        _activeVoiceCount = 0;
    }

    public int ActiveVoiceCount => _activeVoiceCount;

    public int Capacity => _voices.Length;

    public DecentSamplerVoice RequestNew()
    {
        if (_activeVoiceCount < _voices.Length)
        {
            var free = _voices[_activeVoiceCount];
            _activeVoiceCount++;
            return free;
        }

        DecentSamplerVoice candidate = null;
        var lowestPriority = float.MaxValue;

        for (var i = 0; i < _activeVoiceCount; i++)
        {
            var voice = _voices[i];
            var priority = voice.Priority;

            if (priority < lowestPriority)
            {
                lowestPriority = priority;
                candidate = voice;
            }
            else if (priority == lowestPriority && candidate != null && voice.VoiceLength > candidate.VoiceLength)
            {
                candidate = voice;
            }
        }

        candidate?.Recycle();
        return candidate;
    }

    public void Process()
    {
        var i = 0;

        while (true)
        {
            if (i == _activeVoiceCount)
            {
                return;
            }

            if (ProcessOne(_voices[i]))
            {
                i++;
            }
            else
            {
                _activeVoiceCount--;

                var swap = _voices[i];
                _voices[i] = _voices[_activeVoiceCount];
                _voices[_activeVoiceCount] = swap;
            }
        }
    }

    // Renders one voice with its own modulation in place. A voice-scope modulator's contribution
    // does not reach the group and zone properties the voice reads, so the runtime publishes this
    // voice's own effective values first and puts the global ones back afterwards.
    private bool ProcessOne(DecentSamplerVoice voice)
    {
        var modulation = _synthesizer.Modulation;

        if (modulation == null || !modulation.HasVoiceScope)
        {
            return voice.Process();
        }

        modulation.PushVoice(voice.Id);

        try
        {
            return voice.Process();
        }
        finally
        {
            modulation.PopVoice();
        }
    }

    public void Clear()
    {
        for (var i = 0; i < _activeVoiceCount; i++)
        {
            _voices[i].Recycle();
        }

        _activeVoiceCount = 0;
    }

    public Enumerator GetEnumerator() => new Enumerator(this);

    public struct Enumerator : IEnumerator<DecentSamplerVoice>
    {
        private readonly DecentSamplerVoiceCollection _collection;

        private int _index;
        private DecentSamplerVoice _current;

        internal Enumerator(DecentSamplerVoiceCollection collection)
        {
            _collection = collection;
            _index = 0;
            _current = null;
        }

        public DecentSamplerVoice Current => _current;

        object IEnumerator.Current => throw new NotSupportedException();

        public bool MoveNext()
        {
            if (_index < _collection._activeVoiceCount)
            {
                _current = _collection._voices[_index];
                _index++;
                return true;
            }

            return false;
        }

        public void Reset()
        {
            _index = 0;
            _current = null;
        }

        public void Dispose()
        {
        }
    }
}
