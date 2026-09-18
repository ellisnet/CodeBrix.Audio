using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.Abc;

/// <summary>
/// How an abc tune is turned into MIDI.
/// </summary>
/// <remarks>
/// Every property has a default that plays an ordinary tune sensibly, so
/// <see cref="AbcToMidi.Convert(AbcTune)"/> needs none of this. Each one validates when it is set,
/// because a bad value here produces a file that is wrong rather than a file that fails.
/// </remarks>
public sealed class AbcToMidiOptions
{
    private int _ticksPerQuarterNote = 480;
    private double _defaultBeatsPerMinute = 120.0;
    private int _velocity = 100;
    private AbcDuration _graceNoteLength = new AbcDuration(1, 64);
    private readonly Dictionary<string, int> _voiceChannels = new Dictionary<string, int>(StringComparer.Ordinal);

    /// <summary>
    /// The resolution of the collection produced: how many ticks one quarter note lasts. The
    /// default is 480.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 32767.</exception>
    /// <remarks>
    /// 480 divides by 2, 3, 4, 5, 6 and 8, so triplets, quintuplets and ordinary subdivisions all
    /// land on whole ticks. Raise it when a tune uses tuplets that 480 cannot express exactly - the
    /// conversion says so in its problems when that happens.
    /// </remarks>
    public int TicksPerQuarterNote
    {
        get => _ticksPerQuarterNote;
        set
        {
            if (value < 1 || value > 32767)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "Ticks per quarter note must be between 1 and 32767.");
            }

            _ticksPerQuarterNote = value;
        }
    }

    /// <summary>
    /// The tempo used when the tune carries no <c>Q:</c> field, or one with only a text label. The
    /// default is 120 quarter notes per minute.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 1000.</exception>
    public double DefaultBeatsPerMinute
    {
        get => _defaultBeatsPerMinute;
        set
        {
            if (!(value >= 1.0) || value > 1000.0)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "The default tempo must be between 1 and 1000 beats per minute.");
            }

            _defaultBeatsPerMinute = value;
        }
    }

    /// <summary>
    /// The velocity every note is given. The default is 100.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not between 1 and 127.</exception>
    /// <remarks>
    /// Abc says nothing about how loud a note is - dynamics are decorations, which are printing
    /// marks - so one velocity for the whole tune is the honest reading of the text.
    /// </remarks>
    public int Velocity
    {
        get => _velocity;
        set
        {
            if (value < 1 || value > 127)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value, "Velocity must be between 1 and 127.");
            }

            _velocity = value;
        }
    }

    /// <summary>
    /// How long one grace note lasts, as a fraction of a whole note. The default is 1/64.
    /// </summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is not longer than zero.</exception>
    /// <remarks>
    /// The abc standard leaves this to the software on purpose: "the unit duration to use for
    /// gracenotes is not specified by the abc file, but by the software". Highland pipe music is
    /// usually played with 1/32.
    /// </remarks>
    public AbcDuration GraceNoteLength
    {
        get => _graceNoteLength;
        set
        {
            if (value <= AbcDuration.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "The grace note length must be longer than zero.");
            }

            _graceNoteLength = value;
        }
    }

    /// <summary>
    /// Channels chosen by the caller, keyed by voice id, 1 to 16. A voice named here plays on that
    /// channel whatever the tune says.
    /// </summary>
    /// <remarks>
    /// The single voice of a tune with no <c>V:</c> fields has an empty id, so
    /// <c>options.VoiceChannels[""] = 10</c> puts a one-voice tune on the percussion channel. This
    /// map wins over a <c>%%MIDI channel</c> directive, which in turn wins over the automatic
    /// assignment.
    /// </remarks>
    public IDictionary<string, int> VoiceChannels => _voiceChannels;

    /// <summary>
    /// Whether <c>%%MIDI program</c> and <c>%%MIDI channel</c> directives in the tune are honoured.
    /// The default is <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// Turn it off to get the notes without the instrument choices the transcriber made - the
    /// voices then take their automatic channels and no program change is written.
    /// </remarks>
    public bool HonourMidiDirectives { get; set; } = true;

    /// <summary>
    /// Makes a copy of these options.
    /// </summary>
    /// <returns>A new instance with the same values.</returns>
    public AbcToMidiOptions Clone()
    {
        var copy = new AbcToMidiOptions
        {
            TicksPerQuarterNote = TicksPerQuarterNote,
            DefaultBeatsPerMinute = DefaultBeatsPerMinute,
            Velocity = Velocity,
            GraceNoteLength = GraceNoteLength,
            HonourMidiDirectives = HonourMidiDirectives,
        };

        foreach (var pair in _voiceChannels)
        {
            copy._voiceChannels[pair.Key] = pair.Value;
        }

        return copy;
    }
}
