using System;

namespace CodeBrix.Audio.Synth.Sfz;

// Sample playback for one SFZ voice: fixed-point positioning with linear interpolation, mono or
// stereo sources, offset/end bounds, and the four SFZ loop modes. The fixed-point scheme is the same
// as the SoundFont oscillator's: an Int64 whose lower 24 bits are the fraction.
//
// SHARED WITH THE DECENT SAMPLER ENGINE. That engine plays its sample zones through this same
// oscillator, and adds one thing SFZ has no opcode for: a loop crossfade. The crossfade is OFF unless
// the Decent Sampler Start overload turns it on, so the SFZ path runs exactly the code it always did.
internal sealed class SfzOscillator
{
    private const int fracBits = 24;
    private const long fracUnit = 1L << fracBits;
    private const float inverseFracUnit = 1f / fracUnit;

    private float[] left;
    private float[] right;

    private long start;
    private long end;       // Exclusive bound of playable data.
    private long loopStart;
    private long loopEnd;   // Exclusive bound of the loop.

    private bool looping;

    private long crossfadeFrames;   // 0 for SFZ; the Decent Sampler loopCrossfade length otherwise.
    private bool crossfadeEqualPower;

    private long position_fp;

    // Starts playback over the region's slice of the sample. loopEndInclusive follows the SFZ (and
    // smpl chunk) convention that the loop end frame is played.
    public void Start(SfzSampleData sample, SfzLoopMode loopMode, long offset, long endInclusive, long loopStartFrame, long loopEndInclusive)
    {
        Start(sample, loopMode, offset, endInclusive, loopStartFrame, loopEndInclusive, 0, false);
    }

    // The Decent Sampler overload: the same playback with a loop crossfade.
    //
    // Measured against the reference player (plan section 7 item 11): loopCrossfade is a length in
    // FRAMES, the crossfade region is the last `crossfade` frames BEFORE loopEnd, the audio faded in
    // over it is the same number of frames immediately before loopStart, and the loop period is
    // unchanged. With u running 0..1 across the region,
    //     linear      out = (1-u)*endOfLoop + u*beforeLoopStart
    //     equal_power out = sqrt(1-u)*endOfLoop + sqrt(u)*beforeLoopStart   (the format's default)
    // The equal-power pair is the SQUARE ROOT pair, not sine/cosine - measured, worst error 0.010
    // against 0.15 for sine/cosine.
    public void Start(
        SfzSampleData sample, SfzLoopMode loopMode, long offset, long endInclusive,
        long loopStartFrame, long loopEndInclusive, long crossfade, bool equalPower)
    {
        left = sample.Channels[0];
        right = sample.ChannelCount > 1 ? sample.Channels[1] : null;

        start = Math.Clamp(offset, 0, sample.Frames);
        end = endInclusive < 0 ? sample.Frames : Math.Min(endInclusive + 1, sample.Frames);

        loopStart = Math.Clamp(loopStartFrame, 0, sample.Frames);
        loopEnd = Math.Clamp(loopEndInclusive + 1, loopStart + 1, sample.Frames);

        looping = loopMode == SfzLoopMode.Continuous || loopMode == SfzLoopMode.Sustain;

        // The faded-in audio comes from before loopStart, so the region can never be longer than the
        // audio in front of the loop, nor than the loop itself.
        crossfadeFrames = Math.Max(0, Math.Min(crossfade, Math.Min(loopStart, loopEnd - loopStart - 1)));
        crossfadeEqualPower = equalPower;

        position_fp = start << fracBits;
    }

    // A loop_sustain voice leaves its loop when the note is released and plays through to the end.
    public void Release(SfzLoopMode loopMode)
    {
        if (loopMode == SfzLoopMode.Sustain)
        {
            looping = false;
        }
    }

    public bool IsStereo => right != null;

    // Fills one block (both channels for stereo sources) at the given resampling ratio. Returns false
    // when the sample ran out before the block started, which ends the voice.
    public bool Process(float[] blockLeft, float[] blockRight, double pitchRatio) =>
        Process(blockLeft, blockRight, pitchRatio, blockLeft.Length);

    // Fills the FIRST `frames` frames of a block and advances the sample by exactly that many. A
    // caller whose voice starts part-way through a block asks for the remainder of it and places the
    // audio itself; the sample must not run on through the silence in front of the note.
    public bool Process(float[] blockLeft, float[] blockRight, double pitchRatio, int frames)
    {
        if (frames <= 0)
        {
            return true;
        }

        if (frames > blockLeft.Length)
        {
            frames = blockLeft.Length;
        }

        var pitchRatio_fp = (long)(fracUnit * pitchRatio);

        if (!looping)
        {
            return FillBlockNoLoop(blockLeft, blockRight, pitchRatio_fp, frames);
        }

        return crossfadeFrames > 0
            ? FillBlockCrossfaded(blockLeft, blockRight, pitchRatio_fp, frames)
            : FillBlockContinuous(blockLeft, blockRight, pitchRatio_fp, frames);
    }

