using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Execution.Context;
using SharedKernel.Execution.Tenancy;

namespace SharedKernel.ServiceDefaults.Security;

/// <summary>
/// The ambient <see cref="IRequestContext"/> of one HTTP request: the correlation id resolved when the request
/// entered, and the caller of the request's <see cref="SecurityRequestContext"/>.
/// </summary>
/// <remarks>
/// The caller is resolved on first access, not when the request enters, because
/// <see cref="RequestContextMiddleware"/> runs before <c>UseAuthentication()</c>: the scoped <c>IUserContext</c>
/// behind <see cref="SecurityRequestContext"/> snapshots <c>HttpContext.User</c> when it is first created, so
/// creating it any earlier would fix the caller as anonymous for the whole request.
/// </remarks>
/// <param name="httpContext">The request.</param>
/// <param name="correlationId">The request's correlation id.</param>
internal sealed class HttpRequestContext(HttpContext httpContext, string correlationId) : IRequestContext
{
    private SecurityRequestContext? _caller;

    private SecurityRequestContext Caller =>
        _caller ??= httpContext.RequestServices.GetRequiredService<SecurityRequestContext>();

    /// <inheritdoc />
    public bool IsAuthenticated => Caller.IsAuthenticated;

    /// <inheritdoc />
    public string? UserId => Caller.UserId;

    /// <inheritdoc />
    public TenantId? TenantId => Caller.TenantId;

    /// <inheritdoc />
    public ActorKind ActorKind => Caller.ActorKind;

    /// <inheritdoc />
    public string? ClientId => Caller.ClientId;

    /// <inheritdoc />
    public string? SessionId => Caller.SessionId;

    /// <inheritdoc />
    public string? CorrelationId => correlationId;

    /// <inheritdoc />
    public ValueTask<bool> HasPermissionAsync(string permission, CancellationToken cancellationToken) =>
        Caller.HasPermissionAsync(permission, cancellationToken);
}
