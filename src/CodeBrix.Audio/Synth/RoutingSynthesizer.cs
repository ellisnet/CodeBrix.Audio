using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.Synth;

/// <summary>
/// An <see cref="IMidiSynthesizer"/> made of other synthesizers: one child per MIDI channel, each
/// with its own gain and an optional layered second child, mixed under one master gain.
/// </summary>
/// <remarks>
/// <para>
/// WHY THIS EXISTS. <see cref="MidiSequencer"/> and <see cref="MidiStreamSequencer"/> each drive
/// exactly ONE synthesizer, and a voiced arrangement needs one synthesizer per part - a per-voice
/// gain and a layered second instrument cannot be had from a single multi-timbral synthesizer,
/// which mixes internally at one level. This is the piece that makes those two facts fit together:
/// to everything above it, a whole voiced arrangement looks like one synthesizer.
/// </para>
/// <para>
/// It works the same in both modes, which is the point. A <see cref="MidiStreamSequencer"/> drives
/// it live, so a voiced arrangement can play while the music is still being written; and
/// <see cref="SoundFontRenderer.Render(IMidiSynthesizer, MidiSequence, TimeSpan)"/> renders through
/// it offline. One code path, one sound.
/// </para>
/// <code>
/// var router = new RoutingSynthesizer(44100);
/// router.SetChannel(1, library.CreateSynthesizer(program: 8, 44100), gain: 0.8f);   // celesta
/// router.SetChannel(2, () =&gt; library.CreateSynthesizer(program: 89, 44100), 0.5f); // built on first use
/// router.SetLayer(2, () =&gt; library.CreateSynthesizer(program: 52, 44100), 0.3f);   // doubled by a choir
/// router.SetChannel(10, library.CreatePercussionSynthesizer(44100));
/// var samples = SoundFontRenderer.Render(router, sequence, TimeSpan.FromSeconds(2));
/// </code>
/// <para>
/// CHANNELS ARE NUMBERED 1 TO 16 HERE, the way <see cref="CodeBrix.Audio.Midi.MidiEvent.Channel"/>
/// and <see cref="CodeBrix.Audio.Midi.GeneralMidi.PercussionChannel"/> number them - so percussion
/// is channel 10, as a musician would say. The <see cref="ProcessMidiMessage"/> argument is the
/// wire number, 0 to 15, because that is what every <see cref="IMidiSynthesizer"/> is handed; the
/// router does the conversion. Messages are forwarded to the child WITHOUT renumbering, so a child
/// keeps its per-channel controller state where the music put it and still treats the wire's
/// channel 9 as percussion.
/// </para>
/// <para>
/// CHILDREN ARE CREATED LAZILY when a factory is given instead of an instance: nothing is built
/// until the first message for that channel arrives or the first block is rendered. A rendition
/// over a large sample library then pays only for the parts the music actually uses.
/// </para>
/// <para>
/// A MESSAGE FOR AN UNROUTED CHANNEL IS DROPPED - silently, because a piece carrying a part nobody
/// voiced should not stop the music. <see cref="UnroutedMessageCount"/> counts them, so a
/// diagnostic can say plainly that something was played and nothing was listening.
/// </para>
/// <para>
/// EVERY CHILD MUST SHARE THE ROUTER'S SAMPLE RATE, and no synthesizer instance may be routed to
/// more than one slot: each child is rendered exactly once per block at exactly one gain, and an
/// instance in two slots has no single answer to either. Children may differ in
/// <see cref="IMidiSynthesizer.BlockSize"/> - each one is asked for the frames the caller wants and
/// blocks internally however it likes - so the router's own <see cref="BlockSize"/> is fixed at
/// construction and is simply the granularity at which a sequencer interleaves messages with audio.
/// </para>
/// <para>
/// Like every other synthesizer here, this one is not thread-safe: rendering and MIDI events must
/// not overlap, and neither may overlap a change to the routing table.
/// </para>
/// </remarks>
public sealed class RoutingSynthesizer : IMidiSynthesizer
{
    /// <summary>The number of MIDI channels a router covers.</summary>
    public const int ChannelCount = 16;

    /// <summary>The block size used when none is given.</summary>
    public const int DefaultBlockSize = 64;

    private readonly int sampleRate;
    private readonly int blockSize;
    private readonly Route[] routes = new Route[ChannelCount];
    private readonly Route[] layers = new Route[ChannelCount];

    private float[] scratchLeft = [];
    private float[] scratchRight = [];

    private float masterVolume = 1.0F;
    private long unroutedMessageCount;

