using System;
using CodeBrix.Audio.Synth;

namespace CodeBrix.Audio.Playback.Internal;

/// <summary>
/// Renders a track's MIDI performance through its own synthesizer, applying the track's percussion
/// rule as the messages go past.
/// </summary>
/// <remarks>
/// <para>
/// The percussion rule exists because drum and percussion parts are routinely written with
/// zero-length notes: the note-off arrives a tick or two after the note-on, because a hit has no
/// duration. Played literally, the sample is cut off in its attack and every drum becomes a click.
/// Two ways out, both per track:
/// <see cref="PlayerTrack.IgnoreNoteOff"/> discards note-offs entirely, and
/// <see cref="PlayerTrack.MinimumNoteHold"/> DEFERS one that arrives too soon until the note has
/// sounded for long enough.
/// </para>
/// <para>
/// Deferral is why this renders in small slices rather than handing the whole block to the
/// sequencer: a deferred note-off has to be released at a definite moment, and the slice boundary
/// is that moment. The slice is the synthesizer's own block size, so the timing of a release is
/// accurate to a block - well under two milliseconds at any realistic rate, and the same
/// granularity everything else in the synthesizer works at.
/// </para>
/// </remarks>
internal sealed class MidiSourceRenderer : SourceRenderer
{
    // A cap on how many notes can be waiting for a deferred release at once. Comfortably beyond any
    // synthesizer's polyphony; a fixed array keeps the render callback allocation-free.
    private const int MaximumPendingReleases = 256;

    private readonly IMidiSynthesizer synthesizer;
    private readonly MidiSequencer sequencer;
    private readonly int sliceFrames;
    private readonly int holdFrames;
    private readonly bool ignoreNoteOff;
    private readonly long lengthFrames;

    private readonly float[] left;
    private readonly float[] right;

    private readonly int[] pendingChannel = new int[MaximumPendingReleases];
    private readonly int[] pendingNote = new int[MaximumPendingReleases];
    private readonly long[] pendingDueFrame = new long[MaximumPendingReleases];
    private readonly bool[] pendingDeferred = new bool[MaximumPendingReleases];
    private int pendingCount;

    private long position;

    /// <summary>Builds a renderer for one track's MIDI source.</summary>
    /// <param name="track">The track whose sequence, synthesizer factory and percussion rule to use.</param>
    /// <param name="sampleRate">The rate the synthesizer must render at.</param>
    internal MidiSourceRenderer(PlayerTrack track, int sampleRate)
    {
        var trackSequence = track.MidiSequence;
        synthesizer = track.SynthesizerFactory(sampleRate);

        if (synthesizer == null)
        {
            throw new InvalidOperationException(
                $"The synthesizer factory for track '{track.Name}' returned null.");
        }

        ignoreNoteOff = track.IgnoreNoteOff;
        holdFrames = ignoreNoteOff
            ? 0
            : (int)(track.MinimumNoteHold.TotalSeconds * sampleRate);

        sliceFrames = synthesizer.BlockSize > 0 ? synthesizer.BlockSize : 64;
        left = new float[sliceFrames];
        right = new float[sliceFrames];

        lengthFrames = (long)Math.Ceiling(trackSequence.Length.TotalSeconds * sampleRate);

        sequencer = new MidiSequencer(synthesizer);
        if (ignoreNoteOff || holdFrames > 0)
        {
            sequencer.OnSendMessage = OnSequencerMessage;
        }

        sequencer.Play(trackSequence, loop: false);
    }

    /// <inheritdoc/>
    internal override long LengthFrames => lengthFrames;

    /// <summary>The synthesizer this renderer drives. Not thread-safe; the mixer serializes it.</summary>
    internal IMidiSynthesizer Synthesizer => synthesizer;

    /// <inheritdoc/>
    internal override void Seek(long frame)
    {
        if (frame < 0)
        {
            frame = 0;
        }

        pendingCount = 0;
        position = frame;
        sequencer.Seek(TimeSpan.FromSeconds((double)frame / synthesizer.SampleRate));
    }

