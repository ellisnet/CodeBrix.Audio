================================================================================
AGENT-README: CodeBrix.Audio.ModestSynth
A Guide for AI Coding Agents - CONSUMING the
CodeBrix.Audio.ModestSynth.MitLicenseForever NuGet package
================================================================================

OVERVIEW
========
CodeBrix.Audio.ModestSynth adds SYNTHESIS to CodeBrix.Audio: oscillators that
generate sound from a description instead of playing back a recording, and the
creative effects a synth is expected to have - a phaser, a pitch shifter, two
distortion curves, a stereo widener, a bit crusher and a stutter gate. It
targets .NET 10 or later.

It is a pure managed library with NO native code: no P/Invoke, no runtimes/
folder, nothing to rebuild when a platform is added. It works everywhere
CodeBrix.Audio works.

There are three ways to use it, and they are independent of one another:

  1. ONE OSCILLATOR OR ONE EFFECT. Create an oscillator, tell it the sample rate
     and the pitch, and ask it for blocks of samples; or create an effect,
     prepare it at your sample rate and push blocks through it. Nothing needs
     registering, nothing needs a file, and CodeBrix.Audio's players and writers
     take the samples from there.

  2. THE STANDALONE SYNTHESIZER. ModestSynthesizer plays a ModestPatch from MIDI
     events - sixteen channels, velocity, the sustain pedal, pitch bend, voice
     stealing and an amplitude envelope - and implements the same
     IMidiSynthesizer contract as the SoundFont, SFZ and Decent Sampler engines,
     so MidiMusicPlayer, MidiSequencer, SoundFontRenderer and MultiTrackPlayer
     all play it. ModestSynthPresets holds six worked example patches. No preset
     file and no registration are involved.

  3. INSIDE A DECENT SAMPLER INSTRUMENT. A Decent Sampler group can hold an
     <oscillator> element instead of (or as well as) samples, and an <effects>
     chain can name an effect type the core does not carry. CodeBrix.Audio
     plays the sampled side and runs the mixing and room effects on its own;
     the oscillators and the creative effects are what this package supplies,
     and one call at start-up hands both sets over:

         ModestSynth.Register();

An OSCILLATOR'S output is mono float, nominally in the range -1 to 1, because a
voice's panning and width belong to whatever mixes the voices. An EFFECT is
stereo and works in place, on a pair of float buffers, because that is where it
sits in the signal path.

WHAT THIS RELEASE GENERATES
  sine, saw, square, triangle, noise (also spelled white_noise), pluck1,
  wavetable, harmonic, fm6op, and formant - the last of them a waveform the
  published format guide does not document at all; see THE UNDOCUMENTED
  WAVEFORM below.

WHAT THIS RELEASE PROCESSES
  phaser, pitch_shift, wave_folder, wave_shaper, stereo_simulator, bit_crusher,
  gate. The rest of the format's effects - the filters, gain, reverb, delay,
  chorus, convolution and the compressor - belong to CodeBrix.Audio itself and
  are there whether this package is referenced or not.

THE UNDOCUMENTED WAVEFORM
  The formant waveform. A newer reference player accepts it and the published
  format guide does not document it; measurement settles what it is: it takes NO
  attributes at all - twenty-one spellings at two or three values each produced
  bit-identical audio - and it sounds one formant region near 2.4 kHz over the
  note's own fundamental. FormantOscillator renders that tone from the
  reference's own twenty-partial spectrum, read as a spectral envelope in HERTZ
  so the resonance holds still while the harmonics move through it. Every
  formant-* attribute a preset writes is reported, because the reference ignores
  them all.

  Two things about it are worth knowing before you use it. It is LOUD - the
  reference's own tone reads 5.85 dB above what the same player's sine renders at
  the same group volume - so a preset that leans on it wants its own volume
  attribute. And the PHASE of each partial cannot be recovered from a magnitude
  spectrum, so the waveform's shape is this package's own (Schroeder phases, the
  lowest crest factor the measured spectrum admits) even though its spectrum is
  the reference's.

  The core's own feature list still reports the waveform as unrecognised when a
  preset names it, because the guide that list is built from does not document
  it. That line in Problems is expected and the group sounds anyway.

HOW CLOSE IT IS
  The classic waveforms were measured against recordings of the reference player
  and match its power-weighted spectral centroid to within a fraction of a
  decibel: sine 0.08 dB, saw 0.14 dB, square 0.01 dB, triangle 0.05 dB. pluck1
  is within 1.4 dB on all five of the settings the reference was recorded at,
  and its -60 dB decay time now lands within 3 % at all three of the damping
  settings the reference was measured at (0.62 s, 1.03 s and 4.3 s), because the
  loss per trip round the string turned out to be CUBIC in one minus damping
  rather than linear in the gain. White noise carries the reference's own
  anti-imaging rolloff above 8 kHz - flat to there, -6 dB at 16 kHz and -27 dB at
  20 kHz - reproduced to within 0.8 dB at every measured band. Its power centroid
  lands 1.16 dB darker than the reference's; the reference's own two figures
  disagree by more than that, so no single response can meet both.

  wavetable and harmonic have since been measured properly, and most of what
  they do is now the reference player's own behaviour rather than a reading of
  the guide: a wavetable's frame is position * (frameCount - 1) with a linear
  crossfade; a partial's level is an amplitude; numPartials is a ceiling; no
  levels at all means a pure sine; the tilt law is 12 dB per octave per unit;
  and the odd/even balance is evenGain = min(1, 2b) against oddGain =
  min(1, 2 - 2b). All of those match, and so does the loudness compensation,
  which is (1 - n) + n / sum - a plain SUM DIVIDE blended linearly toward unity
  rather than the root-sum-of-squares law this used to apply. A wavetable's frame
  snap is round-half-up, the file's clm chunk beats any wavetableFrameSize you
  pass, and a stereo wavetable is read as its LEFT channel - all three measured.
  The EFFECTS are measured where a measurement exists and are honest where one
  does not. The wave shaper's drive law reproduces the reference player's
  measured +16.4 dB boost to within 0.6 dB; the stereo simulator reproduces both
  of its measured figures - a mono sum 6.02 dB down at width="0.5" and the
  decorrelation ordering, matching the reference's own 440 Hz measurement to
  0.001 on two of the three algorithms; the gate loses 2.6 dB where the
  reference lost 2.4; and the pitch shifter is accurate semitones. The WAVE
  FOLDER is now a closed form the reference was fitted to across an input-level
  sweep - out = foldOnce(in*k)/k with k = drive / (2*sqrt(2)*threshold), a single
  reflection that is not clamped and a bit-identical pass-through below the fold
  point - which closes the 5.6 dB divergence it used to publish. The PHASER's
  structure and sweep are measured too: six first-order allpasses at
  centerFrequency, an exponential sweep of five octaves each way, and a feedback
  that is SUBTRACTED, which is what turns its notches into the +1.5 dB peaks the
  reference showed.

  fm6op is measured throughout now. An fm6op oscillator with no fm* attributes
  renders a pure sine 4.44 dB below a plain sine oscillator, reproduced to within
  a fiftieth of a decibel; the modulation index is beta = 4*pi*level, which
  reproduces the reference's whole Bessel sideband pattern to within 1.5 dB over
  six lines; fmOpNRatio defaults to the operator's own NUMBER; carriers sum with
  no count normalisation; the velocity table is a five-point table whose neutral
  point is velocity 96, so a hard note is pushed UP rather than merely left
  alone; the envelope rate scale runs 5.5 rate units per halving and rate 0
  CRAWLS rather than holding, whatever the format's description says; detune
  follows a power law in frequency, 0.005341 * f0^0.630, over the six octaves it
  was measured across; and fmOpNFeedback acts only on the algorithm's own
  feedback operator and is clamped at 1. Its modulator-to-modulator chains are
  measured under load too: each link runs through the intermediate operator's OWN
  OUTPUT, so an operator whose downstream neighbour is silent is inaudible, and a
  voice's tail is the longest OPERATOR release rather than the group envelope's.
  What is still reasoned rather than measured: the feedback DEPTH in cycles, the
  level scale's 0-99 curve and the rate scaling, which the format has no
  attribute for at all.


INSTALLATION
============
NuGet package:   CodeBrix.Audio.ModestSynth.MitLicenseForever
Command:         dotnet add package CodeBrix.Audio.ModestSynth.MitLicenseForever

Note that the PACKAGE id carries the ".MitLicenseForever" suffix, but the
NAMESPACE is simply "CodeBrix.Audio.ModestSynth" (no suffix).

  License:        MIT (licence acceptance is required)
  Depends on:     CodeBrix.Audio.MitLicenseForever, at the same version - the
                  two packages are built and published together
  Target:         .NET 10 or later
  Native libs:    NONE of its own. The only native code in the tree is the audio
                  engine's playback backend, which arrives with CodeBrix.Audio
                  and is not needed to RENDER anything.
  OS limits:      none. Rendering is pure managed code.

See also: the CodeBrix.Audio package's own guide, at
https://github.com/ellisnet/CodeBrix.Audio/blob/main/AGENT-README.txt - it
documents the players, the WAV writer, MidiMusicPlayer and the sampled-instrument
engines that this package's output joins.


KEY NAMESPACES / USINGS
=======================
  using CodeBrix.Audio.ModestSynth;              // ModestSynth.Register()
  using CodeBrix.Audio.ModestSynth.Oscillators;  // the oscillators themselves
  using CodeBrix.Audio.ModestSynth.Fm;           // the six-operator FM engine
  using CodeBrix.Audio.ModestSynth.Patch;        // ModestPatch, the FM operator model
  using CodeBrix.Audio.ModestSynth.Wavetable;    // WavetableOscillator, WavetableFile
  using CodeBrix.Audio.ModestSynth.Harmonic;     // HarmonicOscillator
  using CodeBrix.Audio.ModestSynth.Effects;      // the seven creative effects
  using CodeBrix.Audio.ModestSynth.Integration;  // ModestVoiceSource, the adapter

An effect also needs the contract it implements, which lives in the core:

  using CodeBrix.Audio.Synth.DecentSampler.Engine;   // IInstrumentEffect

Everything else in the assembly is internal.

The type ModestSynth and the namespace CodeBrix.Audio.ModestSynth share a name.
That is legal and it resolves the way you would want - "ModestSynth.Register()"
in a file that has the using directive above is the static class - but if a
compiler ever disagrees with you about it, write CodeBrix.Audio.ModestSynth
.ModestSynth.Register() and move on.


REGISTERING WITH THE DECENT SAMPLER ENGINE
==========================================
    using CodeBrix.Audio.ModestSynth;

    ModestSynth.Register();     // once, at application start-up

One call registers BOTH sets: every waveform this package generates and every
creative effect it supplies. After it, a group holding

    <oscillator waveform="fm6op" fmAlgorithm="5" fmOp2Level="0.7" />

sounds, an <effect type="phaser" /> in any chain processes, and every
OSCILLATOR_* and FX_* binding in the preset moves what you hear. Without it, an
oscillator group is SILENT and an add-on effect is BYPASSED - the rest of the
preset plays normally - and the synthesizer's Problems list carries one line per
missing feature naming this package and this call.

