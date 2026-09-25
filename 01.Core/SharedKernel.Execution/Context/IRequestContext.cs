namespace SharedKernel.Execution.Context;

/// <summary>
/// The identity of the caller currently executing a request: who it is, which tenant it acts for,
/// what kind of actor it is, and whether it holds a permission.
/// </summary>
/// <remarks>
/// <para>
/// The single caller contract of the platform. <c>SharedKernel.Application.Behaviors</c> authorizes
/// against it and scopes caches by it; <c>06.Persistence</c> stamps audit columns and audit records
/// from it and filters tenant data by <see cref="TenantId"/>. A service implements it once, at its
/// composition root, over its real identity source — typically <c>12.Security</c>'s
/// <c>IUserContext</c>/<c>ITenantProvider</c>, for which <c>13.ServiceDefaults</c> ships a ready-made
/// implementation. This package itself references neither.
/// </para>
/// <para>
/// <strong>Fail closed.</strong> When nothing is registered, the persistence layer falls back to
/// <see cref="AnonymousRequestContext"/>: not authenticated, no tenant. A <see langword="null"/>
/// <see cref="TenantId"/> matches no tenant-scoped row and rejects every tenant-scoped write.
/// </para>
/// <para>
/// Inbound adapters (the HTTP middleware, the message consume filter, the workflow interceptor, the
/// scheduler) make the context ambient with <see cref="RequestContextScope.Begin"/>, so code without a DI
/// scope reads it through <see cref="IRequestContextAccessor"/>.
/// </para>
/// <para>
/// <see cref="ActorKind"/>, <see cref="ClientId"/>, <see cref="SessionId"/>, <see cref="ImpersonatorId"/>
/// and <see cref="CorrelationId"/> have default implementations, so an implementation written before they
/// existed keeps compiling; override them wherever the identity source knows the answer.
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
    /// <remarks>
    /// Persistence writes this value to <c>CreatedBy</c>/<c>ModifiedBy</c>/<c>DeletedBy</c> and to an
    /// audit record's actor; when it is <see langword="null"/> the service name configured on the
    /// persistence layer is written instead.
    /// </remarks>
    string? UserId { get; }

    /// <summary>
    /// Gets the tenant identifier associated with the current request, or <see langword="null"/>
    /// when the request has no tenant.
    /// </summary>
    Guid? TenantId { get; }

    /// <summary>Gets the kind of actor the current caller is.</summary>
    /// <remarks>
    /// Defaults to <see cref="Context.ActorKind.User"/> for an authenticated caller and
    /// <see cref="Context.ActorKind.Anonymous"/> otherwise. An implementation that can tell a machine
    /// caller (client credentials, API key, mTLS) apart from a human reports
    /// <see cref="Context.ActorKind.Service"/> for it.
    /// </remarks>
    ActorKind ActorKind => IsAuthenticated ? ActorKind.User : ActorKind.Anonymous;

    /// <summary>Gets the OAuth2 client id the caller authenticated through, if known.</summary>
    string? ClientId => null;

    /// <summary>Gets the caller's session identifier, if the caller runs under a tracked session.</summary>
    string? SessionId => null;

    /// <summary>
    /// Gets the identity acting on behalf of <see cref="UserId"/> (impersonation or support tooling),
    /// or <see langword="null"/> when the caller acts as itself.
    /// </summary>
    string? ImpersonatorId => null;

    /// <summary>
    /// Gets the correlation id of the call, as received from the caller or created when the call entered
    /// the system, or <see langword="null"/> when the call has none.
    /// </summary>
    /// <remarks>
    /// Outbound clients forward this value unchanged, so one id follows a request across every service it
    /// reaches. It is not the trace id: a trace id is replaced at every process boundary that starts a new
    /// trace, a correlation id is not.
    /// </remarks>
    string? CorrelationId => null;

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
