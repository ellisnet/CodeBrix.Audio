using System;
using CodeBrix.Audio.Instruments;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Playback.Suno;

/// <summary>
/// What a <see cref="MultiTrackPlayer"/> built from a <see cref="SunoSong"/> should do beyond
/// putting one track on each stem: which instrument plays a MIDI stem, whether the measured
/// alignment is applied, and how percussion is handled.
/// </summary>
/// <remarks>
/// <para>
/// Every property has a default that suits an ordinary export, so <c>new SunoPlayerOptions()</c> is
/// a sensible thing to pass and passing nothing at all is equally sensible. The one thing worth
/// setting is an instrument: without an instrument library, an
/// <see cref="InstrumentFactory"/> or a General MIDI SoundFont there is nothing to play a MIDI stem
/// THROUGH, so the player is built from the recordings alone.
/// </para>
/// <para>
/// THE ONE-LINE WAY is <see cref="InstrumentLibraryName"/>, with
/// <see cref="StemInstruments"/> for the parts that want something else and
/// <see cref="MidiStems"/> for which parts play from their transcription at all:
/// </para>
/// <code>
/// using var player = song.CreatePlayer(new SunoPlayerOptions
/// {
///     InstrumentLibraryName = "ModestSynthGm",
///     MidiStems = SunoStemSelection.EverythingBut("Vocals", "Backing Vocals"),
///     AutoSetRelativeTrackLevels = true,
/// });
/// </code>
/// <para>
/// WHICH INSTRUMENT A STEM GETS - the order, most specific first:
/// </para>
/// <list type="number">
/// <item><description><see cref="StemInstruments"/>, for a stem named there;</description></item>
/// <item><description><see cref="InstrumentLibrary"/>, or <see cref="InstrumentLibraryName"/>
/// resolved through <see cref="InstrumentLibraryRegistry"/>;</description></item>
/// <item><description><see cref="InstrumentFactory"/>;</description></item>
/// <item><description><see cref="GeneralMidiSoundFontPath"/>, or the path the song was loaded
/// with.</description></item>
/// </list>
/// <para>
/// WITH NONE OF THE FIRST TWO SET, a player behaves exactly as it always has. Setting BOTH a
/// library and an <see cref="InstrumentFactory"/> is not an error - the library wins - but it is
/// reported in the player's <see cref="MultiTrackPlayer.Problems"/>, because a factory that is
/// never called is almost always a leftover.
/// </para>
/// </remarks>
public sealed class SunoPlayerOptions
{
    /// <summary>
    /// The SoundFonts a player shares when the options name no cache of their own. Loading a
    /// General MIDI SoundFont costs tens of megabytes and several hundred milliseconds, so one
    /// instance per file is all a process needs; call <see cref="SoundFontCache.Clear"/> on this to
    /// let go of them.
    /// </summary>
    public static SoundFontCache SharedSoundFonts { get; } = new SoundFontCache();

    /// <summary>
    /// Builds the instrument for one stem's MIDI, given the stem and the sample rate the
    /// synthesizer must render at. Null - the default - falls back to
    /// <see cref="GeneralMidiSoundFontPath"/>.
    /// </summary>
    /// <remarks>
    /// It is called once per stem per render context, and it may be called from a worker thread, so
    /// it must be able to build more than one instance and it must not hand out the same
    /// <see cref="IMidiSynthesizer"/> twice. Share the <see cref="SoundFont"/> or the SFZ instrument
    /// behind them instead; those are the expensive part and they are safe to share.
    /// </remarks>
    public Func<SunoStem, int, IMidiSynthesizer> InstrumentFactory { get; set; }

    /// <summary>
    /// The instrument library every MIDI stem is played through, BY REGISTERED NAME - the one-word
    /// way to change what the whole song sounds like. Null - the default - means no library.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Resolved through <see cref="InstrumentLibraryRegistry"/> WHEN THE PLAYER IS BUILT, so an
    /// unknown name is the registry's own error - listing what IS registered - on the line that
    /// builds the player, rather than a surprise from a worker thread in the middle of a song. An
    /// empty registry is the registry's own error too, naming what to register.
    /// </para>
    /// <para>
    /// Each stem gets <c>CreateSynthesizer(stem.GmProgram, rate)</c>, or
    /// <c>CreatePercussionSynthesizer(rate)</c> when the stem is percussion. A library that does
    /// not offer the per-part shape is refused by name, because a stems export is an arrangement of
    /// separate parts and one multi-timbral synthesizer cannot give them separate gains.
    /// </para>
    /// <para>
    /// Ignored when <see cref="InstrumentLibrary"/> holds an instance.
    /// </para>
    /// </remarks>
    public string InstrumentLibraryName { get; set; }

    /// <summary>
    /// The instrument library every MIDI stem is played through, as an INSTANCE - for a library
    /// that was never registered, or one built for this song alone. Null - the default - falls back
    /// to <see cref="InstrumentLibraryName"/>.
    /// </summary>
    /// <remarks>
    /// The same rules as <see cref="InstrumentLibraryName"/>: per-part synthesizers, percussion
    /// through the kit, and a library that does not offer the per-part shape is refused when the
    /// player is built.
    /// </remarks>
    public IInstrumentLibrary InstrumentLibrary { get; set; }

