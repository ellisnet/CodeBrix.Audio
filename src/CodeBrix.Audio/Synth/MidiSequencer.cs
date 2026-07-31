using System;

// ReSharper disable once CheckNamespace
namespace CodeBrix.Audio.Synth; //was previously: MeltySynth

/// <summary>
/// An instance of the MIDI file sequencer.
/// </summary>
/// <remarks>
/// Note that this class does not provide thread safety.
/// If you want to control playback and render the waveform in separate threads,
/// you must make sure that the methods are not called at the same time.
/// </remarks>
public sealed class MidiSequencer : IAudioRenderer
{
    private readonly SoundFontSynthesizer synthesizer;

    private float speed;

    private MidiSequence midiSequence;
    private bool loop;

    private int blockWrote;

    private TimeSpan currentTime;
    private int msgIndex;
    private int loopIndex;

    private MessageHook onSendMessage;

    /// <summary>
    /// Initializes a new instance of the sequencer.
    /// </summary>
    /// <param name="synthesizer">The synthesizer to be used by the sequencer.</param>
    public MidiSequencer(SoundFontSynthesizer synthesizer)
    {
        if (synthesizer == null)
        {
            throw new ArgumentNullException(nameof(synthesizer));
        }

        this.synthesizer = synthesizer;

        speed = 1F;
    }

    /// <summary>
    /// Plays the MIDI file.
    /// </summary>
    /// <param name="midiSequence">The MIDI file to be played.</param>
    /// <param name="loop">If <c>true</c>, the MIDI file loops after reaching the end.</param>
    public void Play(MidiSequence midiSequence, bool loop)
    {
        if (midiSequence == null)
        {
            throw new ArgumentNullException(nameof(midiSequence));
        }

        this.midiSequence = midiSequence;
        this.loop = loop;

        blockWrote = synthesizer.BlockSize;

        currentTime = TimeSpan.Zero;
        msgIndex = 0;
        loopIndex = 0;

        synthesizer.Reset();
    }

    /// <summary>
    /// Stops playing.
    /// </summary>
    public void Stop()
    {
        midiSequence = null;

        synthesizer.Reset();
    }

    /// <summary>
    /// Moves playback to the given position in the sequence.
    /// </summary>
    /// <param name="position">
    /// The position to seek to, from the start of the sequence. Negative values are clamped to zero.
    /// </param>
    /// <remarks>
    /// <para>
    /// The controller state leading up to <paramref name="position"/> is replayed into the synthesizer -
    /// program and bank changes, volume, pan, expression, pitch bend - so the instrument sounds the way
    /// it would have if the sequence had been played from the start. Note-on messages are deliberately
    /// NOT replayed: re-triggering every note that ever sounded would be both wrong and deafening.
    /// </para>
    /// <para>
    /// A note that was sounding across <paramref name="position"/> therefore does not resume; it starts
    /// again at its next note-on. This matches what other MIDI players do when you drag a scrubber.
    /// </para>
    /// <para>Does nothing when no sequence is playing.</para>
    /// </remarks>
    //was previously: not present in MeltySynth; MidiFileSequencer had no seek.
    public void Seek(TimeSpan position)
    {
        if (midiSequence == null)
        {
            return;
        }

        if (position < TimeSpan.Zero)
        {
            position = TimeSpan.Zero;
        }

        synthesizer.Reset();

        blockWrote = synthesizer.BlockSize;
        currentTime = position;
        msgIndex = 0;
        loopIndex = 0;

        while (msgIndex < midiSequence.Messages.Length && midiSequence.Times[msgIndex] <= position)
        {
            var msg = midiSequence.Messages[msgIndex];

            if (msg.Type == MidiSequence.MessageType.Normal)
            {
                // 0x90 is Note On and 0x80 is Note Off; everything else is controller state
                // that has to be reapplied for the sequence to sound correct from here.
                if (msg.Command != 0x90 && msg.Command != 0x80)
                {
                    synthesizer.ProcessMidiMessage(msg.Channel, msg.Command, msg.Data1, msg.Data2);
                }
            }
            else if (msg.Type == MidiSequence.MessageType.LoopStart)
            {
                loopIndex = msgIndex;
            }

            msgIndex++;
        }
    }

