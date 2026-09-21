using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CodeBrix.Audio.Wave;

/// <summary>
/// The map from file extension to audio WRITER, and the place to add a format CodeBrix.Audio does
/// not encode itself - the mirror image of <see cref="AudioFileReaderRegistry"/>.
/// </summary>
/// <remarks>
/// <para>
/// WAV and AIFF are registered out of the box, over the writers this library has always had. An
/// add-on package carrying an encoder registers it once at start-up, and every extension-based
/// entry point picks it up - including
/// <see cref="CodeBrix.Audio.Synth.SoundFontRenderer.RenderToFile(CodeBrix.Audio.Synth.IMidiSynthesizer, CodeBrix.Audio.Synth.MidiSequence, string, TimeSpan)"/>:
/// </para>
/// <code>
/// CodeBrixAudioOpus.Register();                                  // CodeBrix.Audio.Opus
/// SoundFontRenderer.RenderToFile(synthesizer, sequence, "tune.opus");
/// </code>
/// <para>
/// This is what keeps format support a REGISTRATION question rather than a DEPENDENCY question:
/// CodeBrix.Audio stays MIT with no dependencies, and a consumer reaches any format by referencing
/// the package that carries it and calling its <c>Register()</c>.
/// </para>
/// <para>
/// This covers writing by file name. Reading the same format back is a separate registration, with
/// <see cref="AudioFileReaderRegistry.Register"/>.
/// </para>
/// </remarks>
public static class AudioFileWriterRegistry
{
    private static readonly object Gate = new object();

    private static readonly Dictionary<string, IAudioFileWriterFactory> Factories =
        BuildDefaults();

    /// <summary>
    /// Registers a factory under every extension it declares, replacing anything already
    /// registered for those extensions.
    /// </summary>
    /// <param name="factory">The factory to register.</param>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The factory declares no extensions, or one of them is blank.
    /// </exception>
    public static void Register(IAudioFileWriterFactory factory)
    {
        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        var extensions = factory.Extensions;

        if (extensions == null || extensions.Count == 0)
        {
            throw new ArgumentException(
                "An audio writer factory must declare at least one file extension.", nameof(factory));
        }

        lock (Gate)
        {
            foreach (var extension in extensions)
            {
                if (string.IsNullOrWhiteSpace(extension))
                {
                    throw new ArgumentException(
                        "An audio writer factory declared a blank file extension.", nameof(factory));
                }

                Factories[Normalize(extension)] = factory;
            }
        }
    }

