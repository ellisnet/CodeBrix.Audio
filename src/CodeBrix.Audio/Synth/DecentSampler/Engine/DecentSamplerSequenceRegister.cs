using System;
using CodeBrix.Audio.Synth.DecentSampler.Model;

namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

// One round-robin position register. Measured against the reference player (round 1, item 9):
//
//  - seqLength auto-detection is INSTRUMENT-WIDE: with no seqLength the queue is as long as the highest
//    seqPosition declared ANYWHERE in the preset, so a preset mixing round-robin sets of different
//    lengths produces silent notes. That is the reference behaviour and is mirrored here, with one line
//    in Problems so the library author can see it.
//  - The register starts at 1 and is ADVANCED BEFORE each selection, so the first note of a session
//    plays position 2.
//  - A position with no zone plays nothing; the queue is never compacted.
//  - "random" draws over the whole length and never repeats a position twice in a row; "true_random"
//    repeats freely; "always" turns round robins off entirely.
internal sealed class DecentSamplerSequenceRegister
{
    private readonly int _length;

    private int _position;
    private int _lastDraw;

    public DecentSamplerSequenceRegister(int length)
    {
        _length = Math.Max(1, length);
        Reset();
    }

    public int Length => _length;

    // The position selected for the note being handled. Valid only after Advance.
    public int Position => _position;

    public void Reset()
    {
        _position = 1;
        _lastDraw = -1;
    }

    // Chooses the position for one note-on. Called at most once per note-on per register.
    public int Advance(DecentSamplerSeqMode mode, Random random)
    {
        switch (mode)
        {
            case DecentSamplerSeqMode.RoundRobin:
                _position = _position >= _length ? 1 : _position + 1;
                break;

            case DecentSamplerSeqMode.Random:
                _position = DrawWithoutImmediateRepeat(random);
                break;

            case DecentSamplerSeqMode.TrueRandom:
                _position = _length == 1 ? 1 : random.Next(_length) + 1;
                break;

            default:
                // "always" never consults the register.
                break;
        }

        _lastDraw = _position;
        return _position;
    }

    private int DrawWithoutImmediateRepeat(Random random)
    {
        if (_length <= 1)
        {
            return 1;
        }

        // Drawing from the length minus one and stepping past the last value gives a uniform draw over
        // the other positions in one call, so the number of random draws per note stays fixed and the
        // seeded stream stays reproducible.
        if (_lastDraw < 1 || _lastDraw > _length)
        {
            return random.Next(_length) + 1;
        }

        var draw = random.Next(_length - 1) + 1;
        return draw >= _lastDraw ? draw + 1 : draw;
    }
}