Call it BEFORE loading an instrument that needs it. Waveforms and effect types
are resolved while an instrument is being BUILT, not while its file is being
parsed, so registering later does not retrofit an instrument that is already
loaded - reload it.

  * Idempotent. Calling it twice does nothing the second time, from any thread.
  * ModestSynth.IsRegistered reports whether it has run.
  * ModestSynth.RegisteredOscillatorWaveforms lists the waveform names that were
    offered, spelled as a preset spells them, and includes every accepted
    synonym (white noise appears as both "noise" and "white_noise"). It is empty
    until Register() has run. Useful in start-up diagnostics.
  * ModestSynth.RegisteredEffectTypes lists the effect type names that were
    offered - the seven creative ones, never the core's own. Also empty until
    Register() has run.
  * ModestSynth.Register(DecentSamplerExtensionRegistry) fills a registry of
    your own instead of the process-wide one - the value you would put in
    DecentSamplerSynthesizerSettings.Extensions, or an isolated registry in a
    test. It leaves IsRegistered and RegisteredEffectTypes alone, because those
    describe the process-wide registration.
  * There is deliberately no module initializer doing this for you. A module
    initializer only runs once something in the assembly is touched, which
    trimming and lazy assembly loading make unreliable: the package would work
    in a debug build and silently fail to register in a trimmed publish. The
    explicit call is the contract, and it keeps the package free of reflection.
  * The APPLICATION takes the dependency and makes the call. A library that
    merely renders audio should not decide this for its host.
  * The standalone API needs none of this. Registration only wires the package
    into Decent Sampler preset loading. ModestSynthesizer, ModestPatch, the
    oscillators and the effects all work with nothing called.
  * It also changes what the engine SAYS about itself.
    DecentSamplerSupportedFeatures.StatusOf answers Implemented for a waveform
    or an add-on effect type as soon as a registry has a factory for the name,
    and Parsed while nothing supplies it - so a host can report honestly whether
    this package is present. There is an overload taking a
    DecentSamplerExtensionRegistry when you want the answer for a registry of
    your own rather than the process-wide one.

WHAT REGISTRATION GIVES A ZONE
------------------------------
ModestVoiceSource (CodeBrix.Audio.ModestSynth.Integration) is what the engine
builds per voice. You never construct one unless you are filling a registry by
hand, but what it does is worth knowing:

  * It reads EVERY oscillator attribute off the resolved zone - waveform,
    damping, pluckType, randomPhase, the wavetable file, frame size, position and
    frame interpolation, numPartials, tilt, oddEvenBalance, normalization, all
    sixty-four partial levels, fmAlgorithm and all twenty parameters of each of
    the six operators.
  * The zone stays the single source of truth while the note sounds. An
    OSCILLATOR_* binding writes the zone; the source reads it back at the top of
    the next block, so a knob or a modulator moves a note that is already
    playing. The re-read is gated on the instrument's ParameterVersion, so a
    block during which nothing moved costs one integer comparison.
  * PITCH comes from the engine: the MIDI note in equal temperament with A4 at
    440, plus the zone's tuning, its glide ramp and the channel's pitch bend,
    handed over once per block.
  * VELOCITY goes to the oscillator as well as to the amplitude, which is what
    makes an fm6op operator's velocity sensitivity work.
  * The GROUP'S ADSR gates the oscillator, because only pluck1 and fm6op stop by
    themselves. An fm6op using the -1 release sentinel never finishes on its own
    (UsesOuterRelease), and the group's envelope is what ends it. An fm6op whose
    operators carry REAL releases is the other way round: MEASURED, its tail is
    the longest OPERATOR release and never the group envelope's, so the adapter
    reports OwnsRelease and the group envelope holds instead of releasing over
    the top of it. Voice stealing and silencedByTags still fade such a voice out,
    because those are not the key coming up.
  * LEVEL. Every oscillator is trimmed by ModestVoiceSource.ReferenceOscillatorGain
    (0.9419, about -0.52 dB), which is where the reference player puts an
    oscillator zone relative to a sample zone. It is one trim over all of them,
    so the measured level RELATIONSHIPS - noise 2.68 dB below sine, fm6op 4.44 dB
    below it, harmonic and wavetable level with it - are the oscillators' own.
  * A WAVETABLE FILE IS RESOLVED THROUGH THE PRESET'S OWN CONTAINER, so a path
    works from a folder and from inside a .dslibrary archive, and its
    capitalisation does not have to match the disk. The first voice of a
    wavetable group reads the file; every voice after it shares the decoded
    table through WavetableFileCache.
  * Rendering allocates nothing. Building a source, and starting a note on a
    waveform that source has not played before, do allocate; the engine's
    per-zone voice pooling is what keeps that off the steady state.


THE STANDALONE PATCH API
========================
Two pieces: an oscillator, which is one voice's worth of sound-generating state,
and a patch, which is the settings you build voices from.

IModestOscillator  (CodeBrix.Audio.ModestSynth.Oscillators)
-----------------------------------------------------------
  string Waveform             the name this oscillator implements, e.g. "pluck1"
  int    SampleRate           the rate blocks are rendered at
  double Frequency            the pitch being generated, in Hz
  void   SetSampleRate(int)   call before rendering; this is where allocation happens
  void   SetFrequency(double) safe between blocks - this is how glide and vibrato work
  void   Reset(double phase)  note-on; phase is in CYCLES, so only its fraction matters
  void   Render(Span<float>)  fills the block, overwriting it

  Lifecycle: SetSampleRate, SetFrequency, Reset, then Render as often as you
  like. Retuning never restarts the waveform; Reset always does.

  Threading: an oscillator belongs to the thread rendering it. Nothing is
  thread-safe and nothing needs to be - give every voice its own oscillator.

  Audio-thread hygiene: Render allocates nothing, locks nothing and opens
  nothing. SetSampleRate may allocate, which is why it is not something to call
  between blocks.

IModestVoiceOscillator : IModestOscillator  (same namespace)
------------------------------------------------------------
  Some waveforms are played as NOTES rather than simply run: their sound depends
  on how hard the key was hit, and they have envelopes of their own to finish
  after the key comes up. Those implement this instead.

  bool IsKeyDown              true between NoteOn and NoteOff
  bool IsFinished             the key is up and nothing audible is left; the
                              voice can be recycled
  void NoteOn(int velocity)   MIDI velocity 0..127, clamped; restarts every
                              envelope but does NOT touch the phase
  void NoteOff()              releases the note; keep rendering until IsFinished

  Lifecycle: SetSampleRate, SetFrequency, Reset(startPhase), NoteOn(velocity),
  Render per block, NoteOff(), Render until IsFinished.

  EVERY waveform in this package implements it, so anything that plays notes has
  one contract to drive. The default behaviour is the one a waveform that simply
  runs wants: NoteOn records the velocity and marks the key down, NoteOff marks
  it up, and IsFinished is never true - a sine has no end of its own, so whatever
  owns the envelope decides when the note stops. The two waveforms that DO end by
  themselves override it:

    pluck1   finished once the string has decayed below -100 dB, whether the key
             is still down or not - a plucked string does not care about the key.
    fm6op    finished once the key is up and every carrier that can be heard has
             finished its envelope - the longest of them, which is MEASURED to be
             what ends the voice. Usually never, because the format's default
             release is the -1 sentinel: see UsesOuterRelease.

ModestOscillatorFactory  (static)
---------------------------------
  IReadOnlyList<ModestWaveform> SupportedWaveforms
  bool IsSupported(ModestWaveform) / IsSupported(string name)
  IModestOscillator Create(ModestWaveform)
  bool TryCreate(string name, out IModestOscillator)

  Every waveform the enumeration names now has a generator, so Create builds one
  for all of them; it throws only for a value that is not a waveform at all.
  TryCreate returns false for a NAME it does not recognise - use it when the name
  came from a file. IsSupported stays because the enumeration may grow before the
  generators do.

ModestWaveform / ModestWaveforms
--------------------------------
  ModestWaveform is the enum: Sine, Saw, Square, Triangle, Noise, Pluck1,
  Wavetable, Harmonic, Fm6Op, Formant.

  ModestWaveforms holds the string spellings as constants and converts:
    string ToName(ModestWaveform)          - "pluck1", never a synonym
    bool   TryParse(string, out ModestWaveform)
  Parsing is case-insensitive, trims whitespace, accepts "white_noise" as a
  synonym for "noise", and returns false for anything else rather than throwing.

ModestPatch  (CodeBrix.Audio.ModestSynth.Patch)
-----------------------------------------------
  A plain settings object. It holds no audio state, so one patch can build any
  number of voices, and it is safe to read from several threads as long as
  nothing is writing to it.

    Waveform            which shape (default Sine)
    Damping             pluck1 decay, 0..1 (default 0.5)
    PluckType           pluck1 excitation, 0..1 (default 0.5)
    RandomPhase         scatter each voice's start phase (default false)
    Seed                the seed for anything random; 0 leaves each oscillator
                        on its own default seed (default 0)

    WavetableFile, WavetableTable, WavetableFrameSize (2048),
    WavetablePosition (0.0), WavetableFrameInterpolation (true)
      WavetableTable is a WavetableFile you already have, and it is used INSTEAD
      of reading WavetableFile - which is how a table built in code is played,
      and how CreateOscillator is kept off the disk entirely. The format has no
      attribute for it.
      Otherwise CreateOscillator LOADS WavetableFile, through the shared cache. The
      format writes that path relative to the .dspreset file, so combine it with
      the preset's folder yourself first - a relative path resolves against the
      process's current directory, which is rarely what a preset meant. A path
      that cannot be read leaves the oscillator sounding like a sine and puts one
      line in WavetableOscillator.Problem.

    NumPartials (8), HarmonicTilt (0.0), HarmonicOddEvenBalance (0.5),
    HarmonicNormalization (0.0), GetPartialLevel(n) / SetPartialLevel(n, level)
    for partials 1..64 (each 0.0)

    FmAlgorithm (1), FmOperators (six ModestFmOperator settings objects),
    GetFmOperator(1..6)

    CanCreateOscillator     whether this release can build the selected waveform
    CreateOscillator()      at the default sample rate
    CreateOscillator(rate)  at a rate you choose
    GetStartPhase(voiceSeed) 0 when RandomPhase is off; a scattered, repeatable
                             phase in [0,1) when it is on

  CreateOscillator hands back an oscillator at the requested rate, carrying the
  patch's parameters, at its default pitch and NOT reset. Set the pitch, then
  Reset with GetStartPhase at note-on.

  Every property is named for the attribute it mirrors, and every default is the
  format's documented default. Out-of-range values are CLAMPED, not rejected: an
  attribute out of range is never worth an exception. Non-finite values (NaN,
  infinity) are ignored, leaving the previous value in place.

