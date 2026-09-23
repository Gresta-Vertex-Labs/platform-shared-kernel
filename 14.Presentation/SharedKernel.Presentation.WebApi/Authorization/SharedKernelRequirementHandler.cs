using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Presentation.WebApi.Authorization;

/// <summary>
/// Evaluates this package's requirements against the <see cref="IUserContext"/> of the principal being authorized —
/// the same for an HTTP request, a SignalR hub method and a gRPC call — never against raw claims.
/// </summary>
/// <remarks>
/// The caller is resolved with <see cref="UserContextResolver"/> over the registered <see cref="IUserContextMapper"/>s,
/// so the authentication package decides which claims carry roles and permissions. The clock is resolved only when a
/// freshness requirement is evaluated.
/// </remarks>
internal sealed class SharedKernelRequirementHandler : IAuthorizationHandler
{
    private readonly IEnumerable<IUserContextMapper> _mappers;
    private readonly IServiceProvider _services;

    public SharedKernelRequirementHandler(IEnumerable<IUserContextMapper> mappers, IServiceProvider services)
    {
        _mappers = mappers;
        _services = services;
    }

    /// <inheritdoc />
    public Task HandleAsync(AuthorizationHandlerContext context)
    {
        var pending = context.PendingRequirements.OfType<SharedKernelRequirement>().ToArray();
        if (pending.Length == 0)
        {
            return Task.CompletedTask;
        }

        var user = UserContextResolver.Resolve(context.User, _mappers);
        if (!user.IsAuthenticated)
        {
            // An anonymous caller fails the authenticated-user requirement every policy carries and is challenged.
            // A signed-in principal that no mapper understands cannot be evaluated: refuse it outright rather than
            // asking it to sign in again, which could never help.
            if (context.User.Identities.Any(identity => identity.IsAuthenticated))
            {
                context.Fail(new AuthorizationFailureReason(this, "No IUserContextMapper handles the caller's authentication scheme."));
            }

            return Task.CompletedTask;
        }

        foreach (var requirement in pending)
        {
            if (requirement.IsSatisfiedBy(user, Now))
            {
                context.Succeed(requirement);
            }
        }

        return Task.CompletedTask;
    }

    private DateTimeOffset Now() => (_services.GetService<IClock>() ?? new SystemClock()).UtcNow;
}
