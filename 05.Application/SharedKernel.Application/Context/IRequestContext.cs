namespace SharedKernel.Application.Context;

/// <summary>
/// A minimal seam exposing the identity of the caller currently executing a request.
/// </summary>
/// <remarks>
/// <para>
/// This is <b>not</b> <c>SharedKernel.Security.Abstractions.IUserContext</c>, and this package
/// carries no project reference to <c>12.Security</c> — the layering ceiling for
/// <c>05.Application</c> is <c>01–04</c>. It exposes only the minimal shape
/// <c>SharedKernel.Application.Behaviors.Authorization.AuthorizationBehavior{TRequest,TResponse}</c>
/// and the caching pipeline's tenant-scoping logic need. The consuming service implements this
/// interface at its own composition root over its real identity source (typically
/// <c>IUserContext</c>/<c>ITenantProvider</c>) — the same bridging pattern
/// <c>Transaction.IUnitOfWork</c> already established for
/// <c>SharedKernel.Persistence.Abstractions.IUnitOfWork</c>.
/// </para>
/// <para>
/// Correlation id is deliberately <b>not</b> exposed here — it flows ambiently through
/// <see cref="System.Diagnostics.Activity"/> baggage, not through this seam.
/// </para>
/// </remarks>
public interface IRequestContext
{
    /// <summary>Gets a value indicating whether the current caller is authenticated.</summary>
    bool IsAuthenticated { get; }

    /// <summary>
    /// Gets the opaque subject identifier of the current caller, or <see langword="null"/> when
    /// <see cref="IsAuthenticated"/> is <see langword="false"/>.
    /// </summary>
    string? UserId { get; }

    /// <summary>
    /// Gets the tenant identifier associated with the current request, or <see langword="null"/>
    /// when the request has no tenant.
    /// </summary>
    Guid? TenantId { get; }

    /// <summary>
    /// Determines whether the current caller holds <paramref name="permission"/>.
    /// </summary>
    /// <param name="permission">
    /// An opaque permission name whose meaning is entirely owned by the consuming service's
    /// implementation — this package never interprets it.
    /// </param>
    /// <param name="cancellationToken">A token to observe for cancellation.</param>
    ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken);
}
