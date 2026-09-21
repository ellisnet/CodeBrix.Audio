using System;
using CodeBrix.Audio.Midi;

namespace CodeBrix.Audio.ModestSynth.Internal.Gm;

// THE BANK: 128 program voicings and 47 percussion voicings, each a family spine with a row of
// differences applied on top (plan D12).
//
// Nothing here is hand-written per program beyond the row itself. A row is applied to its template
// mechanically - a field the row states wins, a field it leaves alone keeps the template's answer -
// so a listening session's feedback lands as an edit to a row or, when it is a whole family, as one
// edit to a spine.
//
// The built voicings are cached and SHARED: a GmVoiceSpec holds no audio state and is never mutated
// after it is built, so one instance serves every synthesizer in the process. The per-program
// adjustments a consumer sets are applied on top of a spec at note-on and never touch it.
internal static class GmBank
{
    private static readonly object Gate = new object();
    private static readonly GmVoiceSpec[] Programs = new GmVoiceSpec[GeneralMidi.ProgramCount];

    private static readonly GmVoiceSpec[] Percussion =
        new GmVoiceSpec[GeneralMidi.HighestPercussionNote - GeneralMidi.LowestPercussionNote + 1];

    internal static bool IsPercussionNote(int noteNumber) =>
        noteNumber >= GeneralMidi.LowestPercussionNote && noteNumber <= GeneralMidi.HighestPercussionNote;

    internal static GmVoiceSpec Program(int program)
    {
        if (program < 0 || program >= GeneralMidi.ProgramCount)
        {
            throw new ArgumentOutOfRangeException(nameof(program), program,
                "A General MIDI program is 0 to 127.");
        }

        GmVoiceSpec built = Programs[program];
        if (built != null) { return built; }

        lock (Gate)
        {
            built = Programs[program];
            if (built != null) { return built; }

            built = Build(
                GmProgramRows.Row(program),
                GeneralMidi.FamilyOf((GeneralMidiProgram)program),
                GeneralMidi.DisplayName((GeneralMidiProgram)program),
                percussion: false);

            Programs[program] = built;
            return built;
        }
    }

    internal static GmVoiceSpec PercussionNote(int noteNumber)
    {
        if (!IsPercussionNote(noteNumber))
        {
            throw new ArgumentOutOfRangeException(nameof(noteNumber), noteNumber,
                "A General MIDI percussion note is 35 to 81.");
        }

        int index = noteNumber - GeneralMidi.LowestPercussionNote;

        GmVoiceSpec built = Percussion[index];
        if (built != null) { return built; }

        lock (Gate)
        {
            built = Percussion[index];
            if (built != null) { return built; }

            built = Build(
                GmPercussionRows.Row(noteNumber),
                GeneralMidiProgramFamily.Percussive,
                GeneralMidi.DisplayName((GeneralMidiPercussion)noteNumber),
                percussion: true);

            Percussion[index] = built;
            return built;
        }
    }

    private static GmVoiceSpec Build(
        GmRow row, GeneralMidiProgramFamily family, string displayName, bool percussion)
    {
        GmVoiceSpec spec = percussion
            ? GmFamilyTemplates.Percussion()
            : GmFamilyTemplates.For(row.Template ?? family);

        spec.Name = row.Name ?? displayName;

        ApplyMainLayer(spec, row);

        // The amplitude envelope is settled BEFORE the extra layers and the filter, because both of
        // those inherit from it when they state only part of an envelope of their own.
        ApplyAmplitude(spec, row);
        ApplyExtraLayers(spec, row);
        ApplyFilter(spec, row);
        ApplyModulation(spec, row);
        ApplyMix(spec, row, percussion);

        for (int i = 0; i < spec.Layers.Length; i++)
        {
            GmLayerSpec layer = spec.Layers[i];
            layer.Patch = GmTones.Create(layer.Tone, layer.Shape, layer.Ring);
        }

        LimitOscillators(spec);

        return spec;
    }

    private static void ApplyMainLayer(GmVoiceSpec spec, GmRow row)
    {
        GmLayerSpec layer = spec.Layers[0];

        if (row.Tone != GmTone.None)
        {
            layer.Tone = row.Tone;

            // A row that changes the recipe starts that recipe from ITS OWN defaults rather than
            // inheriting knobs meant for a different one.
            layer.Shape = double.NaN;
            layer.Ring = double.NaN;
        }

        Set(ref layer.Shape, row.Shape);
        Set(ref layer.Ring, row.Ring);
        Set(ref layer.Transpose, row.Transpose);
        Set(ref layer.FineCents, row.Fine);
        Set(ref layer.Level, row.MainLevel);

        if (row.Unison > 0) { layer.Unison = row.Unison; }

        Set(ref layer.UnisonDetuneCents, row.Detune);
        Set(ref layer.UnisonSpread, row.Spread);
    }

    private static void ApplyExtraLayers(GmVoiceSpec spec, GmRow row)
    {
        if (row.Layer2 == null && row.Layer3 == null) { return; }

        int extra = (row.Layer2 == null ? 0 : 1) + (row.Layer3 == null ? 0 : 1);

        // A row's extra layers REPLACE whatever extras the template had: the pipe spine's breath
        // layer is the template's idea, and a row that states its own layers has a better one.
        GmLayerSpec[] layers = new GmLayerSpec[1 + extra];
        layers[0] = spec.Layers[0];

        int next = 1;
        if (row.Layer2 != null) { layers[next++] = LayerFrom(row.Layer2, spec); }
        if (row.Layer3 != null) { layers[next] = LayerFrom(row.Layer3, spec); }

        spec.Layers = layers;
    }

