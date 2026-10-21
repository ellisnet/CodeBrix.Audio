using CodeBrix.Audio.Engine.Components;
using CodeBrix.Audio.Engine.Structs;
using CodeBrix.Audio.Engine.Structs.Events;

namespace CodeBrix.Audio.Engine.Abstracts.Devices;  //was previously: SoundFlow.Abstracts.Devices

/// <summary>
/// Represents a playback (output) audio device.
/// </summary>
public abstract class AudioPlaybackDevice : AudioDevice
{
    /// <summary>
    /// The <see cref="AppContext"/> switch that makes every playback device created while it is on
    /// write silence: <c>AppContext.SetSwitch("CodeBrix.Audio.Muted", true)</c>, or the same name as a
    /// runtimeconfig option. CodeBrix PlayTests turn it on unless a test run opts into sound.
    /// </summary>
    public const string MutedSwitchName = "CodeBrix.Audio.Muted";

    /// <summary>
    /// True when <see cref="MutedSwitchName"/> was on when this device was created. A muted device
    /// renders exactly as an audible one - players advance, events fire, a game's audio keeps its
    /// timing - and then writes silence to the output.
    /// </summary>
    public bool IsOutputMuted { get; }

    /// <summary>
    /// Gets the master mixer for this device. All audio to be played on this device
    /// must be routed to this mixer.
    /// </summary>
    public Mixer MasterMixer { get; }
    
    /// <summary>
    /// Cached event args object to prevent GC allocations every frame.
    /// </summary>
    protected readonly AudioFramesRenderedEventArgs CachedRenderEventArgs;

    /// <summary>Raises the engine render notification using the cached event arguments.</summary>
    /// <param name="frameCount">Number of frames in the current callback.</param>
    protected void NotifyFramesRendered(int frameCount)
    {
        CachedRenderEventArgs.FrameCount = frameCount;
        Engine.RaiseAudioFramesRendered(CachedRenderEventArgs);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="AudioPlaybackDevice"/> class.
    /// </summary>
    /// <param name="engine">The parent audio engine.</param>
    /// <param name="format">The desired audio format.</param>
    /// <param name="config">The device configuration.</param>
    protected AudioPlaybackDevice(AudioEngine engine, AudioFormat format, DeviceConfig config) : base(engine, format, config)
    {
        MasterMixer = new Mixer(engine, Format, isMasterMixer: true) { ParentDevice = this };
        CachedRenderEventArgs = new AudioFramesRenderedEventArgs(this, 0);
        IsOutputMuted = AppContext.TryGetSwitch(MutedSwitchName, out var muted) && muted;
    }

    /// <summary>
    /// Fills a buffer with this device's output: the engine's soloed component if it has one,
    /// otherwise <see cref="MasterMixer"/> - then silence instead when <see cref="IsOutputMuted"/>.
    /// Backends call this from their render callback.
    /// </summary>
    /// <param name="buffer">The interleaved float buffer to fill.</param>
    /// <param name="channels">The number of channels to process.</param>
    protected void RenderOutput(Span<float> buffer, int channels)
    {
        buffer.Clear();

        var soloedComponent = Engine.GetSoloedComponent();
        if (soloedComponent != null)
            soloedComponent.Process(buffer, channels);
        else
            MasterMixer.Process(buffer, channels);

        if (IsOutputMuted)
            buffer.Clear();
    }
}
