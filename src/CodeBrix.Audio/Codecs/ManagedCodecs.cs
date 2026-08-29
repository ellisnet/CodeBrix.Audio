using System;
using System.Collections.Generic;
using CodeBrix.Audio.Engine.Abstracts;
using CodeBrix.Audio.Engine.Interfaces;

namespace CodeBrix.Audio.Codecs;

/// <summary>
/// Registers CodeBrix.Audio's fully managed decoders with an audio engine.
/// </summary>
/// <remarks>
/// <para>
/// The engine decodes through its bundled native library by default, which is the right thing:
/// decoding stays in C, off the managed heap, and out of the way of the audio thread. These
/// factories register BELOW it and are reached only when the native decoder declines - a native
/// library built before Ogg Vorbis support was added, or a platform with no native binary at all.
/// </para>
/// <para>
/// Registering them costs nothing when the native path is healthy, and turns "this file will not
/// play here" into "this file plays, slightly more expensively" when it is not:
/// </para>
/// <code>
/// var engine = new MiniAudioEngine();
/// ManagedCodecs.RegisterAll(engine);
/// </code>
/// </remarks>
public static class ManagedCodecs
{
    /// <summary>
    /// Registers every managed codec factory with the engine: the Ogg Vorbis and FLAC STREAM
    /// decoders, and the Vorbis PACKET decoder.
    /// </summary>
    /// <param name="engine">The engine to register with.</param>
    /// <remarks>
    /// The packet factories serve a different seam - audio a media container delivers as loose
    /// packets, with no Ogg framing around it - and have no native counterpart to sit below, so they
    /// are registered at the built-in priority rather than as a fallback. The stream factories are a
    /// fresh instance per call; the packet factories are the shared instances in
    /// <see cref="BuiltInPacketCodecFactories"/>, so that asking what the packet seam supports and
    /// registering it are answered from the same list.
    /// </remarks>
    public static void RegisterAll(AudioEngine engine)
    {
        if (engine == null) throw new ArgumentNullException(nameof(engine));

        engine.RegisterCodecFactory(new VorbisCodecFactory());
        engine.RegisterCodecFactory(new FlacCodecFactory());

        foreach (var packetFactory in BuiltInPacketCodecFactories)
        {
            engine.RegisterPacketCodecFactory(packetFactory);
        }
    }

    /// <summary>
    /// THE list of packet codec factories this package carries. <see cref="RegisterAll"/> registers
    /// exactly these, and <see cref="Wave.SharedAudioOutput.IsPacketCodecSupported"/> asks exactly
    /// these, so what the shared output supports and what it registers cannot drift apart.
    /// </summary>
    /// <remarks>
    /// One instance each, shared by every engine they are registered with. A packet codec factory
    /// holds no per-engine state - the engine keeps the priority and registration order in its own
    /// registration record - so sharing is safe and lets a caller ask about the seam without an
    /// engine existing at all.
    /// </remarks>
    internal static readonly IReadOnlyList<IPacketCodecFactory> BuiltInPacketCodecFactories =
        new IPacketCodecFactory[] { new VorbisPacketCodecFactory() };
}
