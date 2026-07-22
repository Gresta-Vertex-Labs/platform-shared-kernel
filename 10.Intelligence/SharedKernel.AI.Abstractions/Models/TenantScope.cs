namespace SharedKernel.AI.Abstractions.Models;

/// <summary>
/// The tenant discriminator value for a vector-collection operation — a mandatory, non-nullable,
/// non-defaulted separate method parameter on every read and every filtered write of
/// <see cref="Abstractions.IVectorCollection{TRecord}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Value"/> is the tenant discriminator <em>value</em>. The <em>field</em> name lives on
/// <see cref="VectorCollectionDefinition.TenantField"/>, never here and never caller-supplied at query
/// time.
/// </para>
/// <para>
/// <b>Mandatory, non-nullable, non-defaulted — the highest-severity decision in the domain:</b> a
/// tenant predicate travelling through the same filter tree as business predicates can be dropped by a
/// translation bug; a dropped business clause is a bug, a dropped tenant clause is a cross-tenant data
/// leak. Adapters inject it as the outermost <c>AND</c> clause after translating the caller's
/// <see cref="VectorFilter"/>, so no translation path can omit it.
/// </para>
/// <para>
/// <b>Fail closed, driven by the collection definition — not by a builder flag:</b> if the registered
/// collection definition declares a <see cref="VectorCollectionDefinition.TenantField"/> and the
/// caller passes <see cref="None"/>, the provider returns <c>IntelligenceErrors.TenantScopeMissing</c>
/// and performs no I/O.
/// </para>
/// <para>
/// Structurally identical to <c>09.Search</c>'s <c>TenantScope</c>, deliberately — this is a proven,
/// load-bearing shape and reinventing it differently here would just be gratuitous inconsistency.
/// Declared locally rather than shared because <c>10.Intelligence</c> may not reference
/// <c>09.Search</c>.
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
    /// Gets the explicit single-tenant/global-collection sentinel (<see cref="Value"/> equals
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
