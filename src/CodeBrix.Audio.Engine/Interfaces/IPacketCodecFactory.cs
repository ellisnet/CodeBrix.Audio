using CodeBrix.Audio.Engine.Abstracts;
using CodeBrix.Audio.Engine.Structs;

namespace CodeBrix.Audio.Engine.Interfaces;  //was previously: SoundFlow.Interfaces

/// <summary>
/// Defines a factory for creating <see cref="IPacketSoundDecoder"/> instances - decoders for audio
/// that arrives as container packets rather than as a stream. Implement this interface to teach the
/// engine a codec that a demultiplexer will feed it packet by packet.
/// </summary>
/// <remarks>
/// This is the packet-level counterpart of <see cref="ICodecFactory"/>, and it is registered,
/// prioritised and queried exactly like one: see <see cref="AudioEngine.RegisterPacketCodecFactory"/>,
/// <see cref="AudioEngine.SetPacketCodecPriority"/> and
/// <see cref="AudioEngine.CreatePacketDecoder"/>. The two registries are separate, because they are
/// keyed differently - a stream factory declares CONTAINER format identifiers ("ogg", "flac"), a
/// packet factory declares CODEC identifiers ("vorbis", "opus").
/// </remarks>
public interface IPacketCodecFactory
{
    /// <summary>
    /// Gets a unique, lowercase string identifier for this factory implementation. It is recommended to
    /// use a namespace-qualified name to avoid collisions, e.g., "mycompany.custompacketfactory".
    /// This ID is used for unregistering and re-prioritizing codecs.
    /// </summary>
    string FactoryId { get; }

    /// <summary>
    /// Gets a collection of unique, lowercase string identifiers for the CODECS this factory decodes -
    /// for example ["vorbis"], ["opus"], or ["vorbis", "opus"] for a factory that does both.
    /// </summary>
    /// <remarks>
    /// These name the codec itself, not the container it was carried in, and they are matched
    /// case-insensitively by <see cref="AudioEngine.CreatePacketDecoder"/>.
    /// </remarks>
    IReadOnlyCollection<string> SupportedCodecIds { get; }

    /// <summary>
    /// Gets the default priority of this factory. This priority is used upon initial registration but
    /// can be overridden at runtime using <see cref="AudioEngine.SetPacketCodecPriority"/>. When
    /// multiple factories are registered for the same codec, the one with the highest priority number
    /// will be tried first. Built-in packet codecs use a priority of 0.
    /// </summary>
    int Priority { get; }

    /// <summary>
    /// Creates a decoder for one of the supported codecs.
    /// </summary>
    /// <param name="codecId">
    /// The codec identifier being requested (for example "vorbis"), so a factory that serves several
    /// codecs knows which decoder to build.
    /// </param>
    /// <param name="codecPrivate">
    /// The codec's initialisation data, exactly as the container carried it: for Vorbis the three
    /// Xiph-laced setup headers, for Opus the identification-header bytes. May be empty for a codec
    /// that needs none.
    /// </param>
    /// <param name="hint">
    /// An optional hint describing the format the caller would like back. A decoder that cannot
    /// convert is free to ignore it and report what it actually produces.
    /// </param>
    /// <returns>
    /// A valid <see cref="IPacketSoundDecoder"/> on success, or <c>null</c> if this factory cannot
    /// serve the request - the wrong codec, or initialisation data it does not recognise. Returning
    /// null lets the engine try the next factory; throwing does not.
    /// </returns>
    IPacketSoundDecoder? CreateDecoder(string codecId, ReadOnlyMemory<byte> codecPrivate, AudioFormat? hint);
}
