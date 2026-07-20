using System.Text;
using CodeBrix.Audio.Engine.Metadata.Abstracts;
using CodeBrix.Audio.Engine.Metadata.Models;
using CodeBrix.Audio.Engine.Metadata.Writers.Tags;
using CodeBrix.Audio.Engine.Structs;

namespace CodeBrix.Audio.Engine.Metadata.Writers.Format;  //was previously: SoundFlow.Metadata.Writers.Format

internal class WavWriter : ISoundFormatWriter
{
    public async Task<Result> RemoveTagsAsync(string sourcePath, string destinationPath)
    {
        return await ProcessWavFileAsync(sourcePath, destinationPath, null).ConfigureAwait(false);
    }

    public async Task<Result> WriteTagsAsync(string sourcePath, string destinationPath, SoundTags tags)
    {
        return await ProcessWavFileAsync(sourcePath, destinationPath, tags).ConfigureAwait(false);
    }
    
    private async Task<Result> ProcessWavFileAsync(string sourcePath, string destinationPath, SoundTags? tags)
    {
        try
        {
            var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read);
            await using var sourceStreamScope = sourceStream.ConfigureAwait(false);
            var destStream = new FileStream(destinationPath, FileMode.Create, FileAccess.Write);
            await using var destStreamScope = destStream.ConfigureAwait(false);
            using var reader = new BinaryReader(sourceStream);
            var writer = new BinaryWriter(destStream);
            await using var writerScope = writer.ConfigureAwait(false);

            writer.Write("RIFF"u8.ToArray());
            writer.Write(0); // Placeholder for RIFF chunk size
            writer.Write("WAVE"u8.ToArray());

            sourceStream.Position = 12;
            while (sourceStream.Position < sourceStream.Length)
            {
                if (sourceStream.Position + 8 > sourceStream.Length) break;
                
                var chunkId = new string(reader.ReadChars(4));
                var chunkSize = reader.ReadInt32();
                if (sourceStream.Position + chunkSize > sourceStream.Length)
                    return new CorruptChunkError(chunkId, "Chunk size exceeds file boundaries.");

                if (chunkId is "id3 " or "LIST")
                {
                    sourceStream.Seek(chunkSize, SeekOrigin.Current);
                }
                else
                {
                    writer.Write(Encoding.ASCII.GetBytes(chunkId));
                    writer.Write(chunkSize);
                    var chunkData = new byte[chunkSize];
                    await sourceStream.ReadExactlyAsync(chunkData).ConfigureAwait(false);
                    writer.Write(chunkData);
                }
                
                if (chunkSize % 2 != 0 && sourceStream.Position < sourceStream.Length) sourceStream.ReadByte();
            }

            if (tags != null)
            {
                var id3Data = Id3V2Builder.Build(tags);
                writer.Write("id3 "u8.ToArray());
                writer.Write(id3Data.Length);
                writer.Write(id3Data);
                if (id3Data.Length % 2 != 0) writer.Write((byte)0);
            }
            
            var finalSize = (int)destStream.Length - 8;
            writer.Seek(4, SeekOrigin.Begin);
            writer.Write(finalSize);

            return Result.Ok();
        }
        catch (Exception ex)
        {
            return new Error("An unexpected error occurred while processing the WAV file.", ex);
        }
    }
}