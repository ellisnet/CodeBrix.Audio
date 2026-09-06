namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// The kind of effect an <c>&lt;effect&gt;</c> element declares.
/// </summary>
public enum DecentSamplerEffectType
{
    /// <summary>A two-pole resonant low-pass filter (<c>lowpass</c>, legacy <c>lowpass_4pl</c>).</summary>
    Lowpass,

    /// <summary>A one-pole low-pass filter with no resonance (<c>lowpass_1pl</c>).</summary>
    Lowpass1Pole,

    /// <summary>A two-pole resonant band-pass filter (<c>bandpass</c>).</summary>
    Bandpass,

    /// <summary>A two-pole resonant high-pass filter (<c>highpass</c>).</summary>
    Highpass,

    /// <summary>A notch filter (<c>notch</c>).</summary>
    Notch,

    /// <summary>A peaking equalizer band (<c>peak</c>).</summary>
    Peak,

    /// <summary>A gain stage in decibels or linear units (<c>gain</c>).</summary>
    Gain,

    /// <summary>A room reverb (<c>reverb</c>).</summary>
    Reverb,

    /// <summary>A delay line in seconds or musical time (<c>delay</c>).</summary>
    Delay,

    /// <summary>A chorus (<c>chorus</c>).</summary>
    Chorus,

    /// <summary>A phaser (<c>phaser</c>).</summary>
    Phaser,

    /// <summary>A convolution reverb or amp simulation (<c>convolution</c>).</summary>
    Convolution,

    /// <summary>A pitch shifter (<c>pitch_shift</c>).</summary>
    PitchShift,

    /// <summary>A wave folder (<c>wave_folder</c>).</summary>
    WaveFolder,

    /// <summary>A wave shaper (<c>wave_shaper</c>).</summary>
    WaveShaper,

    /// <summary>A mono-to-pseudo-stereo widener (<c>stereo_simulator</c>).</summary>
    StereoSimulator,

    /// <summary>A bit-depth and sample-rate reducer (<c>bit_crusher</c>).</summary>
    BitCrusher,

    /// <summary>A stereo-linked dynamics compressor (<c>compressor</c>).</summary>
    Compressor,

    /// <summary>A randomized stutter gate (<c>gate</c>).</summary>
    Gate,

    /// <summary>A type this engine does not recognise. The raw text is kept in <c>TypeName</c>.</summary>
    Unknown,
}
