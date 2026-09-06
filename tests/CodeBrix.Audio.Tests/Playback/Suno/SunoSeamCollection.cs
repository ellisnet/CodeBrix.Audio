using Xunit;

namespace CodeBrix.Audio.Tests.Playback.Suno;

/// <summary>
/// Groups every test that loads a stems export into one non-parallel collection. The alignment
/// estimator lives behind a process-wide static seam, and some of these tests replace it with a
/// stub while others depend on the real one being installed, so they must not run at the same
/// time as one another.
/// </summary>
[CollectionDefinition("SunoStems", DisableParallelization = true)]
public sealed class SunoSeamCollection
{
}
