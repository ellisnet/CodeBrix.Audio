using System.Collections.Generic;
using CodeBrix.Audio.Synth.Sfz;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.Tests.Synth.Sfz;

/// <summary>
/// Covers the modulation curves: the seven built-ins and vertex-defined <c>&lt;curve&gt;</c> sections
/// with linear interpolation between defined points.
/// </summary>
public class SfzCurveTests
{
    [Fact]
    public void the_built_in_curves_hit_their_documented_endpoints()
    {
        //Arrange + Act + Assert
        SfzCurve.Linear.Evaluate(0f).Should().Be(0f);
        SfzCurve.Linear.Evaluate(1f).Should().Be(1f);

        SfzCurve.Bipolar.Evaluate(0f).Should().Be(-1f);
        SfzCurve.Bipolar.Evaluate(1f).Should().Be(1f);

        SfzCurve.Inverted.Evaluate(0f).Should().Be(1f);
        SfzCurve.Inverted.Evaluate(1f).Should().Be(0f);

        SfzCurve.BipolarInverted.Evaluate(0f).Should().Be(1f);
        SfzCurve.BipolarInverted.Evaluate(1f).Should().Be(-1f);

        SfzCurve.Concave.Evaluate(0f).Should().Be(0f);
        SfzCurve.Concave.Evaluate(1f).Should().Be(1f);

        SfzCurve.PowerIn.Evaluate(0f).Should().Be(0f);
        SfzCurve.PowerIn.Evaluate(1f).Should().Be(1f);

        SfzCurve.PowerOut.Evaluate(0f).Should().Be(1f);
        SfzCurve.PowerOut.Evaluate(1f).Should().Be(0f);
    }

    [Fact]
    public void the_concave_curve_sits_below_linear_in_the_middle()
    {
        //Arrange + Act + Assert
        SfzCurve.Concave.Evaluate(0.5f).Should().BeLessThan(0.5f);
        SfzCurve.PowerIn.Evaluate(0.5f).Should().BeGreaterThan(0.5f);
    }

    [Fact]
    public void vertex_curves_interpolate_linearly_between_defined_points()
    {
        //Arrange
        var curve = SfzCurve.FromVertices(new Dictionary<int, float>
        {
            [0] = 0f,
            [127] = 1f,
        });

        //Act + Assert
        curve.Evaluate(0.5f).Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void values_before_the_first_and_after_the_last_vertex_hold_steady()
    {
        //Arrange
        var curve = SfzCurve.FromVertices(new Dictionary<int, float>
        {
            [32] = 0.25f,
            [96] = 0.75f,
        });

        //Act + Assert
        curve.Evaluate(0f).Should().Be(0.25f);
        curve.Evaluate(1f).Should().Be(0.75f);
        curve.Evaluate(0.5f).Should().BeApproximately(0.5f, 0.01f);
    }

    [Fact]
    public void a_curve_with_no_vertices_is_linear()
    {
        //Arrange
        var curve = SfzCurve.FromVertices(new Dictionary<int, float>());

        //Act + Assert
        curve.Evaluate(0.25f).Should().BeApproximately(0.25f, 0.001f);
    }

    [Fact]
    public void evaluation_clamps_out_of_range_input()
    {
        //Arrange + Act + Assert
        SfzCurve.Linear.Evaluate(-0.5f).Should().Be(0f);
        SfzCurve.Linear.Evaluate(1.5f).Should().Be(1f);
    }
}
