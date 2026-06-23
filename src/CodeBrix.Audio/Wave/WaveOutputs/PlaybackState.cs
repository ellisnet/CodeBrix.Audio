using System;
using System.Collections.Generic;
using System.Text;

namespace CodeBrix.Audio.Wave; //was previously: NAudio.Wave;

/// <summary>
/// Playback State
/// </summary>
public enum PlaybackState
{
    /// <summary>
    /// Stopped
    /// </summary>
    Stopped,
    /// <summary>
    /// Playing
    /// </summary>
    Playing,
    /// <summary>
    /// Paused
    /// </summary>
    Paused
}
