using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using CodeBrix.Audio.Synth.DecentSampler.Bindings;
using CodeBrix.Audio.Synth.DecentSampler.Containers;
using CodeBrix.Audio.Synth.DecentSampler.Model;
using CodeBrix.Audio.Synth.DecentSampler.Samples;
using CodeBrix.Audio.Synth.DecentSampler.Streaming;

namespace CodeBrix.Audio.Synth.DecentSampler;

/// <summary>
/// A loaded Decent Sampler instrument: the parsed preset, its resolved groups and zones, the decoded
/// samples, and everything that could not be honoured.
/// </summary>
/// <remarks>
/// <para>
/// This is the Decent Sampler counterpart of <see cref="CodeBrix.Audio.Synth.Sfz.SfzInstrument"/>: load
/// it once - ideally through a <see cref="DecentSamplerInstrumentCache"/> - and share the instance.
/// Loading is tolerant in the same way: a missing sample, an unreadable value or an unknown attribute
/// is recorded in <see cref="Problems"/> and the instrument still loads with whatever did.
/// </para>
/// <para>
/// The instrument owns its decoded samples, so it is disposable. Disposing it releases the audio and,
/// for an archive, closes the container; anything still playing from it stops making sense, so dispose
/// only when nothing is using it.
/// </para>
/// <para>
/// Loading runs the parameter and binding engine: <see cref="Controls"/> is the live control surface,
/// every binding is resolved to what it changes, and the controls' initial values are applied, so the
/// groups, zones, effects and tags already carry the balance the preset was written with.
/// <see cref="Ui"/> stays the parsed interface - the values there are the ones written in the file -
/// and <see cref="UnsupportedFeatures"/> names what the loaded preset asked for that the engine cannot
/// honour.
/// </para>
/// </remarks>
public sealed class DecentSamplerInstrument : IDisposable
{
    private readonly List<string> _problems = [];
    private readonly SortedSet<string> _unsupported = new(StringComparer.Ordinal);
    private readonly Dictionary<DecentSamplerZone, ISampleSource> _sourcesByZone = [];
    private readonly Dictionary<DecentSamplerZone, string> _resolvedPathsByZone = [];
    private readonly Dictionary<DecentSamplerZone, DeferredSample> _deferredByZone = [];
    private readonly List<DeferredSample> _deferred = [];
    private readonly DecentSamplerSampleCache _sampleCache;
    private readonly bool _ownsSampleCache;
    private readonly DecentSamplerContainer _container;
    private readonly bool _ownsContainer;
    private readonly DecentSamplerBindingEngine _bindings;

    private readonly object _runtimeProblemGate = new object();
    private readonly HashSet<string> _runtimeProblemKeys = new(StringComparer.Ordinal);
    private readonly List<string> _runtimeProblems = [];

    private string[] _problemsSnapshot;
    private int _pendingDecodes;
    private int _streamedSampleCount;
    private int _effectivePreloadFrames = DecentSamplerLoadOptions.DefaultStreamingPreloadFrames;
    private bool _loaded;
    private bool _disposed;

    private DecentSamplerInstrument(
        DecentSamplerPreset preset,
        DecentSamplerContainer container,
        bool ownsContainer,
        DecentSamplerLoadOptions options,
        DecentSamplerSampleCache sharedSampleCache)
    {
        Preset = preset;
        Options = options;
        _container = container;
        _ownsContainer = ownsContainer;
        _sampleCache = sharedSampleCache ?? new DecentSamplerSampleCache();
        _ownsSampleCache = sharedSampleCache == null;

        Path = preset.Path;
        Name = preset.Name;
        Groups = preset.ResolveGroups();
        Zones = Groups.SelectMany(group => group.Zones).ToArray();

        _problems.AddRange(preset.Problems);

        ReportUnsupportedFeatures();
        LoadSamples();

        _bindings = new DecentSamplerBindingEngine(this);
        _bindings.ParameterChanged += (_, arguments) =>
        {
            ReportStreamingOnlyParameter(arguments);
            ParameterChanged?.Invoke(this, arguments);
        };
        _bindings.AllNotesOff += (_, arguments) => AllNotesOffRequested?.Invoke(this, arguments);
        _bindings.Build();
        _bindings.ApplyInitialState();
        _problems.AddRange(_bindings.Problems);

        _loaded = true;
    }

    /// <summary>
    /// Raised when a binding changes an engine parameter: a group's volume, a zone's loop point, an
    /// effect's cut-off. A synthesizer listens here to learn that a value it has cached has moved.
    /// </summary>
    public event EventHandler<DecentSamplerParameterChangedEventArgs> ParameterChanged;

    /// <summary>
    /// Raised when an <c>ALL_NOTES_OFF</c> binding fires. A synthesizer stops every voice.
    /// </summary>
    public event EventHandler AllNotesOffRequested;

