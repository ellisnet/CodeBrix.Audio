using System;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Playback.Suno;

/// <summary>
/// What a <see cref="MultiTrackPlayer"/> built from a <see cref="SunoSong"/> should do beyond
/// putting one track on each stem: which instrument plays a MIDI stem, whether the measured
/// alignment is applied, and how percussion is handled.
/// </summary>
/// <remarks>
/// Every property has a default that suits an ordinary export, so <c>new SunoPlayerOptions()</c> is
/// a sensible thing to pass and passing nothing at all is equally sensible. The one thing worth
/// setting is an instrument: without either <see cref="InstrumentFactory"/> or a General MIDI
/// SoundFont there is nothing to play a MIDI stem THROUGH, so the player is built from the
/// recordings alone.
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
    public SunoPlayerOptions Clone() =>
        new SunoPlayerOptions
        {
            InstrumentFactory = InstrumentFactory,
            GeneralMidiSoundFontPath = GeneralMidiSoundFontPath,
            SoundFontCache = SoundFontCache,
            IncludeMidiSources = IncludeMidiSources,
            ApplyAlignmentOffsets = ApplyAlignmentOffsets,
            IgnoreNoteOffOnPercussion = IgnoreNoteOffOnPercussion,
            AutoSetRelativeTrackLevels = AutoSetRelativeTrackLevels,
        };
}
