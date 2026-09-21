using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

/// <summary>
/// Serialises the test classes that use the process-wide <c>GeneralMidiInstrumentLibrary.Instance</c>.
/// </summary>
/// <remarks>
/// The library is a singleton by design, and its <c>Adjustments</c> are a TEMPLATE copied into every
/// synthesizer it creates - so a test that sets an adjustment on it (and resets it afterwards) and a
/// test that creates a synthesizer from it cannot run at the same time: the second would be built
/// with the first one's temporary setting and render at a different level. That is exactly what a
/// Release-configuration run caught. Putting every class that touches the singleton in one
/// collection is xUnit's way of saying so.
/// </remarks>
[CollectionDefinition(Name, DisableParallelization = true)]
public class GeneralMidiLibraryCollection
{
    /// <summary>The collection's name.</summary>
    public const string Name = "General MIDI instrument library singleton";
}
