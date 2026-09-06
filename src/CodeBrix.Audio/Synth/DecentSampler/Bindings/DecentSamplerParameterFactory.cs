using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.CompilerServices;
using CodeBrix.Audio.Synth.DecentSampler.Internal;

namespace CodeBrix.Audio.Synth.DecentSampler.Bindings;

/// <summary>
/// Builds the parameter target behind a (thing, parameter) pair, and remembers the ones it has built
/// so that two bindings on the same parameter share one target and therefore one set of modulation
/// slots.
/// </summary>
/// <remarks>
/// <para>
/// Targets are created lazily. Appendix B lists over three hundred parameters and a preset may hold
/// dozens of groups, so building every combination up front would cost thousands of objects for the
/// dozen a preset actually binds.
/// </para>
/// <para>
/// A target PUSHES its effective value onto the runtime object that owns it, so
/// <c>group.Volume</c>, <c>zone.LoopStart</c> and <c>effect.WetLevel</c> stay plain properties the
/// voice runtime reads with no lookup.
/// </para>
/// </remarks>
internal sealed partial class DecentSamplerParameterFactory
{
    private readonly DecentSamplerBindingEngine _engine;
    private readonly Dictionary<OwnerKey, DecentSamplerParameter> _cache = new(OwnerKeyComparer.Instance);

    internal DecentSamplerParameterFactory(DecentSamplerBindingEngine engine) => _engine = engine;

    /// <summary>Every target built so far, in the order they were first needed.</summary>
    internal IReadOnlyCollection<DecentSamplerParameter> Targets => _cache.Values;

    private static string Fold(string parameter) => DecentSamplerEnumNames.Fold(parameter);

    private static string Ordinal(int value) => value.ToString(CultureInfo.InvariantCulture);

    private static bool TryTrailingIndex(string folded, string prefix, string suffix, int count, out int index)
    {
        index = 0;

        if (!folded.StartsWith(prefix, StringComparison.Ordinal) ||
            !folded.EndsWith(suffix, StringComparison.Ordinal) ||
            folded.Length <= prefix.Length + suffix.Length)
        {
            return false;
        }

        var middle = folded.Substring(prefix.Length, folded.Length - prefix.Length - suffix.Length);

        return int.TryParse(middle, NumberStyles.None, CultureInfo.InvariantCulture, out index) &&
               index >= 1 && index <= count;
    }

    private static bool TryEnumeration<TEnum>(DecentSamplerParameterValue value, out TEnum result)
        where TEnum : struct, Enum
    {
        if (value.Kind == DecentSamplerParameterValueKind.Text && value.Text != null &&
            DecentSamplerEnumNames.TryParse<TEnum>(value.Text, out result))
        {
            return true;
        }

        var members = Enum.GetValues<TEnum>();
        var ordinal = (int)Math.Round(value.AsNumber, MidpointRounding.AwayFromZero);

        if (ordinal >= 0 && ordinal < members.Length)
        {
            result = members[ordinal];
            return true;
        }

        result = default;
        return false;
    }

    private static int ToInteger(DecentSamplerParameterValue value) =>
        (int)Math.Round(value.AsNumber, MidpointRounding.AwayFromZero);

    private static long ToFrame(DecentSamplerParameterValue value)
    {
        var number = Math.Round(value.AsNumber, MidpointRounding.AwayFromZero);
        return number < 0.0 ? 0L : number > long.MaxValue ? long.MaxValue : (long)number;
    }

    private DecentSamplerParameter Number(
        object owner, string name, string parameter, double initial, Action<double> publish) =>
        Build(
            owner, name, parameter, DecentSamplerParameterValueKind.Number,
            DecentSamplerParameterValue.FromNumber(initial),
            value => publish(value.AsNumber));

    private DecentSamplerParameter Integer(
        object owner, string name, string parameter, double initial, Action<int> publish) =>
        Build(
            owner, name, parameter, DecentSamplerParameterValueKind.Number,
            DecentSamplerParameterValue.FromNumber(initial),
            value => publish(ToInteger(value)));

    private DecentSamplerParameter Frames(
        object owner, string name, string parameter, double initial, Action<long> publish) =>
        Build(
            owner, name, parameter, DecentSamplerParameterValueKind.Number,
            DecentSamplerParameterValue.FromNumber(initial),
            value => publish(ToFrame(value)));

    private DecentSamplerParameter Switch(
        object owner, string name, string parameter, bool initial, Action<bool> publish) =>
        Build(
            owner, name, parameter, DecentSamplerParameterValueKind.Boolean,
            DecentSamplerParameterValue.FromBoolean(initial),
            value => publish(value.AsBoolean));

    private DecentSamplerParameter Words(
        object owner, string name, string parameter, string initial, Action<string> publish) =>
        Build(
            owner, name, parameter, DecentSamplerParameterValueKind.Text,
            DecentSamplerParameterValue.FromText(initial),
            value => publish(value.AsText));

    private DecentSamplerParameter Choice<TEnum>(
        object owner, string name, string parameter, TEnum initial, Action<TEnum> publish)
        where TEnum : struct, Enum =>
        Build(
            owner, name, parameter, DecentSamplerParameterValueKind.Text,
            DecentSamplerParameterValue.FromText(initial.ToString()),
            value =>
            {
                if (TryEnumeration<TEnum>(value, out var member))
                {
                    publish(member);
                }
            });

    private DecentSamplerParameter Command(object owner, string name, string parameter, Action run) =>
        Build(
            owner, name, parameter, DecentSamplerParameterValueKind.Action,
            DecentSamplerParameterValue.FromBoolean(false),
            value =>
            {
                if (value.AsBoolean)
                {
                    run();
                }
            });

    private DecentSamplerParameter Build(
        object owner,
        string name,
        string parameter,
        DecentSamplerParameterValueKind kind,
        DecentSamplerParameterValue initial,
        Action<DecentSamplerParameterValue> publish)
    {
        // The cache key folds the parameter's spelling, because a preset writes parameter="value" as
        // readily as parameter="VALUE" and both must land on ONE target - otherwise two bindings on the
        // same knob would keep separate modulation slots.
        var key = new OwnerKey(owner, Fold(parameter));

        if (_cache.TryGetValue(key, out var existing))
        {
            return existing;
        }

        var target = new DecentSamplerParameter(name, parameter, kind, initial, publish);
        target.Changed += _engine.OnParameterChanged;
        _cache[key] = target;
        return target;
    }

    private readonly struct OwnerKey(object owner, string parameter)
    {
        public object Owner { get; } = owner;

        public string Parameter { get; } = parameter;
    }

    private sealed class OwnerKeyComparer : IEqualityComparer<OwnerKey>
    {
        internal static readonly OwnerKeyComparer Instance = new OwnerKeyComparer();

        public bool Equals(OwnerKey left, OwnerKey right) =>
            ReferenceEquals(left.Owner, right.Owner) &&
            string.Equals(left.Parameter, right.Parameter, StringComparison.Ordinal);

        public int GetHashCode(OwnerKey key) =>
            RuntimeHelpers.GetHashCode(key.Owner) ^
            (key.Parameter?.GetHashCode(StringComparison.Ordinal) ?? 0);
    }
}
