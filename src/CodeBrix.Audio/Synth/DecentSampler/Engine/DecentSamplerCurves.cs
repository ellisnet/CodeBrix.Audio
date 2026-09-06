using System;

namespace CodeBrix.Audio.Synth.DecentSampler.Engine;

// The Decent Sampler envelope curve law, measured against the reference player (round 1, item 4; the
// measurement documents are named in MAINTAINER-README.txt, PROVENANCE AND VENDORED SOURCES).
//
// One shape function serves all three segments. With u the fraction of the segment elapsed:
//
//     shape(k, u) = (1 - exp(-k*u)) / (1 - exp(-k))          k = 4.1
//
// A POSITIVE argument gives the concave "fast first, slow later" curve, a negative one its mirror.
// The three segments feed it a different sign of their own curve attribute, because a rising segment
// and a falling one are described from opposite ends:
//
//     attack  : S = Shape(-attackCurve, u)          level    = S
//     decay   : S = Shape(+decayCurve, u)           fallen   = S  (1 down to sustain)
//     release : S = Shape(+releaseCurve, u)         fallen   = S  (level down to 0)
//
// Values between the three named points (-100 logarithmic, 0 linear, 100 exponential) are a LINEAR
// BLEND of the straight line against the extreme shape by |curve|/100 - measured to fit four times
// better than blending the exponent.
internal static class DecentSamplerCurves
{
    // The exponent of the extreme shapes. The measurement fitted 4.01 to the concave trace and 4.29 to
    // the convex one; 4.1 is the best single number for both, and reproduces every measured trace to
    // within 0.015 in amplitude.
    public const double ShapeConstant = 4.1;

    // The exponent of an <envelope> MODULATOR's stages, which is NOT the amplitude envelope's.
    // MEASURED (round 3, item 45): a two-second attack, sampled at twelve points and normalised, is
    // gain = (1 - exp(-4x)) / (1 - exp(-4)) to within 0.05 over the whole stage, and the decay and the
    // release are the mirror of the same curve toward the sustain and toward zero. The measurement's
    // own confidence on the exact constant is MEDIUM (it was fitted to one stage), which is why it is
    // a separate number from ShapeConstant rather than a correction to it.
    public const double ModulatorShapeConstant = 4.0;

    private static readonly double ConcaveScale = 1.0 / (1.0 - Math.Exp(-ShapeConstant));
    private static readonly double ConvexScale = 1.0 / (1.0 - Math.Exp(ShapeConstant));

    private static readonly double ModulatorConcaveScale =
        1.0 / (1.0 - Math.Exp(-ModulatorShapeConstant));

    private static readonly double ModulatorConvexScale =
        1.0 / (1.0 - Math.Exp(ModulatorShapeConstant));

    // The curve attribute range the format documents. Anything outside is clamped.
    public const double MinimumCurve = -100.0;

    public const double MaximumCurve = 100.0;

    // The normalized progress of a segment whose curve attribute has already been given the sign the
    // segment needs. u is clamped to 0..1.
    public static double Shape(double curve, double u) =>
        Shape(curve, u, ShapeConstant, ConcaveScale, ConvexScale);

    // The same law for an <envelope> MODULATOR's stages, which run at ModulatorShapeConstant. With no
    // curve attributes written the defaults (attack -100, decay and release +100) put this at the full
    // concave extreme, which is exactly the measured (1 - exp(-4x)) / (1 - exp(-4)).
    //
    // A DELIBERATE DIVERGENCE: the reference ACCEPTS AND IGNORES attackCurve, decayCurve and
    // releaseCurve on an <envelope> modulator - a two-second attack at curves -100 and +100 agreed to
    // 0.1 dB at thirteen points - while they work here. The group AMPLITUDE envelope's curves do work
    // in the reference (round 1 item 4), so a preset that writes them expects them to.
    public static double ModulatorShape(double curve, double u) =>
        Shape(curve, u, ModulatorShapeConstant, ModulatorConcaveScale, ModulatorConvexScale);

    private static double Shape(
        double curve, double u, double constant, double concaveScale, double convexScale)
    {
        if (u <= 0.0)
        {
            return 0.0;
        }

        if (u >= 1.0)
        {
            return 1.0;
        }

        var clamped = Math.Clamp(curve, MinimumCurve, MaximumCurve);
        var blend = Math.Abs(clamped) * 0.01;

        if (blend <= 0.0)
        {
            return u;
        }

        var extreme = clamped > 0.0
            ? (1.0 - Math.Exp(-constant * u)) * concaveScale
            : (1.0 - Math.Exp(constant * u)) * convexScale;

        return (1.0 - blend) * u + blend * extreme;
    }
}
