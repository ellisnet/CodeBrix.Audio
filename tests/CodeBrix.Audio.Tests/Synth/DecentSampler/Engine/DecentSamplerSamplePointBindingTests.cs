using System;
using CodeBrix.Audio.Synth.DecentSampler;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// SAMPLE_START, SAMPLE_END, LOOP_START and LOOP_END on an IN-MEMORY zone: when a binding that moves
/// one of them takes effect.
/// </summary>
/// <remarks>
/// <para>
/// THE RULE, now measured (round 4, item 54). SAMPLE_START takes effect at the NEXT NOTE-ON and a
/// sounding voice keeps the start it began with. SAMPLE_END takes effect at the next note-on AND on a
/// SOUNDING voice: a voice whose read position is already past the new end stops at once.
/// </para>
/// <para>
/// LOOP_START and LOOP_END do NOTHING AT ALL in the reference player - not live, not at the next
/// note-on, and through none of the three ways a binding can name its target. Here they take effect at
/// the next note-on, which is a published improvement rather than a match. Following them live would
/// mean re-resolving a sounding voice's loop from the audio callback for a parameter presets drive
/// from a knob, and it would match nothing.
/// </para>
/// <para>
/// The streamed case keeps the note-on rule for every one of the four and reports itself once in
/// <see cref="DecentSamplerInstrument.Problems"/>: the guide restricts all four to in-memory playback.
/// </para>
/// </remarks>
public class DecentSamplerSamplePointBindingTests
{
    private const int SectionFrames = 4000;

    private const string StartPreset = """
        <DecentSampler>
          <groups>
            <group ampVelTrack="0" attack="0" decay="0" sustain="1" release="0.001">
              <sample path="Samples/steps.wav" rootNote="60" loNote="0" hiNote="127"
                      pitchKeyTrack="0" />
            </group>
          </groups>
          <ui>
            <tab name="main">
              <labeled-knob x="0" y="0" width="90" height="100" parameterName="Start"
                            minValue="0" maxValue="16000" value="0" triggerOnLoad="false">
                <binding type="general" level="group" position="0" parameter="SAMPLE_START" />
              </labeled-knob>
            </tab>
          </ui>
        </DecentSampler>
        """;

    private const string EndPreset = """
        <DecentSampler>
          <groups>
            <group ampVelTrack="0" attack="0" decay="0" sustain="1" release="0.001">
              <sample path="Samples/steps.wav" rootNote="60" loNote="0" hiNote="127"
                      pitchKeyTrack="0" />
            </group>
          </groups>
          <ui>
            <tab name="main">
              <labeled-knob x="0" y="0" width="90" height="100" parameterName="End"
                            minValue="0" maxValue="16000" value="16000" triggerOnLoad="false">
                <binding type="general" level="group" position="0" parameter="SAMPLE_END" />
              </labeled-knob>
            </tab>
          </ui>
        </DecentSampler>
        """;

