namespace CodeBrix.Audio.Playback;

/// <summary>
/// Where <see cref="PacketAudioPlayer"/> gets its compressed audio: a queue that something else - a
/// demultiplexer reading a media container, a network reader - keeps filled.
/// </summary>
/// <remarks>
/// <para>
/// THE ONE RULE: both members are called ON THE AUDIO THREAD and must return immediately. Never
/// block, never wait on a lock that I/O is holding, never allocate if it can be avoided. A source
/// that reads ahead on its own thread into a bounded queue and hands packets out of that queue is
/// the shape this is designed for.
/// </para>
/// <para>
/// Running dry is expected and is not an error: return false from <see cref="TryReadPacket"/> with
/// <see cref="EndOfStream"/> still false, and the player fills the gap with silence and keeps the
/// voice alive, ready for the packets that follow. Only <see cref="EndOfStream"/> ends playback.
/// </para>
/// </remarks>
public interface IAudioPacketSource
{
    /// <summary>
    /// Takes the next packet if one is ready.
    /// </summary>
    /// <param name="packet">The next packet, when this returns true; otherwise the default value.</param>
    /// <returns>
    /// True when a packet was handed over; false when none is ready right now - which means an
    /// underrun if <see cref="EndOfStream"/> is false, and the end of the audio if it is true.
    /// </returns>
    /// <remarks>
    /// The memory behind <see cref="AudioPacket.Data"/> must stay valid and unchanged until the next
    /// call to this method: the player decodes the packet before asking for another one, and never
    /// holds on to it afterwards. That is enough for a source that hands out slices of a rolling
    /// buffer, which is why the contract is stated this way rather than requiring a copy.
    /// </remarks>
    bool TryReadPacket(out AudioPacket packet);

    /// <summary>
    /// Whether the source has delivered every packet it will ever deliver.
    /// </summary>
    /// <remarks>
    /// Read only when <see cref="TryReadPacket"/> comes back empty, so the player can tell an
    /// underrun (keep going, play silence) from the end of the audio (raise
    /// <see cref="PacketAudioPlayer.PlaybackEnded"/> once the last decoded samples have been heard).
    /// A source that is repositioned - see <see cref="PacketAudioPlayer.Seek"/> - returns false
    /// again afterwards, because it has more to deliver from the new position.
    /// </remarks>
    bool EndOfStream { get; }
}
