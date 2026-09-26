namespace SharedKernel.ServiceDefaults.HealthChecks;

/// <summary>Settings of <see cref="ReadinessHealthCheckExtensions.AddSharedKernelReadiness"/>.</summary>
public sealed class ReadinessHealthCheckOptions
{
    private readonly HashSet<string> _excluded = new(StringComparer.Ordinal);

    /// <summary>
    /// Gets or sets how long one probe may run before its check reports unhealthy, or <see langword="null"/>
    /// (default) for no limit beyond the request's own.
    /// </summary>
    public TimeSpan? Timeout { get; set; }

    /// <summary>Gets the names of the probes that are not mapped to health checks.</summary>
    public IReadOnlySet<string> ExcludedProbes => _excluded;

    /// <summary>Keeps the probe named <paramref name="probeName"/> off the readiness endpoint.</summary>
    /// <param name="probeName">The probe's name.</param>
    /// <returns>These options, for chaining.</returns>
    /// <exception cref="ArgumentException"><paramref name="probeName"/> is <see langword="null"/>, empty or whitespace.</exception>
    public ReadinessHealthCheckOptions Exclude(string probeName)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(probeName);
        _excluded.Add(probeName);
        return this;
    }
}
