namespace SharedKernel.Persistence.EfCore.Interceptors;

/// <summary>
/// Provides the current user's identifier for audit field population in EF Core interceptors.
/// </summary>
/// <remarks>
/// <para>
/// This interface is defined within <c>SharedKernel.Persistence.EfCore</c> to avoid a forbidden
/// project reference to <c>12.Security</c> packages from the persistence layer.
/// </para>
/// <para>
/// <c>EfCorePersistenceBuilder.Build()</c> registers a scoped no-op implementation that returns
/// <c>"system"</c> when no <c>IUserContext</c> is already present in the DI container. Consuming
/// services override this by registering their own implementation (e.g., from
/// <c>SharedKernel.Security.Oidc</c>) before or after calling <c>.Build()</c> — the last
/// registration wins.
/// </para>
/// <para>
/// <strong>Scoped lifetime required.</strong> Both <c>AuditInterceptor</c> and
/// <c>SoftDeleteInterceptor</c> are scoped so they receive a per-request <c>IUserContext</c>
/// instance from DI.
/// </para>
/// </remarks>
public interface IUserContext
{
    /// <summary>
    /// Gets the identifier of the currently authenticated user, or a fallback value (e.g.,
    /// <c>"system"</c>) when no user context is available.
    /// </summary>
    string UserId { get; }
}
