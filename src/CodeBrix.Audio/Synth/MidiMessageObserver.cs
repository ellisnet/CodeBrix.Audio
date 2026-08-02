namespace CodeBrix.Audio.Synth;

/// <summary>
/// Observes a MIDI message after it has been delivered to the synthesizer during playback.
/// </summary>
/// <param name="channel">The channel the message was sent to, 0-15.</param>
/// <param name="command">The message's command nibble - 0x90 note-on, 0x80 note-off, 0xB0 control change, and so on.</param>
/// <param name="data1">The first data byte: the note number for a note message, the controller number for a control change.</param>
/// <param name="data2">The second data byte: the velocity for a note message, the controller value for a control change.</param>
/// <remarks>
/// <para>
/// This is the OBSERVE-ONLY hook, and it is the one to reach for when something outside the audio
/// should react to the music - a drum hit driving a screen shake, a note spawning a particle, a
/// karaoke or rhythm-game display. It cannot change what is played; the message has already been
/// delivered by the time it runs. To CHANGE messages, use
/// <see cref="MidiSequencer.MessageHook"/> instead, and note the difference carefully: that one
/// REPLACES delivery.
/// </para>
/// <para>
/// It is invoked on the real-time AUDIO THREAD, while the synthesizer lock is held. So it must be
/// fast, allocation-free, and must never block, touch UI, or call back into the player that raised
/// it (which would deadlock). Hand the information to your own thread through a lock-free field or
/// a bounded queue and act on it there.
/// </para>
/// </remarks>
public delegate void MidiMessageObserver(int channel, int command, int data1, int data2);
