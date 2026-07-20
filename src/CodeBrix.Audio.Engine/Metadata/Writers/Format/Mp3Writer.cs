using CodeBrix.Audio.Engine.Metadata.Abstracts;
using CodeBrix.Audio.Engine.Metadata.Models;
using CodeBrix.Audio.Engine.Metadata.Readers.Tags;
using CodeBrix.Audio.Engine.Metadata.Writers.Tags;
using CodeBrix.Audio.Engine.Structs;

namespace CodeBrix.Audio.Engine.Metadata.Writers.Format;  //was previously: SoundFlow.Metadata.Writers.Format

internal class Mp3Writer : ISoundFormatWriter
{
    public async Task<Result> RemoveTagsAsync(string sourcePath, string destinationPath)
    {
        try
        {
            var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
            await using var sourceStreamScope = sourceStream.ConfigureAwait(false);
            var audioOffsetResult = await GetAudioDataOffsetAsync(sourceStream).ConfigureAwait(false);
            if (audioOffsetResult.IsFailure) return audioOffsetResult;
            var audioDataOffset = audioOffsetResult.Value;
            
            var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
            await using var destStreamScope = destStream.ConfigureAwait(false);
            sourceStream.Position = audioDataOffset;
            await sourceStream.CopyToAsync(destStream).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error("An unexpected error occurred while removing MP3 tags.", ex);
        }
    }

    public async Task<Result> WriteTagsAsync(string sourcePath, string destinationPath, SoundTags tags)
    {
        try
        {
            var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
            await using var sourceStreamScope3 = sourceStream.ConfigureAwait(false);
            var audioOffsetResult = await GetAudioDataOffsetAsync(sourceStream).ConfigureAwait(false);
            if (audioOffsetResult.IsFailure) return audioOffsetResult;
            var audioDataOffset = audioOffsetResult.Value;
            
            var newTagData = Id3V2Builder.Build(tags);

            var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
            await using var destStreamScope5 = destStream.ConfigureAwait(false);
            await destStream.WriteAsync(newTagData).ConfigureAwait(false);
            
            sourceStream.Position = audioDataOffset;
            await sourceStream.CopyToAsync(destStream).ConfigureAwait(false);
            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error("An unexpected error occurred while writing MP3 tags.", ex);
        }
    }

    /// <summary>
    /// Finds the start of the audio data in an MP3 file by skipping over any ID3v2 tags.
    /// </summary>
    private async Task<Result<long>> GetAudioDataOffsetAsync(Stream stream)
    {
        stream.Position = 0;
        var id3Reader = new Id3V2Reader();
        var readResult = await id3Reader.ReadAsync(stream, new ReadOptions { ReadTags = true, ReadAlbumArt = false }).ConfigureAwait(false);
        if (readResult.IsFailure) return Result<long>.Fail(readResult.Error!);
        
        var (_, tagSize) = readResult.Value;
        return tagSize;
    }
}