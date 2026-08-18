using System.Globalization;
using System.Security.Claims;
using SharedKernel.Security.Abstractions.Abstractions;

namespace SharedKernel.Testing.Security;

/// <summary>
/// Fluent builder that composes a <see cref="ClaimsPrincipal"/> and/or an <see cref="IUserContext"/>
/// test fixture from one shared set of identity fields.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="Build"/> and <see cref="BuildUserContext"/> are two INDEPENDENT, PARALLEL projections of
/// the same fluent state — mirroring how production's <c>OidcUserContext</c> (parses a
/// <see cref="ClaimsPrincipal"/>) and this package's own <see cref="FakeUserContext"/> (direct property
/// assignment) are already two independent, deliberately-non-derived mechanisms for satisfying
/// <see cref="IUserContext"/>. <see cref="BuildUserContext"/> NEVER constructs a
/// <see cref="ClaimsPrincipal"/> first and parses it back through claim-mapping logic.
/// </para>
/// <para>
/// References <c>SharedKernel.Security.Abstractions</c> only — never
/// <c>SharedKernel.Security.Oidc</c>/<c>.ApiKey</c>/<c>.Mtls</c> (the concrete provider packages),
/// mirroring this folder's established abstraction-only rule. The claim-type literals used by
/// <see cref="Build"/> are private constants local to this builder — mirroring
/// <c>SharedKernel.Security.Oidc</c>'s <c>ClaimMappingOptions</c> defaults ("sub"/"email"/"name"/
/// "roles"/"scope"/"amr"/"acr"/"auth_time") without a reference to that package.
/// </para>
/// <para>
/// SCOPE LOCK: no <c>.WithTenantId(...)</c> method exists — never asked for by this type's own
/// acceptance criteria. SCOPE LOCK: no fluent support for WO-058/P-376's
/// <see cref="IUserContext.IsSenderConstrained"/>/DPoP surface or WO-058/P-377's
/// <c>SharedKernel.Security.Mtls</c> package — neither was named by this type's own acceptance criteria.
/// </para>
/// </remarks>
public sealed class SecurityTestContextBuilder
{
    private const string SubClaimType = "sub";
    private const string EmailClaimType = "email";
    private const string NameClaimType = "name";
    private const string RoleClaimType = "roles";
    private const string PermissionClaimType = "scope";
    private const string AmrClaimType = "amr";
    private const string AcrClaimType = "acr";
    private const string AuthTimeClaimType = "auth_time";
    private const string AuthenticationType = "Bearer";

    private static readonly Guid DefaultUserId = new("11111111-1111-1111-1111-111111111111");

    private readonly List<string> _roles = [];
    private readonly List<string> _permissions = [];
    private readonly List<string> _authenticationMethods = [];
    private readonly List<Claim> _additionalClaims = [];

    private Guid _userId = DefaultUserId;
    private string? _email;
    private string? _username;
    private IdentityKind _identityKind = IdentityKind.User;
    private bool _isAuthenticated = true;
    private string? _authContextClassReference;
    private DateTimeOffset? _authTime;

    /// <summary>Sets the subject (<c>sub</c>) identity. Defaults to a fixed, non-empty test <see cref="Guid"/>.</summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public SecurityTestContextBuilder WithUserId(Guid userId)
    {
        _userId = userId;
        return this;
    }

    /// <summary>Sets the email (<c>email</c>) claim value.</summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public SecurityTestContextBuilder WithEmail(string? email)
    {
        _email = email;
        return this;
    }

    /// <summary>Sets the username (<c>name</c>) claim value.</summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public SecurityTestContextBuilder WithUsername(string? username)
    {
        _username = username;
        return this;
    }

    /// <summary>Replaces the role set with <paramref name="roles"/>.</summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public SecurityTestContextBuilder WithRoles(params string[] roles)
    {
        ArgumentNullException.ThrowIfNull(roles);
        _roles.Clear();
        _roles.AddRange(roles);
        return this;
    }

