using SharedKernel.Execution.Tenancy;
using SharedKernel.Execution.Context;
using System.Globalization;
using System.Security.Claims;
using SharedKernel.Security.Abstractions;

namespace SharedKernel.Testing.Security;

/// <summary>
/// Builds a caller for tests, either as the <see cref="ClaimsPrincipal"/> an OIDC bearer token produces or as a
/// <see cref="FakeUserContext"/>.
/// </summary>
/// <remarks>
/// <see cref="Build"/> uses the short claim names from <see cref="SecurityClaimTypes"/> and the <c>Bearer</c>
/// authentication type, matching what <c>SharedKernel.Security.Oidc</c> produces with its default claim settings.
/// </remarks>
public sealed class SecurityTestContextBuilder
{
    private const string AuthenticationType = "Bearer";

    private readonly List<string> _roles = [];
    private readonly List<string> _permissions = [];
    private readonly List<string> _authenticationMethods = [];
    private readonly List<(string Method, DateTimeOffset VerifiedAt, Claim Claim)> _authenticationMethodTimes = [];
    private readonly List<KeyValuePair<string, string>> _additionalClaims = [];

    private string _subjectId = FakeUserContext.DefaultSubjectId;
    private string? _clientId;
    private TenantId? _tenantId;
    private string? _sessionId;
    private string? _name;
    private string? _email;
    private ActorKind _identityKind = ActorKind.User;
    private string? _authContextClassReference;
    private DateTimeOffset? _authTime;

