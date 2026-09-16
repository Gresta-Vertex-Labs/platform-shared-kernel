namespace SharedKernel.Cryptography.Signing;

/// <summary>An asymmetric signature algorithm, named as in JSON Web Algorithms (RFC 7518).</summary>
/// <remarks>
/// Prefer <see cref="PS256"/> or <see cref="ES256"/>. Use the <c>RS</c> algorithms only where an external system
/// requires RSA PKCS #1 v1.5, such as some JWT validators.
/// </remarks>
public enum SignatureAlgorithm
{
    /// <summary>RSASSA-PSS with SHA-256.</summary>
    PS256 = 1,

    /// <summary>RSASSA-PSS with SHA-384.</summary>
    PS384 = 2,

    /// <summary>RSASSA-PSS with SHA-512.</summary>
    PS512 = 3,

    /// <summary>RSASSA-PKCS1-v1_5 with SHA-256.</summary>
    RS256 = 4,

    /// <summary>RSASSA-PKCS1-v1_5 with SHA-384.</summary>
    RS384 = 5,

    /// <summary>RSASSA-PKCS1-v1_5 with SHA-512.</summary>
    RS512 = 6,

    /// <summary>ECDSA on the P-256 curve with SHA-256.</summary>
    ES256 = 7,

    /// <summary>ECDSA on the P-384 curve with SHA-384.</summary>
    ES384 = 8,

    /// <summary>ECDSA on the P-521 curve with SHA-512.</summary>
    ES512 = 9,
}
