using System;
using CodeBrix.Audio.Synth.DecentSampler.Engine;
using CodeBrix.Audio.Synth.DecentSampler.Streaming;
using CodeBrix.Audio.Synth.Mpe;

namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>
/// The settings for Decent Sampler synthesis: sample rate, block size, polyphony, the seed behind
/// round-robin and random layer selection, the master volume, and the extension registry the
/// synthesizer resolves oscillator waveforms and effect types through.
/// </summary>
/// <remarks>
/// <para>
/// Shaped after <see cref="Sfz.SfzSynthesizerSettings"/> so the two sampled formats configure alike.
/// The one addition is <see cref="Extensions"/>, which lets a test hand the synthesizer an isolated
/// registry instead of the process-wide one.
/// </para>
/// <para>
/// The seed is fixed by default, so the same MIDI renders the same audio on every run - round robins,
/// random layers and all. Change <see cref="RandomSeed"/> for a different performance.
/// </para>
/// </remarks>
public sealed class DecentSamplerSynthesizerSettings
{
    /// <summary>The block size used when none is given.</summary>
    public const int DefaultBlockSize = 64;

    /// <summary>The polyphony used when none is given.</summary>
    /// <remarks>
    /// Higher than the SFZ engine's 64 on purpose: a Decent Sampler preset routinely layers a dozen or
    /// more groups (one corpus library has eighteen), so a five-note chord can want a hundred voices
    /// before anything is wrong.
    /// </remarks>
    public const int DefaultMaximumPolyphony = 192;

    /// <summary>The random seed used when none is given.</summary>
    public const int DefaultRandomSeed = 12345;

    /// <summary>The number of streaming ring buffers used when none is given.</summary>
    public const int DefaultStreamingVoiceCount = 32;

    /// <summary>The size of each streaming ring buffer used when none is given, in frames.</summary>
    public const int DefaultStreamingRingFrames = 8192;

    private int _sampleRate;
    private int _blockSize;
    private int _maximumPolyphony;
    private int _streamingVoiceCount = DefaultStreamingVoiceCount;
    private int _streamingRingFrames = DefaultStreamingRingFrames;

    /// <summary>
    /// Creates settings for an output sample rate.
    /// </summary>
    /// <param name="sampleRate">The synthesis sample rate in Hz, 16000 to 192000.</param>
    /// <exception cref="ArgumentOutOfRangeException">The sample rate is out of range.</exception>
    public DecentSamplerSynthesizerSettings(int sampleRate)
    {
        CheckSampleRate(sampleRate);

        _sampleRate = sampleRate;
        _blockSize = DefaultBlockSize;
        _maximumPolyphony = DefaultMaximumPolyphony;
        RandomSeed = DefaultRandomSeed;
        MasterVolume = 0.5f;
    }

    /// <summary>The synthesis sample rate in Hz. 16000 to 192000.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
    public int SampleRate
    {
        get => _sampleRate;
        set
        {
            CheckSampleRate(value);
            _sampleRate = value;
        }
    }

    /// <summary>The number of frames rendered per internal block. 8 to 1024.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
    public int BlockSize
    {
        get => _blockSize;
        set
        {
            if (!(8 <= value && value <= 1024))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "The block size must be between 8 and 1024.");
            }

