using System;

namespace CodeBrix.Audio.ModestSynth.Effects.Internal;

/// <summary>
/// A fixed-capacity circular delay line with linearly interpolated fractional reads: the storage
/// behind the pitch shifter's grains and the stereo simulator's decorrelating taps.
/// </summary>
/// <remarks>
/// The capacity is rounded up to a power of two so that wrapping is a mask rather than a branch,
/// which keeps <see cref="Write" /> and <see cref="Read" /> free of both allocation and division.
/// Everything is allocated in the constructor; nothing here allocates afterwards.
/// </remarks>
internal sealed class ModestDelayLine
{
    private readonly float[] buffer;
    private readonly int mask;

    private int writeIndex;

    /// <summary>
    /// Creates a delay line able to look back at least <paramref name="capacity" /> samples.
    /// </summary>
    /// <param name="capacity">The longest delay, in samples, that will be read.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="capacity" /> is not positive.</exception>
    internal ModestDelayLine(int capacity)
    {
        if (capacity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(capacity), capacity, "The capacity must be positive.");
        }

        int length = 4;
        while (length < capacity + 4) { length <<= 1; }

        buffer = new float[length];
        mask = length - 1;
    }

    /// <summary>How many samples the line holds. A read further back than this wraps onto stale data.</summary>
    internal int Capacity
    {
        get { return buffer.Length; }
    }

    /// <summary>Clears the line and returns the write position to the start.</summary>
    internal void Reset()
    {
        Array.Clear(buffer, 0, buffer.Length);
        writeIndex = 0;
    }

    /// <summary>Writes one sample and advances the write position.</summary>
    /// <param name="value">The sample.</param>
    internal void Write(double value)
    {
        buffer[writeIndex] = (float)value;
        writeIndex = (writeIndex + 1) & mask;
    }

    /// <summary>
    /// Reads the line at a fractional delay behind the most recently written sample.
    /// </summary>
    /// <param name="delaySamples">
    /// How far back to read. Zero is the sample just written; values are clamped into
    /// 0..<see cref="Capacity" /> - 2.
    /// </param>
    /// <returns>The interpolated sample.</returns>
    internal double Read(double delaySamples)
    {
        double delay = delaySamples;
        if (double.IsNaN(delay) || delay < 0.0) { delay = 0.0; }

        double limit = buffer.Length - 2;
        if (delay > limit) { delay = limit; }

        int whole = (int)delay;
        double fraction = delay - whole;

        // writeIndex already points PAST the newest sample, so one step back is delay zero.
        int index = (writeIndex - 1 - whole) & mask;
        int previous = (index - 1) & mask;

        double a = buffer[index];
        double b = buffer[previous];

        return a + ((b - a) * fraction);
    }
}
