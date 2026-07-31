using System;
using CodeBrix.Audio.Engine.Abstracts;

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
    /// Registers every managed codec factory - currently Ogg Vorbis and FLAC - with the engine.
    /// </summary>
    /// <param name="engine">The engine to register with.</param>
    public static void RegisterAll(AudioEngine engine)
    {
        if (engine == null) throw new ArgumentNullException(nameof(engine));

        engine.RegisterCodecFactory(new VorbisCodecFactory());
        engine.RegisterCodecFactory(new FlacCodecFactory());
    }
}
