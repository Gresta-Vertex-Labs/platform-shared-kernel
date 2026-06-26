namespace SharedKernel.Cryptography.Hashing;

/// <summary>
/// Describes the outcome of verifying a plaintext password against a previously stored hash.
/// </summary>
public enum PasswordVerificationResult
{
    /// <summary>The supplied password does not match the stored hash.</summary>
    Failed,

    /// <summary>
    /// The supplied password matches the stored hash, and the hash was produced with the
    /// current algorithm version and iteration count.
    /// </summary>
    Success,

    /// <summary>
    /// The supplied password matches the stored hash, but the hash was produced with an older
    /// iteration count or algorithm version than the one currently configured. The caller
    /// should re-hash the password with <see cref="IPasswordHasher.Hash(string)"/> and persist
    /// the new value.
    /// </summary>
    SuccessRehashNeeded,
}
