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
    /// The minimum permitted value of <see cref="Pbkdf2Iterations"/> (P-512/WO-083). Chosen well
    /// below this package's own 600,000 OWASP-2023+ default — so the floor never becomes the
    /// practical ceiling for a correctly-configured service — and well above the previous
    /// effective floor of <c>1</c>, which defeated the entire point of a deliberately slow key
    /// derivation function. Enforced entirely through the standard <see cref="RangeAttribute"/> /
    /// <c>AddValidatedOptions</c> / <c>ValidateOnStart()</c> startup-validation path this options
    /// type was already wired into — no new <c>IValidateOptions{T}</c> implementation. This floor
    /// applies only to newly <em>configured</em> values validated at startup — it is never
    /// retroactively enforced against an already-stored hash's own embedded iteration count at
    /// <see cref="Hashing.Pbkdf2OneWayHasher.Verify"/> time; a hash legitimately produced under an
    /// older, lower-than-this configuration continues to verify indefinitely. See
    /// <see cref="Hashing.Pbkdf2OneWayHasher.MaxVerifiableIterations"/> for the separate,
    /// independent verify-time ceiling that protects against an attacker-supplied hash instead.
    /// </summary>
    public const int MinimumPbkdf2Iterations = 100_000;

    /// <summary>
    /// The number of PBKDF2-HMACSHA256 iterations used by <see cref="Hashing.Pbkdf2OneWayHasher"/>
    /// when hashing new secrets. Defaults to 600,000 (OWASP 2023+ guidance). Raising this value
    /// does not invalidate already-stored hashes — the iteration count used at hash time is
    /// embedded in the stored hash string itself. Must be at least
    /// <see cref="MinimumPbkdf2Iterations"/> — a value below that floor fails startup validation.
    /// </summary>
    [Range(MinimumPbkdf2Iterations, int.MaxValue)]
    public int Pbkdf2Iterations { get; set; } = 600_000;

    /// <summary>
    /// The default key identifier used by signing operations when the caller does not supply
    /// one explicitly. Optional — services that always pass an explicit <c>keyId</c> need not
    /// set this.
    /// </summary>
    public string? DefaultSigningKeyId { get; set; }
}