    /// <summary>The preset this instrument was built from, with every parsed element.</summary>
    public DecentSamplerPreset Preset { get; }

    /// <summary>The options the instrument was loaded with.</summary>
    public DecentSamplerLoadOptions Options { get; }

    /// <summary>
    /// The path the preset was read from. For an archive this is the archive's path, so it identifies
    /// the file a consumer opened.
    /// </summary>
    public string Path { get; }

    /// <summary>The instrument's display name - the preset file name without its extension.</summary>
    public string Name { get; }

    /// <summary>
    /// The container the preset was read from - the folder it sits in, or the <c>.dslibrary</c> or
    /// <c>.dsbundle</c> archive it came out of - so that a file the preset names beside itself can be
    /// resolved and opened the same way the engine resolves a sample.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Use <see cref="DecentSamplerContainer.TryResolve"/> to turn a preset-relative path into a key
    /// (capitalisation need not match the disk, and both slash directions are accepted), then
    /// <see cref="DecentSamplerContainer.OpenFile"/> to read it. For an archive the relative path is
    /// resolved against the preset's own folder inside the zip, exactly as a sample path is.
    /// </para>
    /// <para>
    /// THE INSTRUMENT OWNS IT. Do not dispose it: it is closed when the instrument is disposed, and
    /// using it afterwards throws. Reading through it is safe from a worker thread - it is read-only
    /// once the instrument has loaded - but it is not an audio-thread operation, because opening a
    /// file allocates and does I/O.
    /// </para>
    /// </remarks>
    public DecentSamplerContainer Container => _container;

    /// <summary>
    /// The library's <c>DSLibraryInfo.xml</c>, when one was found beside or above the preset, otherwise
    /// null.
    /// </summary>
    public DecentSamplerLibraryInfo LibraryInfo { get; private set; }

    /// <summary>The groups with their inheritance resolved, in document order.</summary>
    public IReadOnlyList<DecentSamplerGroup> Groups { get; }

    /// <summary>Every zone of every group, in document order.</summary>
    public IReadOnlyList<DecentSamplerZone> Zones { get; }

    /// <summary>The instrument-level effect chain, or null when the preset declares none.</summary>
    public DecentSamplerEffectsElement Effects => Preset.Effects;

    /// <summary>The buses, in document order. Empty when the preset declares none.</summary>
    public IReadOnlyList<DecentSamplerBus> Buses => Preset.Buses?.Buses ?? [];

    /// <summary>The MIDI handlers, in document order. Empty when the preset declares none.</summary>
    public IReadOnlyList<DecentSamplerMidiHandler> MidiHandlers => Preset.Midi?.Handlers ?? [];

    /// <summary>The modulators, in document order. Empty when the preset declares none.</summary>
    public IReadOnlyList<DecentSamplerModulator> Modulators => Preset.Modulators?.Modulators ?? [];

    /// <summary>The note sequences, in document order. Empty when the preset declares none.</summary>
    public IReadOnlyList<DecentSamplerNoteSequence> Sequences => Preset.NoteSequences?.Sequences ?? [];

    /// <summary>The arpeggiator, or null when the preset declares none.</summary>
    public DecentSamplerArpeggiator Arpeggiator => Preset.Arpeggiator;

    /// <summary>The declared tags, in document order. Empty when the preset declares none.</summary>
    public IReadOnlyList<DecentSamplerTag> Tags => Preset.Tags?.Tags ?? [];

    /// <summary>
    /// The parsed user interface, or null when the preset declares none. This is data, not a live
    /// parameter model: the control values here are the ones the preset was written with. Use
    /// <see cref="Controls"/> for the live ones.
    /// </summary>
    public DecentSamplerUi Ui => Preset.Ui;

    /// <summary>
    /// The live control surface: one entry for every element under every tab, in the order a binding's
    /// <c>controlIndex</c> or <c>position</c> counts them, labels and images included.
    /// </summary>
    /// <remarks>
    /// Setting a control's value fires its bindings, exactly as turning the knob in a player would.
    /// This is the seam a host exposes as automatable parameters and a future panel renders.
    /// </remarks>
    public IReadOnlyList<DecentSamplerControl> Controls =>
        _bindings?.Controls ?? (IReadOnlyList<DecentSamplerControl>)[];

    /// <summary>
    /// The live state of every tag named anywhere in the preset - on a <c>&lt;tag&gt;</c> element, on a
    /// group, on a sample, or in a binding's <c>identifier</c>.
    /// </summary>
    public IReadOnlyList<DecentSamplerTagState> TagStates =>
        _bindings?.TagStates ?? (IReadOnlyList<DecentSamplerTagState>)[];

    /// <summary>
    /// The interface background image, relative to the preset. A <c>BG_IMAGE</c> binding changes it,
    /// which is why it is here rather than only on <see cref="Ui"/>.
    /// </summary>
    public string BackgroundImage => _bindings?.BackgroundImage;

