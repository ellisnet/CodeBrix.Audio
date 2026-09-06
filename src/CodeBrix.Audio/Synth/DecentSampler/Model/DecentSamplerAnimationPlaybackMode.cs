namespace CodeBrix.Audio.Synth.DecentSampler.Model;

/// <summary>
/// How a multi-frame image animates.
/// </summary>
public enum DecentSamplerAnimationPlaybackMode
{
    /// <summary>Forwards, repeating (<c>forward_loop</c>). The default.</summary>
    ForwardLoop,

    /// <summary>Forwards, once (<c>forward_once</c>).</summary>
    ForwardOnce,

    /// <summary>Backwards, repeating (<c>reverse_loop</c>).</summary>
    ReverseLoop,

    /// <summary>Backwards, once (<c>reverse_once</c>).</summary>
    ReverseOnce,

    /// <summary>Forwards then backwards, repeating (<c>ping_pong_loop</c>).</summary>
    PingPongLoop,

    /// <summary>Not animating (<c>stopped</c>).</summary>
    Stopped,
}
