using SharedKernel.Security.Abstractions;

namespace SharedKernel.Security.Oidc.Options;

/// <summary>Which access token claims populate <see cref="IUserContext"/>.</summary>
/// <remarks>
/// <para>
/// The defaults follow the OIDC and OAuth specifications and read the claim names as issued; the package turns off
/// ASP.NET Core's inbound claim renaming. For Microsoft Entra ID, set <see cref="TenantClaimType"/> to <c>tid</c>
/// and consider <see cref="SubjectClaimType"/> <c>oid</c>, which is the same for a user across applications.
/// </para>
/// <para>
/// A configured list replaces the defaults rather than adding to them.
/// </para>
/// </remarks>
public sealed class OidcClaimOptions
{
    /// <summary>Gets or sets the claim holding the subject identifier. Defaults to <c>sub</c>.</summary>
    public string SubjectClaimType { get; set; } = SecurityClaimTypes.Subject;

    /// <summary>Gets or sets the claim holding the display name. Defaults to <c>name</c>.</summary>
    public string NameClaimType { get; set; } = SecurityClaimTypes.Name;

    /// <summary>Gets or sets the claim holding the email address. Defaults to <c>email</c>.</summary>
    public string EmailClaimType { get; set; } = SecurityClaimTypes.Email;

    /// <summary>Gets or sets the claim holding roles, one per claim. Defaults to <c>roles</c>.</summary>
    public string RoleClaimType { get; set; } = SecurityClaimTypes.Roles;

    /// <summary>
    /// Gets or sets the claims holding permissions. Each value may list several, separated by spaces. Empty, the
    /// default, reads <c>scope</c> and <c>scp</c> (Microsoft Entra ID).
    /// </summary>
    public List<string> PermissionClaimTypes { get; set; } = [];

    /// <summary>
    /// Gets or sets the claims holding the client id, first match wins. Empty, the default, reads <c>azp</c>,
    /// <c>client_id</c> and <c>appid</c> (Microsoft Entra ID v1 tokens).
    /// </summary>
    public List<string> ClientIdClaimTypes { get; set; } = [];

    /// <summary>Gets or sets the claim holding the tenant id as a GUID. Defaults to <c>tenant_id</c>.</summary>
    public string TenantClaimType { get; set; } = SecurityClaimTypes.TenantId;

    /// <summary>
    /// Gets or sets the claims holding the session id, first match wins. Empty, the default, reads <c>sid</c>, then
    /// the token ids <c>jti</c> and <c>uti</c> (Microsoft Entra ID), which bind the session to one token when the
    /// provider issues no session id.
    /// </summary>
    public List<string> SessionIdClaimTypes { get; set; } = [];

    /// <summary>Gets or sets the claim holding authentication methods. Defaults to <c>amr</c>.</summary>
    public string AuthenticationMethodClaimType { get; set; } = SecurityClaimTypes.AuthenticationMethod;

    /// <summary>Gets or sets the claim holding the authentication context class. Defaults to <c>acr</c>.</summary>
    public string AuthContextClassReferenceClaimType { get; set; } = SecurityClaimTypes.AuthContextClassReference;

    /// <summary>Gets or sets the claim holding the authentication time. Defaults to <c>auth_time</c>.</summary>
    public string AuthTimeClaimType { get; set; } = SecurityClaimTypes.AuthTime;

    /// <summary>
    /// Gets or sets claim values that mark an application-only token (no user), matched by type and value. Empty,
    /// the default, matches <c>idtyp=app</c> (Microsoft Entra ID) and <c>gty=client-credentials</c> (Auth0).
    /// </summary>
    /// <remarks>
    /// A token is also treated as application-only when its subject equals its client id (Okta, Duende) or when it
    /// has a client id but no subject. Providers whose client-credentials tokens carry a separate subject, such as a
    /// Keycloak service-account user id, need an entry here.
    /// </remarks>
    public Dictionary<string, string> ApplicationTokenClaims { get; set; } = new(StringComparer.Ordinal);
}
