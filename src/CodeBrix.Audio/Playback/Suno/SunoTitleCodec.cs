using System;
using CodeBrix.Audio.Utils;

namespace CodeBrix.Audio.Playback.Suno;

/// <summary>
/// The lossy transform a Suno stems export applies to a song title on its way into a MIDI
/// track-name meta event, and the matching that transform makes possible.
/// </summary>
/// <remarks>
/// <para>
/// Suno (Suno, Inc.) writes the track name one byte per UTF-16 code unit of the title, and the byte
/// it writes is the low byte of the CODE POINT that code unit belongs to. For plain text that is the
/// identity - "Fading Breath (Drums)" comes through unchanged - but any character above U+00FF
/// arrives as rubbish, and a character outside the Basic Multilingual Plane arrives as its low byte
/// twice, once for each half of the surrogate pair. The title "🍄💎⛪ Mycelium Dream Cathedral"
/// reaches the file as the bytes <c>44 44 8E 8E EA</c> followed by " Mycelium Dream Cathedral".
/// </para>
/// <para>
/// The transform cannot be inverted, so the meta name is never displayed. What it is good for is
/// CONFIRMATION: the true title is on the file name, and running that title through the same
/// transform says whether the MIDI file belongs to it.
/// </para>
/// </remarks>
internal static class SunoTitleCodec
{
    /// <summary>
    /// Applies the transform: one byte per UTF-16 code unit, the low byte of the code point that
    /// code unit belongs to.
    /// </summary>
    /// <param name="title">The true title, as it appears on the file name.</param>
    /// <returns>The bytes the exporter would write into the track-name meta event.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="title"/> is null.</exception>
    internal static byte[] Encode(string title)
    {
        if (title == null)
        {
            throw new ArgumentNullException(nameof(title));
        }

        var bytes = new byte[title.Length];
        for (var i = 0; i < title.Length; i++)
        {
            bytes[i] = (byte)(CodePointAt(title, i) & 0xFF);
        }

        return bytes;
    }

    /// <summary>
    /// Whether the raw bytes of a track-name meta event are exactly what the transform makes of a
    /// candidate title.
    /// </summary>
    /// <param name="rawTrackName">The bytes of the track-name meta event.</param>
    /// <param name="candidateTitle">The title to test, normally taken from the file name.</param>
    /// <returns><see langword="true"/> when the bytes match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="candidateTitle"/> is null.</exception>
    internal static bool Matches(byte[] rawTrackName, string candidateTitle) =>
        Matches(rawTrackName == null ? default : rawTrackName.AsSpan(), candidateTitle);

    /// <summary>
    /// Whether the raw bytes of a track-name meta event are exactly what the transform makes of a
    /// candidate title.
    /// </summary>
    /// <param name="rawTrackName">The bytes of the track-name meta event.</param>
    /// <param name="candidateTitle">The title to test, normally taken from the file name.</param>
    /// <returns><see langword="true"/> when the bytes match.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="candidateTitle"/> is null.</exception>
    internal static bool Matches(ReadOnlySpan<byte> rawTrackName, string candidateTitle)
    {
        if (candidateTitle == null)
        {
            throw new ArgumentNullException(nameof(candidateTitle));
        }

        return rawTrackName.Length == candidateTitle.Length
            && HasPrefix(rawTrackName, candidateTitle);
    }

    /// <summary>
    /// Whether the raw bytes of a track-name meta event begin with what the transform makes of a
    /// candidate title. Suno writes "&lt;Title&gt; (&lt;Stem&gt;)" into the meta event, so this is
    /// how a whole track name is matched against the song title alone.
    /// </summary>
    /// <param name="rawTrackName">The bytes of the track-name meta event.</param>
    /// <param name="candidateTitle">The title to test, normally taken from the folder or zip name.</param>
    /// <returns><see langword="true"/> when the bytes start with the encoded title.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="candidateTitle"/> is null.</exception>
    internal static bool StartsWith(ReadOnlySpan<byte> rawTrackName, string candidateTitle)
    {
        if (candidateTitle == null)
        {
            throw new ArgumentNullException(nameof(candidateTitle));
        }

        return rawTrackName.Length >= candidateTitle.Length
            && HasPrefix(rawTrackName, candidateTitle);
    }

    /// <summary>
    /// Whether the transform loses information for this title - that is, whether the title contains
    /// anything above U+00FF, so that the track-name meta event will be unreadable.
    /// </summary>
    /// <param name="title">The title to test.</param>
    /// <returns><see langword="true"/> when encoding the title is lossy.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="title"/> is null.</exception>
    internal static bool IsLossy(string title)
    {
        if (title == null)
        {
            throw new ArgumentNullException(nameof(title));
        }

        for (var i = 0; i < title.Length; i++)
        {
            if (title[i] > 0xFF)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Reads the raw bytes back as text, one character per byte. Useful for a diagnostic message;
    /// never for display, because a mangled name reads as rubbish.
    /// </summary>
    /// <param name="rawTrackName">The bytes of the track-name meta event.</param>
    /// <returns>The bytes as characters.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="rawTrackName"/> is null.</exception>
    internal static string ToBestEffortString(byte[] rawTrackName)
    {
        if (rawTrackName == null)
        {
            throw new ArgumentNullException(nameof(rawTrackName));
        }

        return ByteEncoding.Instance.GetString(rawTrackName);
    }

    private static bool HasPrefix(ReadOnlySpan<byte> rawTrackName, string candidateTitle)
    {
        for (var i = 0; i < candidateTitle.Length; i++)
        {
            if (rawTrackName[i] != (byte)(CodePointAt(candidateTitle, i) & 0xFF))
            {
                return false;
            }
        }

        return true;
    }

    // The code point the code unit at this index belongs to: the combined value at a high surrogate,
    // and the code unit itself everywhere else. A low surrogate and its code point share a low byte,
    // so both halves of a surrogate pair produce the same byte - which is exactly what the exporter
    // writes.
    private static int CodePointAt(string text, int index)
    {
        var c = text[index];
        if (char.IsHighSurrogate(c) && index + 1 < text.Length && char.IsLowSurrogate(text[index + 1]))
        {
            return char.ConvertToUtf32(c, text[index + 1]);
        }

        return c;
    }
}
