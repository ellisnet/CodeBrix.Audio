using System;
using System.Collections.Generic;
using System.Linq;

namespace CodeBrix.Audio.Instruments;

/// <summary>
/// The process-wide map from name to <see cref="IInstrumentLibrary"/>, and the place a consumer
/// says which instruments an application plays with.
/// </summary>
/// <remarks>
/// <para>
/// CodeBrix.Audio SHIPS NO INSTRUMENTS and registers nothing, so this registry starts EMPTY and
/// nothing can make a sound until a consumer fills it. Registration is always the consumer's job,
/// through the static <c>Register()</c> entry point an instrument package provides - never a
/// module initializer, and never a library registering itself behind the consumer's back:
/// </para>
/// <code>
/// GeneralMidiInstrumentLibrary.Register();                 // CodeBrix.Audio.ModestSynth
/// new SoundFontInstrumentLibrary("MyPiano", "A grand piano", "piano.sf2").Register();
/// </code>
/// <para>
/// THE RULES. Names are unique and matched case-insensitively. Registering the SAME library
/// instance again is a no-op, so a package's <c>Register()</c> stays idempotent; a DIFFERENT
/// library under a taken name is an error. There is no priority and no ordering beyond one thing:
/// THE FIRST LIBRARY REGISTERED IS THE DEFAULT. A consumer with two registered may make either one
/// the default with <see cref="SetDefault"/>, and a consumer who needs certainty asks for a library
/// BY NAME with <see cref="Resolve"/> rather than relying on <see cref="Default"/>, because which
/// library is the default depends on which registration ran first.
/// </para>
/// <para>
/// Every member is safe to call from several threads at once.
/// </para>
/// </remarks>
public static class InstrumentLibraryRegistry
{
    private static readonly object Gate = new object();

    private static readonly Dictionary<string, IInstrumentLibrary> Libraries =
        new Dictionary<string, IInstrumentLibrary>(StringComparer.OrdinalIgnoreCase);

    private static readonly List<IInstrumentLibrary> RegistrationOrder = new List<IInstrumentLibrary>();

    private static string defaultName;

    /// <summary>
    /// Registers an instrument library under its own <see cref="IInstrumentLibrary.Name"/>, and
    /// makes it the default when it is the first one registered.
    /// </summary>
    /// <param name="library">The library to register.</param>
    /// <exception cref="ArgumentNullException"><paramref name="library"/> is null.</exception>
    /// <exception cref="ArgumentException">The library's name is null or blank.</exception>
    /// <exception cref="InvalidOperationException">
    /// A DIFFERENT library is already registered under that name. Registering the same instance
    /// again is a no-op and does not throw.
    /// </exception>
    public static void Register(IInstrumentLibrary library)
    {
        if (library == null)
        {
            throw new ArgumentNullException(nameof(library));
        }

        var name = library.Name;

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException(
                "An instrument library must have a name: it is how a consumer asks for it.",
                nameof(library));
        }

