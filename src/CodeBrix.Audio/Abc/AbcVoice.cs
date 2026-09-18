using System.Collections.Generic;

namespace CodeBrix.Audio.Abc;

/// <summary>
/// One voice of a tune, and the bars it holds.
/// </summary>
/// <remarks>
/// <para>
/// A tune with no <c>V:</c> field has exactly one voice, whose <see cref="Id"/> is an empty string.
/// A tune with <c>V:</c> fields has one voice per distinct id, in the order the ids first appeared,
/// and material can be given to a voice in one block or bar by bar - the two layouts in the
/// standard's own example produce the same voices.
/// </para>
/// <para>
/// <see cref="MidiProgram"/> and <see cref="MidiChannel"/> carry what the <c>%%MIDI</c> directives
/// asked for, when the tune carried any. The channel a voice actually plays on is settled during
/// conversion - see <see cref="AbcToMidiOptions.VoiceChannels"/>.
/// </para>
/// </remarks>
public sealed class AbcVoice
{
    private readonly List<AbcBar> _bars;

    /// <summary>
    /// Creates a voice.
    /// </summary>
    /// <param name="id">The voice id from its <c>V:</c> field, or an empty string.</param>
    /// <param name="name">The voice name from <c>name=</c> or <c>nm=</c>, or an empty string.</param>
    /// <param name="midiProgram">The program from <c>%%MIDI program</c>, or <see langword="null"/>.</param>
    /// <param name="midiChannel">The channel from <c>%%MIDI channel</c>, or <see langword="null"/>.</param>
    /// <param name="bars">The bars of this voice, in order.</param>
    public AbcVoice(string id, string name, int? midiProgram, int? midiChannel, IEnumerable<AbcBar> bars)
    {
        Id = id ?? string.Empty;
        Name = name ?? string.Empty;
        MidiProgram = midiProgram;
        MidiChannel = midiChannel;
        _bars = bars == null ? new List<AbcBar>() : new List<AbcBar>(bars);
    }

    /// <summary>
    /// The voice id from its <c>V:</c> field. An empty string for the single voice of a tune that
    /// has no <c>V:</c> fields.
    /// </summary>
    public string Id { get; }

    /// <summary>
    /// The voice name from the <c>name=</c> or <c>nm=</c> property, or an empty string. It becomes
    /// the track name in the converted MIDI.
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// The first program this voice's <c>%%MIDI program</c> directives asked for, 0 to 127, or
    /// <see langword="null"/> when it carried none.
    /// </summary>
    public int? MidiProgram { get; }

    /// <summary>
    /// The channel this voice's <c>%%MIDI channel</c> directive asked for, 1 to 16, or
    /// <see langword="null"/> when it carried none.
    /// </summary>
    public int? MidiChannel { get; }

    /// <summary>The bars of this voice, in order. Repeats are NOT unrolled.</summary>
    public IReadOnlyList<AbcBar> Bars => _bars;

    /// <summary>Describes the voice.</summary>
    /// <returns>For example <c>"V:T1 (12 bars)"</c>.</returns>
    public override string ToString() => $"V:{Id} ({_bars.Count} bar(s))";
}