    /// <summary>
    /// Counts every parameter change a binding has made. A render loop can compare it with the value it
    /// last saw to know whether anything moved, without subscribing to
    /// <see cref="ParameterChanged"/>.
    /// </summary>
    public int ParameterVersion => _bindings?.Version ?? 0;

    /// <summary>
    /// Everything that went wrong while loading, none of it fatal: parse problems, missing sample
    /// files, samples that failed to decode, a store-tied library. An instrument with problems still
    /// plays what loaded.
    /// </summary>
    /// <remarks>
    /// Streaming adds a second source of entries, after loading: a starved streamed voice, a sample
    /// that was never decoded when the first note wanted it, and a binding that moved a sample or loop
    /// point that only in-memory playback honours. Each is added once, however many times it happens,
    /// and the list is replaced rather than mutated so that reading it while a voice sounds is safe.
    /// </remarks>
    public IReadOnlyList<string> Problems =>
        Volatile.Read(ref _problemsSnapshot) ?? (IReadOnlyList<string>)_problems;

    /// <summary>
    /// The names of features the loaded preset uses that this engine cannot honour, sorted. The same
    /// list is written to the Debug listener at load, once per name.
    /// </summary>
    public IReadOnlyCollection<string> UnsupportedFeatures => _unsupported;

    /// <summary>
    /// How many distinct sample files this instrument's cache holds. When the instrument came from a
    /// <see cref="DecentSamplerInstrumentCache"/> that shares sample data, this counts every file that
    /// cache holds, not only the ones this preset uses.
    /// </summary>
    public int DecodedSampleCount => _sampleCache.Count;

    /// <summary>
    /// How much audio the instrument holds in memory, in bytes. A streamed sample counts only its
    /// preload head here, which is the point of streaming it.
    /// </summary>
    public long DecodedByteCount => _sampleCache.DecodedByteCount;

    /// <summary>How many distinct sample files are streamed from disk rather than held in memory.</summary>
    public int StreamedSampleCount => _streamedSampleCount;

    /// <summary>
    /// The preload head each streamed sample keeps in memory, in frames. This is
    /// <see cref="DecentSamplerLoadOptions.StreamingPreloadFrames"/> unless the instrument memory budget
    /// could not be met with heads that long, in which case the policy shortened it and said so in
    /// <see cref="MemoryPolicySummary"/>.
    /// </summary>
    public int EffectiveStreamingPreloadFrames => _effectivePreloadFrames;

    /// <summary>
    /// One line saying what the memory policy decided: how many samples are in memory, how many stream,
    /// what each costs, and the threshold and budget that decided it. Empty when the instrument has no
    /// samples.
    /// </summary>
    public string MemoryPolicySummary { get; private set; } = string.Empty;

    /// <summary>
    /// How many distinct sample files are waiting to be decoded because the first note that wanted them
    /// has not arrived yet. Always zero unless the instrument was loaded with
    /// <see cref="DecentSamplerLoadOptions.DecodeSamples"/> off.
    /// </summary>
    public int PendingDecodeCount
    {
        get
        {
            var pending = 0;
            foreach (var deferred in _deferred)
            {
                if (deferred.Source == null && !deferred.Failed)
                {
                    pending++;
                }
            }

            return pending;
        }
    }

    /// <summary>
    /// Loads an instrument from a <c>.dspreset</c>, a <c>.dslibrary</c>, a <c>.dsbundle</c> or a folder
    /// holding one of those.
    /// </summary>
    /// <param name="path">The preset, archive or folder.</param>
    /// <returns>The loaded instrument.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="FileNotFoundException">Nothing exists at that path, or it holds no preset.</exception>
    /// <exception cref="DecentSamplerParseException">The preset is not well-formed XML.</exception>
    public static DecentSamplerInstrument Load(string path) => Load(path, null);

    /// <summary>
    /// Loads an instrument from a <c>.dspreset</c>, a <c>.dslibrary</c>, a <c>.dsbundle</c> or a folder
    /// holding one of those.
    /// </summary>
    /// <param name="path">The preset, archive or folder.</param>
    /// <param name="options">How to load it, or null for the defaults.</param>
    /// <returns>The loaded instrument.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="path"/> is null.</exception>
    /// <exception cref="FileNotFoundException">Nothing exists at that path, or it holds no preset.</exception>
    /// <exception cref="DecentSamplerParseException">The preset is not well-formed XML.</exception>
    public static DecentSamplerInstrument Load(string path, DecentSamplerLoadOptions options) =>
        Load(path, options, null);