ModestFmOperator / ModestFmOperatorMode / ModestFmEnvelopeType
--------------------------------------------------------------
  The six-operator FM parameter model: Ratio, Detune (-7..7), Mode
  (Ratio/Fixed), FixedFrequency, Level, VelocitySensitivity (0..7), Feedback,
  Attack/Decay/Sustain/Release, EnvelopeType (Adsr/Dx7) and EgRate1..4 /
  EgLevel1..4 (0..99). Attack and Release accept -1, the sentinel meaning "no
  envelope of my own - the group's envelope gates me". Full ranges, defaults and
  meanings are in the fm6op section below.

  A ModestPatch's six operators are a TEMPLATE. CreateOscillator copies their
  values into the voice it builds, so changing a voice's operator changes that
  voice alone, and changing the patch afterwards does not reach voices that are
  already sounding.

WavetableFile  (CodeBrix.Audio.ModestSynth.Wavetable)
------------------------------------------------------
  A decoded wavetable: the frames a .wav file holds, plus the band-limited
  copies the oscillator plays from. IMMUTABLE, so one table serves every voice.

    static bool TryLoad(string path, int frameSize,
                        out WavetableFile file, out string problem)
                             reads a file; never throws for bad content
    static bool TryLoad(Stream stream, string sourceName, int frameSize,
                        out WavetableFile file, out string problem)
                             reads .wav bytes that are not a file on disk - an
                             entry inside a .dslibrary archive, for instance.
                             The stream must be readable and seekable and stays
                             yours to dispose
    static WavetableFile FromSamples(ReadOnlySpan<float>, int frameSize,
                                     string name)
                             a wavetable written in code

    string SourcePath        the full path, or the name you gave FromSamples
    int    FrameSize         samples in one cycle
    int    FrameCount        how many frames; always at least 1
    int    SampleCount       FrameCount * FrameSize
    bool   HasClmChunk       whether the file declared its own frame size
    int    DeclaredFrameSize what the clm chunk said, or 0
    int    SourceSampleRate  what the file was recorded at; irrelevant to pitch
    int    SourceChannels    before the downmix
    long   ApproximateSizeInBytes    about 4.5x the frame data
    ReadOnlySpan<float> GetFrame(int index)   the raw samples, no copy

    Constants: DefaultFrameSize (2048), MinimumFrameSize (2),
               MaximumFrameSize (65536), MaximumSampleCount (16 mega-samples -
               8,192 frames of 2,048; anything larger is reported, not loaded)

WavetableFileCache  (static)
-----------------------------
  bool TryGetOrLoad(string path, int frameSize, out WavetableFile, out string problem)
  bool TryGetOrLoad(string cacheKey, int frameSize, Func<Stream> openStream,
                    out WavetableFile, out string problem)
                                                          for bytes that are not
                                                          a file: you supply the
                                                          key and the opener
  WavetableFile GetOrLoad(string path, int frameSize)      null on failure
  void Clear()
  int  Count
  long ApproximateSizeInBytes

  One decode per (full path, requested frame size), shared by every voice.
  Thread-safe; none of it belongs on the audio thread. Failures are NOT cached,
  so a file that appears later is picked up. Nothing is evicted until you call
  Clear().


THE STANDALONE SYNTHESIZER
==========================
ModestSynthesizer plays a ModestPatch from MIDI events. There is no preset file,
nothing to register and nothing to resolve: a patch, some settings, and the same
IMidiSynthesizer contract the SoundFont, SFZ and Decent Sampler engines
implement, so every player and renderer in CodeBrix.Audio takes it.

A COMPLETE EXAMPLE - render an electric piano phrase to a WAV file
------------------------------------------------------------------
    using CodeBrix.Audio.Midi;
    using CodeBrix.Audio.ModestSynth;
    using CodeBrix.Audio.Synth;

    // 1. The sound, and how it is played.
    var patch = ModestSynthPresets.ElectricPiano();
    var settings = ModestSynthPresets.SettingsFor(
        ModestSynthPresets.ElectricPianoName, 48000);
    settings.MaximumPolyphony = 16;

    var synthesizer = new ModestSynthesizer(patch, settings);

    // 2. Something to play. Any MidiSequence will do - one read from a .mid
    //    file, one built by MultiTrackPlayer, or one written by hand.
    var events = new MidiEventCollection(1, 480);
    foreach (var (tick, note) in new[] { (0, 60), (480, 64), (960, 67) })
    {
        events.AddEvent(new NoteEvent(tick, 1, MidiCommandCode.NoteOn, note, 100), 1);
        events.AddEvent(
            new NoteEvent(tick + 440, 1, MidiCommandCode.NoteOff, note, 0), 1);
    }

    events.PrepareForExport();
    var sequence = MidiSequence.FromEvents(events);

    // 3. Render it offline, leaving half a second for the release tails.
    SoundFontRenderer.RenderToWavFile(
        synthesizer, sequence, "piano.wav", TimeSpan.FromSeconds(0.5));

    // Or play it live, letting the player build the synthesizer at the device's
    // own rate - which is the overload to prefer, because a synthesizer built at
    // the wrong rate is transposed:
    //
    //     using var player = new MidiMusicPlayer();
    //     player.Load(rate => new ModestSynthesizer(patch, ModestSynthPresets
    //         .SettingsFor(ModestSynthPresets.ElectricPianoName, rate)), sequence);
    //     player.Play();

ModestSynthesizer  (CodeBrix.Audio.ModestSynth)
------------------------------------------------
  new ModestSynthesizer(patch, sampleRate)
  new ModestSynthesizer(patch, settings)

  ModestPatch Patch            the sound being played
  int  SampleRate / BlockSize / MaximumPolyphony / ChannelCount (16)
  int  ActiveVoiceCount
  float MasterVolume           0.5 by default, as the whole family uses
  void ProcessMidiMessage(channel, command, data1, data2)
  void NoteOn(channel, key, velocity)      velocity 0 is a note-off
  void NoteOff(channel, key)
  void NoteOffAll(bool immediate)
  void NoteOffAll(int channel, bool immediate)
  void Reset()
  void Render(Span<float> left, Span<float> right)

  WHAT IT HANDLES. Sixteen channels; note-on and note-off with velocity; the
  sustain pedal (CC 64), which holds a released note until the pedal lifts;
  pitch bend over the settings' range; all sound off (CC 120), reset all
  controllers (CC 121) and all notes off (CC 123); and a fixed voice pool that
  steals the oldest RELEASED voice first, then the oldest voice of any kind.

  WHAT IT DOES NOT. There are no filters, no modulators, no layers and no key or
  velocity zones - those belong to an instrument format. This plays ONE patch
  across the whole keyboard. When you want the rest, a Decent Sampler preset
  played through DecentSamplerSynthesizer is where it lives.

  THE PATCH IS READ WHEN THE SYNTHESIZER IS BUILT. Every voice gets its own
  oscillator, configured then; changing the patch afterwards changes nothing.
  Build another synthesizer for another sound. Rendering allocates nothing and
  takes no lock; like every synthesizer in this family it is not thread-safe, so
  MIDI events and rendering must not overlap.

ModestSynthesizerSettings  (same namespace)
--------------------------------------------
  SampleRate            8,000..192,000        (44,100)
  BlockSize             8..1,024 frames       (64)
  MaximumPolyphony      1..1,024 voices       (32)
  Attack / Decay        seconds               (0 / 0)
  Sustain               0..1 amplitude        (1)
  Release               seconds               (0.1)
  GlideSeconds          0 turns glide off     (0)   linear in pitch, and this
                                                    long whatever the interval
  PitchBendSemitones    0..48                 (2)
  VelocityTracking      0..1                  (1)   gain = (1 - t) + t * vel/127,
                                                    the format's ampVelTrack law
  MasterVolume                                (0.5)
  RandomSeed                                  (12345)
  Clone()

  Every value clamps rather than throwing, except SampleRate, BlockSize and
  MaximumPolyphony, which are structural and throw. The settings are COPIED by
  the constructor, so changing them afterwards has no effect.

  The patch says what a voice sounds like; these say how it is played. They are
  separate because ModestPatch is the Decent Sampler format's own oscillator
  model, which has no envelope of its own - in a preset the group's envelope does
  that job, and here these do.

ModestSynthPresets  (same namespace)
-------------------------------------
  Six worked examples, each a new object every time you ask:

    SubSine()          a plain sine - a sub-bass, and the tone to check a signal
                       path with
    SawLead()          a band-limited sawtooth with randomPhase on, so stacking
                       two synthesizers thickens rather than doubles
    PluckedString()    a waveguide string, damping 0.7 and pluckType 0.35
    ElectricPiano()    fm6op algorithm 5: a fourteen-times tine decaying in a
                       quarter of a second over a soft body, with the tine's
                       velocity sensitivity doing the brightness
    WavetablePad()     a wavetable on a four-frame table BUILT IN CODE (through
                       ModestPatch.WavetableTable), so it touches no file
    AdditiveOrgan()    the drawbar partials 1, 2, 3, 4, 6 and 8 with the
                       loudness compensation on

    IReadOnlyList<string> Names
    ModestPatch Create(string name)                     case-insensitive
    ModestSynthesizerSettings SettingsFor(string, rate) the envelope, glide and
                                                        volume that go with it

  SettingsFor exists because a patch carries no envelope: a pad wants a slow
  attack and a long release, a plucked string wants neither, and an organ wants a
  switch. An unknown name throws ArgumentException from both.

EVERY WAVEFORM AND ITS PARAMETERS
=================================

sine  -  SineOscillator
------------------------
  Parameters: none beyond pitch.
  One partial, so it cannot alias at any pitch below Nyquist. Output -1 to 1.
  This is the default an <oscillator> with no waveform attribute gets.

saw  -  SawOscillator
----------------------
  Parameters: none beyond pitch.
  Both odd and even harmonics; the bright shape a subtractive patch starts from.
  The falling edge is band-limited with a polyBLEP correction, which pushes the
  aliases a naive saw would fold below Nyquist far down. Expect a small
  overshoot past 1 at the corrected edge; that is the correction doing its job.

square  -  SquareOscillator
----------------------------
  Parameters: PulseWidth (0.01..0.99, default 0.5).
  Odd harmonics at the default width; the hollow, reed-like shape. Both edges
  are band-limited. PulseWidth is a STANDALONE extra - the <oscillator> element
  has no pulse-width attribute, so an oscillator built from a preset always runs
  at 50%. Out-of-range widths are clamped, because two edges that collide cannot
  both be band-limited.

triangle  -  TriangleOscillator
--------------------------------
  Parameters: none beyond pitch.
  Odd harmonics falling away far faster than a square's; the mellow shape. Its
  discontinuity is in the SLOPE rather than the value, so it is corrected with a
  polyBLAMP at each corner. That keeps the level and the shape right at every
  pitch, which the common "integrate a square" trick does not.

noise / white_noise  -  NoiseOscillator
----------------------------------------
  Parameters: Seed (uint, default fixed), Amplitude (0..1, default 0.8996).
  Every sample drawn independently and uniformly from [-Amplitude, Amplitude):
  flat spectrum, measured within 0.7 dB across every octave. The default
  amplitude is MEASURED: it puts this oscillator's RMS 2.68 dB below a
  full-amplitude sine's, which is where the reference player's noise oscillator
  sits. Set Amplitude to 1.0 for a full-scale noise source (RMS 0.577).
  No pitch: SetFrequency is accepted and recorded so a
  voice can drive every oscillator the same way, and changes nothing you can
  hear. Reset restarts the sequence from Seed and IGNORES the phase it is
  handed, because noise has no phase.
  Determinism: the same seed renders the same samples, every run, every
  platform. Two noise voices with the SAME seed are one louder copy, not a wider
  sound - give them different seeds.

