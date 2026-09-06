using System;

namespace CodeBrix.Audio.Synth.DecentSampler.Streaming;

// Sample playback out of a ring buffer, sample for sample identical to SfzOscillator's playback out of
// a decoded array.
//
// It is a separate class, not a mode of SfzOscillator, for one reason: the two do their loop wrapping
// in different places. The in-memory oscillator wraps its own read position; the streamed one plays a
// straight line that the READER wrapped for it, because a ring buffer indexed by file frame cannot hold
// both sides of a loop seam. Everything else - the 24-bit fixed-point position, the linear
// interpolation, the end condition, the crossfade curves - is transcribed unchanged, and
// DecentSamplerStreamingIdentityTests proves the two agree to the last sample on the same instrument.
//
// The audio thread runs this. It allocates nothing, takes no lock and touches no file: a block whose
// frames the reader has not produced yet is written as silence, the position advances anyway so the
// timeline never shifts, and the voice reports one underrun that the instrument turns into a single
// problem line.
internal sealed class DecentSamplerStreamingOscillator
{
    private const int fracBits = 24;
    private const long fracUnit = 1L << fracBits;
    private const float inverseFracUnit = 1f / fracUnit;

    private StreamingVoiceBuffer buffer;
    private bool stereo;

    private long position_fp;
    private long streamEnd;         // Exclusive bound in STREAM frames; long.MaxValue while looping.
    private bool looping;

    private long crossfadeFrames;
    private bool crossfadeEqualPower;
    private long loopEndExclusive;  // File space, for the crossfade's own arithmetic.
    private long loopLength;

    // Starts playback of one note over a buffer the caller has already configured and primed.
    public void Start(StreamingVoiceBuffer voiceBuffer, int channelCount, long crossfade, bool equalPower)
    {
        buffer = voiceBuffer;
        stereo = channelCount > 1;

        position_fp = 0;
        looping = voiceBuffer.Looping;
        streamEnd = looping ? long.MaxValue : voiceBuffer.StreamEndFrames;

        crossfadeFrames = Math.Max(0, crossfade);
        crossfadeEqualPower = equalPower;
        loopEndExclusive = voiceBuffer.LoopEndFrameExclusive;
        loopLength = voiceBuffer.LoopLength;
    }

    public bool IsStereo => stereo;

    // Fills one block at the given resampling ratio. Returns false when the audio ran out before the
    // block started, which ends the voice.
    public bool Process(float[] blockLeft, float[] blockRight, double pitchRatio) =>
        Process(blockLeft, blockRight, blockLeft == null ? 0 : blockLeft.Length, pitchRatio);

    // Fills the FIRST `frames` frames of the block and advances the position by exactly that many.
    // A voice starting part-way through a block asks for fewer than a whole block (a note delay, or a
    // sequenced or arpeggiated note); a source that ignored the count would run on through the silence
    // in front of the note and swallow that much of the sample.
    public bool Process(float[] blockLeft, float[] blockRight, int frames, double pitchRatio)
    {
        if (buffer == null)
        {
            return false;
        }

        frames = Math.Clamp(frames, 0, blockLeft.Length);

        if (frames == 0)
        {
            return true;
        }

        var pitchRatio_fp = (long)(fracUnit * pitchRatio);

        // The lowest stream frame still needed, published so the reader knows what it may overwrite.
        buffer.SetConsumed(position_fp >> fracBits);

        var available = buffer.Available;
        var starved = false;

        var result = !looping
            ? FillBlockNoLoop(blockLeft, blockRight, frames, pitchRatio_fp, available, ref starved)
            : crossfadeFrames > 0
                ? FillBlockCrossfaded(blockLeft, blockRight, frames, pitchRatio_fp, available, ref starved)
                : FillBlockContinuous(blockLeft, blockRight, frames, pitchRatio_fp, available, ref starved);

        if (starved)
        {
            buffer.ReportUnderrun();
        }

        buffer.SetConsumed(position_fp >> fracBits);
        return result;
    }

