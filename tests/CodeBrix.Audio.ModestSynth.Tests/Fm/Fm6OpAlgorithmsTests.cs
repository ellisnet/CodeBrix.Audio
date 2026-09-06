using System;
using System.Collections.Generic;
using CodeBrix.Audio.ModestSynth.Fm;
using SilverAssertions;
using Xunit;

namespace CodeBrix.Audio.ModestSynth.Tests.Fm;

/// <summary>
/// The 32 routings, checked against a hand-written copy of the published algorithm chart.
/// </summary>
/// <remarks>
/// The library derives the carriers and the evaluation order from the routing it is given;
/// <see cref="Dx7AlgorithmChart" /> states them outright. Checking one against the other catches
/// both a mistyped routing and a broken derivation.
/// </remarks>
public class Fm6OpAlgorithmsTests
{
    [Fact]
    public void Algorithms_has_all_thirty_two_in_order()
    {
        //Assert
        Fm6OpAlgorithms.Algorithms.Should().HaveCount(32);
        for (int number = 1; number <= 32; number++)
        {
            Fm6OpAlgorithms.Algorithms[number - 1].Number.Should().Be(number);
            Fm6OpAlgorithms.Get(number).Should().BeSameAs(Fm6OpAlgorithms.Algorithms[number - 1]);
        }
    }

    [Theory]
    [MemberData(nameof(Dx7AlgorithmChart.AllAlgorithms), MemberType = typeof(Dx7AlgorithmChart))]
    public void Carriers_match_the_published_chart(int number)
    {
        //Act
        Fm6OpAlgorithm algorithm = Fm6OpAlgorithms.Get(number);

        //Assert
        algorithm.Carriers.Should().Equal(Dx7AlgorithmChart.Carriers(number));
    }

    [Theory]
    [MemberData(nameof(Dx7AlgorithmChart.AllAlgorithms), MemberType = typeof(Dx7AlgorithmChart))]
    public void Modulation_paths_match_the_published_chart(int number)
    {
        //Arrange
        Fm6OpAlgorithm algorithm = Fm6OpAlgorithms.Get(number);
        IReadOnlyList<int> routing = Dx7AlgorithmChart.Routing(number);

        //Act
        int paths = 0;
        for (int modulator = 1; modulator <= 6; modulator++)
        {
            for (int target = 1; target <= 6; target++)
            {
                if (algorithm.Modulates(modulator, target)) { paths++; }
            }
        }

        //Assert
        paths.Should().Be(routing.Count / 2);
        for (int i = 0; i < routing.Count; i += 2)
        {
            algorithm.Modulates(routing[i], routing[i + 1]).Should().BeTrue();
        }
    }

    [Theory]
    [MemberData(nameof(Dx7AlgorithmChart.AllAlgorithms), MemberType = typeof(Dx7AlgorithmChart))]
    public void The_feedback_loop_matches_the_published_chart(int number)
    {
        //Act
        Fm6OpAlgorithm algorithm = Fm6OpAlgorithms.Get(number);

        //Assert
        algorithm.FeedbackSource.Should().Be(Dx7AlgorithmChart.FeedbackSource(number));
        algorithm.FeedbackDestination.Should().Be(Dx7AlgorithmChart.FeedbackDestination(number));
    }

    [Theory]
    [MemberData(nameof(Dx7AlgorithmChart.AllAlgorithms), MemberType = typeof(Dx7AlgorithmChart))]
    public void Every_operator_is_either_a_carrier_or_a_modulator(int number)
    {
        //Arrange
        Fm6OpAlgorithm algorithm = Fm6OpAlgorithms.Get(number);

        //Act
        int total = algorithm.Carriers.Count + algorithm.Modulators.Count;

        //Assert
        total.Should().Be(6);
        for (int operatorNumber = 1; operatorNumber <= 6; operatorNumber++)
        {
            bool carrier = algorithm.IsCarrier(operatorNumber);
            bool modulator = Includes(algorithm.Modulators, operatorNumber);
            (carrier ^ modulator).Should().BeTrue();
        }
    }

