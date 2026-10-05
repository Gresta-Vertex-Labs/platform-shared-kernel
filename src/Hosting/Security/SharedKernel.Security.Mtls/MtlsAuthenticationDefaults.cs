namespace SharedKernel.Security.Mtls;

/// <summary>Names used by the client certificate authentication scheme.</summary>
public static class MtlsAuthenticationDefaults
{
    /// <summary>The authentication scheme name, also the identity's authentication type: <c>Certificate</c>.</summary>
    public const string AuthenticationScheme = "Certificate";

    /// <summary>
    /// The claim carrying the Base64url SHA-256 thumbprint of the client certificate, as used by RFC 8705:
    /// <c>x5t#S256</c>.
    /// </summary>
    public const string CertificateThumbprintClaimType = "x5t#S256";
}
