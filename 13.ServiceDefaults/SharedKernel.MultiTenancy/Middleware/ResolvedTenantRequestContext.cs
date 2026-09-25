using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;

namespace SharedKernel.MultiTenancy.Middleware;

/// <summary>
/// The request's <see cref="IRequestContext"/> with its tenant replaced by the one
/// <see cref="TenantResolutionMiddleware"/> resolved; every other member forwards to the inner context.
/// </summary>
/// <param name="inner">The request's own context (the caller).</param>
/// <param name="tenantId">The resolved tenant, or <see langword="null"/> when none was resolved.</param>
internal sealed class ResolvedTenantRequestContext(IRequestContext inner, TenantId? tenantId) : IRequestContext
{
    /// <inheritdoc/>
    public bool IsAuthenticated => inner.IsAuthenticated;

    /// <inheritdoc/>
    public string? UserId => inner.UserId;

    /// <inheritdoc/>
    public TenantId? TenantId => tenantId;

    /// <inheritdoc/>
    public ActorKind ActorKind => inner.ActorKind;

    /// <inheritdoc/>
    public string? ClientId => inner.ClientId;

    /// <inheritdoc/>
    public string? SessionId => inner.SessionId;

    /// <inheritdoc/>
    public string? ImpersonatorId => inner.ImpersonatorId;

    /// <inheritdoc/>
    public string? CorrelationId => inner.CorrelationId;

    /// <inheritdoc/>
    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken) =>
        inner.HasPermissionAsync(permission, cancellationToken);
}