    /// <summary>Creates an empty router at a sample rate, with the default block size.</summary>
    /// <param name="sampleRate">The sample rate every child must render at, in Hz.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="sampleRate"/> is not positive.</exception>
    public RoutingSynthesizer(int sampleRate) : this(sampleRate, DefaultBlockSize)
    {
    }

    /// <summary>Creates an empty router at a sample rate and block size.</summary>
    /// <param name="sampleRate">The sample rate every child must render at, in Hz.</param>
    /// <param name="blockSize">
    /// The granularity at which a sequencer interleaves MIDI messages with audio. It does not have
    /// to match any child's own block size.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="sampleRate"/> or <paramref name="blockSize"/> is not positive.
    /// </exception>
    public RoutingSynthesizer(int sampleRate, int blockSize)
    {
        if (sampleRate <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(sampleRate), sampleRate, "The sample rate must be positive.");
        }

        if (blockSize <= 0)
        {
            throw new ArgumentOutOfRangeException(
                nameof(blockSize), blockSize, "The block size must be positive.");
        }

        this.sampleRate = sampleRate;
        this.blockSize = blockSize;
    }

    /// <inheritdoc />
    public int SampleRate => sampleRate;

    /// <inheritdoc />
    public int BlockSize => blockSize;

    /// <inheritdoc />
    public int ActiveVoiceCount
    {
        get
        {
            var total = 0;

            foreach (var route in AllRoutes())
            {
                if (route.Synthesizer != null)
                {
                    total += route.Synthesizer.ActiveVoiceCount;
                }
            }

            return total;
        }
    }

    /// <summary>
    /// The gain applied to the whole mix, after every child's own gain. One by default, because
    /// each child already carries whatever level its own engine renders at.
    /// </summary>
    public float MasterVolume
    {
        get => masterVolume;
        set => masterVolume = value;
    }

    /// <summary>
    /// How many MIDI messages arrived for a channel with nothing routed to it. A part that was
    /// played and never heard shows up here.
    /// </summary>
    public long UnroutedMessageCount => unroutedMessageCount;

    /// <summary>
    /// Every child synthesizer that has actually been created, each listed once, in channel order
    /// with a channel's layer after its main child. A lazily routed channel that has never been
    /// used is not in here.
    /// </summary>
    public IReadOnlyList<IMidiSynthesizer> Synthesizers
    {
        get
        {
            var created = new List<IMidiSynthesizer>();

            for (var index = 0; index < ChannelCount; index++)
            {
                if (routes[index] != null && routes[index].Synthesizer != null)
                {
                    created.Add(routes[index].Synthesizer);
                }

                if (layers[index] != null && layers[index].Synthesizer != null)
                {
                    created.Add(layers[index].Synthesizer);
                }
            }

            return created;
        }
    }

    /// <summary>Routes a channel to a synthesizer at unity gain.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="synthesizer">The child that plays this channel.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="synthesizer"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The synthesizer renders at a different sample rate, or is already routed elsewhere.
    /// </exception>
    public void SetChannel(int channel, IMidiSynthesizer synthesizer) => SetChannel(channel, synthesizer, 1.0F);

    /// <summary>Routes a channel to a synthesizer at a given gain.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="synthesizer">The child that plays this channel.</param>
    /// <param name="gain">The gain this child is mixed at.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="synthesizer"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The synthesizer renders at a different sample rate, or is already routed elsewhere.
    /// </exception>
    public void SetChannel(int channel, IMidiSynthesizer synthesizer, float gain) =>
        routes[Index(channel)] = BuildRoute(synthesizer, gain);

    /// <summary>Routes a channel to a synthesizer that is built on first use.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="synthesizerFactory">
    /// Builds the child. Called at most once, the first time this channel is played or rendered.
    /// </param>
    /// <param name="gain">The gain this child is mixed at.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="synthesizerFactory"/> is null.</exception>
    public void SetChannel(int channel, Func<IMidiSynthesizer> synthesizerFactory, float gain = 1.0F) =>
        routes[Index(channel)] = BuildRoute(synthesizerFactory, gain);

    /// <summary>Adds a second synthesizer that plays a channel alongside its main child.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="synthesizer">The child layered over this channel.</param>
    /// <param name="gain">The gain this layer is mixed at.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="synthesizer"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The synthesizer renders at a different sample rate, or is already routed elsewhere.
    /// </exception>
    public void SetLayer(int channel, IMidiSynthesizer synthesizer, float gain = 1.0F) =>
        layers[Index(channel)] = BuildRoute(synthesizer, gain);

    /// <summary>Adds a second synthesizer, built on first use, that plays a channel alongside its main child.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="synthesizerFactory">
    /// Builds the layer. Called at most once, the first time this channel is played or rendered.
    /// </param>
    /// <param name="gain">The gain this layer is mixed at.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="synthesizerFactory"/> is null.</exception>
    public void SetLayer(int channel, Func<IMidiSynthesizer> synthesizerFactory, float gain = 1.0F) =>
        layers[Index(channel)] = BuildRoute(synthesizerFactory, gain);

    /// <summary>Removes a channel's main child and its layer, so the channel goes silent.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    public void ClearChannel(int channel)
    {
        var index = Index(channel);
        routes[index] = null;
        layers[index] = null;
    }

    /// <summary>Removes a channel's layer, leaving its main child in place.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    public void ClearLayer(int channel) => layers[Index(channel)] = null;

    /// <summary>Whether anything is routed to a channel, created or not.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <returns>True when a message for that channel reaches a child.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    public bool IsRouted(int channel) => routes[Index(channel)] != null;

    /// <summary>Whether a channel carries a layered second child.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <returns>True when a layer is routed to that channel.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    public bool HasLayer(int channel) => layers[Index(channel)] != null;

    /// <summary>The gain a channel's main child is mixed at, or zero when nothing is routed.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <returns>The gain.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    public float GetChannelGain(int channel) => GainOf(routes[Index(channel)]);

    /// <summary>Sets the gain a channel's main child is mixed at.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="gain">The new gain.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    /// <exception cref="InvalidOperationException">Nothing is routed to that channel.</exception>
    public void SetChannelGain(int channel, float gain) =>
        SetGain(routes[Index(channel)], gain, channel, "channel");

    /// <summary>The gain a channel's layer is mixed at, or zero when it has none.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <returns>The gain.</returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    public float GetLayerGain(int channel) => GainOf(layers[Index(channel)]);

    /// <summary>Sets the gain a channel's layer is mixed at.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="gain">The new gain.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    /// <exception cref="InvalidOperationException">That channel has no layer.</exception>
    public void SetLayerGain(int channel, float gain) =>
        SetGain(layers[Index(channel)], gain, channel, "layer");

    /// <inheritdoc />
    /// <remarks>
    /// <paramref name="channel"/> is the WIRE channel, 0 to 15 - the routing table is addressed 1
    /// to 16. A message for a channel with nothing routed to it is dropped and counted in
    /// <see cref="UnroutedMessageCount"/>.
    /// </remarks>
    public void ProcessMidiMessage(int channel, int command, int data1, int data2)
    {
        if (channel < 0 || channel >= ChannelCount)
        {
            return;
        }

        var route = routes[channel];
        var layer = layers[channel];

        if (route == null && layer == null)
        {
            unroutedMessageCount++;
            return;
        }

        if (route != null)
        {
            Materialize(route).ProcessMidiMessage(channel, command, data1, data2);
        }

        if (layer != null)
        {
            Materialize(layer).ProcessMidiMessage(channel, command, data1, data2);
        }
    }

    /// <inheritdoc />
    public void NoteOffAll(bool immediate)
    {
        foreach (var route in AllRoutes())
        {
            // Only what already exists: building a synthesizer in order to silence it would be a
            // strange way to spend a hundred megabytes.
            route.Synthesizer?.NoteOffAll(immediate);
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns every child that has been created to its initial state and clears
    /// <see cref="UnroutedMessageCount"/>. The routing table is configuration rather than state, so
    /// it is left alone: a reset router plays the same arrangement from the beginning.
    /// </remarks>
    public void Reset()
    {
        foreach (var route in AllRoutes())
        {
            route.Synthesizer?.Reset();
        }

        unroutedMessageCount = 0;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Each created child renders the frames asked for - whatever its own block size - and is
    /// mixed in at its gain; the sum is then scaled by <see cref="MasterVolume"/>. A channel routed
    /// lazily and never played is SKIPPED rather than built, because a synthesizer that has been
    /// sent no message renders silence anyway - which is what lets a rendition over a large sample
    /// library cost only the parts the music uses.
    /// </remarks>
    public void Render(Span<float> left, Span<float> right)
    {
        if (left.Length != right.Length)
        {
            throw new ArgumentException("The output buffers for the left and right must be the same length.");
        }

        left.Clear();
        right.Clear();

        var frames = left.Length;

        if (frames == 0)
        {
            return;
        }

        EnsureScratch(frames);

        foreach (var route in AllRoutes())
        {
            var synthesizer = route.Synthesizer;

            if (synthesizer == null)
            {
                continue;
            }

            var scratchL = scratchLeft.AsSpan(0, frames);
            var scratchR = scratchRight.AsSpan(0, frames);

            synthesizer.Render(scratchL, scratchR);

            var gain = route.Gain;

            for (var index = 0; index < frames; index++)
            {
                left[index] += gain * scratchL[index];
                right[index] += gain * scratchR[index];
            }
        }

        if (masterVolume == 1.0F)
        {
            return;
        }

        for (var index = 0; index < frames; index++)
        {
            left[index] *= masterVolume;
            right[index] *= masterVolume;
        }
    }

    private static float GainOf(Route route) => route == null ? 0.0F : route.Gain;

    private static void SetGain(Route route, float gain, int channel, string slot)
    {
        if (route == null)
        {
            throw new InvalidOperationException(
                $"Nothing is routed to the {slot} of MIDI channel {channel}, so it has no gain to set.");
        }

        route.Gain = gain;
    }

    private static int Index(int channel)
    {
        if (channel < 1 || channel > ChannelCount)
        {
            throw new ArgumentOutOfRangeException(
                nameof(channel), channel, "A MIDI channel is 1 to 16, the way MidiEvent counts them.");
        }

        return channel - 1;
    }

    private Route BuildRoute(IMidiSynthesizer synthesizer, float gain)
    {
        if (synthesizer == null)
        {
            throw new ArgumentNullException(nameof(synthesizer));
        }

        if (synthesizer.SampleRate != sampleRate)
        {
            throw new ArgumentException(
                $"This router renders at {sampleRate} Hz and the synthesizer renders at " +
                $"{synthesizer.SampleRate} Hz. Every child of a router must share its sample rate.",
                nameof(synthesizer));
        }

        if (IsAlreadyRouted(synthesizer))
        {
            throw new ArgumentException(
                "That synthesizer is already routed to another channel or layer. A router renders " +
                "each child once per block at one gain, so an instance may fill only one slot; " +
                "create a second synthesizer for the second part.",
                nameof(synthesizer));
        }

        return new Route(synthesizer, null, gain);
    }

    private Route BuildRoute(Func<IMidiSynthesizer> synthesizerFactory, float gain)
    {
        if (synthesizerFactory == null)
        {
            throw new ArgumentNullException(nameof(synthesizerFactory));
        }

        return new Route(null, synthesizerFactory, gain);
    }

    private bool IsAlreadyRouted(IMidiSynthesizer synthesizer)
    {
        foreach (var route in AllRoutes())
        {
            if (ReferenceEquals(route.Synthesizer, synthesizer))
            {
                return true;
            }
        }

        return false;
    }

    private IEnumerable<Route> AllRoutes()
    {
        for (var index = 0; index < ChannelCount; index++)
        {
            if (routes[index] != null)
            {
                yield return routes[index];
            }

            if (layers[index] != null)
            {
                yield return layers[index];
            }
        }
    }

    private IMidiSynthesizer Materialize(Route route)
    {
        if (route.Synthesizer != null)
        {
            return route.Synthesizer;
        }

        var synthesizer = route.Factory();

        if (synthesizer == null)
        {
            throw new InvalidOperationException(
                "A routing synthesizer's factory returned null. It must build a synthesizer.");
        }

        if (synthesizer.SampleRate != sampleRate)
        {
            throw new InvalidOperationException(
                $"This router renders at {sampleRate} Hz and the synthesizer its factory built " +
                $"renders at {synthesizer.SampleRate} Hz. Every child of a router must share its " +
                "sample rate.");
        }

        route.Synthesizer = synthesizer;
        return synthesizer;
    }

    private void EnsureScratch(int frames)
    {
        if (scratchLeft.Length >= frames)
        {
            return;
        }

        scratchLeft = new float[frames];
        scratchRight = new float[frames];
    }

    // One slot of the routing table: the child, or the factory that has not built it yet, plus the
    // gain that child is mixed at.
    private sealed class Route
    {
        internal Route(IMidiSynthesizer synthesizer, Func<IMidiSynthesizer> factory, float gain)
        {
            Synthesizer = synthesizer;
            Factory = factory;
            Gain = gain;
        }

        internal IMidiSynthesizer Synthesizer { get; set; }

        internal Func<IMidiSynthesizer> Factory { get; }

        internal float Gain { get; set; }
    }
}