    /// <summary>
    /// Registers a factory for one extension, replacing any factory already registered for it.
    /// </summary>
    /// <param name="extension">The extension, with or without the leading dot (".opus" or "opus").</param>
    /// <param name="factory">The factory that writes that format.</param>
    /// <exception cref="ArgumentException"><paramref name="extension"/> is null or blank.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="factory"/> is null.</exception>
    public static void Register(string extension, IAudioFileWriterFactory factory)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            throw new ArgumentException("An extension is required.", nameof(extension));
        }

        if (factory == null)
        {
            throw new ArgumentNullException(nameof(factory));
        }

        lock (Gate)
        {
            Factories[Normalize(extension)] = factory;
        }
    }

    /// <summary>Whether a writer is registered for the given file name or extension.</summary>
    /// <param name="fileNameOrExtension">A file name ("music.opus") or an extension (".opus").</param>
    /// <returns>True when the format can be written by file name.</returns>
    public static bool Supports(string fileNameOrExtension)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrExtension)) return false;

        lock (Gate)
        {
            return Factories.ContainsKey(ExtensionOf(fileNameOrExtension));
        }
    }

    /// <summary>Every registered extension, in alphabetical order, each including its leading dot.</summary>
    public static IEnumerable<string> SupportedExtensions
    {
        get { lock (Gate) { return Factories.Keys.OrderBy(ext => ext, StringComparer.Ordinal).ToArray(); } }
    }

    /// <summary>Gets the writer factory for a file name or extension.</summary>
    /// <param name="fileNameOrExtension">A file name ("music.opus") or an extension (".opus").</param>
    /// <returns>The registered factory.</returns>
    /// <exception cref="ArgumentException"><paramref name="fileNameOrExtension"/> is null or blank.</exception>
    /// <exception cref="NotSupportedException">
    /// No writer is registered for the extension. The message names the extension and lists what IS
    /// registered.
    /// </exception>
    public static IAudioFileWriterFactory Resolve(string fileNameOrExtension)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrExtension))
        {
            throw new ArgumentException("A file name or extension is required.", nameof(fileNameOrExtension));
        }

        var extension = ExtensionOf(fileNameOrExtension);

        lock (Gate)
        {
            if (Factories.TryGetValue(extension, out var factory))
            {
                return factory;
            }
        }

        throw new NotSupportedException(
            $"No audio writer is registered for '{extension}'. Registered formats: " +
            $"{string.Join(", ", SupportedExtensions)}. Add one with AudioFileWriterRegistry.Register.");
    }

    /// <summary>Creates a writer for a format, chosen by file name or extension.</summary>
    /// <param name="fileNameOrExtension">A file name ("music.wav") or an extension (".wav").</param>
    /// <param name="stream">The stream to write to. The writer does not close it.</param>
    /// <param name="format">The sample rate, channel count and stored sample format to write.</param>
    /// <returns>A writer ready for its first samples.</returns>
    /// <exception cref="ArgumentException">
    /// The extension is blank, the stream cannot be written to, or the stream cannot seek and the
    /// format needs it to.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> or <paramref name="format"/> is null.</exception>
    /// <exception cref="NotSupportedException">No writer is registered for the extension.</exception>
    public static IAudioFileWriter Create(string fileNameOrExtension, Stream stream, WaveFormat format)
    {
        var factory = Resolve(fileNameOrExtension);

        return factory.Create(stream, format);
    }

    /// <summary>
    /// Creates a writer for a format, chosen by file name or extension, in that format's own
    /// default sample format.
    /// </summary>
    /// <param name="fileNameOrExtension">A file name ("music.wav") or an extension (".wav").</param>
    /// <param name="stream">The stream to write to. The writer does not close it.</param>
    /// <param name="sampleRate">The sample rate, in Hz.</param>
    /// <param name="channels">The number of channels.</param>
    /// <returns>A writer ready for its first samples.</returns>
    /// <exception cref="ArgumentException">
    /// The extension is blank, the stream cannot be written to, or the stream cannot seek and the
    /// format needs it to.
    /// </exception>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="sampleRate"/> or <paramref name="channels"/> is not positive.
    /// </exception>
    /// <exception cref="NotSupportedException">No writer is registered for the extension.</exception>
    public static IAudioFileWriter Create(
        string fileNameOrExtension, Stream stream, int sampleRate, int channels)
    {
        var factory = Resolve(fileNameOrExtension);

        return factory.Create(stream, factory.DefaultFormat(sampleRate, channels));
    }

    /// <summary>
    /// Restores the registry to the WAV and AIFF writers it starts with. For tests that register a
    /// format of their own; nothing in a shipping application should call this.
    /// </summary>
    internal static void ResetForTesting()
    {
        lock (Gate)
        {
            Factories.Clear();

            foreach (var entry in BuildDefaults())
            {
                Factories.Add(entry.Key, entry.Value);
            }
        }
    }

    private static Dictionary<string, IAudioFileWriterFactory> BuildDefaults()
    {
        var wav = new WavAudioFileWriterFactory();
        var aiff = new AiffAudioFileWriterFactory();

        return new Dictionary<string, IAudioFileWriterFactory>(StringComparer.OrdinalIgnoreCase)
        {
            [".wav"] = wav,
            [".aif"] = aiff,
            [".aiff"] = aiff
        };
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
