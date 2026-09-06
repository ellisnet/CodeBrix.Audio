using System;
using System.Collections.Generic;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using CodeBrix.Audio.Synth.DecentSampler;
using CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

namespace CodeBrix.Audio.Tests.Synth.Mpe;

/// <summary>
/// Builds the MIDI performances the MPE tests play, in code, and renders them through a Decent
/// Sampler instrument offline.
/// </summary>
/// <remarks>
/// Nothing here comes from a file on disk: an expressive performance is only a particular pattern of
/// ordinary MIDI messages, so it can be written out message by message and the engine cannot tell
/// the difference.
/// </remarks>
internal sealed class MpeSequences : IDisposable
{
    /// <summary>Ticks per quarter note in every sequence built here.</summary>
    public const int TicksPerQuarterNote = 960;

    /// <summary>The sample rate every MPE test renders at.</summary>
    public const int SampleRate = 44100;

    /// <summary>The frequency of the tone the fixture instrument plays at its root note.</summary>
    public const double RootHertz = 440.0;

    /// <summary>Ticks per second at the fixture tempo of 120 BPM.</summary>
    public const double TicksPerSecond = TicksPerQuarterNote * 2.0;

    private readonly DecentSamplerEngineFixtures _files;

    private MpeSequences(DecentSamplerEngineFixtures files, DecentSamplerInstrument instrument)
    {
        _files = files;
        Instrument = instrument;
    }

    /// <summary>The loaded instrument.</summary>
    public DecentSamplerInstrument Instrument { get; }

    /// <summary>The synthesizer the last render used, so a test can read its state afterwards.</summary>
    public DecentSamplerSynthesizer LastSynthesizer { get; private set; }

    /// <summary>
    /// A one-zone instrument playing a 440 Hz sine at its root note, with a very short release so
    /// that one note's tail does not colour the next note's pitch measurement.
    /// </summary>
    /// <param name="modulators">The inside of a <c>&lt;modulators&gt;</c> element, or null.</param>
    /// <param name="lowestNote">
    /// The lowest key the zone answers to. A note below it still reaches the MPE state - the channel
    /// rules are about channels, not about whether a note sounds - but starts no voice, which is how
    /// a test isolates one sounding note from another on the same channel.
    /// </param>
    /// <returns>The fixture. The caller disposes it.</returns>
    public static MpeSequences Create(string modulators = null, int lowestNote = 0)
    {
        var files = DecentSamplerEngineFixtures.Create();
        files.WriteSineWav("tone.wav", RootHertz, 0.5f, SampleRate * 6);

        var xml =
            "<DecentSampler><groups><group release=\"0.005\">" +
            "<sample path=\"tone.wav\" rootNote=\"60\" loNote=\"" + lowestNote +
            "\" hiNote=\"127\" />" +
            "</group></groups>" +
            (modulators == null ? string.Empty : "<modulators>" + modulators + "</modulators>") +
            "</DecentSampler>";

        return new MpeSequences(files, files.LoadPreset(xml));
    }

    /// <summary>Ticks for a time in seconds at the fixture tempo.</summary>
    /// <param name="seconds">The time.</param>
    /// <returns>The tick.</returns>
    public static long Ticks(double seconds) => (long)Math.Round(seconds * TicksPerSecond);

    /// <summary>The 14-bit pitch-bend value that bends by a fraction of the channel's range.</summary>
    /// <param name="normalized">The bend, -1 to 1.</param>
    /// <returns>The 14-bit value, 0 to 16383.</returns>
    public static int BendValue(double normalized) =>
        Math.Clamp((int)Math.Round((normalized * 8192.0) + 8192.0), 0, 16383);

    /// <summary>Turns a list of events into a playable sequence at 120 BPM.</summary>
    /// <param name="events">The events, in any order; they are sorted on export.</param>
    /// <returns>The sequence.</returns>
    public static MidiSequence Sequence(IEnumerable<MidiEvent> events)
    {
        var collection = new MidiEventCollection(0, TicksPerQuarterNote);
        var track = collection.AddTrack();

        track.Add(new TempoEvent(500000, 0));

        foreach (var midiEvent in events)
        {
            track.Add(midiEvent);
        }

        collection.PrepareForExport();
        return MidiSequence.FromEvents(collection);
    }

