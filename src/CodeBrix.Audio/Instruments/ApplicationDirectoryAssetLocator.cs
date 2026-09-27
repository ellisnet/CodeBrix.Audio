using System;
using System.IO;

namespace CodeBrix.Audio.Instruments;

/// <summary>
/// The default <see cref="IPackagedAssetLocator"/>: packaged assets sit in a folder, beside the
/// application unless another root is given.
/// </summary>
/// <remarks>
/// This is what desktop applications get without doing anything. An instrument package's build
/// targets copy the files it ships into the output folder of every project that references it,
/// and this locator finds them there.
/// </remarks>
public sealed class ApplicationDirectoryAssetLocator : IPackagedAssetLocator
{
    private readonly string rootDirectory;

    /// <summary>
    /// Creates a locator rooted at the application's base directory.
    /// </summary>
    public ApplicationDirectoryAssetLocator()
        : this(AppContext.BaseDirectory)
    {
    }

    /// <summary>
    /// Creates a locator rooted at a folder of the caller's choosing.
    /// </summary>
    /// <param name="rootDirectory">The folder packaged assets are looked for in.</param>
    /// <exception cref="ArgumentException"><paramref name="rootDirectory"/> is null or blank.</exception>
    public ApplicationDirectoryAssetLocator(string rootDirectory)
    {
        if (string.IsNullOrWhiteSpace(rootDirectory))
        {
            throw new ArgumentException("A root directory is required.", nameof(rootDirectory));
        }

        this.rootDirectory = rootDirectory;
    }

    /// <summary>
    /// The folder packaged assets are looked for in.
    /// </summary>
    public string RootDirectory => rootDirectory;

    /// <inheritdoc />
    public string Locate(string assetPath)
    {
        return Path.Combine(rootDirectory, PackagedAssets.NormalizeAssetPath(assetPath).Replace('/', Path.DirectorySeparatorChar));
    }

    /// <inheritdoc />
    public bool Exists(string assetPath)
    {
        var path = Locate(assetPath);
        return File.Exists(path) || Directory.Exists(path);
    }
}
