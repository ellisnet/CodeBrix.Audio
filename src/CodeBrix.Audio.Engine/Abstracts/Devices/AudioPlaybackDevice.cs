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
    }
}
