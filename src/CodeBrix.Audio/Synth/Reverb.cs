// This reverb implementation is based on Freeverb, a public domain reverb
// implementation by Jezar at Dreampoint.

using System;

// ReSharper disable once CheckNamespace
namespace CodeBrix.Audio.Synth; //was previously: MeltySynth

/// <summary>
/// A stereo reverberation unit: the send effect <see cref="SoundFontSynthesizer"/> runs when its
/// settings ask for reverb, and the one any other synthesizer in the family should reach for rather
/// than growing a second implementation.
/// </summary>
/// <remarks>
/// <para>
/// IT IS A SEND EFFECT, NOT AN INSERT. <see cref="Process(float[], float[], float[])"/> takes a MONO
/// input - the sum of everything being sent to it, each voice scaled by its own send level and by
/// <see cref="InputGain"/> - and writes the WET signal alone into two output buffers. Add that wet
/// signal to the dry mix yourself; the reverb never sees the dry path and never mixes it back.
/// </para>
/// <para>
/// The buffer lengths are computed from the sample rate handed to the constructor, so build one per
/// synthesizer and keep it. <see cref="Mute"/> clears the tails without disturbing the settings,
/// which is what a reset wants.
/// </para>
/// <para>
/// Nothing here is thread-safe and nothing allocates once the instance exists: it belongs to the
/// thread rendering it, exactly as a synthesizer's voices do.
/// </para>
/// </remarks>
public sealed class Reverb
{
    private const float fixedGain = 0.015F;
    private const float scaleWet = 3F;
    private const float scaleDamp = 0.4F;
    private const float scaleRoom = 0.28F;
    private const float offsetRoom = 0.7F;
    private const float initialRoom = 0.5F;
    private const float initialDamp = 0.5F;
    private const float initialWet = 1F / scaleWet;
    private const float initialWidth = 1F;
    private const int stereoSpread = 23;

    private const int cfTuningL1 = 1116;
    private const int cfTuningR1 = 1116 + stereoSpread;
    private const int cfTuningL2 = 1188;
    private const int cfTuningR2 = 1188 + stereoSpread;
    private const int cfTuningL3 = 1277;
    private const int cfTuningR3 = 1277 + stereoSpread;
    private const int cfTuningL4 = 1356;
    private const int cfTuningR4 = 1356 + stereoSpread;
    private const int cfTuningL5 = 1422;
    private const int cfTuningR5 = 1422 + stereoSpread;
    private const int cfTuningL6 = 1491;
    private const int cfTuningR6 = 1491 + stereoSpread;
    private const int cfTuningL7 = 1557;
    private const int cfTuningR7 = 1557 + stereoSpread;
    private const int cfTuningL8 = 1617;
    private const int cfTuningR8 = 1617 + stereoSpread;
    private const int apfTuningL1 = 556;
    private const int apfTuningR1 = 556 + stereoSpread;
    private const int apfTuningL2 = 441;
    private const int apfTuningR2 = 441 + stereoSpread;
    private const int apfTuningL3 = 341;
    private const int apfTuningR3 = 341 + stereoSpread;
    private const int apfTuningL4 = 225;
    private const int apfTuningR4 = 225 + stereoSpread;

    private readonly CombFilter[] cfsL;
    private readonly CombFilter[] cfsR;
    private readonly AllPassFilter[] apfsL;
    private readonly AllPassFilter[] apfsR;

    private float gain;
    private float roomSize, roomSize1;
    private float damp, damp1;
    private float wet, wet1, wet2;
    private float width;

    /// <summary>Creates a reverb at a sample rate, with the classic default room.</summary>
    /// <param name="sampleRate">
    /// The rate the reverb will be run at, in Hz. The delay lines are scaled from the tuning the
    /// algorithm was published at, so a reverb built for one rate must not be run at another.
    /// </param>
    public Reverb(int sampleRate)
    {
        cfsL = new CombFilter[]
        {
            new CombFilter(ScaleTuning(sampleRate, cfTuningL1)),
            new CombFilter(ScaleTuning(sampleRate, cfTuningL2)),
            new CombFilter(ScaleTuning(sampleRate, cfTuningL3)),
            new CombFilter(ScaleTuning(sampleRate, cfTuningL4)),
            new CombFilter(ScaleTuning(sampleRate, cfTuningL5)),
            new CombFilter(ScaleTuning(sampleRate, cfTuningL6)),
            new CombFilter(ScaleTuning(sampleRate, cfTuningL7)),
            new CombFilter(ScaleTuning(sampleRate, cfTuningL8))
        };

        cfsR = new CombFilter[]
        {
            new CombFilter(ScaleTuning(sampleRate, cfTuningR1)),
            new CombFilter(ScaleTuning(sampleRate, cfTuningR2)),
            new CombFilter(ScaleTuning(sampleRate, cfTuningR3)),
            new CombFilter(ScaleTuning(sampleRate, cfTuningR4)),
            new CombFilter(ScaleTuning(sampleRate, cfTuningR5)),
            new CombFilter(ScaleTuning(sampleRate, cfTuningR6)),
            new CombFilter(ScaleTuning(sampleRate, cfTuningR7)),
            new CombFilter(ScaleTuning(sampleRate, cfTuningR8))
        };

        apfsL = new AllPassFilter[]
        {
            new AllPassFilter(ScaleTuning(sampleRate, apfTuningL1)),
            new AllPassFilter(ScaleTuning(sampleRate, apfTuningL2)),
            new AllPassFilter(ScaleTuning(sampleRate, apfTuningL3)),
            new AllPassFilter(ScaleTuning(sampleRate, apfTuningL4))
        };

        apfsR = new AllPassFilter[]
        {
            new AllPassFilter(ScaleTuning(sampleRate, apfTuningR1)),
            new AllPassFilter(ScaleTuning(sampleRate, apfTuningR2)),
            new AllPassFilter(ScaleTuning(sampleRate, apfTuningR3)),
            new AllPassFilter(ScaleTuning(sampleRate, apfTuningR4))
        };

        foreach (var apf in apfsL)
        {
            apf.Feedback = 0.5F;
        }

        foreach (var apf in apfsR)
        {
            apf.Feedback = 0.5F;
        }

        Wet = initialWet;
        RoomSize = initialRoom;
        Damp = initialDamp;
        Width = initialWidth;
    }

