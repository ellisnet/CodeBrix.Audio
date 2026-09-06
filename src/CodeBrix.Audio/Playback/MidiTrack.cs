using System;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Playback;

/// <summary>
/// A <see cref="MultiTrackPlayer"/> track that starts life as a MIDI performance: a
/// <see cref="MidiSequence"/> played through a synthesizer the consumer chooses - a SoundFont, an
/// SFZ instrument, or anything else implementing <see cref="IMidiSynthesizer"/>.
/// </summary>
/// <remarks>
/// <para>
/// This is one of the two ways to make a track; the other is <see cref="AudioTrack"/>. The
/// difference is only what the track starts with - give this one
/// <see cref="PlayerTrack.SetAudioSource(string)"/> as well and it holds both, with
/// <see cref="PlayerTrack.ActiveSource"/> choosing which is heard.
/// </para>
/// <para>
/// The synthesizer is built at the sample rate the mix runs at, so nothing is resampled. The
/// factory may be called more than once (the live mix, an offline render and a loudness measurement
/// each want their own instance), so share the SoundFont or SFZ instrument between them - that is
/// the expensive part, and both are designed to be shared.
/// </para>
/// <para>
/// <see cref="PlayerTrack.MinimumNoteHold"/> defaults to 60 ms here, which is what makes a drum
/// part written with zero-length notes audible. See that property for why.
/// </para>
/// </remarks>
public sealed class MidiTrack : PlayerTrack
{
    /// <summary>Creates a track from a MIDI sequence and the synthesizer that plays it.</summary>
    /// <param name="sequence">The performance to play.</param>
    /// <param name="synthesizerFactory">
    /// Builds the synthesizer, called with the sample rate it must render at. It may be called more
    /// than once.
    /// </param>
    /// <param name="name">A display name for the track.</param>
    /// <exception cref="ArgumentNullException"><paramref name="sequence"/> or <paramref name="synthesizerFactory"/> is null.</exception>
    public MidiTrack(MidiSequence sequence, Func<int, IMidiSynthesizer> synthesizerFactory, string name = null)
        : base(name)
    {
        SetMidiSource(sequence, synthesizerFactory);
        SetInitialActiveSource(TrackSource.Midi);
    }
}
