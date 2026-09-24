using System.Collections.ObjectModel;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Testing.Security;

/// <summary>A settable <see cref="IUserContext"/> for unit tests.</summary>
/// <remarks>
/// <para>
/// Defaults to an authenticated <see cref="IdentityKind.User"/> with <see cref="DefaultSubjectId"/>, so most tests
/// need no setup. <see cref="IsAuthenticated"/> follows <see cref="IdentityKind"/>, as on every real context; set
/// <see cref="IdentityKind"/> to <see cref="IdentityKind.Anonymous"/> for an unauthenticated caller.
/// </para>
/// <para>
/// For a step-up with a maximum age (<c>[RequireAuthenticationMethod("otp", MaxAgeSeconds = 300)]</c>), list the
/// method and date it from the test's clock:
/// <c>new FakeUserContext { AuthenticationMethods = ["pwd", "otp"] }.WithAuthenticationMethodTime("otp", clock.UtcNow.AddMinutes(-2))</c>
/// is fresh, and <c>AddMinutes(-6)</c> is expired.
/// </para>
/// </remarks>
public sealed class FakeUserContext : IUserContext
{
    /// <summary>The default subject id.</summary>
    public const string DefaultSubjectId = "11111111-1111-1111-1111-111111111111";

    /// <inheritdoc/>
    public IdentityKind IdentityKind { get; set; } = IdentityKind.User;

    /// <inheritdoc/>
    public bool IsAuthenticated => IdentityKind != IdentityKind.Anonymous;

    /// <inheritdoc/>
    /// <remarks>Defaults to <see cref="DefaultSubjectId"/>.</remarks>
    public string? SubjectId { get; set; } = DefaultSubjectId;

    /// <inheritdoc/>
    public string? ClientId { get; set; }

    /// <inheritdoc/>
    public Guid? TenantId { get; set; }

    /// <inheritdoc/>
    public string? SessionId { get; set; }

    /// <inheritdoc/>
    public string? Name { get; set; }

    /// <inheritdoc/>
    public string? Email { get; set; }

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Roles { get; set; } = [];

    /// <inheritdoc/>
    public IReadOnlyCollection<string> Permissions { get; set; } = [];

    /// <inheritdoc/>
    public IReadOnlyCollection<string> AuthenticationMethods { get; set; } = [];

    /// <summary>
    /// Gets or sets when authentication methods were verified, keyed by method. Empty by default. Read by
    /// <see cref="GetAuthenticationMethodTime"/>, which compares methods ordinally.
    /// </summary>
    /// <remarks>
    /// As on <see cref="UserContext.AuthenticationMethodTimes"/>, a time only dates a method:
    /// <see cref="AuthenticationMethods"/> still decides which methods the caller has, and a listed method without a
    /// time was verified at <see cref="AuthTime"/>. <see cref="WithAuthenticationMethodTime"/> sets one entry.
    /// </remarks>
    public IReadOnlyDictionary<string, DateTimeOffset> AuthenticationMethodTimes { get; set; } =
        ReadOnlyDictionary<string, DateTimeOffset>.Empty;

    /// <inheritdoc/>
    public string? AuthContextClassReference { get; set; }

    /// <inheritdoc/>
    public DateTimeOffset? AuthTime { get; set; }

    /// <inheritdoc/>
    public bool IsSenderConstrained { get; set; }

    /// <summary>Gets or sets the claims searched by <see cref="FindClaim"/> and <see cref="FindClaims"/>, in order.</summary>
    public IReadOnlyList<KeyValuePair<string, string>> Claims { get; set; } = [];

    /// <inheritdoc/>
    public string? FindClaim(string claimType)
    {
        ArgumentNullException.ThrowIfNull(claimType);
        return Claims.FirstOrDefault(claim => string.Equals(claim.Key, claimType, StringComparison.Ordinal)).Value;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> FindClaims(string claimType)
    {
        ArgumentNullException.ThrowIfNull(claimType);
        return [.. Claims.Where(claim => string.Equals(claim.Key, claimType, StringComparison.Ordinal)).Select(claim => claim.Value)];
    }

    /// <inheritdoc/>
    public bool HasRole(string role) => Roles.Contains(role, StringComparer.Ordinal);

    /// <inheritdoc/>
    public bool HasPermission(string permission) => Permissions.Contains(permission, StringComparer.Ordinal);

    /// <inheritdoc/>
    public bool WasAuthenticatedWith(string method) => AuthenticationMethods.Contains(method, StringComparer.Ordinal);

    /// <inheritdoc/>
    public bool IsAuthenticationFresherThan(TimeSpan maxAge, DateTimeOffset now) =>
        AuthTime is { } authTime && authTime - now <= UserContext.MaxFutureAuthTime && now - authTime <= maxAge;

    /// <inheritdoc/>
    /// <remarks>
    /// Answers as <see cref="UserContext"/> does: <see langword="null"/> unless <see cref="WasAuthenticatedWith"/> is
    /// <see langword="true"/> for the method; then its time in <see cref="AuthenticationMethodTimes"/>, or, when it has
    /// none, <see cref="AuthTime"/>.
    /// </remarks>
    public DateTimeOffset? GetAuthenticationMethodTime(string method)
    {
        ArgumentNullException.ThrowIfNull(method);

        if (!WasAuthenticatedWith(method))
        {
            return null;
        }

        foreach (var (recordedMethod, verifiedAt) in AuthenticationMethodTimes)
        {
            if (string.Equals(recordedMethod, method, StringComparison.Ordinal))
            {
                return verifiedAt;
            }
        }

        return AuthTime;
    }

    /// <summary>Records when the caller verified an authentication method, replacing an earlier time for it.</summary>
    /// <param name="method">The authentication method reference, such as <c>otp</c>.</param>
    /// <param name="verifiedAt">When it was verified; take it from the test's clock.</param>
    /// <returns>This context.</returns>
    /// <remarks>
    /// Only dates the method: list it in <see cref="AuthenticationMethods"/> too, or
    /// <see cref="GetAuthenticationMethodTime"/> ignores the time, as a real context does.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="method"/> is null, empty or white space.</exception>
    public FakeUserContext WithAuthenticationMethodTime(string method, DateTimeOffset verifiedAt)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(method);

        var times = new Dictionary<string, DateTimeOffset>(StringComparer.Ordinal);
        foreach (var (recordedMethod, recordedAt) in AuthenticationMethodTimes)
        {
            times[recordedMethod] = recordedAt;
        }

        times[method] = verifiedAt;
        AuthenticationMethodTimes = times.AsReadOnly();
        return this;
    }
}
