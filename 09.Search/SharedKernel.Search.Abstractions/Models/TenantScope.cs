namespace SharedKernel.Search.Abstractions.Models;

/// <summary>
/// The tenant discriminator value for a search operation — a mandatory, non-nullable, non-defaulted
/// separate method parameter on every read and every filtered write of <c>ISearchIndex&lt;TDocument&gt;</c>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Value"/> is the tenant discriminator <em>value</em>. The <em>field</em> name lives on
/// <c>SearchIndexDefinition.TenantField</c>, never here and never caller-supplied at query time.
/// </para>
/// <para>
/// <b>Mandatory, non-nullable, non-defaulted — the highest-severity decision in the domain:</b> a
/// tenant predicate travelling through the same filter tree as business predicates can be dropped by
/// a translation bug; a dropped business clause is a bug, a dropped tenant clause is a cross-tenant
/// data leak. A member on a request object can also be lost when that object crosses a layer
/// boundary; a required parameter cannot. Adapters inject it as the outermost <c>AND</c> clause after
/// translating the caller's filter, so no translation path can omit it.
/// </para>
/// <para>
/// <b>Fail closed, driven by the index definition — not by a builder flag:</b> if the registered
/// index definition declares a <c>TenantField</c> and the caller passes <see cref="None"/>, the
/// provider returns a tenant-scope-missing error and performs no I/O.
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
    /// Gets the explicit single-tenant/global-index sentinel (<see cref="Value"/> equals
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