pluck1  -  Pluck1Oscillator
----------------------------
  Parameters: Damping (0..1, default 0.5), PluckType (0..1, default 0.5),
              Seed (uint, default fixed).
  Read-only: DecayTimeSeconds - what Damping currently maps to.

  A Karplus-Strong waveguide: a delay line one period long fed back through a
  gentle low-pass, so high partials die away before low ones exactly as they do
  on a real string. Excited once at Reset and decaying from there; every Reset
  is a new pluck.

  Damping is the DECAY, not a filter cutoff: 0.0 is heavily damped and short,
  1.0 barely damped and long. It sets the fraction the string keeps on each trip
  round the waveguide, so the decay is PITCH-DEPENDENT by design and a low note
  rings longer than a high one at the same setting, as on a real instrument. At
  82 Hz, damping 0.0 falls 60 dB in about 2.2 s, 0.1 in 3.1 s, 0.5 in 4.9 s and
  0.9 in 17 s; at 220 Hz each of those is roughly a third as long. Read
  DecayTimeSeconds for the number at the current setting and pitch. It takes
  effect on the next rendered sample, so a knob can shorten a ringing string.

  PluckType is the EXCITATION: 0.0 fills the string with a smooth triangle
  (soft, mellow), 1.0 with a noise burst (bright, aggressive), and values
  between blend the two linearly. It is only read at note-on, so changing it
  mid-note does nothing until the next Reset.

  Pitch: tuned with a first-order all-pass inside the loop, so the fundamental
  lands on the requested frequency rather than on the nearest whole number of
  samples. Tunable from 8 Hz up to a THIRD of the sample rate - which covers
  every MIDI note at every ordinary sample rate - and asking for anything
  outside that throws ArgumentOutOfRangeException, because a string a handful of
  samples long has no pitch to speak of.

  Reset(phase) rotates the excitation rather than setting a running phase, which
  is what decorrelates two plucks of the same string. The DC component is
  removed from every excitation, so a string never decays as an audible thump.

  Useful settings, from the format's own guidance:
    bass guitar      Damping 0.3-0.5, PluckType 0.6-0.8
    acoustic guitar  Damping 0.5-0.7, PluckType 0.4-0.6
    harp, lyre       Damping 0.7-0.9, PluckType 0.2-0.4

wavetable  -  WavetableOscillator  (namespace ...ModestSynth.Wavetable)
------------------------------------------------------------------------
  Parameters: Table (a WavetableFile, or null), Position (0..1, default 0.0),
              FrameInterpolation (default true).
  Read-only:  HasTable, Problem, MipLevel, Table.
  Methods:    TryLoad(path, frameSize), ReportProblem(reason).

  It reads one cycle per period from a frame of a multi-frame .wav file, so
  moving Position walks the table from timbre to timbre. This is the classic
  wavetable-scanning sound.

  THE FILE. Any .wav CodeBrix.Audio can read: 8, 16, 24 or 32-bit PCM, 32 or
  64-bit IEEE float, any sample rate, mono or multi-channel. A multi-channel
  file is DOWNMIXED by averaging its channels, because an oscillator is one mono
  voice. The file's own sample rate is irrelevant - a frame is one cycle
  whatever rate it was recorded at - and is kept only as information. Samples
  left over after the last whole frame are dropped.

  THE FRAME SIZE, in the order the format gives:
    1. If the file carries a Serum-compatible "clm " RIFF chunk (four
       characters, the fourth a SPACE), the frame size is read from it and
       WINS. Its text starts "<!>" followed by the frame size - for example
       "<!>2048 00000000 wavetable (www.xferrecords.com)". A chunk whose text
       names nothing plausible is ignored rather than trusted.
    2. Otherwise the wavetableFrameSize you asked for is used.
    3. Otherwise 2048, the format's documented default.
  HasClmChunk and DeclaredFrameSize on the loaded WavetableFile say which
  happened.

  POSITION. 0.0 is the first frame, 1.0 the last, so a value maps to frame
  index position * (FrameCount - 1). With FrameInterpolation on - the default -
  adjacent frames are linearly crossfaded, which is what a morphing table wants.
  With it off the position SNAPS to the nearest whole frame (a half-way value
  rounds up), which is what a table of unrelated shapes wants. Both are meant to
  be changed between blocks: they are the targets of the
  OSCILLATOR_WAVETABLE_POSITION and OSCILLATOR_WAVETABLE_FRAME_INTERPOLATION
  bindings, so an LFO, an envelope, a MIDI CC or a knob can sweep them.
  A position change is RAMPED across the block it arrives in, so a modulator
  scanning the table does not click once per block; the property reads back the
  target, which is reached by the end of that block. Reset snaps instead of
  ramping, because a new note starts wherever it was told to.

  NO TABLE MEANS A SINE. That is measured, not a fallback of convenience: the
  reference player renders <oscillator waveform="wavetable"/> with no
  wavetableFile as a pure sine at the note's frequency and at a sine's own
  level. A missing or unreadable file behaves the same way, and Problem then
  carries one line in the style of the reference's own preset validator -
  "Missing file reference: <oscillator> @wavetableFile=..." - for a Problems
  list. Nothing throws, ever.

  ALIASING. The table is band-limited per octave when it is loaded, so a frame
  full of harmonics stays clean at the top of the keyboard. Measured on a
  sawtooth frame, worst alias below 10 kHz relative to the fundamental:
  -71 dB at 1 kHz, -84 dB at 4 kHz, -90 dB at 8 kHz - better at every pitch
  than this package's own polyBLEP sawtooth.

  MEMORY AND TIME. Those band-limited copies cost about 4.5 times the frame
  data and are built once: a 256-frame table of 2048-sample frames is 9 MB and
  takes about 45 ms. Load through WavetableFileCache so every voice shares one
  table, and be aware that a preset offering a menu of wavetables loads them all
  if it builds a group per table.

  RANDOM PHASE. Reset takes the start phase in cycles, so
  ModestPatch.GetStartPhase drives the format's randomPhase attribute. The
  guide's advice is worth repeating: always set randomPhase="true" when layering
  wavetable groups, because voices that all start at phase zero cancel each
  other instead of thickening.

harmonic  -  HarmonicOscillator  (namespace ...ModestSynth.Harmonic)
---------------------------------------------------------------------
  Parameters: NumPartials (1..64, default 8), Tilt (-1..1, default 0),
              OddEvenBalance (0..1, default 0.5), Normalization (0..1,
              default 0), GetPartialLevel(n) / SetPartialLevel(n, level) for
              partials 1..64 (each 0.0), ClearPartialLevels().
  Read-only:  IsPureSineFallback, ActivePartialCount, GetPartialGain(n).

  Additive synthesis: up to 64 sine partials at whole multiples of the note's
  frequency, summed. Every parameter can be changed between blocks and is meant
  to be - they are the targets of OSCILLATOR_HARMONIC_NUM_PARTIALS,
  OSCILLATOR_HARMONIC_TILT, OSCILLATOR_HARMONIC_ODD_EVEN_BALANCE,
  OSCILLATOR_HARMONIC_NORMALIZATION and OSCILLATOR_HARMONIC_PARTIAL_1_LEVEL
  through ..._64_LEVEL.

  NOTHING SET MEANS A SINE. Every harmonicPartialNLevel defaults to 0, and the
  reference player renders an oscillator carrying no harmonic* attributes as a
  pure sine at a sine's own level. So "no partial has a level" is treated as
  "the fundamental is at full level". Set ANY partial's level and that stops:
  what you set is what sounds, and IsPureSineFallback says which state you are
  in. ClearPartialLevels() puts it back.

  numPartials is a CEILING, not a count of what sounds. A partial inside the
  limit whose level is 0 contributes nothing; a partial above the limit is
  silent whatever its level.

  THE THREE SHAPING LAWS. The guide gives each attribute's range and its
  direction and nothing else; measurement supplied the rest, and all three are
  now the reference player's own behaviour:

    harmonicTilt      partial k is multiplied by k to the power of -2*tilt.
                      MEASURED: that is 12 dB per octave per unit, pivoting on
                      the fundamental, so tilt=+0.5 gives exactly a sawtooth's
                      1/k slope (darker) and tilt=+1 a triangle's 1/k-squared.
                      The fundamental never changes. A NEGATIVE tilt boosts
                      hard - at tilt=-1 partial 64 comes up 72 dB - so pair it
                      with harmonicNormalization unless you want the level to
                      climb.

    harmonicOddEven   odd partials are multiplied by min(1, 2*(1-balance)) and
    Balance           even ones by min(1, 2*balance). MEASURED, and exactly what
                      the reference player does. 0.5 leaves BOTH at full
                      level, so it is genuinely neutral rather than a
                      half-and-half mix; 0.0 silences the even partials and 1.0
                      the odd ones, each fading linearly over its half of the
                      range. The fundamental counts as odd.

    harmonic          the gain is (1 - n) + n / sum, where sum is the total of
    Normalization     the partial gains - a plain SUM DIVIDE blended linearly
                      toward unity. MEASURED. Full compensation therefore holds
                      the summed PEAK where a single full-level partial's would
                      be rather than the RMS, and the blend is linear in GAIN
                      rather than in decibels. A single full-level partial needs
                      none, so normalization never changes the plain sine this
                      waveform defaults to.

  NO ALIASING, EVER. A partial whose frequency reaches Nyquist is not summed at
  all, so a note sweeping up the keyboard loses partials one at a time instead
  of folding them back down. At 1 kHz on a 48 kHz stream, 23 of 64 partials
  sound; ActivePartialCount says how many. They come back when the pitch falls.

  COST. Only the partials that actually sound are summed, and each costs a
  multiply, a wrap and a table lookup rather than a call to Math.Sin. Sixty-four
  partials render about 125 times faster than real time on one core at 48 kHz;
  the default single partial is far cheaper than that. Render allocates nothing
  even on the block where a parameter changed.


