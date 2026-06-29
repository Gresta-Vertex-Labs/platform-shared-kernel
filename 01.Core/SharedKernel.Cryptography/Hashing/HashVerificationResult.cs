namespace SharedKernel.Cryptography.Hashing;

/// <summary>
/// Describes the outcome of verifying a plaintext secret against a previously stored hash.
/// </summary>
public enum HashVerificationResult
{
    /// <summary>The supplied secret does not match the stored hash.</summary>
    Failed,

    /// <summary>
    /// The supplied secret matches the stored hash, and the hash was produced with the
    /// current algorithm version and iteration count.
    /// </summary>
    Success,

    /// <summary>
    /// The supplied secret matches the stored hash, but the hash was produced with an older
    /// iteration count or algorithm version than the one currently configured. The caller
    /// should re-hash the secret with <see cref="IOneWayHasher.Hash(string)"/> and persist
    /// the new value.
    /// </summary>
    SuccessRehashNeeded,
}
