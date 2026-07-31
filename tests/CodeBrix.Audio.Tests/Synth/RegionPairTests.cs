using System;
using System.Linq;
using CodeBrix.Audio.Synth;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth;

public class RegionPairTests
{
    [Theory]
    [MemberData(nameof(SynthTestAssets.SoundFontNames), MemberType = typeof(SynthTestAssets))]
    public void default_preset_region_does_not_affect(string soundFontName)
    {
        var soundFont = SynthTestAssets.LoadSoundFont(soundFontName);

        foreach (var instrument in soundFont.Instruments)
        {
            foreach (var region in instrument.Regions)
            {
                var pair = new RegionPair(PresetRegion.Default, region);
                AreEqual(region, pair);
            }
        }
    }

    [Theory]
    [MemberData(nameof(SynthTestAssets.SoundFontNames), MemberType = typeof(SynthTestAssets))]
    public void parameter_check(string soundFontName)
    {
        var soundFont = SynthTestAssets.LoadSoundFont(soundFontName);

        foreach (var preset in soundFont.Presets)
        {
            foreach (var presetRegion in preset.Regions)
            {
                foreach (var instrumentRegion in presetRegion.SoundFontInstrument.Regions)
                {
                    var pair = new RegionPair(presetRegion, instrumentRegion);
                    Check(presetRegion, instrumentRegion, pair);
                }
            }
        }
    }

    private static void AreEqual(InstrumentRegion expected, RegionPair actual)
    {
        expected.StartAddressOffset.Should().Be(actual.StartAddressOffset);
        expected.EndAddressOffset.Should().Be(actual.EndAddressOffset);
        expected.StartLoopAddressOffset.Should().Be(actual.StartLoopAddressOffset);
        expected.EndLoopAddressOffset.Should().Be(actual.EndLoopAddressOffset);

        expected.ModulationLfoToPitch.Should().Be(actual.ModulationLfoToPitch);
        expected.VibratoLfoToPitch.Should().Be(actual.VibratoLfoToPitch);
        expected.ModulationEnvelopeToPitch.Should().Be(actual.ModulationEnvelopeToPitch);
        expected.InitialFilterCutoffFrequency.ShouldBeApproximately(actual.InitialFilterCutoffFrequency, 1.0E-6);
        expected.InitialFilterQ.ShouldBeApproximately(actual.InitialFilterQ, 1.0E-6);
        expected.ModulationLfoToFilterCutoffFrequency.Should().Be(actual.ModulationLfoToFilterCutoffFrequency);
        expected.ModulationEnvelopeToFilterCutoffFrequency.Should().Be(actual.ModulationEnvelopeToFilterCutoffFrequency);

        expected.ModulationLfoToVolume.ShouldBeApproximately(actual.ModulationLfoToVolume, 1.0E-6);

        expected.ChorusEffectsSend.ShouldBeApproximately(actual.ChorusEffectsSend, 1.0E-6);
        expected.ReverbEffectsSend.ShouldBeApproximately(actual.ReverbEffectsSend, 1.0E-6);
        expected.Pan.ShouldBeApproximately(actual.Pan, 1.0E-6);

        expected.DelayModulationLfo.ShouldBeApproximately(actual.DelayModulationLfo, 1.0E-6);
        expected.FrequencyModulationLfo.ShouldBeApproximately(actual.FrequencyModulationLfo, 1.0E-6);
        expected.DelayVibratoLfo.ShouldBeApproximately(actual.DelayVibratoLfo, 1.0E-6);
        expected.FrequencyVibratoLfo.ShouldBeApproximately(actual.FrequencyVibratoLfo, 1.0E-6);
        expected.DelayModulationEnvelope.ShouldBeApproximately(actual.DelayModulationEnvelope, 1.0E-6);
        expected.AttackModulationEnvelope.ShouldBeApproximately(actual.AttackModulationEnvelope, 1.0E-6);
        expected.HoldModulationEnvelope.ShouldBeApproximately(actual.HoldModulationEnvelope, 1.0E-6);
        expected.DecayModulationEnvelope.ShouldBeApproximately(actual.DecayModulationEnvelope, 1.0E-6);
        expected.SustainModulationEnvelope.ShouldBeApproximately(actual.SustainModulationEnvelope, 1.0E-6);
        expected.ReleaseModulationEnvelope.ShouldBeApproximately(actual.ReleaseModulationEnvelope, 1.0E-6);
        expected.KeyNumberToModulationEnvelopeHold.Should().Be(actual.KeyNumberToModulationEnvelopeHold);
        expected.KeyNumberToModulationEnvelopeDecay.Should().Be(actual.KeyNumberToModulationEnvelopeDecay);
        expected.DelayVolumeEnvelope.ShouldBeApproximately(actual.DelayVolumeEnvelope, 1.0E-6);
        expected.AttackVolumeEnvelope.ShouldBeApproximately(actual.AttackVolumeEnvelope, 1.0E-6);
        expected.HoldVolumeEnvelope.ShouldBeApproximately(actual.HoldVolumeEnvelope, 1.0E-6);
        expected.DecayVolumeEnvelope.ShouldBeApproximately(actual.DecayVolumeEnvelope, 1.0E-6);
        expected.SustainVolumeEnvelope.ShouldBeApproximately(actual.SustainVolumeEnvelope, 1.0E-6);
        expected.ReleaseVolumeEnvelope.ShouldBeApproximately(actual.ReleaseVolumeEnvelope, 1.0E-6);
        expected.KeyNumberToVolumeEnvelopeHold.Should().Be(actual.KeyNumberToVolumeEnvelopeHold);
        expected.KeyNumberToVolumeEnvelopeDecay.Should().Be(actual.KeyNumberToVolumeEnvelopeDecay);

        expected.InitialAttenuation.ShouldBeApproximately(actual.InitialAttenuation, 1.0E-6);

        expected.CoarseTune.Should().Be(actual.CoarseTune);
        expected.FineTune.Should().Be(actual.FineTune);
        expected.SampleModes.Should().Be(actual.SampleModes);

        expected.ScaleTuning.Should().Be(actual.ScaleTuning);
        expected.ExclusiveClass.Should().Be(actual.ExclusiveClass);
        expected.RootKey.Should().Be(actual.RootKey);
    }

