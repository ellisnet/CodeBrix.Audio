using System;
using CodeBrix.Audio.Synth.DecentSampler.Streaming;

namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

// The voice source behind a STREAMED <sample> zone. The sibling of DecentSamplerSampleVoiceSource, with
// the same pitch, loop and crossfade behaviour and the same resampling to the synthesis rate; the only
// difference is where the frames come from.
//
// One instance belongs to one voice and is reused note after note. Its per-note state - the ring buffer
// the reader fills - is RENTED at note-on and returned when the voice ends, so a hundred idle voices
// cost a hundred small objects rather than a hundred ring buffers.
//
// A note that cannot get a buffer, because every one is out, plays silence and says so. That is the
// same failure mode as running out of voices, and for the same reason: an audio thread may not wait.
internal sealed class DecentSamplerStreamingVoiceSource : IVoiceSource
{
    private readonly DecentSamplerStreamingOscillator _oscillator = new DecentSamplerStreamingOscillator();
    private readonly int _outputSampleRate;

    private StreamingSampleSource _source;
    private StreamingVoicePool _pool;
    private StreamingVoiceBuffer _buffer;
    private DecentSamplerStreamingMode _mode;

    private long _startFrame;
    private long _endFrameExclusive;
    private long _loopStartFrame;
    private long _loopEndFrameExclusive;
    private bool _loopEnabled;
    private long _loopCrossfadeFrames;
    private bool _loopCrossfadeEqualPower;
    private double _rootHz;
    private double _rateRatio;

    private double _ratio;
    private bool _finished;

    public DecentSamplerStreamingVoiceSource(int outputSampleRate)
    {
        _outputSampleRate = outputSampleRate;
    }

    public bool IsStereo => _source != null && _source.ChannelCount > 1;

    public bool IsFinished => _finished;

    // Sets up the zone's playback bounds, mirroring SfzOscillator.Start's own clamping exactly so that
    // a streamed zone and a decoded one agree about where the audio starts, ends and loops.
    public void Prepare(
        StreamingSampleSource source,
        StreamingVoicePool pool,
        DecentSamplerStreamingMode mode,
        long startFrame,
        long endFrameInclusive,
        bool loopEnabled,
        long loopStartFrame,
        long loopEndFrameInclusive,
        long loopCrossfadeFrames,
        bool loopCrossfadeEqualPower,
        int rootNote)
    {
        _source = source;
        _pool = pool;
        _mode = mode;

        if (source == null)
        {
            return;
        }

        var frames = source.Frames;

        _startFrame = Math.Clamp(startFrame, 0, frames);
        _endFrameExclusive = endFrameInclusive < 0 ? frames : Math.Min(endFrameInclusive + 1, frames);

        _loopStartFrame = Math.Clamp(loopStartFrame, 0, frames);
        _loopEndFrameExclusive = Math.Clamp(loopEndFrameInclusive + 1, _loopStartFrame + 1, frames);

        _loopEnabled = loopEnabled;

        // A start past the loop end has no streamed meaning: the reader would have to wrap before it
        // had produced anything. The in-memory path drifts back into the loop one frame per sample
        // instead; this is the one place the two can differ, and only for a preset that asks a zone to
        // start after its own loop ends.
        if (_loopEnabled && _startFrame >= _loopEndFrameExclusive)
        {
            _startFrame = Math.Max(0, _loopEndFrameExclusive - 1);
        }

        // The faded-in audio comes from before loopStart, so the region can never be longer than the
        // audio in front of the loop, nor than the loop itself. The same clamp SfzOscillator applies.
        _loopCrossfadeFrames = Math.Max(
            0,
            Math.Min(
                loopCrossfadeFrames,
                Math.Min(_loopStartFrame, _loopEndFrameExclusive - _loopStartFrame - 1)));

        _loopCrossfadeEqualPower = loopCrossfadeEqualPower;

        _rootHz = DecentSamplerSampleVoiceSource.NoteToHertz(rootNote);
        _rateRatio = _outputSampleRate <= 0 ? 1.0 : (double)source.SampleRate / _outputSampleRate;
    }

    public void Start(int note, int velocity, double pitchHz)
    {
        // A voice that was stolen rather than allowed to finish may still be holding a buffer.
        Recycle();

        _finished = _source == null;

        if (_source == null)
        {
            return;
        }

        _buffer = _pool?.Rent();

        if (_buffer == null)
        {
            // Every buffer is busy. The pool counts it and the streaming context turns the count into
            // one problem line; the note is silent, exactly as a stolen voice would be.
            _finished = true;
            return;
        }

        _buffer.Configure(
            _source,
            _startFrame,
            _endFrameExclusive,
            _loopEnabled,
            _loopStartFrame,
            _loopEndFrameExclusive,
            _loopCrossfadeFrames);

        // RAM only: the head of the file is already decoded, so the first block has audio without the
        // reader having run at all. Everything past the head arrives from the reader.
        _buffer.PrimeFromHead();
        _buffer.Start();

        _oscillator.Start(_buffer, _source.ChannelCount, _loopCrossfadeFrames, _loopCrossfadeEqualPower);

        if (_mode == DecentSamplerStreamingMode.RealTime)
        {
            DecentSamplerStreamingReader.Shared.Wake();
        }

        SetPitch(pitchHz);
    }

    // A sample zone has no note-off behaviour of its own: the amplitude envelope owns the release.
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
        if (_finished || _buffer == null)
        {
            return false;
        }

        if (_mode == DecentSamplerStreamingMode.Offline)
        {
            // Offline: this thread is a worker, not an audio callback, so it fetches its own frames and
            // a streamed render can never fall behind however fast it runs.
            _buffer.Service();
        }

        // `frames` can be fewer than a whole block on a voice's first sounding block - a note delay,
        // or a sequenced or arpeggiated note starting part-way through the block. The oscillator must
        // fill exactly that many and advance by exactly that many, or the note jumps just after its
        // onset.
        if (!_oscillator.Process(left, right, frames, _ratio))
        {
            _finished = true;
            return false;
        }

        return true;
    }

    // Hands the ring buffer back. Safe to call more than once.
    public void Recycle()
    {
        if (_buffer != null)
        {
            _buffer.Release(immediate: _mode == DecentSamplerStreamingMode.Offline);
            _buffer = null;
        }
    }

    // A sample source has no OSCILLATOR_* parameters of its own.
    public bool TrySetParameter(string name, double value) => false;

    public bool TryGetParameter(string name, out double value)
    {
        value = 0.0;
        return false;
    }
}
