using System;
using System.Globalization;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// One announcement in an audition: what starts sounding, and when it starts.
/// </summary>
/// <remarks>
/// A cue is what makes a listening session usable. The audible tests print it as the sound
/// arrives, so the terminal says "040  Violin" while the violin is playing, and the offline
/// renders print the same cues into the index beside the <c>.wav</c> files so a listener knows
/// where in the file to go.
/// </remarks>
public sealed class GmAuditionCue
{
    /// <summary>The number given to a cue that names no program and no percussion note.</summary>
    public const int NoNumber = -1;

    /// <summary>Creates a cue.</summary>
    /// <param name="start">When it starts sounding, from the beginning of the audition.</param>
    /// <param name="number">The General MIDI program (0-127), percussion note (35-81), or <see cref="NoNumber"/>.</param>
    /// <param name="name">What is heard, as the listener should read it.</param>
    /// <exception cref="ArgumentNullException"><paramref name="name"/> is null.</exception>
    public GmAuditionCue(TimeSpan start, int number, string name)
    {
        if (name == null)
        {
            throw new ArgumentNullException(nameof(name));
        }

        Start = start;
        Number = number;
        Name = name;
    }

    /// <summary>When it starts sounding, from the beginning of the audition.</summary>
    public TimeSpan Start { get; }

    /// <summary>The General MIDI program, percussion note, or <see cref="NoNumber"/>.</summary>
    public int Number { get; }

    /// <summary>What is heard.</summary>
    public string Name { get; }

    /// <summary>
    /// The cue as one line: its start time, its number and its name, in fixed columns so a run of
    /// them lines up in a terminal or a text file.
    /// </summary>
    /// <returns>The line, without a trailing newline.</returns>
    public string Describe()
    {
        string time = string.Format(
            CultureInfo.InvariantCulture,
            "[{0}:{1:00}.{2}]",
            (int)Start.TotalMinutes,
            Start.Seconds,
            Start.Milliseconds / 100);

        string number = Number < 0
            ? "   "
            : Number.ToString("000", CultureInfo.InvariantCulture);

        return time + "  " + number + "  " + Name;
    }

    /// <inheritdoc />
    public override string ToString() => Describe();
}
