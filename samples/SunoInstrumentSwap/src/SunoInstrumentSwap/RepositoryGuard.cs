using System;
using System.IO;

namespace SunoInstrumentSwap;

/// <summary>
/// Keeps what this sample writes OUT of the repository it lives in.
/// </summary>
/// <remarks>
/// A render is hundreds of megabytes and an extraction cache is more, and neither belongs in
/// source control. The repository root is found by walking up from wherever this executable is
/// until a folder holding <c>CodeBrix.Audio.slnx</c> turns up; a copy of this sample built
/// somewhere else simply finds nothing and lets every path through.
/// </remarks>
internal static class RepositoryGuard
{
    private const string RootMarker = "CodeBrix.Audio.slnx";

    /// <summary>Whether a path is inside the repository this sample was built in.</summary>
    /// <param name="path">The folder or file to check.</param>
    /// <returns><see langword="true"/> when it is inside, and false when there is no repository.</returns>
    internal static bool IsInsideRepository(string path)
    {
        var root = FindRepositoryRoot();
        if (root == null || string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));
        var rootPath = Path.TrimEndingDirectorySeparator(root);

        if (string.Equals(full, rootPath, PathComparison))
        {
            return true;
        }

        return full.StartsWith(rootPath + Path.DirectorySeparatorChar, PathComparison);
    }

    /// <summary>The repository root, or null when this copy is not inside one.</summary>
    /// <returns>The folder holding the repository's solution file, or null.</returns>
    internal static string FindRepositoryRoot()
    {
        var folder = AppContext.BaseDirectory;

        while (!string.IsNullOrEmpty(folder))
        {
            if (File.Exists(Path.Combine(folder, RootMarker)))
            {
                return folder;
            }

            var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(folder));
            if (string.Equals(parent, folder, StringComparison.Ordinal))
            {
                return null;
            }

            folder = parent;
        }

        return null;
    }

    // Paths are case-sensitive on this family's platform of record and not on every other one.
    // Comparing case-insensitively is the safe direction here: it can only refuse MORE paths.
    private static StringComparison PathComparison => StringComparison.OrdinalIgnoreCase;
}
