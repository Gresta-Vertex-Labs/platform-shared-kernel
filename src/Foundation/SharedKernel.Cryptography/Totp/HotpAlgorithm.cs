namespace SharedKernel.Cryptography.Totp;

/// <summary>The HMAC algorithm an HOTP or TOTP code is derived with.</summary>
public enum HotpAlgorithm
{
    /// <summary>HMAC-SHA1, the RFC default and the only algorithm every authenticator app supports.</summary>
    Sha1 = 0,

    /// <summary>HMAC-SHA256.</summary>
    Sha256 = 1,

    /// <summary>HMAC-SHA512.</summary>
    Sha512 = 2,
}
