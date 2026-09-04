namespace SharedKernel.Scheduling.Registry;

/// <summary>
/// The tenant discriminator value for a single scheduled job registration.
/// </summary>
/// <remarks>
/// <para>
/// <b>Deliberately used as <c>TenantScope?</c> everywhere in this domain — never mandatory.</b> Every
/// other tenant-aware capability domain on this platform (<c>09.Search</c>, <c>10.Intelligence</c>,
/// <c>17.Workflows</c>, <c>18.Idempotency</c>) makes a tenant-scope parameter mandatory and
/// non-defaulted on every operation. <c>19.Scheduling</c> deliberately does not, because a scheduled
/// job is registered <b>once, at startup, as a system-level actor</b> — it is not a per-request or
/// per-tenant operation the way a search query or a vector write is.
/// </para>
/// <para>
/// A genuinely per-tenant recurring job (for example, "send each active tenant's weekly digest") is
/// modeled as <b>one system-level registration whose command handler iterates its own tenant
/// directory</b> — the scheduler itself never fans out N tenant-scoped executions on the job's behalf.
/// <see cref="Value"/> exists purely as an optional, informational label a job registration can attach
/// (surfaced on <see cref="Jobs.ScheduledJobExecutionContext.TenantScope"/> for logging/telemetry
/// correlation, or for the rare job that is genuinely single-tenant-owned) — it carries no
/// tenant-isolation enforcement of any kind, unlike its namesake types in other domains. Do not "fix"
/// this into a mandatory parameter; that would require the scheduler to fan out per-tenant
/// executions itself, which is explicitly out of scope for this domain.
/// </para>
/// </remarks>
public readonly record struct TenantScope
{
    private TenantScope(string value)
    {
        Value = value;
    }

    /// <summary>Gets the tenant discriminator value.</summary>
    public string Value { get; }

    /// <summary>Creates a <see cref="TenantScope"/> for the given tenant discriminator value.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null, empty, or whitespace.</exception>
    public static TenantScope Of(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new TenantScope(value);
    }
}
