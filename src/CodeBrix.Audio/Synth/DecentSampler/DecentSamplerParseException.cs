using System;

namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>
/// Thrown when a <c>.dspreset</c> or <c>DSLibraryInfo.xml</c> is not well-formed XML.
/// </summary>
/// <remarks>
/// This is the only hard failure the parser has. Everything else - an unknown element, an unknown
/// attribute, a value it cannot read, a missing sample - is recorded in the preset's problem list and
/// the preset still loads.
/// </remarks>
public sealed class DecentSamplerParseException : Exception
{
    /// <summary>Creates the exception.</summary>
    /// <param name="message">What went wrong.</param>
    /// <param name="path">The file being read, or null when the source was text or a stream.</param>
    /// <param name="lineNumber">The line the XML reader failed on, or 0 when it is not known.</param>
    /// <param name="linePosition">The column the XML reader failed on, or 0 when it is not known.</param>
    /// <param name="innerException">The underlying XML error.</param>
    public DecentSamplerParseException(
        string message, string path, int lineNumber, int linePosition, Exception innerException)
        : base(message, innerException)
    {
        Path = path;
        LineNumber = lineNumber;
        LinePosition = linePosition;
    }

    /// <summary>The file being read, or null when the source was text or a stream.</summary>
    public string Path { get; }

    /// <summary>The line the XML reader failed on, or 0 when it is not known.</summary>
    public int LineNumber { get; }

    /// <summary>The column the XML reader failed on, or 0 when it is not known.</summary>
    public int LinePosition { get; }
}
