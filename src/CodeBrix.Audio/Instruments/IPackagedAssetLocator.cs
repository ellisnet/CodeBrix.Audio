namespace CodeBrix.Audio.Instruments;

/// <summary>
/// Turns the name of a file or folder that an instrument-library package ships into the path it
/// can be opened at on the current platform.
/// </summary>
/// <remarks>
/// A platform package supplies one of these when files do not simply sit beside the
/// application: an Android platform package, for instance, extracts the asset out of the APK
/// into private storage and returns that copy. Install it with
/// <see cref="PackagedAssets.Use"/>. Instrument packages never implement this; they call
/// <see cref="PackagedAssets.Locate"/> and open what comes back.
/// </remarks>
public interface IPackagedAssetLocator
{
    /// <summary>
    /// Returns the full path at which a packaged asset can be opened, producing it first if the
    /// platform needs to (for example by extracting it from an application package).
    /// </summary>
    /// <param name="assetPath">
    /// The asset's path as the package ships it - a file name such as <c>FluidR3_GM.sf2</c>, or a
    /// relative forward-slash path to a file or folder.
    /// </param>
    /// <returns>The full path. The file or folder may still be missing; <see cref="Exists"/> says.</returns>
    string Locate(string assetPath);

    /// <summary>
    /// Whether the asset is available, without producing it.
    /// </summary>
    /// <param name="assetPath">The asset's path as the package ships it.</param>
    /// <returns>True when <see cref="Locate"/> would return a path that exists.</returns>
    bool Exists(string assetPath);
}
