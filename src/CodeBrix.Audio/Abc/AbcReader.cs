using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CodeBrix.Audio.Abc.Internal;

namespace CodeBrix.Audio.Abc;

/// <summary>
/// Reads abc notation - the text music format - into a tune book.
/// </summary>
/// <remarks>
/// <para>
/// Abc is written as plain text: a header of information fields, then music code where a letter is
/// a note. One file holds one tune or many, and every tune starts at an <c>X:</c> field. This reader
/// follows the abc standard version 2.1, and reads what PLAYBACK needs - pitches, lengths, keys,
/// meters, tempos, repeats, voices - while skipping what only affects the printed page.
/// </para>
/// <para>
/// It is tolerant in exactly the way the MIDI reader is. A content problem - a meter that will not
/// parse, an unterminated chord, a decoration it does not model - is a line in
/// <see cref="AbcTune.Problems"/> or <see cref="AbcTuneBook.Problems"/>, never an exception; each
/// KIND is listed once. A null argument or a missing file DOES throw, because those are the
/// caller's mistakes rather than the content's.
/// </para>
/// <para>
/// A tune is converted to the editable MIDI model with <see cref="AbcToMidi.Convert(AbcTune)"/>,
/// and from there plays, renders or saves like any other MIDI in this library.
/// </para>
/// </remarks>
public static class AbcReader
{
    /// <summary>
    /// Reads abc notation from text.
    /// </summary>
    /// <param name="text">The abc text. May hold one tune or many.</param>
    /// <returns>The tunes found, with anything that could not be honoured in the problem lists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="text"/> is null.</exception>
    public static AbcTuneBook Parse(string text)
    {
        if (text == null)
        {
            throw new ArgumentNullException(nameof(text));
        }

        return ParseCore(text);
    }

    /// <summary>
    /// Reads abc notation from a file.
    /// </summary>
    /// <param name="path">The path of the <c>.abc</c> file.</param>
    /// <returns>The tunes found, with anything that could not be honoured in the problem lists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="path"/> is empty or white space.</exception>
    /// <exception cref="FileNotFoundException">There is no file at <paramref name="path"/>.</exception>
    public static AbcTuneBook Read(string path)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        if (path.Trim().Length == 0)
        {
            throw new ArgumentException("A file path is required.", nameof(path));
        }

        if (!File.Exists(path))
        {
            throw new FileNotFoundException("The abc file was not found.", path);
        }

        return ParseCore(File.ReadAllText(path, Encoding.UTF8));
    }

    /// <summary>
    /// Reads abc notation from a stream.
    /// </summary>
    /// <param name="stream">The stream to read. It is read to the end and left open.</param>
    /// <returns>The tunes found, with anything that could not be honoured in the problem lists.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">The stream cannot be read.</exception>
    public static AbcTuneBook Read(Stream stream)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        if (!stream.CanRead)
        {
            throw new ArgumentException("The stream cannot be read.", nameof(stream));
        }

        using (var reader = new StreamReader(stream, Encoding.UTF8, true, 1024, true))
        {
            return ParseCore(reader.ReadToEnd());
        }
    }

    private static AbcTuneBook ParseCore(string text)
    {
        var problems = new List<string>();
        var lines = Prepare(text);

        int first = -1;
        for (int i = 0; i < lines.Count; i++)
        {
            if (lines[i].StartsWith("X:", StringComparison.Ordinal))
            {
                first = i;
                break;
            }
        }

        if (first < 0)
        {
            problems.Add("No tune was found; an abc tune begins with an X: field.");
            return new AbcTuneBook(new AbcTune[0], problems);
        }

        // Anything before the first X: is free text. A file HEADER can live there too - fields and
        // directives that set defaults for every tune in the file - and the standard itself
        // recommends against using one in a tunebook, because a tune extracted from the file loses
        // it. This reader says so rather than applying it silently.
        for (int i = 0; i < first; i++)
        {
            string line = lines[i];
            if (line.StartsWith("%%", StringComparison.Ordinal) ||
                (line.Length > 1 && line[1] == ':' && char.IsLetter(line[0])))
            {
                problems.Add("A file header was present before the first tune; it was not applied to the tunes.");
                break;
            }
        }

        var tunes = new List<AbcTune>();
        int position = first;
        while (position < lines.Count)
        {
            if (!lines[position].StartsWith("X:", StringComparison.Ordinal))
            {
                position++;
                continue;
            }

            var tuneLines = new List<string>();
            tuneLines.Add(lines[position]);
            position++;

            // A tune ends at a blank line, at the end of the file, or - defensively, because real
            // files are not always separated properly - at the next X: field.
            while (position < lines.Count &&
                   lines[position].Length > 0 &&
                   !lines[position].StartsWith("X:", StringComparison.Ordinal))
            {
                tuneLines.Add(lines[position]);
                position++;
            }

            tunes.Add(AbcTuneParser.Parse(tuneLines));
        }

        return new AbcTuneBook(tunes, problems);
    }

    // Splits the text into lines and applies the three rules that have to happen before anything is
    // parsed:
    //
    //   - all three line-ending conventions are read;
    //   - a comment is removed, and a line that held nothing but a comment is removed WITH it, so
    //     that it does not become a blank line and end the tune early - the standard is explicit
    //     about this;
    //   - a trailing backslash continues a music line, which matters for the printed page and not
    //     for a single tick, so it simply goes.
    private static List<string> Prepare(string text)
    {
        var lines = new List<string>();
        var raw = text.Split(['\n']);

        for (int i = 0; i < raw.Length; i++)
        {
            string line = raw[i].TrimEnd('\r');
            if (i == 0 && line.Length > 0 && line[0] == '﻿')
            {
                line = line.Substring(1);
            }

            line = line.Trim();

            if (line.StartsWith("%%", StringComparison.Ordinal))
            {
                lines.Add(line);
                continue;
            }

            bool hadComment = false;
            int percent = IndexOfComment(line);
            if (percent >= 0)
            {
                hadComment = true;
                line = line.Substring(0, percent).TrimEnd();
            }

            if (hadComment && line.Length == 0)
            {
                // A comment-only line is removed entirely; it does not introduce a blank line.
                continue;
            }

            if (line.EndsWith("\\", StringComparison.Ordinal))
            {
                line = line.Substring(0, line.Length - 1).TrimEnd();
            }

            lines.Add(line);
        }

        return lines;
    }

    private static int IndexOfComment(string line)
    {
        for (int i = 0; i < line.Length; i++)
        {
            if (line[i] != '%')
            {
                continue;
            }

            // "\%" is how a text string writes a literal percent sign.
            if (i > 0 && line[i - 1] == '\\')
            {
                continue;
            }

            return i;
        }

        return -1;
    }
}
