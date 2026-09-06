using System;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Utils;

namespace CodeBrix.Audio.Synth;

/// <summary>
/// A textual meta event found while reading a <see cref="MidiSequence"/>: a track name, a lyric, a
/// marker, a copyright line and so on.
/// </summary>
/// <remarks>
/// <para>
/// The Standard MIDI File specification says nothing about the character encoding of these events,
/// and generators disagree - some write UTF-8, some write a single-byte encoding, and some write
/// neither. The bytes are therefore kept exactly as they appeared; <see cref="Text"/> is a
/// best-effort reading of them, one character per byte, which is faithful for plain ASCII and
/// legible but lossy for anything else.
/// </para>
/// <para>
/// A sequence is a playback object and ignores these events; they are collected so that a caller
/// that needs to identify a file - matching a track name against a title, say - does not have to
/// read it a second time with the editable model.
/// </para>
/// </remarks>
public sealed class MidiTextMeta
{
    private readonly byte[] data;

    /// <summary>
    /// Creates a text meta record.
    /// </summary>
    /// <param name="kind">Which textual meta event this is.</param>
    /// <param name="trackNumber">The zero-based track the event was found in.</param>
    /// <param name="tick">The tick within that track at which the event appeared.</param>
    /// <param name="data">The event's bytes. The array is taken as given and not copied.</param>
    /// <exception cref="ArgumentNullException"><paramref name="data"/> is null.</exception>
    public MidiTextMeta(MetaEventType kind, int trackNumber, int tick, byte[] data)
    {
        this.data = data ?? throw new ArgumentNullException(nameof(data));
        Kind = kind;
        TrackNumber = trackNumber;
        Tick = tick;
    }

    /// <summary>Which textual meta event this is.</summary>
    public MetaEventType Kind { get; }

    /// <summary>The zero-based track the event was found in.</summary>
    public int TrackNumber { get; }

    /// <summary>The tick within that track at which the event appeared.</summary>
    public int Tick { get; }

    /// <summary>The event's bytes, exactly as they appeared in the file.</summary>
    public ReadOnlySpan<byte> Data => data;

    /// <summary>The number of bytes the event carried.</summary>
    public int Length => data.Length;

    /// <summary>
    /// A best-effort reading of the bytes as text, one character per byte. Faithful for plain
    /// ASCII; legible but lossy for anything else.
    /// </summary>
    public string Text => ByteEncoding.Instance.GetString(data);

    /// <summary>
    /// Copies the event's bytes into a new array.
    /// </summary>
    /// <returns>A fresh copy of the bytes.</returns>
    public byte[] ToArray() => (byte[])data.Clone();

    /// <summary>
    /// Describes this event.
    /// </summary>
    /// <returns>A short description naming the kind, the track and the text.</returns>
    public override string ToString() => $"Track {TrackNumber} tick {Tick} {Kind}: {Text}";
}
