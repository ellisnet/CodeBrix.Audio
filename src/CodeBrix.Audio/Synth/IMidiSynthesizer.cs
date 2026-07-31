using System;

namespace CodeBrix.Audio.Synth;

/// <summary>
/// A MIDI-driven synthesizer that renders stereo audio in fixed-size blocks: the contract
/// <see cref="MidiSequencer"/>, <c>MidiMusicPlayer</c> and <c>SoundFontRenderer</c> drive, implemented
/// by both <see cref="SoundFontSynthesizer"/> and <see cref="Sfz.SfzSynthesizer"/>.
/// </summary>
/// <remarks>
/// Implementations are not thread-safe by design: rendering and MIDI events must not overlap. The
/// playback layer serializes the two; anything driving a synthesizer directly must do the same.
/// </remarks>
public interface IMidiSynthesizer : IAudioRenderer
{
    /// <summary>The sample rate for synthesis, in Hz.</summary>
    int SampleRate { get; }

    /// <summary>The number of frames rendered per internal block.</summary>
    int BlockSize { get; }

    /// <summary>The number of voices currently sounding.</summary>
    int ActiveVoiceCount { get; }

    /// <summary>The master output gain, where 0.5 is the conventional default.</summary>
    float MasterVolume { get; set; }

    /// <summary>Processes one MIDI channel message.</summary>
    /// <param name="channel">The channel the message is addressed to.</param>
    /// <param name="command">The status command (0x80 note off, 0x90 note on, 0xB0 controller, ...).</param>
    /// <param name="data1">The first data byte.</param>
    /// <param name="data2">The second data byte.</param>
    void ProcessMidiMessage(int channel, int command, int data1, int data2);

    /// <summary>Stops every note.</summary>
    /// <param name="immediate">If <see langword="true"/>, voices stop at once instead of releasing.</param>
    void NoteOffAll(bool immediate);

    /// <summary>Returns the synthesizer to its initial state: no voices, controllers at defaults.</summary>
    void Reset();
}
