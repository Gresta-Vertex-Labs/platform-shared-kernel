using SharedKernel.Execution.Tenancy;
using SharedKernel.Execution.Context;

namespace SharedKernel.Security.Abstractions;

/// <summary>The <see cref="IUserContext"/> of an unauthenticated caller.</summary>
/// <remarks>Every identifier is <see langword="null"/>, every collection is empty and every check returns <see langword="false"/>.</remarks>
public sealed class AnonymousUserContext : IUserContext
{
    /// <summary>Gets the shared instance.</summary>
    public static AnonymousUserContext Instance { get; } = new();

    private AnonymousUserContext()
    {
    }

    /// <inheritdoc/>
    public ActorKind ActorKind => ActorKind.Anonymous;

    /// <inheritdoc/>
    public bool IsAuthenticated => false;

    /// <inheritdoc/>
    public string? SubjectId => null;

    /// <inheritdoc/>
    public string? ClientId => null;

    /// <inheritdoc/>
    public TenantId? TenantId => null;

    /// <inheritdoc/>
    public string? SessionId => null;

    /// <inheritdoc/>
    public string? Name => null;

    /// <inheritdoc/>
    public string? Email => null;

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Roles => [];

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Permissions => [];

    /// <inheritdoc/>
    public IReadOnlyCollection<string> AuthenticationMethods => [];

    /// <inheritdoc/>
    public string? AuthContextClassReference => null;

    /// <inheritdoc/>
    public DateTimeOffset? AuthTime => null;

    /// <inheritdoc/>
    public bool IsSenderConstrained => false;

    /// <inheritdoc/>
    public string? FindClaim(string claimType) => null;

    /// <inheritdoc/>
    public IReadOnlyList<string> FindClaims(string claimType) => [];

    /// <inheritdoc/>
    public bool HasRole(string role) => false;

    /// <inheritdoc/>
    public bool HasPermission(string permission) => false;

    /// <inheritdoc/>
    public bool WasAuthenticatedWith(string method) => false;

    /// <inheritdoc/>
    public bool IsAuthenticationFresherThan(TimeSpan maxAge, DateTimeOffset now) => false;
}
