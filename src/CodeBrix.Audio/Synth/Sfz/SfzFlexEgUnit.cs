using System;
using System.Collections.Generic;

namespace CodeBrix.Audio.Synth.Sfz;

// The runtime of one flexible envelope (egN): piecewise-linear movement through the region's points,
// starting from level 0, holding at the sustain point while the note is held, and running the
// remaining points after release. Output is the bipolar point level, -1..1, before depth scaling.
internal sealed class SfzFlexEgUnit
{
    private IReadOnlyList<float> times;
    private IReadOnlyList<float> levels;
    private int sustainPoint;

    private int targetPoint;
    private float segmentElapsed;
    private float segmentStartLevel;
    private bool released;
    private bool holding;
    private float value;

    public void Start(SfzFlexEg envelope)
    {
        times = envelope.Times;
        levels = envelope.Levels;
        sustainPoint = envelope.SustainPoint ?? -1;

        targetPoint = 0;
        segmentElapsed = 0f;
        segmentStartLevel = 0f;
        released = false;
        holding = false;
        value = 0f;
    }

    public void Release()
    {
        released = true;
        if (holding)
        {
            // Leave the sustain hold and head for the next point.
            holding = false;
            targetPoint = Math.Min(targetPoint + 1, levels.Count);
            segmentElapsed = 0f;
            segmentStartLevel = value;
        }
    }

    // Advances by one block and returns the envelope level, -1 to 1.
    public float Advance(float blockSeconds)
    {
        if (holding || targetPoint >= levels.Count)
        {
            return value;
        }

        segmentElapsed += blockSeconds;

        while (targetPoint < levels.Count)
        {
            var duration = times[targetPoint];
            if (segmentElapsed < duration)
            {
                var progress = duration <= 0f ? 1f : segmentElapsed / duration;
                value = segmentStartLevel + progress * (levels[targetPoint] - segmentStartLevel);
                return value;
            }

            // The point is reached exactly; move on, or hold here when it is the sustain point.
            segmentElapsed -= duration;
            value = levels[targetPoint];
            segmentStartLevel = value;

            if (targetPoint == sustainPoint && !released)
            {
                holding = true;
                return value;
            }

            targetPoint++;
        }

        return value;
    }

    public float Value => value;
}