    // The overload the instrument cache uses: several instruments over one library share one decode,
    // so two presets that reference the same recording pay for it once. The cache owns the shared
    // sample cache and disposes it; the instruments built over it do not.
    internal static DecentSamplerInstrument Load(
        string path, DecentSamplerLoadOptions options, DecentSamplerSampleCache sharedSampleCache)
    {
        if (path == null)
        {
            throw new ArgumentNullException(nameof(path));
        }

        var settings = options?.Clone() ?? new DecentSamplerLoadOptions();
        var extension = System.IO.Path.GetExtension(path);

        var isArchive =
            string.Equals(extension, ".dslibrary", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(extension, ".dsbundle", StringComparison.OrdinalIgnoreCase);

        if (!isArchive && !Directory.Exists(path) && !File.Exists(path))
        {
            throw new FileNotFoundException("Nothing exists at that path.", path);
        }

        return isArchive || Directory.Exists(path)
            ? LoadFromContainer(path, settings, sharedSampleCache)
            : LoadFromPresetFile(path, settings, sharedSampleCache);
    }

    /// <summary>
    /// Builds an instrument from an already-parsed preset and an already-open container.
    /// </summary>
    /// <param name="preset">The parsed preset.</param>
    /// <param name="container">Where the preset's files come from. The caller keeps ownership.</param>
    /// <param name="options">How to load it, or null for the defaults.</param>
    /// <returns>The loaded instrument.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="preset"/> or <paramref name="container"/> is null.</exception>
    public static DecentSamplerInstrument Create(
        DecentSamplerPreset preset, DecentSamplerContainer container, DecentSamplerLoadOptions options = null)
    {
        if (preset == null)
        {
            throw new ArgumentNullException(nameof(preset));
        }

        if (container == null)
        {
            throw new ArgumentNullException(nameof(container));
        }

        return new DecentSamplerInstrument(
            preset, container, false, options?.Clone() ?? new DecentSamplerLoadOptions(), null);
    }

    /// <summary>The parameter and binding engine, which the synthesizer drives MIDI through.</summary>
    internal DecentSamplerBindingEngine BindingEngine => _bindings;

    /// <summary>Finds a control by its name, case-insensitively.</summary>
    /// <param name="name">The control's <c>parameterName</c> or label.</param>
    /// <returns>The first control of that name, or null.</returns>
    public DecentSamplerControl GetControl(string name)
    {
        if (name == null)
        {
            return null;
        }

        foreach (var control in Controls)
        {
            if (string.Equals(control.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                return control;
            }
        }

        return null;
    }

    /// <summary>
    /// Feeds a continuous-controller message to the preset's <c>&lt;midi&gt;&lt;cc&gt;</c> handlers.
    /// </summary>
    /// <param name="channel">The MIDI channel, 0 to 15.</param>
    /// <param name="controller">The controller number, 0 to 127.</param>
    /// <param name="value">The controller value, 0 to 127.</param>
    /// <returns><see langword="true"/> when a handler fired.</returns>
    internal bool ProcessControlChange(int channel, int controller, int value) =>
        _bindings.ProcessControlChange(channel, controller, value);

    /// <summary>
    /// Feeds a note-on to the preset's <c>&lt;midi&gt;</c> handlers before the sampler sees it.
    /// </summary>
    /// <param name="channel">The MIDI channel, 0 to 15.</param>
    /// <param name="note">The note number, 0 to 127.</param>
    /// <param name="velocity">The velocity, 1 to 127.</param>
    /// <returns><see langword="true"/> when the note was swallowed and must not be played.</returns>
    internal bool ProcessNoteOn(int channel, int note, int velocity) =>
        _bindings.ProcessNoteOn(channel, note, velocity);

    /// <summary>Feeds a note-off to the preset's <c>&lt;midi&gt;</c> handlers.</summary>
    /// <param name="channel">The MIDI channel, 0 to 15.</param>
    /// <param name="note">The note number, 0 to 127.</param>
    /// <param name="velocity">The release velocity, 0 to 127.</param>
    /// <returns><see langword="true"/> when the note was swallowed.</returns>
    internal bool ProcessNoteOff(int channel, int note, int velocity) =>
        _bindings.ProcessNoteOff(channel, note, velocity);

    /// <summary>The decoded audio behind a zone, or null when it has none.</summary>
    /// <param name="zone">A zone of this instrument.</param>
    /// <returns>The source, or null.</returns>
    internal ISampleSource GetSampleSource(DecentSamplerZone zone) =>
        zone != null && _sourcesByZone.TryGetValue(zone, out var source) ? source : null;

    // The not-yet-decoded file behind a zone, or null when the zone has none waiting. The synthesizer
    // hands this to the zone runtime at build time so that the audio thread never looks anything up.
    internal DeferredSample GetDeferredSample(DecentSamplerZone zone) =>
        zone != null && _deferredByZone.TryGetValue(zone, out var deferred) ? deferred : null;

    // Raises the "please decode this" flag. Callable from the audio thread: one interlocked write.
    internal void RequestDecode(DeferredSample deferred)
    {
        if (deferred == null || !deferred.Request())
        {
            return;
        }

        Volatile.Write(ref _pendingDecodes, 1);
        DecentSamplerStreamingReader.Shared.Wake();
    }

    // Decodes whatever the audio thread has asked for. Called by the streaming reader thread, and
    // directly by an offline render, which may read files on its own thread. Never on the audio thread.
    internal void ServicePendingDecodes()
    {
        if (Volatile.Read(ref _pendingDecodes) == 0 || _disposed)
        {
            return;
        }

        Volatile.Write(ref _pendingDecodes, 0);

        foreach (var deferred in _deferred)
        {
            if (!deferred.IsPending)
            {
                continue;
            }

            try
            {
                deferred.Complete(_sampleCache.GetOrAdd(deferred.CacheKey, () => deferred.Stream
                    ? StreamingSampleSource.Open(
                        _container.OpenStreamingFile(deferred.ContainerKey),
                        deferred.ContainerKey,
                        deferred.PreloadFrames)
                    : InMemorySampleSource.Decode(
                        _container.OpenFile(deferred.ContainerKey), deferred.ContainerKey)));

                AddRuntimeProblem(
                    $"{Name}: sample {(deferred.Stream ? "opened" : "decoded")} on first use, so the " +
                    $"note that asked for it was silent: {deferred.Path}");
            }
            catch (Exception exception)
            {
                deferred.Fail();
                AddRuntimeProblem(
                    $"{Name}: sample failed to load on first use: {deferred.Path} ({exception.Message})");
            }
        }
    }

    // Adds a problem discovered while playing rather than while loading: an underrun, a note that
    // arrived before its sample was decoded, a binding that only in-memory playback honours. Each text
    // is added at most once, and the exposed list is replaced rather than mutated, so a consumer
    // enumerating Problems on another thread never sees it change under them.
    internal void AddRuntimeProblem(string text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return;
        }

        lock (_runtimeProblemGate)
        {
            if (!_runtimeProblemKeys.Add(text))
            {
                return;
            }

            _runtimeProblems.Add(text);

            var combined = new string[_problems.Count + _runtimeProblems.Count];
            _problems.CopyTo(combined, 0);
            _runtimeProblems.CopyTo(combined, _problems.Count);

            Volatile.Write(ref _problemsSnapshot, combined);
        }
    }

    // Decodes a file that is not a zone's sample - an effect's impulse response, for instance -
    // through the same container and the same shared cache the samples use, so the instrument still
    // owns and disposes every decoded byte. Paths are relative to the preset exactly as a sample path
    // is. Returns null and a one-line reason when the file cannot be found or decoded.
    //
    // Safe to call from a worker thread: the cache serialises decoding and the container is read-only
    // once the instrument has loaded.
    internal ISampleSource LoadAuxiliaryFile(string relativePath, out string problem)
    {
        problem = null;

        if (string.IsNullOrWhiteSpace(relativePath))
        {
            problem = "no file was named";
            return null;
        }

        if (!_container.TryResolve(relativePath, out var key))
        {
            problem = $"file not found: {relativePath} (in {_container.Description})";
            return null;
        }

        try
        {
            return _sampleCache.GetOrAdd(
                _container.CacheKeyFor(key), () => InMemorySampleSource.Decode(_container.OpenFile(key), key));
        }
        catch (Exception exception)
        {
            problem = $"file failed to decode: {relativePath} ({exception.Message})";
            return null;
        }
    }

    /// <summary>The file a zone's sample resolved to, or null when it did not resolve.</summary>
    /// <param name="zone">A zone of this instrument.</param>
    /// <returns>The resolved container key, or null.</returns>
    public string GetResolvedSamplePath(DecentSamplerZone zone) =>
        zone != null && _resolvedPathsByZone.TryGetValue(zone, out var resolved) ? resolved : null;

    /// <summary>Releases the decoded samples and, for an archive it opened, closes the container.</summary>
    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;

        if (_ownsSampleCache)
        {
            _sampleCache.Dispose();
        }

        if (_ownsContainer)
        {
            _container.Dispose();
        }
    }

