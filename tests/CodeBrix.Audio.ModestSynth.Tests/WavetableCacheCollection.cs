using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// Serialises the test classes that touch the process-wide <c>WavetableFileCache</c>.
/// </summary>
/// <remarks>
/// The cache is static by design - a wavetable is decoded once and shared by every voice - so a
/// test that clears it and a test that counts its entries cannot run at the same time. Putting
/// both classes in one collection is xUnit's way of saying so.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public class WavetableCacheCollection
{
    /// <summary>The collection's name.</summary>
    public const string Name = "Wavetable file cache";
}
