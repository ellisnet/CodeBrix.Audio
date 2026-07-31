using System;
using System.IO;

namespace CodeBrix.Audio.Engine.Tests;

/// <summary>
/// In-code audio fixtures so the test suite needs no binary assets (matching the
/// CodeBrix.Audio.Tests convention).
/// </summary>
internal static class TestAudio
{
    /// <summary>
    /// Builds a valid in-memory PCM-16 WAV (RIFF/WAVE) containing a sine wave of the
    /// given frequency, laid out by hand so no external file or codec is needed.
    /// </summary>
    public static byte[] BuildSineWavPcm16(int sampleRate, int channels, int frames, double frequency = 440.0)
    {
        const short bitsPerSample = 16;
        var blockAlign = channels * bitsPerSample / 8;
        var byteRate = sampleRate * blockAlign;
        var dataLen = frames * blockAlign;

        using var ms = new MemoryStream(44 + dataLen);
        using var w = new BinaryWriter(ms);

        Tag(w, "RIFF");
        w.Write(36 + dataLen);
        Tag(w, "WAVE");

        Tag(w, "fmt ");
        w.Write(16);                     // PCM fmt chunk size
        w.Write((short)1);               // audio format = PCM
        w.Write((short)channels);
        w.Write(sampleRate);
        w.Write(byteRate);
        w.Write((short)blockAlign);
        w.Write(bitsPerSample);

        Tag(w, "data");
        w.Write(dataLen);
        for (var i = 0; i < frames; i++)
        {
            var t = (double)i / sampleRate;
            var sample = (short)(Math.Sin(2 * Math.PI * frequency * t) * short.MaxValue * 0.5);
            for (var c = 0; c < channels; c++)
                w.Write(sample);
        }

        w.Flush();
        return ms.ToArray();
    }

    private static void Tag(BinaryWriter w, string fourCc)
    {
        foreach (var ch in fourCc)
            w.Write((byte)ch);
    }


    // ---------------------------------------------------------------------------------------
    // The five-tone motif from "Close Encounters of the Third Kind"
    // ---------------------------------------------------------------------------------------
    //
    // Every test in this suite that actually makes a sound on the speakers plays this, so an
    // audible test run is recognisable as one from across the room - and a run that has gone
    // wrong sounds wrong. G, A, F, F an octave below, C: up a major second, down a major third,
    // down an octave, up a perfect fifth.
    //
    // PITCHED AN OCTAVE ABOVE the usual rendering, and deliberately so: down at concert pitch the
    // fourth tone is F3, about 175 Hz, which laptop and monitor speakers barely reproduce (most
    // roll off below ~200 Hz) and the ear is least sensitive to. It measured at full amplitude and
    // was still inaudible on a real machine - the tune came out as three notes, a hole, and a
    // note. Up here the lowest tone is F4 at 349 Hz, comfortably within what small speakers do
    // well, and the octave drop is still an octave drop. Do not move it back down.

    /// <summary>The five tones, in order: G5, A5, F5, F4, C5.</summary>
    public static readonly double[] CloseEncountersTones = [783.99, 880.00, 698.46, 349.23, 523.25];

    /// <summary>How long each tone sounds.</summary>
    public const double CloseEncountersNoteSeconds = 0.30;

    /// <summary>The silence between tones, so the five stay distinct.</summary>
    public const double CloseEncountersGapSeconds = 0.06;

    /// <summary>Total length of the motif.</summary>
    public static TimeSpan CloseEncountersDuration =>
        TimeSpan.FromSeconds(CloseEncountersTones.Length *
                             (CloseEncountersNoteSeconds + CloseEncountersGapSeconds));

    /// <summary>
    /// Renders the motif as interleaved float samples in [-1, 1].
    /// </summary>
    /// <param name="sampleRate">Sample rate to render at.</param>
    /// <param name="channels">Channel count; every channel gets the same audio.</param>
    /// <returns>The rendered samples.</returns>
    public static float[] BuildCloseEncountersSamples(int sampleRate, int channels)
    {
        var noteFrames = (int)(sampleRate * CloseEncountersNoteSeconds);
        var gapFrames = (int)(sampleRate * CloseEncountersGapSeconds);
        var totalFrames = CloseEncountersTones.Length * (noteFrames + gapFrames);
        var samples = new float[totalFrames * channels];

        // A short fade at each end of a note: a tone that starts or stops at full amplitude
        // clicks, and five clicks would be the most audible thing in the test run.
        var fadeFrames = Math.Max(1, sampleRate / 100);

        var frame = 0;
        foreach (var frequency in CloseEncountersTones)
        {
            for (var n = 0; n < noteFrames; n++, frame++)
            {
                var envelope = Math.Min(1.0, Math.Min(n, noteFrames - 1 - n) / (double)fadeFrames);
                var value = (float)(0.4 * envelope * Math.Sin(2.0 * Math.PI * frequency * n / sampleRate));

                for (var channel = 0; channel < channels; channel++)
                {
                    samples[frame * channels + channel] = value;
                }
            }

            frame += gapFrames; // left silent
        }

        return samples;
    }

    /// <summary>
    /// Builds a PCM-16 WAV of the Close Encounters motif.
    /// </summary>
    /// <param name="sampleRate">Sample rate to render at.</param>
    /// <param name="channels">Channel count.</param>
    /// <returns>The complete WAV file bytes.</returns>
    public static byte[] BuildCloseEncountersWavPcm16(int sampleRate, int channels)
    {
        var samples = BuildCloseEncountersSamples(sampleRate, channels);
        var frames = samples.Length / channels;
        var wav = BuildSineWavPcm16(sampleRate, channels, frames);

        // Overwrite the generated sine's data chunk with the motif, keeping the header intact.
        var offset = wav.Length - frames * channels * 2;
        for (var i = 0; i < samples.Length; i++)
        {
            var value = (short)(Math.Clamp(samples[i], -1f, 1f) * short.MaxValue);
            wav[offset + i * 2] = (byte)value;
            wav[offset + i * 2 + 1] = (byte)(value >> 8);
        }

        return wav;
    }
}
