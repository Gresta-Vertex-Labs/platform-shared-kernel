using System.Collections.Concurrent;
using System.Globalization;
using System.Text;
using System.Text.Json;
using OpenFeature;
using OpenFeature.Constant;
using OpenFeature.Model;
using OpenFeature.Serialization;

namespace SharedKernel.FeatureManagement.Internal;

/// <summary>
/// The per-scope <see cref="IFeatureClient"/>: the first successful evaluation of a flag is reused for the rest
/// of the scope (up to a maximum age), so a flag cannot change halfway through one request or message.
/// </summary>
internal sealed class ScopedFeatureClient : IFeatureClient
{
    // Unit separator: cannot appear in a flag key or a JSON-serialized value unescaped.
    private const char Separator = (char)0x1F;

    private readonly IFeatureClient _inner;
    private readonly TimeProvider _time;
    private readonly TimeSpan _lifetime;
    private readonly ConcurrentDictionary<string, Entry> _results = new(StringComparer.Ordinal);

    public ScopedFeatureClient(IFeatureClient inner, TimeProvider time, TimeSpan lifetime)
    {
        _inner = inner;
        _time = time;
        _lifetime = lifetime;
    }

    public ProviderStatus ProviderStatus => _inner.ProviderStatus;

    public void AddHandler(ProviderEventTypes type, EventHandlerDelegate handler) => _inner.AddHandler(type, handler);

    public void RemoveHandler(ProviderEventTypes type, EventHandlerDelegate handler) => _inner.RemoveHandler(type, handler);

    public void AddHooks(IEnumerable<Hook> hooks) => _inner.AddHooks(hooks);

    public IEnumerable<Hook> GetHooks() => _inner.GetHooks();

    public EvaluationContext GetContext() => _inner.GetContext();

    public void SetContext(EvaluationContext context)
    {
        _inner.SetContext(context);
        _results.Clear();
    }

    public ClientMetadata GetMetadata() => _inner.GetMetadata();

    public void Track(string trackingEventName, EvaluationContext? evaluationContext = null, TrackingEventDetails? trackingEventDetails = null) =>
        _inner.Track(trackingEventName, evaluationContext, trackingEventDetails);

    public async Task<bool> GetBooleanValueAsync(string flagKey, bool defaultValue, EvaluationContext? context = null, FlagEvaluationOptions? config = null, CancellationToken cancellationToken = default) =>
        (await GetBooleanDetailsAsync(flagKey, defaultValue, context, config, cancellationToken).ConfigureAwait(false)).Value;

    public Task<FlagEvaluationDetails<bool>> GetBooleanDetailsAsync(string flagKey, bool defaultValue, EvaluationContext? context = null, FlagEvaluationOptions? config = null, CancellationToken cancellationToken = default) =>
        GetOrEvaluateAsync(FlagValueType.Boolean, flagKey, defaultValue ? "true" : "false", context, config,
            () => _inner.GetBooleanDetailsAsync(flagKey, defaultValue, context, config, cancellationToken));

    public async Task<string> GetStringValueAsync(string flagKey, string defaultValue, EvaluationContext? context = null, FlagEvaluationOptions? config = null, CancellationToken cancellationToken = default) =>
        (await GetStringDetailsAsync(flagKey, defaultValue, context, config, cancellationToken).ConfigureAwait(false)).Value;

    public Task<FlagEvaluationDetails<string>> GetStringDetailsAsync(string flagKey, string defaultValue, EvaluationContext? context = null, FlagEvaluationOptions? config = null, CancellationToken cancellationToken = default) =>
        GetOrEvaluateAsync(FlagValueType.String, flagKey, defaultValue, context, config,
            () => _inner.GetStringDetailsAsync(flagKey, defaultValue, context, config, cancellationToken));

    public async Task<int> GetIntegerValueAsync(string flagKey, int defaultValue, EvaluationContext? context = null, FlagEvaluationOptions? config = null, CancellationToken cancellationToken = default) =>
        (await GetIntegerDetailsAsync(flagKey, defaultValue, context, config, cancellationToken).ConfigureAwait(false)).Value;

