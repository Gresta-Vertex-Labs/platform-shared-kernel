using Microsoft.AspNetCore.Authorization;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using SharedKernel.Primitives.Clocks;
using SharedKernel.Primitives.Logging;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Presentation.WebApi.Authorization;

/// <summary>
/// Evaluates this package's requirements against the <see cref="IUserContext"/> of the principal being authorized —
/// the same for an HTTP request, a SignalR hub method and a gRPC call — never against raw claims.
/// </summary>
/// <remarks>
/// The caller is resolved with <see cref="UserContextResolver"/> over the registered <see cref="IUserContextMapper"/>s,
/// so the authentication package decides which claims carry roles and permissions. The clock is resolved only when a
/// freshness requirement, or an authentication-method requirement with a maximum age, is evaluated — on every SignalR
/// hub method call as well, which is what ends a step-up on a connection that stays open.
/// </remarks>
internal sealed partial class SharedKernelRequirementHandler : IAuthorizationHandler
{
    private const string NoAuthenticationType = "(none)";

    private readonly IEnumerable<IUserContextMapper> _mappers;
    private readonly IServiceProvider _services;
    private readonly ILogger<SharedKernelRequirementHandler> _logger;

    public SharedKernelRequirementHandler(
        IEnumerable<IUserContextMapper> mappers,
        IServiceProvider services,
        ILogger<SharedKernelRequirementHandler>? logger = null)
    {
        _mappers = mappers;
        _services = services;
        _logger = logger ?? NullLogger<SharedKernelRequirementHandler>.Instance;
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
            // asking it to sign in again, which could never help, and say why — the response cannot.
            if (context.User.Identities.FirstOrDefault(identity => identity.IsAuthenticated) is { } identity)
            {
                Log.NoUserContextMapper(_logger, identity.AuthenticationType ?? NoAuthenticationType);
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

    private static partial class Log
    {
        [LoggerMessage(
            EventId = LoggingEventIdRanges.Presentation + 9,
            Level = LogLevel.Warning,
            Message = "Refused a caller authenticated as {AuthenticationType}: no IUserContextMapper handles that "
                + "authentication type, so its roles and permissions cannot be read. Register the mapper of the "
                + "authentication package in use.")]
        public static partial void NoUserContextMapper(ILogger logger, string authenticationType);
    }
}