    private bool FillBlockNoLoop(
        float[] blockLeft, float[] blockRight, long pitchRatio_fp, int frames)
    {
        var stereo = right != null;

        for (var t = 0; t < frames; t++)
        {
            var index = position_fp >> fracBits;

            if (index + 1 >= end)
            {
                if (t == 0)
                {
                    return false;
                }

                Array.Clear(blockLeft, t, frames - t);
                if (stereo)
                {
                    Array.Clear(blockRight, t, frames - t);
                }

                return true;
            }

            var a = inverseFracUnit * (position_fp & (fracUnit - 1));
            var x1 = left[index];
            var x2 = left[index + 1];
            blockLeft[t] = x1 + a * (x2 - x1);

            if (stereo)
            {
                var r1 = right[index];
                var r2 = right[index + 1];
                blockRight[t] = r1 + a * (r2 - r1);
            }

            position_fp += pitchRatio_fp;
        }

        return true;
    }

    private bool FillBlockContinuous(
        float[] blockLeft, float[] blockRight, long pitchRatio_fp, int frames)
    {
        var stereo = right != null;

        var loopEnd_fp = loopEnd << fracBits;
        var loopLength = loopEnd - loopStart;
        var loopLength_fp = loopLength << fracBits;

        for (var t = 0; t < frames; t++)
        {
            if (position_fp >= loopEnd_fp)
            {
                position_fp -= loopLength_fp;
            }

            var index1 = position_fp >> fracBits;
            var index2 = index1 + 1;

            if (index2 >= loopEnd)
            {
                index2 -= loopLength;
            }

            var a = inverseFracUnit * (position_fp & (fracUnit - 1));

            var x1 = left[index1];
            var x2 = left[index2];
            blockLeft[t] = x1 + a * (x2 - x1);

            if (stereo)
            {
                var r1 = right[index1];
                var r2 = right[index2];
                blockRight[t] = r1 + a * (r2 - r1);
            }

            position_fp += pitchRatio_fp;
        }

        return true;
    }

    // The looping fill with a crossfade over the last `crossfadeFrames` frames of the loop. Identical
    // to FillBlockContinuous outside that region, so a crossfade of zero never reaches this method.
    private bool FillBlockCrossfaded(
        float[] blockLeft, float[] blockRight, long pitchRatio_fp, int frames)
    {
        var stereo = right != null;

        var loopEnd_fp = loopEnd << fracBits;
        var loopLength = loopEnd - loopStart;
        var loopLength_fp = loopLength << fracBits;

        var fadeStart = loopEnd - crossfadeFrames;
        var inverseCrossfade = 1.0 / crossfadeFrames;

        for (var t = 0; t < frames; t++)
        {
            if (position_fp >= loopEnd_fp)
            {
                position_fp -= loopLength_fp;
            }

            var index1 = position_fp >> fracBits;
            var index2 = index1 + 1;

            if (index2 >= loopEnd)
            {
                index2 -= loopLength;
            }

            var a = inverseFracUnit * (position_fp & (fracUnit - 1));

            var sampleLeft = Interpolate(left, index1, index2, a);
            var sampleRight = stereo ? Interpolate(right, index1, index2, a) : 0f;

            if (index1 >= fadeStart)
            {
                // u runs 0 at the start of the region to 1 at loopEnd. The fading-in audio sits exactly
                // one loop length earlier, which is the run of frames immediately before loopStart.
                var u = (index1 - fadeStart + a) * inverseCrossfade;
                u = Math.Clamp(u, 0.0, 1.0);

                var fadeIn = crossfadeEqualPower ? Math.Sqrt(u) : u;
                var fadeOut = crossfadeEqualPower ? Math.Sqrt(1.0 - u) : 1.0 - u;

                var other1 = index1 - loopLength;
                var other2 = other1 + 1;

                sampleLeft = (float)(fadeOut * sampleLeft + fadeIn * Interpolate(left, other1, other2, a));
                if (stereo)
                {
                    sampleRight = (float)(fadeOut * sampleRight + fadeIn * Interpolate(right, other1, other2, a));
                }
            }

            blockLeft[t] = sampleLeft;
            if (stereo)
            {
                blockRight[t] = sampleRight;
            }

            position_fp += pitchRatio_fp;
        }

        return true;
    }

    private static float Interpolate(float[] channel, long index1, long index2, float fraction)
    {
        if (index1 < 0 || index1 >= channel.Length)
        {
            return 0f;
        }

        var x1 = channel[index1];
        var x2 = index2 >= 0 && index2 < channel.Length ? channel[index2] : x1;
        return x1 + fraction * (x2 - x1);
    }
}
