using System.Security.Cryptography;
using Azure.Security.KeyVault.Keys.Cryptography;
// AsymmetricAlgorithm (ECDsa's base class) declares an unrelated instance `string SignatureAlgorithm`
// property, which shadows the Azure.Security.KeyVault.Keys.Cryptography.SignatureAlgorithm TYPE
// name for unqualified identifier resolution inside a class derived from ECDsa. An alias
// sidesteps the ambiguity instead of fully qualifying every reference.
using AzureSignatureAlgorithm = Azure.Security.KeyVault.Keys.Cryptography.SignatureAlgorithm;

namespace SharedKernel.Cryptography.KeyVault.Azure;

/// <summary>
/// A thin <see cref="ECDsa"/> subclass returned by <see cref="AzureKeyVaultAsymmetricKeyProvider.GetEcdsaKeyAsync"/>
/// whose sign/verify operations are performed <b>remotely</b> by Azure Key Vault — this instance
/// never holds private key material.
/// </summary>
/// <remarks>
/// <para>
/// <b>OVERRIDES <c>SignHash</c>/<c>VerifyHash</c>, NOT <c>SignData</c>/<c>VerifyData</c>.</b>
/// <see cref="ECDsa.SignData(byte[], HashAlgorithmName)"/> and
/// <see cref="ECDsa.VerifyData(byte[], byte[], HashAlgorithmName)"/> are <b>non-virtual</b>
/// convenience methods that hash the input locally and then call <see cref="SignHash"/>/
/// <see cref="VerifyHash"/> internally — the real overridable BCL extension points. Attempting
/// <c>override</c> on <c>SignData</c>/<c>VerifyData</c> directly fails to compile (CS0506); this
/// was discovered during P-493's own test-double implementation (see
/// <c>SharedKernel.Cryptography.Tests/Signing/DisposeGuardedKeys.cs</c>). Because
/// <see cref="Signing.EcdsaSignatureService"/> calls the convenience methods, and those internally
/// route to <see cref="SignHash"/>/<see cref="VerifyHash"/> below, this class is consumed through
/// the exact same public <see cref="ECDsa"/> contract as any local key — <b>zero changes to
/// <see cref="Signing.EcdsaSignatureService"/> were needed beyond what P-493 already introduced</b>.
/// </para>
/// <para>
/// <b>NARROW, FIXED ALGORITHM MAPPING — NOT A GENERAL-PURPOSE NEGOTIATION SURFACE.</b> Unlike
/// <see cref="RSA.SignHash(byte[], HashAlgorithmName, RSASignaturePadding)"/>,
/// <see cref="ECDsa.SignHash(byte[])"/> carries no hash-algorithm parameter at all — the caller
/// (<see cref="ECDsa.SignData(byte[], HashAlgorithmName)"/>) hashes locally and hands over only
/// the raw digest bytes. This class therefore maps every call unconditionally to Azure's
/// <see cref="AzureSignatureAlgorithm.ES256"/> — the fixed choice <see cref="Signing.EcdsaSignatureService"/>
/// always uses (SHA-256 on the P-256 curve) — and defends against a wrong digest length (anything
/// other than the 32-byte SHA-256 digest size) by throwing <see cref="NotSupportedException"/>
/// rather than silently forwarding a mismatched digest to Azure.
/// </para>
/// <para>
/// <b>SIGNING AND VERIFICATION PERFORM A REAL, UNAVOIDABLE BLOCKING NETWORK CALL TO AZURE KEY
/// VAULT.</b> Azure's SDK offers a genuine <em>synchronous</em>
/// <see cref="CryptographyClient.Sign(AzureSignatureAlgorithm, byte[], System.Threading.CancellationToken)"/>/
/// <see cref="CryptographyClient.Verify(AzureSignatureAlgorithm, byte[], byte[], System.Threading.CancellationToken)"/>
/// pair — this is not a <c>.GetAwaiter().GetResult()</c> bridge over the async members, since
/// <see cref="ECDsa"/>'s BCL surface gives this class no async entry point to bridge from in the
/// first place (<see cref="Signing.IAsymmetricSignatureService.SignAsync"/>'s own P-493 remarks
/// state this limitation explicitly: only <em>key resolution</em> is genuinely asynchronous). The
/// call is still synchronous network I/O either way — it blocks the calling thread for the
/// duration of the round trip to Key Vault, on every single call, with no local fast path.
/// </para>
/// <para>
/// <see cref="ExportParameters"/>/<see cref="ImportParameters"/>/<see cref="GenerateKey"/> all
/// throw <see cref="NotSupportedException"/> — private key material never crosses the process
/// boundary, and this instance is permanently bound to one already-provisioned Azure Key Vault
/// key, mirroring <see cref="AzureKeyVaultEncryptionKeyProvider"/>'s equivalent invariant for the
/// vault's master key material.
/// </para>
/// </remarks>
internal sealed class KeyVaultEcdsaKey : ECDsa
{
    // The SHA-256 digest size in bytes — the only digest length Signing.EcdsaSignatureService
    // ever hands this class, since it always hashes with HashAlgorithmName.SHA256 before calling
    // SignData/VerifyData (which route to SignHash/VerifyHash below).
    private const int ExpectedHashLengthBytes = 32;