    /// <summary>A note-on. Channels are 1-based, as the MIDI event model writes them.</summary>
    /// <param name="seconds">When the note starts.</param>
    /// <param name="channel">The channel, 1 to 16.</param>
    /// <param name="key">The note number.</param>
    /// <param name="velocity">The note-on velocity.</param>
    /// <returns>The event.</returns>
    public static MidiEvent NoteOn(double seconds, int channel, int key, int velocity = 100) =>
        new NoteEvent(Ticks(seconds), channel, MidiCommandCode.NoteOn, key, velocity);

    /// <summary>A note-off carrying a release velocity.</summary>
    /// <param name="seconds">When the note ends.</param>
    /// <param name="channel">The channel, 1 to 16.</param>
    /// <param name="key">The note number.</param>
    /// <param name="velocity">The release velocity.</param>
    /// <returns>The event.</returns>
    public static MidiEvent NoteOff(double seconds, int channel, int key, int velocity = 0) =>
        new NoteEvent(Ticks(seconds), channel, MidiCommandCode.NoteOff, key, velocity);

    /// <summary>A pitch bend as a fraction of the channel's range.</summary>
    /// <param name="seconds">When it happens.</param>
    /// <param name="channel">The channel, 1 to 16.</param>
    /// <param name="normalized">The bend, -1 to 1.</param>
    /// <returns>The event.</returns>
    public static MidiEvent Bend(double seconds, int channel, double normalized) =>
        new PitchWheelChangeEvent(Ticks(seconds), channel, BendValue(normalized));

    /// <summary>A continuous-controller message.</summary>
    /// <param name="seconds">When it happens.</param>
    /// <param name="channel">The channel, 1 to 16.</param>
    /// <param name="controller">The controller number.</param>
    /// <param name="value">The value, 0 to 127.</param>
    /// <returns>The event.</returns>
    public static MidiEvent Controller(double seconds, int channel, int controller, int value) =>
        new ControlChangeEvent(Ticks(seconds), channel, (MidiController)controller, value);

    /// <summary>Channel pressure.</summary>
    /// <param name="seconds">When it happens.</param>
    /// <param name="channel">The channel, 1 to 16.</param>
    /// <param name="pressure">The pressure, 0 to 127.</param>
    /// <returns>The event.</returns>
    public static MidiEvent Pressure(double seconds, int channel, int pressure) =>
        new ChannelAfterTouchEvent(Ticks(seconds), channel, pressure);

    /// <summary>
    /// A registered-parameter write: the two selection controllers followed by data entry.
    /// </summary>
    /// <param name="seconds">When it happens.</param>
    /// <param name="channel">The channel, 1 to 16.</param>
    /// <param name="parameter">The registered parameter number, 0 to 16383.</param>
    /// <param name="dataMsb">The data entry most significant byte.</param>
    /// <param name="dataLsb">The data entry least significant byte, or -1 to send none.</param>
    /// <returns>The events, in order.</returns>
    public static IEnumerable<MidiEvent> Rpn(
        double seconds, int channel, int parameter, int dataMsb, int dataLsb = -1)
    {
        yield return Controller(seconds, channel, 101, (parameter >> 7) & 0x7F);
        yield return Controller(seconds, channel, 100, parameter & 0x7F);
        yield return Controller(seconds, channel, 6, dataMsb);

        if (dataLsb >= 0)
        {
            yield return Controller(seconds, channel, 38, dataLsb);
        }
    }

    /// <summary>Renders a sequence through this instrument and returns the left channel.</summary>
    /// <param name="sequence">The sequence to play.</param>
    /// <param name="seconds">How long to render for.</param>
    /// <param name="configure">Configures the synthesizer before it plays.</param>
    /// <returns>The left channel.</returns>
    public float[] Render(
        MidiSequence sequence, double seconds, Action<DecentSamplerSynthesizer> configure = null)
    {
        var settings = new DecentSamplerSynthesizerSettings(SampleRate) { MasterVolume = 1f };
        var synthesizer = new DecentSamplerSynthesizer(Instrument, settings);

        configure?.Invoke(synthesizer);
        LastSynthesizer = synthesizer;

        var sequencer = new MidiSequencer(synthesizer);
        sequencer.Play(sequence, loop: false);

        var frames = (int)Math.Ceiling(seconds * SampleRate);
        var left = new float[frames];
        var right = new float[frames];
        sequencer.Render(left, right);

        return left;
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        Instrument.Dispose();
        _files.Dispose();
    }
}
