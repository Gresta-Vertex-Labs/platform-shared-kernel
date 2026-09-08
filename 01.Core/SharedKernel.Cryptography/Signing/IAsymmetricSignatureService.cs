namespace SharedKernel.Cryptography.Signing;

/// <summary>
/// Signs and verifies data using an asymmetric algorithm (RSA or ECDSA).
/// </summary>
/// <remarks>
/// <b>A GENUINE BCL LIMITATION, RECORDED HERE RATHER THAN GLOSSED OVER (P-493/WO-081):</b>
/// <see cref="System.Security.Cryptography.RSA"/>/<see cref="System.Security.Cryptography.ECDsa"/>'s
/// <c>SignData</c>/<c>VerifyData</c> have <b>no async overload anywhere in the BCL</b>. So even
/// inside <see cref="SignAsync(byte[], string, CancellationToken)"/>/
/// <see cref="VerifyAsync(byte[], byte[], string, CancellationToken)"/>, once the now-asynchronous
/// key resolution (<see cref="IAsymmetricKeyProvider"/>) completes, the actual cryptographic call
/// is inherently synchronous. For a remote-KMS-backed key (e.g. Azure Key Vault Keys' own remote
/// signing operation) that final call still performs a real blocking network round trip
/// internally, on the calling thread. This phase makes <b>key resolution</b> asynchronous — never
/// the signing primitive itself, which the BCL simply does not offer an async form of.
/// </remarks>
public interface IAsymmetricSignatureService
{
    /// <summary>
    /// Signs <paramref name="data"/> using the private key identified by <paramref name="keyId"/>.
    /// </summary>
    /// <param name="data">The data to sign.</param>
    /// <param name="keyId">The identifier of the key pair to sign with, resolved by an <see cref="IAsymmetricKeyProvider"/> implemented by the consuming service.</param>
    /// <returns>The raw signature bytes.</returns>
    /// <exception cref="NotSupportedException">
    /// The registered <see cref="IAsymmetricKeyProvider"/> was not confirmed genuinely
    /// synchronous — via <see cref="ISynchronousAsymmetricKeyProvider"/> — at construction time.
    /// See <see cref="AsymmetricKeyProviderCapabilities.IsGenuinelySynchronous(IAsymmetricKeyProvider)"/>.
    /// Call <see cref="SignAsync(byte[], string, CancellationToken)"/> instead.
    /// </exception>
    /// <remarks>
    /// <b>(P-493/WO-081)</b> This member is gated behind a one-time, construction-time capability
    /// check, mirroring <c>ISymmetricEncryptionService</c>'s P-492 gating exactly. When the
    /// registered <see cref="IAsymmetricKeyProvider"/> genuinely never blocks (implements
    /// <see cref="ISynchronousAsymmetricKeyProvider"/>), this method bridges onto
    /// <see cref="IAsymmetricKeyProvider.GetRsaKeyAsync(string, CancellationToken)"/>/
    /// <see cref="IAsymmetricKeyProvider.GetEcdsaKeyAsync(string, CancellationToken)"/> via
    /// <c>.GetAwaiter().GetResult()</c>, which observes an already-completed
    /// <see cref="ValueTask{TResult}"/> and is genuinely non-blocking for key resolution. IF THIS
    /// PROVIDER IS UNMARKED, THIS METHOD THROWS <see cref="NotSupportedException"/> IMMEDIATELY,
    /// WITHOUT ATTEMPTING THE BRIDGE — IT NEVER SILENTLY BLOCKS A REAL THREAD ON A NETWORK/IPC
    /// ROUND TRIP. Hot-path/high-throughput callers, and any caller registering a KMS/HSM-backed
    /// provider, should always prefer <see cref="SignAsync(byte[], string, CancellationToken)"/>
    /// instead. See this interface's type-level remarks for the separate BCL limitation that
    /// even the async member cannot fix: the underlying cryptographic sign call itself has no
    /// async BCL overload.
    /// </remarks>
    byte[] Sign(byte[] data, string keyId);

