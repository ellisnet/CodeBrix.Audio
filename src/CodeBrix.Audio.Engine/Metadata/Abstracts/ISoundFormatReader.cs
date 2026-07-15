using CodeBrix.Audio.Engine.Metadata.Models;
using CodeBrix.Audio.Engine.Structs;

namespace CodeBrix.Audio.Engine.Metadata.Abstracts;  //was previously: SoundFlow.Metadata.Abstracts

/// <summary>
///     Internal interface for format-specific parsers.
/// </summary>
internal interface ISoundFormatReader
{
    Result<SoundFormatInfo> Read(Stream stream, ReadOptions options);
    Task<Result<SoundFormatInfo>> ReadAsync(Stream stream, ReadOptions options);
}