    /// <inheritdoc/>
    internal override void Render(Span<float> buffer)
    {
        var frames = buffer.Length / 2;
        var written = 0;

        while (written < frames)
        {
            var take = Math.Min(sliceFrames, frames - written);

            ReleaseDueNotes();

            var leftSlice = left.AsSpan(0, take);
            var rightSlice = right.AsSpan(0, take);
            sequencer.Render(leftSlice, rightSlice);

            for (var i = 0; i < take; i++)
            {
                buffer[(written + i) * 2] = leftSlice[i];
                buffer[(written + i) * 2 + 1] = rightSlice[i];
            }

            written += take;
            position += take;
        }
    }

    /// <inheritdoc/>
    public override void Dispose()
    {
        sequencer.OnSendMessage = null;
        sequencer.Stop();
        pendingCount = 0;

        if (synthesizer is IDisposable disposable)
        {
            disposable.Dispose();
        }
    }

    // Sends the note-offs whose hold time has elapsed, and forgets the notes that outlived theirs.
    // Runs at slice boundaries, before the sequencer is asked for more audio, so a deferred release
    // lands on the block it is due on.
    //
    // Only a DEFERRED entry produces a note-off. An entry that is merely being timed - the note-off
    // has not arrived yet - must not, or every held note would be cut off at the hold length, which
    // is exactly the opposite of what the rule is for.
    private void ReleaseDueNotes()
    {
        var index = 0;
        while (index < pendingCount)
        {
            if (pendingDueFrame[index] > position)
            {
                index++;
                continue;
            }

            if (pendingDeferred[index])
            {
                synthesizer.ProcessMidiMessage(pendingChannel[index], 0x80, pendingNote[index], 0);
            }

            RemovePendingAt(index);
        }
    }

    // The sequencer's hook REPLACES delivery: when it is set, the sequencer does not call the
    // synthesizer itself, so everything that is not deferred has to be passed on here.
    private void OnSequencerMessage(IMidiSynthesizer target, int channel, int command, int data1, int data2)
    {
        var isNoteOff = command == 0x80 || (command == 0x90 && data2 == 0);

        if (isNoteOff)
        {
            if (ignoreNoteOff)
            {
                return;
            }

            if (holdFrames > 0 && TryDeferRelease(channel, data1))
            {
                return;
            }

            RemovePending(channel, data1);
            target.ProcessMidiMessage(channel, command, data1, data2);
            return;
        }

        target.ProcessMidiMessage(channel, command, data1, data2);

        if (command == 0x90 && data2 > 0 && holdFrames > 0 && !ignoreNoteOff)
        {
            SchedulePending(channel, data1, position + holdFrames);
        }
    }

    // True when the note's release was pushed out to its due frame instead of being sent now.
    private bool TryDeferRelease(int channel, int note)
    {
        for (var i = 0; i < pendingCount; i++)
        {
            if (pendingChannel[i] == channel && pendingNote[i] == note)
            {
                if (pendingDueFrame[i] <= position)
                {
                    // The hold has already elapsed; let the note-off through as written.
                    RemovePendingAt(i);
                    return false;
                }

                pendingDeferred[i] = true;
                return true;
            }
        }

        return false;
    }

    private void SchedulePending(int channel, int note, long dueFrame)
    {
        for (var i = 0; i < pendingCount; i++)
        {
            if (pendingChannel[i] == channel && pendingNote[i] == note)
            {
                // The same note re-struck before its hold elapsed: the new hit owns the release.
                pendingDueFrame[i] = dueFrame;
                pendingDeferred[i] = false;
                return;
            }
        }

        if (pendingCount >= MaximumPendingReleases)
        {
            // More notes held at once than the rule can track. Dropping the newest is the safe
            // failure: its note-off is then delivered as written rather than being lost.
            return;
        }

        pendingChannel[pendingCount] = channel;
        pendingNote[pendingCount] = note;
        pendingDueFrame[pendingCount] = dueFrame;
        pendingDeferred[pendingCount] = false;
        pendingCount++;
    }

    private void RemovePending(int channel, int note)
    {
        for (var i = 0; i < pendingCount; i++)
        {
            if (pendingChannel[i] == channel && pendingNote[i] == note)
            {
                RemovePendingAt(i);
                return;
            }
        }
    }

    private void RemovePendingAt(int index)
    {
        pendingCount--;
        pendingChannel[index] = pendingChannel[pendingCount];
        pendingNote[index] = pendingNote[pendingCount];
        pendingDueFrame[index] = pendingDueFrame[pendingCount];
        pendingDeferred[index] = pendingDeferred[pendingCount];
    }
}
