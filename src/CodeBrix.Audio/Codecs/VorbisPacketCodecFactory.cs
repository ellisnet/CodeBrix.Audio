using System;
using System.Collections.Generic;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Engine.Structs;

namespace CodeBrix.Audio.Codecs;

/// <summary>
/// Supplies the audio engine with a fully managed Vorbis decoder for audio that arrives as CONTAINER
/// PACKETS - the shape a demultiplexer produces - rather than as an Ogg stream.
/// </summary>
/// <remarks>
/// <para>
/// Register it once, early:
/// </para>
/// <code>
/// engine.RegisterPacketCodecFactory(new VorbisPacketCodecFactory());
/// </code>
/// <para>
/// or, for the process-wide shared output,
/// <see cref="CodeBrix.Audio.Wave.SharedAudioOutput.RegisterPacketCodecFactory"/>, which
/// <see cref="ManagedCodecs.RegisterAll"/> already does for this factory.
/// </para>
/// <para>
/// <see cref="Priority"/> is 0 - the built-in level - because there is no native packet decoder for
/// it to sit below: the bundled native library decodes Ogg STREAMS, not loose packets. An add-on
/// package that wants to take Vorbis packets over from this factory registers above 0.
/// </para>
/// </remarks>
public sealed class VorbisPacketCodecFactory : IPacketCodecFactory
{
    private const string VorbisCodecId = "vorbis";

    /// <inheritdoc />
    public string FactoryId => "CodeBrix.Audio.ManagedVorbis.Packets";

    /// <inheritdoc />
    public IReadOnlyCollection<string> SupportedCodecIds { get; } = new[] { VorbisCodecId };

    /// <inheritdoc />
    public int Priority => 0;

    /// <inheritdoc />
    /// <remarks>
    /// <paramref name="codecPrivate"/> is the container's Vorbis setup data in its Xiph-laced form:
    /// a count byte (2, being three headers minus one), the lengths of the identification and comment
    /// headers as 255-continuation bytes, then those two headers and the setup header back to back.
    /// Anything else - another codec's id, or data that is not that shape - returns null so the
    /// engine can offer the request to the next factory.
    /// </remarks>
    public IPacketSoundDecoder CreateDecoder(string codecId, ReadOnlyMemory<byte> codecPrivate, AudioFormat? hint)
    {
        if (!string.Equals(codecId, VorbisCodecId, StringComparison.OrdinalIgnoreCase)) return null;
        if (codecPrivate.Length < 3 || codecPrivate.Span[0] != 2) return null;

        return new VorbisPacketSoundDecoder(codecPrivate);
    }
}