    private static void Check(PresetRegion preset, InstrumentRegion instrument, RegionPair pair)
    {
        instrument.StartAddressOffset.Should().Be(pair.StartAddressOffset);
        instrument.EndAddressOffset.Should().Be(pair.EndAddressOffset);
        instrument.StartLoopAddressOffset.Should().Be(pair.StartLoopAddressOffset);
        instrument.EndLoopAddressOffset.Should().Be(pair.EndLoopAddressOffset);

        (instrument.ModulationLfoToPitch + preset.ModulationLfoToPitch).Should().Be(pair.ModulationLfoToPitch);
        (instrument.VibratoLfoToPitch + preset.VibratoLfoToPitch).Should().Be(pair.VibratoLfoToPitch);
        (instrument.ModulationEnvelopeToPitch + preset.ModulationEnvelopeToPitch).Should().Be(pair.ModulationEnvelopeToPitch);
        (instrument.InitialFilterCutoffFrequency * preset.InitialFilterCutoffFrequency).ShouldBeApproximately(pair.InitialFilterCutoffFrequency, 1.0);
        (instrument.InitialFilterQ + preset.InitialFilterQ).ShouldBeApproximately(pair.InitialFilterQ, 1.0E-3);
        (instrument.ModulationLfoToFilterCutoffFrequency + preset.ModulationLfoToFilterCutoffFrequency).Should().Be(pair.ModulationLfoToFilterCutoffFrequency);
        (instrument.ModulationEnvelopeToFilterCutoffFrequency + preset.ModulationEnvelopeToFilterCutoffFrequency).Should().Be(pair.ModulationEnvelopeToFilterCutoffFrequency);

        (instrument.ModulationLfoToVolume + preset.ModulationLfoToVolume).ShouldBeApproximately(pair.ModulationLfoToVolume, 1.0E-3);

        (instrument.ChorusEffectsSend + preset.ChorusEffectsSend).ShouldBeApproximately(pair.ChorusEffectsSend, 1.0E-3);
        (instrument.ReverbEffectsSend + preset.ReverbEffectsSend).ShouldBeApproximately(pair.ReverbEffectsSend, 1.0E-3);
        (instrument.Pan + preset.Pan).ShouldBeApproximately(pair.Pan, 1.0E-3);

        (instrument.DelayModulationLfo * preset.DelayModulationLfo).ShouldBeApproximately(pair.DelayModulationLfo, 1.0E-3);
        (instrument.FrequencyModulationLfo * preset.FrequencyModulationLfo).ShouldBeApproximately(pair.FrequencyModulationLfo, 1.0E-3);
        (instrument.DelayVibratoLfo * preset.DelayVibratoLfo).ShouldBeApproximately(pair.DelayVibratoLfo, 1.0E-3);
        (instrument.FrequencyVibratoLfo * preset.FrequencyVibratoLfo).ShouldBeApproximately(pair.FrequencyVibratoLfo, 1.0E-3);
        (instrument.DelayModulationEnvelope * preset.DelayModulationEnvelope).ShouldBeApproximately(pair.DelayModulationEnvelope, 1.0E-3);
        (instrument.AttackModulationEnvelope * preset.AttackModulationEnvelope).ShouldBeApproximately(pair.AttackModulationEnvelope, 1.0E-3);
        (instrument.HoldModulationEnvelope * preset.HoldModulationEnvelope).ShouldBeApproximately(pair.HoldModulationEnvelope, 1.0E-3);
        (instrument.DecayModulationEnvelope * preset.DecayModulationEnvelope).ShouldBeApproximately(pair.DecayModulationEnvelope, 1.0E-3);
        (instrument.SustainModulationEnvelope + preset.SustainModulationEnvelope).ShouldBeApproximately(pair.SustainModulationEnvelope, 1.0E-2);
        (instrument.ReleaseModulationEnvelope * preset.ReleaseModulationEnvelope).ShouldBeApproximately(pair.ReleaseModulationEnvelope, 1.0E-2);
        (instrument.KeyNumberToModulationEnvelopeHold + preset.KeyNumberToModulationEnvelopeHold).Should().Be(pair.KeyNumberToModulationEnvelopeHold);
        (instrument.KeyNumberToModulationEnvelopeDecay + preset.KeyNumberToModulationEnvelopeDecay).Should().Be(pair.KeyNumberToModulationEnvelopeDecay);
        (instrument.DelayVolumeEnvelope * preset.DelayVolumeEnvelope).ShouldBeApproximately(pair.DelayVolumeEnvelope, 1.0E-3);
        (instrument.AttackVolumeEnvelope * preset.AttackVolumeEnvelope).ShouldBeApproximately(pair.AttackVolumeEnvelope, 1.0E-3);
        (instrument.HoldVolumeEnvelope * preset.HoldVolumeEnvelope).ShouldBeApproximately(pair.HoldVolumeEnvelope, 1.0E-3);
        (instrument.DecayVolumeEnvelope * preset.DecayVolumeEnvelope).ShouldBeApproximately(pair.DecayVolumeEnvelope, 1.0E-3);
        (instrument.SustainVolumeEnvelope + preset.SustainVolumeEnvelope).ShouldBeApproximately(pair.SustainVolumeEnvelope, 1.0E-3);
        (instrument.ReleaseVolumeEnvelope * preset.ReleaseVolumeEnvelope).ShouldBeApproximately(pair.ReleaseVolumeEnvelope, 1.0E-3);
        (instrument.KeyNumberToVolumeEnvelopeHold + preset.KeyNumberToVolumeEnvelopeHold).Should().Be(pair.KeyNumberToVolumeEnvelopeHold);
        (instrument.KeyNumberToVolumeEnvelopeDecay + preset.KeyNumberToVolumeEnvelopeDecay).Should().Be(pair.KeyNumberToVolumeEnvelopeDecay);

        (instrument.InitialAttenuation + preset.InitialAttenuation).ShouldBeApproximately(pair.InitialAttenuation, 1.0E-3);

        (instrument.CoarseTune + preset.CoarseTune).Should().Be(pair.CoarseTune);
        (instrument.FineTune + preset.FineTune).Should().Be(pair.FineTune);
        instrument.SampleModes.Should().Be(pair.SampleModes);

        (instrument.ScaleTuning + preset.ScaleTuning).Should().Be(pair.ScaleTuning);
        instrument.ExclusiveClass.Should().Be(pair.ExclusiveClass);
        instrument.RootKey.Should().Be(pair.RootKey);
    }
}
