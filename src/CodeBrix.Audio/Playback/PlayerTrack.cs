using System;
using System.IO;
using CodeBrix.Audio.Playback.Internal;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Playback;

/// <summary>
/// One track of a <see cref="MultiTrackPlayer"/>: a part of a song, with its own level, position in
/// the stereo field, and timing offset, that can be heard either as a recording or as a MIDI
/// performance.
/// </summary>
/// <remarks>
/// <para>
/// Create tracks as <see cref="AudioTrack"/> or <see cref="MidiTrack"/> depending on what you have.
/// Either kind can then be given the OTHER source as well - a stem that arrived as a WAV and a MIDI
/// file becomes one track holding both - and <see cref="ActiveSource"/> chooses which one is heard.
/// Both are rendered in step while both are present, so a switch mid-song is a 20 ms crossfade and
/// nothing restarts.
/// </para>
/// <para>
/// Every property here is safe to change while the player is running, with one exception:
/// <see cref="Offset"/> is read when the transport is prepared, played, stopped or seeked, so a
/// change to it takes effect at the next of those rather than immediately. Everything else - gain,
/// mute, solo, pan, the active source - takes effect on the next render block.
/// </para>
/// </remarks>
public abstract class PlayerTrack
{
    /// <summary>
    /// The default shortest time a MIDI note sounds for, regardless of when its note-off arrives.
    /// </summary>
    public static readonly TimeSpan DefaultMinimumNoteHold = TimeSpan.FromMilliseconds(60);

    private string name;
    private float gain = 1.0f;
    private float midiSourceGain = 1.0f;
    private float pan;
    private TimeSpan minimumNoteHold = DefaultMinimumNoteHold;
    private TrackSource activeSource;
    private AudioSourceDefinition audioSource;
    private MidiSequence midiSequence;
    private Func<int, IMidiSynthesizer> synthesizerFactory;
    private int gmProgram = -1;

    /// <summary>Creates a track with no sources yet.</summary>
    /// <param name="name">A display name for the track, or null for an empty one.</param>
    private protected PlayerTrack(string name)
    {
        this.name = name ?? string.Empty;
    }

    /// <summary>
    /// A display name for the track - the stem name, the instrument, whatever the consumer's UI
    /// shows. Never null; setting null stores an empty string.
    /// </summary>
    public string Name
    {
        get => name;
        set => name = value ?? string.Empty;
    }

    /// <summary>
    /// The track's level as a linear multiplier, where 1.0 is unity gain and 0.5 is half amplitude
    /// (about 6 dB down). Defaults to 1.0. Negative values are clamped to zero.
    /// </summary>
    /// <remarks>
    /// <see cref="MultiTrackPlayer.AutoSetRelativeTrackLevels"/> never touches this - it writes
    /// <see cref="MidiSourceGain"/> instead, so a level the consumer set stays set.
    /// </remarks>
    public float Gain
    {
        get => gain;
        set => gain = value > 0.0f ? value : 0.0f;
    }

    /// <summary>
    /// An extra linear gain applied only while the MIDI source is the one being heard, so a
    /// synthesized rendition can be matched to the recording without disturbing
    /// <see cref="Gain"/>. Defaults to 1.0. Negative values are clamped to zero.
    /// </summary>
    /// <remarks>
    /// This is what <see cref="MultiTrackPlayer.AutoSetRelativeTrackLevels"/> writes. With that
    /// option off, nothing but the consumer ever writes it.
    /// </remarks>
    public float MidiSourceGain
    {
        get => midiSourceGain;
        set => midiSourceGain = value > 0.0f ? value : 0.0f;
    }

    /// <summary>Whether the track is silenced. Mute wins over <see cref="Solo"/>.</summary>
    public bool Mute { get; set; }

    /// <summary>
    /// Whether the track is soloed. While ANY track in the player is soloed, only soloed tracks are
    /// heard; when none is, the flag does nothing.
    /// </summary>
    public bool Solo { get; set; }

    /// <summary>
    /// The track's position in the stereo field: -1.0 hard left, 0.0 centred (the default),
    /// 1.0 hard right. Values outside that range are clamped.
    /// </summary>
    /// <remarks>
    /// This is a BALANCE control, the right one for stereo material: it attenuates the opposite
    /// channel and leaves the near one alone, so a centred track passes through untouched rather
    /// than being scaled by a constant-power law.
    /// </remarks>
    public float Pan
    {
        get => pan;
        set => pan = value < -1.0f ? -1.0f : value > 1.0f ? 1.0f : value;
    }

    /// <summary>
    /// How far the track is shifted in time relative to the rest of the song. Positive DELAYS the
    /// track (its first sample is heard later); negative pulls it earlier, and whatever falls
    /// before the start of the song is simply not heard. Defaults to zero.
    /// </summary>
    /// <remarks>
    /// Read when the transport is prepared, played, stopped or seeked - a change made while the
    /// music is running takes effect at the next of those, because applying it mid-block would
    /// mean a discontinuity anyway.
    /// </remarks>
    public TimeSpan Offset { get; set; }

