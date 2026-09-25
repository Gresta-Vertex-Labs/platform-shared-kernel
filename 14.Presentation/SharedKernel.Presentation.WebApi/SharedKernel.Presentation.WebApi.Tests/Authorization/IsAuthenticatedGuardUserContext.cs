using SharedKernel.Execution.Context;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Presentation.WebApi.Tests.Authorization;

/// <summary>
/// A test-only <see cref="IUserContext"/> whose <see cref="IsAuthenticated"/> getter throws if it
/// is ever read.
/// </summary>
/// <remarks>
/// Used to prove that <see cref="Authorization.AuthorizationRequirementEndpointFilter"/> rejects an
/// unauthenticated-shaped caller purely through the ordinary <see cref="HasRole"/>/
/// <see cref="HasPermission"/> false-path — mirroring <c>12.Security</c>'s shipped
/// <c>AnonymousUserContext</c>/<c>SystemUserContext</c>, both of which hardcode
/// <c>HasRole</c>/<c>HasPermission</c> to <see langword="false"/> unconditionally — never via a
/// bespoke <see cref="IsAuthenticated"/> branch. If a future change to the filter introduced such a
/// branch, a test using this double would fail with the exception thrown here instead of a clean
/// 403 rejection, making the regression immediately obvious.
/// </remarks>
internal sealed class IsAuthenticatedGuardUserContext : IUserContext
{
    public string? SubjectId => null;

    public string? ClientId => null;

    public SharedKernel.Execution.Tenancy.TenantId? TenantId => null;

    public string? SessionId => null;

    public string? Name => null;

    public string? FindClaim(string claimType) => null;

    public IReadOnlyList<string> FindClaims(string claimType) => [];

    public string? Email => null;

    public IReadOnlyCollection<string> Roles => [];

    public IReadOnlyCollection<string> Permissions => [];

    public bool IsAuthenticated =>
        throw new InvalidOperationException(
            "AuthorizationRequirementEndpointFilter must never read IsAuthenticated — rejection of an " +
            "unauthenticated-shaped caller must flow through the ordinary HasRole/HasPermission false-path only.");

    public ActorKind ActorKind => ActorKind.Anonymous;

    public IReadOnlyCollection<string> AuthenticationMethods => [];

    public string? AuthContextClassReference => null;

    public DateTimeOffset? AuthTime => null;

    public bool IsSenderConstrained => false;

    public bool HasRole(string role) => false;

    public bool HasPermission(string permission) => false;

    public bool WasAuthenticatedWith(string method) => false;

    public bool IsAuthenticationFresherThan(TimeSpan maxAge, DateTimeOffset now) => false;
}
