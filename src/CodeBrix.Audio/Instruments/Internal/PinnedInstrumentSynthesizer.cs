using System;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Instruments.Internal;

/// <summary>
/// Holds any synthesizer to the one instrument it was built to play: the per-part contract of
/// <see cref="IInstrumentLibrary"/>, applied to a synthesizer that came from somewhere else.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="ProgramPinnedSynthesizer"/> does this for a <see cref="SoundFontSynthesizer"/> by
/// SELECTING a bank and a program on every channel. A substitute instrument has no program to
/// select - a Decent Sampler preset or an SFZ file IS one instrument - so all that is left to do
/// is make sure nothing in the music can move it: program change (0xC0) and bank select (CC 0 and
/// CC 32) are swallowed here and everything else passes through untouched.
/// </para>
/// <para>
/// It is not idle work. <see cref="Synth.Sfz.SfzSynthesizer"/> answers to program change, because
/// <c>loprog</c> and <c>hiprog</c> regions select on it; a substitute that is really a whole
/// multi-timbral synthesizer answers to both. A generated piece carries its own program changes,
/// and without this a part the caller voiced deliberately would be re-voiced by the music.
/// </para>
/// </remarks>
internal sealed class PinnedInstrumentSynthesizer : IMidiSynthesizer
{
    private const int BankSelectCoarse = 0x00;
    private const int BankSelectFine = 0x20;

    private readonly IMidiSynthesizer inner;

    private PinnedInstrumentSynthesizer(IMidiSynthesizer inner)
    {
        this.inner = inner;
    }

    /// <summary>The synthesizer underneath, for a caller that built it.</summary>
    internal IMidiSynthesizer Inner => inner;

    /// <summary>
    /// Pins a synthesizer to its instrument, or hands back one that is already pinned.
    /// </summary>
    /// <param name="synthesizer">The synthesizer to pin.</param>
    /// <returns>
    /// A synthesizer that ignores program change and bank select. A
    /// <see cref="ProgramPinnedSynthesizer"/> or a <see cref="PinnedInstrumentSynthesizer"/> is
    /// returned unchanged, because it already does.
    /// </returns>
    internal static IMidiSynthesizer Pin(IMidiSynthesizer synthesizer) =>
        synthesizer is PinnedInstrumentSynthesizer || synthesizer is ProgramPinnedSynthesizer
            ? synthesizer
            : new PinnedInstrumentSynthesizer(synthesizer);

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
    public void Reset() => inner.Reset();

    /// <inheritdoc />
    public void Render(Span<float> left, Span<float> right) => inner.Render(left, right);
}
