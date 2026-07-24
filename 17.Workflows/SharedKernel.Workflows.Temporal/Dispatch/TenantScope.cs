namespace SharedKernel.Workflows.Temporal.Dispatch;

/// <summary>
/// The tenant discriminator value for a workflow dispatch operation — a mandatory, non-nullable,
/// non-defaulted separate parameter on every member of <see cref="IWorkflowDispatcher"/> and
/// <see cref="IWorkflowHandle"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Mandatory, non-nullable, non-defaulted — more load-bearing here than anywhere else on the
/// platform:</b> a workflow execution is addressed by its workflow id, a caller-supplied string in a
/// flat per-namespace keyspace. Without a tenant discriminator baked into the id by a mechanism the
/// caller cannot bypass, tenant B signalling tenant A's workflow is one guessed string away — and
/// unlike a search query, a signal <em>mutates</em>. <see cref="TenantScope"/> feeds
/// <see cref="IWorkflowIdFactory"/>, which composes the physical workflow id, and travels as a
/// Temporal header value asserted worker-side by the propagation interceptor on every activity and
/// signal handler. Both halves are required: the id prevents collision, the header prevents a
/// workflow started under one tenant from executing activities under another.
/// </para>
/// <para>
/// A dispatch call made with <see cref="None"/> against a tenant-scoped operation returns
/// <see cref="Errors.WorkflowErrors.TenantScopeMissing"/> with no I/O performed.
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

    /// <summary>
    /// Gets the explicit sentinel meaning "no tenant scope supplied" (<see cref="Value"/> equals
    /// <see cref="string.Empty"/>).
    /// </summary>
    public static TenantScope None { get; } = new(string.Empty);

    /// <summary>Creates a <see cref="TenantScope"/> for the given tenant discriminator value.</summary>
    /// <exception cref="ArgumentException"><paramref name="value"/> is null, empty, or whitespace.</exception>
    public static TenantScope Of(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        return new TenantScope(value);
    }
}
