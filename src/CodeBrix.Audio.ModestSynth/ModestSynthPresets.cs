using System;
using System.Collections.Generic;
using CodeBrix.Audio.ModestSynth.Oscillators;
using CodeBrix.Audio.ModestSynth.Patch;
using CodeBrix.Audio.ModestSynth.Wavetable;

namespace CodeBrix.Audio.ModestSynth;

/// <summary>
/// Six worked example patches for <see cref="ModestSynthesizer" />, one per waveform family, written
/// so they can be read as documentation and copied as a starting point.
/// </summary>
/// <remarks>
/// <para>
/// Every factory here returns a NEW <see cref="ModestPatch" /> each time, so changing what you get
/// back never changes what the next caller gets. <see cref="SettingsFor" /> hands back the amplitude
/// envelope, glide and volume that go with the patch - a pad wants a slow attack and a long release,
/// a plucked string wants neither - because a patch carries no envelope of its own.
/// </para>
/// <para>
/// They are examples, not presets in the Decent Sampler sense: nothing here reads or writes a file,
/// and none of it needs <c>ModestSynth.Register()</c>.
/// </para>
/// </remarks>
public static class ModestSynthPresets
{
    /// <summary>The name of the sub sine patch.</summary>
    public const string SubSineName = "sub sine";

    /// <summary>The name of the saw lead patch.</summary>
    public const string SawLeadName = "saw lead";

    /// <summary>The name of the plucked string patch.</summary>
    public const string PluckedStringName = "plucked string";

    /// <summary>The name of the FM electric piano patch.</summary>
    public const string ElectricPianoName = "electric piano";

    /// <summary>The name of the wavetable pad patch.</summary>
    public const string WavetablePadName = "wavetable pad";

    /// <summary>The name of the additive organ patch.</summary>
    public const string AdditiveOrganName = "additive organ";

    private const int PadFrameSize = 1024;

    private static readonly string[] AllNames =
    [
        SubSineName, SawLeadName, PluckedStringName, ElectricPianoName, WavetablePadName,
        AdditiveOrganName,
    ];

    private static readonly object PadGate = new object();

    private static WavetableFile padTable;

    /// <summary>Every patch name this class offers, in the order they are documented.</summary>
    public static IReadOnlyList<string> Names => AllNames;

    /// <summary>
    /// A plain sine, the quietest thing a synthesizer can make and the easiest to check a chain with.
    /// </summary>
    /// <returns>A new patch.</returns>
    /// <remarks>
    /// One partial, no shaping, nothing random. Useful as a sub-bass under something brighter, and as
    /// the reference tone when you want to know whether a problem is the oscillator or the rest of the
    /// signal path.
    /// </remarks>
    public static ModestPatch SubSine() => new ModestPatch { Waveform = ModestWaveform.Sine };

    /// <summary>
    /// A band-limited sawtooth: the classic subtractive lead, without the aliasing a naive ramp makes.
    /// </summary>
    /// <returns>A new patch.</returns>
    /// <remarks>
    /// <c>randomPhase</c> is on, so stacking two synthesizers on the same notes thickens the sound
    /// instead of doubling one waveform exactly.
    /// </remarks>
    public static ModestPatch SawLead() =>
        new ModestPatch
        {
            Waveform = ModestWaveform.Saw,
            RandomPhase = true,
        };

    /// <summary>
    /// A waveguide plucked string: nylon-ish, picked about a tenth of the way along, ringing for a
    /// few seconds at the bottom of the keyboard and far less at the top.
    /// </summary>
    /// <returns>A new patch.</returns>
    /// <remarks>
    /// <c>damping</c> is how UNdamped the string is, so 0.7 rings longer than the 0.5 default;
    /// <c>pluckType</c> blends the excitation from a mellow triangle at 0 to noise at 1. The string
    /// decays by itself, so the envelope that goes with it barely does anything.
    /// </remarks>
    public static ModestPatch PluckedString() =>
        new ModestPatch
        {
            Waveform = ModestWaveform.Pluck1,
            Damping = 0.7,
            PluckType = 0.35,
            RandomPhase = true,
        };

