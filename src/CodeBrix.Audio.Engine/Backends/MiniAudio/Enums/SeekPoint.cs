namespace CodeBrix.Audio.Engine.Backends.MiniAudio.Enums;  //was previously: SoundFlow.Backends.MiniAudio.Enums

internal enum SeekPoint
{
    /// <summary>
    ///     Seek from the beginning of the stream.
    /// </summary>
    FromStart,

    /// <summary>
    ///     Seek from the current position in the stream.
    /// </summary>
    FromCurrent
}