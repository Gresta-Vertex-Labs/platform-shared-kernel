namespace SharedKernel.Security.Abstractions.Abstractions;

/// <summary>
/// Resolves the tenant identifier for the current request from JWT claims.
/// </summary>
/// <remarks>
/// <para>
/// Registered as <b>Scoped</b> — one instance per HTTP request. Never inject into singleton services.
/// </para>
/// <para>
/// Returns <see cref="Guid.Empty"/> when no tenant claim is present (unauthenticated or
/// system-level requests). Callers must handle the <see cref="Guid.Empty"/> case explicitly.
/// </para>
/// <para>
/// Application-layer code resolves <see cref="TenantId"/> and passes it as a <see cref="Guid"/>
/// primitive to domain aggregate constructors. Domain code (<c>03.Domain</c>) must never
/// reference <see cref="ITenantProvider"/> directly.
/// </para>
/// <para>
/// Infrastructure code (<c>06.Persistence</c>) may inject <see cref="ITenantProvider"/> only
/// for global tenant filter application in a multi-tenant <c>DbContext</c>.
/// </para>
/// </remarks>
public interface ITenantProvider
{
    /// <summary>
    /// Gets the tenant identifier for the current request.
    /// </summary>
    /// <value>
    /// The parsed <see cref="Guid"/> from the tenant claim, or <see cref="Guid.Empty"/>
    /// when the claim is absent or cannot be parsed.
    /// </value>
    Guid TenantId { get; }
}
