using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// One <c>&lt;sample&gt;</c>: a playable zone backed by an audio file.
/// </summary>
/// <remarks>
/// Everything except <see cref="Path"/>, <see cref="RootNote"/>, <see cref="PreviousNotes"/>,
/// <see cref="LegatoInterval"/> and <see cref="Length"/> is inherited from the enclosing
/// <c>&lt;group&gt;</c> and <c>&lt;groups&gt;</c> when this element does not write it.
/// </remarks>
public sealed class DecentSamplerSampleElement : DecentSamplerSoundElement
{
    /// <summary>The sample's 0-based position within its group.</summary>
    public int Index { get; internal set; }

    /// <summary>
    /// The audio file, relative to the preset (<c>path</c>). WAV, AIFF and FLAC are supported.
    /// </summary>
    public string Path { get; internal set; }

    /// <summary>The note the file was recorded at (<c>rootNote</c>).</summary>
    public int? RootNote { get; internal set; }

    /// <summary>
    /// Notes that must have been the previously triggered note for this zone to play
    /// (<c>previousNotes</c>). Never null; empty when none were written.
    /// </summary>
    public IReadOnlyList<int> PreviousNotes { get; internal set; } = [];

    /// <summary>
    /// The exact semitone distance from the previously triggered note that lets this zone play
    /// (<c>legatoInterval</c>).
    /// </summary>
    public int? LegatoInterval { get; internal set; }

    /// <summary>
    /// The file's length in frames as the preset recorded it (<c>length</c>). Written by the
    /// reference editor and shown in the guide's boilerplate preset; the engine reads the real length
    /// from the file and uses this only as a cross-check.
    /// </summary>
    public long? Length { get; internal set; }
}
