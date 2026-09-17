using Microsoft.Extensions.Options;
using SharedKernel.Persistence.Abstractions.Auditing;
using SharedKernel.Persistence.EfCore.Options;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Persistence.EfCore.Auditing;

/// <summary>
/// Default <see cref="IAuditActorContext"/>, bridging the already-approved <see cref="IUserContext"/>/
/// <see cref="ITenantProvider"/> (P-078, <c>SharedKernel.Security.Abstractions</c>).
/// </summary>
/// <remarks>
/// <para>
/// WO-071/P-457/D-125. Registered by <c>EfCorePersistenceBuilder{TContext}.WithAuditTrail()</c> —
/// a consuming service may override this default with its own <see cref="IAuditActorContext"/>
/// registration. A genuine ergonomic advantage over <c>05.Application</c>'s equivalent
/// <c>IRequestContext</c>, whose owning package has no <c>Security.Abstractions</c> access at
/// all and can never ship a default of its own.
/// </para>
/// <para>
/// <see cref="ActorId"/> is computed via the SAME <c>userId.ToString("D")</c>/service-name-fallback
/// format <see cref="Interceptors.AuditInterceptor"/> already uses (P-091) — reuse, not reinvent.
/// </para>
/// </remarks>
public sealed class EfCoreAuditActorContext : IAuditActorContext
{
    private readonly IUserContext _userContext;
    private readonly ITenantProvider _tenantProvider;
    private readonly IOptions<PersistenceServiceOptions> _serviceOptions;

    /// <summary>
    /// Initialises a new <see cref="EfCoreAuditActorContext"/>.
    /// </summary>
    /// <param name="userContext">The current scoped user identity.</param>
    /// <param name="tenantProvider">The current scoped tenant identity.</param>
    /// <param name="serviceOptions">Options providing the unauthenticated audit fallback string (defaults to <c>"system"</c>).</param>
    public EfCoreAuditActorContext(
        IUserContext userContext,
        ITenantProvider tenantProvider,
        IOptions<PersistenceServiceOptions> serviceOptions)
    {
        _userContext = userContext;
        _tenantProvider = tenantProvider;
        _serviceOptions = serviceOptions;
    }

    /// <inheritdoc />
    public string ActorId =>
        _userContext.IsAuthenticated && _userContext.SubjectId is { } subjectId
            ? subjectId
            : _serviceOptions.Value.ServiceName;

    /// <inheritdoc />
    public Guid TenantId => _tenantProvider.TenantId;
}