    private static DecentSamplerInstrument LoadFromPresetFile(
        string path, DecentSamplerLoadOptions options, DecentSamplerSampleCache sharedSampleCache)
    {
        var preset = DecentSamplerParser.ParseFile(path);
        var directory = System.IO.Path.GetDirectoryName(System.IO.Path.GetFullPath(path)) ?? ".";
        var container = new DecentSamplerFolderContainer(directory);

        var instrument = new DecentSamplerInstrument(
            preset, container, true, options, sharedSampleCache);
        instrument.LoadLibraryInfoFromFolder(container);
        return instrument;
    }

    private static DecentSamplerInstrument LoadFromContainer(
        string path, DecentSamplerLoadOptions options, DecentSamplerSampleCache sharedSampleCache)
    {
        if (Directory.Exists(path))
        {
            var folder = new DecentSamplerFolderContainer(path);
            var presets = folder.FindPresets();
            var chosen = ChoosePreset(presets, options.PresetName, name =>
                System.IO.Path.GetFileNameWithoutExtension(name));

            folder.Dispose();

            if (chosen == null)
            {
                throw new FileNotFoundException("The folder holds no .dspreset file.", path);
            }

            return LoadFromPresetFile(chosen, options, sharedSampleCache);
        }

        var archive = new DecentSamplerArchiveContainer(path);

        try
        {
            var entries = archive.FindPresets();
            var entry = ChoosePreset(entries, options.PresetName, name =>
                System.IO.Path.GetFileNameWithoutExtension(name));

            if (entry == null)
            {
                throw new FileNotFoundException("The container holds no .dspreset entry.", path);
            }

            var libraryInfoEntry = archive.FindLibraryInfo();

            DecentSamplerPreset preset;
            using (var stream = archive.OpenFile(entry))
            {
                preset = DecentSamplerParser.Parse(stream, path);
            }

            preset.Name = System.IO.Path.GetFileNameWithoutExtension(entry);

            var slash = entry.LastIndexOf('/');
            var baseDirectory = slash < 0 ? string.Empty : entry.Substring(0, slash);
            var presetContainer = new DecentSamplerArchiveContainer(path, baseDirectory);

            archive.Dispose();
            archive = null;

            var instrument = new DecentSamplerInstrument(
                preset, presetContainer, true, options, sharedSampleCache);

            instrument.LoadLibraryInfoFromArchive(presetContainer, libraryInfoEntry);
            return instrument;
        }
        catch (Exception)
        {
            archive?.Dispose();
            throw;
        }
    }

