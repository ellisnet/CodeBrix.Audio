# CodeBrix.Audio.ModestSynth

Synthesis for [CodeBrix.Audio](https://github.com/ellisnet/CodeBrix.Audio): oscillators that generate sound instead of playing recorded samples, and the creative effects a synth is expected to have. Band-limited classic waveforms, white noise, a waveguide plucked string, a multi-frame wavetable, a 64-partial additive oscillator, a six-operator FM engine and a formant tone, plus a phaser, a pitch shifter, two distortion curves, a stereo widener, a bit crusher and a stutter gate — usable on their own, playable straight from MIDI through a small polyphonic synthesizer, and the sound generators and effects a Decent Sampler instrument reaches for when a group holds an `<oscillator>` rather than a `<sample>`, or an effect chain names something the core does not carry.
CodeBrix.Audio.ModestSynth depends only on .NET and CodeBrix.Audio, and is provided as a .NET 10 library and associated `CodeBrix.Audio.ModestSynth.MitLicenseForever` NuGet package.

CodeBrix.Audio.ModestSynth supports applications and assemblies that target Microsoft .NET version 10.0 and later.
Microsoft .NET version 10.0 is a Long-Term Supported (LTS) version of .NET, and was released on Nov 11, 2025; and will be actively supported by Microsoft until Nov 14, 2028.
Please update your C#/.NET code and projects to the latest LTS version of Microsoft .NET.

## Installation

```
dotnet add package CodeBrix.Audio.ModestSynth.MitLicenseForever
```

Note that the NuGet package ID and the namespace are different - there is no package named plain `CodeBrix.Audio.ModestSynth`:

* NuGet package ID: `CodeBrix.Audio.ModestSynth.MitLicenseForever`
* Assembly and primary namespace: `CodeBrix.Audio.ModestSynth` - i.e. `using CodeBrix.Audio.ModestSynth;`, with the oscillators in `CodeBrix.Audio.ModestSynth.Oscillators`, `CodeBrix.Audio.ModestSynth.Wavetable`, `CodeBrix.Audio.ModestSynth.Harmonic` and `CodeBrix.Audio.ModestSynth.Fm`, the effects in `CodeBrix.Audio.ModestSynth.Effects`, the parameter model in `CodeBrix.Audio.ModestSynth.Patch`, and the sampler adapter in `CodeBrix.Audio.ModestSynth.Integration`.

XML documentation (IntelliSense) ships alongside the assembly.

The package pulls in the following automatically; no version pinning is needed in the consuming project:

* `CodeBrix.Audio.MitLicenseForever` - the audio library this package adds synthesis to. Both packages are MIT and are published together at the same version.

There is nothing else to add - no native-asset package, and no platform-specific payload.

## CodeBrix.Audio.ModestSynth supports:

* `sine` - a pure sine wave, the shape a sub-bass layer or a clean tone starts from
* `saw` - a sawtooth with both odd and even harmonics, band-limited so it stays clean at any pitch
* `square` - a rectangular wave with odd harmonics, band-limited, with a pulse width for standalone use
* `triangle` - a mellow triangle, band-limited at its corners rather than by integrating a square, so its level and shape hold at every pitch
* `noise` (also spelled `white_noise`) - a flat-spectrum noise source, seeded, so the same seed renders the same samples on every run and every platform
* `pluck1` - a plucked string built as a digital waveguide, with the format's `damping` (how long it rings) and `pluckType` (a smooth triangle excitation through to a noise burst), tuned with an in-loop all-pass so the fundamental lands on the requested frequency across the whole MIDI range
* `wavetable` - a multi-frame `.wav` file played one cycle per period, with Serum-compatible `clm ` frame-size detection, a position that crossfades between adjacent frames or snaps to the nearest one, and a random start phase for layering. Any bit depth, any sample rate, stereo files downmixed
* `harmonic` - additive synthesis of up to 64 partials, each with its own level, shaped by a spectral tilt, an odd/even balance and a loudness compensation, with everything above Nyquist muted rather than folded back
* `fm6op` - six sine operators wired by any of the 32 classic six-operator algorithms: per-operator frequency ratio or fixed frequency, detune, output level, velocity sensitivity, feedback, and a choice of a per-operator ADSR envelope in seconds or the four-stage rate and level envelope that vintage patch banks are written in
* `formant` - one resonant region over the note's own fundamental, rendered from the reference sampler's own measured spectrum. It is a waveform the published format documentation does not describe at all and it takes no parameters, so a preset that names it plays here rather than falling silent
* `phaser` - a six-section all-pass cascade swept by an oscillator in hertz, with a centre frequency, feedback and a dry/wet crossfade, putting three moving notches in the spectrum
* `pitch_shift` - a two-grain overlap-add shifter, accurate to the semitone across four octaves, with a dry/wet crossfade
* `wave_folder` - a folder with drive and threshold, matched to the reference sampler in closed form: a bit-identical pass-through until the signal reaches the fold point, and a single reflection past it
* `wave_shaper` - a saturating curve with drive, drive boost and output level, and optional four-times oversampling that pushes the aliases a hard drive would fold back down by about 19 dB
* `stereo_simulator` - one signal into two by three classic algorithms (complementary combs, double delays, or a modulated delay), with a width control that blends the middle against a decorrelated side
* `bit_crusher` - bit-depth quantisation and sample-and-hold rate reduction, both continuous so a knob can sweep them, with a dry/wet crossfade
* `gate` - a seeded randomised stutter gate on fifty-millisecond decision windows, crossfaded so it never clicks, and reproducible run after run
* Band limiting that holds up at the top of the keyboard: the wavetable is mip-mapped per octave when it loads, measuring at least 71 dB below the fundamental for the worst alias of a sawtooth frame, and the additive oscillator simply never sounds a partial that would alias
* Wavetables decoded once and shared by every voice through a cache, so a sixteen-voice group costs one table rather than sixteen
* Waveforms and effects measured against the reference sampler rather than guessed at: the four classic shapes match its spectral centroid to within a seventh of a decibel, the plucked string to within one and a half, the wave shaper's drive law to within six tenths, and the stereo widener's decorrelation to three decimal places — where a measurement could not settle something, the documentation says so instead of pretending
* A parameter model, `ModestPatch`, that mirrors every documented oscillator attribute one for one, so a patch written for a sampler `<oscillator>` element plays here unchanged
* `ModestSynthesizer` - a polyphonic synthesizer that plays a patch from MIDI events with no preset file involved: sixteen channels, velocity, the sustain pedal, pitch bend, glide, an amplitude envelope and voice stealing, implementing the same contract as the SoundFont, SFZ and sampler engines so every player and offline renderer in CodeBrix.Audio takes it
* `ModestSynthPresets` - six worked example patches with the envelope settings that go with them: a sub sine, a saw lead, a plucked string, an FM electric piano, a wavetable pad on a table built in code, and an additive organ
* Effects that answer to both the attribute names and the `FX_*` binding names the format uses, matched without regard to case or punctuation, so a knob wired either way reaches the same parameter
* Render and Process calls that allocate nothing, lock nothing and touch no file, so a voice or an effect can run straight from an audio callback

## Sample Code

### Render a band-limited saw

```csharp
using CodeBrix.Audio.ModestSynth.Oscillators;

var saw = new SawOscillator();
saw.SetSampleRate(48000);
saw.SetFrequency(220.0);      // A3
saw.Reset(0.0);               // note-on

var block = new float[512];
saw.Render(block);            // mono, nominally -1 to 1
```

### Pluck a string

```csharp
using CodeBrix.Audio.ModestSynth.Oscillators;

var string1 = new Pluck1Oscillator
{
    Damping = 0.7,            // longer ring - an acoustic guitar rather than a muted bass
    PluckType = 0.45,         // mostly the smooth excitation, with some bite
};
string1.SetSampleRate(48000);
string1.SetFrequency(146.83); // D3
string1.Reset(0.0);           // every Reset is a new pluck

var block = new float[48000];
string1.Render(block);        // one second of a decaying string
```

### Scan a wavetable

```csharp
using CodeBrix.Audio.ModestSynth.Wavetable;

// One decode, shared by every voice that plays this file.
var table = WavetableFileCache.GetOrLoad("Wavetables/Growl 01.wav", 2048);

var voice = new WavetableOscillator { Table = table, FrameInterpolation = true };
voice.SetSampleRate(48000);
voice.SetFrequency(110.0);     // A2
voice.Reset(0.0);

var block = new float[512];
for (int i = 0; i < 64; i++)
{
    voice.Position = i / 63.0;  // sweep the table; the change is ramped, not stepped
    voice.Render(block);
}
```

A file with no Serum `clm ` chunk is cut into 2048-sample frames unless you say
otherwise. A file that cannot be read leaves the oscillator sounding like a sine
and puts one line in `voice.Problem` - nothing throws.

### Build a tone out of partials

```csharp
using CodeBrix.Audio.ModestSynth.Harmonic;

var organ = new HarmonicOscillator { NumPartials = 16, Normalization = 1.0 };
organ.SetPartialLevel(1, 1.0);
organ.SetPartialLevel(2, 0.5);
organ.SetPartialLevel(4, 0.35);
organ.SetPartialLevel(8, 0.2);

organ.SetSampleRate(48000);
organ.SetFrequency(261.63);   // middle C
organ.Reset(0.0);

var block = new float[512];
organ.Render(block);          // partials that would alias are simply not sounded
```

### Build a voice from a patch

```csharp
using CodeBrix.Audio.ModestSynth.Oscillators;
using CodeBrix.Audio.ModestSynth.Patch;

var patch = new ModestPatch
{
    Waveform = ModestWaveform.Pluck1,
    Damping = 0.6,
    PluckType = 0.8,
    RandomPhase = true,       // layered voices should not all start together
};

uint voiceNumber = 0;
IModestOscillator voice = patch.CreateOscillator(48000);
voice.SetFrequency(220.0);
voice.Reset(patch.GetStartPhase(voiceNumber++));
```

A patch holds no audio state, so one patch builds any number of voices; an oscillator holds a
voice's worth of state, so give every voice its own.

### Play a six-operator FM voice

```csharp
using CodeBrix.Audio.ModestSynth.Fm;

var voice = new Fm6OpOscillator { Algorithm = 5 };   // three carrier/modulator pairs
voice.SetSampleRate(48000);
voice.SetFrequency(261.63);                          // middle C

voice.GetOperator(1).Level = 0.9;                    // the carrier
voice.GetOperator(2).Level = 0.55;                   // its modulator
voice.GetOperator(2).Ratio = 14.0;                   // the classic electric-piano tine
voice.GetOperator(2).Attack = 0.001;
voice.GetOperator(2).Decay = 0.4;
voice.GetOperator(2).Sustain = 0.0;                  // the tine fades, the note stays

voice.Reset(0.0);
voice.NoteOn(100);                                   // MIDI velocity

var block = new float[48000];
voice.Render(block);
voice.NoteOff();
```

### Crush and stutter a stereo buffer

```csharp
using CodeBrix.Audio.ModestSynth.Effects;

var crusher = new BitCrusherEffect { BitDepth = 6, SampleRateReduction = 4 };
crusher.Prepare(48000);          // the only call that allocates

var stutter = new GateEffect { Amount = 0.4, Seed = 12345 };
stutter.Prepare(48000);

var left = new float[512];
var right = new float[512];
// ... fill both buffers ...

crusher.Process(left, right, 512);   // in place, on both channels
stutter.Process(left, right, 512);
```

Effects carry the format's own defaults, so `new PhaserEffect()` is exactly what
`<effect type="phaser" />` means. The gate is seeded, so the same seed renders
the same dropouts every time.

### Play a patch from MIDI

```csharp
using CodeBrix.Audio.ModestSynth;
using CodeBrix.Audio.Synth;

var patch = ModestSynthPresets.ElectricPiano();
var settings = ModestSynthPresets.SettingsFor(ModestSynthPresets.ElectricPianoName, 48000);

var synth = new ModestSynthesizer(patch, settings);

// Live, straight from MIDI events:
synth.NoteOn(0, 60, 100);
var left = new float[512];
var right = new float[512];
synth.Render(left, right);
synth.NoteOff(0, 60);

// Or offline, through the renderer every other synthesizer in the library uses:
SoundFontRenderer.RenderToWavFile(synth, sequence, "piano.wav", TimeSpan.FromSeconds(0.5));
```

No preset file and no registration are involved. The patch is read when the synthesizer is built,
so build another one for another sound.

### Turn it on for Decent Sampler instruments

```csharp
using CodeBrix.Audio.ModestSynth;

ModestSynth.Register();       // once, at application start-up, BEFORE loading an instrument
```

One call registers both sets: every waveform this package generates and every creative effect it supplies.
Waveforms and effect types are resolved when an instrument is built into a synthesizer, so registering afterwards
does not retrofit one - reload the instrument and build the synthesizer again. Without the call an oscillator group
is silent and an add-on effect is bypassed, the rest of the preset plays as usual, and the reason is in the
instrument's `Problems` list rather than in an exception.

## Documentation

The NuGet package includes `AGENT-README.txt`, a complete API reference and usage guide written for AI coding agents - point your agent at that file when it is writing code against this library.

CodeBrix.Audio's own `AGENT-README.txt` covers the players, readers and MIDI types this package's output flows into; read that one for them.

Additional sample code and usage examples are available in the `CodeBrix.Audio.ModestSynth.Tests` project:
https://github.com/ellisnet/CodeBrix.Audio/tree/main/tests/CodeBrix.Audio.ModestSynth.Tests

## License

CodeBrix.Audio.ModestSynth is licensed under the MIT License - see the
[LICENSE](https://github.com/ellisnet/CodeBrix.Audio/blob/main/LICENSE) file.

For licensing and provenance information about the open source code included in
this package, see [THIRD-PARTY-NOTICES.txt](https://github.com/ellisnet/CodeBrix.Audio/blob/main/THIRD-PARTY-NOTICES.txt).
