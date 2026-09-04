namespace SharedKernel.Cryptography.Totp;

/// <summary>
/// The HMAC hash algorithm used by <see cref="IHotpGenerator"/>/<see cref="ITotpGenerator"/> to
/// derive an HOTP/TOTP code.
/// </summary>
public enum HotpAlgorithm
{
    /// <summary>HMAC-SHA1 — the RFC 4226/RFC 6238 default and the only algorithm every authenticator app is guaranteed to support.</summary>
    Sha1 = 0,

    /// <summary>HMAC-SHA256 — a common, widely-supported opt-in extension beyond the RFC default.</summary>
    Sha256 = 1,

    /// <summary>HMAC-SHA512 — a common, widely-supported opt-in extension beyond the RFC default.</summary>
    Sha512 = 2,
}