        lock (Gate)
        {
            if (Libraries.TryGetValue(name, out var existing))
            {
                if (ReferenceEquals(existing, library))
                {
                    // Idempotent by design: a package's Register() may be called on every start-up
                    // path without the consumer having to track whether it ran already.
                    return;
                }

                throw new InvalidOperationException(
                    $"A different instrument library is already registered under the name '{name}'. " +
                    "Instrument library names are unique and matched case-insensitively; registering " +
                    "the same library again is a no-op, but two different libraries cannot share a name.");
            }

            Libraries.Add(name, library);
            RegistrationOrder.Add(library);

            if (defaultName == null)
            {
                defaultName = name;
            }
        }
    }

    /// <summary>
    /// Every registered library, in the order they were registered - so the first entry is the
    /// library that became the default.
    /// </summary>
    public static IReadOnlyList<IInstrumentLibrary> Registered
    {
        get { lock (Gate) { return RegistrationOrder.ToArray(); } }
    }

    /// <summary>The name of every registered library, in registration order.</summary>
    public static IReadOnlyList<string> RegisteredNames
    {
        get { lock (Gate) { return RegistrationOrder.Select(library => library.Name).ToArray(); } }
    }

    /// <summary>Whether a library is registered under a name, matched case-insensitively.</summary>
    /// <param name="name">The library name to look for.</param>
    /// <returns>True when that name resolves.</returns>
    public static bool IsRegistered(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return false;

        lock (Gate) { return Libraries.ContainsKey(name); }
    }

    /// <summary>Gets a registered library by name, matched case-insensitively.</summary>
    /// <param name="name">The library name, as its <see cref="IInstrumentLibrary.Name"/> reads.</param>
    /// <returns>The registered library.</returns>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or blank.</exception>
    /// <exception cref="InvalidOperationException">
    /// Nothing is registered at all, or no library is registered under that name. The message lists
    /// what IS registered.
    /// </exception>
    public static IInstrumentLibrary Resolve(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An instrument library name is required.", nameof(name));
        }

        lock (Gate)
        {
            if (Libraries.Count == 0)
            {
                throw new InvalidOperationException(NothingRegisteredMessage);
            }

            if (Libraries.TryGetValue(name, out var library))
            {
                return library;
            }

            throw new InvalidOperationException(UnknownNameMessage(name));
        }
    }

    /// <summary>
    /// The library used when none is named: the FIRST one registered, unless
    /// <see cref="SetDefault"/> changed it.
    /// </summary>
    /// <exception cref="InvalidOperationException">
    /// No instrument library is registered. The message says what to register.
    /// </exception>
    public static IInstrumentLibrary Default
    {
        get
        {
            lock (Gate)
            {
                if (defaultName == null)
                {
                    throw new InvalidOperationException(NothingRegisteredMessage);
                }

                return Libraries[defaultName];
            }
        }
    }

    /// <summary>
    /// The name of the current default library, or null when nothing is registered. Reading this
    /// never throws, so a diagnostic can report the state of an empty registry.
    /// </summary>
    public static string DefaultName
    {
        get { lock (Gate) { return defaultName; } }
    }

    /// <summary>Makes an already-registered library the default, by name.</summary>
    /// <param name="name">The library name, matched case-insensitively.</param>
    /// <exception cref="ArgumentException"><paramref name="name"/> is null or blank.</exception>
    /// <exception cref="InvalidOperationException">
    /// Nothing is registered at all, or no library is registered under that name. The message lists
    /// what IS registered.
    /// </exception>
    public static void SetDefault(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("An instrument library name is required.", nameof(name));
        }

        lock (Gate)
        {
            if (Libraries.Count == 0)
            {
                throw new InvalidOperationException(NothingRegisteredMessage);
            }

            if (!Libraries.TryGetValue(name, out var library))
            {
                throw new InvalidOperationException(UnknownNameMessage(name));
            }

            // Store the library's OWN spelling of its name, not the caller's, so DefaultName reads
            // back the way the library writes it.
            defaultName = library.Name;
        }
    }

    /// <summary>
    /// Empties the registry. For tests that have to exercise registration order or the default,
    /// which is process-wide state; nothing in a shipping application should call this.
    /// </summary>
    internal static void ResetForTesting()
    {
        lock (Gate)
        {
            Libraries.Clear();
            RegistrationOrder.Clear();
            defaultName = null;
        }
    }

    // Prose is not a dependency: CodeBrix.Audio cannot reference CodeBrix.Audio.ModestSynth, but it
    // can still tell a developer which package holds the instruments they almost certainly want.
    private const string NothingRegisteredMessage =
        "No instrument library is registered, so no music can be played or rendered. Register the " +
        "General MIDI library provided by CodeBrix.Audio.ModestSynth - call " +
        "GeneralMidiInstrumentLibrary.Register() - or register another instrument library with " +
        "InstrumentLibraryRegistry.Register.";

    private static string UnknownNameMessage(string name) =>
        $"No instrument library named '{name}' is registered. Registered libraries: " +
        $"{string.Join(", ", RegistrationOrder.Select(library => library.Name))}. Register it " +
        "before asking for it by name.";
}
