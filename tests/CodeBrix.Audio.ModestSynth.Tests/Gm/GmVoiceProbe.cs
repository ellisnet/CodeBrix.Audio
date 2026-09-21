using System;
using CodeBrix.Audio.ModestSynth.Internal.Gm;

namespace CodeBrix.Audio.ModestSynth.Tests.Gm;

// Renders ONE voicing straight through the voice, with no synthesizer, no channel, no sends and no
// master volume in the way - so a test of the voice architecture measures the voice and nothing else.
internal static class GmVoiceProbe
{
    internal const int SampleRate = Spectrum.SampleRate;
    internal const int BlockSize = 64;

    // A voicing built from nothing: one layer of the named recipe, no filter, no modulation, and an
    // envelope a test can state in full.
    internal static GmVoiceSpec Spec(GmTone tone = GmTone.Sine, double shape = double.NaN)
    {
        GmVoiceSpec spec = new GmVoiceSpec
        {
            Name = "probe",
            Layers = [new GmLayerSpec { Tone = tone, Shape = shape }],
            Amplitude = new GmEnvelopeSpec
            {
                Attack = 0.002, Decay = 1.0, Sustain = 1.0, Release = 0.05,
            },
            Filter = new GmFilterSpec { Mode = GmFilterMode.Off },
            Lfo = new GmLfoSpec { ToPitchCents = 0.0 },
            Level = 1.0,
            VelocityToLevel = 0.0,
            KeyToTime = 0.0,
            ReverbSend = 0.0,
        };

        return spec;
    }

    // A second or third component, ready to be added to a spec's layers.
    internal static GmLayerSpec Layer(GmTone tone, double level = 1.0, double transpose = 0.0) =>
        new GmLayerSpec { Tone = tone, Level = level, Transpose = transpose };

    // Fills in every layer's oscillator patch, which is what GmBank does once a row has been applied.
    internal static GmVoiceSpec Build(GmVoiceSpec spec)
    {
        for (int i = 0; i < spec.Layers.Length; i++)
        {
            GmLayerSpec layer = spec.Layers[i];
            layer.Patch = GmTones.Create(layer.Tone, layer.Shape, layer.Ring);
        }

        return spec;
    }

    internal static (float[] Left, float[] Right) Render(
        GmVoiceSpec spec,
        int key = 60,
        int velocity = 100,
        double hold = 0.5,
        double tail = 0.2,
        double modulationCents = 0.0,
        double bendSemitones = 0.0)
    {
        Build(spec);

        GmProgramRuntime runtime = new GmProgramRuntime(spec, SampleRate, 4, 0x1234u);
        GmVoice voice = new GmVoice(SampleRate, BlockSize);

        voice.Start(0, key, velocity, 0L, runtime, GmVoiceAdjustment.None, 0.0, 0.0, 0x2468u);

        int holdBlocks = Math.Max(1, (int)(hold * SampleRate / BlockSize));
        int tailBlocks = Math.Max(1, (int)(tail * SampleRate / BlockSize));

        float[] left = new float[(holdBlocks + tailBlocks) * BlockSize];
        float[] right = new float[left.Length];

        float[] blockLeft = new float[BlockSize];
        float[] blockRight = new float[BlockSize];

        for (int block = 0; block < holdBlocks + tailBlocks; block++)
        {
            if (block == holdBlocks) { voice.Release(force: false); }

            Array.Clear(blockLeft, 0, BlockSize);
            Array.Clear(blockRight, 0, BlockSize);

            voice.Render(blockLeft, blockRight, BlockSize, bendSemitones, modulationCents);

            Array.Copy(blockLeft, 0, left, block * BlockSize, BlockSize);
            Array.Copy(blockRight, 0, right, block * BlockSize, BlockSize);
        }

        return (left, right);
    }
}
