using CodeBrix.Audio.Synth.DecentSampler.Engine;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.DecentSampler.Engine;

/// <summary>
/// The envelope curve law, checked against the 21-point shape table measured from the reference player
/// (plan section 7 item 4).
/// </summary>
/// <remarks>
/// The measurement's own analysis window smears a shape by about 0.015 in amplitude, and the convex
/// extreme fitted its analytic form with an rms error of 0.020, so the tolerances below are the
/// measurement's noise floor rather than a slack target.
/// </remarks>
public class DecentSamplerCurvesTests
{
    // The measured concave shape, u from 0.00 to 1.00 in steps of 0.05: the mean of the
    // attackCurve = -100 and releaseCurve = +100 traces.
    private static readonly double[] MeasuredConcave =
    [
        0.000, 0.186, 0.343, 0.470, 0.572, 0.654, 0.720, 0.772, 0.813, 0.845, 0.871,
        0.893, 0.912, 0.928, 0.943, 0.955, 0.966, 0.975, 0.983, 0.989, 1.000,
    ];

    // The measured convex shape: the mean of the attackCurve = +100 and releaseCurve = -100 traces.
    private static readonly double[] MeasuredConvex =
    [
        0.000, 0.008, 0.015, 0.023, 0.032, 0.043, 0.055, 0.068, 0.083, 0.101, 0.122,
        0.147, 0.178, 0.217, 0.266, 0.328, 0.405, 0.501, 0.622, 0.771, 1.000,
    ];

    [Fact]
    public void curve_zero_is_a_straight_line()
    {
        for (var step = 0; step <= 20; step++)
        {
            var u = step * 0.05;
            DecentSamplerCurves.Shape(0.0, u).Should().BeApproximately(u, 1e-12);
        }
    }

    [Fact]
    public void the_positive_extreme_matches_the_measured_concave_shape()
    {
        for (var step = 0; step <= 20; step++)
        {
            DecentSamplerCurves.Shape(100.0, step * 0.05)
                .Should().BeApproximately(MeasuredConcave[step], 0.02);
        }
    }

    [Fact]
    public void the_negative_extreme_matches_the_measured_convex_shape()
    {
        for (var step = 0; step <= 20; step++)
        {
            DecentSamplerCurves.Shape(-100.0, step * 0.05)
                .Should().BeApproximately(MeasuredConvex[step], 0.045);
        }
    }

    [Fact]
    public void intermediate_curves_are_a_linear_blend_of_the_straight_line_and_the_extreme()
    {
        for (var step = 0; step <= 20; step++)
        {
            var u = step * 0.05;

            DecentSamplerCurves.Shape(50.0, u)
                .Should().BeApproximately(0.5 * u + 0.5 * DecentSamplerCurves.Shape(100.0, u), 1e-12);

            DecentSamplerCurves.Shape(-50.0, u)
                .Should().BeApproximately(0.5 * u + 0.5 * DecentSamplerCurves.Shape(-100.0, u), 1e-12);
        }
    }

    [Fact]
    public void the_shape_runs_from_zero_to_one_and_clamps_outside()
    {
        DecentSamplerCurves.Shape(100.0, 0.0).Should().Be(0.0);
        DecentSamplerCurves.Shape(100.0, 1.0).Should().Be(1.0);
        DecentSamplerCurves.Shape(100.0, -1.0).Should().Be(0.0);
        DecentSamplerCurves.Shape(100.0, 5.0).Should().Be(1.0);
    }

    [Fact]
    public void a_curve_beyond_the_documented_range_clamps_to_the_extreme()
    {
        DecentSamplerCurves.Shape(500.0, 0.25)
            .Should().Be(DecentSamplerCurves.Shape(100.0, 0.25));

        DecentSamplerCurves.Shape(-500.0, 0.25)
            .Should().Be(DecentSamplerCurves.Shape(-100.0, 0.25));
    }

    [Fact]
    public void the_two_extremes_are_mirror_images()
    {
        for (var step = 0; step <= 20; step++)
        {
            var u = step * 0.05;

            DecentSamplerCurves.Shape(100.0, u)
                .Should().BeApproximately(1.0 - DecentSamplerCurves.Shape(-100.0, 1.0 - u), 1e-12);
        }
    }
}