    /// <summary>
    /// An instrument for one named stem, overriding the library for that stem alone. Never null;
    /// empty by default.
    /// </summary>
    /// <remarks>
    /// Keyed by STEM NAME rather than by General MIDI program, because in a stems export two parts
    /// share channel 10 and two parts can share a program. See <see cref="SunoStemInstruments"/>.
    /// </remarks>
    public SunoStemInstruments StemInstruments { get; } = new SunoStemInstruments();

    /// <summary>
    /// Which stems start on their transcription instead of their recording. Null - the default -
    /// leaves every track on its recording, which is what a player has always done.
    /// </summary>
    /// <remarks>
    /// <see cref="SunoStemSelection.EverythingBut(string[])"/> is the usual answer: everything with
    /// a usable transcription except the vocals. Setting this replaces the loop over
    /// <see cref="MultiTrackPlayer.Tracks"/> that every consumer otherwise writes; each track's
    /// <see cref="PlayerTrack.ActiveSource"/> is still a property that can be changed afterwards.
    /// </remarks>
    public SunoStemSelection MidiStems { get; set; }

    /// <summary>
    /// The General MIDI SoundFont every MIDI stem is played through when
    /// <see cref="InstrumentFactory"/> is null. Null - the default - falls back to
    /// <see cref="SunoLoadOptions.GeneralMidiSoundFontPath"/>, the one the song was loaded with.
    /// </summary>
    /// <remarks>
    /// FluidR3_GM is the SoundFont to recommend: it is MIT licensed, it may be redistributed, and it
    /// covers all 128 programs and the standard drum kit that a stems export's programs refer to.
    /// </remarks>
    public string GeneralMidiSoundFontPath { get; set; }

    /// <summary>
    /// Where the SoundFont named by <see cref="GeneralMidiSoundFontPath"/> is loaded from and kept.
    /// Null - the default - uses <see cref="SharedSoundFonts"/>.
    /// </summary>
    public SoundFontCache SoundFontCache { get; set; }

    /// <summary>
    /// Whether each stem's MIDI is attached to its track as a second source, so that the track can
    /// be switched between the recording and a synthesized rendition of the same part. Defaults to
    /// <see langword="true"/>, and does nothing when there is no instrument to play a stem through.
    /// </summary>
    /// <remarks>
    /// A track holding two sources renders both on every block, which is what makes a switch
    /// seamless and is also the player's main cost. Turn this off for a player that will only ever
    /// play the recordings.
    /// </remarks>
    public bool IncludeMidiSources { get; set; } = true;

    /// <summary>
    /// Whether each stem's measured <see cref="SunoStem.AlignmentOffset"/> is applied to its track's
    /// <see cref="PlayerTrack.MidiSourceOffset"/>. Defaults to <see langword="true"/>.
    /// </summary>
    /// <remarks>
    /// It moves the synthesized rendition only. The recordings of an export are already in step with
    /// one another and are never moved; it is the machine transcription that sits tens or hundreds
    /// of milliseconds away from the notes it transcribed.
    /// </remarks>
    public bool ApplyAlignmentOffsets { get; set; } = true;

    /// <summary>
    /// Whether percussion tracks discard note-offs entirely instead of holding each note for
    /// <see cref="SunoLoadOptions.MinimumNoteHold"/>. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Both settings answer the same problem - an export writes a drum hit as a note-on with a
    /// note-off a tick or two later, which played literally is a click. Ignoring note-offs is right
    /// for one-shot samples that already have the right length, and wrong for a kit whose sounds
    /// sustain, because nothing will ever stop a note.
    /// </remarks>
    public bool IgnoreNoteOffOnPercussion { get; set; }

    /// <summary>
    /// What <see cref="MultiTrackPlayer.AutoSetRelativeTrackLevels"/> is set to on the player.
    /// Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// With it on, each stem's synthesized rendition is matched to the loudness of that stem's own
    /// recording, so the balance between the parts follows the original mix whichever source each
    /// track is playing. It costs a full decode and a full synthesis pass per stem, on a worker.
    /// </remarks>
    public bool AutoSetRelativeTrackLevels { get; set; }

    /// <summary>Returns an independent copy of these options.</summary>
    /// <returns>A new instance carrying the same values.</returns>
    /// <remarks>
    /// The per-stem instruments and the stem selection are copied too, so changing either of them
    /// afterwards does not reach a player that was already built. The loaded instruments THEMSELVES
    /// are shared, as they are everywhere else - they are the expensive part and they are safe to
    /// share.
    /// </remarks>
    public SunoPlayerOptions Clone()
    {
        var copy = new SunoPlayerOptions
        {
            InstrumentFactory = InstrumentFactory,
            InstrumentLibraryName = InstrumentLibraryName,
            InstrumentLibrary = InstrumentLibrary,
            MidiStems = MidiStems == null ? null : MidiStems.Clone(),
            GeneralMidiSoundFontPath = GeneralMidiSoundFontPath,
            SoundFontCache = SoundFontCache,
            IncludeMidiSources = IncludeMidiSources,
            ApplyAlignmentOffsets = ApplyAlignmentOffsets,
            IgnoreNoteOffOnPercussion = IgnoreNoteOffOnPercussion,
            AutoSetRelativeTrackLevels = AutoSetRelativeTrackLevels,
        };

        foreach (var stemName in StemInstruments.StemNames)
        {
            copy.StemInstruments.Set(stemName, StemInstruments[stemName]);
        }

        return copy;
    }
}
