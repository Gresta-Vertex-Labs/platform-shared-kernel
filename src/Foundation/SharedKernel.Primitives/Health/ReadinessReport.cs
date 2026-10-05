using System.Collections.ObjectModel;

namespace SharedKernel.Primitives.Health;

/// <summary>The result of one <see cref="IReadinessProbe.ProbeAsync"/> call.</summary>
/// <remarks>
/// <para>
/// <see cref="Data"/> carries whatever detail the provider has — a round-trip time, a backlog size, a
/// document count — keyed by stable, PascalCase names the provider documents. It is copied when the report is
/// built, so changing the source dictionary afterwards does not change the report, and it is compared by
/// content: two reports are equal when their status, latency, description and data entries are equal.
/// </para>
/// <para>
/// Everything in a report may be shown on a health endpoint: never put connection strings, credentials,
/// tenant data or exception messages in it.
/// </para>
/// </remarks>
public sealed record ReadinessReport
{
    private static readonly IReadOnlyDictionary<string, object> Empty =
        new ReadOnlyDictionary<string, object>(new Dictionary<string, object>(StringComparer.Ordinal));

    private readonly ReadinessStatus _status;
    private readonly IReadOnlyDictionary<string, object> _data = Empty;

    /// <summary>Creates a report.</summary>
    /// <param name="status">The outcome.</param>
    /// <param name="latency">How long the check took, when the provider measured it.</param>
    /// <param name="description">A short, human-readable explanation, or <see langword="null"/>.</param>
    /// <param name="data">Provider-specific detail, or <see langword="null"/> for none.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="status"/> is not a defined <see cref="ReadinessStatus"/>, or <paramref name="latency"/>
    /// is negative.
    /// </exception>
    /// <exception cref="ArgumentException"><paramref name="data"/> contains a <see langword="null"/> value.</exception>
    public ReadinessReport(
        ReadinessStatus status,
        TimeSpan? latency = null,
        string? description = null,
        IReadOnlyDictionary<string, object>? data = null)
    {
        Status = status;
        Latency = latency;
        Description = description;
        Data = data ?? Empty;
    }

    /// <summary>The outcome.</summary>
    public ReadinessStatus Status
    {
        get => _status;
        init
        {
            if (!Enum.IsDefined(value))
                throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown readiness status.");
            _status = value;
        }
    }

    /// <summary>How long the check took, or <see langword="null"/> when the provider did not measure it.</summary>
    public TimeSpan? Latency
    {
        get;
        init
        {
            if (value < TimeSpan.Zero)
                throw new ArgumentOutOfRangeException(nameof(value), value, "Latency cannot be negative.");
            field = value;
        }
    }

    /// <summary>A short, human-readable explanation, or <see langword="null"/>.</summary>
    public string? Description { get; init; }

    /// <summary>Provider-specific detail. Never <see langword="null"/>; empty when the provider has none.</summary>
    public IReadOnlyDictionary<string, object> Data
    {
        get => _data;
        init => _data = Snapshot(value);
    }

    /// <summary>Whether <see cref="Status"/> is <see cref="ReadinessStatus.Healthy"/>.</summary>
    public bool IsHealthy => _status == ReadinessStatus.Healthy;

    /// <summary>Creates a <see cref="ReadinessStatus.Healthy"/> report.</summary>
    /// <param name="description">A short explanation, or <see langword="null"/>.</param>
    /// <param name="data">Provider-specific detail, or <see langword="null"/>.</param>
    /// <param name="latency">How long the check took, or <see langword="null"/>.</param>
    /// <returns>The report.</returns>
    public static ReadinessReport Healthy(
        string? description = null,
        IReadOnlyDictionary<string, object>? data = null,
        TimeSpan? latency = null) =>
        new(ReadinessStatus.Healthy, latency, description, data);

    /// <summary>Creates a <see cref="ReadinessStatus.Degraded"/> report.</summary>
    /// <param name="description">Why the dependency is degraded.</param>
    /// <param name="data">Provider-specific detail, or <see langword="null"/>.</param>
    /// <param name="latency">How long the check took, or <see langword="null"/>.</param>
    /// <returns>The report.</returns>
    public static ReadinessReport Degraded(
        string? description,
        IReadOnlyDictionary<string, object>? data = null,
        TimeSpan? latency = null) =>
        new(ReadinessStatus.Degraded, latency, description, data);

    /// <summary>Creates a <see cref="ReadinessStatus.Unhealthy"/> report.</summary>
    /// <param name="description">Why the dependency is not ready.</param>
    /// <param name="data">Provider-specific detail, or <see langword="null"/>.</param>
    /// <param name="latency">How long the check took, or <see langword="null"/>.</param>
    /// <returns>The report.</returns>
    public static ReadinessReport Unhealthy(
        string? description,
        IReadOnlyDictionary<string, object>? data = null,
        TimeSpan? latency = null) =>
        new(ReadinessStatus.Unhealthy, latency, description, data);

    /// <inheritdoc />
    public bool Equals(ReadinessReport? other)
    {
        if (other is null)
            return false;
        if (ReferenceEquals(this, other))
            return true;
        if (_status != other._status
            || Latency != other.Latency
            || !string.Equals(Description, other.Description, StringComparison.Ordinal)
            || _data.Count != other._data.Count)
        {
            return false;
        }

        foreach (var (key, value) in _data)
        {
            if (!other._data.TryGetValue(key, out var otherValue) || !object.Equals(value, otherValue))
                return false;
        }

        return true;
    }

    /// <inheritdoc />
    public override int GetHashCode()
    {
        var hash = HashCode.Combine(_status, Latency, Description, _data.Count);
        foreach (var key in _data.Keys.Order(StringComparer.Ordinal))
            hash = HashCode.Combine(hash, key);
        return hash;
    }

    private static IReadOnlyDictionary<string, object> Snapshot(IReadOnlyDictionary<string, object>? data)
    {
        if (data is null || data.Count == 0)
            return Empty;

        var copy = new Dictionary<string, object>(data.Count, StringComparer.Ordinal);
        foreach (var (key, value) in data)
        {
            if (value is null)
                throw new ArgumentException($"Readiness data entry '{key}' has a null value.", nameof(data));
            copy[key] = value;
        }

        return new ReadOnlyDictionary<string, object>(copy);
    }
}
