namespace CodeBrix.Audio.ModestSynth.Internal;

/// <summary>
/// A tiny seeded noise source: the same seed always produces the same sequence, and drawing a
/// value allocates nothing and takes no lock, so it is usable inside a render callback.
/// </summary>
/// <remarks>
/// It is an xorshift32 generator - three shifts and three exclusive-ors. That is not a
/// cryptographic generator and is not meant to be one; it is a white-noise source whose spectrum
/// is flat and whose output is reproducible, which is what determinism in this library means.
/// </remarks>
internal struct ModestRandom
{
    // xorshift32 dies at zero, so a seed of zero is replaced by an arbitrary non-zero constant.
    private const uint ZeroSeedReplacement = 0x9E3779B9u;

    private uint state;

    /// <summary>Creates a generator seeded with <paramref name="seed" />.</summary>
    /// <param name="seed">The seed. Zero is replaced by a fixed non-zero constant.</param>
    internal ModestRandom(uint seed) => state = seed == 0u ? ZeroSeedReplacement : seed;

    /// <summary>Restarts the sequence from <paramref name="seed" />.</summary>
    /// <param name="seed">The seed. Zero is replaced by a fixed non-zero constant.</param>
    internal void Reseed(uint seed) => state = seed == 0u ? ZeroSeedReplacement : seed;

    /// <summary>Returns the next 32-bit value in the sequence.</summary>
    internal uint NextUInt32()
    {
        uint x = state;
        x ^= x << 13;
        x ^= x >> 17;
        x ^= x << 5;
        state = x;
        return x;
    }

    /// <summary>
    /// Returns the next sample of white noise, uniformly distributed in [-1, 1).
    /// </summary>
    internal double NextBipolar()
    {
        // 24 bits of mantissa is exactly what a float carries, so nothing is thrown away.
        const double scale = 1.0 / 8388608.0;   // 2^-23
        return ((NextUInt32() >> 8) * scale) - 1.0;
    }
}
