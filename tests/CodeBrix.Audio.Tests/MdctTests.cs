using CodeBrix.Audio.Tests.Utils;
using Xunit;

namespace CodeBrix.Audio.Tests;

/// <summary>Regression coverage for signed MDCT offsets and every legal Vorbis block size.</summary>
public class MdctTests
{
    /// <summary>Checks transform values and writes against an independent mathematical reference.</summary>
    [Theory]
    [InlineData(64)]
    [InlineData(128)]
    [InlineData(256)]
    [InlineData(512)]
    [InlineData(1024)]
    [InlineData(2048)]
    [InlineData(4096)]
    [InlineData(8192)]
    public void reverse_matches_the_direct_inverse_transform_and_preserves_the_buffer_tail(int size)
    {
        //Arrange: each legal size exercises a different combination of FFT stages.
        //Act / Assert: compare with independent cosine sums and guard the array tail.
        MdctReferenceChecks.Check(size);
    }
}
