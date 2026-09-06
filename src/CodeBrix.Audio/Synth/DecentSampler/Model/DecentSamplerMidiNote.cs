namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// A <c>&lt;note&gt;</c> handler: bindings that fire on one MIDI note or a range of them. This is how
/// key switches are built.
/// </summary>
public sealed class DecentSamplerMidiNote : DecentSamplerMidiHandler
{
    /// <summary>The <c>note</c> attribute exactly as written, which may be a number or a range.</summary>
    public string NoteText { get; internal set; }

    /// <summary>The lowest note the handler listens on.</summary>
    public int? LowNote { get; internal set; }

    /// <summary>The highest note the handler listens on, inclusive.</summary>
    public int? HighNote { get; internal set; }

    /// <summary>Which note events fire the bindings (<c>eventType</c>). Default any.</summary>
    public DecentSamplerMidiEventType? EventType { get; internal set; }

    /// <summary>Whether the handler is listening (<c>enabled</c>). Default true.</summary>
    public bool? Enabled { get; internal set; }

    /// <summary>
    /// Whether the note is consumed rather than passed on to the sampler (<c>swallowNotes</c>).
    /// Default false, so a key switch also sounds unless this is set.
    /// </summary>
    public bool? SwallowNotes { get; internal set; }
}
