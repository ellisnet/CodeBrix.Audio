using System;
using System.Linq;
using CodeBrix.Audio.Midi;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

/// <summary>
/// Covers <see cref="RoutingSynthesizer"/> - one synthesizer made of others, which is what lets a
/// voiced, per-part arrangement be driven by a sequencer that only knows how to drive one.
/// </summary>
/// <remarks>
/// The children here are mostly <see cref="RecordingSynthesizer"/> instances rendering a flat
/// level, so a gain is arithmetic rather than a matter of opinion. The one test that renders real
/// audio compares two renders made on THIS machine in THIS run against each other, never against
/// pinned values - see MAINTAINER-README, "PINNED RENDERS AND THE PLATFORM MATHS LIBRARY".
/// </remarks>
public class RoutingSynthesizerTests
{
    private const int Rate = 44100;

    // ----- routing -----

    [Fact]
    public void ProcessMidiMessage_reaches_only_the_child_routed_to_that_channel()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        var first = new RecordingSynthesizer();
        var drums = new RecordingSynthesizer();
        router.SetChannel(1, first);
        router.SetChannel(GeneralMidi.PercussionChannel, drums);

        //Act
        router.ProcessMidiMessage(0, 0x90, 60, 100);
        router.ProcessMidiMessage(9, 0x90, 38, 110);

