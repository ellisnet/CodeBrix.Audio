using System;
using System.Collections.Generic;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Instruments.Internal;

/// <summary>
/// The multi-timbral shape of <see cref="MappedInstrumentLibrary"/>: one synthesizer that honours
/// the program changes the music carries, playing a channel with the developer's own instrument
/// whenever that channel's CURRENT program has one and with the base library's synthesizer
/// otherwise.
/// </summary>
/// <remarks>
/// <para>
/// THE SUBSTITUTIONS ARE A SNAPSHOT taken when this synthesizer was built. Setting another
/// instrument on the library afterwards changes what the NEXT synthesizer plays and leaves this
/// one alone, which is what lets a developer swap a voice while something is already sounding.
/// </para>
/// <para>
/// ONE SUBSTITUTE INSTANCE PER PROGRAM, shared by every channel currently playing that program and
/// built the first time such a channel is used. Every synthesizer here is multi-timbral in the
/// sense that matters - it keeps its controller state per channel - so two parts on one substituted
/// program cost one instrument and one voice pool, the same way the base library's own synthesizer
/// serves all sixteen channels from one pool. The kit substitution is one instance as well, and the
/// base library's synthesizer is built only if a channel actually needs it.
/// </para>
/// <para>
/// WHEN A CHANNEL MOVES between the base and a substitute - which happens on a program change, and
/// only there - the notes it is sounding are released cleanly: the sustain pedal is lifted first,
/// because a synthesizer that honours it would otherwise hold notes on a channel nothing is playing
/// any more, and then all notes on that channel are released. The channel's recorded state - bank,
/// program, every controller the music has sent, the pitch bend and the channel pressure - is then
/// replayed onto whichever synthesizer takes the channel over, so volume, pan, expression, sustain
/// and pitch bend follow the part rather than staying behind with the instrument that used to play
/// it. The parameter-select controllers and the data entry that follows them are replayed last, in
/// that order, so "select, then set" is replayed as "select, then set".
/// </para>
/// <para>
/// THE PERCUSSION CHANNEL - wire channel 9, the musician's channel 10 - is played by the kit
/// substitution whenever there is one, whatever program the music selects there. With no kit
/// substitution it is played by the base library's synthesizer, which decides for itself what
/// channel 10 means.
/// </para>
/// <para>
/// WITH NO BASE LIBRARY a channel whose program has no instrument of its own is SILENT. That is the
/// deliberate answer rather than an error: a piece carrying a part this library was never given an
/// instrument for should not stop the music.
/// </para>
/// </remarks>
internal sealed class MappedMultiTimbralSynthesizer : IMidiSynthesizer
{
    private const int ChannelCount = 16;
    private const int PercussionWireChannel = 9;
    private const int ControllerCount = 128;
    private const int BankSelectCoarse = 0x00;
    private const int BankSelectFine = 0x20;
    private const int SustainPedal = 0x40;
    private const int ResetAllControllers = 0x79;
    private const int AllNotesOff = 0x7B;
    private const int LowestChannelModeController = 0x78;

    // The parameter-select pair and the data entry that acts on it, replayed after everything else
    // and in this order, because replaying the data entry first would set whatever parameter
    // happened to be selected before.
    private static readonly int[] ParameterControllerOrder = [0x63, 0x62, 0x65, 0x64, 0x06, 0x26];

    private readonly int sampleRate;
    private readonly int blockSize;
    private readonly IInstrumentLibrary baseLibrary;
    private readonly IReadOnlyDictionary<int, InstrumentSubstitution> substitutions;
    private readonly InstrumentSubstitution percussion;
    private readonly ChannelState[] channels = new ChannelState[ChannelCount];
    private readonly Dictionary<int, IMidiSynthesizer> substituteByProgram =
        new Dictionary<int, IMidiSynthesizer>();

    private IMidiSynthesizer baseSynthesizer;
    private IMidiSynthesizer percussionSynthesizer;
    private float[] scratchLeft = [];
    private float[] scratchRight = [];
    private float masterVolume = 1.0F;

    /// <summary>Builds the multi-timbral shape over a snapshot of a mapped library.</summary>
    /// <param name="sampleRate">The sample rate to render at, in Hz.</param>
    /// <param name="blockSize">The granularity a sequencer interleaves messages with audio at.</param>
    /// <param name="baseLibrary">The library that plays everything not substituted, or null.</param>
    /// <param name="substitutions">The melodic substitutions, by General MIDI program.</param>
    /// <param name="percussion">The kit substitution, or null.</param>
    internal MappedMultiTimbralSynthesizer(
        int sampleRate,
        int blockSize,
        IInstrumentLibrary baseLibrary,
        IReadOnlyDictionary<int, InstrumentSubstitution> substitutions,
        InstrumentSubstitution percussion)
    {
        this.sampleRate = sampleRate;
        this.blockSize = blockSize;
        this.baseLibrary = baseLibrary;
        this.substitutions = substitutions;
        this.percussion = percussion;

        for (var channel = 0; channel < ChannelCount; channel++)
        {
            channels[channel] = new ChannelState();
        }
    }

