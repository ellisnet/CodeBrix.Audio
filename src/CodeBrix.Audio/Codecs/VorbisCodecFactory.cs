using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Engine.Abstracts;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Engine.Structs;

namespace CodeBrix.Audio.Codecs;

/// <summary>
/// Supplies the audio engine with a fully managed Ogg Vorbis decoder, so that .ogg plays on any
/// platform whether or not the bundled native library was built with Vorbis support.
/// </summary>
/// <remarks>
/// <para>
/// Register it once, early:
/// </para>
/// <code>
/// engine.RegisterCodecFactory(new VorbisCodecFactory());
/// </code>
/// <para>
/// <see cref="Priority"/> is deliberately negative, which puts this factory BELOW the engine's
/// built-in native factory. Where the native library can decode Vorbis it wins, keeping decoding
/// in C with no managed work on the audio path; this factory is reached only when the native one
/// declines - a library built before Vorbis support, or an architecture without a binary.
/// </para>
/// </remarks>
public sealed class VorbisCodecFactory : ICodecFactory
{
    /// <inheritdoc />
    public string FactoryId => "CodeBrix.Audio.ManagedVorbis";

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedFormatIds { get; } = new[] { "ogg" };

    /// <inheritdoc />
    /// <remarks>
    /// Below the built-in native factory's 0, so this is a fallback rather than a replacement.
    /// </remarks>
    public int Priority => -10;

    /// <inheritdoc />
    public ISoundDecoder CreateDecoder(Stream stream, string formatId, AudioFormat format)
    {
        if (stream == null) throw new ArgumentNullException(nameof(stream));
        if (!string.Equals(formatId, "ogg", StringComparison.OrdinalIgnoreCase)) return null;

        // An earlier factory may have read from the stream before giving up on it.
        if (stream.CanSeek) stream.Position = 0;
        if (!IsOggStream(stream)) return null;

        var channels = format.Channels > 0 ? format.Channels : 2;
        var sampleRate = format.SampleRate > 0 ? format.SampleRate : 48000;

        return new VorbisSoundDecoder(stream, channels, sampleRate);
    }

    /// <inheritdoc />
    public ISoundDecoder TryCreateDecoder(Stream stream, out AudioFormat detectedFormat, AudioFormat? hintFormat = null)
    {
        detectedFormat = hintFormat ?? default;

        if (stream == null || !stream.CanSeek) return null;

        stream.Position = 0;
        if (!IsOggStream(stream)) return null;

        // Probing has no target format to honour, so decode at the file's own rate and layout
        // (the 0s below) and report that back; the caller adapts to what it is told.
        var decoder = new VorbisSoundDecoder(stream, 0, 0);

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
    /// <remarks>Encoding Ogg Vorbis is not supported; this factory decodes only.</remarks>
    public ISoundEncoder CreateEncoder(Stream stream, string formatId, AudioFormat format) => null;

    /// <summary>
    /// Checks that the stream is an Ogg container carrying Vorbis specifically.
    /// </summary>
    /// <remarks>
    /// Ogg also carries Opus and FLAC, and the engine identifies all of them as format "ogg", so
    /// this factory is offered streams it cannot decode. Declining them by returning null - rather
    /// than accepting and then failing - is what lets a separately packaged Opus codec take the
    /// same stream and succeed.
    /// </remarks>
    private static bool IsOggStream(Stream stream)
    {
        return OggCodecSniffer.Identify(stream) == OggCodec.Vorbis;
    }
}
