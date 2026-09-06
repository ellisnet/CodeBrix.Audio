namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// What kind of thing a <c>&lt;binding&gt;</c> targets.
/// </summary>
public enum DecentSamplerBindingType
{
    /// <summary>Amplitude, tuning, pan and routing values (<c>amp</c>).</summary>
    Amp,

    /// <summary>General instrument, group, oscillator and tag values (<c>general</c>).</summary>
    General,

    /// <summary>An effect parameter (<c>effect</c>).</summary>
    Effect,

    /// <summary>A user-interface control (<c>control</c>).</summary>
    Control,

    /// <summary>A labelled knob, the older spelling of a control target (<c>labeled_knob</c>).</summary>
    LabeledKnob,

    /// <summary>A <c>&lt;midi&gt;&lt;note&gt;</c> handler (<c>note</c>).</summary>
    Note,

    /// <summary>One binding beneath a <c>&lt;midi&gt;&lt;note&gt;</c> handler (<c>note_binding</c>).</summary>
    NoteBinding,

    /// <summary>One binding beneath a <c>&lt;midi&gt;&lt;velocity&gt;</c> handler (<c>velocity_binding</c>).</summary>
    VelocityBinding,

    /// <summary>A binding inside a MIDI continuous-controller handler (<c>cc_binding</c>).</summary>
    CcBinding,

    /// <summary>One binding beneath a button state (<c>button_state_binding</c>).</summary>
    ButtonStateBinding,

    /// <summary>An on-screen keyboard colour range (<c>keyboard_color</c>).</summary>
    KeyboardColor,

    /// <summary>A modulator parameter (<c>modulator</c>).</summary>
    Modulator,

    /// <summary>A note sequence (<c>note_sequence</c>).</summary>
    NoteSequence,

    /// <summary>The arpeggiator (<c>arpeggiator</c>).</summary>
    Arpeggiator,

    /// <summary>A type this engine does not recognise. The raw text is kept in <c>TypeName</c>.</summary>
    Unknown,
}
