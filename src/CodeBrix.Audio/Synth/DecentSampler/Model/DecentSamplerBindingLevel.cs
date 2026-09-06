namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// Which layer of the instrument a <c>&lt;binding&gt;</c> addresses.
/// </summary>
public enum DecentSamplerBindingLevel
{
    /// <summary>The user interface (<c>ui</c>).</summary>
    Ui,

    /// <summary>The instrument as a whole (<c>instrument</c>).</summary>
    Instrument,

    /// <summary>One or more groups (<c>group</c>).</summary>
    Group,

    /// <summary>Individual samples, selected by tag (<c>sample</c>).</summary>
    Sample,

    /// <summary>Individual oscillators, selected by tag (<c>oscillator</c>).</summary>
    Oscillator,

    /// <summary>A tag (<c>tag</c>).</summary>
    Tag,

    /// <summary>A MIDI handler (<c>midi</c>).</summary>
    Midi,

    /// <summary>A bus (<c>bus</c>).</summary>
    Bus,

    /// <summary>A level this engine does not recognise. The raw text is kept in <c>LevelName</c>.</summary>
    Unknown,
}