    public Task<FlagEvaluationDetails<int>> GetIntegerDetailsAsync(string flagKey, int defaultValue, EvaluationContext? context = null, FlagEvaluationOptions? config = null, CancellationToken cancellationToken = default) =>
        GetOrEvaluateAsync(FlagValueType.Number, flagKey, "i:" + defaultValue.ToString(CultureInfo.InvariantCulture), context, config,
            () => _inner.GetIntegerDetailsAsync(flagKey, defaultValue, context, config, cancellationToken));

    public async Task<double> GetDoubleValueAsync(string flagKey, double defaultValue, EvaluationContext? context = null, FlagEvaluationOptions? config = null, CancellationToken cancellationToken = default) =>
        (await GetDoubleDetailsAsync(flagKey, defaultValue, context, config, cancellationToken).ConfigureAwait(false)).Value;

    public Task<FlagEvaluationDetails<double>> GetDoubleDetailsAsync(string flagKey, double defaultValue, EvaluationContext? context = null, FlagEvaluationOptions? config = null, CancellationToken cancellationToken = default) =>
        GetOrEvaluateAsync(FlagValueType.Number, flagKey, "d:" + defaultValue.ToString("R", CultureInfo.InvariantCulture), context, config,
            () => _inner.GetDoubleDetailsAsync(flagKey, defaultValue, context, config, cancellationToken));

    public async Task<Value> GetObjectValueAsync(string flagKey, Value defaultValue, EvaluationContext? context = null, FlagEvaluationOptions? config = null, CancellationToken cancellationToken = default) =>
        (await GetObjectDetailsAsync(flagKey, defaultValue, context, config, cancellationToken).ConfigureAwait(false)).Value;

    public Task<FlagEvaluationDetails<Value>> GetObjectDetailsAsync(string flagKey, Value defaultValue, EvaluationContext? context = null, FlagEvaluationOptions? config = null, CancellationToken cancellationToken = default) =>
        GetOrEvaluateAsync(FlagValueType.Object, flagKey, Serialize(defaultValue), context, config,
            () => _inner.GetObjectDetailsAsync(flagKey, defaultValue, context, config, cancellationToken));

    private async Task<FlagEvaluationDetails<T>> GetOrEvaluateAsync<T>(
        FlagValueType type,
        string flagKey,
        string defaultKey,
        EvaluationContext? context,
        FlagEvaluationOptions? config,
        Func<Task<FlagEvaluationDetails<T>>> evaluate)
    {
        // A call with its own hooks or hints asks for a real evaluation.
        if (config is not null)
        {
            return await evaluate().ConfigureAwait(false);
        }

        string key = CacheKey(type, flagKey, defaultKey, context);
        DateTimeOffset now = _time.GetUtcNow();
        if (_results.TryGetValue(key, out Entry cached)
            && cached.Details is FlagEvaluationDetails<T> reused
            && now - cached.EvaluatedAt < _lifetime)
        {
            return reused;
        }

        FlagEvaluationDetails<T> details = await evaluate().ConfigureAwait(false);
        if (details.ErrorType != ErrorType.None)
        {
            return details;
        }

        // Two concurrent first evaluations in one scope: keep the first stored, so both callers agree.
        Entry stored = _results.AddOrUpdate(
            key,
            new Entry(details, now),
            (_, existing) => now - existing.EvaluatedAt < _lifetime ? existing : new Entry(details, now));

        return stored.Details as FlagEvaluationDetails<T> ?? details;
    }

    private static string CacheKey(FlagValueType type, string flagKey, string defaultKey, EvaluationContext? context)
    {
        var key = new StringBuilder();
        key.Append((int)type).Append(Separator).Append(flagKey).Append(Separator).Append(defaultKey);
        if (context is not null)
        {
            key.Append(Separator).Append(context.TargetingKey);
            foreach (KeyValuePair<string, Value> entry in context.AsDictionary().OrderBy(static e => e.Key, StringComparer.Ordinal))
            {
                key.Append(Separator).Append(entry.Key).Append('=').Append(Serialize(entry.Value));
            }
        }

        return key.ToString();
    }

    private static string Serialize(Value? value) =>
        value is null ? "null" : JsonSerializer.Serialize(value, OpenFeatureJsonSerializerContext.Default.Value);

    private readonly record struct Entry(object Details, DateTimeOffset EvaluatedAt);
}
