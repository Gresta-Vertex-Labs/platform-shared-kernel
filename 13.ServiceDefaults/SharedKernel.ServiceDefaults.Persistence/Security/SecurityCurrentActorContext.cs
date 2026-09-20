using Microsoft.Extensions.Options;
using SharedKernel.Persistence.Abstractions.Context;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.ServiceDefaults.Persistence.Security;

/// <summary>
/// Default <see cref="ICurrentActorContext"/> that bridges <see cref="IUserContext"/>
/// (<c>12.Security.Abstractions</c>) into <c>06.Persistence</c>'s local actor seam.
/// </summary>
/// <remarks>
/// <para>
/// Split from the former combined <c>IAuditActorContext</c>/<c>SecurityCurrentActorContext</c>
/// bridge — see <see cref="SecurityCurrentTenantContext"/> for the tenant half.
/// <c>SharedKernel.Persistence.EfCore</c> no longer references
/// <c>SharedKernel.Security.Abstractions</c> at all — it only knows about the seam
/// (<see cref="ICurrentActorContext"/>) and registers a fully system-attributed default
/// (<c>AnonymousActorContext</c>) when nothing else is registered. Register this bridge instead, via
/// <see cref="Extensions.PersistenceSecurityExtensions.AddSharedKernelPersistenceSecurityBridge"/>,
/// so audit columns and the audit trail resolve the real caller identity.
/// </para>
/// <para>
/// <see cref="ActorId"/> uses the SAME format <c>AuditInterceptor</c> always has: the
/// authenticated caller's <see cref="IUserContext.SubjectId"/> when present, otherwise
/// <see cref="PersistenceServiceOptions.ServiceName"/>. <see cref="ActorKind"/> maps
/// <see cref="IUserContext.IdentityKind"/> onto this package's smaller, persistence-local vocabulary:
/// <c>IdentityKind.User</c> → <c>ActorKind.User</c>, <c>IdentityKind.ServicePrincipal</c> →
/// <c>ActorKind.Service</c>, and both <c>IdentityKind.System</c> and <c>IdentityKind.Anonymous</c> →
/// <c>ActorKind.System</c> (an anonymous caller falls back to the service-name identity, which IS
/// the platform, hence <c>System</c>).
/// </para>
/// </remarks>
public sealed class SecurityCurrentActorContext : ICurrentActorContext
{
    private readonly IUserContext _userContext;
    private readonly IOptions<PersistenceServiceOptions> _serviceOptions;

    /// <summary>Initialises a new <see cref="SecurityCurrentActorContext"/>.</summary>
    /// <param name="userContext">The current scoped user identity.</param>
    /// <param name="serviceOptions">Options providing the unauthenticated audit fallback string.</param>
    public SecurityCurrentActorContext(
        IUserContext userContext,
        IOptions<PersistenceServiceOptions> serviceOptions)
    {
        _userContext = userContext;
        _serviceOptions = serviceOptions;
    }

    /// <inheritdoc />
    public string ActorId =>
        _userContext.IsAuthenticated && _userContext.SubjectId is { } subjectId
            ? subjectId
            : _serviceOptions.Value.ServiceName;

    /// <inheritdoc />
    public ActorKind ActorKind => _userContext.IdentityKind switch
    {
        IdentityKind.User => ActorKind.User,
        IdentityKind.ServicePrincipal => ActorKind.Service,
        _ => ActorKind.System,
    };
}