    private bool FillBlockNoLoop(
        float[] blockLeft, float[] blockRight, int frames, long pitchRatio_fp, long available,
        ref bool starved)
    {
        for (var t = 0; t < frames; t++)
        {
            var index = position_fp >> fracBits;

            if (index + 1 >= streamEnd)
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

            if (index + 1 >= available)
            {
                Silence(blockLeft, blockRight, t);
                starved = true;
                position_fp += pitchRatio_fp;
                continue;
            }

            var a = inverseFracUnit * (position_fp & (fracUnit - 1));
            var x1 = buffer.Sample(0, index);
            var x2 = buffer.Sample(0, index + 1);
            blockLeft[t] = x1 + a * (x2 - x1);

            if (stereo)
            {
                var r1 = buffer.Sample(1, index);
                var r2 = buffer.Sample(1, index + 1);
                blockRight[t] = r1 + a * (r2 - r1);
            }

            position_fp += pitchRatio_fp;
        }

        return true;
    }

    private bool FillBlockContinuous(
        float[] blockLeft, float[] blockRight, int frames, long pitchRatio_fp, long available,
        ref bool starved)
    {
        for (var t = 0; t < frames; t++)
        {
            var index1 = position_fp >> fracBits;

            if (index1 + 1 >= available)
            {
                Silence(blockLeft, blockRight, t);
                starved = true;
                position_fp += pitchRatio_fp;
                continue;
            }

            var a = inverseFracUnit * (position_fp & (fracUnit - 1));

            var x1 = buffer.Sample(0, index1);
            var x2 = buffer.Sample(0, index1 + 1);
            blockLeft[t] = x1 + a * (x2 - x1);

            if (stereo)
            {
                var r1 = buffer.Sample(1, index1);
                var r2 = buffer.Sample(1, index1 + 1);
                blockRight[t] = r1 + a * (r2 - r1);
            }

            position_fp += pitchRatio_fp;
        }

        return true;
    }

    // The looping fill with a crossfade over the last `crossfadeFrames` frames of the loop. The fading
    // OUT audio is the ring, exactly as above; the fading IN audio is the run of frames before the loop
    // start, which the buffer holds separately because it sits a whole loop length behind the read head.
    private bool FillBlockCrossfaded(
        float[] blockLeft, float[] blockRight, int frames, long pitchRatio_fp, long available,
        ref bool starved)
    {
        var fadeStart = loopEndExclusive - crossfadeFrames;
        var inverseCrossfade = 1.0 / crossfadeFrames;

        for (var t = 0; t < frames; t++)
        {
            var index1 = position_fp >> fracBits;

            if (index1 + 1 >= available)
            {
                Silence(blockLeft, blockRight, t);
                starved = true;
                position_fp += pitchRatio_fp;
                continue;
            }

            var a = inverseFracUnit * (position_fp & (fracUnit - 1));

            var sampleLeft = Interpolate(0, index1, a);
            var sampleRight = stereo ? Interpolate(1, index1, a) : 0f;

            var fileIndex1 = buffer.FileFrameAt(index1);

            if (fileIndex1 >= fadeStart)
            {
                // u runs 0 at the start of the region to 1 at loopEnd. The fading-in audio sits exactly
                // one loop length earlier, which is the run of frames immediately before loopStart.
                var u = (fileIndex1 - fadeStart + a) * inverseCrossfade;
                u = Math.Clamp(u, 0.0, 1.0);

                var fadeIn = crossfadeEqualPower ? Math.Sqrt(u) : u;
                var fadeOut = crossfadeEqualPower ? Math.Sqrt(1.0 - u) : 1.0 - u;

                var other1 = fileIndex1 - loopLength;

                sampleLeft = (float)(fadeOut * sampleLeft + fadeIn * FadeInterpolate(0, other1, a));
                if (stereo)
                {
                    sampleRight = (float)(fadeOut * sampleRight + fadeIn * FadeInterpolate(1, other1, a));
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

    private float Interpolate(int channel, long index1, float fraction)
    {
        var x1 = buffer.Sample(channel, index1);
        var x2 = buffer.Sample(channel, index1 + 1);
        return x1 + fraction * (x2 - x1);
    }

    private float FadeInterpolate(int channel, long index1, float fraction)
    {
        var x1 = buffer.FadeSample(channel, index1);
        var x2 = buffer.FadeSample(channel, index1 + 1);
        return x1 + fraction * (x2 - x1);
    }

    private void Silence(float[] blockLeft, float[] blockRight, int t)
    {
        blockLeft[t] = 0f;

        if (stereo)
        {
            blockRight[t] = 0f;
        }
    }
}
