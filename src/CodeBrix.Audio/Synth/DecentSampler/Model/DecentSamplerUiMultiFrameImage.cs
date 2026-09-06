namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// A <c>&lt;multiFrameImage&gt;</c>: an animation played from a strip of frames in one image file.
/// </summary>
public sealed class DecentSamplerUiMultiFrameImage : DecentSamplerUiElement
{
    /// <summary>The image file holding the frame strip, relative to the preset (<c>path</c>).</summary>
    public string Path { get; internal set; }

    /// <summary>How many frames the strip holds (<c>numFrames</c>).</summary>
    public int? NumFrames { get; internal set; }

    /// <summary>Frames per second (<c>frameRate</c>). The reference player caps this at 24.</summary>
    public double? FrameRate { get; internal set; }

    /// <summary>How opaque the animation is, 0 to 1 (<c>opacity</c>). Default 1.</summary>
    public double? Opacity { get; internal set; }

    /// <summary>How the frames are laid out in the strip (<c>sourceFormat</c>).</summary>
    public DecentSamplerImageStripFormat? SourceFormat { get; internal set; }

    /// <summary>The <c>sourceFormat</c> attribute exactly as written.</summary>
    public string SourceFormatName { get; internal set; }

    /// <summary>Which way and how often the animation plays (<c>playbackMode</c>). Default forward loop.</summary>
    public DecentSamplerAnimationPlaybackMode? PlaybackMode { get; internal set; }

    /// <summary>The <c>playbackMode</c> attribute exactly as written.</summary>
    public string PlaybackModeName { get; internal set; }
}
