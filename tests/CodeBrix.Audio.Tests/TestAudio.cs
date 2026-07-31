using System;
using System.IO;
using CodeBrix.Audio.Wave;

namespace CodeBrix.Audio.Tests;

/// <summary>
/// Shared helpers for generating self-contained test audio: synthesized WAV
/// files and a hand-built silent MP3 byte stream. Keeping generation in code
/// avoids bundling any third-party audio assets into the test project.
/// </summary>
internal static class TestAudio
{
    /// <summary>Default sample rate used by the generated test audio.</summary>
    public const int SampleRate = 44100;

    /// <summary>
    /// Writes a mono 16-bit PCM sine wave to <paramref name="path"/> and returns
    /// the float samples that were written (for round-trip comparison).
    /// </summary>
    public static float[] WriteSineWaveFile(string path, double frequency = 440.0,
        double seconds = 0.25, int sampleRate = SampleRate)
    {
        int sampleCount = (int)(seconds * sampleRate);
        var samples = new float[sampleCount];
        for (int n = 0; n < sampleCount; n++)
        {
            samples[n] = (float)(0.5 * Math.Sin(2.0 * Math.PI * frequency * n / sampleRate));
        }

        using (var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, 1)))
        {
            writer.WriteSamples(samples, 0, samples.Length);
        }
        return samples;
    }

    /// <summary>
    /// Builds a valid silent MPEG-1 Layer III stream (44.1 kHz, 128 kbps, mono).
    /// Each frame is a 4-byte header followed by zeroed side-info and main data,
    /// which a conformant decoder renders as silence.
    /// </summary>
    public static byte[] BuildSilentMp3(int frameCount = 25)
    {
        // MPEG-1 Layer III, 128 kbps, 44100 Hz, mono, no CRC.
        // FrameLength = 144 * 128000 / 44100 = 417 bytes (no padding).
        const int frameLength = 417;
        byte[] header = { 0xFF, 0xFB, 0x90, 0xC0 };
        using var ms = new MemoryStream();
        for (int i = 0; i < frameCount; i++)
        {
            ms.Write(header, 0, header.Length);
            ms.Write(new byte[frameLength - header.Length], 0, frameLength - header.Length);
        }
        return ms.ToArray();
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
    /// Writes the Close Encounters motif to a WAV file.
    /// </summary>
    /// <param name="path">File to write.</param>
    /// <param name="sampleRate">Sample rate to render at.</param>
    /// <param name="channels">Channel count.</param>
    /// <returns>The path written, for convenience.</returns>
    public static string WriteCloseEncountersWaveFile(string path, int sampleRate = 44100, int channels = 1)
    {
        var samples = BuildCloseEncountersSamples(sampleRate, channels);

        using (var writer = new WaveFileWriter(path, new WaveFormat(sampleRate, 16, channels)))
        {
            writer.WriteSamples(samples, 0, samples.Length);
        }

        return path;
    }
}
