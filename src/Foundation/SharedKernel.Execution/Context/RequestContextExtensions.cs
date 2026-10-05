using SharedKernel.Execution.Tenancy;

namespace SharedKernel.Execution.Context;

/// <summary>Derives a request context that differs from another in one member.</summary>
public static class RequestContextExtensions
{
    /// <summary>
    /// Returns a context identical to <paramref name="context"/> except that its
    /// <see cref="IRequestContext.CorrelationId"/> is <paramref name="correlationId"/>.
    /// </summary>
    /// <param name="context">The context to derive from. Every other member is forwarded to it on each access.</param>
    /// <param name="correlationId">The correlation id of the derived context.</param>
    /// <returns>The derived context.</returns>
    /// <remarks>
    /// Used by an inbound adapter that restored the correlation id from its own transport while the caller came from
    /// somewhere else — for example the gRPC server interceptor over the DI-registered caller.
    /// </remarks>
    public static IRequestContext WithCorrelationId(this IRequestContext context, string correlationId)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(correlationId);

        return new DerivedRequestContext(context, overridesTenant: false, tenantId: null, correlationId);
    }

    /// <summary>
    /// Returns a context identical to <paramref name="context"/> except that its <see cref="IRequestContext.TenantId"/>
    /// is <paramref name="tenantId"/>.
    /// </summary>
    /// <param name="context">The context to derive from. Every other member is forwarded to it on each access.</param>
    /// <param name="tenantId">The tenant of the derived context, or <see langword="null"/> for none.</param>
    /// <returns>The derived context.</returns>
    public static IRequestContext WithTenant(this IRequestContext context, TenantId? tenantId)
    {
        ArgumentNullException.ThrowIfNull(context);

        return new DerivedRequestContext(context, overridesTenant: true, tenantId, correlationId: null);
    }

    private sealed class DerivedRequestContext(
        IRequestContext inner,
        bool overridesTenant,
        TenantId? tenantId,
        string? correlationId) : IRequestContext
    {
        public bool IsAuthenticated => inner.IsAuthenticated;

        public string? UserId => inner.UserId;

        public TenantId? TenantId => overridesTenant ? tenantId : inner.TenantId;

        public ActorKind ActorKind => inner.ActorKind;

        public string? ClientId => inner.ClientId;

        public string? SessionId => inner.SessionId;

        public string? ImpersonatorId => inner.ImpersonatorId;

        public string? CorrelationId => correlationId ?? inner.CorrelationId;

        public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken)
            => inner.HasPermissionAsync(permission, cancellationToken);
    }
}