    private readonly CryptographyClient _cryptographyClient;
    private readonly int _keySizeBits;

    internal KeyVaultEcdsaKey(CryptographyClient cryptographyClient, int keySizeBits)
    {
        _cryptographyClient = cryptographyClient;
        _keySizeBits = keySizeBits;
    }

    /// <inheritdoc />
    /// <remarks>
    /// Fixed by the underlying Azure Key Vault key at resolution time — the setter throws
    /// <see cref="NotSupportedException"/>, since this instance cannot change the vault key's size.
    /// </remarks>
    public override int KeySize
    {
        get => _keySizeBits;
        set => throw new NotSupportedException(
            $"{nameof(KeyVaultEcdsaKey)}'s key size is fixed by the underlying Azure Key Vault key " +
            "and cannot be changed locally.");
    }

    /// <summary>
    /// Performs a REAL, BLOCKING NETWORK CALL to Azure Key Vault. See the type-level remarks.
    /// </summary>
    public override byte[] SignHash(byte[] hash)
    {
        ArgumentNullException.ThrowIfNull(hash);
        EnsureExpectedHashLength(hash);

        // REAL, BLOCKING NETWORK CALL — see type-level remarks.
        SignResult result = _cryptographyClient.Sign(AzureSignatureAlgorithm.ES256, hash);
        return result.Signature;
    }

    /// <summary>
    /// Performs a REAL, BLOCKING NETWORK CALL to Azure Key Vault. See the type-level remarks.
    /// </summary>
    public override bool VerifyHash(byte[] hash, byte[] signature)
    {
        ArgumentNullException.ThrowIfNull(hash);
        ArgumentNullException.ThrowIfNull(signature);
        EnsureExpectedHashLength(hash);

        // REAL, BLOCKING NETWORK CALL — see type-level remarks.
        VerifyResult result = _cryptographyClient.Verify(AzureSignatureAlgorithm.ES256, hash, signature);
        return result.IsValid;
    }

    /// <summary>
    /// Private key material never crosses the process boundary — always throws.
    /// </summary>
    public override ECParameters ExportParameters(bool includePrivateParameters) =>
        throw new NotSupportedException(
            $"{nameof(KeyVaultEcdsaKey)} never exports key material — the underlying Azure Key " +
            "Vault key's private material never crosses the process boundary, and this class is " +
            "not a general-purpose local ECDsa key.");

    /// <summary>
    /// This instance is bound to a specific, already-provisioned Azure Key Vault key — always
    /// throws.
    /// </summary>
    public override void ImportParameters(ECParameters parameters) =>
        throw new NotSupportedException(
            $"{nameof(KeyVaultEcdsaKey)} cannot import key material — it is permanently bound to " +
            "the Azure Key Vault key it was resolved for.");

    /// <summary>
    /// This instance is bound to a specific, already-provisioned Azure Key Vault key — always
    /// throws.
    /// </summary>
    public override void GenerateKey(ECCurve curve) =>
        throw new NotSupportedException(
            $"{nameof(KeyVaultEcdsaKey)} cannot generate a new key locally — it is permanently " +
            "bound to the Azure Key Vault key it was resolved for.");

    private static void EnsureExpectedHashLength(byte[] hash)
    {
        if (hash.Length != ExpectedHashLengthBytes)
        {
            throw new NotSupportedException(
                $"{nameof(KeyVaultEcdsaKey)} only supports a {ExpectedHashLengthBytes}-byte SHA-256 " +
                $"digest (mapped to Azure's {nameof(AzureSignatureAlgorithm.ES256)} on the P-256 curve) — " +
                $"the fixed combination {nameof(Signing.EcdsaSignatureService)} always requests. " +
                $"Received a {hash.Length}-byte digest.");
        }
    }
}
