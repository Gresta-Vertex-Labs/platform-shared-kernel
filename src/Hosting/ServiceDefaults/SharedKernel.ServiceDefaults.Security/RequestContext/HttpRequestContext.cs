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
/// <para>
/// When the request ends, <see cref="RequestContextMiddleware"/> calls <see cref="CaptureCaller"/>, so a context that
/// outlives its request — a SignalR connection keeps the context of the request that opened it, and with long polling
/// that request ends long before the connection — still answers from the caller the request authenticated, never
/// from the request's disposed services (P-579).
/// </para>
/// </remarks>
/// <param name="httpContext">The request.</param>
/// <param name="correlationId">The request's correlation id.</param>
internal sealed class HttpRequestContext(HttpContext httpContext, string correlationId) : IRequestContext
{
    private SecurityRequestContext? _caller;

    private SecurityRequestContext Caller =>
        _caller ??= httpContext.RequestServices.GetRequiredService<SecurityRequestContext>();

    /// <summary>
    /// Resolves the caller now, while the request's services are alive, unless something already has. Does nothing
    /// when no <c>IUserContext</c> is registered: nothing could have read the caller then either.
    /// </summary>
    internal void CaptureCaller()
    {
        if (_caller is not null)
        {
            return;
        }

        var services = httpContext.RequestServices;
        if (services?.GetService<IServiceProviderIsService>()?.IsService(typeof(SharedKernel.Security.Abstractions.IUserContext)) == true)
        {
            _caller = services.GetRequiredService<SecurityRequestContext>();
        }
    }

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
