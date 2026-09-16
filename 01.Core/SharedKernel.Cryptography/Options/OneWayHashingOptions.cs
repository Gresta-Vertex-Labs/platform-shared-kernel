using System.ComponentModel.DataAnnotations;
using SharedKernel.Cryptography.Hashing;

namespace SharedKernel.Cryptography.Options;

/// <summary>Settings for <see cref="IOneWayHasher"/>.</summary>
public sealed class OneWayHashingOptions
{
    /// <summary>The shortest accepted pepper: 32 bytes.</summary>
    public const int MinimumPepperBytes = 32;

    /// <summary>
    /// The PHC identifier of the algorithm new hashes use. Defaults to <see cref="Pbkdf2OneWayHashAlgorithm.Id"/>,
    /// which is FIPS 140-3 approved. Set <c>argon2id</c> after registering <c>SharedKernel.Cryptography.Argon2</c>
    /// where FIPS compliance is not required. Hashes from the previous algorithm keep verifying and report
    /// <see cref="HashVerificationResult.SuccessRehashNeeded"/>.
    /// </summary>
    [Required]
    public string Algorithm { get; set; } = Pbkdf2OneWayHashAlgorithm.Id;

    /// <summary>
    /// The id of the pepper new hashes use, or <see langword="null"/> for no pepper. Must name an entry in
    /// <see cref="Peppers"/>.
    /// </summary>
    /// <remarks>
    /// A pepper is a secret key, kept outside the database, that is mixed into every hash with HMAC-SHA256 before
    /// the slow algorithm runs. A stolen hash table cannot be attacked without it. Keep retired peppers in
    /// <see cref="Peppers"/> until no stored hash uses them: removing one makes those hashes fail verification.
    /// </remarks>
    public string? CurrentPepperId { get; set; }

    /// <summary>
    /// Pepper secrets by id, each Base64-encoded and at least <see cref="MinimumPepperBytes"/> bytes. Ids use
    /// letters, digits and hyphens, at most 32 characters. Load these from a secret store, never from a file in
    /// source control.
    /// </summary>
    public Dictionary<string, string> Peppers { get; set; } = new(StringComparer.Ordinal);
}
