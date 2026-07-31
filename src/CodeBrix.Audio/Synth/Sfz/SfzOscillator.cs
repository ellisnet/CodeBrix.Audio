using System;

namespace CodeBrix.Audio.Synth.Sfz;

// Sample playback for one SFZ voice: fixed-point positioning with linear interpolation, mono or
// stereo sources, offset/end bounds, and the four SFZ loop modes. The fixed-point scheme is the same
// as the SoundFont oscillator's: an Int64 whose lower 24 bits are the fraction.
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

    private long position_fp;

    // Starts playback over the region's slice of the sample. loopEndInclusive follows the SFZ (and
    // smpl chunk) convention that the loop end frame is played.
    public void Start(SfzSampleData sample, SfzLoopMode loopMode, long offset, long endInclusive, long loopStartFrame, long loopEndInclusive)
    {
        left = sample.Channels[0];
        right = sample.ChannelCount > 1 ? sample.Channels[1] : null;

        start = Math.Clamp(offset, 0, sample.Frames);
        end = endInclusive < 0 ? sample.Frames : Math.Min(endInclusive + 1, sample.Frames);

        loopStart = Math.Clamp(loopStartFrame, 0, sample.Frames);
        loopEnd = Math.Clamp(loopEndInclusive + 1, loopStart + 1, sample.Frames);

        looping = loopMode == SfzLoopMode.Continuous || loopMode == SfzLoopMode.Sustain;

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
    public bool Process(float[] blockLeft, float[] blockRight, double pitchRatio)
    {
        var pitchRatio_fp = (long)(fracUnit * pitchRatio);

        return looping
            ? FillBlockContinuous(blockLeft, blockRight, pitchRatio_fp)
            : FillBlockNoLoop(blockLeft, blockRight, pitchRatio_fp);
    }

    private bool FillBlockNoLoop(float[] blockLeft, float[] blockRight, long pitchRatio_fp)
    {
        var stereo = right != null;

        for (var t = 0; t < blockLeft.Length; t++)
        {
            var index = position_fp >> fracBits;

            if (index + 1 >= end)
            {
                if (t == 0)
                {
                    return false;
                }

                Array.Clear(blockLeft, t, blockLeft.Length - t);
                if (stereo)
                {
                    Array.Clear(blockRight, t, blockRight.Length - t);
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

    private bool FillBlockContinuous(float[] blockLeft, float[] blockRight, long pitchRatio_fp)
    {
        var stereo = right != null;

        var loopEnd_fp = loopEnd << fracBits;
        var loopLength = loopEnd - loopStart;
        var loopLength_fp = loopLength << fracBits;

        for (var t = 0; t < blockLeft.Length; t++)
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
}