    /// <summary>
    /// A six-operator FM electric piano: a fast tine over a soft body, both decaying, brighter the
    /// harder the key is struck.
    /// </summary>
    /// <returns>A new patch.</returns>
    /// <remarks>
    /// Algorithm 5 is three independent carrier-and-modulator pairs. This uses two of them: operator 2
    /// at fourteen times the pitch, decaying in a quarter of a second, is the tine striking operator 1;
    /// operator 4 at the same pitch as operator 3 fills in the body. Operator 2's velocity sensitivity
    /// is what makes a hard note bright and a soft one round - the amplitude envelope alone cannot do
    /// that. Operators 5 and 6 are left silent.
    /// </remarks>
    public static ModestPatch ElectricPiano()
    {
        ModestPatch patch = new ModestPatch
        {
            Waveform = ModestWaveform.Fm6Op,
            FmAlgorithm = 5,
        };

        ModestFmOperator carrier = patch.GetFmOperator(1);
        carrier.Ratio = 1.0;
        carrier.Level = 1.0;
        carrier.Attack = 0.0;
        carrier.Decay = 2.4;
        carrier.Sustain = 0.0;
        carrier.Release = 0.4;

        ModestFmOperator tine = patch.GetFmOperator(2);
        tine.Ratio = 14.0;
        tine.Level = 0.55;
        tine.VelocitySensitivity = 4;
        tine.Attack = 0.0;
        tine.Decay = 0.25;
        tine.Sustain = 0.0;
        tine.Release = 0.1;

        ModestFmOperator body = patch.GetFmOperator(3);
        body.Ratio = 1.0;
        body.Level = 0.4;
        body.Attack = 0.004;
        body.Decay = 3.0;
        body.Sustain = 0.0;
        body.Release = 0.4;

        ModestFmOperator bodyModulator = patch.GetFmOperator(4);
        bodyModulator.Ratio = 1.0;
        bodyModulator.Level = 0.28;
        bodyModulator.VelocitySensitivity = 2;
        bodyModulator.Attack = 0.0;
        bodyModulator.Decay = 1.2;
        bodyModulator.Sustain = 0.0;
        bodyModulator.Release = 0.3;

        patch.GetFmOperator(5).Level = 0.0;
        patch.GetFmOperator(6).Level = 0.0;

        return patch;
    }

    /// <summary>
    /// A wavetable pad on a table built in code: four frames from a pure sine to a full sawtooth, so
    /// that moving <see cref="ModestPatch.WavetablePosition" /> opens the tone up.
    /// </summary>
    /// <returns>A new patch.</returns>
    /// <remarks>
    /// The table is generated once and shared by every patch this method returns, which is safe
    /// because a <see cref="WavetableFile" /> is immutable. It touches no file, so this is also the
    /// example to copy when a wavetable has to come from somewhere other than a .wav on disk.
    /// </remarks>
    public static ModestPatch WavetablePad() =>
        new ModestPatch
        {
            Waveform = ModestWaveform.Wavetable,
            WavetableTable = PadTable(),
            WavetableFrameSize = PadFrameSize,
            WavetablePosition = 0.25,
            WavetableFrameInterpolation = true,
            RandomPhase = true,
        };

    /// <summary>
    /// An additive organ: the drawbar partials of a tonewheel organ - 1, 2, 3, 4, 6 and 8 - summed
    /// directly, with the loudness compensation on so the stack sits where one sine would.
    /// </summary>
    /// <returns>A new patch.</returns>
    /// <remarks>
    /// Partials above Nyquist are dropped rather than folded, so the registration thins out towards
    /// the top of the keyboard exactly as a real one does.
    /// </remarks>
    public static ModestPatch AdditiveOrgan()
    {
        ModestPatch patch = new ModestPatch
        {
            Waveform = ModestWaveform.Harmonic,
            NumPartials = 16,
            HarmonicNormalization = 1.0,
        };

        patch.SetPartialLevel(1, 1.0);
        patch.SetPartialLevel(2, 0.75);
        patch.SetPartialLevel(3, 0.5);
        patch.SetPartialLevel(4, 0.6);
        patch.SetPartialLevel(6, 0.3);
        patch.SetPartialLevel(8, 0.35);

        return patch;
    }