    /// <inheritdoc />
    public int SampleRate => sampleRate;

    /// <inheritdoc />
    public int BlockSize => blockSize;

    /// <inheritdoc />
    public int ActiveVoiceCount
    {
        get
        {
            var total = 0;

            foreach (var synthesizer in CreatedSynthesizers())
            {
                total += synthesizer.ActiveVoiceCount;
            }

            return total;
        }
    }

    /// <inheritdoc />
    public float MasterVolume
    {
        get => masterVolume;
        set => masterVolume = value;
    }

    /// <inheritdoc />
    public void ProcessMidiMessage(int channel, int command, int data1, int data2)
    {
        if (channel < 0 || channel >= ChannelCount)
        {
            return;
        }

        var state = channels[channel];

        if (command == 0xC0)
        {
            state.Program = data1 & 0x7F;

            var target = TargetFor(channel);

            if (ReferenceEquals(target, state.Active))
            {
                // The channel stays where it is, so the program change is simply news for whoever
                // is playing it: the base re-voices, a pinned substitute ignores it.
                state.Active?.ProcessMidiMessage(channel, command, data1, data2);
                return;
            }

            MoveChannel(channel, target);
            return;
        }

        // Activating BEFORE the message is recorded is what keeps a controller from being applied
        // twice: the replay carries everything sent up to now, and the message itself follows.
        var active = state.Active ?? Activate(channel);

        Record(state, command, data1, data2);

        active?.ProcessMidiMessage(channel, command, data1, data2);
    }

    /// <inheritdoc />
    public void NoteOffAll(bool immediate)
    {
        foreach (var synthesizer in CreatedSynthesizers())
        {
            synthesizer.NoteOffAll(immediate);
        }
    }

    /// <inheritdoc />
    public void Reset()
    {
        foreach (var synthesizer in CreatedSynthesizers())
        {
            synthesizer.Reset();
        }

        for (var channel = 0; channel < ChannelCount; channel++)
        {
            channels[channel].Reset();
        }
    }

