namespace SharedKernel.Persistence.EfCore.Options;

/// <summary>
/// Discoverability/observability record describing the transient-fault retry configuration
/// registered via <c>EfCorePersistenceBuilder.WithTransientFaultRetry(...)</c>.
/// </summary>
/// <param name="MaxRetryCount">The maximum number of retry attempts.</param>
/// <param name="MaxRetryDelay">The maximum delay between retry attempts, or <see langword="null"/> to use the provider default.</param>
/// <remarks>
/// <para>
/// WO-051/P-320 — carries no behavior of its own. Registering this record does NOT itself enable
/// Npgsql retry — <c>SharedKernel.Persistence.EfCore</c> never references Npgsql, so it cannot call
/// <c>EnableRetryOnFailure</c> directly. This is a documented, required two-call PAIR with
/// <c>UsePostgreSQL(..., maxRetryCount, maxRetryDelay)</c> (the PostgreSQL package) — the only legal
/// call site for that Npgsql extension method. Calling only
/// <c>EfCorePersistenceBuilder.WithTransientFaultRetry()</c> without also passing matching values to
/// <c>UsePostgreSQL(...)</c> registers this options singleton but enables no actual retry behavior.
/// </para>
/// </remarks>
public sealed record TransientFaultRetryOptions(int MaxRetryCount, TimeSpan? MaxRetryDelay);
