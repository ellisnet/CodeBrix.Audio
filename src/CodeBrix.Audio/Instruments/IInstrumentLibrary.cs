using System;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Instruments;

/// <summary>
/// A named set of instruments addressed the way MIDI addresses them - by General MIDI program, and
/// by note number on the percussion channel - that can produce an <see cref="IMidiSynthesizer"/>
/// on demand.
/// </summary>
/// <remarks>
/// <para>
/// CodeBrix.Audio ships this interface, a generic implementation over a SoundFont
/// (<see cref="SoundFontInstrumentLibrary"/>) and nothing else: no instruments of its own and no
/// library registered. A consumer registers one with <see cref="InstrumentLibraryRegistry"/>; the
/// General MIDI library realised by synthesis lives in the CodeBrix.Audio.ModestSynth package.
/// </para>
/// <para>
/// THERE ARE TWO SHAPES, and a library may offer either or both.
/// </para>
/// <list type="bullet">
/// <item>
/// <description>
/// PER-PART (<see cref="SupportsPerPart"/>): one synthesizer per voice, from
/// <see cref="CreateSynthesizer"/> and <see cref="CreatePercussionSynthesizer"/>. This is the
/// shape a voiced arrangement needs, because a per-voice gain and a layered second instrument both
/// require separate synthesizers - one multi-timbral synthesizer mixes internally at one level.
/// The parts are then played together through a <see cref="RoutingSynthesizer"/>. Creation is
/// per part and deliberately lazy, which is what lets a multi-hundred-megabyte sample library sit
/// behind this interface without loading everything.
/// </description>
/// </item>
/// <item>
/// <description>
/// MULTI-TIMBRAL (<see cref="SupportsMultiTimbral"/>): ONE synthesizer, from
/// <see cref="CreateMultiTimbralSynthesizer"/>, that honours program changes inline. This is the
/// "play this .mid with no configuration" case, where the music carries its own instrument
/// assignments.
/// </description>
/// </item>
/// </list>
/// <para>
/// ASKING FOR A SHAPE THE LIBRARY DOES NOT OFFER THROWS <see cref="NotSupportedException"/>. Check
/// <see cref="SupportsPerPart"/> or <see cref="SupportsMultiTimbral"/> first; the exception message
/// names the library and the shape it does offer.
/// </para>
/// <para>
/// Implementations must be safe to call from several threads at once, because the registry is
/// process-wide and nothing serialises the callers. The SYNTHESIZERS they hand back are not:
/// <see cref="IMidiSynthesizer"/> is single-threaded by contract.
/// </para>
/// </remarks>
public interface IInstrumentLibrary
{
    /// <summary>
    /// How a consumer asks for this library. Unique in the registry and matched
    /// case-insensitively - "ModestSynthGm", "FluidR3Gm".
    /// </summary>
    string Name { get; }

    /// <summary>
    /// What this library sounds like, in a sentence a user interface can show - what it is built
    /// from, and what it is good and bad at.
    /// </summary>
    string Description { get; }

    /// <summary>
    /// Which programs and percussion notes this library really covers, so a caller can avoid
    /// asking for what is not there.
    /// </summary>
    InstrumentCoverage Coverage { get; }

    /// <summary>
    /// Whether this library can produce one synthesizer per part - see
    /// <see cref="CreateSynthesizer"/> and <see cref="CreatePercussionSynthesizer"/>.
    /// </summary>
    bool SupportsPerPart { get; }

    /// <summary>
    /// Whether this library can produce one synthesizer that honours program changes inline - see
    /// <see cref="CreateMultiTimbralSynthesizer"/>.
    /// </summary>
    bool SupportsMultiTimbral { get; }

    /// <summary>
    /// Creates a synthesizer that plays ONE General MIDI program, for one part of an arrangement.
    /// </summary>
    /// <param name="program">The General MIDI program number, 0 to 127.</param>
    /// <param name="sampleRate">The sample rate to synthesize at, in Hz.</param>
    /// <returns>
    /// A synthesizer bound to that program. It stays bound: a program change arriving in the music
    /// does not re-voice a part the caller has voiced deliberately.
    /// </returns>
    /// <exception cref="NotSupportedException"><see cref="SupportsPerPart"/> is false.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="program"/> is outside 0 to 127, or <paramref name="sampleRate"/> is not positive.
    /// </exception>
    IMidiSynthesizer CreateSynthesizer(int program, int sampleRate);

    /// <summary>
    /// Creates the synthesizer that plays the percussion kit, where each note number is its own
    /// instrument rather than a pitch.
    /// </summary>
    /// <param name="sampleRate">The sample rate to synthesize at, in Hz.</param>
    /// <returns>A synthesizer playing the kit.</returns>
    /// <exception cref="NotSupportedException"><see cref="SupportsPerPart"/> is false.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive.</exception>
    IMidiSynthesizer CreatePercussionSynthesizer(int sampleRate);

    /// <summary>
    /// Creates one synthesizer that plays a whole piece on its own, honouring the program changes
    /// the music carries and treating channel 10 as percussion.
    /// </summary>
    /// <param name="sampleRate">The sample rate to synthesize at, in Hz.</param>
    /// <returns>A synthesizer for the whole piece.</returns>
    /// <exception cref="NotSupportedException"><see cref="SupportsMultiTimbral"/> is false.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive.</exception>
    IMidiSynthesizer CreateMultiTimbralSynthesizer(int sampleRate);
}
