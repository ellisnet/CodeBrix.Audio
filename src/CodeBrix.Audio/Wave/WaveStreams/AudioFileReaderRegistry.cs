using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeBrix.Audio.Wave;

/// <summary>
/// The map from file extension to reader that <see cref="AudioFileReader"/> uses, and the place to
/// add a format CodeBrix.Audio does not ship itself.
/// </summary>
/// <remarks>
/// <para>
/// WAV, MP3, Ogg Vorbis and FLAC are registered out of the box. An add-on package that carries
/// another decoder - a separately licensed codec, for instance - registers it once at start-up and
/// every extension-based entry point picks it up:
/// </para>
/// <code>
/// AudioFileReaderRegistry.Register(".xyz", stream => new XyzFileReader(stream));
/// </code>
/// <para>
/// This covers the file-reading side only. To make the same format playable through
/// <see cref="CodeBrix.Audio.Playback.AudioFilePlayer"/> or
/// <see cref="CodeBrix.Audio.Playback.SoundEffectClip"/>, also register a codec factory with
/// <see cref="SharedAudioOutput.RegisterCodecFactory"/> - those go through the audio engine, which
/// identifies formats by content rather than by file name.
/// </para>
/// </remarks>
public static class AudioFileReaderRegistry
{
    private static readonly object Gate = new object();

    private static readonly Dictionary<string, Func<Stream, WaveStream>> Readers =
        new Dictionary<string, Func<Stream, WaveStream>>(StringComparer.OrdinalIgnoreCase)
        {
            [".wav"] = stream => new WaveFileReader(stream),
            [".mp3"] = stream => new Mp3FileReader(stream),
            [".ogg"] = stream => new OggVorbisFileReader(stream),
            [".flac"] = stream => new FlacFileReader(stream)
        };

    /// <summary>
    /// Registers a reader for a file extension, replacing any reader already registered for it.
    /// </summary>
    /// <param name="extension">The extension, with or without the leading dot (".opus" or "opus").</param>
    /// <param name="readerFactory">Creates a reader over a stream positioned at the start of the file.</param>
    /// <exception cref="ArgumentException"><paramref name="extension"/> is null or blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="readerFactory"/> is null.</exception>
    public static void Register(string extension, Func<Stream, WaveStream> readerFactory)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new ArgumentException("An extension is required.", nameof(extension));
        }
        if (readerFactory == null)
        {
            throw new ArgumentNullException(nameof(readerFactory));
        }

        lock (Gate)
        {
            Readers[Normalize(extension)] = readerFactory;
        }
    }

    /// <summary>
    /// Whether a reader is registered for the given file name or extension.
    /// </summary>
    /// <param name="fileNameOrExtension">A file name ("music.opus") or an extension (".opus").</param>
    /// <returns>True when the format can be opened by file name.</returns>
    public static bool Supports(string fileNameOrExtension)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrExtension)) return false;

        lock (Gate)
        {
            return Readers.ContainsKey(ExtensionOf(fileNameOrExtension));
        }
    }

    /// <summary>
    /// Every registered extension, in alphabetical order, each including its leading dot.
    /// </summary>
    public static IEnumerable<string> SupportedExtensions
    {
        get { lock (Gate) { return Readers.Keys.OrderBy(ext => ext, StringComparer.Ordinal).ToArray(); } }
    }

    /// <summary>
    /// Opens a file with the reader registered for its extension.
    /// </summary>
    /// <param name="fileName">The audio file to open.</param>
    /// <returns>
    /// A <see cref="FileOwningWaveStream"/> over the file; disposing it disposes the reader and
    /// closes the file. Its <see cref="FileOwningWaveStream.Reader"/> is the reader the registered
    /// factory produced, for callers that need its concrete type.
    /// </returns>
    /// <exception cref="NotSupportedException">No reader is registered for the extension.</exception>
    public static WaveStream OpenFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName))
        {
            throw new ArgumentException("A file name is required.", nameof(fileName));
        }

        var factory = GetFactory(fileName);
        var stream = File.OpenRead(fileName);

        try
        {
            // A factory is handed a stream it does not own (see Register), so the file handle stays
            // this method's responsibility - pairing it with the reader is what closes the file.
            return new FileOwningWaveStream(factory(stream), stream);
        }
        catch (Exception)
        {
            stream.Dispose();
            throw;
        }
    }

    /// <summary>
    /// Gets the reader factory for a file name or extension.
    /// </summary>
    /// <param name="fileNameOrExtension">A file name ("music.opus") or an extension (".opus").</param>
    /// <returns>The registered factory.</returns>
    /// <exception cref="NotSupportedException">No reader is registered for the extension.</exception>
    public static Func<Stream, WaveStream> GetFactory(string fileNameOrExtension)
    {
        var extension = ExtensionOf(fileNameOrExtension);

        lock (Gate)
        {
            if (Readers.TryGetValue(extension, out var factory))
            {
                return factory;
            }
        }

        throw new NotSupportedException(
            $"No audio reader is registered for '{extension}'. Registered formats: " +
            $"{string.Join(", ", SupportedExtensions)}. Add one with AudioFileReaderRegistry.Register.");
    }

    private static string ExtensionOf(string fileNameOrExtension)
    {
        var extension = Path.GetExtension(fileNameOrExtension);
        return Normalize(string.IsNullOrEmpty(extension) ? fileNameOrExtension : extension);
    }

    private static string Normalize(string extension)
    {
        var trimmed = extension.Trim();
        return trimmed.StartsWith('.') ? trimmed.ToLowerInvariant() : "." + trimmed.ToLowerInvariant();
    }
}
