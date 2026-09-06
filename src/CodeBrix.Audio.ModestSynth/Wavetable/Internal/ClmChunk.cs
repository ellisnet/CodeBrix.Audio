using System;
using System.Text;

namespace CodeBrix.Audio.ModestSynth.Wavetable.Internal;

/// <summary>
/// Reads the frame size out of the <c>clm&#160;</c> RIFF chunk that Serum and Serum-compatible
/// exporters write into a wavetable .wav file.
/// </summary>
/// <remarks>
/// <para>
/// The chunk identifier is four characters, the last of which is a SPACE: <c>clm</c> then a space.
/// Its payload is a short ASCII line whose first field, introduced by the marker <c>&lt;!&gt;</c>,
/// is the number of samples in one frame - for example
/// <c>&lt;!&gt;2048 00000000 wavetable (www.xferrecords.com)</c>. The fields after it describe the
/// exporter's own interpolation and looping flags and are of no use to a player, so they are
/// ignored.
/// </para>
/// <para>
/// Nothing about that payload is guaranteed. It is parsed DEFENSIVELY: the marker is looked for
/// first, and if it is absent the first run of digits anywhere in the text is used instead, since
/// every variant seen in the wild puts the frame size first. Anything that does not yield a
/// plausible frame size is treated as "no usable clm chunk", which puts the file back on the
/// <c>wavetableFrameSize</c> attribute and then on the format's 2048 default.
/// </para>
/// </remarks>
internal static class ClmChunk
{
    /// <summary>The four-character RIFF identifier, whose fourth character is a space.</summary>
    internal const string ChunkId = "clm ";

    /// <summary>The marker that introduces the frame size in the chunk's text.</summary>
    internal const string FrameSizeMarker = "<!>";

    /// <summary>The largest number of bytes of chunk payload worth reading.</summary>
    internal const int MaximumPayloadBytes = 4096;

    /// <summary>
    /// Parses the frame size out of a chunk payload.
    /// </summary>
    /// <param name="payload">The raw chunk bytes; may be null or empty.</param>
    /// <param name="minimumFrameSize">The smallest frame size to accept.</param>
    /// <param name="maximumFrameSize">The largest frame size to accept.</param>
    /// <param name="frameSize">On success, the frame size the chunk declares; otherwise 0.</param>
    /// <returns><see langword="true" /> when a plausible frame size was found.</returns>
    internal static bool TryParseFrameSize(byte[] payload, int minimumFrameSize, int maximumFrameSize,
        out int frameSize)
    {
        frameSize = 0;
        if (payload == null || payload.Length == 0) { return false; }

        int count = payload.Length > MaximumPayloadBytes ? MaximumPayloadBytes : payload.Length;
        string text = Encoding.ASCII.GetString(payload, 0, count).Replace('\0', ' ');

        int start = text.IndexOf(FrameSizeMarker, StringComparison.Ordinal);
        start = start >= 0 ? start + FrameSizeMarker.Length : 0;

        while (start < text.Length && !IsDigit(text[start])) { start++; }
        if (start >= text.Length) { return false; }

        long value = 0L;
        int digits = 0;
        while (start < text.Length && IsDigit(text[start]))
        {
            value = (value * 10L) + (text[start] - '0');
            digits++;
            start++;

            // A run of digits long enough to overflow is not a frame size; stop before it can.
            if (digits > 9) { return false; }
        }

        if (digits == 0 || value < minimumFrameSize || value > maximumFrameSize) { return false; }

        frameSize = (int)value;
        return true;
    }

    private static bool IsDigit(char value) => value >= '0' && value <= '9';
}
