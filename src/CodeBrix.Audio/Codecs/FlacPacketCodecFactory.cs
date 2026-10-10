using System;
using System.Collections.Generic;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Engine.Structs;

namespace CodeBrix.Audio.Codecs;

/// <summary>
/// Supplies the audio engine with a fully managed FLAC decoder for audio that arrives as CONTAINER
/// PACKETS - the shape a demultiplexer produces - rather than as a .flac stream.
/// </summary>
/// <remarks>
/// <para>
/// Register it once, early:
/// </para>
/// <code>
/// engine.RegisterPacketCodecFactory(new FlacPacketCodecFactory());
/// </code>
/// <para>
/// or, for the process-wide shared output,
/// <see cref="CodeBrix.Audio.Wave.SharedAudioOutput.RegisterPacketCodecFactory"/>, which
/// <see cref="ManagedCodecs.RegisterAll"/> already does for this factory.
/// </para>
/// <para>
/// <see cref="Priority"/> is 0 - the built-in level - because there is no native packet decoder for
/// it to sit below: the bundled native library decodes FLAC STREAMS, not loose frames. An add-on
/// package that wants to take FLAC packets over from this factory registers above 0.
/// </para>
/// <para>
/// EACH PACKET IS EXACTLY ONE COMPLETE FLAC FRAME, from its sync code through its CRC-16. FLAC
/// frames decode independently of one another, so every packet's samples are final the moment it
/// is decoded, and a jump to any frame needs no pre-roll.
/// </para>
/// </remarks>
public sealed class FlacPacketCodecFactory : IPacketCodecFactory
{
    private const string FlacCodecId = "flac";

    /// <inheritdoc />
    public string FactoryId => "CodeBrix.Audio.ManagedFlac.Packets";

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedCodecIds { get; } = new[] { FlacCodecId };

    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    /// <remarks>
    /// <paramref name="codecPrivate"/> is the stream header in the form Matroska's A_FLAC carries it:
    /// the bytes a .flac file starts with - the "fLaC" marker, then the metadata blocks, of which
    /// STREAMINFO is mandatory and the rest are optional, the last one flagged as last. A bare
    /// 34-byte STREAMINFO body, with no marker and no block header, is accepted too. Anything else -
    /// another codec's id, data that is not one of those shapes, or a STREAMINFO no frame could be
    /// decoded against - returns null so the engine can offer the request to the next factory.
    /// </remarks>
    public IPacketSoundDecoder CreateDecoder(string codecId, ReadOnlyMemory<byte> codecPrivate, AudioFormat? hint)
    {
        if (!string.Equals(codecId, FlacCodecId, StringComparison.OrdinalIgnoreCase)) return null;

        var streamInfo = FlacPacketSoundDecoder.ParseCodecPrivate(codecPrivate.Span);
        if (streamInfo == null) return null;

        return new FlacPacketSoundDecoder(streamInfo);
    }
}
