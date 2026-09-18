using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Serialization.Metadata;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;
using OpenFeature.Serialization;
using SharedKernel.FeatureManagement;

namespace SharedKernel.Testing.FeatureManagement;

/// <summary>
/// In-memory OpenFeature <see cref="IFeatureClient"/> for unit tests: flags return the values the test sets,
/// with no configuration, provider or host.
/// </summary>
/// <remarks>
/// <para>
/// Mirrors the real client's contract: a flag the test never set returns the caller's default with
/// <see cref="ErrorType.FlagNotFound"/>, and no evaluation throws. Evaluate through the same
/// <see cref="FeatureClientExtensions"/> calls production code uses (<c>IsEnabledAsync(flag)</c>,
/// <c>GetValueAsync(flag)</c>).
/// </para>
/// <para>
/// A deterministic value map, not a rules engine: a test that needs "on for tenant A only" sets a rule
/// with <see cref="Set{T}(FeatureFlag{T}, Func{EvaluationContext, T})"/>. It does not reproduce percentage
/// rollouts; those belong to <c>Microsoft.FeatureManagement</c>'s own tests.
/// </para>
/// </remarks>
public sealed class FakeFeatureClient : IFeatureClient
{
    private readonly ConcurrentDictionary<string, Func<EvaluationContext, object>> _rules = new(StringComparer.Ordinal);
    private readonly ConcurrentQueue<string> _evaluated = new();
    private readonly ConcurrentQueue<string> _tracked = new();
    private readonly List<Hook> _hooks = [];
    private EvaluationContext _context = EvaluationContext.Empty;

    /// <summary>Every flag key evaluated, in order, including repeats.</summary>
    public IReadOnlyCollection<string> EvaluatedFlags => _evaluated;

    /// <summary>Every tracking event name passed to <see cref="Track"/>, in order.</summary>
    public IReadOnlyCollection<string> TrackedEvents => _tracked;

    /// <inheritdoc />
    public ProviderStatus ProviderStatus => ProviderStatus.Ready;

    /// <summary>Turns <paramref name="flag"/> on or off for every caller.</summary>
    /// <param name="flag">The flag.</param>
    /// <param name="enabled">Whether it is on.</param>
    /// <returns>This fake, for chaining.</returns>
    public FakeFeatureClient SetEnabled(FeatureFlag<bool> flag, bool enabled = true) => Set(flag, enabled);

    /// <summary>Sets the value <paramref name="flag"/> returns for every caller.</summary>
    /// <typeparam name="T"><see cref="bool"/>, <see cref="string"/>, <see cref="int"/> or <see cref="double"/>.</typeparam>
    /// <param name="flag">The flag.</param>
    /// <param name="value">The value.</param>
    /// <returns>This fake, for chaining.</returns>
    public FakeFeatureClient Set<T>(FeatureFlag<T> flag, T value) => Set(flag, _ => value);

    /// <summary>
    /// Sets a rule computing the value of <paramref name="flag"/> from the evaluation context (the client's
    /// context merged with the call's), for tests of targeted behavior.
    /// </summary>
    /// <typeparam name="T"><see cref="bool"/>, <see cref="string"/>, <see cref="int"/> or <see cref="double"/>.</typeparam>
    /// <param name="flag">The flag.</param>
    /// <param name="rule">Computes the value for one evaluation.</param>
    /// <returns>This fake, for chaining.</returns>
    /// <example><c>fake.Set(Flags.NewCheckout, ctx =&gt; ctx.GetValue(FeatureContextKeys.TenantId)?.AsString == "acme");</c></example>
    public FakeFeatureClient Set<T>(FeatureFlag<T> flag, Func<EvaluationContext, T> rule)
    {
        ArgumentNullException.ThrowIfNull(flag);
        ArgumentNullException.ThrowIfNull(rule);
        if (flag.Kind == FeatureFlagKind.Object)
        {
            throw new ArgumentException(
                $"'{flag.Key}' is an object flag; use the SetObject overload that takes its JsonTypeInfo.", nameof(flag));
        }

        _rules[flag.Key] = context => rule(context)!;
        return this;
    }

    /// <summary>Sets the value an object flag returns for every caller.</summary>
    /// <typeparam name="T">The flag's type.</typeparam>
    /// <param name="flag">The flag.</param>
    /// <param name="value">The value.</param>
    /// <param name="typeInfo">The same source-generated metadata the flag was declared with.</param>
    /// <returns>This fake, for chaining.</returns>
    public FakeFeatureClient SetObject<T>(FeatureFlag<T> flag, T value, JsonTypeInfo<T> typeInfo)
    {
        ArgumentNullException.ThrowIfNull(flag);
        ArgumentNullException.ThrowIfNull(typeInfo);
        string json = JsonSerializer.Serialize(value, typeInfo);
        Value structure = JsonSerializer.Deserialize(json, OpenFeatureJsonSerializerContext.Default.Value) ?? new Value();
        _rules[flag.Key] = _ => structure;
        return this;
    }

    /// <summary>Forgets every value, rule, evaluation and tracked event.</summary>
    public void Reset()
    {
        _rules.Clear();
        _evaluated.Clear();
        _tracked.Clear();
    }

