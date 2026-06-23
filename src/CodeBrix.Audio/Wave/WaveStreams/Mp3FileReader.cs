using System.IO;
using CodeBrix.Audio.Mpeg.Mp3Support;

namespace CodeBrix.Audio.Wave; //was previously: NAudio.Wave;

/// <summary>
/// Reads MP3 (MPEG-1/2/2.5 Layer I/II/III) audio from a file or stream and
/// presents it as a repositionable <see cref="WaveStream"/> of decoded PCM.
/// Decoding is performed entirely in managed code by the NLayer-derived MPEG
/// decoder, so no platform-specific codec (ACM, DMO, or Media Foundation) is
/// required and reading behaves identically on Windows, macOS, and Linux.
/// </summary>
public class Mp3FileReader : Mp3FileReaderBase
{
    /// <summary>
    /// Opens an MP3 file by name. The underlying file stream is owned by this
    /// reader and is closed when the reader is disposed.
    /// </summary>
    /// <param name="mp3FileName">The path of the MP3 file to open.</param>
    public Mp3FileReader(string mp3FileName)
        : base(File.OpenRead(mp3FileName), CreateManagedFrameDecompressor, true)
    {
    }

    /// <summary>
    /// Opens MP3 audio from an existing stream. The stream is not owned by this
    /// reader and will not be disposed when the reader is disposed.
    /// </summary>
    /// <param name="inputStream">The stream containing MP3 data.</param>
    public Mp3FileReader(Stream inputStream)
        : base(inputStream, CreateManagedFrameDecompressor, false)
    {
    }

    /// <summary>
    /// Creates the fully managed MP3 frame decompressor used by this reader.
    /// </summary>
    /// <param name="mp3Format">The wave format describing the MP3 source.</param>
    /// <returns>A managed <see cref="IMp3FrameDecompressor"/> instance.</returns>
    public static IMp3FrameDecompressor CreateManagedFrameDecompressor(WaveFormat mp3Format)
        => new Mp3FrameDecompressor(mp3Format);
}
