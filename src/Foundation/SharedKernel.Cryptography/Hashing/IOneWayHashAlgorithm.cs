namespace SharedKernel.Cryptography.Hashing;

/// <summary>
/// One slow, salted key-derivation algorithm that <see cref="OneWayHasher"/> can hash with and verify against.
/// </summary>
/// <remarks>
/// <para>
/// Register each implementation as an <see cref="IOneWayHashAlgorithm"/> service. <see cref="OneWayHasher"/> hashes
/// with the algorithm named by <see cref="Options.OneWayHashingOptions.Algorithm"/> and verifies with whichever
/// registered algorithm's <see cref="AlgorithmId"/> matches the stored hash, so changing the configured algorithm
/// upgrades stored hashes as users sign in.
/// </para>
/// <para>
/// Implementations receive the secret already UTF-8 encoded, normalized and, when a pepper is configured, keyed with
/// it. They never see the <c>k</c> parameter, which <see cref="OneWayHasher"/> reserves for the pepper id.
/// </para>
/// </remarks>
public interface IOneWayHashAlgorithm
{
    /// <summary>The PHC algorithm identifier this implementation writes and reads, for example <c>argon2id</c>.</summary>
    string AlgorithmId { get; }

    /// <summary>Hashes <paramref name="secret"/> with a fresh random salt and the currently configured cost.</summary>
    /// <param name="secret">The encoded secret.</param>
    /// <returns>The hash, with every parameter needed to verify it later.</returns>
    PhcHashString Hash(ReadOnlySpan<byte> secret);

    /// <summary>
    /// Verifies <paramref name="secret"/> against <paramref name="hash"/> using the parameters stored in the hash.
    /// </summary>
    /// <param name="hash">A hash whose <see cref="PhcHashString.AlgorithmId"/> equals <see cref="AlgorithmId"/>.</param>
    /// <param name="secret">The encoded secret.</param>
    /// <returns>
    /// <see langword="true"/> when the secret matches. <see langword="false"/> when it does not, or when the stored
    /// parameters are malformed or outside the bounds this implementation accepts. Stored hashes can be written by
    /// an attacker, so implementations must reject costs high enough to exhaust CPU or memory before deriving.
    /// </returns>
    bool Verify(PhcHashString hash, ReadOnlySpan<byte> secret);

    /// <summary>Returns whether <paramref name="hash"/> was produced with parameters other than the current configuration.</summary>
    /// <param name="hash">A hash that <see cref="Verify"/> accepted.</param>
    /// <returns><see langword="true"/> when the caller should hash the secret again and store the new hash.</returns>
    bool RequiresRehash(PhcHashString hash);
}