    /// <summary>
    /// How far the track's MIDI source is shifted in time relative to the track's own recording.
    /// Positive DELAYS the MIDI rendition; negative pulls it earlier. Defaults to zero, and it does
    /// nothing on a track that holds no MIDI source.
    /// </summary>
    /// <remarks>
    /// <para>
    /// This is the alignment lever, and it is separate from <see cref="Offset"/> because the two
    /// mean different things. A stems export's recordings are already in step with each other, so
    /// moving a whole track moves a stem out of the song; what needs moving is the TRANSCRIPTION,
    /// which a machine placed tens or hundreds of milliseconds from the notes it transcribed. Set
    /// this and the recording stays where the mix engineer put it while the synthesized rendition
    /// of the same part lines up with it.
    /// </para>
    /// <para>
    /// The sign matches an alignment measurement exactly: a measured
    /// <c>audioTime = midiTime + offset</c> goes straight in here. Read at the same moments
    /// <see cref="Offset"/> is - prepare, play, stop, seek - and applied on top of it, so the MIDI
    /// source ends up at <c>Offset + MidiSourceOffset</c> and the recording at <c>Offset</c>.
    /// </para>
    /// </remarks>
    public TimeSpan MidiSourceOffset { get; set; }

    /// <summary>Which source is heard. Changing it while playing crossfades over about 20 ms.</summary>
    /// <exception cref="InvalidOperationException">The track does not hold that source.</exception>
    public TrackSource ActiveSource
    {
        get => activeSource;
        set
        {
            if (value == TrackSource.Audio && audioSource == null)
            {
                throw new InvalidOperationException(
                    $"Track '{Name}' has no audio source, so it cannot be switched to TrackSource.Audio. " +
                    "Call SetAudioSource first.");
            }

            if (value == TrackSource.Midi && midiSequence == null)
            {
                throw new InvalidOperationException(
                    $"Track '{Name}' has no MIDI source, so it cannot be switched to TrackSource.Midi. " +
                    "Call SetMidiSource first.");
            }

            activeSource = value;
        }
    }

    /// <summary>Whether the track holds a decoded-audio source.</summary>
    public bool HasAudioSource => audioSource != null;

    /// <summary>Whether the track holds a MIDI source.</summary>
    public bool HasMidiSource => midiSequence != null;

    /// <summary>The MIDI performance this track can play, or null when it has none.</summary>
    public MidiSequence MidiSequence => midiSequence;

    /// <summary>
    /// The factory that builds the track's synthesizer, or null when the track has no MIDI source.
    /// It is called with the sample rate the synthesizer must render at.
    /// </summary>
    public Func<int, IMidiSynthesizer> SynthesizerFactory => synthesizerFactory;

    /// <summary>
    /// The shortest time a MIDI note sounds for, however soon its note-off arrives. Defaults to
    /// <see cref="DefaultMinimumNoteHold"/> (60 ms). Set it to <see cref="TimeSpan.Zero"/> to
    /// honour the written note lengths exactly.
    /// </summary>
    /// <remarks>
    /// Drum and percussion parts are routinely written with ZERO-LENGTH notes - the note-off lands
    /// a tick or two after the note-on, because a hit has no duration to express. Played back
    /// literally, that gives every drum a click where the sample is cut off in its attack. Holding
    /// each note for a moment lets the sample speak. Negative values are treated as zero.
    /// </remarks>
    public TimeSpan MinimumNoteHold
    {
        get => minimumNoteHold;
        set => minimumNoteHold = value > TimeSpan.Zero ? value : TimeSpan.Zero;
    }

    /// <summary>
    /// Whether the MIDI source's note-offs are discarded entirely, leaving every note to ring until
    /// its sample or envelope ends. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// The blunt alternative to <see cref="MinimumNoteHold"/>, and the right one for a one-shot
    /// percussion instrument whose samples already have the correct length. It suppresses
    /// <see cref="MinimumNoteHold"/> - a note with no note-off does not need a minimum. Beware of
    /// setting it on a SUSTAINING instrument: nothing will ever stop a note, and the synthesizer
    /// will run out of voices.
    /// </remarks>
    public bool IgnoreNoteOff { get; set; }

    /// <summary>
    /// The General MIDI program this track's part is written for, 0-127, or -1 (the default) when
    /// it is unknown or the sequence sets its own.
    /// </summary>
    /// <remarks>
    /// Used by <see cref="MultiTrackPlayer.ExportMergedMidi(string)"/>, which writes it as a program
    /// change at tick 0 on the track's channel. It does not affect playback: which instrument is
    /// heard is the synthesizer's business, and the sequence's own program changes still apply.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is outside -1 to 127.</exception>
    public int GmProgram
    {
        get => gmProgram;
        set
        {
            if (value < -1 || value > 127)
            {
                throw new ArgumentOutOfRangeException(nameof(value), value,
                    "A General MIDI program must be 0-127, or -1 for 'unknown'.");
            }

            gmProgram = value;
        }
    }

