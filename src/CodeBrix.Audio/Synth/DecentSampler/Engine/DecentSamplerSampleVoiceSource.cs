using System;
using CodeBrix.Audio.Synth.Sfz;

namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

// The voice source behind a <sample> zone: the SFZ engine's own oscillator, unchanged apart from the
// loop crossfade it gained for this format. One instance belongs to one voice and is reused note after
// note, so nothing allocates once the synthesizer exists.
//
// Playback is resampled to the synthesis rate the way the SFZ engine does it, which is what lets a
// 96 kHz library play at any device rate: the ratio is (pitch / rootPitch) * (fileRate / outputRate).
internal sealed class DecentSamplerSampleVoiceSource : IVoiceSource
{
    private readonly SfzOscillator _oscillator = new SfzOscillator();
    private readonly int _outputSampleRate;

    private SfzSampleData _data;
    private long _startFrame;
    private long _endFrameInclusive;
    private long _loopStartFrame;
    private long _loopEndFrameInclusive;
    private bool _loopEnabled;
    private long _loopCrossfadeFrames;
    private bool _loopCrossfadeEqualPower;
    private double _rootHz;
    private double _rateRatio;

    private double _ratio;
    private bool _finished;

    public DecentSamplerSampleVoiceSource(int outputSampleRate)
    {
        _outputSampleRate = outputSampleRate;
    }

    public bool IsStereo => _data != null && _data.ChannelCount > 1;

    public bool IsFinished => _finished;

    // Sets up the zone's playback bounds. Called before Start, once per note, from the note-on path.
    public void Prepare(
        SfzSampleData data,
        long startFrame,
        long endFrameInclusive,
        bool loopEnabled,
        long loopStartFrame,
        long loopEndFrameInclusive,
        long loopCrossfadeFrames,
        bool loopCrossfadeEqualPower,
        int rootNote)
    {
        _data = data;
        _startFrame = startFrame;
        _endFrameInclusive = endFrameInclusive;
        _loopEnabled = loopEnabled;
        _loopStartFrame = loopStartFrame;
        _loopEndFrameInclusive = loopEndFrameInclusive;
        _loopCrossfadeFrames = loopCrossfadeFrames;
        _loopCrossfadeEqualPower = loopCrossfadeEqualPower;
        _rootHz = NoteToHertz(rootNote);
        _rateRatio = data == null || _outputSampleRate <= 0
            ? 1.0
            : (double)data.SampleRate / _outputSampleRate;
    }

    // Moves the end bound while the voice sounds, for a SAMPLE_END binding. MEASURED (round 4, item
    // 54): the reference applies SAMPLE_END to a voice that is already sounding, and one whose read
    // position is past the new end falls silent immediately.
    public void SetEndFrame(long endFrameInclusive)
    {
        _endFrameInclusive = endFrameInclusive;
        _oscillator.SetEnd(endFrameInclusive);
    }

    public void Start(int note, int velocity, double pitchHz)
    {
        _finished = _data == null;

        if (_data == null)
        {
            return;
        }

        _oscillator.Start(
            _data,
            _loopEnabled ? SfzLoopMode.Continuous : SfzLoopMode.NoLoop,
            _startFrame,
            _endFrameInclusive,
            _loopStartFrame,
            _loopEndFrameInclusive,
            _loopCrossfadeFrames,
            _loopCrossfadeEqualPower);

        SetPitch(pitchHz);
    }

    // A sample zone has no note-off behaviour of its own: the amplitude envelope owns the release, and
    // a Decent Sampler loop is never a sustain loop that unwinds on release.
    public void NoteOff()
    {
    }

    public void SetPitch(double pitchHz)
    {
        if (_rootHz <= 0.0 || pitchHz <= 0.0)
        {
            _ratio = _rateRatio;
            return;
        }

        _ratio = pitchHz / _rootHz * _rateRatio;
    }

    public bool Render(float[] left, float[] right, int frames)
    {
        if (_finished || _data == null)
        {
            return false;
        }

        // `frames` matters: a voice that starts part-way through a block asks for the remainder of
        // it and places the audio itself, and the sample must advance by exactly what it produced.
        if (!_oscillator.Process(left, right, _ratio, frames))
        {
            _finished = true;
            return false;
        }

        return true;
    }

    // A sample source has no OSCILLATOR_* parameters of its own.
    public bool TrySetParameter(string name, double value) => false;

    public bool TryGetParameter(string name, out double value)
    {
        value = 0.0;
        return false;
    }

    // Concert A is MIDI 69 at 440 Hz - the MIDI tuning standard, independent of how the format spells
    // note NAMES (which is the Yamaha C3 = 60 convention; see DecentSamplerValues).
    public static double NoteToHertz(double note) => 440.0 * Math.Pow(2.0, (note - 69.0) / 12.0);
}
