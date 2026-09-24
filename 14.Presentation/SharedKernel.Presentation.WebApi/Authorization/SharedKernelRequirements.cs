using Microsoft.AspNetCore.Authorization;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Presentation.WebApi.Authorization;

/// <summary>A requirement of one of this package's attributes, evaluated against the caller's <see cref="IUserContext"/>.</summary>
internal abstract class SharedKernelRequirement : IAuthorizationRequirement
{
    /// <summary>
    /// Gets a value indicating whether failing this requirement asks the caller to authenticate again, more recently
    /// or more strongly (RFC 9470), rather than refusing them.
    /// </summary>
    public virtual bool IsStepUp => false;

    /// <summary>
    /// Gets the longest acceptable age of the authentication this requirement asks for, which a step-up challenge
    /// carries as <c>max_age</c> (RFC 9470); <see langword="null"/> when the requirement sets none.
    /// </summary>
    public virtual TimeSpan? StepUpMaxAge => null;

    public abstract bool IsSatisfiedBy(IUserContext user, Func<DateTimeOffset> now);
}

/// <summary>Satisfied when the caller holds any of the permissions.</summary>
internal sealed class PermissionRequirement : SharedKernelRequirement
{
    public PermissionRequirement(IReadOnlyList<string> permissions)
    {
        Permissions = permissions;
    }

    public IReadOnlyList<string> Permissions { get; }

    public override bool IsSatisfiedBy(IUserContext user, Func<DateTimeOffset> now) => Permissions.Any(user.HasPermission);
}

/// <summary>Satisfied when the caller holds any of the roles.</summary>
internal sealed class RoleRequirement : SharedKernelRequirement
{
    public RoleRequirement(IReadOnlyList<string> roles)
    {
        Roles = roles;
    }

    public IReadOnlyList<string> Roles { get; }

    public override bool IsSatisfiedBy(IUserContext user, Func<DateTimeOffset> now) => Roles.Any(user.HasRole);
}

/// <summary>Satisfied when the caller authenticated no longer than <see cref="MaxAge"/> ago.</summary>
internal sealed class FreshAuthenticationRequirement : SharedKernelRequirement
{
    public FreshAuthenticationRequirement(TimeSpan maxAge)
    {
        MaxAge = maxAge;
    }

    public TimeSpan MaxAge { get; }

    public override bool IsStepUp => true;

    public override TimeSpan? StepUpMaxAge => MaxAge;

    public override bool IsSatisfiedBy(IUserContext user, Func<DateTimeOffset> now) =>
        user.IsAuthenticationFresherThan(MaxAge, now());
}

/// <summary>
/// Satisfied when the caller authenticated with any of the methods (<c>amr</c> values) and, when <see cref="MaxAge"/> is
/// set, verified that method no longer than <see cref="MaxAge"/> ago.
/// </summary>
/// <remarks>
/// Without a maximum age a method counts for as long as the principal carries it, which on a SignalR connection is the
/// life of the connection. With one, the method's time (<see cref="IUserContext.GetAuthenticationMethodTime"/>) is
/// compared with the clock at every evaluation, the way <see cref="UserContext.IsAuthenticationFresherThan"/> compares
/// the sign-in time: an unknown time is never recent, and a time more than <see cref="UserContext.MaxFutureAuthTime"/>
/// ahead of the clock never passes.
/// </remarks>
internal sealed class AuthenticationMethodRequirement : SharedKernelRequirement
{
    public AuthenticationMethodRequirement(IReadOnlyList<string> methods, TimeSpan? maxAge = null)
    {
        Methods = methods;
        MaxAge = maxAge;
    }

    public IReadOnlyList<string> Methods { get; }

    public TimeSpan? MaxAge { get; }

    public override bool IsStepUp => true;

    public override TimeSpan? StepUpMaxAge => MaxAge;

    public override bool IsSatisfiedBy(IUserContext user, Func<DateTimeOffset> now)
    {
        if (MaxAge is not { } maxAge)
        {
            return Methods.Any(user.WasAuthenticatedWith);
        }

        var current = now();
        return Methods.Any(method => user.WasAuthenticatedWith(method) && IsRecent(user.GetAuthenticationMethodTime(method), maxAge, current));
    }

    private static bool IsRecent(DateTimeOffset? verifiedAt, TimeSpan maxAge, DateTimeOffset now) =>
        verifiedAt is { } at && at - now <= UserContext.MaxFutureAuthTime && now - at <= maxAge;
}
