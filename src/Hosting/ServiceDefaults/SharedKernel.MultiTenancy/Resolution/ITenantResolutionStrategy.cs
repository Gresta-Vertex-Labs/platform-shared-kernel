using SharedKernel.Execution.Tenancy;
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
    /// Gets the explicit resolution-order key this strategy is identified by.
    /// </summary>
    /// <remarks>
    /// Matched against <see cref="TenantResolutionOptions.StrategyOrder"/> entries by
    /// <see cref="Middleware.TenantResolutionMiddleware"/> — never against the implementing
    /// type's CLR type name. The three platform strategies declare their <see cref="StrategyName"/>
    /// from <see cref="TenantResolutionStrategyNames"/>; a custom strategy registered by a
    /// consuming service declares its own value and becomes reachable from
    /// <see cref="TenantResolutionOptions.StrategyOrder"/> purely by that declared value.
    /// </remarks>
    string StrategyName { get; }

    /// <summary>
    /// Attempts to resolve a tenant identifier from <paramref name="context"/>.
    /// </summary>
    /// <param name="context">The current HTTP context.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>
    /// The resolved tenant identifier, or <see langword="null"/> when this strategy cannot
    /// resolve a tenant from the given request.
    /// </returns>
    Task<TenantId?> TryResolveAsync(HttpContext context, CancellationToken cancellationToken);
}
