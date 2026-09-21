using System;
using System.Linq;
using CodeBrix.Audio.Instruments;
using SilverAssertions;
using SilverAssertions.Collections;
using SilverAssertions.Numeric;
using SilverAssertions.Primitives;
using SilverAssertions.Specialized;
using Xunit;

namespace CodeBrix.Audio.Tests.Instruments;

/// <summary>
/// Covers <see cref="InstrumentLibraryRegistry"/> - the rules that hold whatever else is
/// registered.
/// </summary>
/// <remarks>
/// <para>
/// The registry is PROCESS-WIDE and has no undo, so every test here gives its library a name
/// nobody else uses, asks for it BY NAME, and never reads or asserts on the default - which
/// belongs to whichever test registered first and is therefore not a test's to claim. The rules
/// that need an empty registry, and the default itself, live in
/// <see cref="InstrumentLibraryRegistryDefaultTests"/> behind an environment variable.
/// </para>
/// </remarks>
public class InstrumentLibraryRegistryTests
{
    private static string UniqueName(string hint) => $"Test-{hint}-{Guid.NewGuid():N}";

    [Fact]
    public void Register_makes_a_library_resolvable_by_its_own_name()
    {
        //Arrange
        var library = new FakeInstrumentLibrary(UniqueName("resolvable"));

        //Act
        InstrumentLibraryRegistry.Register(library);

        //Assert
        InstrumentLibraryRegistry.Resolve(library.Name).Should().BeSameAs(library);
    }

    [Fact]
    public void Resolve_matches_a_name_without_regard_to_case()
    {
        //Arrange
        var library = new FakeInstrumentLibrary(UniqueName("CaseFolded"));
        InstrumentLibraryRegistry.Register(library);

        //Act
        var found = InstrumentLibraryRegistry.Resolve(library.Name.ToUpperInvariant());

        //Assert
        found.Should().BeSameAs(library);
    }

    [Fact]
    public void Register_is_a_no_op_for_the_same_library_a_second_time()
    {
        //Arrange
        var library = new FakeInstrumentLibrary(UniqueName("idempotent"));

        //Act
        InstrumentLibraryRegistry.Register(library);
        InstrumentLibraryRegistry.Register(library);
        InstrumentLibraryRegistry.Register(library);

        //Assert
        InstrumentLibraryRegistry.Registered
            .Count(entry => ReferenceEquals(entry, library)).Should().Be(1);
    }

    [Fact]
    public void Register_rejects_a_different_library_under_a_taken_name()
    {
        //Arrange
        var name = UniqueName("taken");
        InstrumentLibraryRegistry.Register(new FakeInstrumentLibrary(name));
        var impostor = new FakeInstrumentLibrary(name);

        //Act
        var act = () => InstrumentLibraryRegistry.Register(impostor);

        //Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*already registered under the name '{name}'*");
    }

    [Fact]
    public void Register_rejects_a_taken_name_that_differs_only_in_case()
    {
        //Arrange
        var name = UniqueName("CaseTaken");
        InstrumentLibraryRegistry.Register(new FakeInstrumentLibrary(name));
        var impostor = new FakeInstrumentLibrary(name.ToLowerInvariant());

        //Act
        var act = () => InstrumentLibraryRegistry.Register(impostor);

        //Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void Register_rejects_a_null_library()
    {
        //Act
        var act = () => InstrumentLibraryRegistry.Register(null);

        //Assert
        act.Should().Throw<ArgumentNullException>();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Register_rejects_a_library_with_no_name(string name)
    {
        //Arrange
        var library = new FakeInstrumentLibrary(name);

        //Act
        var act = () => InstrumentLibraryRegistry.Register(library);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void Resolve_rejects_an_unknown_name_and_lists_what_is_registered()
    {
        //Arrange
        var library = new FakeInstrumentLibrary(UniqueName("listed"));
        InstrumentLibraryRegistry.Register(library);
        var missing = UniqueName("absent");

        //Act
        var act = () => InstrumentLibraryRegistry.Resolve(missing);

        //Assert
        // The message has to carry both halves: the name that failed, and the names that would
        // have worked - a developer who mistypes one learns the right spelling from the error.
        var thrown = act.Should().Throw<InvalidOperationException>().Which;
        thrown.Message.Should().Contain($"No instrument library named '{missing}' is registered.");
        thrown.Message.Should().Contain("Registered libraries:");
        thrown.Message.Should().Contain(library.Name);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Resolve_rejects_a_blank_name(string name)
    {
        //Act
        var act = () => InstrumentLibraryRegistry.Resolve(name);

        //Assert
        act.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void IsRegistered_answers_without_throwing()
    {
        //Arrange
        var library = new FakeInstrumentLibrary(UniqueName("probe"));
        InstrumentLibraryRegistry.Register(library);

        //Act & Assert
        InstrumentLibraryRegistry.IsRegistered(library.Name).Should().BeTrue();
        InstrumentLibraryRegistry.IsRegistered(library.Name.ToUpperInvariant()).Should().BeTrue();
        InstrumentLibraryRegistry.IsRegistered(UniqueName("never")).Should().BeFalse();
        InstrumentLibraryRegistry.IsRegistered(null).Should().BeFalse();
    }

    [Fact]
    public void RegisteredNames_names_every_registered_library()
    {
        //Arrange
        var library = new FakeInstrumentLibrary(UniqueName("named"));
        InstrumentLibraryRegistry.Register(library);

        //Act
        var names = InstrumentLibraryRegistry.RegisteredNames;

        //Assert
        names.Should().Contain(library.Name);
    }

    [Fact]
    public void SetDefault_rejects_an_unknown_name_and_lists_what_is_registered()
    {
        //Arrange
        // Registering first guarantees the registry is not empty, so this is the unknown-name
        // error rather than the nothing-registered one.
        InstrumentLibraryRegistry.Register(new FakeInstrumentLibrary(UniqueName("present")));
        var missing = UniqueName("absent");

        //Act
        var act = () => InstrumentLibraryRegistry.SetDefault(missing);

        //Assert
        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*No instrument library named '{missing}' is registered.*");
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void SetDefault_rejects_a_blank_name(string name)
    {
        //Act
        var act = () => InstrumentLibraryRegistry.SetDefault(name);

        //Assert
        act.Should().Throw<ArgumentException>();
    }
}