    /// <summary>
    /// Whether this track is a percussion part. Defaults to <see langword="false"/>.
    /// </summary>
    /// <remarks>
    /// Used by <see cref="MultiTrackPlayer.ExportMergedMidi(string)"/>, which puts percussion tracks
    /// on MIDI channel 10 (the General MIDI drum channel) and everything else on a channel of its
    /// own. It does not affect playback.
    /// </remarks>
    public bool IsPercussion { get; set; }

    /// <summary>The length of the track's audio source, or zero when it has none.</summary>
    public TimeSpan AudioDuration => audioSource == null ? TimeSpan.Zero : audioSource.Duration;

    /// <summary>
    /// The length of the track's MIDI source, <see cref="MidiSourceOffset"/> included, or zero when
    /// it has none.
    /// </summary>
    public TimeSpan MidiDuration
    {
        get
        {
            if (midiSequence == null)
            {
                return TimeSpan.Zero;
            }

            var end = midiSequence.Length + MidiSourceOffset;
            return end > TimeSpan.Zero ? end : TimeSpan.Zero;
        }
    }

    /// <summary>
    /// The length of the longer of the track's two sources, NOT counting <see cref="Offset"/>.
    /// </summary>
    /// <remarks>
    /// Deliberately the longer of the two rather than the active one, so that a track's contribution
    /// to <see cref="MultiTrackPlayer.Duration"/> does not change when the source is switched.
    /// </remarks>
    public TimeSpan Duration
    {
        get
        {
            var audio = AudioDuration;
            var midi = MidiDuration;
            return audio >= midi ? audio : midi;
        }
    }

    /// <summary>
    /// Gives the track a recording, replacing any it already had.
    /// </summary>
    /// <param name="filePath">
    /// Path to a WAV, MP3, Ogg Vorbis or FLAC file, or to a format registered with
    /// <see cref="Wave.AudioFileReaderRegistry.Register"/>. The format is identified from the file's
    /// CONTENT, not from its extension.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="filePath"/> is null.</exception>
    /// <exception cref="IOException">The file could not be opened.</exception>
    /// <exception cref="InvalidDataException">The content is not a format this package reads.</exception>
    /// <remarks>
    /// The file is opened here once to read its length and format, then closed again; it is
    /// reopened for each render. The file must therefore still be there when the music plays.
    /// </remarks>
    public void SetAudioSource(string filePath)
    {
        if (filePath == null)
        {
            throw new ArgumentNullException(nameof(filePath));
        }

        audioSource = AudioSourceDefinition.FromFile(filePath);
    }

    /// <summary>
    /// Gives the track a recording held in a stream, replacing any it already had.
    /// </summary>
    /// <param name="stream">
    /// A readable, seekable stream holding a WAV, MP3, Ogg Vorbis or FLAC file.
    /// </param>
    /// <param name="leaveOpen">
    /// When <see langword="true"/>, the caller keeps ownership of the stream and it is not disposed
    /// here. Either way the stream is READ IN FULL by this call - see the remarks.
    /// </param>
    /// <exception cref="ArgumentNullException"><paramref name="stream"/> is null.</exception>
    /// <exception cref="ArgumentException">The stream cannot seek.</exception>
    /// <exception cref="InvalidDataException">The content is not a format this package reads.</exception>
    /// <remarks>
    /// A stream can only be read from one position at a time, and a track is read from several at
    /// once - the live mix, an offline render, a loudness measurement. So the stream's ENCODED bytes
    /// are copied into memory here, once, and every render decodes from that copy. The cost is the
    /// file's compressed size, not its decoded size, but for a long uncompressed WAV those are the
    /// same thing: prefer <see cref="SetAudioSource(string)"/> for anything large.
    /// </remarks>
    public void SetAudioSource(Stream stream, bool leaveOpen = false)
    {
        if (stream == null)
        {
            throw new ArgumentNullException(nameof(stream));
        }

        audioSource = AudioSourceDefinition.FromStream(stream, leaveOpen);
    }

    /// <summary>
    /// Gives the track a MIDI performance and the synthesizer that renders it, replacing any it
    /// already had.
    /// </summary>
    /// <param name="sequence">The performance to play.</param>
    /// <param name="synthesizerFactory">
    /// Builds the synthesizer. It is called with the sample rate the synthesizer must render at -
    /// the output device's rate for playback, the requested rate for an offline render - and may be
    /// called more than once, so it must be able to build more than one instance. Share the
    /// underlying SoundFont or SFZ instrument between them; those are the expensive part.
    /// </param>
    /// <exception cref="ArgumentNullException">Either argument is null.</exception>
    public void SetMidiSource(MidiSequence sequence, Func<int, IMidiSynthesizer> synthesizerFactory)
    {
        if (sequence == null)
        {
            throw new ArgumentNullException(nameof(sequence));
        }

        if (synthesizerFactory == null)
        {
            throw new ArgumentNullException(nameof(synthesizerFactory));
        }

        midiSequence = sequence;
        this.synthesizerFactory = synthesizerFactory;
    }

    // Sets the initial active source from a constructor, bypassing the validation that would
    // reject it before the source has been attached.
    private protected void SetInitialActiveSource(TrackSource source) => activeSource = source;

    // The audio source definition, for the renderers. Null when the track has none.
    internal AudioSourceDefinition AudioSource => audioSource;
}