    /// <summary>Builds the patch with a given name.</summary>
    /// <param name="name">One of <see cref="Names" />, matched case-insensitively.</param>
    /// <returns>A new patch.</returns>
    /// <exception cref="ArgumentException">The name is not one of <see cref="Names" />.</exception>
    public static ModestPatch Create(string name)
    {
        if (string.Equals(name, SubSineName, StringComparison.OrdinalIgnoreCase)) { return SubSine(); }
        if (string.Equals(name, SawLeadName, StringComparison.OrdinalIgnoreCase)) { return SawLead(); }
        if (string.Equals(name, PluckedStringName, StringComparison.OrdinalIgnoreCase)) { return PluckedString(); }
        if (string.Equals(name, ElectricPianoName, StringComparison.OrdinalIgnoreCase)) { return ElectricPiano(); }
        if (string.Equals(name, WavetablePadName, StringComparison.OrdinalIgnoreCase)) { return WavetablePad(); }
        if (string.Equals(name, AdditiveOrganName, StringComparison.OrdinalIgnoreCase)) { return AdditiveOrgan(); }

        throw new ArgumentException("There is no preset called \"" + name + "\".", nameof(name));
    }

    /// <summary>
    /// The amplitude envelope, glide and volume that go with a patch, since a patch carries none.
    /// </summary>
    /// <param name="name">One of <see cref="Names" />, matched case-insensitively.</param>
    /// <param name="sampleRate">The rate to synthesize at, in Hz.</param>
    /// <returns>New settings.</returns>
    /// <exception cref="ArgumentException">The name is not one of <see cref="Names" />.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate" /> is outside the supported range.</exception>
    public static ModestSynthesizerSettings SettingsFor(string name, int sampleRate)
    {
        ModestSynthesizerSettings settings = new ModestSynthesizerSettings(sampleRate);

        if (string.Equals(name, SubSineName, StringComparison.OrdinalIgnoreCase))
        {
            settings.Attack = 0.005;
            settings.Release = 0.12;
            return settings;
        }

        if (string.Equals(name, SawLeadName, StringComparison.OrdinalIgnoreCase))
        {
            settings.Attack = 0.004;
            settings.Decay = 0.35;
            settings.Sustain = 0.75;
            settings.Release = 0.18;
            settings.GlideSeconds = 0.06;
            return settings;
        }

        if (string.Equals(name, PluckedStringName, StringComparison.OrdinalIgnoreCase))
        {
            // The string decays on its own, so the envelope only has to avoid a click.
            settings.Attack = 0.0;
            settings.Release = 0.05;
            return settings;
        }

        if (string.Equals(name, ElectricPianoName, StringComparison.OrdinalIgnoreCase))
        {
            settings.Attack = 0.0;
            settings.Release = 0.3;
            return settings;
        }

        if (string.Equals(name, WavetablePadName, StringComparison.OrdinalIgnoreCase))
        {
            settings.Attack = 0.6;
            settings.Decay = 1.0;
            settings.Sustain = 0.8;
            settings.Release = 1.2;
            settings.MaximumPolyphony = 16;
            return settings;
        }

        if (string.Equals(name, AdditiveOrganName, StringComparison.OrdinalIgnoreCase))
        {
            // An organ is a switch: on at once, off at once. The master volume is lower than the
            // family default because an additive stack held at one sine's RMS still peaks far above
            // it - six partials in phase reach nearly three times their own RMS - and a chord adds
            // those peaks together.
            settings.Attack = 0.002;
            settings.Release = 0.02;
            settings.VelocityTracking = 0.0;
            settings.MasterVolume = 0.25f;
            return settings;
        }

        throw new ArgumentException("There is no preset called \"" + name + "\".", nameof(name));
    }

    // Four frames, built additively so that every one of them is band-limited before the mip map ever
    // sees it: a pure sine, then a third and a fifth, then eight partials, then sixteen. Position 0 is
    // hollow and position 1 is a sawtooth.
    private static WavetableFile PadTable()
    {
        lock (PadGate)
        {
            if (padTable != null) { return padTable; }

            int[] partialCounts = [1, 3, 8, 16];
            float[] samples = new float[partialCounts.Length * PadFrameSize];

            for (int frame = 0; frame < partialCounts.Length; frame++)
            {
                int partials = partialCounts[frame];
                int offset = frame * PadFrameSize;

                for (int i = 0; i < PadFrameSize; i++)
                {
                    double phase = 2.0 * Math.PI * i / PadFrameSize;
                    double value = 0.0;

                    for (int partial = 1; partial <= partials; partial++)
                    {
                        value += Math.Sin(phase * partial) / partial;
                    }

                    samples[offset + i] = (float)(value * 0.6);
                }
            }

            padTable = WavetableFile.FromSamples(samples, PadFrameSize, "ModestSynthPresets pad");
            return padTable;
        }
    }
}