    /// <summary>Sets the subject id. Defaults to <see cref="FakeUserContext.DefaultSubjectId"/>.</summary>
    /// <param name="subjectId">The subject id.</param>
    /// <returns>The same builder.</returns>
    public SecurityTestContextBuilder WithSubjectId(string subjectId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subjectId);
        _subjectId = subjectId;
        return this;
    }

    /// <summary>Sets the client id (<c>azp</c>).</summary>
    /// <param name="clientId">The client id, or <see langword="null"/>.</param>
    /// <returns>The same builder.</returns>
    public SecurityTestContextBuilder WithClientId(string? clientId)
    {
        _clientId = clientId;
        return this;
    }

    /// <summary>Sets the tenant (<c>tenant_id</c>).</summary>
    /// <param name="tenantId">The tenant id, or <see langword="null"/>.</param>
    /// <returns>The same builder.</returns>
    public SecurityTestContextBuilder WithTenantId(TenantId? tenantId)
    {
        _tenantId = tenantId;
        return this;
    }

    /// <summary>Sets the session id (<c>sid</c>).</summary>
    /// <param name="sessionId">The session id, or <see langword="null"/>.</param>
    /// <returns>The same builder.</returns>
    public SecurityTestContextBuilder WithSessionId(string? sessionId)
    {
        _sessionId = sessionId;
        return this;
    }

    /// <summary>Sets the display name (<c>name</c>).</summary>
    /// <param name="name">The name, or <see langword="null"/>.</param>
    /// <returns>The same builder.</returns>
    public SecurityTestContextBuilder WithName(string? name)
    {
        _name = name;
        return this;
    }

    /// <summary>Sets the email address (<c>email</c>).</summary>
    /// <param name="email">The email address, or <see langword="null"/>.</param>
    /// <returns>The same builder.</returns>
    public SecurityTestContextBuilder WithEmail(string? email)
    {
        _email = email;
        return this;
    }

    /// <summary>Replaces the roles (<c>roles</c>).</summary>
    /// <param name="roles">The roles.</param>
    /// <returns>The same builder.</returns>
    public SecurityTestContextBuilder WithRoles(params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        _roles.Clear();
        _roles.AddRange(roles);
        return this;
    }

    /// <summary>Replaces the permissions (<c>scope</c>).</summary>
    /// <param name="permissions">The permissions.</param>
    /// <returns>The same builder.</returns>
    public SecurityTestContextBuilder WithPermissions(params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        _permissions.Clear();
        _permissions.AddRange(permissions);
        return this;
    }

    /// <summary>Replaces the authentication methods (<c>amr</c>).</summary>
    /// <param name="authenticationMethods">The methods, such as <c>pwd</c> and <c>otp</c>.</param>
    /// <returns>The same builder.</returns>
    public SecurityTestContextBuilder WithAuthenticationMethods(params string[] authenticationMethods)
    {
        ArgumentNullException.ThrowIfNull(authenticationMethods);
        _authenticationMethods.Clear();
        _authenticationMethods.AddRange(authenticationMethods);
        return this;
    }

    /// <summary>
    /// Records when an authentication method was verified (<c>amr_time</c>), as a step-up does next to its <c>amr</c>
    /// value. Replaces an earlier time for the same method.
    /// </summary>
    /// <param name="method">The authentication method reference, such as <c>otp</c>.</param>
    /// <param name="verifiedAt">
    /// When it was verified; take it from the test's clock. <see cref="Build"/> records whole seconds, rounded down, as
    /// the claim does; <see cref="BuildUserContext"/> keeps the exact value.
    /// </param>
    /// <returns>The same builder.</returns>
    /// <remarks>
    /// Only dates the method: list it with <see cref="WithAuthenticationMethods"/> too, or the mappers ignore the time.
    /// The claim is written by <see cref="AuthenticationMethodTimeClaim.Create"/>, the helper every platform writer uses.
    /// </remarks>
    /// <exception cref="ArgumentException"><paramref name="method"/> is null, empty or white space.</exception>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="verifiedAt"/> is before the Unix epoch.</exception>
    public SecurityTestContextBuilder WithAuthenticationMethodTime(string method, DateTimeOffset verifiedAt)
    {
        var claim = AuthenticationMethodTimeClaim.Create(method, verifiedAt);
        var entry = (method, verifiedAt, claim);

        int index = _authenticationMethodTimes.FindIndex(existing => string.Equals(existing.Method, method, StringComparison.Ordinal));
        if (index >= 0)
        {
            _authenticationMethodTimes[index] = entry;
        }
        else
        {
            _authenticationMethodTimes.Add(entry);
        }

        return this;
    }

    /// <summary>Sets the authentication context class (<c>acr</c>).</summary>
    /// <param name="authContextClassReference">The value, or <see langword="null"/>.</param>
    /// <returns>The same builder.</returns>
    public SecurityTestContextBuilder WithAuthContextClassReference(string? authContextClassReference)
    {
        _authContextClassReference = authContextClassReference;
        return this;
    }

    /// <summary>Sets when the user authenticated (<c>auth_time</c>).</summary>
    /// <param name="authTime">The time, or <see langword="null"/>.</param>
    /// <returns>The same builder.</returns>
    public SecurityTestContextBuilder WithAuthTime(DateTimeOffset? authTime)
    {
        _authTime = authTime;
        return this;
    }

    /// <summary>Sets the identity kind. Defaults to <see cref="ActorKind.User"/>.</summary>
    /// <param name="actorKind">The identity kind.</param>
    /// <returns>The same builder.</returns>
    public SecurityTestContextBuilder WithActorKind(ActorKind actorKind)
    {
        _identityKind = actorKind;
        return this;
    }

    /// <summary>Makes the caller unauthenticated.</summary>
    /// <returns>The same builder.</returns>
    public SecurityTestContextBuilder Unauthenticated() => WithActorKind(ActorKind.Anonymous);

    /// <summary>Adds a claim.</summary>
    /// <param name="type">The claim type.</param>
    /// <param name="value">The claim value.</param>
    /// <returns>The same builder.</returns>
    public SecurityTestContextBuilder WithClaim(string type, string value)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(value);
        _additionalClaims.Add(new KeyValuePair<string, string>(type, value));
        return this;
    }

    /// <summary>
    /// Builds the principal an OIDC bearer token for this caller produces. A service principal gets
    /// <c>idtyp=app</c>; an unauthenticated caller gets an identity without an authentication type.
    /// </summary>
    /// <returns>The principal.</returns>
    public ClaimsPrincipal Build()
    {
        var claims = new List<Claim> { new(SecurityClaimTypes.Subject, _subjectId) };

        AddIfPresent(claims, SecurityClaimTypes.AuthorizedParty, _clientId);
        AddIfPresent(claims, SecurityClaimTypes.TenantId, _tenantId?.ToString());
        AddIfPresent(claims, SecurityClaimTypes.SessionId, _sessionId);
        AddIfPresent(claims, SecurityClaimTypes.Name, _name);
        AddIfPresent(claims, SecurityClaimTypes.Email, _email);
        AddIfPresent(claims, SecurityClaimTypes.AuthContextClassReference, _authContextClassReference);
        AddIfPresent(claims, SecurityClaimTypes.AuthTime, _authTime?.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture));
        AddIfPresent(claims, SecurityClaimTypes.Scope, _permissions.Count > 0 ? string.Join(' ', _permissions) : null);

        claims.AddRange(_roles.Select(role => new Claim(SecurityClaimTypes.Roles, role)));
        claims.AddRange(_authenticationMethods.Select(method => new Claim(SecurityClaimTypes.AuthenticationMethod, method)));
        claims.AddRange(_authenticationMethodTimes.Select(entry => new Claim(entry.Claim.Type, entry.Claim.Value)));

        if (_identityKind == ActorKind.Service)
        {
            claims.Add(new Claim("idtyp", "app"));
        }

        claims.AddRange(_additionalClaims.Select(claim => new Claim(claim.Key, claim.Value)));

        var identity = new ClaimsIdentity(
            claims,
            _identityKind == ActorKind.Anonymous ? null : AuthenticationType,
            SecurityClaimTypes.Name,
            SecurityClaimTypes.Roles);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Builds a <see cref="FakeUserContext"/> for this caller. <see cref="FakeUserContext.Claims"/> holds the same claims
    /// <see cref="Build"/> emits, so <c>FindClaim</c> behaves as on a mapped token.
    /// </summary>
    /// <returns>The context.</returns>
    public FakeUserContext BuildUserContext()
    {
        bool hasSubject = _identityKind is ActorKind.User or ActorKind.Service;
        return new FakeUserContext
        {
            ActorKind = _identityKind,
            SubjectId = hasSubject ? _subjectId : null,
            ClientId = _clientId,
            TenantId = _tenantId,
            SessionId = _sessionId,
            Name = _name,
            Email = _email,
            Roles = [.. _roles],
            Permissions = [.. _permissions],
            AuthenticationMethods = [.. _authenticationMethods],
            AuthenticationMethodTimes = _authenticationMethodTimes
                .ToDictionary(entry => entry.Method, entry => entry.VerifiedAt, StringComparer.Ordinal)
                .AsReadOnly(),
            AuthContextClassReference = _authContextClassReference,
            AuthTime = _authTime,
            Claims = [.. Build().Claims.Select(claim => new KeyValuePair<string, string>(claim.Type, claim.Value))],
        };
    }

    private static void AddIfPresent(List<Claim> claims, string type, string? value)
    {
        if (value is not null)
        {
            claims.Add(new Claim(type, value));
        }
    }
}