    [Fact]
    public void a_sounding_voice_keeps_the_sample_start_it_began_with()
    {
        //Arrange - four sections of distinct level, so the render says which frame the voice is on.
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteSectionedWav("Samples/steps.wav", SectionFrames, 0.1f, 0.2f, 0.3f, 0.4f);

        using var instrument = DecentSamplerInstrument.Load(fixtures.WritePreset(StartPreset));
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 60, 127);
        var before = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 16).Left;

        instrument.Controls[0].SetValue(SectionFrames * 3);
        var after = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 16).Left;

        //Assert - the note goes on reading section one; the knob has not moved it to section four.
        DecentSamplerRenderProbe.Rms(before).Should().BeApproximately(0.1, 0.005);
        DecentSamplerRenderProbe.Rms(after).Should().BeApproximately(0.1, 0.005);
    }

    [Fact]
    public void the_next_note_starts_where_the_binding_moved_it()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteSectionedWav("Samples/steps.wav", SectionFrames, 0.1f, 0.2f, 0.3f, 0.4f);

        using var instrument = DecentSamplerInstrument.Load(fixtures.WritePreset(StartPreset));
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act
        instrument.Controls[0].SetValue(SectionFrames * 3);
        synthesizer.NoteOn(0, 60, 127);
        var render = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 16).Left;

        //Assert - straight into the fourth section.
        DecentSamplerRenderProbe.Rms(render).Should().BeApproximately(0.4, 0.005);
        instrument.Zones[0].Start.Should().Be(SectionFrames * 3);
    }

    [Fact]
    public void a_sounding_voice_past_the_new_sample_end_stops_at_once()
    {
        //Arrange
        // MEASURED (round 4, item 54): the reference sent SAMPLE_END 3.0 s into a held note, and the
        // voice - whose read position was already past the new end - fell silent at exactly 3.0 s.
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteSectionedWav("Samples/steps.wav", SectionFrames, 0.1f, 0.2f, 0.3f, 0.4f);

        using var instrument = DecentSamplerInstrument.Load(fixtures.WritePreset(EndPreset));
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act - 160 blocks is 10,240 frames, the third section of the file.
        synthesizer.NoteOn(0, 60, 127);
        var before = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 160).Left;

        instrument.Controls[0].SetValue(SectionFrames);
        var after = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 16).Left;

        //Assert
        DecentSamplerRenderProbe.Rms(before, before.Length - 1024, 1024)
            .Should().BeApproximately(0.3, 0.005);
        DecentSamplerRenderProbe.Peak(after).Should().Be(0.0);
        synthesizer.ActiveVoiceCount.Should().Be(0);
    }

    [Fact]
    public void a_sounding_voice_short_of_the_new_sample_end_plays_on_to_it()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteSectionedWav("Samples/steps.wav", SectionFrames, 0.1f, 0.2f, 0.3f, 0.4f);

        using var instrument = DecentSamplerInstrument.Load(fixtures.WritePreset(EndPreset));
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act - 16 blocks is 1,024 frames, still inside the first section.
        synthesizer.NoteOn(0, 60, 127);
        DecentSamplerRenderProbe.RenderBlocks(synthesizer, 16);

        instrument.Controls[0].SetValue(SectionFrames);
        var after = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 100).Left;

        //Assert - it sounds on to the new end at frame 4,000 and then stops, well short of the file.
        DecentSamplerRenderProbe.Rms(after, 0, 2816).Should().BeApproximately(0.1, 0.005);
        DecentSamplerRenderProbe.Peak(after, 3200, 3200).Should().Be(0.0);
        synthesizer.ActiveVoiceCount.Should().Be(0);
    }

    [Fact]
    public void the_next_note_stops_where_the_binding_moved_the_sample_end()
    {
        //Arrange
        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteSectionedWav("Samples/steps.wav", SectionFrames, 0.1f, 0.2f, 0.3f, 0.4f);

        using var instrument = DecentSamplerInstrument.Load(fixtures.WritePreset(EndPreset));
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act
        instrument.Controls[0].SetValue(SectionFrames);
        synthesizer.NoteOn(0, 60, 127);
        var render = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 100).Left;

        //Assert
        DecentSamplerRenderProbe.Rms(render, 0, 3840).Should().BeApproximately(0.1, 0.005);
        DecentSamplerRenderProbe.Peak(render, 4160, 2240).Should().Be(0.0);
        instrument.Zones[0].End.Should().Be(SectionFrames);
    }

    [Fact]
    public void a_sounding_voice_keeps_the_loop_it_began_with()
    {
        //Arrange - a two-section loop at the front of a four-section file.
        const string preset = """
            <DecentSampler>
              <groups>
                <group ampVelTrack="0" attack="0" decay="0" sustain="1" release="0.001">
                  <sample path="Samples/steps.wav" rootNote="60" loNote="0" hiNote="127"
                          pitchKeyTrack="0" loopEnabled="true" loopStart="0" loopEnd="3999" />
                </group>
              </groups>
              <ui>
                <tab name="main">
                  <labeled-knob x="0" y="0" width="90" height="100" parameterName="Loop start"
                                minValue="0" maxValue="16000" value="0" triggerOnLoad="false">
                    <binding type="general" level="group" position="0" parameter="LOOP_START" />
                  </labeled-knob>
                  <labeled-knob x="0" y="0" width="90" height="100" parameterName="Loop end"
                                minValue="0" maxValue="16000" value="3999" triggerOnLoad="false">
                    <binding type="general" level="group" position="0" parameter="LOOP_END" />
                  </labeled-knob>
                </tab>
              </ui>
            </DecentSampler>
            """;

        using var fixtures = DecentSamplerEngineFixtures.Create();
        fixtures.WriteSectionedWav("Samples/steps.wav", SectionFrames, 0.1f, 0.2f, 0.3f, 0.4f);

        using var instrument = DecentSamplerInstrument.Load(fixtures.WritePreset(preset));
        var synthesizer = DecentSamplerRenderProbe.Synthesizer(instrument);

        //Act
        synthesizer.NoteOn(0, 60, 127);
        var before = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 32).Left;

        instrument.Controls[0].SetValue(SectionFrames * 3);
        instrument.Controls[1].SetValue((SectionFrames * 4) - 1);
        var after = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 32).Left;

        //Assert - the sounding note stays in its first-section loop.
        DecentSamplerRenderProbe.Rms(before).Should().BeApproximately(0.1, 0.005);
        DecentSamplerRenderProbe.Rms(after).Should().BeApproximately(0.1, 0.005);

        //Act - the next note picks the moved loop up.
        synthesizer.NoteOffAll(true);
        synthesizer.NoteOn(0, 60, 127);
        var moved = DecentSamplerRenderProbe.RenderBlocks(synthesizer, 400).Left;

        //Assert - it runs 0.1, 0.2, 0.3 and then loops inside the fourth section for ever.
        var tail = new float[SectionFrames];
        Array.Copy(moved, moved.Length - SectionFrames, tail, 0, SectionFrames);
        DecentSamplerRenderProbe.Rms(tail).Should().BeApproximately(0.4, 0.005);
    }
}
