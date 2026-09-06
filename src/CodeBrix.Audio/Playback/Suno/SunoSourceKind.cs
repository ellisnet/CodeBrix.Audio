namespace CodeBrix.Audio.Playback.Suno;

/// <summary>
/// Which of the two shapes a stems export was loaded from.
/// </summary>
public enum SunoSourceKind
{
    /// <summary>A folder holding the extracted files.</summary>
    Folder = 0,

    /// <summary>A "&lt;Title&gt; Stems.zip" archive, read in place.</summary>
    ZipArchive = 1,
}
