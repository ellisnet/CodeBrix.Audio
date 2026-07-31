using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.Sfz;

/// <summary>
/// One header and the opcodes that follow it, in file order.
/// </summary>
/// <remarks>
/// A section is exactly what the file said - no inheritance has been applied. Resolving a region
/// against its group and global scopes is a separate step, so that a tool inspecting a file can see
/// what was literally written rather than what it resolves to.
/// </remarks>
public sealed class SfzSection
{
    private readonly List<SfzOpcode> _opcodes = [];

    /// <summary>Creates a section for the given header.</summary>
    /// <param name="kind">Which header opened this section.</param>
    /// <param name="headerName">The header name as written, lower-cased, without angle brackets.</param>
    /// <param name="lineNumber">The 1-based line the header was read from.</param>
    /// <param name="sourceFile">The file the header came from, which may be an included file.</param>
    public SfzSection(SfzHeaderKind kind, string headerName, int lineNumber, string sourceFile)
    {
        Kind = kind;
        HeaderName = headerName ?? throw new ArgumentNullException(nameof(headerName));
        LineNumber = lineNumber;
        SourceFile = sourceFile;
    }

    /// <summary>Which header opened this section.</summary>
    public SfzHeaderKind Kind { get; }

    /// <summary>The header name as written, lower-cased, without angle brackets.</summary>
    public string HeaderName { get; }

    /// <summary>The 1-based line the header was read from.</summary>
    public int LineNumber { get; }

    /// <summary>The file the header came from. Differs from the top-level file inside an <c>#include</c>.</summary>
    public string SourceFile { get; }

    /// <summary>The opcodes in this section, in file order. Later duplicates are kept, not collapsed.</summary>
    public IReadOnlyList<SfzOpcode> Opcodes => _opcodes;

    /// <summary>Appends an opcode to this section.</summary>
    /// <param name="opcode">The opcode to add.</param>
    /// <exception cref="ArgumentNullException"><paramref name="opcode"/> is null.</exception>
    public void Add(SfzOpcode opcode)
    {
        if (opcode == null)
        {
            throw new ArgumentNullException(nameof(opcode));
        }

        _opcodes.Add(opcode);
    }

    /// <summary>
    /// Finds the effective value of an opcode in this section - the last one written, since a later
    /// definition overrides an earlier one within the same header.
    /// </summary>
    /// <param name="name">The opcode name to look for, compared case-insensitively.</param>
    /// <returns>The opcode, or <see langword="null"/> when this section does not set it.</returns>
    public SfzOpcode Find(string name)
    {
        if (name == null)
        {
            return null;
        }

        for (var i = _opcodes.Count - 1; i >= 0; i--)
        {
            if (string.Equals(_opcodes[i].Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return _opcodes[i];
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public override string ToString() => $"<{HeaderName}> ({_opcodes.Count} opcodes, line {LineNumber})";
}
