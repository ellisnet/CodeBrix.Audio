using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// An audition: the music to play, a title for it, and the cues that say what is being heard as it
/// goes by.
/// </summary>
/// <remarks>
/// One plan drives both halves of a listening session. The audible tests play
/// <see cref="Sequence" /> through the audio device and print each cue as the play head reaches it;
/// the offline render writes the same sequence to a <c>.wav</c> file and writes the same cues into
/// the index beside it. A file name is carried too, so the render and its index agree on what the
/// file is called.
/// </remarks>
public sealed class GmAuditionPlan
{
    private readonly GmAuditionCue[] cues;

    /// <summary>Creates a plan.</summary>
    /// <param name="title">What the audition is, as the listener should read it.</param>
    /// <param name="fileName">The name the offline render writes it under, extension and all.</param>
    /// <param name="sequence">The music.</param>
    /// <param name="cues">What is heard, and when, in time order.</param>
    /// <exception cref="ArgumentNullException">Any argument is null.</exception>
    public GmAuditionPlan(
        string title, string fileName, MidiSequence sequence, IEnumerable<GmAuditionCue> cues)
    {
        if (title == null) { throw new ArgumentNullException(nameof(title)); }
        if (fileName == null) { throw new ArgumentNullException(nameof(fileName)); }
        if (sequence == null) { throw new ArgumentNullException(nameof(sequence)); }
        if (cues == null) { throw new ArgumentNullException(nameof(cues)); }

        Title = title;
        FileName = fileName;
        Sequence = sequence;
        this.cues = new List<GmAuditionCue>(cues).ToArray();
    }

    /// <summary>What the audition is.</summary>
    public string Title { get; }

    /// <summary>The name the offline render writes it under.</summary>
    public string FileName { get; }

    /// <summary>The music.</summary>
    public MidiSequence Sequence { get; }

    /// <summary>What is heard, and when, in time order.</summary>
    public IReadOnlyList<GmAuditionCue> Cues => cues;

    /// <summary>How long the music lasts, before any release tail.</summary>
    public TimeSpan Length => Sequence.Length;

    /// <inheritdoc />
    public override string ToString() => Title;
}
