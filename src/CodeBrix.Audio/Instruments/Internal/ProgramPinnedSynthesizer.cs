using System;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Instruments.Internal;

/// <summary>
/// A synthesizer pinned to one sound on every channel: the per-part shape of
/// <see cref="IInstrumentLibrary"/>, over a synthesizer that is really multi-timbral.
/// </summary>
/// <remarks>
/// <para>
/// A per-part synthesizer exists because the CALLER decided what this part sounds like. The music
/// it is fed may still carry its own program changes - a generated piece usually does - and
/// letting those through would silently re-voice a part that was voiced deliberately. So bank
/// select and program change are swallowed here, and the binding is re-applied after a
/// <see cref="Reset"/> puts the underlying channels back to their defaults.
/// </para>
/// <para>
/// The binding is applied to EVERY channel, because a router forwards each message on the channel
/// the music used and a part can sit on any of them. The one channel that cannot be bound is the
/// percussion channel: <see cref="SoundFontSynthesizer"/> hard-wires it to the drum bank, so a
/// melodic part routed onto MIDI channel 10 sounds as percussion whatever is asked for.
/// </para>
/// </remarks>
internal sealed class ProgramPinnedSynthesizer : IMidiSynthesizer
{
    private const int ChannelCount = 16;
    private const int PercussionChannel = 9;
    private const int BankSelectCoarse = 0x00;
    private const int BankSelectFine = 0x20;

    private readonly IMidiSynthesizer inner;
    private readonly int bank;
    private readonly int program;

    /// <summary>Wraps a synthesizer and binds every channel to one bank and program.</summary>
    /// <param name="inner">The synthesizer to pin.</param>
    /// <param name="bank">The bank to select - 0 for the melodic bank, 128 for the drum bank.</param>
    /// <param name="program">The program number to select, 0 to 127.</param>
    internal ProgramPinnedSynthesizer(IMidiSynthesizer inner, int bank, int program)
    {
        this.inner = inner;
        this.bank = bank;
        this.program = program;

        ApplyBinding();
    }

    /// <summary>The synthesizer underneath, for the library that built this one.</summary>
    internal IMidiSynthesizer Inner => inner;

    /// <inheritdoc />
    public int SampleRate => inner.SampleRate;

    /// <inheritdoc />
    public int BlockSize => inner.BlockSize;

    /// <inheritdoc />
    public int ActiveVoiceCount => inner.ActiveVoiceCount;

    /// <inheritdoc />
    public float MasterVolume
    {
        get => inner.MasterVolume;
        set => inner.MasterVolume = value;
    }

    /// <inheritdoc />
    public void ProcessMidiMessage(int channel, int command, int data1, int data2)
    {
        // 0xC0 is a program change; 0xB0 with controller 0 or 32 is a bank select. Both would move
        // this part off the sound it was created to play.
        if (command == 0xC0)
        {
            return;
        }

        if (command == 0xB0 && (data1 == BankSelectCoarse || data1 == BankSelectFine))
        {
            return;
        }

        inner.ProcessMidiMessage(channel, command, data1, data2);
    }

    /// <inheritdoc />
    public void NoteOffAll(bool immediate) => inner.NoteOffAll(immediate);

    /// <inheritdoc />
    public void Reset()
    {
        inner.Reset();
        ApplyBinding();
    }

    /// <inheritdoc />
    public void Render(Span<float> left, Span<float> right) => inner.Render(left, right);

    private void ApplyBinding()
    {
        for (var channel = 0; channel < ChannelCount; channel++)
        {
            // The percussion channel is already on the drum bank and cannot be moved off it, so
            // selecting a bank there would push it past 128 into a bank no SoundFont holds.
            if (channel != PercussionChannel)
            {
                inner.ProcessMidiMessage(channel, 0xB0, BankSelectCoarse, bank);
            }

            inner.ProcessMidiMessage(channel, 0xC0, program, 0);
        }
    }
}