    /// <summary>Replaces the permission (scope) set with <paramref name="permissions"/>.</summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public SecurityTestContextBuilder WithPermissions(params string[] permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);
        _permissions.Clear();
        _permissions.AddRange(permissions);
        return this;
    }

    /// <summary>Sets the <see cref="IdentityKind"/> represented by this fixture. Defaults to <see cref="IdentityKind.User"/>.</summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public SecurityTestContextBuilder WithIdentityKind(IdentityKind identityKind)
    {
        _identityKind = identityKind;
        return this;
    }

    /// <summary>Appends one additional claim. Additive — never replaces a previously-added claim.</summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public SecurityTestContextBuilder WithClaim(string type, string value)
    {
        ArgumentNullException.ThrowIfNull(type);
        ArgumentNullException.ThrowIfNull(value);
        _additionalClaims.Add(new Claim(type, value));
        return this;
    }

    /// <summary>Appends every entry in <paramref name="claims"/> as an additional claim. Bulk additive.</summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public SecurityTestContextBuilder WithClaims(IReadOnlyDictionary<string, string> claims)
    {
        ArgumentNullException.ThrowIfNull(claims);
        foreach (var (type, value) in claims)
        {
            _additionalClaims.Add(new Claim(type, value));
        }

        return this;
    }

    /// <summary>
    /// Marks this fixture as unauthenticated — the fluent equivalent of <c>12.Security</c>'s own
    /// "omit authenticationType" <see cref="ClaimsIdentity"/> pattern.
    /// </summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public SecurityTestContextBuilder Unauthenticated()
    {
        _isAuthenticated = false;
        return this;
    }

    /// <summary>Sets the OIDC Authentication Method Reference (<c>amr</c>) values.</summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public SecurityTestContextBuilder WithAuthenticationMethods(params string[] authenticationMethods)
    {
        ArgumentNullException.ThrowIfNull(authenticationMethods);
        _authenticationMethods.Clear();
        _authenticationMethods.AddRange(authenticationMethods);
        return this;
    }

    /// <summary>Sets the OIDC Authentication Context Class Reference (<c>acr</c>) claim.</summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public SecurityTestContextBuilder WithAuthContextClassReference(string? authContextClassReference)
    {
        _authContextClassReference = authContextClassReference;
        return this;
    }

    /// <summary>Sets the UTC instant the authentication event actually occurred (the OIDC <c>auth_time</c> claim).</summary>
    /// <returns>This instance, for fluent chaining.</returns>
    public SecurityTestContextBuilder WithAuthTime(DateTimeOffset? authTime)
    {
        _authTime = authTime;
        return this;
    }

    /// <summary>
    /// Assembles a <see cref="ClaimsPrincipal"/> from the fluent state accumulated so far.
    /// </summary>
    /// <remarks>
    /// Roles emit ONE <see cref="Claim"/> per role (never a JSON-array-valued single claim).
    /// Permissions emit ONE space-delimited <c>scope</c> claim. Authentication methods emit ONE
    /// <see cref="Claim"/> per method. <see cref="DateTimeOffset.ToUnixTimeSeconds"/> encodes
    /// <c>auth_time</c> as the OIDC NumericDate string. Every <see cref="WithClaim"/>/
    /// <see cref="WithClaims"/> value is appended verbatim after the standard claims. Wraps into
    /// <c>new ClaimsIdentity(claims, authenticationType: IsAuthenticated ? "Bearer" : null)</c> then
    /// <c>new ClaimsPrincipal(identity)</c> — a non-null authenticationType is exactly what makes
    /// <c>ClaimsPrincipal.Identity.IsAuthenticated</c> return <see langword="true"/>.
    /// </remarks>
    public ClaimsPrincipal Build()
    {
        var claims = new List<Claim> { new(SubClaimType, _userId.ToString()) };

        if (_email is not null)
        {
            claims.Add(new Claim(EmailClaimType, _email));
        }

        if (_username is not null)
        {
            claims.Add(new Claim(NameClaimType, _username));
        }

        foreach (var role in _roles)
        {
            claims.Add(new Claim(RoleClaimType, role));
        }

        if (_permissions.Count > 0)
        {
            claims.Add(new Claim(PermissionClaimType, string.Join(' ', _permissions)));
        }

        foreach (var method in _authenticationMethods)
        {
            claims.Add(new Claim(AmrClaimType, method));
        }

        if (_authContextClassReference is not null)
        {
            claims.Add(new Claim(AcrClaimType, _authContextClassReference));
        }

        if (_authTime is not null)
        {
            claims.Add(new Claim(
                AuthTimeClaimType,
                _authTime.Value.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)));
        }

        claims.AddRange(_additionalClaims);

        var identity = new ClaimsIdentity(claims, authenticationType: _isAuthenticated ? AuthenticationType : null);
        return new ClaimsPrincipal(identity);
    }

    /// <summary>
    /// Projects the fluent state accumulated so far directly onto a new <see cref="FakeUserContext"/>.
    /// </summary>
    /// <remarks>
    /// A DIRECT property-to-property projection of the SAME fluent state <see cref="Build"/> reads —
    /// never by constructing a <see cref="ClaimsPrincipal"/> first and parsing it back. The returned
    /// context's <see cref="IUserContext.Claims"/> dictionary carries only the entries added via
    /// <see cref="WithClaim"/>/<see cref="WithClaims"/> (first value wins for a repeated claim type,
    /// mirroring <see cref="IUserContext.Claims"/>'s own documented resolution rule) — the standard
    /// identity fields (email/username/roles/…) are already surfaced through their own dedicated
    /// <see cref="IUserContext"/> members and are not duplicated into the dictionary.
    /// </remarks>
    public IUserContext BuildUserContext()
    {
        var claims = new Dictionary<string, string>();
        foreach (var claim in _additionalClaims)
        {
            claims.TryAdd(claim.Type, claim.Value);
        }

        return new FakeUserContext
        {
            UserId = _userId,
            Email = _email,
            Username = _username,
            Roles = _roles.ToArray(),
            Permissions = _permissions.ToArray(),
            IdentityKind = _identityKind,
            Claims = claims,
            IsAuthenticated = _isAuthenticated,
            AuthenticationMethods = _authenticationMethods.ToArray(),
            AuthContextClassReference = _authContextClassReference,
            AuthTime = _authTime,
        };
    }
}