    /// <inheritdoc/>
    public void Render(Span<float> left, Span<float> right)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException("The output buffers for the left and right must be the same length.");
        }

        var wrote = 0;
        while (wrote < left.Length)
        {
            if (blockWrote == synthesizer.BlockSize)
            {
                ProcessEvents();
                blockWrote = 0;
                currentTime += MidiSequence.GetTimeSpanFromSeconds((double)speed * synthesizer.BlockSize / synthesizer.SampleRate);
            }

            var srcRem = synthesizer.BlockSize - blockWrote;
            var dstRem = left.Length - wrote;
            var rem = Math.Min(srcRem, dstRem);

            synthesizer.Render(left.Slice(wrote, rem), right.Slice(wrote, rem));

            blockWrote += rem;
            wrote += rem;
        }
    }

    private void ProcessEvents()
    {
        if (midiSequence == null)
        {
            return;
        }

        while (msgIndex < midiSequence.Messages.Length)
        {
            var time = midiSequence.Times[msgIndex];
            var msg = midiSequence.Messages[msgIndex];
            if (time <= currentTime)
            {
                if (msg.Type == MidiSequence.MessageType.Normal)
                {
                    if (onSendMessage == null)
                    {
                        synthesizer.ProcessMidiMessage(msg.Channel, msg.Command, msg.Data1, msg.Data2);
                    }
                    else
                    {
                        onSendMessage(synthesizer, msg.Channel, msg.Command, msg.Data1, msg.Data2);
                    }
                }
                else if (loop)
                {
                    if (msg.Type == MidiSequence.MessageType.LoopStart)
                    {
                        loopIndex = msgIndex;
                    }
                    else if (msg.Type == MidiSequence.MessageType.LoopEnd)
                    {
                        currentTime = midiSequence.Times[loopIndex];
                        msgIndex = loopIndex;
                        synthesizer.NoteOffAll(false);
                    }
                }
                msgIndex++;
            }
            else
            {
                break;
            }
        }

        if (msgIndex == midiSequence.Messages.Length && loop)
        {
            currentTime = midiSequence.Times[loopIndex];
            msgIndex = loopIndex;
            synthesizer.NoteOffAll(false);
        }
    }

    /// <summary>
    /// Gets the synthesizer used by the sequencer.
    /// </summary>
    public SoundFontSynthesizer SoundFontSynthesizer => synthesizer;

    /// <summary>
    /// Gets the currently playing MIDI file.
    /// </summary>
    public MidiSequence MidiSequence => midiSequence;

    /// <summary>
    /// Gets the current playback position.
    /// </summary>
    public TimeSpan Position => currentTime;

    /// <summary>
    /// Gets a value that indicates whether the current playback position is at the end of the sequence.
    /// </summary>
    /// <remarks>
    /// If the <see cref="Play(MidiSequence, bool)">Play</see> method has not yet been called, this value is true.
    /// This value will never be <c>true</c> when loop playback is enabled.
    /// </remarks>
    public bool EndOfSequence
    {
        get
        {
            if (midiSequence == null)
            {
                return true;
            }
            else
            {
                return msgIndex == midiSequence.Messages.Length;
            }
        }
    }

    /// <summary>
    /// Gets or sets the playback speed.
    /// </summary>
    /// <remarks>
    /// The default value is 1.
    /// The tempo will be multiplied by this value.
    /// </remarks>
    public float Speed
    {
        get => speed;

        set
        {
            if (value >= 0)
            {
                speed = value;
            }
            else
            {
                throw new ArgumentOutOfRangeException("The playback speed must be a non-negative value.");
            }
        }
    }

    /// <summary>
    /// Gets or sets the method for modifying MIDI messages during playback.
    /// If <c>null</c>, MIDI messages are sent to the synthesizer without any changes.
    /// </summary>
    public MessageHook OnSendMessage
    {
        get => onSendMessage;
        set => onSendMessage = value;
    }



    /// <summary>
    /// Represents the method that is called each time a MIDI message is processed during playback.
    /// </summary>
    /// <param name="synthesizer">The synthesizer used by the sequencer.</param>
    /// <param name="channel">The channel to which the message will be sent.</param>
    /// <param name="command">The type of the message.</param>
    /// <param name="data1">The first data part of the message.</param>
    /// <param name="data2">The second data part of the message.</param>
    public delegate void MessageHook(SoundFontSynthesizer synthesizer, int channel, int command, int data1, int data2);
}
