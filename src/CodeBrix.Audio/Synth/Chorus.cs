using System;

// ReSharper disable once CheckNamespace
namespace CodeBrix.Audio.Synth; //was previously: MeltySynth

/// <summary>
/// A stereo chorus: the send effect <see cref="SoundFontSynthesizer"/> runs when its settings ask
/// for chorus, and the one any other synthesizer in the family should reach for rather than growing
/// a second implementation.
/// </summary>
/// <remarks>
/// <para>
/// IT IS A SEND EFFECT, NOT AN INSERT. <see cref="Process"/> takes the stereo send bus - everything
/// going to the chorus, each voice already scaled by its own send level - and writes the WET signal
/// alone into two output buffers. Add that wet signal to the dry mix yourself.
/// </para>
/// <para>
/// The two sides read the same modulation table a quarter of a cycle apart, which is what gives the
/// effect its width from a single low-frequency oscillator.
/// </para>
/// <para>
/// Nothing here is thread-safe and nothing allocates once the instance exists: it belongs to the
/// thread rendering it, exactly as a synthesizer's voices do.
/// </para>
/// </remarks>
public sealed class Chorus
{
    private readonly float[] bufferL;
    private readonly float[] bufferR;

    private readonly float[] delayTable;

    private int bufferIndex;

    private int delayTableIndexL;
    private int delayTableIndexR;

    /// <summary>Creates a chorus at a sample rate.</summary>
    /// <param name="sampleRate">The rate the chorus will be run at, in Hz.</param>
    /// <param name="delay">The centre delay in SECONDS - the shortest delay the sweep reaches.</param>
    /// <param name="depth">How far the delay sweeps either side of the centre, in SECONDS.</param>
    /// <param name="frequency">How often the sweep completes a cycle, in Hz.</param>
    /// <remarks>
    /// The figures the SoundFont engine uses are a 2&#160;ms delay, a 1.9&#160;ms depth and a
    /// 0.4&#160;Hz sweep, which is the mild widening a general MIDI bank expects rather than a
    /// pronounced effect.
    /// </remarks>
    public Chorus(int sampleRate, double delay, double depth, double frequency)
    {
        bufferL = new float[(int)(sampleRate * (delay + depth)) + 2];
        bufferR = new float[(int)(sampleRate * (delay + depth)) + 2];

        delayTable = new float[(int)Math.Round(sampleRate / frequency)];
        for (var t = 0; t < delayTable.Length; t++)
        {
            var phase = 2 * Math.PI * t / delayTable.Length;
            delayTable[t] = (float)(sampleRate * (delay + depth * Math.Sin(phase)));
        }

        bufferIndex = 0;

        delayTableIndexL = 0;
        delayTableIndexR = delayTable.Length / 4;
    }

    /// <summary>
    /// Choruses a whole block: the wet signal for every frame of <paramref name="outputLeft"/>.
    /// </summary>
    /// <param name="inputLeft">The left send bus, already scaled by each source's send level.</param>
    /// <param name="inputRight">The right send bus, already scaled by each source's send level.</param>
    /// <param name="outputLeft">The left wet output. OVERWRITTEN, not added to.</param>
    /// <param name="outputRight">The right wet output. OVERWRITTEN, not added to.</param>
    public void Process(float[] inputLeft, float[] inputRight, float[] outputLeft, float[] outputRight)
    {
        for (var t = 0; t < outputLeft.Length; t++)
        {
            {
                var position = bufferIndex - (double)delayTable[delayTableIndexL];
                if (position < 0.0)
                {
                    position += bufferL.Length;
                }

                var index1 = (int)position;
                var index2 = index1 + 1;

                if (index2 == bufferL.Length)
                {
                    index2 = 0;
                }

                var x1 = (double)bufferL[index1];
                var x2 = (double)bufferL[index2];
                var a = position - index1;
                outputLeft[t] = (float)(x1 + a * (x2 - x1));

                delayTableIndexL++;
                if (delayTableIndexL == delayTable.Length)
                {
                    delayTableIndexL = 0;
                }
            }

            {
                var position = bufferIndex - (double)delayTable[delayTableIndexR];
                if (position < 0.0)
                {
                    position += bufferR.Length;
                }

                var index1 = (int)position;
                var index2 = index1 + 1;

                if (index2 == bufferR.Length)
                {
                    index2 = 0;
                }

                var x1 = (double)bufferR[index1];
                var x2 = (double)bufferR[index2];
                var a = position - index1;
                outputRight[t] = (float)(x1 + a * (x2 - x1));

                delayTableIndexR++;
                if (delayTableIndexR == delayTable.Length)
                {
                    delayTableIndexR = 0;
                }
            }

            bufferL[bufferIndex] = inputLeft[t];
            bufferR[bufferIndex] = inputRight[t];
            bufferIndex++;
            if (bufferIndex == bufferL.Length)
            {
                bufferIndex = 0;
            }
        }
    }

    /// <summary>
    /// Clears both delay buffers, so the tail of what was playing does not survive into what plays
    /// next. The modulation settings are left alone.
    /// </summary>
    public void Mute()
    {
        Array.Clear(bufferL, 0, bufferL.Length);
        Array.Clear(bufferR, 0, bufferR.Length);
    }
}
