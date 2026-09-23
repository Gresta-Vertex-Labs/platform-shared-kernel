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

    public override bool IsSatisfiedBy(IUserContext user, Func<DateTimeOffset> now) =>
        user.IsAuthenticationFresherThan(MaxAge, now());
}

/// <summary>Satisfied when the caller authenticated with any of the methods (<c>amr</c> values).</summary>
internal sealed class AuthenticationMethodRequirement : SharedKernelRequirement
{
    public AuthenticationMethodRequirement(IReadOnlyList<string> methods)
    {
        Methods = methods;
    }

    public IReadOnlyList<string> Methods { get; }

    public override bool IsStepUp => true;

    public override bool IsSatisfiedBy(IUserContext user, Func<DateTimeOffset> now) => Methods.Any(user.WasAuthenticatedWith);
}