    /// <inheritdoc />
    public void Render(Span<float> left, Span<float> right)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException("The output buffers for the left and right must be the same length.");
        }

        left.Clear();
        right.Clear();

        var frames = left.Length;

        if (frames == 0)
        {
            return;
        }

        EnsureScratch(frames);

        foreach (var synthesizer in CreatedSynthesizers())
        {
            var scratchL = scratchLeft.AsSpan(0, frames);
            var scratchR = scratchRight.AsSpan(0, frames);

            synthesizer.Render(scratchL, scratchR);

            for (var index = 0; index < frames; index++)
            {
                left[index] += scratchL[index];
                right[index] += scratchR[index];
            }
        }

        if (masterVolume == 1.0F)
        {
            return;
        }

        for (var index = 0; index < frames; index++)
        {
            left[index] *= masterVolume;
            right[index] *= masterVolume;
        }
    }

    // Every child that has actually been built, each exactly once - a substitute shared by two
    // channels must be rendered once, not twice.
    private IEnumerable<IMidiSynthesizer> CreatedSynthesizers()
    {
        if (baseSynthesizer != null)
        {
            yield return baseSynthesizer;
        }

        if (percussionSynthesizer != null)
        {
            yield return percussionSynthesizer;
        }

        foreach (var substitute in substituteByProgram.Values)
        {
            yield return substitute;
        }
    }

    private IMidiSynthesizer Activate(int channel)
    {
        MoveChannel(channel, TargetFor(channel));
        return channels[channel].Active;
    }

    private IMidiSynthesizer TargetFor(int channel)
    {
        if (channel == PercussionWireChannel && percussion != null)
        {
            return percussionSynthesizer ??=
                percussion.CreateSynthesizer(sampleRate, "the percussion kit");
        }

        if (channel != PercussionWireChannel)
        {
            var program = channels[channel].Program;

            if (substitutions.TryGetValue(program, out var substitution))
            {
                if (!substituteByProgram.TryGetValue(program, out var substitute))
                {
                    substitute = substitution.CreateSynthesizer(
                        sampleRate, $"General MIDI program {program}");
                    substituteByProgram.Add(program, substitute);
                }

                return substitute;
            }
        }

        return BaseSynthesizer();
    }

    private IMidiSynthesizer BaseSynthesizer()
    {
        if (baseSynthesizer != null || baseLibrary == null)
        {
            return baseSynthesizer;
        }

        var synthesizer = baseLibrary.CreateMultiTimbralSynthesizer(sampleRate);

        if (synthesizer == null)
        {
            throw new InvalidOperationException(
                $"The instrument library '{baseLibrary.Name}' returned null instead of a " +
                "multi-timbral synthesizer.");
        }

        if (synthesizer.SampleRate != sampleRate)
        {
            throw new InvalidOperationException(
                $"The instrument library '{baseLibrary.Name}' was asked for {sampleRate} Hz and " +
                $"built a synthesizer that renders at {synthesizer.SampleRate} Hz.");
        }

        baseSynthesizer = synthesizer;
        return baseSynthesizer;
    }

    private void MoveChannel(int channel, IMidiSynthesizer target)
    {
        var state = channels[channel];
        var previous = state.Active;

        if (previous != null && !ReferenceEquals(previous, target))
        {
            // Lift the pedal before releasing, or a synthesizer that honours it holds these notes
            // for ever: nothing is going to lift it on a channel this instrument no longer plays.
            previous.ProcessMidiMessage(channel, 0xB0, SustainPedal, 0);
            previous.ProcessMidiMessage(channel, 0xB0, AllNotesOff, 0);
        }

        state.Active = target;

        if (target != null)
        {
            Replay(channel, target);
        }
    }

    private void Replay(int channel, IMidiSynthesizer target)
    {
        var state = channels[channel];

        // The bank before the program, so a target that honours both lands on the right voicing.
        if (state.Controllers[BankSelectCoarse] >= 0)
        {
            target.ProcessMidiMessage(channel, 0xB0, BankSelectCoarse, state.Controllers[BankSelectCoarse]);
        }

        if (state.Controllers[BankSelectFine] >= 0)
        {
            target.ProcessMidiMessage(channel, 0xB0, BankSelectFine, state.Controllers[BankSelectFine]);
        }

        target.ProcessMidiMessage(channel, 0xC0, state.Program, 0);

        for (var controller = 0; controller < ControllerCount; controller++)
        {
            if (state.Controllers[controller] < 0 || IsReplayedLast(controller))
            {
                continue;
            }

            if (controller == BankSelectCoarse || controller == BankSelectFine)
            {
                continue;
            }

            target.ProcessMidiMessage(channel, 0xB0, controller, state.Controllers[controller]);
        }

        foreach (var controller in ParameterControllerOrder)
        {
            if (state.Controllers[controller] >= 0)
            {
                target.ProcessMidiMessage(channel, 0xB0, controller, state.Controllers[controller]);
            }
        }

        if (state.PitchBend >= 0)
        {
            target.ProcessMidiMessage(channel, 0xE0, state.PitchBend & 0x7F, (state.PitchBend >> 7) & 0x7F);
        }

        if (state.ChannelPressure >= 0)
        {
            target.ProcessMidiMessage(channel, 0xD0, state.ChannelPressure, 0);
        }
    }

    private static bool IsReplayedLast(int controller)
    {
        foreach (var parameterController in ParameterControllerOrder)
        {
            if (parameterController == controller)
            {
                return true;
            }
        }

        return false;
    }

    private static void Record(ChannelState state, int command, int data1, int data2)
    {
        switch (command)
        {
            case 0xB0:
                var controller = data1 & 0x7F;

                if (controller >= LowestChannelModeController)
                {
                    // A channel mode message is an action rather than a setting, so there is
                    // nothing to remember - except that "reset all controllers" makes everything
                    // remembered so far wrong.
                    if (controller == ResetAllControllers)
                    {
                        state.ResetControllers();
                    }

                    return;
                }

                state.Controllers[controller] = data2 & 0x7F;
                return;

            case 0xE0:
                state.PitchBend = (data1 & 0x7F) | ((data2 & 0x7F) << 7);
                return;

            case 0xD0:
                state.ChannelPressure = data1 & 0x7F;
                return;
        }
    }

    private void EnsureScratch(int frames)
    {
        if (scratchLeft.Length >= frames)
        {
            return;
        }

        scratchLeft = new float[frames];
        scratchRight = new float[frames];
    }

    // What one channel is doing: the program it is on, the child playing it, and everything the
    // music has said about it that a replacement would need to be told.
    private sealed class ChannelState
    {
        internal ChannelState()
        {
            Controllers = new int[ControllerCount];
            Reset();
        }

        internal int[] Controllers { get; }

        internal int Program { get; set; }

        internal int PitchBend { get; set; }

        internal int ChannelPressure { get; set; }

        internal IMidiSynthesizer Active { get; set; }

        internal void Reset()
        {
            Program = 0;
            Active = null;
            ResetControllers();
        }

        internal void ResetControllers()
        {
            Array.Fill(Controllers, -1);
            PitchBend = -1;
            ChannelPressure = -1;
        }
    }
}
