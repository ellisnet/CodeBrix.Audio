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
/// THE RULE: a change takes effect at the NEXT NOTE-ON, and a voice that is already sounding keeps the
/// bounds it started with. Nothing about this was measured against the reference player - the developer
/// guide says only that these four parameters need in-memory playback, and says nothing about a
/// sounding voice - so this engine takes the cheaper of the two defensible readings.
/// </para>
/// <para>
/// The reason it is the cheaper one: a voice reads its start and end once, at note-on, and its loop
/// bounds come from a zone runtime that resolves them when the instrument is built. Following a change
/// live would mean re-resolving every sounding voice's loop from the audio thread's own callback, on a
/// parameter that presets move from a knob a player is dragging. The audible difference is a jump in
/// the middle of a note, which is not something a library can rely on either way; the cost is a
/// re-entrant edit of running voices. The streamed case behaves identically and additionally reports
/// itself once in <see cref="DecentSamplerInstrument.Problems"/>.
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