    private int ScaleTuning(int sampleRate, int tuning)
    {
        return (int)Math.Round((double)sampleRate / 44100 * tuning);
    }

    /// <summary>
    /// Reverberates a whole block: the wet signal for every frame of <paramref name="outputLeft"/>.
    /// </summary>
    /// <param name="input">
    /// The mono send bus - everything going to the reverb, already scaled by each source's send
    /// level and by <see cref="InputGain"/>.
    /// </param>
    /// <param name="outputLeft">The left wet output. OVERWRITTEN, not added to.</param>
    /// <param name="outputRight">The right wet output. OVERWRITTEN, not added to.</param>
    public void Process(float[] input, float[] outputLeft, float[] outputRight)
    {
        Process(input, outputLeft, outputRight, outputLeft.Length);
    }

    /// <summary>
    /// Reverberates the first <paramref name="count"/> frames of a block, so a caller whose block is
    /// shorter than its scratch arrays does not advance the reverb through the unused tail.
    /// </summary>
    /// <param name="input">
    /// The mono send bus - everything going to the reverb, already scaled by each source's send
    /// level and by <see cref="InputGain"/>.
    /// </param>
    /// <param name="outputLeft">The left wet output. OVERWRITTEN over those frames, not added to.</param>
    /// <param name="outputRight">The right wet output. OVERWRITTEN over those frames, not added to.</param>
    /// <param name="count">How many frames to process. Zero or less does nothing.</param>
    public void Process(float[] input, float[] outputLeft, float[] outputRight, int count)
    {
        if (count <= 0)
        {
            return;
        }

        Array.Clear(outputLeft, 0, count);
        Array.Clear(outputRight, 0, count);

        foreach (var cf in cfsL)
        {
            cf.Process(input, outputLeft, count);
        }

        foreach (var apf in apfsL)
        {
            apf.Process(outputLeft, count);
        }

        foreach (var cf in cfsR)
        {
            cf.Process(input, outputRight, count);
        }

        foreach (var apf in apfsR)
        {
            apf.Process(outputRight, count);
        }

        // With the default settings, we can skip this part.
        if (1F - wet1 > 1.0E-3 || wet2 > 1.0E-3)
        {
            for (var t = 0; t < count; t++)
            {
                var left = outputLeft[t];
                var right = outputRight[t];
                outputLeft[t] = left * wet1 + right * wet2;
                outputRight[t] = right * wet1 + left * wet2;
            }
        }
    }

    /// <summary>
    /// Clears every delay line, so the tail of what was playing does not survive into what plays
    /// next. The settings are left alone.
    /// </summary>
    public void Mute()
    {
        foreach (var cf in cfsL)
        {
            cf.Mute();
        }

        foreach (var cf in cfsR)
        {
            cf.Mute();
        }

        foreach (var apf in apfsL)
        {
            apf.Mute();
        }

        foreach (var apf in apfsR)
        {
            apf.Mute();
        }
    }

    private void Update()
    {
        wet1 = wet * (width / 2F + 0.5F);
        wet2 = wet * ((1F - width) / 2F);

        roomSize1 = roomSize;
        damp1 = damp;
        gain = fixedGain;

        foreach (var cf in cfsL)
        {
            cf.Feedback = roomSize1;
            cf.Damp = damp1;
        }

        foreach (var cf in cfsR)
        {
            cf.Feedback = roomSize1;
            cf.Damp = damp1;
        }
    }

    /// <summary>
    /// The gain a source should be multiplied by on its way into <see cref="Process(float[], float[], float[])"/>.
    /// </summary>
    /// <remarks>
    /// The algorithm's comb filters are written to be fed at a small fraction of full scale; scaling
    /// the send bus by this is what keeps the recirculating network from running away. Multiply each
    /// voice's contribution by its own send level AND by this.
    /// </remarks>
    public float InputGain => gain;

