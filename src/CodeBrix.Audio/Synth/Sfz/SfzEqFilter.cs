using System;

namespace CodeBrix.Audio.Synth.Sfz;

// One parametric EQ band for one channel of one voice: an RBJ peaking biquad parameterized by center
// frequency (Hz), bandwidth (octaves) and gain (dB). Coefficients recompute only when a parameter
// moves meaningfully, since CC and LFO modulation can retune the band every block.
internal sealed class SfzEqFilter
{
    private readonly int sampleRate;

    private float lastFrequency;
    private float lastBandwidth;
    private float lastGain;

    private float b0;
    private float b1;
    private float b2;
    private float a1;
    private float a2;

    private float x1;
    private float x2;
    private float y1;
    private float y2;

    internal SfzEqFilter(int sampleRate)
    {
        this.sampleRate = sampleRate;
    }

    public void Start()
    {
        lastFrequency = -1;
        lastBandwidth = float.MinValue;
        lastGain = float.MinValue;
        ClearBuffer();
    }

    public void ClearBuffer()
    {
        x1 = 0;
        x2 = 0;
        y1 = 0;
        y2 = 0;
    }

    public void SetPeaking(float frequency, float bandwidthOctaves, float gainDb)
    {
        frequency = Math.Clamp(frequency, 10f, 0.45f * sampleRate);
        bandwidthOctaves = Math.Clamp(bandwidthOctaves, 0.001f, 8f);

        if (lastFrequency > 0 &&
            MathF.Abs(frequency - lastFrequency) < 0.001f * lastFrequency &&
            bandwidthOctaves == lastBandwidth &&
            MathF.Abs(gainDb - lastGain) < 0.01f)
        {
            return;
        }

        lastFrequency = frequency;
        lastBandwidth = bandwidthOctaves;
        lastGain = gainDb;

        // RBJ audio-EQ-cookbook peaking coefficients with bandwidth in octaves.
        var amplitude = MathF.Pow(10f, gainDb / 40f);
        var w = 2f * MathF.PI * frequency / sampleRate;
        var cosw = MathF.Cos(w);
        var sinw = MathF.Sin(w);
        var alpha = sinw * MathF.Sinh(0.5f * MathF.Log(2f) * bandwidthOctaves * w / sinw);

        var rb0 = 1f + alpha * amplitude;
        var rb1 = -2f * cosw;
        var rb2 = 1f - alpha * amplitude;
        var ra0 = 1f + alpha / amplitude;
        var ra1 = -2f * cosw;
        var ra2 = 1f - alpha / amplitude;

        b0 = rb0 / ra0;
        b1 = rb1 / ra0;
        b2 = rb2 / ra0;
        a1 = ra1 / ra0;
        a2 = ra2 / ra0;
    }

    public void Process(float[] block)
    {
        for (var t = 0; t < block.Length; t++)
        {
            var input = block[t];
            var output = b0 * input + b1 * x1 + b2 * x2 - a1 * y1 - a2 * y2;

            x2 = x1;
            x1 = input;
            y2 = y1;
            y1 = output;

            block[t] = output;
        }
    }
}
