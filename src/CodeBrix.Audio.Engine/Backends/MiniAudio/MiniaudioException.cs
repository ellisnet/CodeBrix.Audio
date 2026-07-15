using CodeBrix.Audio.Engine.Backends.MiniAudio.Enums;
using CodeBrix.Audio.Engine.Enums;
using CodeBrix.Audio.Engine.Exceptions;

namespace CodeBrix.Audio.Engine.Backends.MiniAudio;  //was previously: SoundFlow.Backends.MiniAudio

/// <summary>
///     An exception thrown when an error occurs in a audio backend.
/// </summary>
/// <param name="backendName">The name of the audio backend that threw the exception.</param>
/// <param name="result">The result returned by the audio backend.</param>
/// <param name="message">The error message of the exception.</param>
public class MiniaudioException(string backendName, MiniAudioResult result, string message) : BackendException(backendName, (int)result, message)
{
    /// <summary>
    ///     The result returned by the audio backend.
    /// </summary>
    public MiniAudioResult Result { get; } = result;

    /// <inheritdoc />
    public override string ToString() => $"Backend: {Backend}\nResult: {Result}\nMessage: {Message}\nStackTrace: {StackTrace}";
}
