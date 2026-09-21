using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Tests.Synth;

/// <summary>
/// A synthesizer that sounds continuously as a sum of sine partials, and renders a partial ONLY
/// when the sample rate it was built at can carry it - which is what every additive synthesizer
/// does, and what makes an instrument's measured loudness depend on the rate it is measured at.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="RecordingSynthesizer"/> renders a flat level, which measures the same at every rate.
/// This one is for the opposite question: a bright instrument whose energy sits above half of a low
/// measuring rate has almost none of its loudness at that rate, and a level measurement taken there
/// hands it a make-up gain many times too large.
/// </para>
/// <para>
/// It ignores every MIDI message and sounds from the first frame, so the RMS over a render is the
/// partials' own and a test can work it out by hand.
/// </para>
/// </remarks>
internal sealed class BandLimitedTestSynthesizer : IMidiSynthesizer
{
    private readonly (double Frequency, float Amplitude)[] partials;

    private long frame;

    /// <summary>Creates a synthesizer sounding the given partials, where the rate allows.</summary>
    /// <param name="sampleRate">The rate it renders at.</param>
    /// <param name="partials">The partials: a frequency in Hz and a peak amplitude each.</param>
    public BandLimitedTestSynthesizer(
        int sampleRate, IReadOnlyList<(double Frequency, float Amplitude)> partials)
    {
        SampleRate = sampleRate;
        MasterVolume = 1.0F;

        var sounding = new List<(double, float)>(partials.Count);
        foreach (var partial in partials)
        {
            // The guard every additive synthesizer has: a partial at or above the Nyquist frequency
            // cannot be represented, so it is not written at all.
            if (partial.Frequency * 2.0 < sampleRate)
            {
                sounding.Add((partial.Frequency, partial.Amplitude));
            }
        }

        this.partials = sounding.ToArray();
    }

    /// <inheritdoc />
    public int SampleRate { get; }

    /// <inheritdoc />
    public int BlockSize => 64;

    /// <inheritdoc />
    public int ActiveVoiceCount => partials.Length;

    /// <inheritdoc />
    public float MasterVolume { get; set; }

    /// <summary>How many of the partials this rate can carry.</summary>
    public int SoundingPartialCount => partials.Length;

    /// <inheritdoc />
    public void ProcessMidiMessage(int channel, int command, int data1, int data2)
    {
        // Deliberately ignored: the point of this synthesizer is a known, steady level.
    }

    /// <inheritdoc />
    public void NoteOffAll(bool immediate)
    {
        // Deliberately ignored; see ProcessMidiMessage.
    }

    /// <inheritdoc />
    public void Reset() => frame = 0;

    /// <inheritdoc />
    public void Render(Span<float> left, Span<float> right)
    {
        for (var i = 0; i < left.Length; i++)
        {
            var value = 0.0;
            foreach (var partial in partials)
            {
                value += partial.Amplitude *
                    Math.Sin(2.0 * Math.PI * partial.Frequency * (frame + i) / SampleRate);
            }

            left[i] = (float)value;
            right[i] = (float)value;
        }

        frame += left.Length;
    }
}