    private static string ChoosePreset(
        IReadOnlyList<string> candidates, string wanted, Func<string, string> nameOf)
    {
        if (candidates.Count == 0)
        {
            return null;
        }

        if (wanted == null)
        {
            return candidates[0];
        }

        foreach (var candidate in candidates)
        {
            if (string.Equals(nameOf(candidate), wanted, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return candidates[0];
    }

    private void LoadLibraryInfoFromFolder(DecentSamplerFolderContainer container)
    {
        var path = container.FindLibraryInfo();
        if (path == null)
        {
            return;
        }

        try
        {
            LibraryInfo = DecentSamplerLibraryInfo.Load(path);
            _problems.AddRange(LibraryInfo.Problems.Select(problem => Name + ": " + problem));
        }
        catch (Exception exception)
        {
            _problems.Add($"{Name}: DSLibraryInfo.xml could not be read: {exception.Message}");
        }
    }

    private void LoadLibraryInfoFromArchive(DecentSamplerArchiveContainer container, string entryName)
    {
        if (entryName == null)
        {
            return;
        }

        try
        {
            using (var stream = container.OpenFile(entryName))
            {
                LibraryInfo = DecentSamplerLibraryInfo.Load(stream, container.ArchivePath);
            }

            _problems.AddRange(LibraryInfo.Problems.Select(problem => Name + ": " + problem));
        }
        catch (Exception exception)
        {
            _problems.Add($"{Name}: DSLibraryInfo.xml could not be read: {exception.Message}");
        }
    }

    private void ReportUnsupportedFeatures()
    {
        foreach (var attribute in Preset.AllUnknownAttributes())
        {
            AddUnsupported($"{attribute.ElementName}@{attribute.Name}");
        }

        foreach (var element in Preset.AllUnknownElements())
        {
            AddUnsupported($"<{element.Name}>");
        }

        foreach (var effect in AllEffects())
        {
            if (effect.EffectType == DecentSamplerEffectType.Unknown)
            {
                AddUnsupported($"effect type '{effect.TypeName}'");
            }
        }

        foreach (var zone in Zones)
        {
            if (zone.Kind == DecentSamplerZoneKind.Oscillator && zone.Waveform == DecentSamplerWaveform.Unknown)
            {
                AddUnsupported($"oscillator waveform '{zone.WaveformName}'");
            }
        }
    }

    private IEnumerable<DecentSamplerEffect> AllEffects()
    {
        if (Preset.Effects != null)
        {
            foreach (var effect in Preset.Effects.Effects)
            {
                yield return effect;
            }
        }

        foreach (var group in Groups)
        {
            if (group.Effects == null)
            {
                continue;
            }

            foreach (var effect in group.Effects.Effects)
            {
                yield return effect;
            }
        }

        foreach (var bus in Buses)
        {
            if (bus.Effects == null)
            {
                continue;
            }

            foreach (var effect in bus.Effects.Effects)
            {
                yield return effect;
            }
        }
    }

    private void AddUnsupported(string name)
    {
        if (_unsupported.Add(name))
        {
            Debug.WriteLine($"CodeBrix.Audio Decent Sampler: not implemented: {name} ({Name})");
        }
    }

    private void LoadSamples()
    {
        var missing = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var zonesByCacheKey = new Dictionary<string, List<DecentSamplerZone>>(StringComparer.Ordinal);
        var containerKeys = new Dictionary<string, string>(StringComparer.Ordinal);
        var order = new List<string>();

        foreach (var zone in Zones)
        {
            if (zone.Kind != DecentSamplerZoneKind.Sample)
            {
                continue;
            }

            if (string.IsNullOrWhiteSpace(zone.Path))
            {
                _problems.Add($"{Name}: a <sample> has no path (line " +
                              $"{zone.Source.LineNumber.ToString(System.Globalization.CultureInfo.InvariantCulture)})");
                continue;
            }

            if (!_container.TryResolve(zone.Path, out var key))
            {
                if (missing.Add(zone.Path))
                {
                    _problems.Add($"{Name}: sample not found: {zone.Path} (in {_container.Description})");
                }

                continue;
            }

            _resolvedPathsByZone[zone] = key;

            var cacheKey = _container.CacheKeyFor(key);

            if (!zonesByCacheKey.TryGetValue(cacheKey, out var list))
            {
                list = [];
                zonesByCacheKey[cacheKey] = list;
                containerKeys[cacheKey] = key;
                order.Add(cacheKey);
            }

            list.Add(zone);
        }

        if (order.Count == 0)
        {
            return;
        }

        var entries = BuildPolicyEntries(order, zonesByCacheKey, containerKeys);

        MemoryPolicySummary = DecentSamplerMemoryPolicy.Decide(
            entries, Options.StreamingSampleThresholdBytes, Options.InstrumentMemoryBudgetBytes,
            Options.StreamingPreloadFrames, out var budgetForced, out _effectivePreloadFrames);

        if (budgetForced)
        {
            _problems.Add($"{Name}: {MemoryPolicySummary}");
        }

        foreach (var entry in entries)
        {
            var zones = zonesByCacheKey[entry.CacheKey];
            var path = zones[0].Path;

            if (Options.DecodeSamples && entry.Stream && TryOpenStream(entry, zones, path))
            {
                continue;
            }

            if (!Options.DecodeSamples)
            {
                // Lazy: nothing is opened now, and the first note that wants this file asks for it. The
                // policy's decision travels with the request, so a lazily-loaded library still streams
                // what it would have streamed.
                var deferred = new DeferredSample(
                    entry.CacheKey, entry.ContainerKey, path, entry.Stream, _effectivePreloadFrames);

                _deferred.Add(deferred);

                if (entry.Stream)
                {
                    _streamedSampleCount++;
                }

                foreach (var zone in zones)
                {
                    _deferredByZone[zone] = deferred;
                }

                continue;
            }

            try
            {
                var source = _sampleCache.GetOrAdd(
                    entry.CacheKey,
                    () => InMemorySampleSource.Decode(
                        _container.OpenFile(entry.ContainerKey), entry.ContainerKey));

                foreach (var zone in zones)
                {
                    _sourcesByZone[zone] = source;
                }
            }
            catch (Exception exception)
            {
                if (missing.Add(path))
                {
                    _problems.Add($"{Name}: sample failed to decode: {path} ({exception.Message})");
                }
            }
        }
    }

    // Opens a streamed source, or reports why it could not and falls back to memory.
    private bool TryOpenStream(
        DecentSamplerMemoryPolicy.Entry entry, List<DecentSamplerZone> zones, string path)
    {
        try
        {
            var source = _sampleCache.GetOrAdd(
                entry.CacheKey,
                () => StreamingSampleSource.Open(
                    _container.OpenStreamingFile(entry.ContainerKey),
                    entry.ContainerKey,
                    _effectivePreloadFrames));

            foreach (var zone in zones)
            {
                _sourcesByZone[zone] = source;
            }

            _streamedSampleCount++;
            return true;
        }
        catch (Exception exception)
        {
            entry.Stream = false;
            _problems.Add(
                $"{Name}: sample could not be streamed, so it is held in memory instead: {path} " +
                $"({exception.Message})");
            return false;
        }
    }

    // Works out, for every distinct file, which playback mode its zones agreed on and what it would
    // cost decoded.
    //
    // Two zones over one file can disagree about playbackMode; memory wins, because a zone that says
    // memory may be moving SAMPLE_START or LOOP_START, which streaming cannot honour, and holding the
    // file satisfies both zones while streaming it satisfies only one.
    private List<DecentSamplerMemoryPolicy.Entry> BuildPolicyEntries(
        List<string> order,
        Dictionary<string, List<DecentSamplerZone>> zonesByCacheKey,
        Dictionary<string, string> containerKeys)
    {
        var modes = new Dictionary<string, DecentSamplerPlaybackMode>(StringComparer.Ordinal);
        var rawBytes = new Dictionary<string, long>(StringComparer.Ordinal);
        var upperBoundTotal = 0L;
        var wantsStreaming = false;
        var overThreshold = false;

        foreach (var cacheKey in order)
        {
            var mode = DecentSamplerPlaybackMode.DiskStreaming;

            foreach (var zone in zonesByCacheKey[cacheKey])
            {
                var zoneMode = Options.PlaybackModeOverride ?? zone.PlaybackMode;

                if (zoneMode == DecentSamplerPlaybackMode.Memory)
                {
                    mode = DecentSamplerPlaybackMode.Memory;
                    break;
                }

                if (zoneMode == DecentSamplerPlaybackMode.Auto)
                {
                    mode = DecentSamplerPlaybackMode.Auto;
                }
            }

            modes[cacheKey] = mode;
            wantsStreaming |= mode == DecentSamplerPlaybackMode.DiskStreaming;

            var raw = Math.Max(0, _container.SizeOf(containerKeys[cacheKey]));
            rawBytes[cacheKey] = raw;

            // The loosest possible decode is 8-bit PCM widened to 32-bit float, four times the bytes;
            // FLAC can reach about five. Eight is a ceiling nothing crosses.
            var upperBound = raw * 8;
            upperBoundTotal += upperBound;
            overThreshold |= Options.StreamingSampleThresholdBytes > 0 &&
                             upperBound > Options.StreamingSampleThresholdBytes;
        }

        var budgetInDoubt = Options.InstrumentMemoryBudgetBytes > 0 &&
                            upperBoundTotal > Options.InstrumentMemoryBudgetBytes;

        var entries = new List<DecentSamplerMemoryPolicy.Entry>(order.Count);

        foreach (var cacheKey in order)
        {
            var mode = modes[cacheKey];
            var containerKey = containerKeys[cacheKey];
            var raw = rawBytes[cacheKey];

            // Probing means opening the file and reading its header, which is cheap for a folder and
            // not cheap for an archive entry. It only happens when the answer could change a decision:
            // a library whose every file is far below the threshold and whose total is far below the
            // budget is decided on the file sizes alone, without opening anything.
            var probe = mode == DecentSamplerPlaybackMode.DiskStreaming || overThreshold || budgetInDoubt;

            long decodedBytes;
            long bytesPerFrame;

            if (probe)
            {
                decodedBytes = ProbeDecodedBytes(containerKey, raw, out bytesPerFrame);
            }
            else
            {
                decodedBytes = raw * 2;
                bytesPerFrame = 2 * sizeof(float);
            }

            entries.Add(new DecentSamplerMemoryPolicy.Entry(
                cacheKey, containerKey, mode, decodedBytes, bytesPerFrame));
        }

        return entries;
    }

    // What a file costs decoded, read from its header. Falls back to twice the file size, which is what
    // 16-bit PCM costs as 32-bit float, when the header cannot be read or the container makes reading
    // it expensive.
    private long ProbeDecodedBytes(string containerKey, long rawBytes, out long bytesPerFrame)
    {
        // Two channels is the conservative guess: it makes a preload head look as expensive as it can.
        bytesPerFrame = 2 * sizeof(float);

        // An archive entry has to be decompressed to be read at all, and the WAV chunk walk crosses the
        // whole entry, so probing one costs as much as decoding it. Estimate instead.
        if (_container.IsArchive)
        {
            return rawBytes * 2;
        }

        try
        {
            using (var stream = _container.OpenStreamingFile(containerKey))
            using (var reader = SampleReaders.Open(stream, containerKey))
            {
                var format = reader.WaveFormat;
                var blockAlign = format.BlockAlign;

                if (blockAlign <= 0 || reader.Length <= 0)
                {
                    return rawBytes * 2;
                }

                var frames = reader.Length / blockAlign;
                var channels = SampleReaders.TargetChannelCount(Math.Max(1, format.Channels));

                bytesPerFrame = channels * sizeof(float);
                return frames * bytesPerFrame;
            }
        }
        catch (Exception)
        {
            // An unreadable file is a problem the decode itself will report, with a better message.
            return rawBytes * 2;
        }
    }

    // A binding moved a sample or loop point. The guide is explicit that those only work when the
    // engine is in memory mode, so a preset that moves them on a streamed instrument is told once.
    private void ReportStreamingOnlyParameter(DecentSamplerParameterChangedEventArgs arguments)
    {
        if (!_loaded || _streamedSampleCount == 0 || arguments == null)
        {
            return;
        }

        var parameter = arguments.Parameter;

        if (parameter != "SAMPLE_START" && parameter != "SAMPLE_END" &&
            parameter != "LOOP_START" && parameter != "LOOP_END")
        {
            return;
        }

        AddRuntimeProblem(
            $"{Name}: {parameter} is only valid for in-memory playback, and this instrument streams " +
            "some of its samples; the value is honoured at the next note-on and not on a sounding note");
    }

    /// <inheritdoc/>
    public override string ToString() =>
        $"{Name}: {Groups.Count} groups, {Zones.Count} zones, {Problems.Count} problems";
}
