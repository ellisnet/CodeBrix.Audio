using System;
using System.Collections.Generic;
using System.IO;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Playback.Suno;

/// <summary>
/// Turning a loaded song into something that plays, and into one General MIDI file.
/// </summary>
public sealed partial class SunoSong
{
    /// <summary>
    /// Builds a <see cref="MultiTrackPlayer"/> for this song: one track per stem, playing the
    /// recordings.
    /// </summary>
    /// <returns>A player, not yet prepared. The caller owns it and disposes it.</returns>
    public MultiTrackPlayer CreatePlayer() => MultiTrackPlayer.Load(this, null);

    /// <summary>
    /// Builds a <see cref="MultiTrackPlayer"/> for this song, with an instrument for its MIDI stems.
    /// </summary>
    /// <param name="instruments">
    /// Builds the instrument for one stem, given the stem and the sample rate the synthesizer must
    /// render at. It may be called more than once and from a worker thread, so it must never hand
    /// out the same synthesizer twice; share the SoundFont or the SFZ instrument behind them.
    /// </param>
    /// <returns>A player, not yet prepared. The caller owns it and disposes it.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="instruments"/> is null.</exception>
    public MultiTrackPlayer CreatePlayer(Func<SunoStem, int, IMidiSynthesizer> instruments)
    {
        if (instruments == null)
        {
            throw new ArgumentNullException(nameof(instruments));
        }

        return MultiTrackPlayer.Load(this, new SunoPlayerOptions { InstrumentFactory = instruments });
    }

    /// <summary>
    /// Builds a <see cref="MultiTrackPlayer"/> for this song.
    /// </summary>
    /// <param name="options">
    /// What to do beyond putting one track on each stem; null for the defaults.
    /// </param>
    /// <returns>A player, not yet prepared. The caller owns it and disposes it.</returns>
    public MultiTrackPlayer CreatePlayer(SunoPlayerOptions options) =>
        MultiTrackPlayer.Load(this, options);

    /// <summary>
    /// Writes every MIDI stem of this song into one General MIDI file: each stem on its own channel
    /// and its own track, percussion on channel 10, the shared tempo map, and each stem's measured
    /// alignment already applied.
    /// </summary>
    /// <param name="path">The file to write.</param>
    /// <returns>
    /// One human-readable line per thing that could not be honoured; empty when everything was.
    /// Never thrown.
    /// </returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="IOException">The file could not be written.</exception>
    /// <remarks>
    /// This is the way to take a stems export somewhere else: nothing outside this package opens
    /// twelve MIDI files as one song, and what comes out here loads in any sequencer and plays the
    /// arrangement the player plays. It needs no instrument and no audio - only the MIDI the export
    /// carried - so it works on a song loaded with nothing configured.
    /// </remarks>
    public IReadOnlyList<string> ExportMergedMidi(string path)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        using var player = CreateMergedMidiPlayer();
        return player.ExportMergedMidi(path);
    }

    /// <summary>
    /// Writes every MIDI stem of this song into one General MIDI file on a stream; see
    /// <see cref="ExportMergedMidi(string)"/>.
    /// </summary>
    /// <param name="output">The stream to write to.</param>
    /// <param name="leaveOpen">
    /// When <see langword="true"/>, the stream is left open for the caller to dispose.
    /// </param>
    /// <returns>One human-readable line per thing that could not be honoured.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="output"/> is null.</exception>
    public IReadOnlyList<string> ExportMergedMidi(Stream output, bool leaveOpen = false)
    {
        if (output == null)
        {
            throw new ArgumentNullException(nameof(output));
        }

        using var player = CreateMergedMidiPlayer();
        return player.ExportMergedMidi(output, leaveOpen);
    }

    // A player holding nothing but the MIDI stems, for the merged export. It is never prepared and
    // never rendered, so the instrument it names is never asked for - which is why the export needs
    // no SoundFont.
    private MultiTrackPlayer CreateMergedMidiPlayer()
    {
        var player = new MultiTrackPlayer();
        try
        {
            foreach (var stem in Stems)
            {
                if (stem.Midi == null)
                {
                    continue;
                }

                var track = new MidiTrack(stem.Midi, NotForRendering, stem.Name)
                {
                    IsPercussion = stem.IsPercussion,
                    MidiSourceOffset = stem.AlignmentOffset,
                    MinimumNoteHold = Options == null ? PlayerTrack.DefaultMinimumNoteHold : Options.MinimumNoteHold,
                };

                if (stem.GmProgram >= 0 && stem.GmProgram <= 127)
                {
                    track.GmProgram = stem.GmProgram;
                }

                player.Add(track);
            }
        }
        catch
        {
            player.Dispose();
            throw;
        }

        return player;
    }

    private static IMidiSynthesizer NotForRendering(int sampleRate) =>
        throw new InvalidOperationException(
            "This player exists only to export merged MIDI and has no instruments; " +
            "use SunoSong.CreatePlayer to build one that plays.");
}
