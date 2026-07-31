using System;
using System.Collections;
using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.Sfz;

// The fixed pool of SFZ voices, allocated once. Follows the SoundFont VoiceCollection exactly: free
// voices come off the end, exhausted ones steal the lowest-priority (then oldest) active voice, and
// Process compacts finished voices out of the active range.
internal sealed class SfzVoiceCollection
{
    private readonly SfzVoice[] voices;

    private int activeVoiceCount;

    internal SfzVoiceCollection(SfzSynthesizer synthesizer, int maxActiveVoiceCount)
    {
        voices = new SfzVoice[maxActiveVoiceCount];
        for (var i = 0; i < voices.Length; i++)
        {
            voices[i] = new SfzVoice(synthesizer);
        }

        activeVoiceCount = 0;
    }

    public SfzVoice RequestNew()
    {
        if (activeVoiceCount < voices.Length)
        {
            var free = voices[activeVoiceCount];
            activeVoiceCount++;
            return free;
        }

        SfzVoice candidate = null;
        var lowestPriority = float.MaxValue;
        for (var i = 0; i < activeVoiceCount; i++)
        {
            var voice = voices[i];
            var priority = voice.Priority;
            if (priority < lowestPriority)
            {
                lowestPriority = priority;
                candidate = voice;
            }
            else if (priority == lowestPriority && voice.VoiceLength > candidate.VoiceLength)
            {
                candidate = voice;
            }
        }

        return candidate;
    }

    public void Process()
    {
        var i = 0;

        while (true)
        {
            if (i == activeVoiceCount)
            {
                return;
            }

            if (voices[i].Process())
            {
                i++;
            }
            else
            {
                activeVoiceCount--;

                var tmp = voices[i];
                voices[i] = voices[activeVoiceCount];
                voices[activeVoiceCount] = tmp;
            }
        }
    }

    public void Clear()
    {
        activeVoiceCount = 0;
    }

    public Enumerator GetEnumerator()
    {
        return new Enumerator(this);
    }

    public int ActiveVoiceCount => activeVoiceCount;

    public struct Enumerator : IEnumerator<SfzVoice>
    {
        private SfzVoiceCollection collection;

        private int index;
        private SfzVoice current;

        internal Enumerator(SfzVoiceCollection collection)
        {
            this.collection = collection;

            index = 0;
            current = null;
        }

        public void Dispose()
        {
        }

        public bool MoveNext()
        {
            if (index < collection.activeVoiceCount)
            {
                current = collection.voices[index];
                index++;
                return true;
            }

            return false;
        }

        public void Reset()
        {
            index = 0;
            current = null;
        }

        public SfzVoice Current => current;

        object IEnumerator.Current => throw new NotSupportedException();
    }
}