        //Assert
        first.Messages.Should().Equal(new RecordedMessage(0, 0x90, 60, 100));
        drums.Messages.Should().Equal(new RecordedMessage(9, 0x90, 38, 110));
    }

    [Fact]
    public void ProcessMidiMessage_forwards_the_wire_channel_unchanged()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        var child = new RecordingSynthesizer();
        router.SetChannel(4, child);

        //Act
        router.ProcessMidiMessage(3, 0xB0, 7, 90);

        //Assert
        // A child keeps its own per-channel controller state, and a SoundFont child still reads
        // wire channel 9 as percussion - renumbering would break both.
        child.Messages.Single().Channel.Should().Be(3);
    }

    [Fact]
    public void ProcessMidiMessage_drops_and_counts_a_message_for_an_unrouted_channel()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        router.SetChannel(1, new RecordingSynthesizer());

        //Act
        router.ProcessMidiMessage(5, 0x90, 60, 100);
        router.ProcessMidiMessage(5, 0x80, 60, 0);

        //Assert
        // Silence is the right answer - a part nobody voiced must not stop the music - but the
        // count is what lets a diagnostic say the part was played and nothing was listening.
        router.UnroutedMessageCount.Should().Be(2);
    }

    [Fact]
    public void Reset_clears_the_unrouted_message_count()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        router.ProcessMidiMessage(0, 0x90, 60, 100);

        //Act
        router.Reset();

        //Assert
        router.UnroutedMessageCount.Should().Be(0);
    }

    [Fact]
    public void ClearChannel_removes_a_channel_and_its_layer()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        router.SetChannel(2, new RecordingSynthesizer());
        router.SetLayer(2, new RecordingSynthesizer());

        //Act
        router.ClearChannel(2);

        //Assert
        router.IsRouted(2).Should().BeFalse();
        router.HasLayer(2).Should().BeFalse();
    }

    [Fact]
    public void ClearLayer_leaves_the_channel_itself_routed()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        router.SetChannel(2, new RecordingSynthesizer());
        router.SetLayer(2, new RecordingSynthesizer());

        //Act
        router.ClearLayer(2);

        //Assert
        router.IsRouted(2).Should().BeTrue();
        router.HasLayer(2).Should().BeFalse();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(17)]
    [InlineData(-1)]
    public void the_routing_table_is_addressed_one_to_sixteen(int channel)
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);

        //Act
        var act = () => router.SetChannel(channel, new RecordingSynthesizer());

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ----- mixing -----

    [Fact]
    public void Render_applies_each_child_its_own_gain()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        router.SetChannel(1, new RecordingSynthesizer(level: 0.4F), gain: 0.5F);
        router.SetChannel(2, new RecordingSynthesizer(level: 0.1F), gain: 2.0F);

        //Act
        var mix = Render(router, frames: 8);

        //Assert
        mix.Left[0].Should().BeApproximately(0.4F * 0.5F + 0.1F * 2.0F, 1e-6F);
        mix.Right[0].Should().BeApproximately(0.4F * 0.5F + 0.1F * 2.0F, 1e-6F);
    }

    [Fact]
    public void Render_mixes_a_layered_second_child_over_the_first()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        router.SetChannel(3, new RecordingSynthesizer(level: 0.2F), gain: 1.0F);
        router.SetLayer(3, new RecordingSynthesizer(level: 0.2F), gain: 0.25F);

        //Act
        var mix = Render(router, frames: 8);

        //Assert
        mix.Left[0].Should().BeApproximately(0.2F + 0.2F * 0.25F, 1e-6F);
    }

    [Fact]
    public void a_layer_hears_the_same_messages_as_the_child_it_layers()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        var main = new RecordingSynthesizer();
        var layer = new RecordingSynthesizer();
        router.SetChannel(3, main);
        router.SetLayer(3, layer);

        //Act
        router.ProcessMidiMessage(2, 0x90, 64, 100);

        //Assert
        layer.Messages.Should().Equal(main.Messages);
    }

    [Fact]
    public void MasterVolume_scales_the_whole_mix()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        router.SetChannel(1, new RecordingSynthesizer(level: 0.5F));
        router.SetChannel(2, new RecordingSynthesizer(level: 0.5F));
        router.MasterVolume = 0.5F;

        //Act
        var mix = Render(router, frames: 8);

        //Assert
        mix.Left[0].Should().BeApproximately((0.5F + 0.5F) * 0.5F, 1e-6F);
    }

    [Fact]
    public void MasterVolume_starts_at_unity()
    {
        //Arrange & Act
        var router = new RoutingSynthesizer(Rate);

        //Assert
        // A child already renders at whatever level its own engine produces; halving it here by
        // default would quietly re-balance every arrangement.
        router.MasterVolume.Should().Be(1.0F);
    }

    [Fact]
    public void Render_mixes_children_whose_block_sizes_do_not_line_up()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate, blockSize: 64);
        router.SetChannel(1, new RecordingSynthesizer(level: 0.25F, blockSize: 7));
        router.SetChannel(2, new RecordingSynthesizer(level: 0.25F, blockSize: 512));

        //Act
        var mix = Render(router, frames: 100);

        //Assert
        // Each child blocks internally however it likes; the router asks every one of them for
        // exactly the frames the caller wants.
        foreach (var sample in mix.Left)
        {
            sample.Should().BeApproximately(0.5F, 1e-6F);
        }
    }

    [Fact]
    public void Render_of_an_empty_router_is_silence()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);

        //Act
        var mix = Render(router, frames: 16);

        //Assert
        mix.Left.Max(Math.Abs).Should().Be(0.0F);
        mix.Right.Max(Math.Abs).Should().Be(0.0F);
    }

    [Fact]
    public void Render_overwrites_whatever_was_in_the_buffers()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        router.SetChannel(1, new RecordingSynthesizer(level: 0.1F));
        var left = Enumerable.Repeat(9.0F, 8).ToArray();
        var right = Enumerable.Repeat(9.0F, 8).ToArray();

        //Act
        router.Render(left, right);

        //Assert
        left[0].Should().BeApproximately(0.1F, 1e-6F);
    }

    [Fact]
    public void Render_rejects_buffers_of_different_lengths()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);

        //Act
        var act = () => router.Render(new float[8], new float[4]);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    // ----- gains -----

    [Fact]
    public void the_gain_of_a_channel_and_of_its_layer_can_be_read_and_changed()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        router.SetChannel(1, new RecordingSynthesizer(level: 1.0F), gain: 0.5F);
        router.SetLayer(1, new RecordingSynthesizer(level: 1.0F), gain: 0.25F);

        //Act
        router.GetChannelGain(1).Should().Be(0.5F);
        router.GetLayerGain(1).Should().Be(0.25F);
        router.SetChannelGain(1, 0.125F);
        router.SetLayerGain(1, 0.0625F);

        //Assert
        var mix = Render(router, frames: 4);
        mix.Left[0].Should().BeApproximately(0.125F + 0.0625F, 1e-6F);
    }

    [Fact]
    public void an_unrouted_channel_has_no_gain_to_read_or_set()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);

        //Act
        var setChannel = () => router.SetChannelGain(4, 0.5F);
        var setLayer = () => router.SetLayerGain(4, 0.5F);

        //Assert
        router.GetChannelGain(4).Should().Be(0.0F);
        router.GetLayerGain(4).Should().Be(0.0F);
        setChannel.Should().Throw<InvalidOperationException>();
        setLayer.Should().Throw<InvalidOperationException>();
    }

    // ----- lazy children -----

    [Fact]
    public void a_lazy_child_is_not_built_until_its_channel_is_played()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        var built = 0;
        router.SetChannel(1, () => { built++; return new RecordingSynthesizer(); });

        //Act
        var afterRouting = built;
        Render(router, frames: 32);
        var afterRendering = built;
        router.ProcessMidiMessage(0, 0x90, 60, 100);

        //Assert
        // Rendering alone must not build it: a rendition over a multi-hundred-megabyte library
        // pays only for the parts the music actually uses.
        afterRouting.Should().Be(0);
        afterRendering.Should().Be(0);
        built.Should().Be(1);
    }

    [Fact]
    public void a_lazy_child_is_built_only_once()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        var built = 0;
        router.SetChannel(1, () => { built++; return new RecordingSynthesizer(level: 0.5F); });

        //Act
        router.ProcessMidiMessage(0, 0x90, 60, 100);
        router.ProcessMidiMessage(0, 0x80, 60, 0);
        Render(router, frames: 8);

        //Assert
        built.Should().Be(1);
    }

    [Fact]
    public void a_lazy_child_sounds_once_it_has_been_built()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        router.SetChannel(1, () => new RecordingSynthesizer(level: 0.5F), gain: 0.5F);

        //Act
        var beforePlaying = Render(router, frames: 4);
        router.ProcessMidiMessage(0, 0x90, 60, 100);
        var afterPlaying = Render(router, frames: 4);

        //Assert
        beforePlaying.Left[0].Should().Be(0.0F);
        afterPlaying.Left[0].Should().BeApproximately(0.25F, 1e-6F);
    }

    [Fact]
    public void a_lazy_factory_that_returns_nothing_is_an_error()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        router.SetChannel(1, () => (IMidiSynthesizer)null);

        //Act
        var act = () => router.ProcessMidiMessage(0, 0x90, 60, 100);

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void a_lazy_factory_that_builds_the_wrong_sample_rate_is_an_error()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        router.SetChannel(1, () => new RecordingSynthesizer(sampleRate: 22050));

        //Act
        var act = () => router.ProcessMidiMessage(0, 0x90, 60, 100);

        //Assert
        act.Should().Throw<InvalidOperationException>().WithMessage("*sample rate*");
    }

    [Fact]
    public void Synthesizers_lists_only_the_children_that_have_been_built()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        var eager = new RecordingSynthesizer();
        router.SetChannel(1, eager);
        router.SetChannel(2, () => new RecordingSynthesizer());

        //Act
        var beforePlaying = router.Synthesizers.ToArray();
        router.ProcessMidiMessage(1, 0x90, 60, 100);

        //Assert
        beforePlaying.Should().HaveCount(1);
        beforePlaying[0].Should().BeSameAs(eager);
        router.Synthesizers.Should().HaveCount(2);
    }

    // ----- validation -----

    [Fact]
    public void a_child_must_share_the_router_sample_rate()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);

        //Act
        var act = () => router.SetChannel(1, new RecordingSynthesizer(sampleRate: 22050));

        //Assert
        act.Should().Throw<ArgumentException>().WithMessage("*sample rate*");
    }

    [Fact]
    public void one_synthesizer_may_not_fill_two_slots()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        var child = new RecordingSynthesizer();
        router.SetChannel(1, child);

        //Act
        var onAnotherChannel = () => router.SetChannel(2, child);
        var asALayer = () => router.SetLayer(1, child);

        //Assert
        // A child is rendered once per block at one gain, so an instance in two slots would either
        // be counted twice or mixed at the wrong level.
        onAnotherChannel.Should().Throw<ArgumentException>().WithMessage("*already routed*");
        asALayer.Should().Throw<ArgumentException>().WithMessage("*already routed*");
    }

    [Fact]
    public void a_router_rejects_a_sample_rate_or_block_size_that_is_not_positive()
    {
        //Act
        var badRate = () => new RoutingSynthesizer(0);
        var badBlock = () => new RoutingSynthesizer(Rate, 0);

        //Assert
        badRate.Should().Throw<ArgumentOutOfRangeException>();
        badBlock.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void a_router_reports_the_rate_and_block_size_it_was_built_with()
    {
        //Arrange & Act
        var router = new RoutingSynthesizer(48000, 128);

        //Assert
        router.SampleRate.Should().Be(48000);
        router.BlockSize.Should().Be(128);
        RoutingSynthesizer.ChannelCount.Should().Be(16);
    }

    [Fact]
    public void SetChannel_and_SetLayer_reject_a_null_child_or_factory()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);

        //Act
        var nullChild = () => router.SetChannel(1, (IMidiSynthesizer)null);
        var nullFactory = () => router.SetChannel(1, (Func<IMidiSynthesizer>)null);
        var nullLayer = () => router.SetLayer(1, (IMidiSynthesizer)null);
        var nullLayerFactory = () => router.SetLayer(1, (Func<IMidiSynthesizer>)null);

        //Assert
        nullChild.Should().Throw<ArgumentNullException>();
        nullFactory.Should().Throw<ArgumentNullException>();
        nullLayer.Should().Throw<ArgumentNullException>();
        nullLayerFactory.Should().Throw<ArgumentNullException>();
    }

    // ----- fan-out -----

    [Fact]
    public void NoteOffAll_reaches_every_child_that_exists()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        var main = new RecordingSynthesizer();
        var layer = new RecordingSynthesizer();
        var lazyBuilt = 0;
        router.SetChannel(1, main);
        router.SetLayer(1, layer);
        router.SetChannel(2, () => { lazyBuilt++; return new RecordingSynthesizer(); });

        //Act
        router.NoteOffAll(immediate: true);

        //Assert
        main.NoteOffAllCount.Should().Be(1);
        layer.NoteOffAllCount.Should().Be(1);
        lazyBuilt.Should().Be(0);
    }

    [Fact]
    public void Reset_reaches_every_child_that_exists_and_leaves_the_routing_alone()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        var main = new RecordingSynthesizer();
        router.SetChannel(1, main);

        //Act
        router.Reset();

        //Assert
        main.ResetCount.Should().Be(1);
        router.IsRouted(1).Should().BeTrue();
    }

    [Fact]
    public void ActiveVoiceCount_adds_up_the_children()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        var first = new RecordingSynthesizer { ActiveVoiceCount = 3 };
        var second = new RecordingSynthesizer { ActiveVoiceCount = 4 };
        router.SetChannel(1, first);
        router.SetLayer(1, second);

        //Act & Assert
        router.ActiveVoiceCount.Should().Be(7);
    }

    // ----- the offline render (D28) -----

    [Fact]
    public void an_offline_render_through_the_router_matches_the_same_parts_mixed_by_hand()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var sequence = BuildTwoPartSequence();
        const float melodyGain = 0.75F;
        const float harmonyGain = 0.4F;

        var combined = new RoutingSynthesizer(Rate);
        combined.SetChannel(1, new SoundFontSynthesizer(soundFont, Rate), melodyGain);
        combined.SetChannel(2, new SoundFontSynthesizer(soundFont, Rate), harmonyGain);

        var melodyOnly = new RoutingSynthesizer(Rate);
        melodyOnly.SetChannel(1, new SoundFontSynthesizer(soundFont, Rate));

        var harmonyOnly = new RoutingSynthesizer(Rate);
        harmonyOnly.SetChannel(2, new SoundFontSynthesizer(soundFont, Rate));

        //Act
        var throughTheRouter = SoundFontRenderer.Render(combined, sequence, TimeSpan.FromSeconds(0.5));
        var melody = SoundFontRenderer.Render(melodyOnly, sequence, TimeSpan.FromSeconds(0.5));
        var harmony = SoundFontRenderer.Render(harmonyOnly, sequence, TimeSpan.FromSeconds(0.5));

        //Assert
        // Two renders taken on THIS machine in THIS run, compared with each other - never a digest
        // pinned across platforms, which the maths library would break.
        throughTheRouter.Length.Should().Be(melody.Length);
        throughTheRouter.Max(Math.Abs).Should().BeGreaterThan(0.0001F);

        for (var index = 0; index < throughTheRouter.Length; index++)
        {
            var byHand = melodyGain * melody[index] + harmonyGain * harmony[index];

            ((double)throughTheRouter[index]).Should().BeApproximately(
                byHand,
                PinnedRender.SampleTolerance,
                "sample {0} must be the two parts mixed at their own gains",
                index);
        }
    }

    [Fact]
    public void an_offline_render_through_the_router_hears_every_routed_part()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var sequence = BuildTwoPartSequence();

        var bothParts = new RoutingSynthesizer(Rate);
        bothParts.SetChannel(1, new SoundFontSynthesizer(soundFont, Rate));
        bothParts.SetChannel(2, new SoundFontSynthesizer(soundFont, Rate));

        var onePart = new RoutingSynthesizer(Rate);
        onePart.SetChannel(1, new SoundFontSynthesizer(soundFont, Rate));

        //Act
        var withBoth = SoundFontRenderer.Render(bothParts, sequence, TimeSpan.FromSeconds(0.5));
        var withOne = SoundFontRenderer.Render(onePart, sequence, TimeSpan.FromSeconds(0.5));

        //Assert
        withBoth.Should().NotEqual(withOne);
        onePart.UnroutedMessageCount.Should().BeGreaterThan(0);
    }

    // ----- replacing a child, and letting it ring out -----

    [Fact]
    public void replacing_a_child_keeps_it_in_the_mix_until_it_has_finished()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        var first = new RecordingSynthesizer(level: 0.5F) { ActiveVoiceCount = 2 };
        router.SetChannel(1, first, gain: 0.5F);

        //Act
        router.SetChannel(1, new RecordingSynthesizer(level: 0.0F));
        var mix = Render(router, frames: 8);

        //Assert
        // The replaced child is RELEASED rather than cut, and goes on being mixed at the gain it
        // had - so the part it was playing does not stop dead at the seam.
        first.NoteOffAllCount.Should().Be(1);
        router.RetiredChildCount.Should().Be(1);
        mix.Left[0].Should().BeApproximately(0.25F, 1e-6F);
    }

    [Fact]
    public void a_retired_child_is_released_once_its_voices_and_its_tail_are_gone()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        var first = new RecordingSynthesizer(level: 0.5F) { ActiveVoiceCount = 2 };
        router.SetChannel(1, first);
        router.SetChannel(1, new RecordingSynthesizer(level: 0.0F));

        //Act
        // Its voices end, but it is still carrying a tail - so it stays.
        first.ActiveVoiceCount = 0;
        Render(router, frames: Rate / 4);
        var whileTheTailRings = router.RetiredChildCount;

        // And now the tail has gone too.
        first.Level = 0.0F;
        Render(router, frames: Rate / 4);

        //Assert
        whileTheTailRings.Should().Be(1);
        router.RetiredChildCount.Should().Be(0);
    }

    [Fact]
    public void a_retired_child_is_always_released_by_the_ring_out_limit()
    {
        //Arrange - a child that never stops sounding and never runs out of voices.
        var router = new RoutingSynthesizer(Rate) { RingOutLimit = TimeSpan.FromMilliseconds(10) };
        var first = new RecordingSynthesizer(level: 0.5F) { ActiveVoiceCount = 4 };
        router.SetChannel(1, first);
        router.SetChannel(1, new RecordingSynthesizer(level: 0.0F));

        //Act
        Render(router, frames: (Rate / 100) + 1);

        //Assert
        // Nothing may go on costing CPU for ever, whatever it claims to still be sounding.
        router.RetiredChildCount.Should().Be(0);
        router.RingOutLimit.Should().Be(TimeSpan.FromMilliseconds(10));
    }

    [Fact]
    public void the_ring_out_limit_must_not_be_negative()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);

        //Act
        var act = () => router.RingOutLimit = TimeSpan.FromSeconds(-1);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
        router.RingOutLimit.Should().Be(RoutingSynthesizer.DefaultRingOutLimit);
    }

    [Fact]
    public void with_the_ring_out_switched_off_a_replaced_child_leaves_the_mix_at_once()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate) { RingOutReplacedChildren = false };
        var first = new RecordingSynthesizer(level: 0.5F) { ActiveVoiceCount = 2 };
        router.SetChannel(1, first);

        //Act
        router.SetChannel(1, new RecordingSynthesizer(level: 0.0F));
        var mix = Render(router, frames: 8);

        //Assert
        // Exactly what a router did before the ring-out existed: the old child is simply gone.
        router.RetiredChildCount.Should().Be(0);
        first.NoteOffAllCount.Should().Be(0);
        mix.Left[0].Should().Be(0.0F);
    }

    [Fact]
    public void a_replaced_layer_rings_out_the_same_way()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        var layer = new RecordingSynthesizer(level: 0.5F);
        router.SetChannel(1, new RecordingSynthesizer(level: 0.0F));
        router.SetLayer(1, layer, gain: 0.5F);

        //Act
        router.SetLayer(1, new RecordingSynthesizer(level: 0.0F));
        var mix = Render(router, frames: 8);

        //Assert
        router.RetiredChildCount.Should().Be(1);
        mix.Left[0].Should().BeApproximately(0.25F, 1e-6F);
    }

    [Fact]
    public void a_lazy_route_that_was_never_built_has_nothing_to_retire()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        router.SetChannel(1, () => new RecordingSynthesizer());

        //Act
        router.SetChannel(1, new RecordingSynthesizer());

        //Assert
        // Nothing was ever built, so nothing was ever sounding.
        router.RetiredChildCount.Should().Be(0);
    }

    [Fact]
    public void ClearChannel_is_immediate_by_default_and_rings_out_when_asked()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        var cut = new RecordingSynthesizer(level: 0.5F);
        var rung = new RecordingSynthesizer(level: 0.5F);
        router.SetChannel(1, cut);
        router.SetChannel(2, rung);

        //Act
        router.ClearChannel(1);
        var afterTheCut = router.RetiredChildCount;
        router.ClearChannel(2, ringOut: true);

        //Assert
        // "So the channel goes silent" is what ClearChannel has always promised, and it still does.
        afterTheCut.Should().Be(0);
        cut.NoteOffAllCount.Should().Be(0);
        router.RetiredChildCount.Should().Be(1);
        rung.NoteOffAllCount.Should().Be(1);
        router.IsRouted(2).Should().BeFalse();
    }

    [Fact]
    public void ClearLayer_is_immediate_by_default_and_rings_out_when_asked()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        router.SetChannel(1, new RecordingSynthesizer());
        var layer = new RecordingSynthesizer(level: 0.5F);
        router.SetLayer(1, layer);

        //Act
        router.ClearLayer(1, ringOut: true);

        //Assert
        router.HasLayer(1).Should().BeFalse();
        router.RetiredChildCount.Should().Be(1);
        layer.NoteOffAllCount.Should().Be(1);
    }

    [Fact]
    public void a_ring_out_asked_for_by_name_happens_even_with_the_switch_off()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate) { RingOutReplacedChildren = false };
        router.SetChannel(1, new RecordingSynthesizer(level: 0.5F));

        //Act
        router.ClearChannel(1, ringOut: true);

        //Assert
        // The switch governs REPLACEMENT; an explicit request is a request.
        router.RetiredChildCount.Should().Be(1);
    }

    [Fact]
    public void the_same_instance_back_into_its_own_slot_is_a_gain_update()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        var child = new RecordingSynthesizer(level: 0.4F);
        router.SetChannel(1, child, gain: 1.0F);

        //Act
        router.SetChannel(1, child, gain: 0.5F);
        var mix = Render(router, frames: 4);

        //Assert
        // Nothing was replaced, so nothing was retired and the child never heard a note-off.
        router.GetChannelGain(1).Should().Be(0.5F);
        router.RetiredChildCount.Should().Be(0);
        child.NoteOffAllCount.Should().Be(0);
        router.Synthesizers.Should().ContainSingle();
        mix.Left[0].Should().BeApproximately(0.2F, 1e-6F);
    }

    [Fact]
    public void the_same_layer_back_into_its_own_slot_is_a_gain_update()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        var layer = new RecordingSynthesizer(level: 0.4F);
        router.SetChannel(1, new RecordingSynthesizer(level: 0.0F));
        router.SetLayer(1, layer, gain: 1.0F);

        //Act
        router.SetLayer(1, layer, gain: 0.25F);

        //Assert
        router.GetLayerGain(1).Should().Be(0.25F);
        router.RetiredChildCount.Should().Be(0);
    }

    [Fact]
    public void a_retired_instance_routed_again_comes_out_of_retirement()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        var child = new RecordingSynthesizer(level: 0.4F) { ActiveVoiceCount = 1 };
        router.SetChannel(1, child);
        router.SetChannel(1, new RecordingSynthesizer(level: 0.0F));

        //Act
        var whileRetired = router.RetiredChildCount;
        router.SetChannel(2, child, gain: 0.5F);
        var mix = Render(router, frames: 4);

        //Assert
        // And it is rendered ONCE - from the table - rather than once there and once in the mix of
        // what is ringing out.
        whileRetired.Should().Be(1);
        router.RetiredChildCount.Should().Be(0);
        router.GetChannel(2).Should().BeSameAs(child);
        mix.Left[0].Should().BeApproximately(0.2F, 1e-6F);
    }

    [Fact]
    public void an_immediate_NoteOffAll_drops_every_retired_child()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        router.SetChannel(1, new RecordingSynthesizer(level: 0.5F));
        router.SetChannel(1, new RecordingSynthesizer(level: 0.0F));

        //Act
        router.NoteOffAll(immediate: true);
        var mix = Render(router, frames: 4);

        //Assert
        router.RetiredChildCount.Should().Be(0);
        mix.Left[0].Should().Be(0.0F);
    }

    [Fact]
    public void a_releasing_NoteOffAll_leaves_the_retired_alone()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        router.SetChannel(1, new RecordingSynthesizer(level: 0.5F));
        router.SetChannel(1, new RecordingSynthesizer(level: 0.0F));

        //Act
        router.NoteOffAll(immediate: false);

        //Assert
        // Each was released when it was retired; saying it again would say nothing new.
        router.RetiredChildCount.Should().Be(1);
    }

    [Fact]
    public void Reset_drops_every_retired_child()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        router.SetChannel(1, new RecordingSynthesizer(level: 0.5F));
        router.SetChannel(1, new RecordingSynthesizer(level: 0.0F));

        //Act
        router.Reset();

        //Assert
        // They belong to the performance that has just been abandoned.
        router.RetiredChildCount.Should().Be(0);
    }

    [Fact]
    public void ActiveVoiceCount_counts_what_is_still_ringing_out()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        var retiring = new RecordingSynthesizer { ActiveVoiceCount = 3 };
        router.SetChannel(1, retiring);

        //Act
        router.SetChannel(1, new RecordingSynthesizer { ActiveVoiceCount = 2 });

        //Assert
        router.ActiveVoiceCount.Should().Be(5);
    }

    [Fact]
    public void Synthesizers_does_not_list_a_retired_child()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        var replaced = new RecordingSynthesizer();
        var replacement = new RecordingSynthesizer();
        router.SetChannel(1, replaced);

        //Act
        router.SetChannel(1, replacement);

        //Assert
        // Synthesizers is the routing table, and a retired child has left it.
        router.Synthesizers.Should().ContainSingle();
        router.Synthesizers[0].Should().BeSameAs(replacement);
        router.RetiredChildCount.Should().Be(1);
    }

    // ----- reading the table back -----

    [Fact]
    public void GetChannel_and_GetLayer_hand_back_the_children_that_exist()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        var main = new RecordingSynthesizer();
        var layer = new RecordingSynthesizer();
        router.SetChannel(3, main);
        router.SetLayer(3, layer);

        //Act & Assert
        router.GetChannel(3).Should().BeSameAs(main);
        router.GetLayer(3).Should().BeSameAs(layer);
    }

    [Fact]
    public void GetChannel_is_null_for_an_empty_slot_and_for_a_lazy_child_not_yet_built()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);
        router.SetChannel(2, () => new RecordingSynthesizer());

        //Act
        var beforePlaying = router.GetChannel(2);
        router.ProcessMidiMessage(1, 0x90, 60, 100);

        //Assert
        // IsRouted is what tells "nothing here" from "not built yet"; the child appears as soon as
        // the channel is first played.
        router.GetChannel(1).Should().BeNull();
        router.IsRouted(1).Should().BeFalse();
        beforePlaying.Should().BeNull();
        router.IsRouted(2).Should().BeTrue();
        router.GetChannel(2).Should().NotBeNull();
    }

    [Fact]
    public void GetChannel_and_GetLayer_are_addressed_one_to_sixteen()
    {
        //Arrange
        var router = new RoutingSynthesizer(Rate);

        //Act
        var channel = () => router.GetChannel(17);
        var layer = () => router.GetLayer(0);

        //Assert
        channel.Should().Throw<ArgumentOutOfRangeException>();
        layer.Should().Throw<ArgumentOutOfRangeException>();
    }

    // ----- the ring-out with real audio (D28's render path) -----

    [Fact]
    public void a_child_replaced_under_a_held_note_does_not_cut_the_note_off()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);

        var ringing = new RoutingSynthesizer(Rate);
        ringing.SetChannel(1, new SoundFontSynthesizer(soundFont, Rate));
        ringing.ProcessMidiMessage(0, 0x90, 60, 100);
        Render(ringing, frames: Rate / 10);

        var cut = new RoutingSynthesizer(Rate) { RingOutReplacedChildren = false };
        cut.SetChannel(1, new SoundFontSynthesizer(soundFont, Rate));
        cut.ProcessMidiMessage(0, 0x90, 60, 100);
        Render(cut, frames: Rate / 10);

        //Act
        ringing.SetChannel(1, new SoundFontSynthesizer(soundFont, Rate));
        cut.SetChannel(1, new SoundFontSynthesizer(soundFont, Rate));

        var afterRinging = Render(ringing, frames: Rate / 20);
        var afterCut = Render(cut, frames: Rate / 20);

        //Assert
        // Two renders taken on THIS machine in THIS run, compared with each other rather than with
        // pinned values - see MAINTAINER-README, "PINNED RENDERS AND THE PLATFORM MATHS LIBRARY".
        afterRinging.Left.Max(Math.Abs).Should().BeGreaterThan(1e-3F);
        afterCut.Left.Max(Math.Abs).Should().BeLessThan(1e-6F);
    }

    [Fact]
    public void a_note_released_by_a_replacement_decays_through_its_release()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var router = new RoutingSynthesizer(Rate);
        router.SetChannel(1, new SoundFontSynthesizer(soundFont, Rate));
        router.ProcessMidiMessage(0, 0x90, 60, 100);
        Render(router, frames: Rate / 10);

        //Act
        router.SetChannel(1, new SoundFontSynthesizer(soundFont, Rate));

        var early = Render(router, frames: Rate / 100);
        Render(router, frames: Rate / 2);
        var late = Render(router, frames: Rate / 100);

        //Assert
        // A RELEASE, not an all-sound-off: it is still there, and it is on its way down.
        early.Left.Max(Math.Abs).Should().BeGreaterThan(1e-3F);
        late.Left.Max(Math.Abs).Should().BeLessThan(early.Left.Max(Math.Abs));
    }

    [Fact]
    public void an_offline_render_hears_a_part_re_voiced_in_the_middle_of_the_piece()
    {
        //Arrange
        var soundFont = SynthTestAssets.LoadSoundFont(SynthTestAssets.TestSoundFontName);
        var sequence = BuildTwoPartSequence();

        var ringing = new RoutingSynthesizer(Rate);
        ringing.SetChannel(1, new SoundFontSynthesizer(soundFont, Rate));
        ringing.SetChannel(2, new SoundFontSynthesizer(soundFont, Rate));

        //Act
        var beforeReplacing = SoundFontRenderer.Render(ringing, sequence, TimeSpan.FromSeconds(0.5));

        ringing.SetChannel(1, new SoundFontSynthesizer(soundFont, Rate));
        var afterReplacing = SoundFontRenderer.Render(ringing, sequence, TimeSpan.FromSeconds(0.5));

        //Assert
        // SoundFontRenderer resets the synthesizer before it starts, which lets the retired child
        // go - so the offline path plays the arrangement the table describes and nothing else.
        ringing.RetiredChildCount.Should().Be(0);
        afterReplacing.Length.Should().Be(beforeReplacing.Length);
        afterReplacing.Max(Math.Abs).Should().BeGreaterThan(0.0001F);
    }

    private static MidiSequence BuildTwoPartSequence()
    {
        var collection = new MidiEventCollection(1, 120);

        collection.AddEvent(new NoteOnEvent(0, 1, 60, 100, 240), 1);
        collection.AddEvent(new NoteEvent(240, 1, MidiCommandCode.NoteOff, 60, 0), 1);
        collection.AddEvent(new NoteOnEvent(0, 2, 67, 90, 240), 1);
        collection.AddEvent(new NoteEvent(240, 2, MidiCommandCode.NoteOff, 67, 0), 1);

        collection.PrepareForExport();
        return MidiSequence.FromEvents(collection);
    }

    private static (float[] Left, float[] Right) Render(RoutingSynthesizer router, int frames)
    {
        var left = new float[frames];
        var right = new float[frames];
        router.Render(left, right);
        return (left, right);
    }

    // ----- the audio thread allocates nothing -----

    [Fact]
    public void Render_allocates_nothing_once_it_is_warm()
    {
        //Arrange
        // A managed allocation on the audio thread is a garbage collection waiting to happen. The
        // routing table used to be walked through a `yield` iterator on this path, which allocated
        // an enumerator on every callback. Sixteen channels and a layer each make the walk as long
        // as it gets; the children render a flat level and allocate nothing themselves.
        var router = new RoutingSynthesizer(Rate);
        for (var channel = 1; channel <= 16; channel++)
        {
            router.SetChannel(channel, new RecordingSynthesizer { Level = 0.1F }, 0.5F);
            router.SetLayer(channel, new RecordingSynthesizer { Level = 0.1F }, 0.25F);
        }
        var left = new float[512];
        var right = new float[512];
        for (var i = 0; i < 8; i++)
        {
            router.Render(left, right);
        }

        //Act
        // Allow a few attempts so a stray background event in one window does not fail an
        // allocation-free path; a genuine per-call allocation shows up in every window.
        long allocated = -1;
        for (var attempt = 0; attempt < 5 && allocated != 0; attempt++)
        {
            var before = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < 100; i++)
            {
                router.Render(left, right);
            }
            allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        }

        //Assert
        allocated.Should().Be(0L);
        left[0].Should().BeApproximately(16 * (0.5F * 0.1F + 0.25F * 0.1F), 0.0001F);
    }
}