    /// <summary>
    /// Asynchronously signs <paramref name="data"/> using the private key identified by
    /// <paramref name="keyId"/>.
    /// </summary>
    /// <param name="data">The data to sign.</param>
    /// <param name="keyId">The identifier of the key pair to sign with, resolved by an <see cref="IAsymmetricKeyProvider"/> implemented by the consuming service.</param>
    /// <param name="ct">A token to observe while resolving the signing key.</param>
    /// <returns>The raw signature bytes.</returns>
    /// <remarks>
    /// Prefer this overload over <see cref="Sign(byte[], string)"/> on hot paths /
    /// high-throughput call sites — it never blocks a thread while resolving the key, regardless
    /// of whether the registered <see cref="IAsymmetricKeyProvider"/> completes synchronously or
    /// asynchronously. See this interface's type-level remarks: the underlying cryptographic sign
    /// call itself is still inherently synchronous once the key has been resolved — the BCL
    /// offers no async <c>SignData</c> overload.
    /// </remarks>
    ValueTask<byte[]> SignAsync(byte[] data, string keyId, CancellationToken ct = default);

    /// <summary>
    /// Verifies that <paramref name="signature"/> is a valid signature of <paramref name="data"/>
    /// produced by the private key paired with the public key identified by <paramref name="keyId"/>.
    /// </summary>
    /// <param name="data">The data that was signed.</param>
    /// <param name="signature">The signature to verify.</param>
    /// <param name="keyId">The identifier of the key pair to verify against.</param>
    /// <returns><c>true</c> if the signature is valid; otherwise <c>false</c>.</returns>
    /// <exception cref="NotSupportedException">
    /// The registered <see cref="IAsymmetricKeyProvider"/> was not confirmed genuinely
    /// synchronous — via <see cref="ISynchronousAsymmetricKeyProvider"/> — at construction time.
    /// See <see cref="AsymmetricKeyProviderCapabilities.IsGenuinelySynchronous(IAsymmetricKeyProvider)"/>.
    /// Call <see cref="VerifyAsync(byte[], byte[], string, CancellationToken)"/> instead.
    /// </exception>
    /// <remarks>
    /// <b>(P-493/WO-081)</b> Gated behind the same construction-time capability check as
    /// <see cref="Sign(byte[], string)"/> — see its remarks. Prefer
    /// <see cref="VerifyAsync(byte[], byte[], string, CancellationToken)"/> on hot paths, and
    /// always when registering a KMS/HSM-backed provider.
    /// </remarks>
    bool Verify(byte[] data, byte[] signature, string keyId);

    /// <summary>
    /// Asynchronously verifies that <paramref name="signature"/> is a valid signature of
    /// <paramref name="data"/> produced by the private key paired with the public key identified
    /// by <paramref name="keyId"/>.
    /// </summary>
    /// <param name="data">The data that was signed.</param>
    /// <param name="signature">The signature to verify.</param>
    /// <param name="keyId">The identifier of the key pair to verify against.</param>
    /// <param name="ct">A token to observe while resolving the verification key.</param>
    /// <returns><c>true</c> if the signature is valid; otherwise <c>false</c>.</returns>
    /// <remarks>
    /// Prefer this overload over <see cref="Verify(byte[], byte[], string)"/> on hot paths /
    /// high-throughput call sites — it never blocks a thread while resolving the key, regardless
    /// of whether the registered <see cref="IAsymmetricKeyProvider"/> completes synchronously or
    /// asynchronously. See this interface's type-level remarks: the underlying cryptographic
    /// verify call itself is still inherently synchronous once the key has been resolved — the
    /// BCL offers no async <c>VerifyData</c> overload.
    /// </remarks>
    ValueTask<bool> VerifyAsync(byte[] data, byte[] signature, string keyId, CancellationToken ct = default);
}
