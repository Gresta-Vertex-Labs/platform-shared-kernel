namespace SharedKernel.Security.Oidc;

/// <summary>Names used by the OIDC authentication scheme.</summary>
public static class OidcAuthenticationDefaults
{
    /// <summary>The authentication scheme name, also the identity's authentication type: <c>Bearer</c>.</summary>
    public const string AuthenticationScheme = "Bearer";

    /// <summary>The HTTP authorization scheme and header name for DPoP (RFC 9449): <c>DPoP</c>.</summary>
    public const string DpopScheme = "DPoP";

    /// <summary>The response header carrying a server-issued DPoP nonce: <c>DPoP-Nonce</c>.</summary>
    public const string DpopNonceHeader = "DPoP-Nonce";
}
