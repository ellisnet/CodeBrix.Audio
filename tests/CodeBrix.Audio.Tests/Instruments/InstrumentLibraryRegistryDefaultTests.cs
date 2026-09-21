using System;
using CodeBrix.Audio.Instruments;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests.Instruments;

/// <summary>
/// The <see cref="InstrumentLibraryRegistry"/> rules that need the registry to themselves: which
/// library is the DEFAULT, and what happens when NOTHING is registered.
/// </summary>
/// <remarks>
/// <para>
/// RUN THESE BY THEMSELVES:
/// </para>
/// <code>
/// CODEBRIX_AUDIO_RUN_DEFAULT_INSTRUMENT_TESTS=1 dotnet test CodeBrix.Audio.slnx
/// </code>
/// <para>
/// They EMPTY the process-wide registry and change its default, which no other test can survive
/// running beside. That is why they are gated rather than merely careful: the default belongs to
/// whichever library registered first, so a test that asserts on it is asserting on the order the
/// rest of the suite happened to run in. Every ungated test resolves by name instead, and this
/// class is the one place that looks at the default at all.
/// </para>
/// </remarks>
public class InstrumentLibraryRegistryDefaultTests
{
    private static readonly bool DefaultTestsEnabled =
        Environment.GetEnvironmentVariable("CODEBRIX_AUDIO_RUN_DEFAULT_INSTRUMENT_TESTS") == "1";

    private const string DefaultTestsSkipReason =
        "Set CODEBRIX_AUDIO_RUN_DEFAULT_INSTRUMENT_TESTS=1 to run the tests that empty the " +
        "process-wide instrument library registry and change its default. Run them by themselves.";

    [Fact]
    public void Default_is_the_first_library_registered()
    {
        //Arrange
        Assert.SkipUnless(DefaultTestsEnabled, DefaultTestsSkipReason);
        InstrumentLibraryRegistry.ResetForTesting();
        var first = new FakeInstrumentLibrary("First");
        var second = new FakeInstrumentLibrary("Second");

        //Act
        InstrumentLibraryRegistry.Register(first);
        InstrumentLibraryRegistry.Register(second);

        //Assert
        InstrumentLibraryRegistry.Default.Should().BeSameAs(first);
        InstrumentLibraryRegistry.DefaultName.Should().Be("First");
    }

    [Fact]
    public void SetDefault_makes_another_registered_library_the_default_by_name()
    {
        //Arrange
        Assert.SkipUnless(DefaultTestsEnabled, DefaultTestsSkipReason);
        InstrumentLibraryRegistry.ResetForTesting();
        var first = new FakeInstrumentLibrary("First");
        var second = new FakeInstrumentLibrary("Second");
        InstrumentLibraryRegistry.Register(first);
        InstrumentLibraryRegistry.Register(second);

        //Act
        InstrumentLibraryRegistry.SetDefault("SECOND");

        //Assert
        InstrumentLibraryRegistry.Default.Should().BeSameAs(second);
        InstrumentLibraryRegistry.DefaultName.Should().Be("Second");
    }

    [Fact]
    public void Registering_the_same_library_again_does_not_move_the_default()
    {
        //Arrange
        Assert.SkipUnless(DefaultTestsEnabled, DefaultTestsSkipReason);
        InstrumentLibraryRegistry.ResetForTesting();
        var first = new FakeInstrumentLibrary("First");
        var second = new FakeInstrumentLibrary("Second");
        InstrumentLibraryRegistry.Register(first);
        InstrumentLibraryRegistry.Register(second);
        InstrumentLibraryRegistry.SetDefault("Second");

        //Act
        InstrumentLibraryRegistry.Register(first);

        //Assert
        InstrumentLibraryRegistry.Default.Should().BeSameAs(second);
    }

    [Fact]
    public void Default_with_nothing_registered_says_what_to_register()
    {
        //Arrange
        Assert.SkipUnless(DefaultTestsEnabled, DefaultTestsSkipReason);
        InstrumentLibraryRegistry.ResetForTesting();

        //Act
        var act = () => { _ = InstrumentLibraryRegistry.Default; };

        //Assert
        // This message is the whole of a developer's guidance when nothing plays, so it is pinned
        // word for word rather than by a fragment.
        act.Should().Throw<InvalidOperationException>().Which.Message.Should().Be(
            "No instrument library is registered, so no music can be played or rendered. Register " +
            "the General MIDI library provided by CodeBrix.Audio.ModestSynth - call " +
            "GeneralMidiInstrumentLibrary.Register() - or register another instrument library with " +
            "InstrumentLibraryRegistry.Register.");
    }

    [Fact]
    public void Resolve_with_nothing_registered_says_what_to_register()
    {
        //Arrange
        Assert.SkipUnless(DefaultTestsEnabled, DefaultTestsSkipReason);
        InstrumentLibraryRegistry.ResetForTesting();

        //Act
        var act = () => InstrumentLibraryRegistry.Resolve("AnythingAtAll");

        //Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No instrument library is registered*")
            .WithMessage("*GeneralMidiInstrumentLibrary.Register()*");
    }

    [Fact]
    public void SetDefault_with_nothing_registered_says_what_to_register()
    {
        //Arrange
        Assert.SkipUnless(DefaultTestsEnabled, DefaultTestsSkipReason);
        InstrumentLibraryRegistry.ResetForTesting();

        //Act
        var act = () => InstrumentLibraryRegistry.SetDefault("AnythingAtAll");

        //Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage("*No instrument library is registered*");
    }

    [Fact]
    public void An_empty_registry_reports_itself_without_throwing()
    {
        //Arrange
        Assert.SkipUnless(DefaultTestsEnabled, DefaultTestsSkipReason);
        InstrumentLibraryRegistry.ResetForTesting();

        //Act & Assert
        InstrumentLibraryRegistry.Registered.Should().BeEmpty();
        InstrumentLibraryRegistry.RegisteredNames.Should().BeEmpty();
        InstrumentLibraryRegistry.DefaultName.Should().BeNull();
        InstrumentLibraryRegistry.IsRegistered("Anything").Should().BeFalse();
    }

    [Fact]
    public void Registered_lists_libraries_in_the_order_they_were_registered()
    {
        //Arrange
        Assert.SkipUnless(DefaultTestsEnabled, DefaultTestsSkipReason);
        InstrumentLibraryRegistry.ResetForTesting();
        var first = new FakeInstrumentLibrary("First");
        var second = new FakeInstrumentLibrary("Second");
        var third = new FakeInstrumentLibrary("Third");

        //Act
        InstrumentLibraryRegistry.Register(first);
        InstrumentLibraryRegistry.Register(second);
        InstrumentLibraryRegistry.Register(third);

        //Assert
        InstrumentLibraryRegistry.RegisteredNames.Should().Equal("First", "Second", "Third");
    }
}