    private static GmLayerSpec LayerFrom(GmLayerRow row, GmVoiceSpec spec)
    {
        GmLayerSpec layer = new GmLayerSpec
        {
            Tone = row.Tone == GmTone.None ? GmTone.Sine : row.Tone,
            Shape = row.Shape,
            Ring = row.Ring,
            Level = row.Level,
            Transpose = row.Transpose,
            FineCents = row.Fine,
            Pan = row.Pan,
            FixedKey = row.FixedKey,
            Unison = row.Unison,
            UnisonDetuneCents = double.IsNaN(row.Detune) ? 8.0 : row.Detune,
            UnisonSpread = double.IsNaN(row.Spread) ? 0.7 : row.Spread,
        };

        if (row.HasOwnEnvelope)
        {
            GmEnvelopeSpec envelope = spec.Amplitude.Clone();

            Set(ref envelope.Delay, row.Delay);
            Set(ref envelope.Attack, row.Attack);
            Set(ref envelope.Hold, row.Hold);
            Set(ref envelope.Decay, row.Decay);
            Set(ref envelope.Sustain, row.Sustain);
            Set(ref envelope.Release, row.Release);

            if (row.LinearAttack) { envelope.LinearAttack = true; }

            layer.Envelope = envelope;
        }

        return layer;
    }

    private static void ApplyAmplitude(GmVoiceSpec spec, GmRow row)
    {
        Set(ref spec.Amplitude.Delay, row.Delay);
        Set(ref spec.Amplitude.Attack, row.Attack);
        Set(ref spec.Amplitude.Hold, row.Hold);
        Set(ref spec.Amplitude.Decay, row.Decay);
        Set(ref spec.Amplitude.Sustain, row.Sustain);
        Set(ref spec.Amplitude.Release, row.Release);

        if (row.LinearAttack) { spec.Amplitude.LinearAttack = true; }
    }

    private static void ApplyFilter(GmVoiceSpec spec, GmRow row)
    {
        if (row.FilterOff) { spec.Filter.Mode = GmFilterMode.Off; }
        else if (row.Filter != GmFilterMode.Off) { spec.Filter.Mode = row.Filter; }

        Set(ref spec.Filter.Cutoff, row.Cutoff);
        Set(ref spec.Filter.Resonance, row.Resonance);
        Set(ref spec.Filter.KeyTracking, row.KeyTracking);
        Set(ref spec.Filter.VelocityOctaves, row.VelocityOctaves);
        Set(ref spec.Filter.EnvelopeOctaves, row.FilterEnvelope);

        bool statesEnvelope =
            !double.IsNaN(row.FilterAttack) || !double.IsNaN(row.FilterDecay) ||
            !double.IsNaN(row.FilterSustain) || !double.IsNaN(row.FilterRelease);

        if (!statesEnvelope) { return; }

        GmEnvelopeSpec envelope = spec.Filter.Envelope == null
            ? spec.Amplitude.Clone()
            : spec.Filter.Envelope;

        Set(ref envelope.Attack, row.FilterAttack);
        Set(ref envelope.Decay, row.FilterDecay);
        Set(ref envelope.Sustain, row.FilterSustain);
        Set(ref envelope.Release, row.FilterRelease);

        spec.Filter.Envelope = envelope;
    }

    private static void ApplyModulation(GmVoiceSpec spec, GmRow row)
    {
        Set(ref spec.Lfo.Rate, row.VibratoRate);
        Set(ref spec.Lfo.Delay, row.VibratoDelay);
        Set(ref spec.Lfo.FadeIn, row.VibratoFade);
        Set(ref spec.Lfo.ToPitchCents, row.VibratoDepth);
        Set(ref spec.Lfo.ToLevel, row.Tremolo);
        Set(ref spec.Lfo.ToCutoffOctaves, row.FilterLfo);

        Set(ref spec.PitchEnvelopeSemitones, row.PitchEnvelope);
        Set(ref spec.PitchEnvelopeSeconds, row.PitchEnvelopeTime);
    }

    private static void ApplyMix(GmVoiceSpec spec, GmRow row, bool percussion)
    {
        Set(ref spec.Level, row.Level);
        Set(ref spec.Pan, row.Pan);
        Set(ref spec.VelocityToLevel, row.VelocityToLevel);
        Set(ref spec.VelocityToAttack, row.VelocityToAttack);
        Set(ref spec.KeyToTime, row.KeyToTime);
        Set(ref spec.ReverbSend, row.Reverb);
        Set(ref spec.ChorusSend, row.Chorus);
        Set(ref spec.FixedKey, row.FixedKey);

        spec.ExclusiveGroup = row.ExclusiveGroup;
        spec.Insert = row.Insert;

        if (percussion || row.IgnoreNoteOff) { spec.IgnoreNoteOff = true; }
    }

    // A voice sizes its arrays once, so a row that asks for more oscillators than a voice can hold
    // has its unison trimmed rather than being allowed to overflow. No row in the bank reaches this;
    // it is here so a future retune cannot break the render path.
    private static void LimitOscillators(GmVoiceSpec spec)
    {
        while (spec.OscillatorCount > GmVoiceSpec.MaximumOscillators)
        {
            int widest = 0;

            for (int i = 1; i < spec.Layers.Length; i++)
            {
                if (spec.Layers[i].OscillatorCount > spec.Layers[widest].OscillatorCount) { widest = i; }
            }

            spec.Layers[widest].Unison = spec.Layers[widest].OscillatorCount - 1;
        }
    }

    private static void Set(ref double target, double value)
    {
        if (!double.IsNaN(value)) { target = value; }
    }
}