    [Theory]
    [MemberData(nameof(Dx7AlgorithmChart.AllAlgorithms), MemberType = typeof(Dx7AlgorithmChart))]
    public void The_render_order_puts_every_modulator_before_what_it_feeds(int number)
    {
        //Arrange
        Fm6OpAlgorithm algorithm = Fm6OpAlgorithms.Get(number);
        IReadOnlyList<int> order = algorithm.RenderOrder;

        //Assert
        order.Should().HaveCount(6);
        for (int operatorNumber = 1; operatorNumber <= 6; operatorNumber++)
        {
            order.Should().Contain(operatorNumber);
        }

        for (int modulator = 1; modulator <= 6; modulator++)
        {
            for (int target = 1; target <= 6; target++)
            {
                if (!algorithm.Modulates(modulator, target)) { continue; }

                IndexOf(order, modulator).Should().BeLessThan(IndexOf(order, target));
            }
        }
    }

    [Fact]
    public void Algorithm_thirty_two_is_six_carriers_and_nothing_else()
    {
        //Act
        Fm6OpAlgorithm algorithm = Fm6OpAlgorithms.Get(32);

        //Assert
        algorithm.Carriers.Should().Equal(1, 2, 3, 4, 5, 6);
        algorithm.Modulators.Should().BeEmpty();
        algorithm.FeedbackDestination.Should().Be(6);
    }

    [Fact]
    public void Algorithm_nineteen_sends_operator_six_into_both_four_and_five()
    {
        //Act
        Fm6OpAlgorithm algorithm = Fm6OpAlgorithms.Get(19);

        //Assert
        algorithm.Modulates(6, 4).Should().BeTrue();
        algorithm.Modulates(6, 5).Should().BeTrue();
        algorithm.GetModulatorsOf(1).Should().Equal(2);
        algorithm.Carriers.Should().Equal(1, 4, 5);
    }

    [Fact]
    public void Algorithm_five_is_three_pairs()
    {
        //Act
        Fm6OpAlgorithm algorithm = Fm6OpAlgorithms.Get(5);

        //Assert
        algorithm.Carriers.Should().Equal(1, 3, 5);
        algorithm.GetModulatorsOf(1).Should().Equal(2);
        algorithm.GetModulatorsOf(3).Should().Equal(4);
        algorithm.GetModulatorsOf(5).Should().Equal(6);
    }

    [Fact]
    public void The_two_algorithms_whose_feedback_loop_spans_operators_say_so()
    {
        //Assert
        Fm6OpAlgorithms.Get(4).FeedbackSource.Should().Be(4);
        Fm6OpAlgorithms.Get(4).FeedbackDestination.Should().Be(6);
        Fm6OpAlgorithms.Get(6).FeedbackSource.Should().Be(5);
        Fm6OpAlgorithms.Get(6).FeedbackDestination.Should().Be(6);
    }

    [Fact]
    public void Nineteen_of_the_thirty_two_put_the_feedback_on_operator_six()
    {
        //Act
        int onOperatorSix = 0;
        for (int number = 1; number <= 32; number++)
        {
            if (Fm6OpAlgorithms.Get(number).FeedbackDestination == 6) { onOperatorSix++; }
        }

        //Assert
        onOperatorSix.Should().Be(19);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(33)]
    public void Get_rejects_an_algorithm_that_does_not_exist(int number)
    {
        //Act
        Action act = () => Fm6OpAlgorithms.Get(number);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Theory]
    [InlineData(-5, 1)]
    [InlineData(0, 1)]
    [InlineData(7, 7)]
    [InlineData(32, 32)]
    [InlineData(99, 32)]
    public void Clamp_brings_an_algorithm_number_into_range(int given, int expected) =>
        Fm6OpAlgorithms.Clamp(given).Should().Be(expected);

    [Theory]
    [InlineData(0)]
    [InlineData(7)]
    public void IsCarrier_rejects_an_operator_that_does_not_exist(int operatorNumber)
    {
        //Act
        Action act = () => Fm6OpAlgorithms.Get(1).IsCarrier(operatorNumber);

        //Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void ToString_names_the_algorithm_its_carriers_and_its_feedback()
    {
        //Assert
        Fm6OpAlgorithms.Get(5).ToString().Should().Be("algorithm 5: carriers 1, 3, 5; feedback 6");
        Fm6OpAlgorithms.Get(4).ToString().Should().Be("algorithm 4: carriers 1, 4; feedback 4->6");
    }

    private static bool Includes(IReadOnlyList<int> values, int value) => IndexOf(values, value) >= 0;

    private static int IndexOf(IReadOnlyList<int> order, int operatorNumber)
    {
        for (int i = 0; i < order.Count; i++)
        {
            if (order[i] == operatorNumber) { return i; }
        }

        return -1;
    }
}
