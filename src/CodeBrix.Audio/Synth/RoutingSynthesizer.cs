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
/// A REPLACED CHILD RINGS OUT. Routing something new onto a slot that is already occupied - a part
/// re-voiced in the middle of a piece, a follow-up prompt whose music uses other instruments on the
/// same channels - RETIRES the old child rather than cutting it off: it is released, it receives no
/// further messages, and it KEEPS BEING MIXED at the gain it had until its voices have finished and
/// its own effects have nothing left to say, or until <see cref="RingOutLimit"/> is up. Set
/// <see cref="RingOutReplacedChildren"/> to <see langword="false"/> for the older behaviour, where a
/// replaced child leaves the mix at once. <see cref="ClearChannel(int)"/> and
/// <see cref="ClearLayer(int)"/> stay immediate whatever the switch says - a cleared channel is
/// meant to go silent - and their two-argument overloads ask for a ring-out explicitly.
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

    /// <summary>
    /// How long a retired child may go on being rendered before it is let go anyway: ten seconds,
    /// which is longer than the longest release and reverb tail a synthesizer here produces.
    /// </summary>
    public static readonly TimeSpan DefaultRingOutLimit = TimeSpan.FromSeconds(10.0);

    // Below this in both channels, a child is saying nothing anybody could hear - about -100 dBFS.
    private const float SilenceLevel = 1.0e-5F;

    // And it has to say nothing for this long before it is let go, so that a decaying tail passing
    // through zero is not mistaken for the end of it.
    private const int SilenceMilliseconds = 50;

    private readonly int sampleRate;
    private readonly int blockSize;
    private readonly Route[] routes = new Route[ChannelCount];
    private readonly Route[] layers = new Route[ChannelCount];
    private readonly List<RetiredChild> retired = new List<RetiredChild>();

    private float[] scratchLeft = [];
    private float[] scratchRight = [];

    private float masterVolume = 1.0F;
    private long unroutedMessageCount;
    private bool ringOutReplacedChildren = true;
    private TimeSpan ringOutLimit = DefaultRingOutLimit;

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
    /// <remarks>
    /// Retired children are counted too, for as long as they are still being mixed: they are part
    /// of what the router is sounding, and a caller waiting for silence is waiting for them as well.
    /// </remarks>
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

            foreach (var child in retired)
            {
                total += child.Synthesizer.ActiveVoiceCount;
            }

            return total;
        }
    }

    /// <summary>
    /// Whether a child that is replaced in an occupied slot is let RING OUT rather than cut off.
    /// <see langword="true"/> by default.
    /// </summary>
    /// <remarks>
    /// <para>
    /// While this is set, routing something new onto a slot that already holds a built child
    /// RETIRES that child: it is released - the notes let go of the key, so releases and effect
    /// tails carry on - it is sent no further messages, and it keeps being mixed at the gain it had
    /// until it has nothing left to say or <see cref="RingOutLimit"/> is up.
    /// <see cref="RetiredChildCount"/> says how many are still sounding.
    /// </para>
    /// <para>
    /// Clear it and a replacement behaves as it always did: the old child leaves the mix at the
    /// moment the new one takes the slot, and whatever it was sounding stops dead. It governs
    /// REPLACEMENT only - <see cref="ClearChannel(int, bool)"/> and
    /// <see cref="ClearLayer(int, bool)"/> are asked for a ring-out in so many words, and get it
    /// either way.
    /// </para>
    /// </remarks>
    public bool RingOutReplacedChildren
    {
        get => ringOutReplacedChildren;
        set => ringOutReplacedChildren = value;
    }

    /// <summary>
    /// How long a retired child may go on being rendered before it is let go whatever it is still
    /// sounding. <see cref="DefaultRingOutLimit"/> to begin with.
    /// </summary>
    /// <remarks>
    /// A retired child normally goes when its voices have finished AND its output has been silent
    /// for a moment - which is what keeps a reverb tail alive after the last voice has ended. This
    /// is the backstop under that rule, so a child whose effects never quite decay to nothing
    /// cannot cost CPU for ever. <see cref="TimeSpan.Zero"/> lets a retired child go at the first
    /// block rendered after it was retired. It is read per block, so lowering it releases children
    /// that are already retired.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The value is negative.</exception>
    public TimeSpan RingOutLimit
    {
        get => ringOutLimit;

        set
        {
            if (value < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(value), value, "The ring-out limit must be a non-negative value.");
            }

            ringOutLimit = value;
        }
    }

    /// <summary>
    /// How many retired children are still being mixed - children that have left the routing table
    /// and have not yet finished sounding. Zero when nothing is ringing out.
    /// </summary>
    public int RetiredChildCount => retired.Count;

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
    /// <remarks>
    /// It is the ROUTING TABLE, so a retired child is not in it: it has left the table, it can no
    /// longer be played, and the only thing left to ask about it is whether it has finished, which
    /// <see cref="RetiredChildCount"/> answers.
    /// </remarks>
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
    /// The synthesizer renders at a different sample rate, or is already routed to another slot.
    /// </exception>
    /// <remarks>
    /// Routing over a slot that already holds a built child retires that child - see
    /// <see cref="RingOutReplacedChildren"/>. Handing back the instance the slot ALREADY holds is
    /// not a replacement at all: it simply sets that child's gain, and nothing is retired.
    /// </remarks>
    public void SetChannel(int channel, IMidiSynthesizer synthesizer, float gain)
    {
        var index = Index(channel);

        if (TrySetGainOfSameChild(routes[index], synthesizer, gain))
        {
            return;
        }

        // Built before anything is retired, so a call that turns out to be invalid leaves the
        // router exactly as it was.
        var route = BuildRoute(synthesizer, gain);

        RetireReplaced(routes[index]);
        routes[index] = route;
    }

    /// <summary>Routes a channel to a synthesizer that is built on first use.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="synthesizerFactory">
    /// Builds the child. Called at most once, the first time this channel is played or rendered.
    /// </param>
    /// <param name="gain">The gain this child is mixed at.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="synthesizerFactory"/> is null.</exception>
    /// <remarks>
    /// Routing over a slot that already holds a built child retires that child - see
    /// <see cref="RingOutReplacedChildren"/>.
    /// </remarks>
    public void SetChannel(int channel, Func<IMidiSynthesizer> synthesizerFactory, float gain = 1.0F)
    {
        var index = Index(channel);
        var route = BuildRoute(synthesizerFactory, gain);

        RetireReplaced(routes[index]);
        routes[index] = route;
    }

    /// <summary>Adds a second synthesizer that plays a channel alongside its main child.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="synthesizer">The child layered over this channel.</param>
    /// <param name="gain">The gain this layer is mixed at.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="synthesizer"/> is null.</exception>
    /// <exception cref="ArgumentException">
    /// The synthesizer renders at a different sample rate, or is already routed to another slot.
    /// </exception>
    /// <remarks>
    /// Layering over a slot that already holds a built layer retires that layer - see
    /// <see cref="RingOutReplacedChildren"/>. Handing back the instance the slot ALREADY holds
    /// simply sets that layer's gain.
    /// </remarks>
    public void SetLayer(int channel, IMidiSynthesizer synthesizer, float gain = 1.0F)
    {
        var index = Index(channel);

        if (TrySetGainOfSameChild(layers[index], synthesizer, gain))
        {
            return;
        }

        var route = BuildRoute(synthesizer, gain);

        RetireReplaced(layers[index]);
        layers[index] = route;
    }

    /// <summary>Adds a second synthesizer, built on first use, that plays a channel alongside its main child.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="synthesizerFactory">
    /// Builds the layer. Called at most once, the first time this channel is played or rendered.
    /// </param>
    /// <param name="gain">The gain this layer is mixed at.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    /// <exception cref="ArgumentNullException"><paramref name="synthesizerFactory"/> is null.</exception>
    /// <remarks>
    /// Layering over a slot that already holds a built layer retires that layer - see
    /// <see cref="RingOutReplacedChildren"/>.
    /// </remarks>
    public void SetLayer(int channel, Func<IMidiSynthesizer> synthesizerFactory, float gain = 1.0F)
    {
        var index = Index(channel);
        var route = BuildRoute(synthesizerFactory, gain);

        RetireReplaced(layers[index]);
        layers[index] = route;
    }

    /// <summary>Removes a channel's main child and its layer, so the channel goes silent.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    /// <remarks>
    /// IMMEDIATE, whatever <see cref="RingOutReplacedChildren"/> says: the children leave the mix
    /// at once and whatever they were sounding stops. Use <see cref="ClearChannel(int, bool)"/> to
    /// let them ring out instead.
    /// </remarks>
    public void ClearChannel(int channel) => ClearChannel(channel, ringOut: false);

    /// <summary>
    /// Removes a channel's main child and its layer, letting them ring out first if asked.
    /// </summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="ringOut">
    /// <see langword="true"/> to RETIRE the children rather than cut them - they are released and
    /// go on being mixed at the gain they had until they have nothing left to say or
    /// <see cref="RingOutLimit"/> is up. <see langword="false"/> for the immediate removal
    /// <see cref="ClearChannel(int)"/> performs.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    /// <remarks>
    /// <paramref name="ringOut"/> is obeyed on its own account: a ring-out asked for here happens
    /// even when <see cref="RingOutReplacedChildren"/> is clear, and a clear asked for here is
    /// immediate even when it is set.
    /// </remarks>
    public void ClearChannel(int channel, bool ringOut)
    {
        var index = Index(channel);

        if (ringOut)
        {
            Retire(routes[index]);
            Retire(layers[index]);
        }

        routes[index] = null;
        layers[index] = null;
    }

    /// <summary>Removes a channel's layer, leaving its main child in place.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    /// <remarks>
    /// IMMEDIATE, whatever <see cref="RingOutReplacedChildren"/> says. Use
    /// <see cref="ClearLayer(int, bool)"/> to let the layer ring out instead.
    /// </remarks>
    public void ClearLayer(int channel) => ClearLayer(channel, ringOut: false);

    /// <summary>Removes a channel's layer, letting it ring out first if asked.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <param name="ringOut">
    /// <see langword="true"/> to RETIRE the layer rather than cut it; <see langword="false"/> for
    /// the immediate removal <see cref="ClearLayer(int)"/> performs.
    /// </param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    /// <remarks>
    /// <paramref name="ringOut"/> is obeyed on its own account, whatever
    /// <see cref="RingOutReplacedChildren"/> says.
    /// </remarks>
    public void ClearLayer(int channel, bool ringOut)
    {
        var index = Index(channel);

        if (ringOut)
        {
            Retire(layers[index]);
        }

        layers[index] = null;
    }

    /// <summary>The child a channel is routed to, or null when there is none to hand back yet.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <returns>
    /// The child playing that channel. Null when NOTHING is routed to it, and null as well when a
    /// factory is routed to it whose child has not been built yet - <see cref="IsRouted"/> tells
    /// the two apart, and the child appears here as soon as the channel is first played.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    public IMidiSynthesizer GetChannel(int channel) => SynthesizerOf(routes[Index(channel)]);

    /// <summary>The layer over a channel, or null when there is none to hand back yet.</summary>
    /// <param name="channel">The MIDI channel, 1 to 16.</param>
    /// <returns>
    /// The layer over that channel. Null when the channel has NO layer, and null as well when a
    /// factory is routed as its layer whose child has not been built yet - <see cref="HasLayer"/>
    /// tells the two apart.
    /// </returns>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="channel"/> is outside 1 to 16.</exception>
    public IMidiSynthesizer GetLayer(int channel) => SynthesizerOf(layers[Index(channel)]);

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
    /// <remarks>
    /// An IMMEDIATE note-off-all drops every retired child as well, because the caller is asking
    /// for silence now and a ring-out is the opposite of that. A releasing one leaves them alone:
    /// each was released when it was retired, and telling it again would say nothing new.
    /// </remarks>
    public void NoteOffAll(bool immediate)
    {
        foreach (var route in AllRoutes())
        {
            // Only what already exists: building a synthesizer in order to silence it would be a
            // strange way to spend a hundred megabytes.
            route.Synthesizer?.NoteOffAll(immediate);
        }

        if (immediate)
        {
            retired.Clear();
        }
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns every child that has been created to its initial state and clears
    /// <see cref="UnroutedMessageCount"/>. The routing table is configuration rather than state, so
    /// it is left alone: a reset router plays the same arrangement from the beginning. Retired
    /// children are DROPPED rather than reset - they belong to the performance that has just been
    /// abandoned, not to the one about to start.
    /// </remarks>
    public void Reset()
    {
        foreach (var route in AllRoutes())
        {
            route.Synthesizer?.Reset();
        }

        retired.Clear();
        unroutedMessageCount = 0;
    }

    /// <inheritdoc />
    /// <remarks>
    /// <para>
    /// Each created child renders the frames asked for - whatever its own block size - and is
    /// mixed in at its gain; the sum is then scaled by <see cref="MasterVolume"/>. A channel routed
    /// lazily and never played is SKIPPED rather than built, because a synthesizer that has been
    /// sent no message renders silence anyway - which is what lets a rendition over a large sample
    /// library cost only the parts the music uses.
    /// </para>
    /// <para>
    /// Retired children are rendered here too, after the routing table and at the gain each of them
    /// had, and this is where one of them is let go: when its voices have finished and its output
    /// has been silent for a moment, or when <see cref="RingOutLimit"/> is up.
    /// </para>
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

        // THE AUDIO THREAD ALLOCATES NOTHING. The routing table is walked by index here rather than
        // through AllRoutes(): that method is a `yield` iterator, and every call to it allocates an
        // enumerator object. On this path that was one allocation per audio callback - about 2 MB of
        // garbage per 45 s of playback - which is a garbage collection waiting to happen in the one
        // place a collection is audible.
        for (var channel = 0; channel < ChannelCount; channel++)
        {
            RenderRoute(routes[channel], left, right, frames);
            RenderRoute(layers[channel], left, right, frames);
        }

        RenderRetired(left, right, frames);

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

    // Renders one route (a channel's synthesizer or its layer) into the scratch buffers and mixes it
    // in at the route's gain. An empty slot, or a route whose synthesizer has not been made yet, is
    // silent and costs nothing.
    private void RenderRoute(Route route, Span<float> left, Span<float> right, int frames)
    {
        if (route == null)
        {
            return;
        }

        var synthesizer = route.Synthesizer;

        if (synthesizer == null)
        {
            return;
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

    // Mixes in whatever is still ringing out, and lets go of whatever has finished. Walked
    // backwards so that a child leaving does not move the ones not yet rendered.
    private void RenderRetired(Span<float> left, Span<float> right, int frames)
    {
        if (retired.Count == 0)
        {
            return;
        }

        var limitFrames = (long)(ringOutLimit.TotalSeconds * sampleRate);
        var silenceFrames = (long)sampleRate * SilenceMilliseconds / 1000L;

        for (var index = retired.Count - 1; index >= 0; index--)
        {
            var child = retired[index];
            var scratchL = scratchLeft.AsSpan(0, frames);
            var scratchR = scratchRight.AsSpan(0, frames);

            child.Synthesizer.Render(scratchL, scratchR);

            var gain = child.Gain;
            var silent = child.SilentFrames;

            for (var frame = 0; frame < frames; frame++)
            {
                var leftSample = scratchL[frame];
                var rightSample = scratchR[frame];

                left[frame] += gain * leftSample;
                right[frame] += gain * rightSample;

                if (Math.Abs(leftSample) > SilenceLevel || Math.Abs(rightSample) > SilenceLevel)
                {
                    silent = 0;
                }
                else
                {
                    silent++;
                }
            }

            child.SilentFrames = silent;
            child.FramesRendered += frames;

            // THE VOICES AND THE OUTPUT, both: a child carrying a reverb is still audible after its
            // last voice has ended, and a child whose voices are in a silent stage of an envelope
            // has not finished. The limit is the backstop under the pair of them.
            var finished = child.Synthesizer.ActiveVoiceCount == 0 && silent >= silenceFrames;

            if (finished || child.FramesRendered >= limitFrames)
            {
                retired.RemoveAt(index);
            }
        }
    }

    // Retires the child a replacement has displaced, when replacements are told to ring out.
    private void RetireReplaced(Route route)
    {
        if (ringOutReplacedChildren)
        {
            Retire(route);
        }
    }

    // Takes a child out of the routing table and into the ring-out list. A route whose lazy factory
    // never ran has nothing sounding, so there is nothing to retire.
    private void Retire(Route route)
    {
        var synthesizer = route == null ? null : route.Synthesizer;

        if (synthesizer == null)
        {
            return;
        }

        // A RELEASE, not a cut: the notes let go of the key, so their release stages and whatever
        // the child's own effects are still carrying play out rather than stopping dead.
        synthesizer.NoteOffAll(immediate: false);
        retired.Add(new RetiredChild(synthesizer, route.Gain));
    }

    // Brings a retired instance back: it is about to be routed again, and a child may be rendered
    // only once per block.
    private void Recall(IMidiSynthesizer synthesizer)
    {
        for (var index = 0; index < retired.Count; index++)
        {
            if (ReferenceEquals(retired[index].Synthesizer, synthesizer))
            {
                retired.RemoveAt(index);
                return;
            }
        }
    }

    // Whether the slot already holds this very instance, in which case the call is a gain update
    // rather than a replacement: there is nothing to retire and nothing to build.
    private static bool TrySetGainOfSameChild(Route route, IMidiSynthesizer synthesizer, float gain)
    {
        if (route == null || synthesizer == null || !ReferenceEquals(route.Synthesizer, synthesizer))
        {
            return false;
        }

        route.Gain = gain;
        return true;
    }

    private static IMidiSynthesizer SynthesizerOf(Route route) => route == null ? null : route.Synthesizer;

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

        // Nothing above this point can still throw, so a retired instance being routed again comes
        // out of retirement here: it is about to be rendered from the table instead.
        Recall(synthesizer);

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

    // A child that has left the routing table and is still sounding: mixed at the gain it had,
    // with the two counters that decide when it has nothing left to say.
    private sealed class RetiredChild
    {
        internal RetiredChild(IMidiSynthesizer synthesizer, float gain)
        {
            Synthesizer = synthesizer;
            Gain = gain;
        }

        internal IMidiSynthesizer Synthesizer { get; }

        internal float Gain { get; }

        // How long it has been retired, in frames, against the ring-out limit.
        internal long FramesRendered { get; set; }

        // How many frames it has been silent for without a break.
        internal long SilentFrames { get; set; }
    }
}
