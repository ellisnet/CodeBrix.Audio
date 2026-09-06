using System;
using System.IO;

namespace CodeBrix.Audio.Playback;

/// <summary>
/// A <see cref="MultiTrackPlayer"/> track that starts life as a recording: a WAV, MP3, Ogg Vorbis or
/// FLAC file, decoded in chunks and converted to the mix's sample rate as it plays.
/// </summary>
/// <remarks>
/// <para>
/// This is one of the two ways to make a track; the other is <see cref="MidiTrack"/>. The
/// difference is only what the track starts with - give this one
/// <see cref="PlayerTrack.SetMidiSource"/> as well and it holds both, with
/// <see cref="PlayerTrack.ActiveSource"/> choosing which is heard.
/// </para>
/// <para>
/// Nothing is decoded up front: the file is opened once to read its length and format, then again
/// for each render, and streamed from there. A multi-minute stem does not sit in memory.
/// </para>
/// </remarks>
public sealed class AudioTrack : PlayerTrack
{
    /// <summary>Creates a track from an audio file.</summary>
    /// <param name="filePath">
    /// Path to a WAV, MP3, Ogg Vorbis or FLAC file, or to a format registered with
    /// <see cref="Wave.AudioFileReaderRegistry.Register"/>. The format is identified from the file's
    /// CONTENT, not from its extension.
    /// </param>
    /// <param name="name">A display name for the track; defaults to the file name without its extension.</param>
    /// <exception cref="ArgumentNullException"><paramref name="filePath"/> is null.</exception>
    /// <exception cref="IOException">The file could not be opened.</exception>
    /// <exception cref="InvalidDataException">The content is not a format this package reads.</exception>
    public AudioTrack(string filePath, string name = null)
        : base(name ?? SafeFileName(filePath))
    {
        SetAudioSource(filePath);
        SetInitialActiveSource(TrackSource.Audio);
    }

    /// <summary>Creates a track from audio held in a stream.</summary>
    /// <param name="stream">A readable, seekable stream holding a WAV, MP3, Ogg Vorbis or FLAC file.</param>
    /// <param name="leaveOpen">
    /// When <see langword="true"/>, the caller keeps ownership of the stream. Either way the stream
    /// is read in full by this constructor - see <see cref="PlayerTrack.SetAudioSource(Stream, bool)"/>.
    /// </param>
    /// <param name="name">A display name for the track.</param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">The stream cannot seek.</exception>
    /// <exception cref="InvalidDataException">The content is not a format this package reads.</exception>
    public AudioTrack(Stream stream, bool leaveOpen = false, string name = null)
        : base(name)
    {
        SetAudioSource(stream, leaveOpen);
        SetInitialActiveSource(TrackSource.Audio);
    }

    private static string SafeFileName(string filePath) =>
        filePath == null ? null : Path.GetFileNameWithoutExtension(filePath);
}
