using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Cryptography.Options;

/// <summary>
/// Configuration for the cryptographic primitives registered by
/// <see cref="Extensions.CryptographyServiceCollectionExtensions.AddSharedKernelCryptography"/>.
/// </summary>
public sealed class CryptographyOptions
{
    /// <summary>The configuration section name this options class binds to.</summary>
    public const string SectionName = "SharedKernel:Cryptography";

    /// <summary>
    /// The number of PBKDF2-HMACSHA256 iterations used by <see cref="Hashing.Pbkdf2PasswordHasher"/>
    /// when hashing new passwords. Defaults to 600,000 (OWASP 2023+ guidance). Raising this value
    /// does not invalidate already-stored hashes — the iteration count used at hash time is
    /// embedded in the stored hash string itself.
    /// </summary>
    [Range(1, int.MaxValue)]
    public int Pbkdf2Iterations { get; set; } = 600_000;

    /// <summary>
    /// The default key identifier used by signing operations when the caller does not supply
    /// one explicitly. Optional — services that always pass an explicit <c>keyId</c> need not
    /// set this.
    /// </summary>
    public string? DefaultSigningKeyId { get; set; }
}