    /// <summary>
    /// How large the room sounds, nominally 0 (small and tight) to 1 (large and long). Default 0.5.
    /// </summary>
    public float RoomSize
    {
        get
        {
            return (roomSize - offsetRoom) / scaleRoom;
        }

        set
        {
            roomSize = (value * scaleRoom) + offsetRoom;
            Update();
        }
    }

    /// <summary>
    /// How fast the high frequencies die away inside the tail, 0 (bright and even) to 1 (dark, as a
    /// soft room absorbs treble first). Default 0.5.
    /// </summary>
    public float Damp
    {
        get
        {
            return damp / scaleDamp;
        }

        set
        {
            damp = value * scaleDamp;
            Update();
        }
    }

    /// <summary>
    /// How loud the wet signal comes out, 0 to 1. Default one third, which is the level the
    /// algorithm was published at. The DRY signal is not this unit's business at all.
    /// </summary>
    public float Wet
    {
        get
        {
            return wet / scaleWet;
        }

        set
        {
            wet = value * scaleWet;
            Update();
        }
    }

    /// <summary>
    /// How wide the tail is spread, 0 (the two outputs summed to the middle) to 1 (fully separated).
    /// Default 1.
    /// </summary>
    public float Width
    {
        get
        {
            return width;
        }

        set
        {
            width = value;
            Update();
        }
    }



    internal sealed class CombFilter
    {
        private readonly float[] buffer;

        private int bufferIndex;
        private float filterStore;

        private float feedback;
        private float damp1;
        private float damp2;

        public CombFilter(int bufferSize)
        {
            buffer = new float[bufferSize];

            bufferIndex = 0;
            filterStore = 0F;

            feedback = 0F;
            damp1 = 0F;
            damp2 = 0F;
        }

        public void Mute()
        {
            Array.Clear(buffer, 0, buffer.Length);
            filterStore = 0F;
        }

        public void Process(float[] inputBlock, float[] outputBlock)
        {
            Process(inputBlock, outputBlock, outputBlock.Length);
        }

        public void Process(float[] inputBlock, float[] outputBlock, int count)
        {
            var blockIndex = 0;
            while (blockIndex < count)
            {
                if (bufferIndex == buffer.Length)
                {
                    bufferIndex = 0;
                }

                var srcRem = buffer.Length - bufferIndex;
                var dstRem = count - blockIndex;
                var rem = Math.Min(srcRem, dstRem);

                for (var t = 0; t < rem; t++)
                {
                    var blockPos = blockIndex + t;
                    var bufferPos = bufferIndex + t;

                    var input = inputBlock[blockPos];

                    // The following ifs are to avoid performance problem due to denormalized number.
                    // The original implementation uses unsafe cast to detect denormalized number.
                    // I tried to reproduce the original implementation using Unsafe.As,
                    // but the simple Math.Abs version was faster according to some benchmarks.

                    var output = buffer[bufferPos];
                    if (MathF.Abs(output) < 1.0E-6F)
                    {
                        output = 0F;
                    }

                    filterStore = (output * damp2) + (filterStore * damp1);
                    if (MathF.Abs(filterStore) < 1.0E-6F)
                    {
                        filterStore = 0F;
                    }

                    buffer[bufferPos] = input + (filterStore * feedback);
                    outputBlock[blockPos] += output;
                }

                bufferIndex += rem;
                blockIndex += rem;
            }
        }

        public float Feedback
        {
            get => feedback;
            set => feedback = value;
        }

        public float Damp
        {
            get => damp1;

            set
            {
                damp1 = value;
                damp2 = 1F - value;
            }
        }
    }



    internal sealed class AllPassFilter
    {
        private readonly float[] buffer;

        private int bufferIndex;

        private float feedback;

        public AllPassFilter(int bufferSize)
        {
            buffer = new float[bufferSize];

            bufferIndex = 0;

            feedback = 0F;
        }

        public void Mute()
        {
            Array.Clear(buffer, 0, buffer.Length);
        }

        public void Process(float[] block)
        {
            Process(block, block.Length);
        }

        public void Process(float[] block, int count)
        {
            var blockIndex = 0;
            while (blockIndex < count)
            {
                if (bufferIndex == buffer.Length)
                {
                    bufferIndex = 0;
                }

                var srcRem = buffer.Length - bufferIndex;
                var dstRem = count - blockIndex;
                var rem = Math.Min(srcRem, dstRem);

                for (var t = 0; t < rem; t++)
                {
                    var blockPos = blockIndex + t;
                    var bufferPos = bufferIndex + t;

                    var input = block[blockPos];

                    var bufout = buffer[bufferPos];
                    if (MathF.Abs(bufout) < 1.0E-6F)
                    {
                        bufout = 0F;
                    }

                    block[blockPos] = bufout - input;
                    buffer[bufferPos] = input + (bufout * feedback);
                }

                bufferIndex += rem;
                blockIndex += rem;
            }
        }

        public float Feedback
        {
            get => feedback;
            set => feedback = value;
        }
    }
}
