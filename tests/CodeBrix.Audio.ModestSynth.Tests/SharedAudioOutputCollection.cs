using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests;

/// <summary>
/// Groups every test that touches the process-wide <see cref="CodeBrix.Audio.Wave.SharedAudioOutput"/>
/// into a single, non-parallel collection. They share one global audio device, so they must not run
/// concurrently with each other.
/// </summary>
[CollectionDefinition("SharedAudioOutput", DisableParallelization = true)]
public sealed class SharedAudioOutputCollection
{
}
