namespace SharedKernel.Security.Abstractions;

/// <summary>
/// Short claim type names from JWT (RFC 7519), OIDC Core and the OAuth specifications, as they appear on the
/// wire. The authentication packages disable ASP.NET Core's renaming of inbound claims, so these are the names
/// found on the <c>ClaimsPrincipal</c>.
/// </summary>
public static class SecurityClaimTypes
{
    /// <summary>The subject identifier: <c>sub</c>.</summary>
    public const string Subject = "sub";

    /// <summary>The display name: <c>name</c>.</summary>
    public const string Name = "name";

    /// <summary>The email address: <c>email</c>.</summary>
    public const string Email = "email";

    /// <summary>The roles, one claim per role or a JSON array: <c>roles</c>.</summary>
    public const string Roles = "roles";

    /// <summary>The OAuth scopes, space-delimited (RFC 8693): <c>scope</c>.</summary>
    public const string Scope = "scope";

    /// <summary>The authentication methods (RFC 8176): <c>amr</c>.</summary>
    public const string AuthenticationMethod = "amr";

    /// <summary>The authentication context class: <c>acr</c>.</summary>
    public const string AuthContextClassReference = "acr";

    /// <summary>When the user authenticated, in seconds since the Unix epoch: <c>auth_time</c>.</summary>
    public const string AuthTime = "auth_time";

    /// <summary>The sign-in session identifier: <c>sid</c>.</summary>
    public const string SessionId = "sid";

    /// <summary>The token identifier: <c>jti</c>.</summary>
    public const string TokenId = "jti";

    /// <summary>The OAuth client identifier (RFC 8693): <c>client_id</c>.</summary>
    public const string ClientId = "client_id";

    /// <summary>The party the token was issued to: <c>azp</c>.</summary>
    public const string AuthorizedParty = "azp";

    /// <summary>The key confirmation of a sender-constrained token (RFC 7800): <c>cnf</c>.</summary>
    public const string Confirmation = "cnf";

    /// <summary>The tenant identifier used by SharedKernel services: <c>tenant_id</c>.</summary>
    public const string TenantId = "tenant_id";

    /// <summary>
    /// When an authentication method was verified, used by SharedKernel services: <c>amr_time</c>. One claim per
    /// method, valued <c>{method} {seconds since the Unix epoch}</c>, such as <c>otp 1790000000</c>.
    /// </summary>
    /// <remarks>
    /// Not a registered JWT claim. A step-up that adds a method after sign-in, such as the TOTP step-up, adds this claim
    /// next to the <c>amr</c> value it dates, so <see cref="IUserContext.GetAuthenticationMethodTime"/> can tell how old
    /// the method is. Write and read it with <see cref="AuthenticationMethodTimeClaim"/>. Like every claim the platform
    /// trusts, an identity provider must not let users set it.
    /// </remarks>
    public const string AuthenticationMethodTime = "amr_time";
}
