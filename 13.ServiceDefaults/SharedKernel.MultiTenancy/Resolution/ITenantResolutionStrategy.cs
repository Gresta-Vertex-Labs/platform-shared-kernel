using Microsoft.AspNetCore.Http;

namespace SharedKernel.MultiTenancy.Resolution;

/// <summary>
/// Resolves a tenant identifier from an incoming HTTP request, or signals that this strategy is
/// not applicable to the given request.
/// </summary>
/// <remarks>
/// <see cref="TryResolveAsync"/> returns <see langword="null"/> — and never throws — when this
/// strategy cannot resolve a tenant from the given request. <see cref="Middleware.TenantResolutionMiddleware"/>
/// then tries the next strategy in <see cref="TenantResolutionOptions.StrategyOrder"/>. Reserve
/// exceptions for genuinely exceptional conditions, not the expected "this strategy doesn't apply
/// to this request" case.
/// </remarks>
public interface ITenantResolutionStrategy
{
    /// <summary>
    /// Attempts to resolve a tenant identifier from <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The resolved tenant identifier, or <see langword="null"/> when this strategy cannot
    /// resolve a tenant from the given request.
    /// </returns>
    Task<Guid?> TryResolveAsync(HttpContext context, CancellationToken cancellationToken);
}