    /// <summary>Whether <paramref name="flag"/> was evaluated at least once.</summary>
    /// <param name="flag">The flag.</param>
    /// <returns><see langword="true"/> if it was evaluated.</returns>
    public bool WasEvaluated(FeatureFlag flag)
    {
        ArgumentNullException.ThrowIfNull(flag);
        return _evaluated.Contains(flag.Key);
    }

    /// <inheritdoc />
    public Task<bool> GetBooleanValueAsync(string flagKey, bool defaultValue, EvaluationContext? context = null, FlagEvaluationOptions? config = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(flagKey, defaultValue, context).Value);

    /// <inheritdoc />
    public Task<FlagEvaluationDetails<bool>> GetBooleanDetailsAsync(string flagKey, bool defaultValue, EvaluationContext? context = null, FlagEvaluationOptions? config = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(flagKey, defaultValue, context));

    /// <inheritdoc />
    public Task<string> GetStringValueAsync(string flagKey, string defaultValue, EvaluationContext? context = null, FlagEvaluationOptions? config = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(flagKey, defaultValue, context).Value);

    /// <inheritdoc />
    public Task<FlagEvaluationDetails<string>> GetStringDetailsAsync(string flagKey, string defaultValue, EvaluationContext? context = null, FlagEvaluationOptions? config = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(flagKey, defaultValue, context));

    /// <inheritdoc />
    public Task<int> GetIntegerValueAsync(string flagKey, int defaultValue, EvaluationContext? context = null, FlagEvaluationOptions? config = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(flagKey, defaultValue, context).Value);

    /// <inheritdoc />
    public Task<FlagEvaluationDetails<int>> GetIntegerDetailsAsync(string flagKey, int defaultValue, EvaluationContext? context = null, FlagEvaluationOptions? config = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(flagKey, defaultValue, context));

    /// <inheritdoc />
    public Task<double> GetDoubleValueAsync(string flagKey, double defaultValue, EvaluationContext? context = null, FlagEvaluationOptions? config = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(flagKey, defaultValue, context).Value);

    /// <inheritdoc />
    public Task<FlagEvaluationDetails<double>> GetDoubleDetailsAsync(string flagKey, double defaultValue, EvaluationContext? context = null, FlagEvaluationOptions? config = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(flagKey, defaultValue, context));

    /// <inheritdoc />
    public Task<Value> GetObjectValueAsync(string flagKey, Value defaultValue, EvaluationContext? context = null, FlagEvaluationOptions? config = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(flagKey, defaultValue, context).Value);

    /// <inheritdoc />
    public Task<FlagEvaluationDetails<Value>> GetObjectDetailsAsync(string flagKey, Value defaultValue, EvaluationContext? context = null, FlagEvaluationOptions? config = null, CancellationToken cancellationToken = default) =>
        Task.FromResult(Resolve(flagKey, defaultValue, context));

    /// <inheritdoc />
    public void Track(string trackingEventName, EvaluationContext? evaluationContext = null, TrackingEventDetails? trackingEventDetails = null) =>
        _tracked.Enqueue(trackingEventName);

    /// <inheritdoc />
    public void AddHooks(IEnumerable<Hook> hooks)
    {
        lock (_hooks)
        {
            _hooks.AddRange(hooks);
        }
    }

    /// <inheritdoc />
    public IEnumerable<Hook> GetHooks()
    {
        lock (_hooks)
        {
            return _hooks.ToArray();
        }
    }

    /// <inheritdoc />
    public EvaluationContext GetContext() => _context;

    /// <inheritdoc />
    public void SetContext(EvaluationContext context) => _context = context ?? EvaluationContext.Empty;

    /// <inheritdoc />
    public ClientMetadata GetMetadata() => new(nameof(FakeFeatureClient), null);

    /// <inheritdoc />
    public void AddHandler(ProviderEventTypes type, EventHandlerDelegate handler)
    {
    }

    /// <inheritdoc />
    public void RemoveHandler(ProviderEventTypes type, EventHandlerDelegate handler)
    {
    }

    private FlagEvaluationDetails<T> Resolve<T>(string flagKey, T defaultValue, EvaluationContext? invocation)
    {
        _evaluated.Enqueue(flagKey);
        if (!_rules.TryGetValue(flagKey, out Func<EvaluationContext, object>? rule))
        {
            return new FlagEvaluationDetails<T>(
                flagKey, defaultValue, ErrorType.FlagNotFound, Reason.Error, null, $"Flag '{flagKey}' is not set in the fake.", null);
        }

        EvaluationContext merged = invocation is null
            ? _context
            : EvaluationContext.Builder().Merge(_context).Merge(invocation).Build();

        return rule(merged) is T value
            ? new FlagEvaluationDetails<T>(flagKey, value, ErrorType.None, Reason.Static, null, null, null)
            : new FlagEvaluationDetails<T>(
                flagKey, defaultValue, ErrorType.TypeMismatch, Reason.Error, null, $"Flag '{flagKey}' is set to another type in the fake.", null);
    }
}