            _blockSize = value;
        }
    }

    /// <summary>The maximum number of simultaneously sounding voices. 8 to 1024.</summary>
    /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
    public int MaximumPolyphony
    {
        get => _maximumPolyphony;
        set
        {
            if (!(8 <= value && value <= 1024))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "The maximum polyphony must be between 8 and 1024.");
            }

            _maximumPolyphony = value;
        }
    }

    /// <summary>
    /// The seed for round-robin random modes and any other random choice. The default is fixed so
    /// identical input renders identically.
    /// </summary>
    public int RandomSeed { get; set; }

    /// <summary>The initial master output gain. 0.5 by default, as the other synthesizers use.</summary>
    public float MasterVolume { get; set; }

    /// <summary>
    /// The registry oscillator waveforms and effect types are resolved through. Null means
    /// <see cref="DecentSamplerExtensions.Shared"/>, which is what an application wants; a test that
    /// registers a fake waveform gives its own registry here instead.
    /// </summary>
    public DecentSamplerExtensionRegistry Extensions { get; set; }

    /// <summary>
    /// The tempo the synthesizer reports before a transport sets one. The reference standalone runs at
    /// 120 BPM with no host, so tempo-driven delays and retriggers line up with it out of the box.
    /// </summary>
    public double DefaultBeatsPerMinute { get; set; } = TempoSource.DefaultBeatsPerMinute;

    /// <summary>
    /// Whether the preset's <c>&lt;modulators&gt;</c> run. On by default; switching it off plays the
    /// instrument exactly as the sampler engine alone would, which is what a comparison against an
    /// unmodulated render needs.
    /// </summary>
    public bool EnableModulators { get; set; } = true;

    /// <summary>
    /// How the synthesizer reads MIDI Polyphonic Expression zones. <see cref="Mpe.MpeMode.Off"/> by
    /// default, which plays every channel as ordinary MIDI.
    /// </summary>
    public MpeMode MpeMode { get; set; } = MpeMode.Off;

    /// <summary>
    /// How far a member channel's pitch bend reaches when the music never says, in semitones.
    /// Forty-eight by default, the value expressive controllers ship with. RPN 0 overrides it.
    /// </summary>
    public double MpeMemberBendRange { get; set; } = MpeChannelState.DefaultMemberBendRange;

    /// <summary>
    /// How many member channels the lower zone holds in an explicit mode. Zero, the default, means
    /// fifteen when only the lower zone is on and seven when both zones are.
    /// </summary>
    public int MpeLowerZoneMemberCount { get; set; }

    /// <summary>The upper zone's equivalent of <see cref="MpeLowerZoneMemberCount"/>.</summary>
    public int MpeUpperZoneMemberCount { get; set; }

    /// <summary>
    /// Who reads a streamed sample off the disk. <see cref="DecentSamplerStreamingMode.RealTime"/> by
    /// default: a background thread does it and the render call never touches a file.
    /// </summary>
    /// <remarks>
    /// Set <see cref="DecentSamplerStreamingMode.Offline"/> when the render call is not an audio
    /// callback - rendering to a WAV, filling a buffer, a test - and the renderer will fetch its own
    /// frames, so a streamed voice cannot fall behind however fast the render loop runs. It is the
    /// wrong choice for playback and the right one for everything else.
    /// </remarks>
    public DecentSamplerStreamingMode StreamingMode { get; set; } = DecentSamplerStreamingMode.RealTime;

    /// <summary>
    /// How many per-voice streaming ring buffers to allocate, which is the number of streamed notes
    /// that can sound at once. Default 32. 1 to 1024.
    /// </summary>
    /// <remarks>
    /// The buffers are allocated when the synthesizer is built and only when the instrument actually
    /// streams something. A note that finds them all busy plays silence and the instrument says so, in
    /// the same way as one that finds no free voice.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
    public int StreamingVoiceCount
    {
        get => _streamingVoiceCount;
        set
        {
            if (!(1 <= value && value <= 1024))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "The streaming voice count must be between 1 and 1024.");
            }

            _streamingVoiceCount = value;
        }
    }

    /// <summary>
    /// How many frames each streaming ring buffer holds, rounded up to a power of two. Default 8,192 -
    /// 186 ms at 44.1 kHz. 1,024 to 1,048,576.
    /// </summary>
    /// <remarks>
    /// This is the reader's head start: the longer it is, the more a busy machine can stall without a
    /// voice starving, at two channels times four bytes per frame per streamed note.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is out of range.</exception>
    public int StreamingRingFrames
    {
        get => _streamingRingFrames;
        set
        {
            if (!(1024 <= value && value <= 1048576))
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "The streaming ring size must be between 1024 and 1048576.");
            }

            _streamingRingFrames = value;
        }
    }

    private static void CheckSampleRate(int value)
    {
        if (!(16000 <= value && value <= 192000))
        {
            throw new ArgumentOutOfRangeException(
                nameof(value), value, "The sample rate must be between 16000 and 192000.");
        }
    }
}
