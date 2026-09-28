using System;

namespace CodeBrix.Audio.Instruments;

/// <summary>
/// The seam through which an instrument-library package finds the files it ships, on whatever
/// platform the application runs on.
/// </summary>
/// <remarks>
/// <para>
/// An instrument package delivers samples - a SoundFont, an SFZ or Decent Sampler folder - and has
/// to open them by path at run time. Where they are depends on the platform: beside the
/// application on desktop, extracted out of the application package on Android. The package does
/// not know and should not care. It asks here:
/// </para>
/// <code>
/// string path = PackagedAssets.Locate("FluidR3_GM.sf2");
/// </code>
/// <para>
/// and a PLATFORM package answers, by installing its <see cref="IPackagedAssetLocator"/> with
/// <see cref="Use"/> during its own start-up. Nothing installed means the default,
/// <see cref="ApplicationDirectoryAssetLocator"/>, which looks beside the application - the
/// desktop behaviour every existing package already relies on. An instrument package that also
/// offers an explicit override (a "use the file at this path" call) keeps it; that override wins
/// over anything this seam would answer, because the consumer said so.
/// </para>
/// <para>
/// Every member is safe to call from several threads at once.
/// </para>
/// </remarks>
public static class PackagedAssets
{
    private static readonly object Gate = new object();

    private static readonly IPackagedAssetLocator DefaultLocator = new ApplicationDirectoryAssetLocator();

    private static IPackagedAssetLocator locator = DefaultLocator;

    /// <summary>
    /// The locator currently answering <see cref="Locate"/> and <see cref="Exists"/>.
    /// </summary>
    public static IPackagedAssetLocator Locator
    {
        get
        {
            lock (Gate)
            {
                return locator;
            }
        }
    }

    /// <summary>
    /// Whether a platform package has installed a locator, as opposed to the default being in
    /// effect.
    /// </summary>
    public static bool HasPlatformLocator
    {
        get
        {
            lock (Gate)
            {
                return !ReferenceEquals(locator, DefaultLocator);
            }
        }
    }

    /// <summary>
    /// Installs the locator a platform package provides. Later calls replace earlier ones.
    /// </summary>
    /// <param name="platformLocator">The locator.</param>
    /// <exception cref="ArgumentNullException"><paramref name="platformLocator"/> is null.</exception>
    public static void Use(IPackagedAssetLocator platformLocator)
    {
        if (platformLocator == null)
        {
            throw new ArgumentNullException(nameof(platformLocator));
        }

        lock (Gate)
        {
            locator = platformLocator;
        }
    }

    /// <summary>
    /// Puts the default locator back, so assets are again looked for beside the application.
    /// </summary>
    public static void ResetToDefault()
    {
        lock (Gate)
        {
            locator = DefaultLocator;
        }
    }

    /// <summary>
    /// Returns the full path at which a packaged asset can be opened on this platform, producing
    /// it first if the platform needs to.
    /// </summary>
    /// <param name="assetPath">
    /// The asset's path as the package ships it - a file name such as <c>FluidR3_GM.sf2</c>, or a
    /// relative forward-slash path to a file or folder.
    /// </param>
    /// <returns>The full path.</returns>
    /// <exception cref="ArgumentException"><paramref name="assetPath"/> is null, blank, absolute, or escapes the assets.</exception>
    public static string Locate(string assetPath)
    {
        return Locator.Locate(NormalizeAssetPath(assetPath));
    }

    /// <summary>
    /// Whether a packaged asset is available on this platform, without producing it.
    /// </summary>
    /// <param name="assetPath">The asset's path as the package ships it.</param>
    /// <returns>True when <see cref="Locate"/> would return a path that exists.</returns>
    /// <exception cref="ArgumentException"><paramref name="assetPath"/> is null, blank, absolute, or escapes the assets.</exception>
    public static bool Exists(string assetPath)
    {
        return Locator.Exists(NormalizeAssetPath(assetPath));
    }

    /// <summary>
    /// Checks and normalises an asset path: forward slashes, no leading or trailing slash, no
    /// empty, <c>.</c> or <c>..</c> segments, and nothing that could name a drive or a scheme.
    /// </summary>
    /// <param name="assetPath">The path as given.</param>
    /// <returns>The normalised path.</returns>
    /// <exception cref="ArgumentException">The path is null, blank, absolute, or escapes the assets.</exception>
    /// <remarks>
    /// A locator is handed paths that have already been through this, and may call it on its own
    /// input as well.
    /// </remarks>
    public static string NormalizeAssetPath(string assetPath)
    {
        if (string.IsNullOrWhiteSpace(assetPath))
        {
            throw new ArgumentException("An asset path must name a file or folder the package ships.", nameof(assetPath));
        }

        var normalized = assetPath.Replace('\\', '/').Trim().Trim('/');
        if (normalized.Length == 0)
        {
            throw new ArgumentException("An asset path must name a file or folder the package ships.", nameof(assetPath));
        }

        if (normalized.Contains("//", StringComparison.Ordinal))
        {
            throw new ArgumentException("An asset path must not contain empty segments.", nameof(assetPath));
        }

        if (normalized.Contains(':', StringComparison.Ordinal))
        {
            throw new ArgumentException("An asset path is relative to what the package ships; it cannot carry a drive or scheme.", nameof(assetPath));
        }

        foreach (var segment in normalized.Split('/'))
        {
            if (segment == "." || segment == "..")
            {
                throw new ArgumentException("An asset path must not contain '.' or '..' segments.", nameof(assetPath));
            }
        }

        return normalized;
    }
}
