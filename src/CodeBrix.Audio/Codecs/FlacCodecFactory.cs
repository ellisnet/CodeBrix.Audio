using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Engine.Abstracts;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Engine.Structs;

namespace CodeBrix.Audio.Codecs;

/// <summary>
/// Supplies the audio engine with a fully managed FLAC decoder, so that .flac plays even where
/// the bundled native library is unavailable.
/// </summary>
/// <remarks>
/// <para>
/// Register it once, early:
/// </para>
/// <code>
/// engine.RegisterCodecFactory(new FlacCodecFactory());
/// </code>
/// <para>
/// <see cref="Priority"/> is negative, which puts this factory below the engine's built-in native
/// factory - the native decoder wins wherever it can run, and this one takes over only when it
/// cannot.
/// </para>
/// </remarks>
public sealed class FlacCodecFactory : ICodecFactory
{
    /// <inheritdoc />
    public string FactoryId => "CodeBrix.Audio.ManagedFlac";

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedFormatIds { get; } = new[] { "flac" };

    /// <inheritdoc />
    /// <remarks>Below the built-in native factory's 0, so this is a fallback rather than a replacement.</remarks>
    public int Priority => -10;

    /// <inheritdoc />
    public ISoundDecoder CreateDecoder(Stream stream, string formatId, AudioFormat format)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (!string.Equals(formatId, "flac", StringComparison.OrdinalIgnoreCase)) return null;

        // An earlier factory may have read from the stream before giving up on it.
        if (stream.CanSeek) stream.Position = 0;
        if (!IsFlacStream(stream)) return null;

        return new FlacSoundDecoder(stream, format.Channels, format.SampleRate);
    }

    /// <inheritdoc />
    public ISoundDecoder TryCreateDecoder(Stream stream, out AudioFormat detectedFormat, AudioFormat? hintFormat = null)
    {
        detectedFormat = hintFormat ?? default;

        if (stream == null || !stream.CanSeek) return null;

        stream.Position = 0;
        if (!IsFlacStream(stream)) return null;

        // Probing has no target format to honour, so decode at the file's own rate and layout
        // (the 0s below) and report that back.
        var decoder = new FlacSoundDecoder(stream, 0, 0);

        detectedFormat = new AudioFormat
        {
            Format = SampleFormat.F32,
            Channels = decoder.Channels,
            Layout = AudioFormat.GetLayoutFromChannels(decoder.Channels),
            SampleRate = decoder.SampleRate
        };

        return decoder;
    }

    /// <inheritdoc />
    /// <remarks>Encoding FLAC is not supported; this factory decodes only.</remarks>
    public ISoundEncoder CreateEncoder(Stream stream, string formatId, AudioFormat format) => null;

    /// <summary>
    /// Checks for the "fLaC" stream marker and leaves the stream where it found it.
    /// </summary>
    private static bool IsFlacStream(Stream stream)
    {
        if (!stream.CanSeek) return false;

        var position = stream.Position;
        try
        {
            Span<byte> header = stackalloc byte[4];
            if (stream.ReadAtLeast(header, 4, throwOnEndOfStream: false) < 4) return false;

            return header[0] == (byte)'f' && header[1] == (byte)'L'
                && header[2] == (byte)'a' && header[3] == (byte)'C';
        }
        finally
        {
            stream.Position = position;
        }
    }
}
