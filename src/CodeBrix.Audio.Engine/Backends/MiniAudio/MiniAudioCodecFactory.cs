using CodeBrix.Audio.Engine.Backends.MiniAudio.Enums;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Interfaces;
using CodeBrix.Audio.Engine.Structs;

namespace CodeBrix.Audio.Engine.Backends.MiniAudio;  //was previously: SoundFlow.Backends.MiniAudio

/// <summary>
/// Implements the <see cref="ICodecFactory"/> for the formats natively supported by MiniAudio.
/// This factory is registered by the <see cref="MiniAudioEngine"/> at a low priority to act as a default/fallback.
/// </summary>
public sealed class MiniAudioCodecFactory : ICodecFactory
{
    /// <inheritdoc />
    public string FactoryId => "CodeBrix.MiniAudio.Default";

    /// <inheritdoc />
    // "ogg" requires a codebrix_miniaudio binary built with stb_vorbis (see native/miniaudio).
    // Older binaries fail decoder construction, and the engine then falls through to the next
    // registered factory for the format - which is how the managed Vorbis codec takes over.
    public IReadOnlyCollection<string> SupportedFormatIds { get; } = ["wav", "mp3", "flac", "ogg"];

    /// <inheritdoc />
    public int Priority => 0; // Low priority, intended as a fallback.

    /// <inheritdoc />
    public ISoundDecoder? CreateDecoder(Stream stream, string formatId, AudioFormat format)
    {
        return SupportedFormatIds.Contains(formatId) ? new MiniAudioDecoder(stream, format.Format, format.Channels, format.SampleRate) : null;
    }
    
    /// <inheritdoc />
    public ISoundDecoder TryCreateDecoder(Stream stream, out AudioFormat detectedFormat, AudioFormat? hintFormat = null)
    {
        // MiniAudio does not support probing, so we just return a default format.
        detectedFormat = hintFormat ?? AudioFormat.DvdHq;
        return new MiniAudioDecoder(stream, detectedFormat.Format, detectedFormat.Channels, detectedFormat.SampleRate);
    }

    /// <inheritdoc />
    public ISoundEncoder? CreateEncoder(Stream stream, string formatId, AudioFormat format)
    {
        // MiniAudio's encoder only supports WAV.
        return formatId == "wav" ?
            new MiniAudioEncoder(stream, format.Format, format.Channels, format.SampleRate) : null;
    }
}