fm6op  -  Fm6OpOscillator  (namespace CodeBrix.Audio.ModestSynth.Fm)
---------------------------------------------------------------------
  Six sine operators wired together by one of 32 algorithms. An operator whose
  output is HEARD is a carrier; one whose output is added to another operator's
  PHASE is a modulator, and that phase bending is what turns a sine into a
  timbre. The algorithm decides which is which.

  This is the waveform that MEANS the most by IModestVoiceOscillator: it takes a
  velocity at note-on and runs six envelopes down after note-off, where a sine
  simply keeps running. Every waveform here answers to that interface, so nothing
  has to special-case it.

      var voice = new Fm6OpOscillator { Algorithm = 5 };
      voice.SetSampleRate(48000);
      voice.SetFrequency(261.63);
      voice.GetOperator(1).Level = 0.9;      // carrier
      voice.GetOperator(2).Level = 0.55;     // its modulator
      voice.GetOperator(2).Ratio = 14.0;
      voice.Reset(0.0);                      // phases
      voice.NoteOn(100);                     // velocity
      voice.Render(block);                   // ... and again, and again
      voice.NoteOff();                       // render on until voice.IsFinished

  THE OSCILLATOR
    int  Algorithm              1..32, clamped. Default 1. Safe to change
                                mid-note: phases and envelopes are kept, so a
                                bound knob morphs rather than restarting.
    Fm6OpAlgorithm Topology     the routing the algorithm selects (below)
    double OutputGain           what the carrier sum is multiplied by.
                                Default 0.6, the MEASURED level of the reference
                                player's fm6op oscillator.
    double ModulationDepthCycles   how far a modulator at full level pushes its
                                target's phase, in cycles. Default 2.0, which is
                                a modulation index of 4*pi at level 1.0 - the
                                MEASURED value, reproducing the reference's whole
                                Bessel sideband pattern to within 1.5 dB.
    double FeedbackDepthCycles  the same for the feedback loop. Default 1.0.
    bool UseOperator6FeedbackFallback   default FALSE; see FEEDBACK below.
    int  RateScaling            0..7, default 0 (off). Speeds the four-stage
                                envelopes up towards the top of the keyboard.
                                The format has NO attribute for this; it is here
                                so an imported hardware patch can keep its
                                key-follow.
    double EffectiveFeedback    the feedback amount actually in use, refreshed
                                at the top of every rendered block
    int  Velocity               the velocity the current note started at
    bool IsKeyDown / IsFinished
    bool UsesOuterRelease       true when a carrier that can be heard has the -1
                                release sentinel, so the group's envelope has to
                                end the note
    bool OperatorUsesOuterRelease(1..6)  the same question, per operator
    ModestFmOperator GetOperator(1..6)
    IReadOnlyList<ModestFmOperator> Operators
    double GetOperatorFrequencyHz(1..6)   what an operator is actually running
                                at, after ratio or fixed frequency and detune
    void ApplyPatch(ModestPatch)   copies the algorithm and all six operators

    Parameters are read ONCE PER BLOCK, at the top of Render. Change anything
    between blocks and it lands on the next one; nothing is read per sample and
    nothing allocates.

  PER-OPERATOR PARAMETERS   (fmOpNRatio and friends, N = 1..6)
    Ratio               fmOpNRatio. Multiplies the played note's frequency. 2.0
                        is an octave up, 0.5 an octave down. DEFAULTS TO THE
                        OPERATOR'S OWN NUMBER, which is measured: with no ratios
                        written, algorithm 32's six carriers land on 1 to 6 times
                        the note. Negative and non-finite values are ignored.
    Detune              fmOpNDetune, -7..7, clamped. Default 0. MEASURED as a
                        power law in the operator's own frequency:
                        offsetHz = detune * 0.005341 * f0^0.630, which is 2.47
                        cents per step at MIDI 24 falling to 0.69 at MIDI 84. So
                        it is neither a fixed number of Hz nor a fixed interval -
                        a strong detune low down and a slight one high up, which
                        is what the format describes, but not linearly.
    Mode                fmOpNMode: Ratio (default) or Fixed.
    FixedFrequency      fmOpNFixedFreq, in Hz. Default 440.0. Used only in Fixed
                        mode, where the note played is ignored. Detune still
                        applies.
    Level               fmOpNLevel, 0..1, clamped. What the operator
                        contributes: audio if the algorithm makes it a carrier,
                        modulation if it makes it a modulator. Default 1.0 on
                        OPERATOR 1 and 0.0 on operators 2 to 6.
    VelocitySensitivity fmOpNVelocitySensitivity, 0..7, clamped. Default 0, no
                        velocity response at all. MEASURED as a five-point
                        decibel table scaled by the sensitivity number: -11.2,
                        -3.40, -1.50, 0 and +0.74 dB per unit at velocities 1,
                        32, 64, 96 and 127. THE NEUTRAL POINT IS VELOCITY 96, not
                        127, so a hard note is pushed UP rather than merely left
                        alone, and sensitivity 7 at velocity 1 is silence.
    Feedback            fmOpNFeedback, 0..1, clamped. Default 0. See FEEDBACK.
    Attack Decay        fmOpNAttack / fmOpNDecay, in SECONDS. Default 0.0 each.
    Sustain             fmOpNSustain, 0..1 amplitude. Default 1.0.
    Release             fmOpNRelease, in seconds. Default -1.0, the SENTINEL.
    EnvelopeType        fmOpNEgType: Adsr (default) or Dx7.
    EgRate1..EgRate4    fmOpNEgRateN, 0..99. Defaults 99, 99, 0, 99.
    EgLevel1..EgLevel4  fmOpNEgLevelN, 0..99. Defaults 99, 99, 99, 0.

    The default operator levels differ from the format's own attribute table,
    which says every operator defaults to 1.0. Three things say that table is
    wrong: the reference player renders a PURE SINE for an fm6op oscillator
    carrying no fm* attributes, which six operators at full level could not
    produce; the format's own tutorial says to raise fmOp2Level "from 0 to 1 to
    hear FM modulation build from a sine wave"; and a measurement round
    confirmed operator 1 at 1.0 and the rest at 0.0 outright. CodeBrix.Audio's
    own DecentSamplerZone now resolves the same defaults, so a preset played
    through the sampler engine and a patch built here agree. A preset that names
    its levels is unaffected either way.

  ALGORITHMS
    Fm6OpAlgorithms.Get(1..32) returns an Fm6OpAlgorithm describing the routing:
      IReadOnlyList<int> Carriers        the operators that are heard
      IReadOnlyList<int> Modulators      the operators that are not
      IReadOnlyList<int> RenderOrder     modulators before what they feed
      int FeedbackSource / FeedbackDestination
      bool IsCarrier(n) / Modulates(m, n) / GetModulatorsOf(n)

    The numbering is the published chart's numbering, so an algorithm number
    from a vintage patch bank can be used as it stands. Landmarks: 1 is two
    chains (2 into 1, and 6 into 5 into 4 into 3); 5 is three carrier and
    modulator pairs; 19 sends one operator 6 into both 4 and 5; 32 is six
    carriers and no modulation at all, which makes the engine additive.

    Carrier counts run 1 (algorithms 16, 17, 18) to 6 (algorithm 32). There is
    NO normalisation by the number of carriers, which is why six loud operators
    clip: turn the carriers down, or the group's volume.

  FEEDBACK
    Each algorithm has exactly one feedback loop, and the operator it arrives at
    is operator 6 in 19 of the 32 algorithms. Two of the loops span several
    operators (algorithm 4's runs 6-5-4 and back into 6, algorithm 6's runs 6-5
    and back), which is why FeedbackSource and FeedbackDestination are separate.

    The format says both that only the algorithm's own feedback operator is
    audible and that fmOp6Feedback works in all 32 algorithms. MEASURED: the
    FIRST is what the reference does - feedback written on any operator but the
    algorithm's own is silently ignored - and the value is CLAMPED AT 1.0, with
    settings of 1, 3 and 7 rendering bit-identically. So
    UseOperator6FeedbackFallback is OFF by default; set it true for a patch bank
    that relies on the second reading, and operator 6's value then stands in
    wherever the algorithm's own operator carries none. EffectiveFeedback reports
    which value won.

    0.0 to 0.15 warms the tone, 0.3 to 0.6 turns it sawtooth-like, and above 0.7
    it breaks into noise, as the format describes. The loop is averaged over two
    samples, which is what keeps it from screaming at Nyquist.

  THE TWO ENVELOPES
    ADSR (EnvelopeType = Adsr, the default). Attack, Decay and Release are
    SECONDS and Sustain is an amplitude. Straight-line segments: silence to full
    over Attack, full to Sustain over Decay, hold, then the level it was at down
    to silence over Release. Attack 0 starts at full on the first sample.

    FOUR-STAGE (EnvelopeType = Dx7). Rates and levels are the hardware's 0..99
    integers and can be copied straight out of a vintage patch bank. R1 climbs
    from silence to L1, R2 moves to L2, R3 moves to L3 and holds there while the
    key is down, and R4 takes it to L4 after note-off. Movement is along the
    LEVEL scale, where 99 is full amplitude and every 8 units below it halves
    the amplitude, so a decay is a constant number of decibels per second.
    Setting EgType to Dx7 makes the operator IGNORE its Attack, Decay, Sustain
    and Release.

    MEASURED: every 5.5 rate units doubles the time a stage takes - the
    reference's rate 20 reached 99 % of its level in 1.99 s and its rate 50 in
    78 ms, a factor of 45 over 30 rate units. Rate 99 covers the whole range in a
    fraction of a millisecond, which the measurement could only bound as "below
    12 ms". AND RATE 0 DOES NOT HOLD: it CRAWLS, covering the whole range in
    about 5.5 s, where the format's own description promises a hold. The default
    R3 of 0 therefore drifts rather than sitting still - give R3 a real rate and
    L3 a real level if you want a stage that stays put.
    Dx7Tables exposes all of it: FullSweepSeconds(rate), LevelUnitsPerSecond,
    LevelUnitsToAmplitude, AmplitudeToLevelUnits, DetuneHz, VelocityScale,
    VelocityDecibels, RateScalingUnits.  [Still reasoned: the 0-99 LEVEL scale
    and the rate scaling, which the format has no attribute for.]

    An L4 above 0 means the operator never goes silent by itself. That is the
    hardware's behaviour, and it means IsFinished stays false until something
    else stops the voice.

  THE -1 SENTINEL
    Release = -1 (the DEFAULT) means "I have no release of my own - the group's
    envelope decides when this note ends". After NoteOff such an operator HOLDS
    its level, and IsFinished never becomes true, because the thing that decides
    is outside the oscillator. Give the operator a real release, or let the
    group's amplitude envelope gate the voice, which is what a preset does.

    Attack = -1 goes further: the operator has no envelope at all and sits at
    full level, gated only by the group. Any negative value becomes the
    sentinel. Both apply to the ADSR only - in four-stage mode R4 and L4 decide.

    UsesOuterRelease is the flag to honour: it is true when a carrier that can
    be heard is in that state, which is exactly when IsFinished will never fire
    and something else has to stop the voice.

  WHAT IS MEASURED, AND WHAT IS NOT
    MEASURED. An fm6op oscillator with no fm* attributes renders a pure sine at
    the note's frequency, 4.44 dB below a plain sine oscillator; that is
    reproduced exactly, as operator 1 alone at full level through an output gain
    of 0.6 (only the PRODUCT of those two is measured, not either one). So are
    the modulation depth a level of 1.0 buys (beta = 4*pi*level, checked against
    the reference's Bessel sidebands over six lines); the carrier set and the
    direct modulation matrix of all 32 algorithms; fmOpNRatio defaulting to the
    operator's own number; the absence of any carrier-count normalisation; the
    five-point velocity table with its neutral point at velocity 96; the
    four-stage rate scale at 5.5 rate units per halving, with rate 0 crawling;
    the detune power law 0.005341 * f0^0.630 over MIDI 24 to 84; and feedback
    acting only on the algorithm's own operator and clamping at 1.
    MEASURED UNDER LOAD, and no longer inherited from the chart: THE MODULATOR-
    TO-MODULATOR CHAINS ARE REAL, and each link is carried by the intermediate
    operator's OWN OUTPUT scaled by its level. In algorithm 1 the path is
    op6 -> op5 -> op4 -> op3, and an operator whose downstream neighbour is
    silent contributes NOTHING: op5 at level 1.0 with op4 down was bit-identical
    to the bare carrier, and op6 at 1.0 with op5 down was bit-identical to op4
    alone. Raising the whole ladder moved the spectral centroid from 110 Hz to
    7261 Hz and the line count from 1 to 89 while the level stayed inside 1.9 dB:
    a chained modulator changes the timbre, not the loudness. The per-operator
    ADSR also runs independently on all six operators at once - six different
    sustains landed on 20*log10(sustain) exactly - and the voice's tail is the
    longest OPERATOR release rather than the group envelope's.
    STILL REASONED RATHER THAN MEASURED, each a single named constant: the
    feedback DEPTH in cycles, the 0-99 level scale's curve, the detune law
    outside MIDI 24 to 84, and rate scaling (which the format does not have at
    all). The chains are measured for algorithm 1, whose depth is four; the
    algorithms whose chains are deeper or shallower have not been walked.


THE CREATIVE EFFECTS
====================
Seven effects, in CodeBrix.Audio.ModestSynth.Effects:

  phaser   pitch_shift   wave_folder   wave_shaper   stereo_simulator
  bit_crusher   gate

They are the NONLINEAR and CREATIVE half of the format's effect set. The mixing
and room half - lowpass, lowpass_1pl, bandpass, highpass, notch, peak, gain,
reverb, delay, chorus, convolution and compressor - belongs to CodeBrix.Audio
itself, so a sample library that uses only those plays in full without this
package.

THE CONTRACT
------------
Every effect implements CodeBrix.Audio's IInstrumentEffect, from
CodeBrix.Audio.Synth.DecentSampler.Engine, so the same object serves a Decent
Sampler chain and your own code:

  bool Enabled                  false bypasses; the block passes through and the
                                effect's internal state is left as it was
  IReadOnlyList<string> Tags    the <effect> element's tags. Never null; setting
                                null clears it
  void Prepare(int sampleRate)  allocates. Call it once, off the audio thread,
                                before the first block
  void Reset()                  clears every buffer, leaves every parameter
  void Process(float[] left, float[] right, int frames)      in place
  bool TrySetParameter(string name, double value)
  bool TryGetParameter(string name, out double value)
  bool TrySetParameter(string name, string value)

Two more members come from this package's own base class, ModestEffectBase:

  int  SampleRate               what it is prepared for
  bool IsPrepared               whether Prepare has run

  Playing one:

      using CodeBrix.Audio.ModestSynth.Effects;
      using CodeBrix.Audio.Synth.DecentSampler.Engine;

      var crusher = new BitCrusherEffect { BitDepth = 6, SampleRateReduction = 4 };
      crusher.Prepare(48000);

      float[] left  = new float[512];
      float[] right = new float[512];
      // ... fill both buffers ...
      crusher.Process(left, right, 512);      // in place

PARAMETER NAMES. Every effect answers to BOTH spellings of each parameter: the
XML attribute the effect page documents ("modRate") and the binding parameter
the appendix documents ("FX_MOD_RATE"). Matching ignores case, punctuation and
the FX_ prefix, so "modrate", "MOD-RATE" and "fx mod rate" are all the same
parameter. "ENABLED" works on every effect, as a number (0 or 1) or as a word
("true" / "false").

  * An unknown name returns false rather than being swallowed, which is how a
    host can tell a typo from a parameter it simply does not use.
  * An out-of-range value is CLAMPED, not rejected: a preset attribute out of
    range is never worth an exception.
  * NaN and infinity leave the parameter where it was, and still return true if
    the NAME was one the effect knows.

DEFAULTS. Every effect has a parameterless constructor that carries the
developer guide's documented defaults, so `new PhaserEffect()` is exactly what
`<effect type="phaser" />` means.

BUILDING ONE BY NAME. ModestEffectFactory.Create("bit_crusher") builds an effect
from a type name, TryCreate does the same without throwing, IsSupported asks
first, and SupportedTypes lists all seven. ModestEffectTypes holds the seven
names as constants. Create THROWS NotSupportedException for a core effect name
like "reverb", with a message saying where it actually lives.

AUDIO-THREAD HYGIENE. Process allocates nothing, locks nothing and opens
nothing, once Prepare has run. An effect that is processed without ever having
been prepared prepares itself at 44,100 Hz on its first block rather than
throwing - which is a convenience, not a licence: if you are rendering at
another rate, that block is the wrong rate and every one after it.

THREADING. An effect instance belongs to the thread rendering it. Nothing is
thread-safe and nothing needs to be: a group chain is instantiated once per
voice, and an instrument or bus chain is owned by whatever renders it.

TWO BUFFERS, NOT ONE. Process writes both channels, so passing the same array
twice throws ArgumentException rather than silently processing it twice.

THE MIX. Six of the seven have a Mix, and it always means the same thing:
out = (1 - mix) * dry + mix * wet. A mix of exactly 0 passes the block through
bit for bit. The stereo simulator has no mix; its `width` does that job.


phaser  -  PhaserEffect
------------------------
  Parameter        Range        Default   Property
  mix              0 .. 1       0.5       Mix
  modDepth         0 .. 1       0.2       ModDepth
  modRate          0 .. 10 Hz   0.2       ModRate
  centerFrequency  20 .. 22000  400 Hz    CenterFrequency
  feedback         -1 .. 1      0.7       Feedback

  Bindings: FX_MIX, FX_MOD_DEPTH, FX_MOD_RATE, FX_CENTER_FREQUENCY, FX_FEEDBACK.

  A cascade of SIX first-order all-pass sections all tuned to centerFrequency,
  added back to the dry signal. MEASURED off the reference's own noise transfer
  function with the oscillator frozen: six sections turn the signal through half
  a cycle at three frequencies, so three notches sit at 0.268, 1.00 and 3.73
  times the corner - the reference's landed on 108/401/1486 Hz at a 400 Hz
  centre, 269/999/3655 at 1 kHz and 1098/4000/11639 at 4 kHz, with the middle
  one exactly on the setting. Four sections would space them 2.414 apart and
  eight 5.03; the measured ratio is 3.71.

  THE SWEEP is exponential and reset at every note-on:
  fc(t) = centerFrequency * 2^(5 * modDepth * -sin(2*pi*modRate*t)), i.e. FIVE
  OCTAVES EACH WAY at full depth, and DOWNWARD first. modRate="0" therefore
  freezes the chain at centerFrequency exactly, whatever the depth says.
  PhaserEffect.AllPassStageCount and PhaserEffect.SweepOctavesAtFullDepth are
  those two numbers, as constants.

  `mix` is a crossfade: total cancellation at 0.5, a flat response at 1.0 - an
  all-pass chain has unit magnitude - and nothing at all at 0.

  FEEDBACK IS SUBTRACTED, not added. That is what the reference measured:
  feedback="0.7" turned its notches into PEAKS of about +1.5 dB and cost about
  2 dB elsewhere, which is exactly A/(1 + k*A) at A = -1 and A = +1. Adding the
  feedback would have deepened the notches instead. A NEGATIVE feedback measured
  identical to zero, so negative values act as none, and the value is held at
  0.99 while processing: an all-pass loop at a gain of exactly one never decays.
  A phaser with feedback is not level-neutral.


pitch_shift  -  PitchShiftEffect
---------------------------------
  Parameter    Range        Default   Property
  pitchShift   -24 .. 24    0         PitchShift    (semitones)
  mix          0 .. 1       0.5       Mix

  Bindings: FX_PITCH_SHIFT, FX_MIX.

  The guide's "old-school pitch shifter": a 50 ms delay line read by two grains
  half a grain apart, each sliding towards or away from the write pointer at
  2^(semitones/12) - 1 samples per sample. Each grain is windowed by a raised
  cosine that reaches zero exactly where it wraps, and the pair sums to one, so
  nothing steps and nothing drops out. GrainLengthSamples reports the grain at
  the prepared rate.

  MEASURED. The tuning is exact - +7 semitones on 440 Hz gives 659.26 Hz here
  and the reference gave 659.8 - and the level is preserved. `mix` is a
  crossfade: at 1.0 there is no trace of the original pitch left.

  UNMEASURED. The reference's grain length, its window, and whether it delays
  the DRY path to match the wet one. This one does NOT delay the dry path, so at
  a mix between 0 and 1 the two are a grain apart and comb against each other,
  the way an analogue-era shifter does. The wet path therefore carries about
  50 ms of latency at any setting, including pitchShift="0", where the effect
  degenerates to a plain half-grain delay.

  The classic cost of the classic method is a warble at the grain rate - about
  ten hertz at seven semitones. That is the method, not a defect; a phase
  vocoder would trade it for smearing instead.


wave_folder  -  WaveFolderEffect
---------------------------------
  Parameter    Range       Default   Property
  drive        1 .. 100    1         Drive
  threshold    0 .. 10     0.25      Threshold
  mix          0 .. 1      1.0       Mix        (undocumented; see below)

  Bindings: FX_DRIVE, FX_THRESHOLD, FX_MIX.

  MEASURED IN CLOSED FORM. With k = drive / (2*sqrt(2)*threshold):

      foldOnce(u) = u for |u| <= 1, else sign(u) * (2 - |u|)
      out = foldOnce(in * k) / k

  Three things in that are not the textbook triangle folder this used to be. It
  is a SINGLE reflection that keeps going down past zero rather than a repeated
  triangle, and it is NOT clamped - the output is not bounded by the threshold.
  The output is divided by the same k the input was multiplied by, so BELOW THE
  FOLD POINT THE EFFECT IS A BIT-IDENTICAL PASS-THROUGH: drive does not even
  amplify until the signal reaches the fold. And the threshold is scaled by
  2*sqrt(2) - the same constant the reference uses on the compressor's threshold.

  The fold point is therefore at 1/k, which at the documented defaults of
  drive="1" threshold="0.25" is 0.707: a signal quieter than that comes out
  untouched. An input of 2/k folds to exactly zero.

  The form reproduces all seven measured root-mean-square points to a mean error
  of 0.01 dB and the harmonic structure of three cases to 0.6 dB, over a range in
  which the third harmonic travels from -14 dB to +24 dB relative to the
  fundamental.

  The guide gives the wave folder no `mix` attribute; one is accepted here and
  defaults to 1.0, so a preset written with only the documented attributes
  behaves exactly as documented.

  The binding appendix gives FX_THRESHOLD a range of 1 to 100, contradicting the
  effect page's own 0 to 10. The effect page wins. Since a threshold above the
  signal's own peak means "no folding at all", the two agree on everything
  audible.

  Because folding is per-voice by nature, the guide's own advice is to put a
  wave folder at GROUP level, where the engine builds one per voice.


wave_shaper  -  WaveShaperEffect
---------------------------------
  Parameter     Range          Default   Property
  drive         1 .. 1000      1         Drive
  driveBoost    0 .. 1         1         DriveBoost
  outputLevel   0 .. 8         0.1       OutputLevel
  highQuality   true / false   false     HighQuality
  mix           0 .. 1         1.0       Mix        (undocumented; see below)

  Bindings: FX_DRIVE, FX_DRIVE_BOOST, FX_OUTPUT_LEVEL, FX_MIX.

  A saturating curve: out = tanh(drive * (1 + driveBoost) * in) * outputLevel.

  OUTPUT LEVEL DEFAULTS TO 0.1, A 20 dB CUT. That is the format's own default,
  confirmed by measurement, and it is not a bug in this code. A preset that
  raises outputLevel is asking for a large boost.

  MEASURED. The reference at drive="10" with outputLevel="1.0" ADDED 16.4 dB to
  white noise; this curve adds 15.8 dB on the same material, inside the plan's
  1.5 dB tolerance for an effect. That match is what fixes the drive law,
  including how much driveBoost is worth: the constant is
  WaveShaperEffect.DriveBoostRange, and driveBoost="1" doubles the drive.

  UNMEASURED. Everything away from that one point. The measurement round rated
  the wave shaper LOW confidence because it never swept the input level, so
  treat the curve as right where it was measured and provisional elsewhere.

  HIGH QUALITY is four-times oversampling through a pair of half-band filters in
  each direction. It pushes the aliases a hard drive would fold back into the
  audible band down by about 19 dB, measured on a 10 kHz tone, and it costs four
  evaluations of the curve plus twelve filter runs per sample - the guide's own
  warning about CPU. It also adds fifteen samples of latency to the wet path,
  which the dry path does not carry; that latency is this implementation's and
  is not measured against the reference.

  The effect page gives outputLevel a range of 0 to 1 and the binding appendix
  0 to 8; the wider one is honoured, so a binding written to the appendix is not
  silently clipped.

  Like the folder, this belongs at GROUP level in a preset.


stereo_simulator  -  StereoSimulatorEffect
-------------------------------------------
  Parameter    Range               Default   Property
  algorithm    lauridsen |         adt       Algorithm (ModestStereoAlgorithm)
               schroeder | adt
  width        0 .. 1              0.5       Width
  delayTime    0.001 .. 0.030 s    0.005     DelayTime
  modRate      0.1 .. 10 Hz        0.5       ModRate    (adt only)
  modDepth     0 .. 1              0.3       ModDepth   (adt only)

  Bindings: FX_WIDTH is the one the guide names; FX_DELAY_TIME, FX_MOD_RATE,
  FX_MOD_DEPTH and FX_ALGORITHM are accepted too, the last of them as text
  ("lauridsen", "schroeder", "adt") or as the enum's number.

  One signal becomes two: the input is summed to mono, a decorrelated copy of it
  is added on the left and subtracted on the right, and `width` decides how much
  of the original is left in the middle.

    middle = (1 - width) * mono          side = width * sideGain * decorrelated

  A STEREO INPUT IS SUMMED TO MONO FIRST, so it loses its own side signal. That
  is what "converts a mono input signal into a pseudo-stereo signal" means, and
  it is why width="0" is documented as "mono/dry": at 0 the output is the mono
  sum in both channels, unchanged.

  MEASURED, AND MATCHED EXACTLY. All three algorithms at width="0.5" put the
  reference player's mono sum 6.02 dB down, which is the (1 - width) law above.
  The three differ only in how far they decorrelate, and the reference's order
  is reproduced - measured the way the reference measured it, on a 440 Hz tone:

                    reference   here
    lauridsen         0.000      0.000    fully decorrelated
    adt               0.606      0.605    the default, and the middle of the three
    schroeder         0.951      0.996    the subtlest

  (Those are left/right correlations: 0 is fully decorrelated, 1 is mono.)

  UNMEASURED. The delay lengths inside each algorithm, schroeder's second delay
  (1.7 times the first here), the modulation shape, and what the reference does
  with a stereo input. The three side gains that produce the ordering above are
  constants in the class, chosen against the measurement.

  At width="1" the middle is removed entirely and the mono sum is silent. That
  is the measured law taken to its end, not a separate rule.


bit_crusher  -  BitCrusherEffect
---------------------------------
  Parameter             Range      Default   Property
  bitDepth              1 .. 24    24        BitDepth
  sampleRateReduction   1 .. 32    1         SampleRateReduction
  mix                   0 .. 1     1.0       Mix

  Bindings: FX_BIT_DEPTH, FX_SAMPLE_RATE_REDUCTION, FX_MIX.

  The defaults are transparent: 24 bits is finer than a float can hold and a
  reduction of 1 holds nothing.

  BIT DEPTH is a MID-TREAD quantiser with NO DITHER - the signal is rounded to
  the nearest multiple of

      STEP = 2*sqrt(2) / 2^(bitDepth-1)   =  2^(2.5 - bitDepth)

  and zero is always one of the levels. SAMPLE-RATE REDUCTION is a
  sample-and-hold: a factor of four holds each sample for four. Both accept
  fractional values, because a knob bound to FX_BIT_DEPTH sweeps through them;
  the hold length then alternates rather than jumping.

  THE FULL SCALE IS 2*sqrt(2), NOT 1, which is the same constant the compressor
  threshold and the wave folder use. It has one consequence worth knowing before
  you reach for a low bit depth: A QUIET SIGNAL CAN BE CRUSHED TO DIGITAL
  SILENCE. Anything whose PEAK is below STEP/2 = 2^(1.5-bitDepth) rounds to zero
  everywhere - 0.1768 at bitDepth 4, 0.01105 at 8, 0.00069 at 12 - so a note at
  -20 dBFS disappears at four bits while the same note ten decibels louder
  crushes as expected. The class exposes the constant as
  BitCrusherEffect.FullScale.

  MEASURED. The step was read directly off the reference's output staircase at
  bit depths 3, 4 and 8 - 0.70846, 0.35431 and 0.022087 internal, against the
  law's 0.70711, 0.35355 and 0.022097 - and the plateaus are exact and
  repeatable, so there is no dither. The apparent "gain" of a crusher follows
  from the step rather than being a parameter: +1.21 dB where the signal spans
  three levels, +0.44 dB where it spans five, 0.00 dB once the step is small.
  This effect reproduces the reference's whole 12-cell level sweep - four input
  levels at bit depths 4, 8 and 12 - to 0.05 dB, digital silence included.

  sampleRateReduction is level-neutral at every input level (0.02 dB over
  factors of 2, 8 and 32) and purely spectral: it pulled the reference's
  spectral centroid from 7.5 kHz to 2.1 kHz on noise at a factor of 8, and moves
  a 440 Hz sine to about 1030 Hz at 32.

  STILL UNMEASURED: bitDepth 1 and 2, where the step exceeds the signal range,
  and a non-periodic input to rule out dither hiding under a periodic one.


gate  -  GateEffect
--------------------
  Parameter   Range     Default   Property
  amount      0 .. 1    0.5       Amount
  mix         0 .. 1    1.0       Mix
  (none)                          Seed, WindowSeconds - standalone extras

  Bindings: FX_GATE_AMOUNT (the attribute is "amount"; both spellings work),
  FX_MIX.

  Roughly every fifty milliseconds the gate flips a weighted coin on whether to
  let the signal through or cut it to silence, crossfading over two milliseconds
  at each decision so the transitions do not click. `amount` is the probability
  of CLOSING, so 0 never gates and 1 gates constantly.

  DETERMINISM. The coin is a seeded generator: two gates with the same Seed
  render the same dropouts, and Reset restarts the sequence. Setting Seed
  restarts it immediately. Built from a preset, the seed is derived from the
  effect's position in its chain, so two gates in one instrument do not stutter
  in lock step and the same instrument rendered twice stutters identically.

  CurrentGain reports the gain the gate is applying right now, 0 to 1.

  MEASURED. The reference at amount="0.5" lost 2.4 dB of average level and
  dropped 30 dB inside a two-second note; this one loses 2.6 dB with a duty
  cycle of 0.52.

  UNMEASURED. The window length is documented as "roughly every 50 milliseconds"
  and was never measured, nor was the crossfade length, nor whether the window
  length is itself randomised. Fixed 50 ms windows with 2 ms raised-cosine edges
  are what this implementation uses; WindowSeconds (0.001..1.0) exposes the
  first of those for standalone use, and GateEffect.EdgeSeconds is the second.


COMMON PITFALLS
===============
  * REGISTER BEFORE LOADING. Waveforms and effect types are resolved while an
    instrument is BUILT INTO A SYNTHESIZER, not while its file is parsed.
    ModestSynth.Register() after the fact does not retrofit an instrument that is
    already loaded, and it does not retrofit a synthesizer that already exists -
    reload the instrument and build the synthesizer again. The symptom of getting
    this wrong is a silent oscillator group and a bypassed effect, with the
    reason in Problems: nothing throws.

  * ONE OSCILLATOR PER VOICE. An oscillator is per-voice state. Sharing one
    across two notes produces one note with a confused pitch.

  * SetSampleRate BEFORE the first Render, and not between blocks. That call is
    where allocation happens; Render is deliberately allocation-free and stays
    that way only if the rate was settled first.

  * pluck1's Reset IS the note-on. Without it the string is silent, because
    there is nothing in the delay line to ring. Rendering a pluck1 that was
    never Reset returns zeros, not a hum.

  * pluck1 pitch has limits. Below 8 Hz or above a third of the sample rate,
    SetFrequency throws. That range covers every MIDI note at every ordinary
    sample rate, but clamp before you call it if your source of pitch can
    produce anything at all.

  * Damping is a decay time, not a tone control. If you want a darker string,
    lower PluckType or filter the output; lowering Damping shortens the note.

  * Seeds decide whether layers thicken or double. Two noise or pluck1 voices
    with the same seed and the same pitch render the same samples and sum to one
    louder copy. Set ModestPatch.Seed per voice, or turn on RandomPhase.

  * RandomPhase means nothing to noise. It has no phase to randomise. It does
    matter to sine, saw, square, triangle, wavetable and to a pluck1 excitation,
    and the format's own guidance is to turn it ON whenever wavetable groups are
    layered.

  * A WAVETABLE PATH IS RELATIVE TO THE PRESET, not to your process. The format
    writes wavetableFile relative to the .dspreset file; ModestPatch hands it to
    the loader as it stands, and a relative path there resolves against the
    current directory. Combine it with the preset's own folder first, or set
    ModestPatch.WavetableTable and skip the loader entirely. INSIDE A PRESET this
    is already done for you: the adapter resolves the path through the
    instrument's own container, archives included.

  * THE FIRST NOTE OF A WAVETABLE GROUP READS A FILE. Voices are pooled per zone,
    so a group's first few notes build their sources and the first of them
    decodes the table; after that everything is cached and the render path
    touches nothing. If that first note falls in a place where file I/O is
    unacceptable, warm WavetableFileCache before you start playing.

  * A wavetable with no usable file is a SINE, not silence, and it does not
    throw. Check WavetableOscillator.Problem and report it; the sound is
    otherwise indistinguishable from a deliberate sine oscillator.

  * Load wavetables through WavetableFileCache. A table costs about 4.5 times
    its frame data in memory and tens of milliseconds to band-limit; doing that
    per voice instead of per file is the difference between 9 MB and 150.

  * A harmonic oscillator with NO partial levels is a sine at full level, which
    is the reference player's behaviour. Setting any one level ends that, so
    setting only harmonicPartial4Level gives you the fourth partial alone, not a
    fourth partial added to a fundamental.

  * numPartials does not add partials, it removes them. Raising it does nothing
    unless the partials above the old limit have levels set.

  * Band-limited shapes overshoot slightly. A saw or square corrected at its
    edges can exceed 1 for a sample or two. Leave headroom, or apply the voice
    gain you were going to apply anyway, rather than assuming a hard 1.0 bound.

  * formant IS LOUD, and it has no parameters. It renders the reference's own
    fixed tone at the level the reference renders it - 5.85 dB above what a sine
    makes at the same volume - and every formant-* attribute a preset writes is
    ignored, by the reference and by this package alike. Give the group its own
    volume attribute. The core's feature list still reports the waveform as
    unrecognised, because the guide it is built from does not document it.

  * fm6op needs a NOTE, not just a Reset. Reset starts it at full velocity so an
    IModestOscillator consumer still gets sound, but velocity only means
    something once NoteOn has been called, and nothing is released until
    NoteOff.

  * fm6op's IsFinished is usually false forever, and that is correct. The
    format's default release is the -1 sentinel, which hands the ending to the
    group's envelope. A standalone caller that wants the voice to stop on its
    own must give at least one carrier a real Release, or a four-stage envelope
    whose L4 is 0.

  * fm6op operators 2 to 6 start SILENT. Set the levels of the operators your
    algorithm actually uses; a patch that sets only fmOp1Level is a sine.

  * fm6op does not normalise by carrier count. Algorithm 32 with six operators
    at full level peaks near 3.6, not 1.0. Lower the carriers or the group
    volume, exactly as the format warns.

  * Out-of-range parameters clamp silently. That is deliberate and matches the
    rest of the family, but it means a value you set may not be the value you
    read back. Read the property back if it matters.

  * THE ZONE WINS OVER TrySetParameter. Inside a preset the resolved zone is the
    source of truth, and the adapter re-reads it whenever a binding fires. A value
    written straight onto a voice through IVoiceSource.TrySetParameter therefore
    stands until the next binding change and is then replaced by the zone's own.
    Drive a preset through its bindings and controls; TrySetParameter is for a
    host that has no bindings at all.

  * ModestSynthesizer READS THE PATCH ONCE. Every voice is built with its own
    oscillator when the synthesizer is constructed. Changing the patch afterwards
    changes nothing you hear - build another synthesizer.

  * A SYNTHESIZER BUILT AT THE WRONG RATE IS TRANSPOSED. MidiMusicPlayer renders
    through the shared device at whatever rate that device settled on. Prefer
    Load(rate => new ModestSynthesizer(patch, ...), sequence), which is handed
    that rate, over Load(synthesizer, sequence), which cannot change it.

  * AN ADDITIVE STACK STILL NEEDS HEADROOM. HarmonicNormalization is the
    measured sum divide, (1 - n) + n / sum(gains), so full compensation holds
    the summed PEAK where a single full-level partial's would be - it says
    nothing about a CHORD, whose peaks add on top of that. Leave headroom, or
    lower the master volume as ModestSynthPresets does for its organ.

  * PREPARE AN EFFECT BEFORE THE FIRST BLOCK. An effect that was never prepared
    prepares itself at 44,100 Hz, which is silently wrong if you are rendering
    at anything else. Prepare is also the only member that allocates, so calling
    it between blocks puts an allocation on the audio thread.

  * AN EFFECT NEEDS TWO DIFFERENT BUFFERS. Passing the same array as both
    channels throws, because Process writes both.

  * wave_shaper's outputLevel is 0.1 by DEFAULT, a 20 dB cut. If a shaper sounds
    too quiet, that is the format's default doing its job, not a fault.

  * highQuality is not free. Four-times oversampling costs four evaluations of
    the curve plus twelve filter runs per sample and adds fifteen samples of
    latency to the wet path. Turn it on where the drive is high and leave it off
    everywhere else.

  * stereo_simulator SUMS ITS INPUT TO MONO. Putting one at the end of an
    instrument chain collapses whatever stereo image was already there.

  * A gate is random, but it is not unpredictable. Give every gate its own Seed
    if you want two of them to stutter differently, and expect the SAME
    dropouts every time you render - which is the point.

  * A PHASER WITH FEEDBACK IS NOT LEVEL-NEUTRAL. The reference subtracts its
    feedback, which turns the notches into resonant peaks and costs about 2 dB
    elsewhere; at mix="1.0" the resonances are all that is left. That is measured
    behaviour, not a fault.

  * The wave folder is a PASS-THROUGH until the signal reaches its fold point,
    which is drive / (2*sqrt(2)*threshold) reciprocated. A preset that raises
    drive and hears nothing change has not reached it yet.


QUICK REFERENCE
===============
  Turn it on for presets   ModestSynth.Register()             (once, before loading)
                           ModestSynth.Register(registry)      (your own registry)
  Did it run?              ModestSynth.IsRegistered
  What did it offer?       ModestSynth.RegisteredOscillatorWaveforms
                           ModestSynth.RegisteredEffectTypes

  Make one                 new SawOscillator()
                           ModestOscillatorFactory.Create(ModestWaveform.Saw)
                           patch.CreateOscillator(48000)
  Play one                 SetSampleRate -> SetFrequency -> Reset -> Render
  Play it as a NOTE        ... -> NoteOn(velocity) -> Render -> NoteOff ->
                           Render until IsFinished  (every waveform)

  Play a whole patch       new ModestSynthesizer(patch, settings)
                           synthesizer.NoteOn(0, 60, 100)
                           synthesizer.Render(left, right)
  Example patches          ModestSynthPresets.Create("electric piano")
                           ModestSynthPresets.SettingsFor("electric piano", 48000)
                           Names: sub sine, saw lead, plucked string,
                           electric piano, wavetable pad, additive organ
  Render one offline       SoundFontRenderer.Render(synthesizer, sequence, tail)
                           SoundFontRenderer.RenderToWavFile(synthesizer,
                               sequence, "out.wav", tail)
  Play one live            player.Load(rate => new ModestSynthesizer(patch,
                               settingsAt(rate)), sequence)

  Waveform names           sine, saw, square, triangle, noise / white_noise,
                           pluck1, wavetable, harmonic, fm6op, formant

  Synthesizer settings     SampleRate (44100), BlockSize (64),
                           MaximumPolyphony (32), Attack (0), Decay (0),
                           Sustain (1), Release (0.1), GlideSeconds (0),
                           PitchBendSemitones (2), VelocityTracking (1),
                           MasterVolume (0.5), RandomSeed (12345)

  pluck1                   Damping 0..1 = short .. long, pitch-dependent
                           (default 0.5; read DecayTimeSeconds for the number)
                           PluckType 0..1 = triangle .. noise (default 0.5)
                           pitch 8 Hz .. sampleRate/3
  fm6op                    Algorithm 1..32 (default 1); per operator Ratio
                           (defaults to the operator's OWN NUMBER, measured),
                           Detune -7..7 (0), Mode ratio/fixed, FixedFrequency
                           (440), Level 0..1 (1.0 on op 1, 0.0 on the rest),
                           VelocitySensitivity 0..7 (0), Feedback 0..1 (0),
                           Attack/Decay/Sustain/Release seconds and amplitude
                           (0, 0, 1.0, -1 = the group's release), EgType
                           adsr/dx7, EgRate1..4 (99, 99, 0, 99) and EgLevel1..4
                           (99, 99, 99, 0). Play it: Reset, NoteOn(velocity),
                           Render..., NoteOff, Render until IsFinished.
  square                   PulseWidth 0.01..0.99 (default 0.5, standalone only)
  noise                    Seed; Amplitude (default 0.8996, the measured level);
                           Reset restarts the sequence, ignores the phase
  wavetable                Table (null = a sine); Position 0..1 = first frame ..
                           last, ramped over a block; FrameInterpolation
                           true = crossfade, false = snap to the nearest frame
                           (a half-way value rounds up); TryLoad(path, frameSize)
                           through the shared cache; Problem says why not.
                           A "clm " chunk's frame size beats the one you ask for
  harmonic                 NumPartials 1..64 (default 8, a ceiling);
                           Tilt -1..1 = brighter .. darker, gain = k^(-2*tilt);
                           OddEvenBalance 0..1, 0.5 = both families at full;
                           Normalization 0..1, the measured sum divide
                           (1 - n) + n / sum(gains), blended linearly in GAIN;
                           SetPartialLevel(1..64, 0..1);
                           no level set anywhere = a plain full-level sine

  Patch defaults           Sine, Damping 0.5, PluckType 0.5, RandomPhase false,
                           Seed 0, WavetableFrameSize 2048, WavetablePosition 0,
                           WavetableFrameInterpolation true, NumPartials 8,
                           HarmonicTilt 0, HarmonicOddEvenBalance 0.5,
                           HarmonicNormalization 0, every partial level 0,
                           FmAlgorithm 1, operator 1 Level 1.0 and operators
                           2..6 Level 0.0

  Make an effect           new GateEffect()
                           ModestEffectFactory.Create("bit_crusher")
  Run one                  Prepare(rate) -> Process(left, right, frames)

  Effect types             phaser, pitch_shift, wave_folder, wave_shaper,
                           stereo_simulator, bit_crusher, gate
                           (the rest of the format's effects are in CodeBrix.Audio)

  phaser                   Mix (0.5), ModDepth (0.2), ModRate Hz (0.2),
                           CenterFrequency Hz (400), Feedback -1..1 (0.7);
                           six all-pass sections, three notches
  pitch_shift              PitchShift semitones -24..24 (0), Mix (0.5);
                           50 ms grains, so the wet path is 50 ms late
  wave_folder              Drive 1..100 (1), Threshold 0..10 (0.25), Mix (1.0);
                           a bit-identical pass-through below the fold point at
                           1/k, k = drive / (2*sqrt(2)*threshold), and NOT
                           clamped above it
  wave_shaper              Drive 1..1000 (1), DriveBoost 0..1 (1),
                           OutputLevel 0..8 (0.1 - a 20 dB cut),
                           HighQuality (false), Mix (1.0)
  stereo_simulator         Algorithm Lauridsen/Schroeder/Adt (Adt),
                           Width 0..1 (0.5, and 0.5 halves the middle),
                           DelayTime s (0.005), ModRate Hz (0.5),
                           ModDepth (0.3); no Mix - Width is the mix
  bit_crusher              BitDepth 1..24 (24), SampleRateReduction 1..32 (1),
                           Mix (1.0); a mid-tread step of
                           2*sqrt(2)/2^(BitDepth-1), so a peak under half a step
                           becomes digital silence
  gate                     Amount 0..1 (0.5), Mix (1.0), Seed, WindowSeconds
                           (0.05); seeded, so the dropouts repeat

  Output                   an oscillator is mono float, nominally -1..1;
                           an effect is stereo and works in place. Render and
                           Process both allocate nothing
