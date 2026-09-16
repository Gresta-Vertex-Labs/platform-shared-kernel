namespace SharedKernel.Cryptography.Hashing;

/// <summary>The outcome of verifying a secret against a stored hash.</summary>
public enum HashVerificationResult
{
    /// <summary>The secret does not match, or the stored hash is malformed or uses an unknown algorithm or pepper.</summary>
    Failed = 0,

    /// <summary>The secret matches and the hash uses the current algorithm, cost and pepper.</summary>
    Success = 1,

    /// <summary>
    /// The secret matches, but the hash uses an older algorithm, cost, pepper or storage format. Hash the secret
    /// again with <see cref="IOneWayHasher.Hash"/> and replace the stored hash.
    /// </summary>
    SuccessRehashNeeded = 2,
}
